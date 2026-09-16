#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;

/// <summary>Constant downward acceleration from an onset update, capped: the shape of every gravity style the game has.</summary>
public readonly record struct GravityTerm(int OnsetUpdate, float Acceleration, float MaxFallSpeed);

/// <summary>Per-update velocity multipliers from an onset. (1, 1) is no drag; the onset is the update the
/// damping starts at, because a thrown knife flies clean for nineteen updates and damps from the twentieth.</summary>
public readonly record struct DragTerm(float Horizontal, float Vertical, int OnsetUpdate);

/// <summary>Thrust or decay from an onset, scaling speed per update within a band.</summary>
public readonly record struct SpeedChangeTerm(int OnsetUpdate, float RatioPerUpdate, float MinSpeed, float MaxSpeed);

/// <summary>What a homing projectile steers toward. Only the nearest eligible body is observed; steering at the aim point fits SteerToAim instead.</summary>
public enum HomingAnchor { NearestNpc }

/// <summary>Blending toward an anchored point from an onset, within a radius, at a speed.</summary>
public readonly record struct HomingTerm(int OnsetUpdate, float Radius, HomingAnchor Anchor, float Blend, float Speed);

/// <summary>Steering toward the aim point from an onset, within a turn cap per update.</summary>
public readonly record struct SteerToAimTerm(int OnsetUpdate, float MaxTurnPerUpdate);

/// <summary>Turning back to the owner at an update, then accelerating toward them within a cap.</summary>
public readonly record struct ReturnToOwnerTerm(int TurnUpdate, float Acceleration, float MaxSpeed);

/// <summary>
/// How one projectile type flies, as fitted terms. One update under a law, in fixed order so fitting and flying
/// agree: speed change, drag, gravity with its cap, homing, steering toward the aim point, return. Updates are
/// zero-based AI steps — the first <c>Advance</c> call is update 0 — and an onset is the first update its term
/// applies to, so the arrow's first gravity at its fifteenth AI step is onset 14.
/// </summary>
public sealed record FlightLaw(int ProjectileType, int Revision, int UpdatesPerTick, int LifetimeUpdates,
    GravityTerm? Gravity, DragTerm Drag, SpeedChangeTerm? SpeedChange, HomingTerm? Homing,
    SteerToAimTerm? SteerToAim, ReturnToOwnerTerm? Return, WallResponse Wall,
    float ResidualPerUpdate, int Evidence, bool Predictable)
{
    /// <summary>
    /// The law before any evidence: today's arc prior in law form — the arrow and thrown-object numbers for those
    /// vanilla styles, straight flight otherwise. <see cref="FitFlightLaws"/> replaces it once a type's own flights
    /// fit, and the vanilla-match rows hold it against <c>VanillaAI</c> tick for tick.
    /// </summary>
    public static FlightLaw Default(int projectileType)
    {
        int updatesPerTick = 1;
        int lifetime = 600;
        GravityTerm? gravity = null;
        DragTerm drag = new(1f, 1f, 0);
        if (ContentSamples.ProjectilesByType.TryGetValue(projectileType, out Projectile? sample) && sample != null)
        {
            updatesPerTick = sample.extraUpdates + 1;
            lifetime = sample.timeLeft;
            if (sample.aiStyle == Terraria.ID.ProjAIStyleID.Arrow && sample.arrow)
                gravity = new GravityTerm(14, 0.1f, 16f);
            else if (sample.aiStyle == Terraria.ID.ProjAIStyleID.ThrownProjectile)
            {
                gravity = new GravityTerm(19, 0.4f, 16f);
                drag = new DragTerm(0.97f, 1f, 19);
            }
        }
        return new FlightLaw(projectileType, 0, updatesPerTick, lifetime, gravity, drag, null, null, null, null,
            WallResponse.Unknown(projectileType), 0f, 0, true);
    }

    /// <summary>
    /// Straight flight with the type's own tick rate and lifetime: what any modded projectile starts as, and what
    /// the unknown-arc row believes before its calibration flights. A fixture seam only; the game reads
    /// <see cref="Default"/> and the fitter's laws.
    /// </summary>
    public static FlightLaw Straight(int projectileType)
    {
        int updatesPerTick = 1;
        int lifetime = 600;
        if (ContentSamples.ProjectilesByType.TryGetValue(projectileType, out Projectile? sample) && sample != null)
        {
            updatesPerTick = sample.extraUpdates + 1;
            lifetime = sample.timeLeft;
        }
        return new FlightLaw(projectileType, 0, updatesPerTick, lifetime, null, new DragTerm(1f, 1f, 0),
            null, null, null, null, WallResponse.Unknown(projectileType), 0f, 0, true);
    }

    /// <summary>
    /// One update of flight under the law: the AI step, then the move. The AI step is its own method so the
    /// fitter's prediction and the simulator's sub-step call it rather than duplicating it; what the fitter fits
    /// is then exactly what the simulator flies. Geometry enters as the offsets the trace sampled — to the
    /// nearest eligible body, to the aim point, to the owner.
    /// </summary>
    public static void Advance(in FlightLaw law, ref Vector2 position, ref Vector2 velocity, ref int update,
        Vector2 toNearestNpc, Vector2 toAimPoint, Vector2 toOwner, bool wet)
    {
        velocity = AIVelocity(in law, velocity, update, toNearestNpc, toAimPoint, toOwner, wet);
        position += velocity;
        update++;
    }

    /// <summary>
    /// The AI step alone: the velocity after one update under the law, without the move. Liquid multiplies
    /// gravity and damps drag the way the game does; the caller reads the tile.
    /// </summary>
    public static Vector2 AIVelocity(in FlightLaw law, Vector2 velocity, int update,
        Vector2 toNearestNpc, Vector2 toAimPoint, Vector2 toOwner, bool wet)
    {
        if (law.SpeedChange is { } thrust && update >= thrust.OnsetUpdate)
        {
            float speed = Math.Clamp(velocity.Length() * thrust.RatioPerUpdate, thrust.MinSpeed, thrust.MaxSpeed);
            velocity = velocity == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(velocity) * speed;
        }
        float gravity = law.Gravity?.Acceleration ?? 0f;
        if (wet)
            gravity *= 2.5f;
        if (update >= law.Drag.OnsetUpdate)
        {
            float dragX = wet ? law.Drag.Horizontal * 0.7f : law.Drag.Horizontal;
            velocity.X *= dragX;
            velocity.Y *= law.Drag.Vertical;
        }
        if (law.Gravity is { } fall && update >= fall.OnsetUpdate)
            velocity.Y = MathF.Min(velocity.Y + gravity, fall.MaxFallSpeed);
        if (law.Homing is { } homing && update >= homing.OnsetUpdate && toNearestNpc != Vector2.Zero
            && toNearestNpc.Length() <= homing.Radius)
        {
            Vector2 want = Vector2.Normalize(toNearestNpc) * homing.Speed;
            velocity = (1f - homing.Blend) * velocity + homing.Blend * want;
        }
        if (law.SteerToAim is { } steer && update >= steer.OnsetUpdate && toAimPoint != Vector2.Zero && velocity != Vector2.Zero)
        {
            float have = velocity.ToRotation();
            float want = toAimPoint.ToRotation();
            float turn = MathHelper.WrapAngle(want - have);
            velocity = velocity.RotatedBy(Math.Clamp(turn, -steer.MaxTurnPerUpdate, steer.MaxTurnPerUpdate));
        }
        if (law.Return is { } ret && update >= ret.TurnUpdate && toOwner != Vector2.Zero)
        {
            velocity += Vector2.Normalize(toOwner) * ret.Acceleration;
            if (velocity.Length() > ret.MaxSpeed)
                velocity = Vector2.Normalize(velocity) * ret.MaxSpeed;
        }
        return velocity;
    }
}
