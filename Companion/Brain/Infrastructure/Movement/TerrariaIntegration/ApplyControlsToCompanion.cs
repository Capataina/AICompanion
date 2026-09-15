#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.CharacterBody;

namespace AICompanion.Companion;

/// <summary>
/// The only component that writes the live NPC's velocity. Each tick it accelerates the orb toward
/// the velocity the brain asked for, runs the circle contact on where that would put the body, and
/// hands the engine the resolved displacement as the NPC's velocity — the engine, with tile
/// collision switched off for this body, does nothing but add it. Liquid is read here too, because
/// the engine reads it inside the collision it no longer runs for the orb.
///
/// <para>Momentum is the motor's own and not the NPC's velocity. The displacement written to the
/// NPC is what contact left after pushing the body out of a wall, and if that were read back as the
/// next tick's velocity a push-out would become a bounce. The motor keeps the post-contact velocity
/// and folds in only what changed the NPC's velocity from outside between two ticks — a knockback,
/// a hit — as the difference between what it applied and what it reads back.</para>
///
/// <para>Speed and acceleration are the player's, read live: the cap is a multiple of the player's
/// maximum run speed after accessories and the acceleration a multiple of his run acceleration,
/// each further scaled by a multiplier on the body that the mastery tree drives. Nothing here lags
/// the player: a companion at the cap overtakes a running player. The motor publishes both to
/// <see cref="OrbPace"/> every tick so the game-free steering brakes and bends against the same
/// numbers it applies.</para>
/// </summary>
public sealed class CompanionMotor
{
    /// <summary>Honey and shimmer are harmless and slow the body: the tick's displacement is
    /// multiplied by these while the circle touches them. Water and lava do not slow it at all,
    /// because the body is meant to leave them and a slowed exit is a longer exposure.</summary>
    public const float HoneyVelocityFactor = 0.25f;
    public const float ShimmerVelocityFactor = 0.375f;

    /// <summary>How fast a downed body sinks to rest; contact stops it on the floor, where the
    /// player can reach it to revive it. A downed orb hovering five tiles up would be unrevivable.</summary>
    private const float DownedSinkSpeed = 2f;

    /// <summary>The direction and pace of the recovery ejection while the phasing body is still inside terrain.</summary>
    private const float RecoveryClearanceSpeed = 3f;

    private readonly CompanionNPC companion;
    private NPC npc => companion.NPC;
    private Vector2 momentum;
    private Vector2 appliedDisplacement;
    private bool appliedOnce;
    private Vector2? previousPosition;
    private Vector2? recoveryLastClearPosition;
    private int waterContactTicks, lavaContactTicks;
    private bool downed;

    public CompanionMotor(CompanionNPC companion) => this.companion = companion;

    /// <summary>The player's maximum run speed after accessories times the orb's speed multiple. <c>accRunSpeed</c>
    /// is the boot-boosted figure and <c>maxRunSpeed</c> the base; the larger is what the player actually reaches.</summary>
    public static float MaxSpeed(float bodyMultiplier)
    {
        Player player = Main.LocalPlayer;
        float run = MathF.Max(player.maxRunSpeed, player.accRunSpeed);
        float speed = run * Weights.OrbSpeedPerRunSpeed * bodyMultiplier;
        return float.IsFinite(speed) && speed > 0f ? speed : Weights.OrbFallbackSpeed;
    }

    /// <summary>The player's run acceleration times the orb's acceleration multiple.</summary>
    public static float Acceleration(float bodyMultiplier)
    {
        float accel = Main.LocalPlayer.runAcceleration * Weights.OrbAccelerationPerRunAcceleration * bodyMultiplier;
        return float.IsFinite(accel) && accel > 0f ? accel : Weights.OrbFallbackAcceleration;
    }

    public float LiveMaxSpeed => MaxSpeed(companion.SpeedMultiplier);
    public float LiveAcceleration => Acceleration(companion.AccelerationMultiplier);

    public int PinnedTicks { get; private set; }
    public string ControlSource { get; private set; } = "uninitialised";
    public long ControlApplications { get; private set; }
    public bool RecoveryFlight { get; private set; }
    /// <summary>What the brain asked for on the last application.</summary>
    public Controls AppliedControls { get; private set; }
    /// <summary>The velocity the brain asked the body to accelerate toward, after the speed cap.</summary>
    public Vector2 DesiredVelocity { get; private set; }
    /// <summary>Which liquid the circle touches, or -1 for none: water 0, lava 1, honey 2, shimmer 3.</summary>
    public int LiquidKind { get; private set; } = -1;
    /// <summary>How many consecutive ticks the body has been touching a liquid that hurts it.</summary>
    public int LiquidContactTicks => Math.Max(waterContactTicks, lavaContactTicks);
    public bool InHurtingLiquid => (LiquidKind == 0 && !companion.ImmuneToWater) || (LiquidKind == 1 && !companion.ImmuneToLava);
    /// <summary>Whether the last contact pass touched a wall, and the wall's normal when it did.</summary>
    public bool TouchedWall { get; private set; }
    public Vector2 WallNormal { get; private set; }
    public bool ClearOfTerrain => !CircleContact.Overlaps(MovementQueries.World, npc.Center);
    /// <summary>The immunities the body carries, as the terrain reading wants them.</summary>
    public LiquidImmunity Immunity => new(companion.ImmuneToWater, companion.ImmuneToLava);

    /// <summary>The orb as the steering and every consumer read it this tick.</summary>
    public OrbState State => new(npc.Center, momentum, LiquidKind, PinnedTicks >= 15);

    /// <summary>
    /// Read what the engine did with the last application before anything acts on this tick: the
    /// body is pinned when it holds a velocity and did not move, which a body the engine only
    /// integrates cannot do on its own. The pace is published here too, so everything the tick
    /// plans against reads the pace this tick will apply.
    /// </summary>
    public void Track()
    {
        Vector2 position = npc.position;
        bool stationary = previousPosition is Vector2 previous && Vector2.DistanceSquared(previous, position) < 0.01f;
        PinnedTicks = stationary && appliedDisplacement.LengthSquared() > 0.25f ? PinnedTicks + 1 : 0;
        previousPosition = position;
        OrbPace.MaxSpeed = LiveMaxSpeed;
        OrbPace.Acceleration = LiveAcceleration;
    }

    /// <summary>Accelerate toward the requested velocity, run contact, read liquid, and hand the engine the move.</summary>
    public void Apply(Controls controls, string source = "navigation")
    {
        AppliedControls = controls;
        Steer(controls.Desired, source);
    }

    /// <summary>The same application from a bare desired velocity, which is what a headless instrument drives.</summary>
    public void Steer(Vector2 desiredVelocity, string source = "navigation")
    {
        ControlApplications++;
        ControlSource = source;
        Vector2 velocity = momentum + ExternalImpulse();
        if (RecoveryFlight)
        {
            if (!ClearOfTerrain)
            {
                // Cancellation inside a wall finishes clearance back towards the last observed
                // clear body: continuous ejection, never travel toward the owner. Stopping here
                // would leave a downed body unrevivable inside the rock.
                Vector2 delta = recoveryLastClearPosition is Vector2 clear && !CircleContact.Overlaps(MovementQueries.World, clear)
                    ? clear - npc.Center : new Vector2(0, -16);
                if (delta.LengthSquared() < 1f) delta = new Vector2(0, -16);
                momentum = Vector2.Normalize(delta) * MathF.Min(RecoveryClearanceSpeed, delta.Length());
                ControlSource = source + "-recovery-clearance";
                Commit(momentum, phasing: true);
                return;
            }
            RecoveryFlight = false;
        }
        float maxSpeed = LiveMaxSpeed, acceleration = LiveAcceleration;
        Vector2 desired = downed ? new Vector2(0f, DownedSinkSpeed) : desiredVelocity;
        if (desired.LengthSquared() > maxSpeed * maxSpeed) desired = Vector2.Normalize(desired) * maxSpeed;
        DesiredVelocity = desired;
        Vector2 change = desired - velocity;
        if (change.LengthSquared() > acceleration * acceleration) change = Vector2.Normalize(change) * acceleration;
        velocity += change;
        if (velocity.LengthSquared() > maxSpeed * maxSpeed) velocity = Vector2.Normalize(velocity) * maxSpeed;
        if (desired.X != 0f) Face(npc.Center.X + desired.X);
        Commit(velocity, phasing: false);
    }

    /// <summary>
    /// The difference between what the motor last handed the engine and what the NPC's velocity
    /// reads now, which is everything that touched it from outside: a knockback, a hit, a fixture
    /// setting it by hand. Zero on the first application, where nothing was handed over yet.
    /// </summary>
    private Vector2 ExternalImpulse()
    {
        if (!appliedOnce) { appliedOnce = true; return npc.velocity; }
        Vector2 external = npc.velocity - appliedDisplacement;
        return external.LengthSquared() < 1e-6f ? Vector2.Zero : external;
    }

    /// <summary>Resolve contact on where the velocity would put the body, then write the move and the liquid facts.</summary>
    private void Commit(Vector2 velocity, bool phasing)
    {
        Vector2 centre = npc.Center;
        Vector2 next = centre + velocity;
        ITileWorld world = MovementQueries.World;
        if (phasing)
        {
            TouchedWall = false;
            WallNormal = Vector2.Zero;
        }
        else
        {
            var contact = CircleContact.Resolve(world, ref next, ref velocity);
            TouchedWall = contact.Touched;
            WallNormal = contact.Normal;
        }
        ReadLiquid(world, next);
        Vector2 displacement = next - centre;
        if (LiquidKind == 2) displacement *= HoneyVelocityFactor;
        else if (LiquidKind == 3) displacement *= ShimmerVelocityFactor;
        momentum = velocity;
        appliedDisplacement = displacement;
        npc.velocity = displacement;
        HurtIfInLiquid();
    }

    /// <summary>
    /// Which liquid the circle touches at <paramref name="centre"/>, written to the NPC's own flags
    /// as well, because the engine only sets them inside the collision it does not run for this body
    /// and everything from hit effects to the senses reads them.
    /// </summary>
    private void ReadLiquid(ITileWorld world, Vector2 centre)
    {
        int kind = -1;
        if (CircleContact.Touches(centre, (x, y) => world.LiquidAmount(x, y) > 0))
        {
            kind = 0;
            // The first wet tile under the circle names the kind; a body straddling two liquids is a
            // corner case the game itself resolves the same way (one flag set at a time).
            int x0 = (int)MathF.Floor((centre.X - CircleContact.Radius) / 16f), x1 = (int)MathF.Floor((centre.X + CircleContact.Radius) / 16f);
            int y0 = (int)MathF.Floor((centre.Y - CircleContact.Radius) / 16f), y1 = (int)MathF.Floor((centre.Y + CircleContact.Radius) / 16f);
            for (int y = y0; y <= y1 && kind == 0; y++)
                for (int x = x0; x <= x1; x++)
                    if (world.LiquidAmount(x, y) > 0 && world.LiquidKind(x, y) != 0) { kind = world.LiquidKind(x, y); break; }
        }
        LiquidKind = kind;
        npc.wet = kind >= 0;
        npc.lavaWet = kind == 1;
        npc.honeyWet = kind == 2;
        npc.shimmerWet = kind == 3;
    }

    /// <summary>
    /// Water and lava hurt on contact: a fixed amount every fixed interval of contact, the pairs
    /// living on the body. A downed body takes nothing, because the game's own strike refuses it
    /// and counting contact ticks against it would deal the damage the moment it got up.
    /// </summary>
    private void HurtIfInLiquid()
    {
        bool water = LiquidKind == 0 && !companion.ImmuneToWater;
        bool lava = LiquidKind == 1 && !companion.ImmuneToLava;
        waterContactTicks = water ? waterContactTicks + 1 : 0;
        lavaContactTicks = lava ? lavaContactTicks + 1 : 0;
        if (downed) return;
        if (water && waterContactTicks % CompanionNPC.WaterHurt.IntervalTicks == 1)
            npc.SimpleStrikeNPC(CompanionNPC.WaterHurt.Damage, 0, noPlayerInteraction: true);
        if (lava && lavaContactTicks % CompanionNPC.LavaHurt.IntervalTicks == 1)
            npc.SimpleStrikeNPC(CompanionNPC.LavaHurt.Damage, 0, noPlayerInteraction: true);
    }

    public void Stop() => Apply(Controls.None, "idle");

    /// <summary>Recovery flight: the phasing straight flight home. Contact is skipped while it runs.</summary>
    public void ApplyRecoveryFlight(Vector2 velocity)
    {
        ControlApplications++;
        if (ClearOfTerrain) recoveryLastClearPosition = npc.Center;
        RecoveryFlight = true;
        AppliedControls = Controls.None;
        ControlSource = "follow-recovery-flight";
        DesiredVelocity = velocity;
        momentum = velocity;
        if (velocity.X != 0f) Face(npc.Center.X + velocity.X);
        Commit(velocity, phasing: true);
    }

    public void NotifyExternalHit() { }

    public void EnterDowned()
    {
        downed = true;
        momentum = RecoveryFlight ? Vector2.Zero : new Vector2(0f, momentum.Y);
        ControlSource = "downed";
    }

    public void LeaveDowned() => downed = false;

    public void Face(float worldX) => npc.direction = npc.spriteDirection = worldX >= npc.Center.X ? 1 : -1;
}
