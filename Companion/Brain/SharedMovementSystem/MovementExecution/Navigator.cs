#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.SharedMovementSystem;

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
    public enum ExecutionStatus { Idle, Arrived, Executable, Preparing, Partial, Unknown, Rejected }
    public ExecutionStatus Status { get; private set; }
    public AStar.SearchStopReason LastSearchStop { get; private set; }
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

    /// <summary>The current ability set, supplied by the coordinator and shared with every local simulation.</summary>
    public MovementCapabilities Capabilities { get; set; } = MovementCapabilities.Basic;

    /// <summary>
    /// Optional game-free danger oracle for local movement. It receives the predicted body and
    /// prediction tick; the reflex layer supplies projected hitboxes through this contract
    /// instead of owning a second physics loop.
    /// </summary>
    public Func<BodyState, int, bool>? UnsafeAtTick { get; set; }
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
    private readonly PlanLocalMovement local = new();
    public PlanLocalMovement.Rejection? LastRejection => local.LastRejection;
    public string PreparationResult => local.PreparationResult;
    private NavStep? onStep;
    private bool edgeReported;
    private TraversalExecution? execution;
    private int ticksOnStep;
    private bool stepFaulted;

    // The steps the body has been stuck on lately, each as the body rectangle at that tile and the
    // tick it stops mattering; merged into the search's avoid list on every plan, priced and not
    // banned, so a detour wins wherever one exists and the direct way is still there when none does.
    private readonly System.Collections.Generic.List<(Rectangle box, int until)> stuckAvoid = new();
    private readonly System.Collections.Generic.List<(NavStep step, BodyState entry, ITileWorld world, int revision, int checkedAt)> failedEntries = new();

    /// <summary>The controls that move the body toward <paramref name="targetFeet"/> this tick; <see cref="Arrived"/> says whether it is there.</summary>
    public Controls MoveTo(BodyState live, Vector2 targetFeet)
    {
        clock++;
        live = live with { Capabilities = Capabilities };
        Status = ExecutionStatus.Unknown;
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
            // The step that carried the body onto the goal is reported before the path is dropped,
            // or the census never sees it. Arrival is tested before Follow runs, so a step whose
            // Done turns true on the same tick the body lands inside the arrival slack used to be
            // discarded here silently, counted as neither completed nor faulted. That is not an
            // even loss across move kinds: a route ends on the move that reaches the goal, and a
            // goal on a ledge is reached by jumping onto it, so jumps are overwhelmingly the last
            // step while walks are overwhelmingly interior. The first telemetry from 0.8.3 read
            // Jump begun 29, completed 0, faulted 15 against Walk begun 486, completed 409 — and
            // the fourteen jumps that were neither is the tell, because a jump the body genuinely
            // cannot finish runs out its allowance and faults rather than vanishing.
            //
            // A table that says every move except a walk is broken, when they are landing, is
            // worse than no table: the plan of record for diagnosing descent hesitancy is to read
            // this exact split, since a move planned often and completed rarely and a move never
            // planned at all want opposite fixes.
            if (onStep is NavStep landed)
                Report(landed, ticksOnStep, execution?.IsDone(live) == true ? TraversalFault.None : TraversalFault.Interrupted);
            Path = null;
            onStep = null;
            execution = null;
            StuckStrikes = 0;
            Arrived = true;
            Status = ExecutionStatus.Arrived;
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
        bool midMove = !noPath && !Path!.Finished && (local.Preparing || (execution?.MidMove ?? For(Path.Current.Kind).MidMove));
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
            // Progress belongs to the body, so replacing a route cannot renew its allowance.
            // Otherwise a stationary entry is repeatedly interrupted before its timer expires.
            if (onStep is NavStep stalled && !edgeReported)
            {
                LastFault = TraversalFault.Stuck;
                FaultCount++;
                Report(stalled, ticksOnStep, TraversalFault.Stuck);
                stepFaulted = true;
            }
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
        {
            // A cadence replan can replace the edge on the tick it lands. Settle its outcome
            // against the old execution before asking the new path where it starts.
            if (onStep is NavStep completed && execution?.IsDone(live) == true)
            {
                Report(completed, ticksOnStep, TraversalFault.None);
                onStep = null;
                execution = null;
            }
            Plan(live, goal.Value);
            if (onStep is NavStep replaced && (Path == null || Path.Finished || Path.Current != replaced))
            {
                Report(replaced, ticksOnStep, TraversalFault.Interrupted);
                onStep = null;
                execution = null;
            }
        }

        // Stuck is tracked in every branch, including the fallback: a body going nowhere is going
        // nowhere whether it is following a step or walking straight at a target no plan reached.
        Controls preferred = Path == null || Path.Finished ? floor.Toward(live, targetFeet) : Follow(live);
        // A route step supplies the strategic direction; the local search executes it from the
        // actual pose, velocity, liquid state and remaining mobility. This stays on for fallback
        // movement as well, so an interrupted path does not revert to the old standing-node rule.
        live = live with { Capabilities = Capabilities };
        Vector2 localTarget = targetFeet;
        if (Path is { Finished: false } p)
            localTarget = p.Current.Kind == MoveKind.Jump ? NavGrid.FeetWorld(p.Current.From) : NavGrid.FeetWorld(p.Current.Tile);
        // A committed traversal owns its preparation controls; the local search remains the
        // active controller whenever there is no committed edge (including after a repair or
        // interruption), where it starts from the live state rather than a snapped node.
        Controls controls = Path is { Finished: false } ? preferred : local.Choose(NavGrid.World, live, localTarget, preferred, Capabilities, UnsafeAtTick);
        TrackStuck(live);
        return controls;
    }

    /// <summary>Runs one retained local repair plan against a projected-hit oracle.</summary>
    public Controls AvoidThreats(BodyState live, Func<BodyState, int, bool> unsafeAtTick, Vector2 targetFeet)
    {
        // A projected hit is an interrupt, not an extra steering opinion beside an active macro.
        // The abandoned edge is reported, then the retained local controller searches safe
        // controls from the live body rather than from the macro's take-off node.
        Interrupt(live);
        UnsafeAtTick = unsafeAtTick;
        return MoveTo(live, targetFeet);
    }

    public void Interrupt(BodyState live)
    {
        Status = ExecutionStatus.Idle;
        if (onStep is NavStep step)
            Report(step, ticksOnStep, TraversalFault.Interrupted);
        Path = null;
        onStep = null;
        execution = null;
        stepFaulted = false;
        forceReplan = false;
        floor.Reset();
    }

    private void Strike(Point tile)
    {
        var box = new Rectangle(tile.X * 16, (tile.Y - NavGrid.BodyHeightTiles + 1) * 16, 16, NavGrid.BodyHeightTiles * 16);
        box.Inflate(4, 4);
        stuckAvoid.Add((box, clock + StuckAvoidTicks));
        StuckStrikes++;
    }

    private void Plan(BodyState live, Point goal)
    {
        Point start = live.FeetTile;
        GoalTile = goal;
        ticksSincePlan = 0;
        forceReplan = false;
        PlannedThisTick = true;
        // The step in hand keeps its clock across a plan that returns it again, so a step the
        // body cannot take runs out its allowance once and faults. A faulted step is terminal;
        // selecting the same edge again begins a new attempt with its own outcome.
        if (stepFaulted)
        {
            onStep = null;
            execution = null;
        }
        stepFaulted = false;
        Point? from = NavGrid.NearestStandable(start, 2);
        if (from == null)
        {
            LastSearchStop = AStar.SearchStopReason.InvalidStart;
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
        int externalAvoidCount = AStar.Avoid.Count;
        foreach ((Rectangle box, _) in stuckAvoid)
            AStar.Avoid.Add(box);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        AStar.MsBudget = PlanMsBudget;
        int used;
        try
        {
            RefreshRejectedEntries(live, clock);
            Path = AStar.Find(from.Value, goal, PlanBudget, out used, out var stop,
                step => !EntryRejected(step, live), live.Pose);
            LastSearchStop = stop;
        }
        finally
        {
            // These prices belong to this navigator's query. Leaving them in the caller's list
            // grows it on every retry and prevents expired failures from ever leaving it.
            AStar.Avoid.RemoveRange(externalAvoidCount, AStar.Avoid.Count - externalAvoidCount);
        }
        LastPlanMs = watch.Elapsed.TotalMilliseconds;
        LastExpansions = used;
        if (Path != null)
        {
            // The floor's stall counter is about one attempt at one target, so it starts clean
            // whenever a path exists to hand back to it later.
            floor.Reset();
            BehaviourCensus.Planned(Path);
        }
        // A partial path that ends on the tile the search started from is not a route. Every step
        // it carries is already covered by the body, so the follower reads them all as done on
        // the first tick and the path is finished without anything having moved — and read as a
        // found path that is strictly worse than no path at all, because it disarms the mechanisms
        // that answer "there is no way there". The brain clears its stranded count on any plan
        // that is not empty, so the roam that walks a sealed pocket never starts; the cadence sees
        // a finished path and replans every tick instead of honouring the failed-plan wait; and
        // the census records a plan that was found. That is the state the record caught at tick
        // 20,679 of the 2026-09-09 underground session: survive running, an Exact request, path 1
        // step of 1, velocity zero, breath zero, for a hundred and twenty ticks. It is discarded
        // rather than walked, which hands the body to the straight-line fallback exactly as no
        // path does, and the plan is reported as the failure it is.
        Point end = Path is { Steps.Count: > 0 } p ? p.Steps[^1].Tile : from.Value;
        if (Path != null && Path.Partial && end == from.Value)
            Path = null;
        // A partial path is followed, and still counted as a failure: the goal was not reached
        // by the plan, and the record needs to say so even while the body walks toward it.
        LastPlanFailed = Path == null || Path.Partial;
        LastPlanEmpty = Path == null;
        if (Path == null)
            BehaviourCensus.PlanFailed();
        if (LastPlanFailed)
            PlanFailed?.Invoke(from.Value, goal, Path?.Goal, used, Path == null ? "no path" : "partial path");
    }

    internal void RefreshRejectedEntries(BodyState live, int now)
    {
        failedEntries.RemoveAll(entry => entry.world != NavGrid.World || entry.revision != NavGrid.World.Revision || !PlanLocalMovement.Matches(entry.entry, live));
        // Unannounced sand/liquid changes have no revision hook. Recheck old negative
        // evidence physically; elapsed time alone never makes a failed edge admissible.
        for (int i = failedEntries.Count - 1; i >= 0; i--)
        {
            var failed = failedEntries[i];
            if (unchecked(now - failed.checkedAt) <= AStar.EdgeCacheLifeTicks) continue;
            var proof = new PlanLocalMovement();
            var retry = new TraversalExecution(For(failed.step.Kind).CopyForExecution(), failed.step, null);
            if (proof.TryExecute(NavGrid.World, retry, live, null, out _, out _))
            {
                failedEntries.RemoveAt(i);
                local.InvalidatePhysicalProof();
            }
            else failedEntries[i] = (failed.step, failed.entry, failed.world, failed.revision, now);
        }
    }

    internal bool EntryRejected(NavStep step, BodyState live) => failedEntries.Exists(entry => entry.step == step && PlanLocalMovement.Matches(entry.entry, live));

    internal void RememberRejectedEntry(NavStep step, BodyState live)
    {
        if (!EntryRejected(step, live)) failedEntries.Add((step, live, NavGrid.World, NavGrid.World.Revision, clock));
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
        while (!path.Finished && (execution?.IsDone(live) ?? For(path.Current.Kind).Done(live, path.Current, After(path))))
        {
            if (onStep is NavStep done && done == path.Current)
                Report(done, ticksOnStep, TraversalFault.None);
            path.Index++;
            execution = null;
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
            edgeReported = false;
            ticksOnStep = 0;
            stepFaulted = false;
            execution = new TraversalExecution(traversal, step, After(path));
            BehaviourCensus.Begun(step);
        }
        else
            ticksOnStep++;

        if (stepFaulted)
            return Controls.None;
        if (!local.TryExecute(NavGrid.World, execution!, live, UnsafeAtTick, out Controls controls, out TraversalFault fault))
        {
            // Coarse edges are proposals from representative resting poses. Rebuild the same
            // destination's movement profiles from the actual entry before rejecting a route.
            // This is shared by jump/drop/platform descent, rather than a correction per cave.
            if (ticksOnStep == 0 && TryRefineEntry(live, step, After(path), out var refined, out controls))
            {
                path.Steps[path.Index] = refined;
                onStep = refined;
                Status = path.Partial ? ExecutionStatus.Partial : ExecutionStatus.Executable;
                return controls;
            }
            if (local.TryPrepare(NavGrid.World, execution!, live, UnsafeAtTick, out controls))
            {
                Status = ExecutionStatus.Preparing;
                return controls;
            }
            if (fault != TraversalFault.None && execution!.Ticks == 0 && !traversal.EntryDependsOnNext)
            {
                RememberRejectedEntry(step, live);
            }
            if (fault == TraversalFault.None)
                fault = TraversalFault.Interrupted;
            stepFaulted = true;
            LastFault = fault;
            FaultCount++;
            Report(step, ticksOnStep, fault);
            if (execution!.Ticks > 0) Strike(step.Tile);
            forceReplan = true;
            Status = ExecutionStatus.Rejected;
            return Controls.None;
        }
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
        Status = path.Partial ? ExecutionStatus.Partial : ExecutionStatus.Executable;
        return controls;
    }

    private bool TryRefineEntry(BodyState live, NavStep step, NavStep? next, out NavStep refined, out Controls controls)
    {
        refined = step;
        controls = Controls.None;
        if (!live.OnGround) return false;
        var candidates = new System.Collections.Generic.List<NavStep>();
        if (step.Kind == MoveKind.Jump)
        {
            int rise = (int)Math.Ceiling((live.Bottom - NavGrid.FeetWorld(step.Tile).Y) / 16f);
            foreach (var profile in JumpTraversal.JumpProfiles(rise, Math.Sign(step.Tile.X - live.FeetTile.X)))
                candidates.Add(step with { JumpScale = profile.scale, StartVx = profile.startVx });
        }
        else
        {
            foreach (var edge in For(step.Kind).Candidates(new NavNode(live.FeetTile, live.Mobility), live.Pose, AStar.AllowLava))
                if (edge.Step.Tile == step.Tile) candidates.Add(edge.Step);
        }
        foreach (NavStep candidate in candidates)
        {
            if (candidate == step) continue;
            var attempt = new TraversalExecution(For(candidate.Kind).CopyForExecution(), candidate, next);
            if (!local.TryExecute(NavGrid.World, attempt, live, UnsafeAtTick, out controls, out _)) continue;
            execution = attempt;
            refined = candidate;
            return true;
        }
        return false;
    }

    private void Report(NavStep step, int ticks, TraversalFault outcome)
    {
        // A fault terminates an attempt even if its path survives until the next ground tick.
        // Clearing or interrupting that path must not count a second terminal outcome.
        if (edgeReported) return;
        edgeReported = true;
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
        Status = ExecutionStatus.Idle;
        if (onStep is NavStep step)
            Report(step, ticksOnStep, TraversalFault.Interrupted);
        Path = null;
        GoalTile = null;
        StuckStrikes = 0;
        PlannedThisTick = false;
        onStep = null;
        execution = null;
        forceReplan = false;
        floor.Reset();
    }
}
