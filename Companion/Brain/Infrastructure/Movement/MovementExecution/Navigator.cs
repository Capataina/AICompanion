#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

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
    public const float ArriveDistance = 12f;

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
    public long SearchId { get; private set; }
    public long AttemptId { get; private set; }
    public int SearchExpansions { get; private set; }
    public bool SearchPending { get; private set; }
    public string ProgressReason { get; private set; } = "idle";
    public int ExperienceRoutesUsed { get; private set; }

    /// <summary>Unfinished route waypoints, separate from whether the current follow objective is satisfied.</summary>
    public int RemainingRouteSteps => Path is { } path ? Math.Max(0, path.Steps.Count - path.Index) : 0;

    /// <summary>
    /// The remaining planned traversal time from the live waypoint. The active traversal's elapsed
    /// ticks are subtracted so telemetry can distinguish a route that is being consumed from one
    /// that is merely retained; a partial route remains an estimate rather than an arrival claim.
    /// </summary>
    public float RemainingEstimatedRouteTicks
    {
        get
        {
            if (Path is not { } path) return 0f;
            float remaining = 0f;
            for (int i = path.Index; i < path.Steps.Count; i++)
                remaining += Math.Max(1, path.Steps[i].Ticks);
            if (onStep != null && path.Index < path.Steps.Count)
                remaining -= Math.Min(ticksOnStep, Math.Max(1, path.Steps[path.Index].Ticks));
            return Math.Max(0f, remaining);
        }
    }

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

    /// <summary>
    /// Why the held goal is not being delivered, or null while it is. It is derived only from
    /// signals this navigator already owns — the search's stop, the macro proof's rejection, the
    /// predicted-versus-observed body and the owner that interrupted — so the class names the first
    /// contract the evidence shows broken instead of a symptom. Delivery evidence (a completed step,
    /// arrival), a voluntary release or a changed goal clears it.
    /// </summary>
    public MovementFailureReport? LastFailure { get; private set; }
    public MovementFailure Failure => LastFailure?.Kind ?? MovementFailure.None;

    /// <summary>How the last begun attempt ended, and the running tallies of each ending. Completion
    /// and voluntary cancellation are counted apart so a replan never reads as a failed move.</summary>
    public AttemptEnding? LastEnding { get; private set; }
    public int CompletedAttempts { get; private set; }
    public int FailedAttempts { get; private set; }
    public int PreemptedAttempts { get; private set; }
    public int CancelledAttempts { get; private set; }

    /// <summary>
    /// The motor's attribution for the difference between the body it predicted and the body the
    /// engine produced, supplied by the coordinator each tick ("none" when nothing external is
    /// known). The navigator cannot see hits or terrain callbacks, and without this a knockback
    /// mid-jump would be scored as a native mismatch and retire a route that works.
    /// </summary>
    public string DisplacementCause { get; set; } = "none";

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
    private int answerWaitTicks;
    /// <summary>Ticks the body has stood without a route waiting for its current goal's search, summed across restarts.</summary>
    internal int AnswerWaitTicks => answerWaitTicks;
    private Vector2 lastPosition;
    private int clock;
    private bool forceReplan;
    private ContinueRouteSearch? search;
    private bool searchLava, searchOneWay;
    private MovementCapabilities searchCapabilities;
    private readonly SearchControlSequences clearance = new();
    private Vector2 clearanceTarget;

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
    // A voluntary release that arrived while the body was committed to a move (airborne, or inside
    // a jump's back-off and run-in) is held here until the move lands, because the executor and
    // not the activity supplies the boundary at which an airborne manoeuvre can stop. The 13:27
    // capture of 14 September holds the case that made this necessary: keep-company read its
    // follow box as satisfied one tick after take-off, because the player was passing overhead
    // mid-jump, handed movement a Hold, and the navigator dropped the step in the air; the body's
    // horizontal steer went to zero while it was still rising, it fell short of the platform,
    // landed outside the box, and the same jump was replanned — 36 of 65 cancelled jumps and all
    // 8 cancelled drops in that play ended airborne with the navigator left Idle. A pre-emption
    // (safety, downing, recovery) still takes the body at once, because those owners supply
    // their own controls; only a release that would leave the body with none is deferred.
    private string? pendingRelease;
    private Vector2 lastTargetFeet;
    /// <summary>A voluntary release is waiting for the committed move in hand to land.</summary>
    public bool ReleasePending => pendingRelease != null;
    /// <summary>Releases deferred to a landing, counted once per deferral rather than per tick held.</summary>
    public int DeferredReleases { get; private set; }
    private BodyState observed, entryState;
    private BodyState? expectedBody;
    private bool attemptContinuous;
    // The first cause the motor gave for a divergence during this attempt; "none" while every
    // observed tick matched its prediction.
    private string divergence = "none";
    private Rectangle sweptBody;
    private int entryRevision;

    // The steps the body has been stuck on lately, each as the body rectangle at that tile and the
    // tick it stops mattering; merged into the search's avoid list on every plan, priced and not
    // banned, so a detour wins wherever one exists and the direct way is still there when none does.
    private readonly System.Collections.Generic.List<(Rectangle box, int until)> stuckAvoid = new();
    private readonly System.Collections.Generic.List<(NavStep step, BodyState entry, ITileWorld world, int revision, int checkedAt)> failedEntries = new();

    /// <summary>The controls that move the body toward <paramref name="targetFeet"/> this tick; <see cref="Arrived"/> says whether it is there.</summary>
    public Controls MoveTo(BodyState live, Vector2 targetFeet)
    {
        // A live request again: whatever release was waiting is withdrawn, not applied.
        pendingRelease = null;
        lastTargetFeet = targetFeet;
        return Tick(live, targetFeet);
    }

    /// <summary>
    /// Steers the committed move a deferred release is waiting on, toward the goal it was asked
    /// for, and applies the release on the first tick the body is no longer committed. The caller
    /// asks this instead of returning no controls, so a body in the air keeps its in-flight steer.
    /// </summary>
    public Controls ContinueCommitted(BodyState live)
    {
        if (pendingRelease is not string cause) return Controls.None;
        if (Committed(live)) return Tick(live, lastTargetFeet);
        // The move has landed. A step whose Done is true is reported completed before the
        // release drops the path, or the census would read the landing as a cancellation, which
        // is the very row this deferral exists to empty.
        if (onStep is NavStep landed && execution?.IsDone(live) == true)
        {
            Report(landed, ticksOnStep, TraversalFault.None, AttemptEnding.Completed);
            onStep = null;
            execution = null;
        }
        pendingRelease = null;
        Interrupt(live, AttemptEnding.Cancelled, cause);
        return Controls.None;
    }

    /// <summary>
    /// A body is committed while it is airborne, or while its step is in the part of a move a fresh
    /// plan would undo (a jump's back-off and run-in, a validated preparation prefix). A grounded
    /// walk is not: it can stop on any tick, and a release stops it at once.
    /// </summary>
    private bool Committed(BodyState live)
        => Path is { Finished: false } && onStep != null
            && (!live.OnGround || local.Preparing || (execution?.MidMove ?? false));

    private Controls Tick(BodyState live, Vector2 targetFeet)
    {
        clock++;
        if (expectedBody is BodyState expected && !PlanLocalMovement.Matches(expected, live))
        {
            attemptContinuous = false;
            if (divergence == "none")
                divergence = DisplacementCause is "none" or "no-prediction" ? "unattributed" : DisplacementCause;
        }
        expectedBody = null;
        observed = live;
        if (onStep != null) sweptBody = Rectangle.Union(sweptBody, BodyBox(live));
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
        if (live.OnGround && Vector2.Distance(live.Feet, targetFeet) <= ArriveDistance
            && (onStep == null || execution?.IsDone(live) == true))
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
                Report(landed, ticksOnStep, TraversalFault.None, AttemptEnding.Completed);
            LastFailure = null;
            Path = null;
            onStep = null;
            execution = null;
            StuckStrikes = 0;
            answerWaitTicks = 0;
            Arrived = true;
            Status = ExecutionStatus.Arrived;
            pendingRelease = null;
            search?.Dispose(); search = null; SearchPending = false;
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
        // A failure explains the goal it was found for; a different goal has not failed yet.
        if (goalMoved) { LastFailure = null; answerWaitTicks = 0; }
        // The strike count is not reset here. It used to be, and with the goal tile changing every
        // seventeen ticks that zeroed it about three times a second, so the two-strike escalation
        // the brain reads to ask for a different spot was unreachable by construction: the body
        // could stand at an impossible step for a whole session and never reach it. The count
        // belongs to the attempt rather than to the tile, and it is cleared on arrival and by
        // ResetStrikes when the brain has acted on it.
        // A failed plan is not retried every tick: at the full budget that is ~3 ms per tick for
        // as long as the goal stays unreachable. It waits FailedPlanRetry ticks unless the goal moves.
        // A finished incomplete search still honours retry cadence. Its usable prefix does
        // not become a failure merely to prevent replanning every tick at the prefix's end.
        bool failedRecently = search is { Finished: true, Stop: not AStar.SearchStopReason.Found }
            && ticksSincePlan < FailedPlanRetry;
        // A partial path walked to its end is progress and not a failure to wait on: the body is
        // standing somewhere it has never planned from, so a fresh search reaches further, and
        // making it wait the failed-plan retry instead handed it to the straight-walk fallback for
        // ninety ticks, which carried it back the way it came and undid the steps it had just
        // performed (Codex review of 7525a1b, traced at run-4 block 10 ticks 4545 to 4620). It
        // becomes a real dead end only when the search from here returns nothing at all, which is
        // the empty-path case this still waits on.
        bool noPath = Path == null;
        // A body with no route to walk moves only by an endpoint-proven clearance, so while its
        // route search is still running, standing still is waiting for computation and not a
        // stall. Striking it discarded the retained frontier every forty ticks and restarted the
        // search from nothing, so a search needing more than forty slices never finished (194
        // restarts in 8,000 ticks on the starved sealed corridor); it also handed the brain a
        // physical strike, and two of those ban the spot, for what was only a slow answer. The
        // stall clock starts once the search has answered.
        // The exemption is bounded by how long the body has waited for an answer to this goal, summed
        // across restarts, and not by the life of one search: a terrain change anywhere invalidates the
        // retained query, so a player mining nearby restarted it every few ticks and a body that could
        // never be answered never struck either (166 restarts and no strike in 3,000 ticks of churn on
        // the starved sealed corridor). Walking a route, arriving or a moved goal starts the wait again.
        if (Path is { Finished: false }) answerWaitTicks = 0;
        if (search is { Finished: false } && (noPath || Path!.Finished)
            && ++answerWaitTicks <= Infrastructure.Selection.Weights.RouteAnswerWaitTicks)
            stuckTicks = 0;
        bool stuck = stuckTicks > StuckReplanTicks;
        // The cadence waits while the current step is part way through a move a fresh plan would
        // undo (a jump's back-off and run-in); a stuck body and a moved goal do not wait.
        bool midMove = !noPath && !Path!.Finished && (local.Preparing || (execution?.MidMove ?? For(Path.Current.Kind).MidMove));
        bool policyChanged = search != null && (searchLava != AStar.AllowLava || searchOneWay != AStar.AllowOneWayDrops || searchCapabilities != Capabilities);
        bool stale = forceReplan || policyChanged || search?.Valid == false || (noPath && !failedRecently)
            || (!noPath && (Path!.Finished || stuck));
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
                var (ending, failure, reason) = ClassifyInFlight(TraversalFault.Stuck, reproof: false);
                Report(stalled, ticksOnStep, TraversalFault.Stuck, ending, failure, reason);
                stepFaulted = true;
            }
            else if ((noPath || Path!.Finished) && LastFailure == null)
                Fail(MovementFailure.UnusableTerminal, "stalled-without-route");
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
        // Both ways a plan starts wait for the run-up. A moved goal did not, and the consequence was
        // that a replan could land in the middle of a jump backing away to its runway mark: the
        // fresh plan offered the mirror jump from the same take-off, the body turned round, and it
        // circled between two jumps with nothing ever faulting. The stale branch did not either,
        // and a terrain revision anywhere in the world (the counter is global) replaced a grounded
        // run-up with a fresh plan; the 13:27 capture of 14 September holds 15 jumps replaced on
        // the ground that way. A stuck body still replans, because standing still is the one thing
        // a committed move never legitimately does. A stale search under a committed move is safe
        // to leave until the move lands: the macro proof re-validates its retained controls against
        // the terrain revision every tick, so a step the edit actually broke faults and replans
        // from there rather than being trusted.
        bool replan = goalMoved ? !midMove || stuck : stale && (!midMove || stuck);
        if (search != null && !goalMoved && !forceReplan && !policyChanged && search.Valid
            && (!search.Finished || search.Stop == AStar.SearchStopReason.Found && (Path == null || Path.Partial)))
        {
            AdvanceSearch(live, publish: !midMove);
            // Only a found route that could not be joined where the body stands is worth searching
            // again at once. A query that finished without one has answered, and searching again from
            // the same tile before anything changed replaced that answer with an identical unfinished
            // query, for ever, whenever the answer took more than one tick to compute.
            replan = search is { Finished: true, Stop: AStar.SearchStopReason.Found } && (Path == null || Path.Finished);
        }
        if (goal != null && replan && (live.OnGround || live.CannotAct))
        {
            // A cadence replan can replace the edge on the tick it lands. Settle its outcome
            // against the old execution before asking the new path where it starts.
            if (onStep is NavStep completed && execution?.IsDone(live) == true)
            {
                Report(completed, ticksOnStep, TraversalFault.None, AttemptEnding.Completed);
                onStep = null;
                execution = null;
            }
            Plan(live, goal.Value);
            if (onStep is NavStep replaced && (Path == null || Path.Finished || Path.Current != replaced))
            {
                Report(replaced, ticksOnStep, TraversalFault.Interrupted, AttemptEnding.Cancelled, reason: "route-replaced");
                onStep = null;
                execution = null;
            }
        }

        // Computed here rather than inside the branch below, because Follow reads the body before
        // the capabilities are stamped onto it on the next line and moving the call would change
        // which state it sees. Where there is no live path this is unused: the branch below owns
        // that case and answers it with a proven clearance move or with nothing at all.
        Controls preferred = Path is { Finished: false } ? Follow(live) : Controls.None;
        // A route step supplies the strategic direction; the local search executes it from the
        // actual pose, velocity, liquid state and remaining mobility. This stays on for fallback
        // movement as well, so an interrupted path does not revert to the old standing-node rule.
        live = live with { Capabilities = Capabilities };
        // A committed traversal owns its preparation controls; the local search remains the
        // active controller whenever there is no committed edge (including after a repair or
        // interruption), where it starts from the live state rather than a snapped node.
        Controls controls;
        if (Path is { Finished: false })
        {
            clearance.Clear();
            controls = preferred;
        }
        else
        {
            if (Vector2.DistanceSquared(clearanceTarget, targetFeet) > GoalSlackTiles * GoalSlackTiles * 256f)
            { clearance.Clear(); clearanceTarget = targetFeet; }
            bool chosen = clearance.TryChoose(NavGrid.World, live,
                state => state.OnGround && Vector2.DistanceSquared(state.Feet, targetFeet) <= ArriveDistance * ArriveDistance,
                state => Vector2.Distance(state.Feet, targetFeet) / BodyPhysics.WalkSpeed,
                Capabilities, PlanBudget / 12, PlanLocalMovement.PreparationMsBudget, out controls, UnsafeAtTick,
                allowPartialProgress: false);
            if (chosen) { Status = ExecutionStatus.Preparing; ProgressReason = "executing-clearance"; }
            else if (clearance.Pending) { Status = ExecutionStatus.Preparing; ProgressReason = "searching-clearance"; }
            // The route planner already supplies useful partial routes. A local repair must
            // prove its endpoint before leaving that ground: Euclidean improvement alone can
            // drop the body into another pocket and undo a detour around the same wall.
            else { controls = Controls.None; ProgressReason = "no-proven-clearance"; }
        }
        TrackStuck(live);
        expectedBody = BodyMotion.Step(NavGrid.World, live, controls, Capabilities);
        return controls;
    }

    /// <summary>Runs one retained local repair plan against a projected-hit oracle.</summary>
    public Controls AvoidThreats(BodyState live, Func<BodyState, int, bool> unsafeAtTick, Vector2 targetFeet)
    {
        // A projected hit is an interrupt, not an extra steering opinion beside an active macro.
        // The abandoned edge is reported, then the retained local controller searches safe
        // controls from the live body rather than from the macro's take-off node.
        Interrupt(live, AttemptEnding.Preempted, "threat-avoidance");
        UnsafeAtTick = unsafeAtTick;
        Controls controls = MoveTo(live, targetFeet);
        // The avoidance is the owner of this tick's body whatever the ordinary search concluded.
        Fail(MovementFailure.Preempted, "threat-avoidance");
        return controls;
    }

    /// <summary>
    /// Ends the retained route. <paramref name="ending"/> says who ended it: a voluntary release or
    /// method change is <see cref="AttemptEnding.Cancelled"/> and clears the failure verdict, while
    /// another owner taking the body is <see cref="AttemptEnding.Preempted"/> and is recorded as the
    /// reason the goal is not being delivered. Neither is a physical failure or retires route memory.
    /// </summary>
    public void Interrupt(BodyState live, AttemptEnding ending = AttemptEnding.Cancelled, string cause = "released")
    {
        bool preempted = ending == AttemptEnding.Preempted;
        // A voluntary release cannot stop a move in the air or inside its run-up: it is held until
        // the move lands and applied by ContinueCommitted, which keeps steering meanwhile. The
        // caller reads ReleasePending to know it must ask for those controls. A pre-emption is
        // never deferred, and it withdraws any release that was waiting, because the pre-empting
        // owner is about to supply the body's controls itself.
        if (!preempted && Committed(live))
        {
            if (pendingRelease == null) { DeferredReleases++; BehaviourCensus.ReleaseDeferred(onStep!.Value); }
            pendingRelease = cause;
            return;
        }
        pendingRelease = null;
        Status = ExecutionStatus.Idle;
        if (onStep is NavStep step)
            Report(step, ticksOnStep, TraversalFault.Interrupted, preempted ? AttemptEnding.Preempted : AttemptEnding.Cancelled,
                preempted ? MovementFailure.Preempted : MovementFailure.None, cause);
        if (preempted) Fail(MovementFailure.Preempted, cause);
        else LastFailure = null;
        Path = null;
        onStep = null;
        execution = null;
        stepFaulted = false;
        forceReplan = false;
        clearance.Clear();
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
        SearchId++;
        Point start = live.FeetTile;
        GoalTile = goal;
        ticksSincePlan = 0;
        forceReplan = false;
        PlannedThisTick = true;
        clearance.Clear();
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
            Fail(MovementFailure.UnusableTerminal, "no-standable-start");
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
            search?.Dispose();
            searchLava = AStar.AllowLava; searchOneWay = AStar.AllowOneWayDrops;
            searchCapabilities = Capabilities;
            search = new ContinueRouteSearch(from.Value, goal, searchLava, searchOneWay,
                live.Pose, step => !EntryExcludedFromSearch(step, live));
            search.Advance(PlanBudget, PlanMsBudget);
            Path = search.Result();
            used = search.Expansions;
            LastSearchStop = search.Stop;
        }
        finally
        {
            // These prices belong to this navigator's query. Leaving them in the caller's list
            // grows it on every retry and prevents expired failures from ever leaving it.
            AStar.Avoid.RemoveRange(externalAvoidCount, AStar.Avoid.Count - externalAvoidCount);
        }
        LastPlanMs = watch.Elapsed.TotalMilliseconds;
        LastExpansions = used;
        SearchExpansions = used;
        ExperienceRoutesUsed = search?.ExperienceRoutesUsed ?? 0;
        SearchPending = search is { Finished: false };
        ProgressReason = SearchPending ? "search-incomplete" : Path != null ? "route-available"
            : LastSearchStop == AStar.SearchStopReason.Exhausted ? "model-exhausted" : "search-limit";
        if (Path != null)
        {
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
        // An executable prefix is incomplete, not failed. Path.Partial and SearchPending carry
        // that distinction while step outcomes establish whether the body makes real progress.
        LastPlanFailed = Path == null && !SearchPending;
        LastPlanEmpty = Path == null;
        ClassifySearch();
        if (LastPlanFailed)
            BehaviourCensus.PlanFailed();
        if (LastPlanFailed)
            PlanFailed?.Invoke(from.Value, goal, Path?.Goal, used, Path == null ? "no path" : "partial path");
    }

    private void AdvanceSearch(BodyState live, bool publish)
    {
        var query = search!;
        if (searchLava != AStar.AllowLava || searchOneWay != AStar.AllowOneWayDrops)
        { query.Dispose(); search = null; forceReplan = true; SearchPending = false; return; }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int before = query.Expansions;
        query.Advance(PlanBudget, PlanMsBudget);
        // The failed-plan retry waits from the answer, not from when the question was asked: a
        // query that took longer than the wait to finish would otherwise be retried the moment it
        // answered.
        if (query.Finished && query.Stop != AStar.SearchStopReason.Found)
            ticksSincePlan = Math.Min(ticksSincePlan, 0);
        LastPlanMs = watch.Elapsed.TotalMilliseconds;
        LastExpansions = query.Expansions - before;
        SearchExpansions = query.Expansions;
        ExperienceRoutesUsed = query.ExperienceRoutesUsed;
        SearchPending = !query.Finished;
        if (Path == null)
            LastPlanFailed = !SearchPending && query.Stop != AStar.SearchStopReason.Found;
        LastSearchStop = query.Stop;
        ProgressReason = SearchPending ? "search-incomplete" : query.Stop == AStar.SearchStopReason.Found ? "route-available"
            : query.Stop == AStar.SearchStopReason.Exhausted ? "model-exhausted" : "search-limit";
        ClassifySearch();
        // A result is attached only where the body actually joins it. A proof rooted at an
        // earlier pose cannot be installed as if the companion had stayed there while searching.
        if (!publish || !live.OnGround) return;
        NavPath? result = query.Result();
        if (result == null) return;
        int join = result.Steps.FindIndex(step => step.From == live.FeetTile);
        if (join < 0) return;
        if (Path is { Finished: false } old && !old.Partial) return;
        if (onStep is NavStep active && result.Steps[join] != active)
        { Report(active, ticksOnStep, TraversalFault.Interrupted, AttemptEnding.Cancelled, reason: "route-replaced"); onStep = null; execution = null; }
        result.Index = join;
        Path = result;
        LastPlanFailed = false;
        LastPlanEmpty = false;
        PlannedThisTick = true;
        ClassifySearch();
    }

    /// <summary>
    /// The search's half of the verdict. A route still being walked is delivery, whatever the
    /// query behind it is doing. Without one, a query still running or stopped on a limit has
    /// established nothing (unfinished), a query that ran out of transitions has established that
    /// the model holds no way there (absent), and a body with no standable node under it cannot be
    /// planned from at all. Physical verdicts from a step are left alone: a search that found no
    /// replacement does not explain away the fault that ended the last attempt.
    /// </summary>
    private void ClassifySearch()
    {
        if (Path is { Finished: false })
        {
            if (LastFailure?.Kind is MovementFailure.AbsentTransition or MovementFailure.UnfinishedSearch)
                LastFailure = null;
            return;
        }
        // The replan after a fault resets the step before searching, and the search it runs may find
        // nothing precisely because that step's entry is now excluded; the fault is the better
        // explanation until another attempt begins. A pre-emption is not protected: once its owner
        // releases the body, the search is what stands between the body and the goal.
        // A physical verdict is evidence only about the terrain it was found on, so an announced
        // change hands the explanation back to the search even when no new attempt has begun.
        if (LastFailure is { Kind: MovementFailure.InvalidActualEntry or MovementFailure.NativeMismatch or MovementFailure.UnusableTerminal } physical
            && physical.AttemptId == AttemptId && physical.Step != null
            && failureWorld == NavGrid.World && failureRevision == NavGrid.World.Revision)
            return;
        if (SearchPending)
            Fail(MovementFailure.UnfinishedSearch, "search-incomplete");
        else if (LastSearchStop == AStar.SearchStopReason.Exhausted)
            Fail(MovementFailure.AbsentTransition, "model-exhausted");
        else if (LastSearchStop == AStar.SearchStopReason.InvalidStart)
            Fail(MovementFailure.UnusableTerminal, "no-standable-start");
        else if (LastSearchStop == AStar.SearchStopReason.Deadline)
            Fail(MovementFailure.UnfinishedSearch, "search-deadline");
        else if (LastSearchStop == AStar.SearchStopReason.ExpansionBudget)
            Fail(MovementFailure.UnfinishedSearch, "search-work-limit");
    }

    private void Fail(MovementFailure kind, string reason)
    {
        if (LastFailure is { } held && held.Kind == kind && held.Reason == reason && held.SearchId == SearchId && held.AttemptId == AttemptId)
            return;
        Record(new MovementFailureReport(kind, reason, SearchId, AttemptId, onStep));
    }

    private ITileWorld? failureWorld;
    private int failureRevision;

    private void Record(MovementFailureReport report)
    {
        LastFailure = report;
        failureWorld = NavGrid.World;
        failureRevision = NavGrid.World.Revision;
    }

    /// <summary>
    /// A step that failed after its first tick, or stalled, read against what the attempt observed.
    /// A divergence the motor attributes to an external hit is somebody else's doing; one it
    /// attributes to an announced terrain change leaves a proof about a world that no longer exists;
    /// an unattributed divergence is the one case that points at the adapter or the controls. A
    /// re-proof that fails while every tick agreed is a refused entry from the state actually reached,
    /// and a fault on the step's own terms under full agreement is a terminal state that cannot go on.
    /// </summary>
    private (AttemptEnding, MovementFailure, string) ClassifyInFlight(TraversalFault fault, bool reproof)
    {
        if (divergence == "external-hit-or-life-change")
            return (AttemptEnding.Preempted, MovementFailure.Preempted, "external-displacement:" + fault);
        if (divergence == "terrain-changed")
            return (AttemptEnding.PhysicalFailure, MovementFailure.InvalidActualEntry, "terrain-changed:" + fault);
        if (!attemptContinuous)
            return (AttemptEnding.PhysicalFailure, MovementFailure.NativeMismatch, $"prediction-diverged:{divergence}:{fault}");
        return reproof
            ? (AttemptEnding.PhysicalFailure, MovementFailure.InvalidActualEntry, $"reproof:{local.LastRejection?.Reason ?? "none"}:{fault}")
            : (AttemptEnding.PhysicalFailure, MovementFailure.UnusableTerminal, "agreed-then-faulted:" + fault);
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

    /// <summary>
    /// The search may skip a rejected first edge only where nothing after it could change the
    /// outcome: a walk arrives at the speed its successor asks for, so a walk refused beside one
    /// successor is not refused beside another. That test belongs here, where the rejection is
    /// consumed, and not where it is recorded — a rejection is a fact about the body and the
    /// terrain, and a guard that refuses to write the fact down because one consumer cannot use
    /// it leaves every other consumer with nothing.
    /// </summary>
    internal bool EntryExcludedFromSearch(NavStep step, BodyState live)
        => EntryRejected(step, live) && !For(step.Kind).EntryDependsOnNext;

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
                Report(done, ticksOnStep, TraversalFault.None, AttemptEnding.Completed);
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
            AttemptId++;
            entryState = live;
            attemptContinuous = true;
            divergence = "none";
            entryRevision = NavGrid.World.Revision;
            sweptBody = BodyBox(live);
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
            // A physical fault from the macro proof is the fact both guards below exist to answer:
            // this step, from this body, is not a move the body can make. Neither guard may ask a
            // proxy question instead, because a fallback's trigger condition has to be the fact it
            // answers rather than the absence of the ordinary path's own precondition.
            bool physicallyImpossible = fault != TraversalFault.None;
            bool atEntry = execution!.Ticks == 0;
            // Counted before anything decides what to do about it, and counted whether or not the
            // refusal goes on to strike, because the count exists to make the refusals visible at
            // all. An exempted refusal is exactly the one nothing else records.
            if (physicallyImpossible && atEntry)
                BehaviourCensus.Refused(step, fault);
            // The direct proof refuses from the exact state; preparation is what asks whether a short
            // run-in, stop or alignment makes the move. A preparation search that ran out of its allowance
            // has not answered that, so the refusal is neither remembered nor struck, whatever the direct
            // proof said — a body knocked toward a ledge faster than it walks mislands going straight off
            // and is rescued by shedding speed first, and striking every such refusal cost three to eight
            // strikes per approach, which is two strikes and a spot ban for a goal the body then reached.
            //
            // What that exemption may not be is reachable by ordinary running, and for a wall-clock
            // allowance it was: two milliseconds against a search that re-simulates a whole macro per
            // candidate prefix meant the budget was spent on essentially every jump refusal, so a refused
            // entry earned no strike, no memory and no controls, the body did not move, and the plan
            // returned the same edge for ever. The 09:28 capture of 14 September holds 684 such rejections
            // against a single jump fault, 390 of them bit-identical on one edge. The allowance is
            // counted in simulated body ticks now and sized to cover the whole search, so exhausting it
            // is a rare and reproducible fact about the move rather than a report on how busy the frame
            // was — which is what makes this exemption safe to keep.
            bool preparationSpent = local.PreparationResult == "search-budget-exhausted";
            if (physicallyImpossible && atEntry && !preparationSpent)
                RememberRejectedEntry(step, live);
            // Which contract refused the step: a preparation search that ran out has not shown the
            // entry impossible, whatever the direct proof said; a refusal with no physical fault
            // came from the threat forecast; a physical fault before the first tick is the refused
            // entry, and after it the attempt's own observations decide.
            var (ending, failure, reason) = preparationSpent
                ? (AttemptEnding.Cancelled, MovementFailure.UnfinishedSearch, "preparation-budget")
                : !physicallyImpossible ? (AttemptEnding.Preempted, MovementFailure.Preempted, "unsafe-forecast")
                : atEntry ? (AttemptEnding.PhysicalFailure, MovementFailure.InvalidActualEntry,
                    $"entry-proof:{local.LastRejection?.Reason ?? "none"}:{fault}:{local.PreparationResult}")
                : ClassifyInFlight(fault, reproof: true);
            if (fault == TraversalFault.None)
                fault = TraversalFault.Interrupted;
            stepFaulted = true;
            LastFault = fault;
            FaultCount++;
            Report(step, ticksOnStep, fault, ending, failure, reason);
            // A step refused before its first tick has failed as completely as one that failed in
            // flight, and more cheaply: the proof ran the whole move and it did not work. Pricing
            // only the in-flight failure left an entry the proof rejects being re-offered by every
            // later plan for ever, which is the 246 mislanded jumps of the 2026-09-11 session, none
            // of which the body ever flew.
            if (physicallyImpossible && !preparationSpent) Strike(step.Tile);
            forceReplan = true;
            Status = ExecutionStatus.Rejected;
            return Controls.None;
        }
        if (fault != TraversalFault.None)
        {
            stepFaulted = true;
            LastFault = fault;
            FaultCount++;
            var (ending, failure, reason) = ClassifyInFlight(fault, reproof: false);
            Report(step, ticksOnStep, fault, ending, failure, reason);
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
        // Every kind refines the same way: ask the traversal for the edges it can prove out of the
        // body's own tile and pose, and keep the ones that reach this step's tile. A jump used to
        // be the exception here, pairing the step with each entry from the profile table directly,
        // which made this a second producer of jump edges — and the only one that never ran the
        // take-off, so the steps it built carried a nominal speed with no run-up mark and no
        // proven launch point behind it. The refinement is exactly the place a step must be
        // proven hardest, because it exists to answer a proof that has already failed once.
        foreach (var edge in For(step.Kind).Candidates(new NavNode(live.FeetTile, live.Mobility), live.Pose, AStar.AllowLava))
            if (edge.Step.Tile == step.Tile) candidates.Add(edge.Step);
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

    private void Report(NavStep step, int ticks, TraversalFault outcome, AttemptEnding ending,
        MovementFailure failure = MovementFailure.None, string reason = "")
    {
        // A fault terminates an attempt even if its path survives until the next ground tick.
        // Clearing or interrupting that path must not count a second terminal outcome.
        if (edgeReported) return;
        edgeReported = true;
        LastEdge = new EdgeReport(step.Kind, step.From, step.Tile, step.Ticks, ticks, outcome);
        LastEnding = ending;
        switch (ending)
        {
            case AttemptEnding.Completed: CompletedAttempts++; break;
            case AttemptEnding.PhysicalFailure: FailedAttempts++; break;
            case AttemptEnding.Preempted: PreemptedAttempts++; break;
            default: CancelledAttempts++; break;
        }
        ProgressReason = outcome == TraversalFault.None ? "traversal-completed" : outcome == TraversalFault.Interrupted ? "traversal-interrupted" : "traversal-failed";
        EdgeCount++;
        // Only a physical failure is evidence against the connection. A knockback, a threat
        // refusal or a preparation search that ran out of time says nothing about whether the move
        // works, and retiring a remembered route for one of those poisons an edge that does.
        if (ending == AttemptEnding.Completed && attemptContinuous && entryRevision == NavGrid.World.Revision)
            RememberExecutedRoutes.World.Record(NavGrid.World, step, entryState, observed, sweptBody);
        else if (ending == AttemptEnding.PhysicalFailure)
            RememberExecutedRoutes.World.Forget(step);
        if (failure != MovementFailure.None)
            Record(new MovementFailureReport(failure, reason, SearchId, AttemptId, step));
        else if (ending == AttemptEnding.Completed)
            LastFailure = null;
        BehaviourCensus.Finished(step, outcome, ending);
    }

    private static Rectangle BodyBox(BodyState state) => new((int)Math.Floor(state.Left),
        (int)Math.Floor(state.Bottom - BodyPhysics.Height), (int)BodyPhysics.Width + 1, (int)BodyPhysics.Height + 1);

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
        pendingRelease = null;
        if (onStep is NavStep step)
            Report(step, ticksOnStep, TraversalFault.Interrupted, AttemptEnding.Cancelled, reason: "cleared");
        LastFailure = null;
        Path = null;
        GoalTile = null;
        clearance.Clear();
        search?.Dispose(); search = null; SearchPending = false;
        StuckStrikes = 0;
        answerWaitTicks = 0;
        PlannedThisTick = false;
        onStep = null;
        execution = null;
        forceReplan = false;
    }
}
