#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Movement;

namespace AICompanion.Companion.Brain.Infrastructure.Aiming;

/// <summary>
/// How a projectile type moves once it is in the air, in the shape the game's own free flight
/// takes: a number of ticks of straight flight, then a constant gravity added to the vertical
/// speed and a drag factor multiplied into the horizontal speed each tick, with the fall capped.
/// The phase counts AI steps, so the first gravity tick is exact rather than an approximate
/// straight-flight duration. Nothing on an item says how its projectile flies, so a motion is
/// either a prior read off a vanilla style or learned from the companion's own shots; both live in
/// <see cref="ProjectileArcs"/>. <see cref="Straight"/> is the guess before anything is known.
/// </summary>
public readonly record struct LearnedMotion(int GravityStartsAtPhase, float Gravity, float HorizontalDrag, float MaxFallSpeed)
{
    /// <summary>
    /// A straight line at launch speed for ever, which is the first guess for a projectile nobody
    /// has watched. The fall cap is the one the game applies to every gravity style it has
    /// (<c>if (velocity.Y > 16f) velocity.Y = 16f</c> in <c>Projectile.AI</c>), kept here so a
    /// learned gravity with no observed plateau still stops accelerating where the game does.
    /// </summary>
    public static readonly LearnedMotion Straight = new(GravityStartsAtPhase: int.MaxValue, Gravity: 0f, HorizontalDrag: 1f, MaxFallSpeed: 16f);

    public bool IsStraight => Gravity == 0f && HorizontalDrag == 1f;
}

/// <summary>
/// What the aimer needs to fly one shot: launch speed, the motion, how long to trace, the box the
/// game will collide, and how far a shot is worth attempting at all. A swung weapon is a one-tick
/// flight whose speed is its reach, so the same sweep of angles that finds an arc finds a swing
/// with terrain in the way.
/// </summary>
public readonly record struct FlightModel(float Speed, LearnedMotion Motion, int MaxFlightTicks, int HitboxSize, float Reach);

/// <summary>The valid flight which a shot, its expected damage and its telemetry all describe.</summary>
public readonly record struct TrajectorySolution(Vector2 LaunchVelocity, Vector2 ExpectedImpact, int ImpactTick);

/// <summary>
/// One projectile motion primitive shared by solve, scoring and final firing validation. It models
/// decompiled vanilla free flight before translating the projectile, the same order the native
/// fixture uses after <c>VanillaAI</c>.
/// </summary>
public static class ProjectileFlight
{
    public static void Advance(ref Vector2 position, ref Vector2 velocity, in FlightModel weapon, ref int phase)
    {
        phase++;
        LearnedMotion motion = weapon.Motion;
        if (phase >= motion.GravityStartsAtPhase)
        {
            float gravity = motion.Gravity;
            float drag = motion.HorizontalDrag;
            var world = Movement.NavGrid.World;
            if (world != null)
            {
                int tx = (int)(position.X / 16f);
                int ty = (int)(position.Y / 16f);
                if (world.InWorld(tx, ty) && (world.Water(tx, ty) || world.Lava(tx, ty)))
                {
                    gravity *= 2.5f;
                    drag *= 0.7f;
                }
            }
            velocity.Y = MathF.Min(velocity.Y + gravity, motion.MaxFallSpeed);
            velocity.X *= drag;
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
    public static Func<bool>? CaptureRequested;
    public static Action<Vector2[], string>? TraceEvaluated;
    private const float AngleStepRadians = MathHelper.Pi / 60f;
    private const float MaxDepressionRadians = MathHelper.Pi / 3f;
    private const float MaxElevationRadians = MathHelper.Pi * .45f;
    private const float CollisionSampleDistance = 4f;

    /// <summary>Returns the first valid arc, flattest first, together with its actual predicted impact.</summary>
    public static bool TrySolve(Vector2 muzzle, NPC target, FlightModel weapon, out TrajectorySolution solution)
        => TrySolve(muzzle, target, weapon, 0, out solution);

    /// <summary>
    /// The same solve against the target as it will be <paramref name="targetTickOffset"/> ticks from now, which is
    /// what a caller asking "will this stand still have a shot once the body has walked to it" needs. The offset is a
    /// tick count rather than a displacement on purpose: the forecast is terrain-constrained, so a slime about to land
    /// on a ledge and a slime about to keep falling differ, and a straight-line displacement equals the forecast only
    /// while the motion is straight. Every interception test inside the trace is then taken at
    /// <c>offset + flight tick</c>, so the flight lead the aimer already applies is preserved on top of the wait.
    /// </summary>
    public static bool TrySolve(Vector2 muzzle, NPC target, FlightModel weapon, int targetTickOffset, out TrajectorySolution solution)
    {
        // Aim at where it will be, not where it is: aiming the sweep from the current centre makes the
        // flattest-first order search the wrong side of the arc for a target that has moved a long way.
        Vector2 aimAt = targetTickOffset > 0 ? PredictObservedMotion.Predict(target, targetTickOffset) : target.Center;
        Vector2 toTarget = aimAt - muzzle;
        float direct = MathF.Atan2(toTarget.Y, toTarget.X);
        for (float offset = 0f; offset <= MaxElevationRadians; offset += AngleStepRadians)
        {
            if (TryAngle(direct - offset, muzzle, target, weapon, targetTickOffset, out solution))
                return true;
            if (offset > 0f && offset <= MaxDepressionRadians && TryAngle(direct + offset, muzzle, target, weapon, targetTickOffset, out solution))
                return true;
        }
        solution = default;
        return false;
    }

    /// <summary>Compatibility query for callers that only need a launch vector, optionally against the target as it
    /// will be after a wait — position selection asks with the trip to the stand, because a stand is chosen now and
    /// stood on a walk later.</summary>
    public static Vector2? Solve(Vector2 muzzle, NPC target, FlightModel weapon, int targetTickOffset = 0)
        => TrySolve(muzzle, target, weapon, targetTickOffset, out TrajectorySolution solution) ? solution.LaunchVelocity : null;

    /// <summary>
    /// Re-validates a concrete launch, including aim noise, against the target and terrain. A
    /// rotation is not allowed to turn a proved shot into an unproved one at the firing boundary.
    /// </summary>
    public static bool TryTrace(Vector2 muzzle, Vector2 launch, NPC target, FlightModel weapon, out TrajectorySolution solution)
        => Trace(muzzle, launch, target, weapon, out solution);

    /// <summary>Counts the hostile bodies a valid projectile trace crosses, in flight order.</summary>
    public static int PathHits(Vector2 muzzle, Vector2 launch, FlightModel weapon, IReadOnlyList<NPC> hostiles, NPC[] into)
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

    private static bool TryAngle(float angle, Vector2 muzzle, NPC target, FlightModel weapon, int targetTickOffset, out TrajectorySolution solution)
        => Trace(muzzle, angle.ToRotationVector2() * weapon.Speed, target, weapon, out solution, targetTickOffset);

    private static bool Trace(Vector2 muzzle, Vector2 launch, NPC target, FlightModel weapon, out TrajectorySolution solution, int targetTickOffset = 0)
    {
        var trace = CaptureRequested?.Invoke() == true ? new List<Vector2> { muzzle } : null;
        Vector2 position = muzzle;
        Vector2 velocity = launch;
        int phase = 0;
        int ignored = 0;
        for (int tick = 1; tick <= weapon.MaxFlightTicks; tick++)
        {
            Vector2 start = position;
            ProjectileFlight.Advance(ref position, ref velocity, weapon, ref phase);
            trace?.Add(position);
            if (!TraceSegment(start, position, weapon, tick + targetTickOffset, null, null, ref ignored, target, out Vector2 impact))
            {
                solution = default;
                if (trace != null) { trace[^1] = impact; TraceEvaluated?.Invoke(trace.ToArray(), "blocked by terrain or world boundary"); }
                return false;
            }
            if (impact != default)
            {
                solution = new TrajectorySolution(launch, impact, tick);
                if (trace != null) { trace[^1] = impact; TraceEvaluated?.Invoke(trace.ToArray(), "target intercepted"); }
                return true;
            }
        }
        solution = default;
        if (trace != null) TraceEvaluated?.Invoke(trace.ToArray(), "target not intercepted within flight limit");
        return false;
    }

    private static bool TraceSegment(Vector2 start, Vector2 end, FlightModel weapon, int tick, IReadOnlyList<NPC>? hostiles, NPC[]? into, ref int found, NPC? stopAtTarget, out Vector2 impact)
    {
        impact = default;
        int samples = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(start, end) / CollisionSampleDistance));
        int half = weapon.HitboxSize / 2;
        for (int step = 1; step <= samples; step++)
        {
            Vector2 position = Vector2.Lerp(start, end, step / (float)samples);
            if (!WorldGen.InWorld((int)(position.X / 16f), (int)(position.Y / 16f), 5)
                || Collision.SolidCollision(position - new Vector2(half), weapon.HitboxSize, weapon.HitboxSize))
            {
                impact = position;
                return false;
            }

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
