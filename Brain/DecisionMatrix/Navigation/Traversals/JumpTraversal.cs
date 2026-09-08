#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// A jump to any standable tile in the jump box that one of the jump profiles lands in when the
/// body's own jump is simulated tick by tick against the shapes; the edge carries the profile
/// (the velocity scale and the start speed) and the flight time. Performing it is making exactly
/// that jump: settling onto the take-off for a standing jump, backing away along the row for the
/// run-up a running jump needs and running in at the profile's own speed, jumping as the take-off
/// is crossed, and steering in the air with the rule the simulation steered with.
/// </summary>
public sealed class JumpTraversal : Traversal
{
    public override MoveKind Kind => MoveKind.Jump;

    /// <summary>The longest flight the planner follows before giving up on a landing.</summary>
    public const int MaxJumpTicks = 120;

    /// <summary>The full jump's own reach, which is the tallest climb the body has today.</summary>
    public override int ClimbTiles => NavGrid.JumpHeightTiles;

    public override IEnumerable<NavEdge> Candidates(NavNode node, BodyPhysics.Pose? here, bool lava)
    {
        Point t = node.Tile;
        // A jump straight up onto the tile above needs a platform to pass through, which Fits
        // allows and a block refuses.
        if (here is not BodyPhysics.Pose fromPose || NavGrid.IsBlock(t.X, t.Y - NavGrid.BodyHeightTiles))
            yield break;
        for (int dx = -NavGrid.JumpGapTiles; dx <= NavGrid.JumpGapTiles; dx++)
        {
            // A jump that lands level or lower is only worth flying from an edge: with the next
            // tile in that direction standable, the walk reaches everything a level jump would at
            // a lower price, and the edge node past the walk offers the gap jump itself. This is
            // what keeps a flat floor from simulating sixteen jumps per node for nothing.
            bool edgeThisWay = dx != 0 && !NavGrid.IsStandable(t.X + Math.Sign(dx), t.Y, lava);
            for (int ny = t.Y - NavGrid.JumpHeightTiles; ny <= t.Y + 2; ny++)
            {
                int nx = t.X + dx;
                if ((dx == 0 && ny >= t.Y) || (ny >= t.Y && !edgeThisWay))
                    continue;
                if (NavGrid.StandAt(nx, ny, lava) is not BodyPhysics.Pose targetPose)
                    continue;
                var target = new Point(nx, ny);
                // The rise in pixels between the two poses' bottoms, rounded up to tiles, which
                // differs from the row difference on a slope or a half block by a whole scale step.
                int rise = (int)Math.Ceiling((fromPose.Bottom - targetPose.Bottom) / 16f);
                // Every profile that could land, lowest arc and fastest start first, and the first
                // that does is the edge.
                foreach ((float scale, float startVx) in JumpProfiles(rise, Math.Sign(dx)))
                {
                    // A running start exists only where the floor behind the take-off is long
                    // enough to build it: the follow harness (2026-09-08) found sixteen blocks
                    // proving a walk-speed jump off a slope at the bottom of a pool with rock
                    // behind it, which the body could only make moving the wrong way.
                    if (startVx != 0f && RunwayPixels(t, -Math.Sign(dx)) < RunwayNeeded(MathF.Abs(startVx) - SpeedSlack))
                        continue;
                    if (BodyPhysics.SimulateJump(NavGrid.World, fromPose, scale, startVx, nx, ny, MaxJumpTicks, out int flight) is not BodyPhysics.Pose landing)
                        continue;
                    var landed = new Point((int)Math.Floor(landing.CentreX / 16f), BodyPhysics.FeetRow(landing.Bottom));
                    if (landed != target)
                        continue;
                    yield return new NavEdge(new NavStep(target, MoveKind.Jump, t, scale, startVx, Ticks: flight), JumpCost(flight), 0, true);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The jumps the body can start with for a rise of so many tiles, in the order the planner
    /// tries them: each velocity scale of the fighter AI's table and the full jump whose apex
    /// clears the rise, lowest first, and for each the start speed at the walk, half of it and
    /// standing. Lowest first because the shortest flight is the cheapest edge and the arc
    /// least likely to meet a ceiling; the table alone over-jumped a four-tile rise onto a
    /// platform above it, and the walk alone hit a three-tile overhang the half-speed arc
    /// clears. A jump straight up has no run-up, so only the standing start is offered.
    /// </summary>
    public static IEnumerable<(float scale, float startVx)> JumpProfiles(int rise, int direction)
    {
        float need = Math.Max(0, rise) * 16f;
        foreach (float scale in JumpScales)
        {
            float apex = BodyPhysics.JumpVelocity * scale;
            apex = apex * apex / (2f * BodyPhysics.Gravity);
            if (apex < need)
                continue;
            if (direction == 0)
            {
                yield return (scale, 0f);
                continue;
            }
            yield return (scale, direction * BodyPhysics.WalkSpeed);
            yield return (scale, direction * BodyPhysics.WalkSpeed * 0.5f);
            yield return (scale, 0f);
        }
    }

    private static readonly float[] JumpScales =
    {
        BodyPhysics.JumpScaleForTiles(2), BodyPhysics.JumpScaleForTiles(3), BodyPhysics.JumpScaleForTiles(4), 1f,
    };

    /// <summary>
    /// A jump costs its flight time in walked tiles plus one, so a jump is taken only where the
    /// walk of the same width does not exist, and a long arc costs more than a short hop.
    /// </summary>
    private static float JumpCost(int ticks) => 1f + ticks * BodyPhysics.WalkSpeed / 16f;

    // The jump edge the run-up state belongs to, and where the run-up stands: backing away from
    // the take-off, already run once (so a second arrival at the take-off jumps whatever the
    // speed, rather than backing away for ever on a runway too short for the profile), and
    // whether the jump itself has been made, which is what turns a landing elsewhere into a fault.
    private (Point From, Point Tile) runUpEdge;
    private bool backingOff;
    private bool ranUp;
    private bool jumped;
    private bool fell;

    /// <summary>Backing away to the runway mark, or running in from it and not yet in the air.</summary>
    public override bool MidMove => backingOff || (ranUp && !jumped);

    public override void Begin(NavStep step)
    {
        // Each attempt at the step starts with the jump not yet made; an airborne body sets it
        // again on its first steer, so a replan that returns the edge mid-flight loses nothing.
        jumped = false;
        fell = false;
        // The run-up state belongs to the edge, not the path index: a replan resets the index,
        // and a new path with a jump at the same index would otherwise inherit another jump's run-up.
        if ((step.From, step.Tile) == runUpEdge)
            return;
        runUpEdge = (step.From, step.Tile);
        backingOff = false;
        ranUp = false;
    }

    public override Controls Steer(BodyState live, NavStep step, NavStep? next)
    {
        Vector2 landing = NavGrid.FeetWorld(step.Tile);
        if (!live.OnGround)
        {
            // A rising body has jumped; a falling one may only have stepped off a kerb, unless
            // it falls faster than a kerb hop ever does, which is a body that left its take-off
            // without jumping and is judged where it lands like a body that jumped.
            if (live.Vy < 0f)
                jumped = true;
            fell |= DropTraversal.Falling(live);
            return new Controls(BodyPhysics.SteerToward(landing.X, live.CentreX, live.Vx));
        }

        Vector2 takeoff = NavGrid.FeetWorld(step.From);
        float need = step.StartVx;
        float vx = live.Vx;
        if (need == 0f)
        {
            // A standing jump: be on the take-off, still, then jump. The first tick of the jump
            // steers as the simulation's first tick did.
            float off = takeoff.X - live.CentreX;
            if (MathF.Abs(off) <= 6f && MathF.Abs(vx) < RestSpeed)
                return Jump(step, landing, live);
            return new Controls(BodyPhysics.SteerToward(takeoff.X, live.CentreX, vx));
        }

        int jd = MathF.Sign(need);
        float along = (live.CentreX - takeoff.X) * jd; // positive once past the take-off toward the landing
        bool fastEnough = vx * jd >= MathF.Abs(need) - SpeedSlack;
        float runway = Runway(step, jd);
        float mark = takeoff.X - jd * runway;
        if (backingOff)
        {
            // Coast onto the runway mark with the jump's own steering rule rather than walking
            // through it: a reversal from the walk speed takes about two tiles to stop, and when
            // the runway is capped by the floor the mark is the last standable tile behind.
            if (MathF.Abs(live.CentreX - mark) <= 6f && MathF.Abs(vx) < RestSpeed)
            {
                backingOff = false;
                ranUp = true;
            }
            else
                return new Controls(BodyPhysics.SteerToward(mark, live.CentreX, vx));
        }
        if (along >= -2f && (fastEnough || ranUp))
            return Jump(step, landing, live);
        if (along >= -2f)
        {
            // At or past the take-off, too slow, and not run up yet: back away if there is
            // anywhere to. Past the take-off counts too, because a fresh plan can start with the
            // body a few pixels beyond the pose centre and a jump from there at no speed is a
            // strike, while backing away is a move away from the edge.
            if (runway < 12f)
                return Jump(step, landing, live);
            backingOff = true;
            return new Controls(BodyPhysics.SteerToward(mark, live.CentreX, vx));
        }
        // Behind the take-off: run in at the profile's own speed, not the walk speed, because the
        // arc was simulated at that speed and a faster body flies a longer arc into the overhang
        // the profile was chosen to clear.
        return new Controls(jd * MathF.Abs(need));
    }

    /// <summary>The jump tick: the impulse at the step's scale, steering toward the landing from where the body stands, as the simulation's first tick did.</summary>
    private Controls Jump(NavStep step, Vector2 landing, BodyState live)
    {
        jumped = true;
        return new Controls(BodyPhysics.SteerToward(landing.X, live.CentreX, live.Vx), Jump: true, JumpScale: step.JumpScale);
    }

    /// <summary>
    /// Landed: standing within the slack of the landing's feet point, or covering the landing
    /// tile, because a jump is one flight and a body that has come down with the tile under it
    /// is where the flight ends; the next step's steering absorbs the rest. A body judged
    /// neither landed nor mislanded walked back to its take-off and flew again for ever.
    /// </summary>
    public override bool Done(BodyState live, NavStep step, NavStep? next)
        => base.Done(live, step, next) || ((jumped || fell) && live.Covers(step.Tile));

    public override TraversalFault Check(BodyState live, NavStep step, int ticksOnStep)
    {
        if ((jumped || fell) && LandedElsewhere(live, step))
            return TraversalFault.Misland;
        return base.Check(live, step, ticksOnStep);
    }

    /// <summary>
    /// A jump's proven ticks are its flight and nothing else, so the allowance has to add the
    /// preparation the follower does before it: a running profile backs away to its mark and runs
    /// in, and the motor needs the speed divided by its acceleration to reach that speed, once
    /// each way. Without this a full-speed jump whose back-off and run-in cost eighty ticks timed
    /// out against an allowance sized for a twelve-tick flight, which is what the cadence hold
    /// (MidMove) made reachable: before it, the replan cut the preparation short instead.
    /// </summary>
    protected override int Allowance(NavStep step)
        => base.Allowance(step) + (step.StartVx == 0f ? 0 : (int)(2f * MathF.Abs(step.StartVx) / BodyPhysics.Acceleration));

    /// <summary>
    /// How far behind a jump's take-off the body can back up along the take-off row, in pixels:
    /// the distance the motor needs to reach the profile's speed from rest, plus a tile to turn
    /// in, capped by the standable tiles actually there.
    /// </summary>
    private static float Runway(NavStep step, int direction)
        => MathF.Min(RunwayNeeded(MathF.Abs(step.StartVx)) + 16f, RunwayPixels(step.From, -direction));

    /// <summary>How far under the profile's speed a body may cross the take-off and still make the jump; the planner proves the runway with the same slack the performer accepts.</summary>
    private const float SpeedSlack = 0.4f;

    /// <summary>
    /// The distance the motor needs to reach a speed from rest, from its own acceleration: v²
    /// over twice the gain per tick. The parameter is a speed <em>magnitude</em> and never a
    /// signed velocity, which is why it is named one: the clamp is there because a caller
    /// subtracts <see cref="SpeedSlack"/> first and that can go below zero, and a signed
    /// velocity handed in instead reads every leftward jump as needing no runway at all
    /// (a −3.5 profile asked for 16 px where its mirror asked for 81.6, Codex review of 7525a1b).
    /// </summary>
    private static float RunwayNeeded(float magnitude) => MathF.Max(0f, magnitude) * MathF.Max(0f, magnitude) / (2f * BodyPhysics.Acceleration);

    /// <summary>The standable floor behind a take-off along its row, in pixels, up to a few tiles; <paramref name="behind"/> is the direction away from the jump.</summary>
    private static float RunwayPixels(Point takeoff, int behind)
    {
        int tiles = 0;
        while (tiles < 8 && NavGrid.IsStandable(takeoff.X + behind * (tiles + 1), takeoff.Y))
            tiles++;
        return tiles * 16f;
    }
}
