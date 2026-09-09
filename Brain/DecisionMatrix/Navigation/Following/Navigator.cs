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

    /// <summary>
    /// The wall-clock one plan may spend in the game, in milliseconds. Zero is no limit, which is
    /// the default on purpose: the replay tool runs this same navigator, and a bound on wall-clock
    /// would make a scenario pass or fail with the machine's load, so the committed corpus would
    /// stop being reproducible. The mod turns it on; the offline tool never does.
    ///
    /// Eight milliseconds is half a frame, which leaves the other half for the rest of the brain
    /// and for the game. A plan cut off here returns the partial path to the nearest node it
    /// reached, so the cost of the limit is a rougher route rather than a missing one.
    /// </summary>
    public static double PlanMsBudget { get; set; }
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

    /// <summary>
    /// How far the requested goal tile may drift before it counts as somewhere else. The
    /// positioner picks afresh on its own cadence, so the tile it names moves continuously even
    /// while the request behind it does not; this is what separates the request changing from the
    /// scorer twitching.
    /// </summary>
    private const int GoalSlackTiles = 2;

    private int ticksSincePlan = ReplanInterval;
    private int stuckTicks;
    private Vector2 lastPosition;
    private int clock;
    private bool forceReplan;

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
        // Standing within the slack, not merely passing through it: a body in mid-flight can be
        // twelve pixels from the target with its path unfinished, and treating that as arrival
        // threw the path away and stopped steering, so the body fell back out of the air, walked
        // in and jumped again for ever. The ledge-four-up fixture did exactly that, and the follow
        // harness scored it as a walk because it read this same flag (Codex review of 7525a1b, A17,
        // which named the harness; the flag it reads is where the fault starts).
        if (live.OnGround && Vector2.Distance(live.Feet, targetFeet) <= ArriveDistance)
        {
            Path = null;
            onStep = null;
            StuckStrikes = 0;
            Arrived = true;
            floor.Reset();
            BehaviourCensus.RequestReached();
            return Controls.None;
        }

        Point start = live.FeetTile;
        Point? goal = NavGrid.NearestStandable(NavGrid.FeetTile(targetFeet), 3);
        ticksSincePlan++;

        // A goal that shifted a tile or two is the same goal. The positioner rescores on its own
        // cadence with no memory of what it last picked, so the exact tile it returns wanders
        // continuously even while the companion is doing one thing, and reading every wander as a
        // new goal cost three things at once: a full search several times a second, a replan
        // landing in the middle of a jump's run-up, and — through the strike reset below — an
        // escalation that could never reach its own threshold. The path is walked to within the
        // arrival slack of the real request rather than of this tile, so the slack costs nothing
        // at the end: the reactive walk covers the last tile or two.
        bool goalMoved = goal is Point want
            && (GoalTile is not Point had || Math.Abs(want.X - had.X) > GoalSlackTiles || Math.Abs(want.Y - had.Y) > GoalSlackTiles);
        // The strike count is not reset here. It used to be, and with the goal tile changing every
        // seventeen ticks that zeroed it about three times a second, so the two-strike escalation
        // the brain reads to ask for a different spot was unreachable by construction: the body
        // could stand at an impossible step for a whole session and never reach it. The count
        // belongs to the attempt rather than to the tile, and it is cleared on arrival and by
        // ResetStrikes when the brain has acted on it.
        // A failed plan is not retried every tick: at the full budget that is ~3 ms per tick for
        // as long as the goal stays unreachable. It waits FailedPlanRetry ticks unless the goal moves.
        bool failedRecently = LastPlanFailed && ticksSincePlan < FailedPlanRetry;
        // A partial path walked to its end is progress and not a failure to wait on: the body is
        // standing somewhere it has never planned from, so a fresh search reaches further, and
        // making it wait the failed-plan retry instead handed it to the straight-walk fallback for
        // ninety ticks, which carried it back the way it came and undid the steps it had just
        // performed (Codex review of 7525a1b, traced at run-4 block 10 ticks 4545 to 4620). It
        // becomes a real dead end only when the search from here returns nothing at all, which is
        // the empty-path case this still waits on.
        bool noPath = Path == null;
        bool stuck = stuckTicks > StuckReplanTicks;
        // The cadence waits while the current step is part way through a move a fresh plan would
        // undo (a jump's back-off and run-in); a stuck body and a moved goal do not wait.
        bool midMove = !noPath && !Path!.Finished && For(Path.Current.Kind).MidMove;
        bool stale = forceReplan || (noPath && !failedRecently) || (!noPath && (Path!.Finished || (ticksSincePlan >= ReplanInterval && !midMove) || stuck));
        // Plan only from the ground: an airborne body has no standable tile under it, and a
        // plan that failed for that reason blocked replanning for the retry wait, during which
        // straight walking hopped every kerb and put the body back in the air for the next try.
        // A body that has stood still long enough to replan while it had somewhere to go earns a
        // strike, and it earns one whether or not a step is in hand. With a step, the grid offered
        // a move the body cannot take and the step is priced so the next plan goes another way
        // (replanning alone returned the same path three times over in run 5, 2026-09-08). With no
        // step there is nothing to price, but the count still has to rise: until it did, a body
        // being shoved at a wall by the fallback recorded no fault and no strike for thousands of
        // ticks, so the brain never reached the two strikes that make it ask for another spot.
        // A pinned body earns its strike and its replan as well as a grounded one. Grounded is
        // velocity.Y == 0, which a body being held with gravity accumulating under it is not, so
        // through the whole shaft freeze both this gate and the planning gate below were shut and
        // the body could neither strike, replan nor escape — independently of the strike reset
        // that has just been removed. Standing still is only the body's own choice while the body
        // is capable of moving.
        if (stuck && (live.OnGround || live.CannotAct))
        {
            if (!noPath && !Path!.Finished)
                Strike(Path.Current.Tile);
            else
                StuckStrikes++;
            // The strike consumes the count. A plan does not: the cadence replans every half
            // second, and a count reset there could never reach the threshold while a path
            // existed, which is why a parked body never struck in seven runs.
            stuckTicks = 0;
            forceReplan = true;
            stale = true;
        }
        // A moved goal waits for the run-up the same way the cadence does. It did not, and the
        // consequence was that a replan could land in the middle of a jump backing away to its
        // runway mark: the fresh plan offered the mirror jump from the same take-off, the body
        // turned round, and it circled between two jumps with nothing ever faulting. The mid-move
        // guard exists for exactly that and was applied to one of the two ways a plan starts.
        bool replan = goalMoved ? !midMove || stuck : stale;
        if (goal != null && replan && (live.OnGround || live.CannotAct))
            Plan(start, goal.Value);

        // Stuck is tracked in every branch, including the fallback: a body going nowhere is going
        // nowhere whether it is following a step or walking straight at a target no plan reached.
        Controls controls = Path == null || Path.Finished ? floor.Toward(live, targetFeet) : Follow(live);
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
            BehaviourCensus.PlanFailed();
            PlanFailed?.Invoke(start, goal, from, 0, "no standable tile at the start");
            return;
        }
        // The brain refills the search's avoid list with the enemies every tick before this runs;
        // the stuck steps join it here, for this plan, and leave when their time is up.
        stuckAvoid.RemoveAll(entry => entry.until < clock);
        foreach ((Rectangle box, _) in stuckAvoid)
            AStar.Avoid.Add(box);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        AStar.MsBudget = PlanMsBudget;
        Path = AStar.Find(from.Value, goal, PlanBudget, out int used);
        LastPlanMs = watch.Elapsed.TotalMilliseconds;
        LastExpansions = used;
        if (Path != null)
        {
            // The floor's stall counter is about one attempt at one target, so it starts clean
            // whenever a path exists to hand back to it later.
            floor.Reset();
            BehaviourCensus.Planned(Path);
        }
        // A partial path is followed, and still counted as a failure: the goal was not reached
        // by the plan, and the record needs to say so even while the body walks toward it.
        LastPlanFailed = Path == null || Path.Partial;
        LastPlanEmpty = Path == null;
        if (Path == null)
            BehaviourCensus.PlanFailed();
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
            BehaviourCensus.Begun(step);
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
        BehaviourCensus.Finished(step, outcome);
    }

    /// <summary>
    /// The floor: a move from the current state with no plan, which is what every vanilla walker
    /// has and this had only a stub of. It used to walk at the target and jump for a wall and
    /// nothing else, which meant an unroutable ledge one tile high stopped the body dead — the
    /// planner was the only thing that could produce a climb, so a plan that failed produced a
    /// statue. <see cref="ReactiveWalk"/> is the fighter AI's own obstacle ladder and gap leap.
    /// </summary>
    private readonly ReactiveWalk floor = new();

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
        floor.Reset();
    }
}
