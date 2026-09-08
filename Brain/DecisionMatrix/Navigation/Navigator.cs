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

    private int ticksSincePlan = ReplanInterval;
    private int stuckTicks;
    private Vector2 lastPosition;

    /// <summary>Move toward <paramref name="targetFeet"/> this tick. Returns true when arrived.</summary>
    public bool MoveTo(NPC npc, CompanionMotor motor, Vector2 targetFeet)
    {
        if (Vector2.Distance(npc.Bottom, targetFeet) <= ArriveDistance)
        {
            motor.Stop();
            Path = null;
            return true;
        }

        Point start = NavGrid.FeetTile(npc.Bottom);
        Point? goal = NavGrid.NearestStandable(NavGrid.FeetTile(targetFeet), 3);
        ticksSincePlan++;

        bool goalMoved = goal != GoalTile;
        // A failed plan is not retried every tick: at the full budget that is ~3 ms per tick for
        // as long as the goal stays unreachable. It waits FailedPlanRetry ticks unless the goal moves.
        bool failedRecently = LastPlanFailed && ticksSincePlan < FailedPlanRetry;
        // A finished partial path is a failed plan that has been walked out: it waits like one.
        bool noPath = Path == null || (Path.Partial && Path.Finished);
        bool stale = (noPath && !failedRecently) || (!noPath && (Path!.Finished || ticksSincePlan >= ReplanInterval || stuckTicks > 40));
        // Plan only from the ground: an airborne body has no standable tile under it, and a
        // plan that failed for that reason blocked replanning for the retry wait, during which
        // straight walking hopped every kerb and put the body back in the air for the next try.
        if (goal != null && (goalMoved || stale) && motor.OnGround)
        {
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
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Path = AStar.Find(from.Value, goal, PlanBudget, out int used);
        LastPlanMs = clock.Elapsed.TotalMilliseconds;
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
                // Jump first so the arc starts from the current tile, then steer in the air with
                // the same rule the planner simulated: full speed toward the landing column, and
                // coast once inside the stopping distance, so the body lands on the tile the plan
                // promised instead of hunting past it.
                if (motor.OnGround)
                    motor.Jump(riseTiles >= 2 ? CompanionMotor.JumpScaleForTiles(riseTiles) : 1f);
                motor.MoveX(BodyPhysics.SteerToward(stepWorld.X, npc.Bottom.X, npc.velocity.X));
                break;
            case MoveKind.Drop:
            {
                // Steer to the middle of the opening, not the tile: centred on one column of a
                // two-wide shaft the body still overhangs the lip and never falls.
                float gap = NavGrid.OpenSpanCentreX(step.Tile.X, NavGrid.FeetTile(npc.Bottom).Y, throughPlatform: false) - npc.Bottom.X;
                int toGap = MathF.Sign(gap) == 0 ? dir : MathF.Sign(gap);
                motor.MoveX(motor.OnGround || MathF.Abs(gap) > 2f ? toGap * CompanionMotor.WalkSpeed * 0.8f : 0f);
                break;
            }
            case MoveKind.FallThrough:
            {
                // Same steer, then let the body pass the platform this tick.
                float gap = NavGrid.OpenSpanCentreX(step.Tile.X, NavGrid.FeetTile(npc.Bottom).Y, throughPlatform: true) - npc.Bottom.X;
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

    public void Clear()
    {
        Path = null;
        GoalTile = null;
    }
}
