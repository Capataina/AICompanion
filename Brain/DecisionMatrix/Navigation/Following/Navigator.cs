#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// What happened to a step the follower performed: the move, how long it was proven to take and
/// how long it took, and how it ended (None is success). The telemetry writes the last one and
/// the replay's follow harness prints every one, which is how the simulated body and the game's
/// are read against each other.
/// </summary>
public readonly record struct EdgeReport(MoveKind Kind, Point From, Point Tile, int Expected, int Actual, TraversalFault Outcome);

/// <summary>
/// Gets the companion to a world position, in the body's own terms and nobody else's: it reads
/// a <see cref="BodyState"/> and returns the <see cref="Controls"/> for this tick, so the motor
/// drives the NPC with them in the game and the replay drives <see cref="BodyMotion"/> with them
/// off it, and the two run one navigator. Plans with <see cref="AStar"/> when the goal tile
/// changes or the path goes stale, keeps the path between plans, and hands each step to the
/// traversal that proved it. Falls back to straight-line walking when no path exists, so a
/// missing path degrades to the old behaviour rather than a freeze.
/// </summary>
public sealed class Navigator
{
    public const int PlanBudget = 1500;
    private const int ReplanInterval = 30;
    private const int FailedPlanRetry = 90;
    private const float ArriveDistance = 12f;

    /// <summary>Where a failed plan is reported (the telemetry's tile-window dump in the game; nothing in the replay): start, goal, where a partial path ends, expansions, reason.</summary>
    public static Action<Point, Point, Point?, int, string>? PlanFailed;

    public NavPath? Path { get; private set; }
    public Point? GoalTile { get; private set; }
    public bool LastPlanFailed { get; private set; }
    /// <summary>The last plan found nothing at all, as against a partial path that walks toward the goal: the brain's stranded count reads this and not the failure, because a goal beyond the budget is far and not sealed off.</summary>
    public bool LastPlanEmpty { get; private set; }
    /// <summary>
    /// A plan ran on this tick, so <see cref="LastPlanFailed"/> and <see cref="LastPlanEmpty"/>
    /// describe the current goal and not an earlier one's. Set by the plan and cleared at the
    /// start of every move and by <see cref="Clear"/>, never read off the replan counter, which
    /// an arrival or a cleared goal leaves at zero for a tick that planned nothing.
    /// </summary>
    public bool PlannedThisTick { get; private set; }
    public int LastExpansions { get; private set; }

    /// <summary>The last move ended within the arrival slack of its target.</summary>
    public bool Arrived { get; private set; }

    /// <summary>Ticks the body has not moved while the follower had somewhere to go; the scenario capture reads it.</summary>
    public int StuckTicks => stuckTicks;

    /// <summary>Wall-clock of the last search, for the telemetry: the frame cost of the pose grid is otherwise unmeasured.</summary>
    public double LastPlanMs { get; private set; }

    /// <summary>
    /// How many times in a row the body has stood still on a path to the current goal long enough
    /// to replan, or a step has faulted. One strike prices the step so the next plan goes another
    /// way; the brain reads two as "this spot cannot be reached the way the grid thinks" and asks
    /// the positioner for a different one. Cleared when the goal moves or is reached.
    /// </summary>
    public int StuckStrikes { get; private set; }

    /// <summary>The fault the current step raised this tick, if any; the scenario capture reads it.</summary>
    public TraversalFault LastFault { get; private set; }

    /// <summary>How many steps have faulted since the navigator was made.</summary>
    public int FaultCount { get; private set; }

    /// <summary>The last step that finished or faulted, and how many have; sticky until the next.</summary>
    public EdgeReport? LastEdge { get; private set; }
    public int EdgeCount { get; private set; }

    /// <summary>Ticks a stuck step stays priced after the strike that added it.</summary>
    private const int StuckAvoidTicks = 600;

    private const int StuckReplanTicks = 40;

    private int ticksSincePlan = ReplanInterval;
    private int stuckTicks;
    private Vector2 lastPosition;
    private int clock;
    private bool forceReplan;
    private int lastDir = 1;

    // The traversals this follower performs steps with, one instance each, because a jump's
    // run-up is state that belongs to the follower and not to the planner's shared set.
    private readonly Traversal[] traversals = Traversal.Fresh();
    private NavStep? onStep;
    private int ticksOnStep;
    private bool stepFaulted;

    // The steps the body has been stuck on lately, each as the body rectangle at that tile and the
    // tick it stops mattering; merged into the search's avoid list on every plan, priced and not
    // banned, so a detour wins wherever one exists and the direct way is still there when none does.
    private readonly System.Collections.Generic.List<(Rectangle box, int until)> stuckAvoid = new();

    /// <summary>The controls that move the body toward <paramref name="targetFeet"/> this tick; <see cref="Arrived"/> says whether it is there.</summary>
    public Controls MoveTo(BodyState live, Vector2 targetFeet)
    {
        clock++;
        PlannedThisTick = false;
        LastFault = TraversalFault.None;
        Arrived = false;
        if (Vector2.Distance(live.Feet, targetFeet) <= ArriveDistance)
        {
            Path = null;
            onStep = null;
            StuckStrikes = 0;
            Arrived = true;
            return Controls.None;
        }

        Point start = live.FeetTile;
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
        // The cadence waits while the current step is part way through a move a fresh plan would
        // undo (a jump's back-off and run-in); a stuck body and a moved goal do not wait.
        bool midMove = !noPath && !Path!.Finished && For(Path.Current.Kind).MidMove;
        bool stale = forceReplan || (noPath && !failedRecently) || (!noPath && (Path!.Finished || (ticksSincePlan >= ReplanInterval && !midMove) || stuck));
        // Plan only from the ground: an airborne body has no standable tile under it, and a
        // plan that failed for that reason blocked replanning for the retry wait, during which
        // straight walking hopped every kerb and put the body back in the air for the next try.
        if (goal != null && (goalMoved || stale) && live.OnGround)
        {
            // A body that stood still on a step long enough to replan has found a step the grid
            // offers and the body cannot take. Replanning alone returned the same path three times
            // over in run 5 (2026-09-08); the step is priced so the next plan goes another way.
            if (stuck && !Path!.Finished)
            {
                Strike(Path.Current.Tile);
                // The strike consumes the count. A plan does not: the cadence replans every half
                // second, and a count reset there could never reach the threshold while a path
                // existed, which is why a parked body never struck in seven runs.
                stuckTicks = 0;
            }
            Plan(start, goal.Value);
        }

        if (Path == null || Path.Finished)
            return WalkStraight(live, targetFeet);

        Controls controls = Follow(live);
        TrackStuck(live);
        return controls;
    }

    private void Strike(Point tile)
    {
        var box = new Rectangle(tile.X * 16, (tile.Y - NavGrid.BodyHeightTiles + 1) * 16, 16, NavGrid.BodyHeightTiles * 16);
        box.Inflate(4, 4);
        stuckAvoid.Add((box, clock + StuckAvoidTicks));
        StuckStrikes++;
    }

    private void Plan(Point start, Point goal)
    {
        GoalTile = goal;
        ticksSincePlan = 0;
        forceReplan = false;
        PlannedThisTick = true;
        // The step in hand keeps its clock across a plan that returns it again, so a step the
        // body cannot take runs out its allowance once and faults; a faulted step gets one more
        // attempt from the fresh plan, and if that is the same step it faults again at once,
        // which is the second strike the brain answers with a different spot.
        stepFaulted = false;
        Point? from = NavGrid.NearestStandable(start, 2);
        if (from == null)
        {
            Path = null;
            LastPlanFailed = true;
            LastPlanEmpty = true;
            LastExpansions = 0;
            PlanFailed?.Invoke(start, goal, from, 0, "no standable tile at the start");
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
        LastPlanEmpty = Path == null;
        if (LastPlanFailed)
            PlanFailed?.Invoke(from.Value, goal, Path?.Goal, used, Path == null ? "no path" : "partial path");
    }

    private Traversal For(MoveKind kind) => traversals[(int)kind];

    /// <summary>The step after the path's current one, or null at its end; a step's traversal reads it to arrive the way the next move starts.</summary>
    private static NavStep? After(NavPath path) => path.Index + 1 < path.Steps.Count ? path.Steps[path.Index + 1] : null;

    /// <summary>
    /// One tick along the path: advance past every step its traversal says is done, then let the
    /// current step's traversal check and steer. A step that faults is priced, counted as a
    /// strike and replanned from on the next ground tick; until then the body asks for nothing,
    /// because steering a move from a place it was not proven from is the defect this exists to end.
    /// </summary>
    private Controls Follow(BodyState live)
    {
        NavPath path = Path!;
        while (!path.Finished && For(path.Current.Kind).Done(live, path.Current, After(path)))
        {
            if (onStep is NavStep done && done == path.Current)
                Report(done, ticksOnStep, TraversalFault.None);
            path.Index++;
        }
        if (path.Finished)
        {
            onStep = null;
            return Controls.None;
        }
        NavStep step = path.Current;
        Traversal traversal = For(step.Kind);
        if (onStep != step)
        {
            onStep = step;
            ticksOnStep = 0;
            stepFaulted = false;
            traversal.Begin(step);
        }
        else
            ticksOnStep++;

        if (stepFaulted)
            return Controls.None;
        TraversalFault fault = traversal.Check(live, step, ticksOnStep);
        if (fault != TraversalFault.None)
        {
            stepFaulted = true;
            LastFault = fault;
            FaultCount++;
            Report(step, ticksOnStep, fault);
            Strike(step.Tile);
            forceReplan = true;
            return Controls.None;
        }
        NavStep? next = After(path);
        return traversal.Steer(live, step, next);
    }

    private void Report(NavStep step, int ticks, TraversalFault outcome)
    {
        LastEdge = new EdgeReport(step.Kind, step.From, step.Tile, step.Ticks, ticks, outcome);
        EdgeCount++;
    }

    private Controls WalkStraight(BodyState live, Vector2 target)
    {
        float dx = target.X - live.CentreX;
        if (MathF.Abs(dx) < 4f)
            return Controls.None;
        int dir = MathF.Sign(dx);
        lastDir = dir;
        // Only a wall earns a jump here. Jumping because the target is above produced a hop every
        // tick under any ledge the planner could not route to.
        return new Controls(dir * BodyPhysics.WalkSpeed, live.OnGround && WalkTraversal.WallAhead(live, dir));
    }

    private void TrackStuck(BodyState live)
    {
        var position = new Vector2(live.Left, live.Bottom);
        if (Vector2.DistanceSquared(position, lastPosition) < 1f)
            stuckTicks++;
        else
            stuckTicks = 0;
        lastPosition = position;
    }

    /// <summary>The brain has acted on the strikes (asked for another spot); start counting again.</summary>
    public void ResetStrikes() => StuckStrikes = 0;

    public void Clear()
    {
        Path = null;
        GoalTile = null;
        StuckStrikes = 0;
        PlannedThisTick = false;
        onStep = null;
        forceReplan = false;
    }
}
