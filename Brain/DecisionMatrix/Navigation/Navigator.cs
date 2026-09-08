#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// Gets the companion to a world position. Plans with <see cref="AStar"/> when the goal
/// tile changes or the path goes stale, keeps the path between plans, and drives the
/// motor along it: walk toward the next step, jump when the step is a jump or the body
/// is blocked, let gravity handle drops. Falls back to straight-line walking when no
/// path exists, so a missing path degrades to the old behaviour rather than a freeze.
/// </summary>
public sealed class Navigator
{
    public const int PlanBudget = 1500;
    private const int ReplanInterval = 30;
    private const int FailedPlanRetry = 90;
    private const float ArriveDistance = 12f;

    public NavPath? Path { get; private set; }
    public Point? GoalTile { get; private set; }
    public bool LastPlanFailed { get; private set; }
    public int LastExpansions { get; private set; }

    /// <summary>Ticks the body has not moved while the follower had somewhere to go; the scenario capture reads it.</summary>
    public int StuckTicks => stuckTicks;

    /// <summary>Wall-clock of the last search, for the telemetry: the frame cost of the pose grid is otherwise unmeasured.</summary>
    public double LastPlanMs { get; private set; }

    /// <summary>
    /// How many times in a row the body has stood still on a path to the current goal long enough
    /// to replan. One strike prices the step it was stuck on so the next plan goes another way;
    /// the brain reads two as "this spot cannot be reached the way the grid thinks" and asks the
    /// positioner for a different one. Cleared when the goal moves or is reached.
    /// </summary>
    public int StuckStrikes { get; private set; }

    /// <summary>Ticks a stuck step stays priced after the strike that added it.</summary>
    private const int StuckAvoidTicks = 600;

    private const int StuckReplanTicks = 40;

    private int ticksSincePlan = ReplanInterval;
    private int stuckTicks;
    private Vector2 lastPosition;
    private int clock;

    // The steps the body has been stuck on lately, each as the body rectangle at that tile and the
    // tick it stops mattering; merged into the search's avoid list on every plan, priced and not
    // banned, so a detour wins wherever one exists and the direct way is still there when none does.
    private readonly System.Collections.Generic.List<(Rectangle box, int until)> stuckAvoid = new();

    /// <summary>Move toward <paramref name="targetFeet"/> this tick. Returns true when arrived.</summary>
    public bool MoveTo(NPC npc, CompanionMotor motor, Vector2 targetFeet)
    {
        clock++;
        if (Vector2.Distance(npc.Bottom, targetFeet) <= ArriveDistance)
        {
            motor.Stop();
            Path = null;
            StuckStrikes = 0;
            return true;
        }

        Point start = NavGrid.FeetTile(npc.Bottom);
        Point? goal = NavGrid.NearestStandable(NavGrid.FeetTile(targetFeet), 3);
        ticksSincePlan++;

        bool goalMoved = goal != GoalTile;
        if (goalMoved)
            StuckStrikes = 0;
        // A failed plan is not retried every tick: at the full budget that is ~3 ms per tick for
        // as long as the goal stays unreachable. It waits FailedPlanRetry ticks unless the goal moves.
        bool failedRecently = LastPlanFailed && ticksSincePlan < FailedPlanRetry;
        // A finished partial path is a failed plan that has been walked out: it waits like one.
        bool noPath = Path == null || (Path.Partial && Path.Finished);
        bool stuck = !noPath && stuckTicks > StuckReplanTicks;
        bool stale = (noPath && !failedRecently) || (!noPath && (Path!.Finished || ticksSincePlan >= ReplanInterval || stuck));
        // Plan only from the ground: an airborne body has no standable tile under it, and a
        // plan that failed for that reason blocked replanning for the retry wait, during which
        // straight walking hopped every kerb and put the body back in the air for the next try.
        if (goal != null && (goalMoved || stale) && motor.OnGround)
        {
            // A body that stood still on a step long enough to replan has found a step the grid
            // offers and the body cannot take. Replanning alone returned the same path three times
            // over in run 5 (2026-09-08); the step is priced so the next plan goes another way.
            if (stuck && !Path!.Finished)
            {
                Point tile = Path.Current.Tile;
                var box = new Rectangle(tile.X * 16, (tile.Y - NavGrid.BodyHeightTiles + 1) * 16, 16, NavGrid.BodyHeightTiles * 16);
                box.Inflate(4, 4);
                stuckAvoid.Add((box, clock + StuckAvoidTicks));
                StuckStrikes++;
            }
            Plan(start, goal.Value);
        }

        if (Path == null || Path.Finished)
        {
            WalkStraight(npc, motor, targetFeet);
            return false;
        }

        Follow(npc, motor);
        TrackStuck(npc);
        return false;
    }

    private void Plan(Point start, Point goal)
    {
        GoalTile = goal;
        ticksSincePlan = 0;
        stuckTicks = 0;
        Point? from = NavGrid.NearestStandable(start, 2);
        if (from == null)
        {
            Path = null;
            LastPlanFailed = true;
            LastExpansions = 0;
            global::AICompanion.Brain.Debug.BrainTelemetry.DumpPlan(start, goal, from, 0, "no standable tile at the start");
            return;
        }
        // The brain refills the search's avoid list with the enemies every tick before this runs;
        // the stuck steps join it here, for this plan, and leave when their time is up.
        stuckAvoid.RemoveAll(entry => entry.until < clock);
        foreach ((Rectangle box, _) in stuckAvoid)
            AStar.Avoid.Add(box);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Path = AStar.Find(from.Value, goal, PlanBudget, out int used);
        LastPlanMs = watch.Elapsed.TotalMilliseconds;
        LastExpansions = used;
        // A partial path is followed, and still counted as a failure: the goal was not reached
        // by the plan, and the record needs to say so even while the body walks toward it.
        LastPlanFailed = Path == null || Path.Partial;
        if (LastPlanFailed)
            global::AICompanion.Brain.Debug.BrainTelemetry.DumpPlan(from.Value, goal, Path?.Goal, used, Path == null ? "no path" : "partial path");
    }

    private void Follow(NPC npc, CompanionMotor motor)
    {
        NavPath path = Path!;
        NavStep step = path.Current;
        Vector2 stepWorld = NavGrid.FeetWorld(step.Tile);

        // Advance past steps we have reached (feet within a tile of the step's feet point).
        while (!path.Finished && Vector2.Distance(npc.Bottom, NavGrid.FeetWorld(path.Current.Tile)) < 10f)
        {
            path.Index++;
        }
        if (path.Finished)
        {
            motor.Stop();
            return;
        }
        step = path.Current;
        stepWorld = NavGrid.FeetWorld(step.Tile);

        float dx = stepWorld.X - npc.Bottom.X;
        float dy = stepWorld.Y - npc.Bottom.Y; // negative = step is above
        int dir = MathF.Sign(dx) == 0 ? npc.direction : MathF.Sign(dx);

        // Rises of one tile are steps, taken by the motor's StepUp without leaving the ground;
        // a jump is only for two tiles or more, at the height the rise needs, or for a real wall.
        int riseTiles = (int)MathF.Ceiling(-dy / 16f);
        switch (step.Kind)
        {
            case MoveKind.Jump:
                FollowJump(npc, motor, path, step, stepWorld);
                break;
            case MoveKind.Drop:
            {
                // Steer to the line the planner dropped the body along (a wall of the shaft or its
                // middle, whichever lands on this step's tile), not the tile: centred on one column
                // of a two-wide shaft the body still overhangs the lip and never falls, and centred
                // in a three-wide one it falls past the lip that only a wall-hugging body lands on.
                float gap = SteerX(step, npc, throughPlatform: false) - npc.Bottom.X;
                int toGap = MathF.Sign(gap) == 0 ? dir : MathF.Sign(gap);
                motor.MoveX(motor.OnGround || MathF.Abs(gap) > 2f ? toGap * CompanionMotor.WalkSpeed * 0.8f : 0f);
                break;
            }
            case MoveKind.FallThrough:
            {
                // Same steer, then let the body pass the platform this tick.
                float gap = SteerX(step, npc, throughPlatform: true) - npc.Bottom.X;
                motor.MoveX(MathF.Abs(gap) > 2f ? MathF.Sign(gap) * CompanionMotor.WalkSpeed * 0.5f : 0f);
                motor.WantsFallThrough = true;
                break;
            }
            default:
                motor.MoveX(dir * CompanionMotor.WalkSpeed);
                if (motor.OnGround && (WallAhead(npc, dir) || riseTiles >= 2))
                    motor.Jump(CompanionMotor.JumpScaleForTiles(Math.Max(2, riseTiles)));
                break;
        }
    }

    // The jump step the run-up state belongs to, and where the run-up stands: backing away from
    // the take-off, or already run once (so a second arrival at the take-off jumps whatever the
    // speed, rather than backing away for ever on a runway too short for the profile).
    private (Point From, Point Tile) runUpEdge;
    private bool backingOff;
    private bool ranUp;

    /// <summary>
    /// Make the jump the planner found, as it found it: at its velocity scale, from its take-off
    /// tile, carrying its start speed. A standing jump settles onto the take-off first. A running
    /// jump arriving at the take-off too slowly backs away along the row, as far as the runway
    /// the profile's speed needs and the standable tiles behind allow, then runs in at that speed
    /// and jumps as it crosses the take-off; past the take-off it jumps at once, because the next
    /// step is the edge. In the air the body steers toward the landing column with the rule the
    /// planner simulated: full speed, then coasting inside the stopping distance. This is what
    /// makes a full-height running jump and a hop two different moves, and what lets the body
    /// reach a ledge under an overhang from a few tiles back instead of from straight beneath it.
    /// </summary>
    private void FollowJump(NPC npc, CompanionMotor motor, NavPath path, NavStep step, Vector2 landing)
    {
        if (!motor.OnGround)
        {
            motor.MoveX(BodyPhysics.SteerToward(landing.X, npc.Bottom.X, npc.velocity.X));
            return;
        }
        // The state belongs to the edge, not the path index: a replan resets the index, and a new
        // path with a jump at the same index would otherwise inherit another jump's run-up.
        if ((step.From, step.Tile) != runUpEdge)
        {
            runUpEdge = (step.From, step.Tile);
            backingOff = false;
            ranUp = false;
        }

        Vector2 takeoff = NavGrid.FeetWorld(step.From);
        float need = step.StartVx;
        float vx = npc.velocity.X;
        if (need == 0f)
        {
            // A standing jump: be on the take-off, still, then jump.
            float off = takeoff.X - npc.Bottom.X;
            if (MathF.Abs(off) <= 6f && MathF.Abs(vx) < 0.6f)
            {
                motor.Jump(step.JumpScale);
                return;
            }
            motor.MoveX(BodyPhysics.SteerToward(takeoff.X, npc.Bottom.X, vx));
            return;
        }

        int jd = MathF.Sign(need);
        float along = (npc.Bottom.X - takeoff.X) * jd; // positive once past the take-off toward the landing
        bool fastEnough = vx * jd >= MathF.Abs(need) - 0.4f;
        float runway = Runway(step, jd);
        float mark = takeoff.X - jd * runway;
        if (backingOff)
        {
            // Coast onto the runway mark with the jump's own steering rule rather than walking
            // through it: a reversal from the walk speed takes about two tiles to stop, and when
            // the runway is capped by the floor the mark is the last standable tile behind.
            if (MathF.Abs(npc.Bottom.X - mark) <= 6f && MathF.Abs(vx) < 0.6f)
            {
                backingOff = false;
                ranUp = true;
            }
            else
            {
                motor.MoveX(BodyPhysics.SteerToward(mark, npc.Bottom.X, vx));
                return;
            }
        }
        if (along >= -2f && (fastEnough || ranUp))
        {
            motor.Jump(step.JumpScale);
            motor.MoveX(BodyPhysics.SteerToward(landing.X, npc.Bottom.X, vx));
            return;
        }
        if (along >= -2f)
        {
            // At or past the take-off, too slow, and not run up yet: back away if there is
            // anywhere to. Past the take-off counts too, because a fresh plan can start with the
            // body a few pixels beyond the pose centre and a jump from there at no speed is a
            // strike, while backing away is a move away from the edge.
            if (runway < 12f)
            {
                motor.Jump(step.JumpScale);
                motor.MoveX(BodyPhysics.SteerToward(landing.X, npc.Bottom.X, vx));
                return;
            }
            backingOff = true;
            motor.MoveX(BodyPhysics.SteerToward(mark, npc.Bottom.X, vx));
            return;
        }
        // Behind the take-off: run in at the profile's own speed, not the walk speed, because the
        // arc was simulated at that speed and a faster body flies a longer arc into the overhang
        // the profile was chosen to clear.
        motor.MoveX(jd * MathF.Abs(need));
    }

    /// <summary>The X a descending step falls along: the plan's own steer point, or the middle of the opening for a path made without one.</summary>
    private static float SteerX(NavStep step, NPC npc, bool throughPlatform)
        => step.SteerX > 0f ? step.SteerX : NavGrid.OpenSpanCentreX(step.Tile.X, NavGrid.FeetTile(npc.Bottom).Y, throughPlatform);

    /// <summary>
    /// How far behind a jump's take-off the body can back up along the take-off row, in pixels:
    /// the distance the motor needs to reach the profile's speed from rest, plus a tile to turn
    /// in, capped by the standable tiles actually there.
    /// </summary>
    private static float Runway(NavStep step, int direction)
    {
        float need = MathF.Abs(step.StartVx);
        float wanted = need * need / (2f * BodyPhysics.Acceleration) + 16f;
        int tiles = 0;
        while (tiles * 16f < wanted && tiles < 8 && NavGrid.IsStandable(step.From.X - direction * (tiles + 1), step.From.Y))
            tiles++;
        return MathF.Min(wanted, tiles * 16f);
    }

    private void WalkStraight(NPC npc, CompanionMotor motor, Vector2 target)
    {
        float dx = target.X - npc.Bottom.X;
        if (MathF.Abs(dx) < 4f)
        {
            motor.Stop();
            return;
        }
        int dir = MathF.Sign(dx);
        motor.MoveX(dir * CompanionMotor.WalkSpeed);
        // Only a wall earns a jump here. Jumping because the target is above produced a hop every
        // tick under any ledge the planner could not route to.
        if (motor.OnGround && WallAhead(npc, dir))
            motor.Jump();
    }

    /// <summary>
    /// A real wall in the walking direction: solid at the feet row and the row above it, so the
    /// motor's StepUp cannot take it. A collision flag alone is not that test: the game sets
    /// collideX for a one-tile kerb on the tick it is met, before StepUp lifts the body over it,
    /// and jumping on the flag made every kerb a hop.
    /// </summary>
    private static bool WallAhead(NPC npc, int dir)
    {
        if (!npc.collideX)
            return false;
        Point feet = NavGrid.FeetTile(npc.Bottom);
        int ahead = feet.X + dir;
        return NavGrid.IsSolid(ahead, feet.Y) && NavGrid.IsSolid(ahead, feet.Y - 1);
    }

    private void TrackStuck(NPC npc)
    {
        if (Vector2.DistanceSquared(npc.position, lastPosition) < 1f)
            stuckTicks++;
        else
            stuckTicks = 0;
        lastPosition = npc.position;
    }

    /// <summary>The brain has acted on the strikes (asked for another spot); start counting again.</summary>
    public void ResetStrikes() => StuckStrikes = 0;

    public void Clear()
    {
        Path = null;
        GoalTile = null;
        StuckStrikes = 0;
    }
}
