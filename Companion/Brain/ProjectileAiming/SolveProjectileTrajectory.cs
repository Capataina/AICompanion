#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.WorldObservation;

namespace AICompanion.Companion.Brain.ProjectileAiming;

/// <summary>
/// The free-flight part of a vanilla projectile's AI. The phase is <c>ai[0]</c> after AI advances
/// it, so the first gravity tick is exact instead of an approximate straight-flight duration.
/// </summary>
public readonly record struct ProjectileMotion(int GravityStartsAtPhase, float Gravity, float HorizontalDrag, float MaxFallSpeed)
{
    public static readonly ProjectileMotion Arrow = new(GravityStartsAtPhase: 15, Gravity: .1f, HorizontalDrag: 1f, MaxFallSpeed: 16f);
    public static readonly ProjectileMotion ThrowingKnife = new(GravityStartsAtPhase: 20, Gravity: .4f, HorizontalDrag: .97f, MaxFallSpeed: 16f);
}

/// <summary>Facts that turn one of the companion's closed weapon kit into a traceable projectile.</summary>
public readonly record struct WeaponProfile(float Speed, ProjectileMotion Motion, int MaxFlightTicks, int HitboxSize, float Reach = 1100f)
{
    // Projectile.SetDefaults gives WoodenArrowFriendly a 10 by 10 box and ThrowingKnife a 12 by 12 box.
    public static readonly WeaponProfile Arrow = new(Speed: 9.6f, Motion: ProjectileMotion.Arrow, MaxFlightTicks: 150, HitboxSize: 10, Reach: 1100f);

    public WeaponProfile WithSpeed(float speed) => this with { Speed = speed };
}

/// <summary>The valid flight which a shot, its expected damage and its telemetry all describe.</summary>
public readonly record struct TrajectorySolution(Vector2 LaunchVelocity, Vector2 ExpectedImpact, int ImpactTick);

/// <summary>
/// One projectile motion primitive shared by solve, scoring and final firing validation. It models
/// decompiled vanilla free flight before translating the projectile, the same order the native
/// fixture uses after <c>VanillaAI</c>.
/// </summary>
public static class ProjectileFlight
{
    public static void Advance(ref Vector2 position, ref Vector2 velocity, in WeaponProfile weapon, ref int phase)
    {
        phase++;
        ProjectileMotion motion = weapon.Motion;
        if (phase >= motion.GravityStartsAtPhase)
        {
            velocity.Y = MathF.Min(velocity.Y + motion.Gravity, motion.MaxFallSpeed);
            velocity.X *= motion.HorizontalDrag;
        }
        position += velocity;
    }
}

/// <summary>
/// Trajectory-aware aiming. Every candidate uses the projectile's own phase, gravity, drag and
/// hitbox. Each translation is swept through Terraria collision so a fast shot cannot prove a
/// clear line by skipping the thin ceiling it will actually strike.
/// </summary>
public static class TrajectoryAimer
{
    private const float AngleStepRadians = MathHelper.Pi / 60f;
    private const float MaxDepressionRadians = MathHelper.Pi / 3f;
    private const float MaxElevationRadians = MathHelper.Pi * .45f;
    private const float CollisionSampleDistance = 4f;

    /// <summary>Returns the first valid arc, flattest first, together with its actual predicted impact.</summary>
    public static bool TrySolve(Vector2 muzzle, NPC target, WeaponProfile weapon, out TrajectorySolution solution)
    {
        Vector2 toTarget = target.Center - muzzle;
        float direct = MathF.Atan2(toTarget.Y, toTarget.X);
        for (float offset = 0f; offset <= MaxElevationRadians; offset += AngleStepRadians)
        {
            if (TryAngle(direct - offset, muzzle, target, weapon, out solution))
                return true;
            if (offset > 0f && offset <= MaxDepressionRadians && TryAngle(direct + offset, muzzle, target, weapon, out solution))
                return true;
        }
        solution = default;
        return false;
    }

    /// <summary>Compatibility query for callers that only need a launch vector.</summary>
    public static Vector2? Solve(Vector2 muzzle, NPC target, WeaponProfile weapon)
        => TrySolve(muzzle, target, weapon, out TrajectorySolution solution) ? solution.LaunchVelocity : null;

    /// <summary>
    /// Re-validates a concrete launch, including aim noise, against the target and terrain. A
    /// rotation is not allowed to turn a proved shot into an unproved one at the firing boundary.
    /// </summary>
    public static bool TryTrace(Vector2 muzzle, Vector2 launch, NPC target, WeaponProfile weapon, out TrajectorySolution solution)
        => Trace(muzzle, launch, target, weapon, out solution);

    /// <summary>Counts the hostile bodies a valid projectile trace crosses, in flight order.</summary>
    public static int PathHits(Vector2 muzzle, Vector2 launch, WeaponProfile weapon, IReadOnlyList<NPC> hostiles, NPC[] into)
    {
        Vector2 position = muzzle;
        Vector2 velocity = launch;
        int phase = 0;
        int found = 0;
        for (int tick = 1; tick <= weapon.MaxFlightTicks && found < into.Length; tick++)
        {
            Vector2 start = position;
            ProjectileFlight.Advance(ref position, ref velocity, weapon, ref phase);
            if (!TraceSegment(start, position, weapon, tick, hostiles, into, ref found, null, out _))
                break;
        }
        return found;
    }

    private static bool TryAngle(float angle, Vector2 muzzle, NPC target, WeaponProfile weapon, out TrajectorySolution solution)
        => Trace(muzzle, angle.ToRotationVector2() * weapon.Speed, target, weapon, out solution);

    private static bool Trace(Vector2 muzzle, Vector2 launch, NPC target, WeaponProfile weapon, out TrajectorySolution solution)
    {
        Vector2 position = muzzle;
        Vector2 velocity = launch;
        int phase = 0;
        int ignored = 0;
        for (int tick = 1; tick <= weapon.MaxFlightTicks; tick++)
        {
            Vector2 start = position;
            ProjectileFlight.Advance(ref position, ref velocity, weapon, ref phase);
            if (!TraceSegment(start, position, weapon, tick, null, null, ref ignored, target, out Vector2 impact))
            {
                solution = default;
                return false;
            }
            if (impact != default)
            {
                solution = new TrajectorySolution(launch, impact, tick);
                return true;
            }
        }
        solution = default;
        return false;
    }

    private static bool TraceSegment(Vector2 start, Vector2 end, WeaponProfile weapon, int tick, IReadOnlyList<NPC>? hostiles, NPC[]? into, ref int found, NPC? stopAtTarget, out Vector2 impact)
    {
        impact = default;
        int samples = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(start, end) / CollisionSampleDistance));
        int half = weapon.HitboxSize / 2;
        for (int step = 1; step <= samples; step++)
        {
            Vector2 position = Vector2.Lerp(start, end, step / (float)samples);
            if (!WorldGen.InWorld((int)(position.X / 16f), (int)(position.Y / 16f), 5)
                || Collision.SolidCollision(position - new Vector2(half), weapon.HitboxSize, weapon.HitboxSize))
                return false;

            Rectangle projectileBox = new((int)position.X - half, (int)position.Y - half, weapon.HitboxSize, weapon.HitboxSize);
            if (stopAtTarget != null && projectileBox.Intersects(PredictedHitbox(stopAtTarget, tick)))
            {
                impact = position;
                return true;
            }
            if (hostiles == null || into == null)
                continue;
            for (int i = 0; i < hostiles.Count && found < into.Length; i++)
            {
                NPC npc = hostiles[i];
                if (npc == null || !npc.active || npc.life <= 0 || Seen(into, found, npc))
                    continue;
                if (projectileBox.Intersects(PredictedHitbox(npc, tick)))
                    into[found++] = npc;
            }
        }
        return true;
    }

    private static Rectangle PredictedHitbox(NPC npc, int tick)
    {
        Rectangle hitbox = npc.Hitbox;
        Vector2 lead = PredictObservedMotion.Predict(npc, tick) - npc.Center;
        hitbox.Offset((int)lead.X, (int)lead.Y);
        return hitbox;
    }

    private static bool Seen(NPC[] into, int count, NPC npc)
    {
        for (int i = 0; i < count; i++)
            if (into[i] == npc)
                return true;
        return false;
    }
}
