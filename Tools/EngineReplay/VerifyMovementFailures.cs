using System.Reflection;
using System.Text.RegularExpressions;
using AICompanion.Companion.Brain.SharedMovementSystem;
using Microsoft.Xna.Framework;
using Terraria;

/// <summary>
/// P07's native movement-failure contract: a held goal the native body can reach is delivered, and
/// one it cannot is refused with the class the evidence names. Each case drives the shared movement
/// coordinator against Terraria's own NPC collision and is built in a pair whose halves differ in
/// exactly the fact the class depends on, so a classification that merged the two would fail one
/// half. The wall-clock planning allowances are lifted except where a deadline is the subject, and
/// route memory and terrain are rebuilt per case, so results do not depend on the machine or on the
/// fixture that ran before.
/// </summary>
internal static class VerifyMovementFailures
{
    public static int Run()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        // The brain fixtures that run before this one leave the production allowances configured,
        // which would make every route here depend on machine load.
        double planBudget = Navigator.PlanMsBudget, preparationBudget = PlanLocalMovement.PreparationMsBudget;
        LimitPlanningWork.End();
        Navigator.PlanMsBudget = 0;
        PlanLocalMovement.PreparationMsBudget = 0;
        int failed = 0;
        try
        {
            failed += Case("search deadline is unfinished and never absent", SearchDeadlineIsUnfinishedNeverAbsent);
            failed += Case("terrain churn cannot hold off stall escalation while an answer is awaited", TerrainChurnCannotHoldOffEscalation);
            failed += Case("away-first detour delivers, sealed twin is absent, reopening delivers", AwayFirstDetourAndSealedTwin);
            failed += Case("ledge and lip entries from varied actual states all deliver", LedgeAndLipEntriesDeliver);
            failed += Case("an entry refused only because preparation ran out of time neither closes the next search nor strikes", SpentPreparationDoesNotCloseTheNextSearch);
            failed += Case("unattributed divergence is native mismatch, an external hit is pre-emption", DivergenceIsAttributedBeforeBlame);
            failed += Case("unannounced wall is a refused entry, announced wall is absent", UnannouncedWallIsRefusedEntry);
            failed += Case("interruptions score as pre-empted or cancelled, never failed", InterruptionsAreNotFailures);
        }
        finally
        {
            Navigator.PlanMsBudget = planBudget;
            PlanLocalMovement.PreparationMsBudget = preparationBudget;
        }
        Console.WriteLine(failed == 0
            ? "movement failures: deadline, detour, entry sweep, divergence attribution, stale terrain and interruption endings pass"
            : $"movement failures: {failed} case(s) failed");
        return failed;
    }

    internal static int Case(string name, Action test, string family = "movement failure")
    {
        try
        {
            test();
            Console.WriteLine($"PASS {family} {name}");
            return 0;
        }
        catch (InvalidOperationException error)
        {
            Console.WriteLine($"FAIL {family} {name}: {error.Message}");
            return 1;
        }
    }

    // ── cases ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// E08's timeout: one geometry, two allowances. A search that has to stop after a single unit of
    /// work per tick has established nothing, so while it runs without a route the verdict must be
    /// unfinished, never absent and never a failed plan, and retained work must still deliver. The
    /// sealed twin shows absent is reachable under the same starvation, but only once the frontier is
    /// actually exhausted.
    /// </summary>
    private static void SearchDeadlineIsUnfinishedNeverAbsent()
    {
        foreach (bool starved in new[] { false, true })
        {
            BuildCorridor(sealGoal: false);
            var run = new Drive(new Point(22, 89));
            Navigator.PlanMsBudget = starved ? .000001 : 0;
            try
            {
                int firstRoute = -1, unfinishedTicks = 0;
                long searches = 0;
                for (; run.Tick < 6000 && !run.Arrived; )
                {
                    run.Step(new Point(40, 89));
                    var nav = run.Movement.Navigator;
                    searches = nav.SearchId;
                    if (firstRoute < 0 && nav.Path != null) firstRoute = run.Tick;
                    if (nav.Path == null && nav.SearchPending)
                    {
                        unfinishedTicks++;
                        Require(nav.Failure == MovementFailure.UnfinishedSearch,
                            $"starved={starved}: a running search without a route must read unfinished, got {nav.Failure} ({nav.LastFailure?.Reason}) at tick {run.Tick}");
                        Require(!nav.LastPlanFailed, $"starved={starved}: a running search is not a failed plan (tick {run.Tick})");
                    }
                }
                Console.WriteLine($"   deadline starved={starved}: arrived={run.Arrived} at {run.Tick}, first route {firstRoute}, unfinished ticks {unfinishedTicks}, searches {searches}, seen {run.SeenText}");
                Require(run.Arrived, $"starved={starved}: retained search must deliver a reachable goal; last {run.Describe()}");
                Require(!run.Seen.Contains(MovementFailure.AbsentTransition), $"starved={starved}: a reachable goal was called absent");
                Require(!starved || unfinishedTicks > 0, "the starved run finished its search in one tick, so it proves nothing about deadlines");
            }
            finally { Navigator.PlanMsBudget = 0; }

            BuildCorridor(sealGoal: true);
            var sealedRun = new Drive(new Point(22, 89));
            Navigator.PlanMsBudget = starved ? .000001 : 0;
            try
            {
                bool absent = false;
                for (; sealedRun.Tick < 8000 && !absent; )
                {
                    sealedRun.Step(new Point(44, 89));
                    var nav = sealedRun.Movement.Navigator;
                    if (nav.Failure != MovementFailure.AbsentTransition) continue;
                    absent = true;
                    Require(!nav.SearchPending && nav.LastSearchStop == AStar.SearchStopReason.Exhausted,
                        $"starved={starved}: absent must follow an exhausted search, got pending={nav.SearchPending} stop={nav.LastSearchStop}");
                    // One search to walk the partial route's end and one from there: retained work
                    // restarted by a stall or by its own answer shows up here as dozens.
                    Require(nav.SearchId <= 3, $"starved={starved}: retained search was restarted {nav.SearchId} times before it could answer");
                }
                Console.WriteLine($"   deadline sealed starved={starved}: absent={absent} at {sealedRun.Tick}, seen {sealedRun.SeenText}");
                Require(absent, $"starved={starved}: a sealed goal must end absent; last {sealedRun.Describe()}");
                Require(sealedRun.Movement.Navigator.FailedAttempts == 0, $"starved={starved}: a closed model is not a physical failure");
            }
            finally { Navigator.PlanMsBudget = 0; }
        }
    }

    /// <summary>
    /// A slow answer is not a stall, but only for so long. A distant tile changed and announced every
    /// twenty ticks invalidates a retained search before a search starved to one work unit per tick can
    /// answer, which is what a player mining beside the companion does. With the goal sealed the body
    /// stands at the wall with no route and a search that never finishes: it must still take a strike
    /// within the answer-wait bound plus the stall threshold, or the spot ban and regroup's travel
    /// pressure never arrive. With the goal open, the same churn must not stop delivery, because each
    /// short search publishes a route the body walks.
    /// </summary>
    private static void TerrainChurnCannotHoldOffEscalation()
    {
        int bound = AICompanion.Companion.Brain.BehaviourSelection.Weights.RouteAnswerWaitTicks;
        foreach (bool sealGoal in new[] { true, false })
        {
            BuildCorridor(sealGoal);
            Point goal = sealGoal ? new Point(44, 89) : new Point(40, 89);
            var run = new Drive(new Point(22, 89));
            Navigator.PlanMsBudget = .000001;
            try
            {
                int stoppedAt = -1, firstStrike = -1, maxWait = 0;
                Point lastFeet = run.Body.FeetTile;
                int limit = sealGoal ? 3000 + bound : 6000;
                for (; run.Tick < limit && !run.Arrived; )
                {
                    if (run.Tick % 20 == 10)
                    {
                        if (Main.tile[90, 50].HasTile) Clear(90, 50); else Solid(90, 50);
                        TerrainChanges.Changed(90, 50);
                    }
                    run.Step(goal);
                    var nav = run.Movement.Navigator;
                    maxWait = Math.Max(maxWait, nav.AnswerWaitTicks);
                    if (run.Body.FeetTile != lastFeet) { lastFeet = run.Body.FeetTile; stoppedAt = -1; }
                    else if (stoppedAt < 0) stoppedAt = run.Tick;
                    if (firstStrike < 0 && nav.StuckStrikes > 0) firstStrike = run.Tick;
                    if (sealGoal && nav.StuckStrikes >= 2) break;
                }
                var n = run.Movement.Navigator;
                Console.WriteLine($"   churn sealed={sealGoal}: arrived={run.Arrived} tick {run.Tick}, stood from {stoppedAt}, first strike {firstStrike}, strikes {n.StuckStrikes}, searches {n.SearchId}, longest answer wait {maxWait}, failure {n.LastFailure}");
                if (sealGoal)
                {
                    Require(firstStrike >= 0 && n.StuckStrikes >= 2,
                        $"a body kept waiting by terrain churn never escalated: strikes {n.StuckStrikes} after {run.Tick} ticks and {n.SearchId} searches");
                    Require(firstStrike - stoppedAt <= bound + 60,
                        $"the first strike came {firstStrike - stoppedAt} ticks after the body stood, past the {bound}-tick answer wait and the stall threshold");
                }
                else
                    Require(run.Arrived, $"churn must not stop delivery of an open goal; last {run.Describe()}");
            }
            finally { Navigator.PlanMsBudget = 0; }
        }
    }

    /// <summary>
    /// K03 and the delivery half of "native-valid held goal": the only way off the shelf leaves it
    /// away from the goal. The route must be taken without any failure verdict after carrying the body
    /// well away from the goal, and the sealed twin (one column closing the exit) must produce absent
    /// rather than silence, then deliver once the column is removed and announced.
    /// </summary>
    private static void AwayFirstDetourAndSealedTwin()
    {
        BuildShelf(sealedExit: false);
        var run = new Drive(new Point(50, 69));
        Point goal = new(70, 79);
        // The discriminating fact is where the body went, not the sign of its first control: a
        // run-up or an entry alignment can step toward the goal before the route leaves away from it.
        int startColumn = run.Body.FeetTile.X, farthestAway = startColumn;
        while (run.Tick < 900 && !run.Arrived)
        {
            run.Step(goal);
            farthestAway = Math.Min(farthestAway, run.Body.FeetTile.X);
        }
        Console.WriteLine($"   detour: arrived={run.Arrived} at {run.Tick}, went {startColumn - farthestAway} columns away first, attempts {run.Endings}, seen {run.SeenText}");
        Require(startColumn - farthestAway >= 8, $"the detour fixture must carry the body well away from the goal before delivering it, went {startColumn - farthestAway} columns");
        Require(run.Arrived, $"the away-first route must be delivered; last {run.Describe()}");
        Require(run.Seen.All(kind => kind is MovementFailure.None or MovementFailure.UnfinishedSearch),
            $"an away-first detour produced a failure verdict: {run.SeenText}");
        Require(run.Movement.Navigator.FailedAttempts == 0, "an away-first detour must not fail an attempt");

        BuildShelf(sealedExit: true);
        var sealedRun = new Drive(new Point(50, 69));
        int absentAt = -1;
        while (sealedRun.Tick < 300)
        {
            sealedRun.Step(goal);
            if (absentAt < 0 && sealedRun.Movement.Navigator.Failure == MovementFailure.AbsentTransition) absentAt = sealedRun.Tick;
            if (absentAt >= 0 && sealedRun.Tick > absentAt + 60) break;
        }
        Console.WriteLine($"   sealed shelf: absent at {absentAt}, final {sealedRun.Movement.Navigator.Failure}, feet {sealedRun.Body.FeetTile}, seen {sealedRun.SeenText}");
        Require(absentAt >= 0, $"the sealed shelf must produce an explicit absent verdict; last {sealedRun.Describe()}");
        Require(sealedRun.Movement.Navigator.Failure == MovementFailure.AbsentTransition, "the absent verdict must hold while nothing changes");
        Require(sealedRun.Movement.Navigator.FailedAttempts == 0, "a closed exit is not a physical failure of any attempt");
        Require(sealedRun.Body.FeetTile.Y == 69, "the sealed shelf body must still be on the shelf");

        for (int y = 64; y <= 70; y++) Clear(39, y);
        for (int y = 64; y <= 70; y++) TerrainChanges.Changed(39, y);
        int reopened = sealedRun.Tick;
        while (sealedRun.Tick < reopened + 900 && !sealedRun.Arrived) sealedRun.Step(goal);
        Console.WriteLine($"   reopened shelf: arrived={sealedRun.Arrived} after {sealedRun.Tick - reopened}, final {sealedRun.Movement.Navigator.Failure}");
        Require(sealedRun.Arrived, $"an announced opening must reopen the absent goal; last {sealedRun.Describe()}");
        Require(sealedRun.Movement.Navigator.Failure == MovementFailure.None, "arrival must clear the failure verdict");
    }

    /// <summary>
    /// E10's actual-entry sweep over one two-tile ledge and its lip: the body starts at several
    /// columns (down to flush against the ledge, where there is no runway), sub-tile offsets and
    /// horizontal velocities, climbing up and descending back. Every entry is native-valid because a
    /// body can always stop and prepare, so every one must arrive; the failures on the way are
    /// printed rather than forbidden, since a refused first proof rescued by preparation is correct.
    /// </summary>
    private static void LedgeAndLipEntriesDeliver()
    {
        var failures = new List<string>();
        int cases = 0, rescued = 0;
        foreach (bool climb in new[] { true, false })
        foreach (int column in climb ? new[] { 28, 31, 33, 34 } : new[] { 35, 36, 38 })
        foreach (float offset in new[] { 1f, 6f, 11f })
        foreach (float vx in new[] { -3.5f, 0f, 3.5f })
        {
            BuildLedge();
            float left = column * 16 + offset - 2f;
            left = climb ? MathF.Min(left, 35 * 16 - BodyPhysics.Width) : MathF.Max(left, 35 * 16);
            float bottom = climb ? 90 * 16 : 88 * 16;
            var run = new Drive(new BodyState(left, bottom, vx, 0f, true, Capabilities: MovementCapabilities.Basic));
            Point goal = climb ? new Point(40, 87) : new Point(30, 89);
            while (run.Tick < 600 && !run.Arrived) run.Step(goal);
            cases++;
            var nav = run.Movement.Navigator;
            if (nav.FailedAttempts > 0 && run.Arrived) rescued++;
            if (!run.Arrived)
                failures.Add($"{(climb ? "climb" : "descend")} col={column} off={offset} vx={vx}: {run.Describe()} seen {run.SeenText}");
        }
        Console.WriteLine($"   entry sweep: {cases - failures.Count}/{cases} delivered, {rescued} delivered after a failed attempt");
        Require(failures.Count == 0, "undelivered native-valid entries:\n     " + string.Join("\n     ", failures));
    }

    /// <summary>
    /// A refused entry is remembered, and struck, because the proof showed the body cannot make the move
    /// from where it stands. When the direct proof refuses and the preparation search that could have found
    /// a way in stops on its time allowance, nothing has shown the move impossible. The shelf's exit end sits
    /// under a low ceiling so its drop is the only way off the end tile. A body arriving at the edge faster
    /// than it walks, as a knock sends it, misses the drop's landing when it goes straight off and is rescued
    /// by a prefix that sheds speed first; those states are found with preparation unbounded. Each is then
    /// driven with preparation starved, where every refusal is a spent budget and must neither record a
    /// rejected entry, strike, nor let a search end absent, and the goal must still be delivered, with the
    /// allowance lifted if the starved body never got there.
    /// </summary>
    private static void SpentPreparationDoesNotCloseTheNextSearch()
    {
        Point goal = new(70, 79);
        var rescued = new List<BodyState>();
        int swept = 0;
        foreach (int column in new[] { 40, 41 })
        foreach (float offset in new[] { 1f, 6f, 11f })
        foreach (float vx in new[] { -8f, -6f })
        {
            BuildLowShelf();
            var start = new BodyState(column * 16 + offset - 2f, 70 * 16, vx, 0f, true, Capabilities: MovementCapabilities.Basic);
            var probe = new Drive(start);
            bool prepared = false;
            while (probe.Tick < 600 && !probe.Arrived)
            {
                probe.Step(goal);
                var nav = probe.Movement.Navigator;
                if (nav.Path is { Finished: false } path && path.Current.Kind is MoveKind.Drop or MoveKind.Jump or MoveKind.FallThrough
                    && nav.PreparationResult.StartsWith("validated-prefix"))
                    prepared = true;
            }
            swept++;
            if (prepared && probe.Arrived) rescued.Add(start);
        }
        Console.WriteLine($"   spent preparation: {rescued.Count} of {swept} shelf-end states need preparation for their exit and are delivered with it");
        Require(rescued.Count > 0, "no shelf-end state needed preparation for its exit, so this case proves nothing about a spent preparation budget");

        var failures = new List<string>();
        foreach (BodyState start in rescued)
        {
            BuildLowShelf();
            var run = new Drive(start);
            var nav = run.Movement.Navigator;
            int refusals = 0, struckRefusals = 0, remembered = 0;
            PlanLocalMovement.PreparationMsBudget = .000001;
            try
            {
                while (run.Tick < 300 && !run.Arrived)
                {
                    int strikes = nav.StuckStrikes, faults = nav.FaultCount;
                    run.Step(goal);
                    if (nav.FaultCount == faults || nav.LastFailure?.Reason != "preparation-budget") continue;
                    refusals++;
                    if (nav.PreparationResult != "search-budget-exhausted")
                        failures.Add($"left={start.Left} vx={start.Vx}: a preparation-budget verdict with preparation result {nav.PreparationResult}");
                    if (nav.StuckStrikes > strikes) struckRefusals++;
                    if (nav.LastRejection is { } rejection && nav.EntryRejected(rejection.Step, rejection.Entry)) remembered++;
                }
            }
            finally { PlanLocalMovement.PreparationMsBudget = 0; }
            var seen = run.SeenText;
            bool absent = run.Seen.Contains(MovementFailure.AbsentTransition);
            bool starvedArrival = run.Arrived;
            int lifted = run.Tick;
            while (run.Tick < lifted + 900 && !run.Arrived) run.Step(goal);
            Console.WriteLine($"   spent preparation left={start.Left} vx={start.Vx}: {refusals} budget refusals, {struckRefusals} struck, {remembered} remembered, seen {seen}; arrived={run.Arrived} ({(starvedArrival ? $"while starved, tick {lifted}" : $"{run.Tick - lifted} ticks after the allowance was lifted")})");
            if (refusals == 0) failures.Add($"left={start.Left} vx={start.Vx}: no refusal on a spent preparation budget was observed");
            if (struckRefusals > 0) failures.Add($"left={start.Left} vx={start.Vx}: {struckRefusals} refusals on a spent budget struck the step");
            if (remembered > 0) failures.Add($"left={start.Left} vx={start.Vx}: {remembered} refusals on a spent budget were remembered as rejected entries");
            if (absent) failures.Add($"left={start.Left} vx={start.Vx}: a spent preparation budget made a reachable goal absent");
            if (!run.Arrived) failures.Add($"left={start.Left} vx={start.Vx}: with the allowance lifted the goal was not delivered; last {run.Describe()}");
        }
        Require(failures.Count == 0, string.Join("\n     ", failures));
    }

    /// <summary>
    /// The native-mismatch class is only honest if an external cause the motor knows about is kept
    /// out of it. One scene, a body pinned mid-walk by a position write, is run twice: unattributed,
    /// which must read native mismatch, fail the attempt and retire the remembered connection it was
    /// walking; and attributed to an external hit, which must read pre-emption, fail nothing and keep
    /// the connection. The walk is first completed once so the connection exists to be kept or lost.
    /// </summary>
    private static void DivergenceIsAttributedBeforeBlame()
    {
        foreach (string cause in new[] { "none", "external-hit-or-life-change" })
        {
            BuildCorridor(sealGoal: false);
            Point goal = new(34, 89);
            var learn = new Drive(new Point(22, 89));
            while (learn.Tick < 600 && !learn.Arrived) learn.Step(goal);
            Require(learn.Arrived, "the learning walk must arrive");
            int remembered = RememberExecutedRoutes.World.Count;
            Require(remembered > 0, "the learning walk must record executed connections");

            var run = new Drive(new Point(22, 89));
            run.Movement.Navigator.DisplacementCause = cause;
            for (int i = 0; i < 20; i++) run.Step(goal);
            BodyState pinned = run.Body;
            var nav = run.Movement.Navigator;
            while (run.Tick < 400 && nav.LastFailure == null)
                run.Step(goal, perturb: _ => pinned);
            Console.WriteLine($"   pinned cause={cause}: {nav.LastFailure}, ending {nav.LastEnding}, attempts {run.Endings}, memory {remembered}->{RememberExecutedRoutes.World.Count}");
            if (cause == "none")
            {
                Require(nav.Failure == MovementFailure.NativeMismatch && nav.LastFailure!.Value.Reason.StartsWith("prediction-diverged:unattributed"),
                    $"an unattributed divergence must read native mismatch, got {nav.LastFailure}");
                Require(nav.LastEnding == AttemptEnding.PhysicalFailure && nav.FailedAttempts == 1, "the pinned attempt must be a physical failure");
                Require(RememberExecutedRoutes.World.Count < remembered, "a physical failure must retire the connection it was walking");
            }
            else
            {
                Require(nav.Failure == MovementFailure.Preempted && nav.LastFailure!.Value.Reason.StartsWith("external-displacement"),
                    $"an attributed external hit must read pre-emption, got {nav.LastFailure}");
                Require(nav.LastEnding == AttemptEnding.Preempted && nav.FailedAttempts == 0, "an external hit must fail no attempt");
                Require(RememberExecutedRoutes.World.Count == remembered, "an external hit must not retire a working connection");
            }
        }
    }

    /// <summary>
    /// X03 at the route level. A wall placed ahead with no terrain callback leaves the edge cache
    /// believing the corridor is open while native collision disagrees; the actual-state proof must
    /// refuse the entry (the prediction used the live tiles, so this is not a mismatch). Announcing
    /// the same wall must invalidate the graph, and the goal behind it becomes absent.
    /// </summary>
    private static void UnannouncedWallIsRefusedEntry()
    {
        BuildCorridor(sealGoal: false);
        Point goal = new(40, 89);
        var run = new Drive(new Point(22, 89));
        while (run.Tick < 400 && run.Body.FeetTile.X < 27) run.Step(goal);
        int wall = run.Body.FeetTile.X + 3;
        for (int y = 70; y <= 89; y++) Solid(wall, y);
        var nav = run.Movement.Navigator;
        int placed = run.Tick;
        while (run.Tick < placed + 400 && nav.LastFailure == null) run.Step(goal);
        Console.WriteLine($"   unannounced wall at x={wall}: {nav.LastFailure} after {run.Tick - placed}, attempts {run.Endings}");
        Require(nav.Failure == MovementFailure.InvalidActualEntry, $"a stale graph edge refused by the actual-state proof must read invalid entry, got {nav.LastFailure}");
        Require(!run.Seen.Contains(MovementFailure.NativeMismatch), "a prediction made from the live tiles cannot be a native mismatch");

        for (int y = 70; y <= 89; y++) TerrainChanges.Changed(wall, y);
        int announced = run.Tick;
        while (run.Tick < announced + 900 && nav.Failure != MovementFailure.AbsentTransition) run.Step(goal);
        Console.WriteLine($"   announced wall: {nav.LastFailure} after {run.Tick - announced}, seen {run.SeenText}");
        Require(nav.Failure == MovementFailure.AbsentTransition, $"an announced full-height wall must make the goal absent, got {nav.LastFailure}");
        Require(!run.Seen.Contains(MovementFailure.NativeMismatch), "no divergence exists anywhere in this scene");
    }

    /// <summary>
    /// "Score physical completion separately from voluntary cancellation": a jump taken from the
    /// body mid-flight by threat avoidance, a walk released by the brain, a walk taken by downing and
    /// a walk taken by a state search each end one attempt with the owner's ending, fail nothing,
    /// keep route memory, and are tallied apart in the census; the pre-empted jump then resumes and
    /// is delivered.
    /// </summary>
    private static void InterruptionsAreNotFailures()
    {
        BehaviourCensus.Reset();
        BuildLedge();
        Point up = new(40, 87);
        var run = new Drive(new Point(30, 89));
        var nav = run.Movement.Navigator;
        while (run.Tick < 400 && !(nav.Path is { Finished: false } path && path.Current.Kind == MoveKind.Jump && !run.Body.OnGround))
            run.Step(up);
        Require(!run.Body.OnGround, "the fixture must reach a jump in flight before interrupting it");
        int remembered = RememberExecutedRoutes.World.Count;
        Controls avoid = run.Movement.AvoidThreats(run.Body, (_, _) => false, NavGrid.FeetWorld(up));
        run.Body = VerifyEngineMotion.RunEngine(run.Body, avoid);
        run.Tick++;
        Require(nav.LastEnding == AttemptEnding.Preempted && nav.PreemptedAttempts == 1 && nav.FailedAttempts == 0,
            $"threat avoidance mid-jump must pre-empt the jump, got ending {nav.LastEnding}, {run.Endings}");
        Require(nav.Failure == MovementFailure.Preempted && nav.LastFailure!.Value.Reason == "threat-avoidance",
            $"the verdict must name the pre-emption, got {nav.LastFailure}");
        while (run.Tick < 900 && !run.Arrived) run.Step(up);
        Require(run.Arrived && nav.Failure == MovementFailure.None, $"the pre-empted climb must resume and deliver; last {run.Describe()}");
        Require(nav.FailedAttempts == 0, $"no attempt in an interrupted climb failed physically: {run.Endings}");
        Require(RememberExecutedRoutes.World.Count >= remembered, "a pre-emption must not retire remembered connections");

        BuildCorridor(sealGoal: false);
        Point along = new(40, 89);
        var walk = new Drive(new Point(22, 89));
        var walker = walk.Movement.Navigator;
        void WalkUntilStepInHand()
        {
            for (int i = 0; i < 12 || walker.Path is not { Finished: false }; i++) walk.Step(along);
        }
        WalkUntilStepInHand();
        int cancelled = walker.CancelledAttempts;
        walk.Movement.Hold(walk.Body);
        Require(walker.LastEnding == AttemptEnding.Cancelled && walker.CancelledAttempts == cancelled + 1 && walker.Failure == MovementFailure.None,
            $"a released request must cancel its attempt and clear the verdict, got {walker.LastEnding} {walker.LastFailure}");
        WalkUntilStepInHand();
        int preempted = walker.PreemptedAttempts;
        walk.Movement.Hold(walk.Body, preemptedBy: "downed");
        Require(walker.LastEnding == AttemptEnding.Preempted && walker.PreemptedAttempts == preempted + 1
            && walker.LastFailure is { Kind: MovementFailure.Preempted, Reason: "downed" },
            $"downing must pre-empt the walk, got {walker.LastEnding} {walker.LastFailure}");
        WalkUntilStepInHand();
        walk.Movement.SeekState(walk.Body, _ => false, _ => 0f, 8, out _, out _);
        Require(walker.LastEnding == AttemptEnding.Preempted && walker.PreemptedAttempts == preempted + 2
            && walker.LastFailure is { Kind: MovementFailure.Preempted, Reason: "state-search" },
            $"a state search must pre-empt the walk, got {walker.LastEnding} {walker.LastFailure}");
        Require(walker.FailedAttempts == 0, $"no interruption may count as a physical failure: {walk.Endings}");

        string census = BehaviourCensus.Report();
        int section = census.IndexOf("attempts by who ended them", StringComparison.Ordinal);
        Require(section >= 0, "the census must print attempt endings by owner");
        (int completed, int failedCount, int preemptedCount, int cancelledCount) Row(string kind)
        {
            Match m = Regex.Match(census[section..], $@"^\s+{kind}\s+([\d,]+)\s+([\d,]+)\s+([\d,]+)\s+([\d,]+)", RegexOptions.Multiline);
            Require(m.Success, $"census endings row for {kind} missing");
            int N(int g) => int.Parse(m.Groups[g].Value.Replace(",", ""));
            return (N(1), N(2), N(3), N(4));
        }
        var jump = Row("Jump");
        var walkRow = Row("Walk");
        Console.WriteLine($"   census endings: Jump {jump}, Walk {walkRow}");
        // The pre-empted body may finish its arc onto the ledge and arrive without a new jump step, so
        // a completed jump is not required; the pre-emption row is the contract.
        Require(jump.preemptedCount >= 1, $"census must show the pre-empted jump: {jump}");
        Require(walkRow.preemptedCount >= 2 && walkRow.cancelledCount >= 1, $"census must separate pre-empted from cancelled walks: {walkRow}");
        Require(jump.failedCount == 0 && walkRow.failedCount == 0, "census must record no physical failures for interruptions");
    }

    // ── driver ───────────────────────────────────────────────────────────────────────────────

    internal sealed class Drive
    {
        public readonly CoordinateMovement Movement = new();
        public BodyState Body;
        public int Tick;
        public readonly HashSet<MovementFailure> Seen = new();

        public Drive(Point feet)
            : this(BodyState.Standing(NavGrid.StandAt(feet.X, feet.Y, false) ?? throw new InvalidOperationException($"fixture start {feet} is not standable")))
        { }

        public Drive(BodyState body) => Body = body;

        public bool Arrived => Movement.Navigator.Arrived;
        public string SeenText => string.Join(",", Seen);
        public string Endings => $"completed {Movement.Navigator.CompletedAttempts}, failed {Movement.Navigator.FailedAttempts}, pre-empted {Movement.Navigator.PreemptedAttempts}, cancelled {Movement.Navigator.CancelledAttempts}";

        /// <summary>One tick: the coordinator decides from the live body, then native collision advances
        /// it, unless <paramref name="perturb"/> replaces the engine's result (a write the motor did not make).</summary>
        public Controls Step(Point goal, Func<BodyState, BodyState>? perturb = null)
        {
            Movement.Configure((uint)Tick, false, true);
            Controls controls = Movement.MoveTo(Body, NavGrid.FeetWorld(goal));
            Seen.Add(Movement.Navigator.Failure);
            BodyState next = VerifyEngineMotion.RunEngine(Body, controls);
            Body = perturb?.Invoke(next) ?? next;
            Tick++;
            return controls;
        }

        public string Describe()
        {
            var nav = Movement.Navigator;
            return $"tick {Tick} feet {Body.FeetTile} status {nav.Status} failure {nav.LastFailure} stop {nav.LastSearchStop} pending {nav.SearchPending} path {(nav.Path == null ? "none" : $"{nav.Path.Index}/{nav.Path.Steps.Count} partial={nav.Path.Partial}")} edge {nav.LastEdge} {Endings}";
        }
    }

    // ── native geometry ──────────────────────────────────────────────────────────────────────

    internal static void NewWorld(int floorRow)
    {
        Main.maxTilesX = Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)100, (ushort)100 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 95; x++) Solid(x, floorRow);
    }

    internal static void Finish()
    {
        TerrainChanges.Reset();
        NavGrid.World = new GameTileWorld();
        AStar.InvalidateEdges();
        RememberExecutedRoutes.World.Clear();
    }

    /// <summary>A floor at row 90 walled at columns 19 and 60, so the reachable region is small and
    /// exhaustible; <paramref name="sealGoal"/> adds a full-height wall at column 42, leaving the
    /// columns beyond it unreachable.</summary>
    private static void BuildCorridor(bool sealGoal)
    {
        NewWorld(90);
        for (int y = 70; y < 90; y++) { Solid(19, y); Solid(60, y); }
        if (sealGoal) for (int y = 70; y < 90; y++) Solid(42, y);
        Finish();
    }

    /// <summary>The C-turn: a shelf under a ceiling with a wall on the goal side, whose only exit is
    /// the drop off its far end; <paramref name="sealedExit"/> closes that end with one column.</summary>
    private static void BuildShelf(bool sealedExit)
    {
        NewWorld(80);
        for (int x = 40; x <= 60; x++) { Solid(x, 70); Solid(x, 64); }
        for (int y = 65; y <= 70; y++) Solid(60, y);
        if (sealedExit) for (int y = 64; y <= 70; y++) Solid(39, y);
        Finish();
    }

    /// <summary>The open C-turn shelf with a ceiling flush over its exit end (row 66, columns 40 to 44), so the
    /// body on the end tile clears it by a few pixels and has no jump off the end: the drop is the only exit.</summary>
    private static void BuildLowShelf()
    {
        NewWorld(80);
        for (int x = 40; x <= 60; x++) { Solid(x, 70); Solid(x, 64); }
        for (int y = 65; y <= 70; y++) Solid(60, y);
        for (int x = 40; x <= 44; x++) Solid(x, 66);
        Finish();
    }

    /// <summary>The native route suite's two-tile ledge: a floor at row 90 and a block from column 35.</summary>
    private static void BuildLedge()
    {
        NewWorld(90);
        for (int x = 35; x < 65; x++) { Solid(x, 88); Solid(x, 89); }
        Finish();
    }

    internal static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = true;
        tile.TileType = 1;
    }

    internal static void Clear(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
