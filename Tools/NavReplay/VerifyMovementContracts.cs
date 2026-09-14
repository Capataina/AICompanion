using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;

internal static class VerifyMovementContracts
{
    public static int Run()
    {
        var world = new FloorWorld();
        NavGrid.World = world;
        AStar.InvalidateEdges();
        var live = new BodyState(100, 160, 0, 0, true);
        var local = new PlanLocalMovement();
        var target = new Vector2(300, 160);
        Require(local.Choose(world, live, target, new Controls(4, Jump: true), MovementCapabilities.Basic, (_, _) => true) == Controls.None,
            "all unsafe candidates must not execute the preferred jump");
        Controls first = local.Choose(world, live, target, new Controls(4), MovementCapabilities.Basic);
        live = BodyMotion.Step(world, live, first);
        Require(local.Choose(world, live, target, new Controls(4), MovementCapabilities.Basic, (_, _) => true) == Controls.None,
            "new threats invalidate retained repair controls");

        var capabilities = new MovementCapabilities(AirJumpCount: 2);
        var airborne = new BodyState(100, 100, 0, 1, false, Mobility: new MobilityState(2), Capabilities: capabilities);
        var one = BodyMotion.Step(world, airborne, new Controls(1, Jump: true));
        Require(one.Mobility.AirJumpsLeft == 1 && one.Capabilities == capabilities, "an air jump consumes one resource and preserves the kit");
        var two = BodyMotion.Step(world, one, new Controls(1, Jump: true));
        Require(two.Mobility.AirJumpsLeft == 0 && two.Capabilities == capabilities, "a chained jump consumes the remaining resource");
        var dry = BodyMotion.Step(world, live with { Wet = true }, Controls.None);
        Require(!dry.Wet, "leaving liquid cannot retain wet state forever in text replay");

        var policy = new CountingTraversal();
        var step = new NavStep(new Point(20, 9), MoveKind.Walk);
        var execution = new TraversalExecution(policy, step, null);
        var copy = execution.Copy();
        Controls copyFirst = copy.NextControls(live, out _);
        copy.NextControls(live, out _);
        Require(execution.NextControls(live, out _) == copyFirst, "probing a copied macro must not advance live policy state");
        Require(execution.Ticks == 1 && copy.Ticks == 2, "macro copies own their clocks");

        var walk = new WalkTraversal();
        walk.Begin(step);
        TraversalFault fault = TraversalFault.None;
        for (int tick = 0; tick < 60; tick++) fault = walk.Check(live, step, tick);
        Require(fault == TraversalFault.Stuck, "a walk with no displacement must report a fault");

        // A walk performs a walk and never a jump. Every rise a walk edge can hold is one tile at
        // most, because the edge is proven by driving the body and the only lift in that
        // simulation is the motor's StepUp; so a body that reads as needing more has left what the
        // proof described, and the answer to that is the fault above and a replan from the live
        // state, not a move this traversal invents for itself. The state below is exactly the one
        // that used to raise one: the walker measured the rise from the *live* bottom rather than
        // from the pose the edge was proven at, so a body sitting low inside its own tile — which
        // is what a floor slope does, the body resting partway down the diagonal — read a one-row
        // step as a two-tile rise and hopped it. On run 4's block 15 that cost 34 ticks against a
        // proven 6 at the lip 3496,479 -> 3497,478, and the hop overflew the tile after it
        // (2026-09-08_13-48-44-plans-shaped.txt; Tools/Scenarios/slope-lip-one-row-up-is-a-walk-not-a-hop.txt
        // is that lip cut out, 83 ticks with the jump against 48 without it).
        var lowInsideItsTile = new BodyState(100, 178, 0, 0, true);
        var oneRowUp = new NavStep(new Point(7, 9), MoveKind.Walk, new Point(6, 10));
        Require(!new WalkTraversal().Steer(lowInsideItsTile, oneRowUp, null).Jump,
            "a walk never raises a jump, however far below its own step the live body sits");
        Require(!new WalkTraversal().Steer(lowInsideItsTile with { CollideX = true }, oneRowUp, null).Jump,
            "a walk pressed sideways against a shape faults and replans rather than jumping at it");

        AStar.MsBudget = 0;
        AStar.Find(new Point(6, 9), new Point(80, 9), 0, out _, out var stop);
        Require(stop == AStar.SearchStopReason.ExpansionBudget, "a spent search budget is not exhausted terrain");
        // The millisecond row holds the wall clock while the case around it runs lifted, because the
        // stop reason it asserts is the deadline itself: with the allowances lifted there is no
        // deadline to reach, the search stops for some other reason, and the row goes red saying the
        // opposite of what is wrong. The expansion-budget row above needs none of this — a work
        // count is not a clock and the lift leaves it alone, which is the whole distinction the
        // suite-wide rule rests on.
        bool lifted = LimitPlanningWork.Unbounded;
        LimitPlanningWork.Unbounded = false;
        try
        {
            AStar.MsBudget = .000001;
            AStar.Find(new Point(6, 9), new Point(80, 9), 10000, out _, out stop);
            Require(stop == AStar.SearchStopReason.Deadline, "a search deadline is not exhausted terrain");
        }
        finally
        {
            LimitPlanningWork.Unbounded = lifted;
            AStar.MsBudget = 0;
        }

        var navigator = new Navigator();
        live = new BodyState(100, 160, 0, 0, true);
        var movingArrival = new Navigator();
        movingArrival.MoveTo(live with { Vx = 2.5f }, live.Feet);
        Require(movingArrival.Arrived, "a grounded body reaching its goal may brake there without overshooting to satisfy a new rest-speed gate");
        navigator.MoveTo(live, target);
        int beforeInterrupt = navigator.EdgeCount;
        navigator.Interrupt(live);
        Require(navigator.EdgeCount == beforeInterrupt + 1 && navigator.LastEdge?.Outcome == TraversalFault.Interrupted,
            "an active movement attempt must record its interruption");
        navigator.Clear();
        Require(navigator.EdgeCount == beforeInterrupt + 1, "clearing an interrupted attempt must not report it twice");
        navigator = new Navigator();
        AStar.Avoid.Clear();
        for (int tick = 0; tick < 150; tick++) navigator.MoveTo(live, target);
        Require(navigator.FaultCount > 0, "replanning cannot hide a stationary body's failed movement request");
        Require(navigator.EdgeCount >= navigator.FaultCount, "retrying a failed edge starts a separately recorded attempt");
        Require(AStar.Avoid.Count == 0, "navigator-local failure prices must not leak into the caller's obstacle list");

        var immobile = new CountingStillWorld();
        var rejectedExecution = new TraversalExecution(new WalkTraversal(), step, null);
        local = new PlanLocalMovement();
        Require(!local.TryExecute(immobile, rejectedExecution, live, null, out _, out TraversalFault refusedFault), "an immobile macro must fail its proof");
        Require(refusedFault != TraversalFault.None,
            "a macro refused on physics reports the fault that refused it, because that fault is the whole of what the strike and the rejection memory read");
        // A refused entry is re-proven rather than answered from a cache of refused states. That
        // cache was a real saving and it was also what made the failure permanent: a refusal
        // leaves the body with no controls, so it does not move, so the next tick's state is
        // bit-identical, so the cached answer comes back — and it comes back without simulating,
        // which means without the evidence anything downstream could act on. The property is
        // asserted rather than merely allowed, because the cheap version of this is the one that
        // gets rebuilt.
        int simulations = immobile.Simulations;
        local.TryExecute(immobile, rejectedExecution, live, null, out _, out _);
        Require(immobile.Simulations > simulations,
            "a refused entry is proven again on the next tick, never answered from a cache: a cached refusal of a body that cannot move is a refusal that never ends");
        Require(!local.TryPrepare(immobile, rejectedExecution, live, null, out _), "standing still is not preparation when it cannot enable the move");
        Require(local.PreparationResult != "search-budget-exhausted",
            $"a preparation search over an immobile body must reach a verdict rather than run out, because a refusal that runs out is exempt from its own strike; it reported {local.PreparationResult}");

        NavGrid.World = immobile;
        navigator = new Navigator();
        navigator.RememberRejectedEntry(step, live);
        navigator.RefreshRejectedEntries(live, 181);
        Require(navigator.EntryRejected(step, live), "elapsed time alone cannot forget a still-invalid movement");
        immobile.Released = true;
        navigator.RefreshRejectedEntries(live, 362);
        Require(!navigator.EntryRejected(step, live), "fresh physical proof reopens an entry after an unannounced world change");

        NavGrid.World = world;
        AStar.InvalidateEdges();
        var rejectedFirst = new HashSet<NavStep>();
        var original = AStar.Find(new Point(6, 9), new Point(12, 9), 200, out _);
        Require(original is { Steps.Count: > 0 }, "first-edge filter fixture has an initial route");
        rejectedFirst.Add(original!.Steps[0]);
        var alternative = AStar.Find(new Point(6, 9), new Point(12, 9), 200, out _, out _, edge => !rejectedFirst.Contains(edge));
        Require(alternative == null || alternative.Steps.Count == 0 || !rejectedFirst.Contains(alternative.Steps[0]), "a rejected physical entry cannot be selected again by a new search");
        VerifyCapturedEntry();
        VerifyOffsetDescent();
        VerifyRetainedSearchAndExperience();
        VerifyProvenJumpsAreFlyable();
        VerifyEntryRejectionIsLearned();
        VerifyCommittedMoveSurvivesRelease();
        Require(new WalkTraversal().EntryDependsOnNext && !new DropTraversal().EntryDependsOnNext,
            "successor-sensitive walks cannot be excluded before their successor is known");
        Require(!new JumpTraversal().EntryDependsOnNext,
            "a jump's steering, arrival and fault tests never read the next step, so its entry failure can exclude the edge");

        Console.WriteLine("movement contracts: unsafe controls, retained threats, capability chains, liquid exit, macro isolation, walk stalls, search limits, interruption outcomes, flyable jump proofs, learned entry rejections and releases deferred to a landing passed");
        return 0;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Every jump edge the planner proves out of the captured window must be a jump the performer
    /// actually flies from the same resting pose. The two halves are one class precisely so this
    /// holds, and it did not: a running profile was admitted whenever the floor behind the take-off
    /// could reach its speed <em>minus the slack the performer accepts</em>, and then the arc was
    /// proven at the full speed, so a one-tile runway against a wall proved a jump entered at 1.75
    /// that the body entered at 1.49 and mislanded. That is what the 2026-09-11 session recorded
    /// 246 times without the body ever leaving the ground.
    /// </summary>
    private static void VerifyProvenJumpsAreFlyable()
    {
        string path = System.IO.Path.Combine("Tools", "Scenarios", "jump-from-a-one-tile-runway-under-a-ceiling.txt");
        var world = TextTileWorld.Parse(new List<string>(System.IO.File.ReadAllLines(path)), out _, out _);
        NavGrid.World = world;
        AStar.InvalidateEdges();
        AStar.AllowLava = false;

        var takeOff = new Point(3494, 605);
        BodyPhysics.Pose pose = NavGrid.StandAt(takeOff.X, takeOff.Y, false)
            ?? throw new InvalidOperationException("the recorded take-off tile must still be a node in the captured window");
        Require(CompareJumpPaths.ProvenEdge(pose, takeOff, new Point(3496, 600)) == null,
            "the jump out of a one-tile runway under a ceiling must not be offered: its run-up cannot reach the speed its arc was proven at");

        // The run-up is cached per direction and speed, which is only sound while it is a function
        // of the take-off, the profile and the body. Two different landings, one answer.
        JumpTraversal.Launch? toNear = JumpTraversal.TakeOff(world, takeOff, pose, new Point(3496, 600), 1f, BodyPhysics.WalkSpeed * .5f);
        JumpTraversal.Launch? toFar = JumpTraversal.TakeOff(world, takeOff, pose, new Point(3497, 599), 1f, BodyPhysics.WalkSpeed * .5f);
        Require(toNear is JumpTraversal.Launch near && toFar is JumpTraversal.Launch far && near == far,
            "a running profile's take-off must not depend on the tile it lands on, because the proof caches it per direction");

        // Every jump the window proves, run through the performer. A landing the shared arrival
        // slack closes on a neighbouring tile is counted apart from a misland on purpose: it is a
        // property of Traversal.Done rather than of the arc, and folding the two together would
        // let a real misland hide inside the tally.
        var unflyable = new List<string>();
        CompareJumpPaths.Audit audit = CompareJumpPaths.AuditWindow(world, 3486, 594, 21, 19, unflyable.Add);
        // Zero findings and zero coverage look identical in a count, so the coverage is asserted too.
        Require(audit.Flown >= 10, $"the captured window must still prove jumps the body flies for this to measure anything; {audit}");
        Require(audit.Unflyable == 0, $"every proven jump must be flown by the performer or closed by the shared arrival test; {audit.Unflyable} of {audit.Proven} were neither: {string.Join(" | ", unflyable)}");
    }

    /// <summary>
    /// A step the macro proof rejects before its first tick has failed as completely as one that
    /// failed in flight, so it must price its tile and be remembered. Neither happened: the strike
    /// was gated on the attempt having ticked and the memory on the traversal declaring its entry
    /// independent of its successor, so a jump refused at entry was re-offered by every later plan.
    /// The world here changes without announcing a revision, which is how a held step becomes
    /// unflyable between the plan and the tick that performs it.
    /// </summary>
    private static void VerifyEntryRejectionIsLearned()
    {
        var world = new RaisableLedgeWorld();
        NavGrid.World = world;
        AStar.InvalidateEdges();
        AStar.AllowLava = false;
        AStar.Avoid.Clear();

        var navigator = new Navigator();
        BodyPhysics.Pose start = NavGrid.StandAt(8, 9, false)!.Value;
        BodyPhysics.Pose goal = NavGrid.StandAt(16, 7, false)!.Value;
        var live = BodyState.Standing(start);
        var destination = new Vector2(goal.CentreX, goal.Bottom);
        bool raised = false;
        NavStep lastStep = default;
        bool struckOnTheFaultingTick = false;
        for (int tick = 0; tick < 400 && navigator.FaultCount == 0; tick++)
        {
            int strikesBefore = navigator.StuckStrikes, faultsBefore = navigator.FaultCount;
            Controls input = navigator.MoveTo(live, destination);
            // The strike has to land on the tick the refusal is found. It used to be able to
            // arrive forty ticks later instead, through the stall monitor noticing a body that
            // had not moved, because a spent preparation allowance suppressed the strike the
            // refusal itself earned. Forty ticks of a companion standing at a take-off is the
            // symptom this fixture exists to keep out, and a fault count that rises without a
            // strike beside it is how it comes back.
            if (navigator.FaultCount > faultsBefore)
                struckOnTheFaultingTick = navigator.StuckStrikes > strikesBefore;
            live = BodyMotion.Step(world, live, input);
            // The moment the body stands on the lip with the jump as its next move, close the
            // landing. The held step still names a tile that is now inside rock.
            if (!raised && live.OnGround && live.FeetTile.Y == 9 && live.FeetTile.X >= 10)
            {
                world.Raised = true;
                raised = true;
            }
            if (navigator.Path is { } held && !held.Finished)
                lastStep = held.Current;
        }
        Console.WriteLine($"   ledge fixture: raised={raised} step={lastStep.Kind} {lastStep.From.X},{lastStep.From.Y}->{lastStep.Tile.X},{lastStep.Tile.Y} faults={navigator.FaultCount} last={navigator.LastFault} strikes={navigator.StuckStrikes}");
        Require(raised, "the fixture must get the body to the lip, or it measures nothing");
        Require(navigator.FaultCount > 0 && navigator.LastFault == TraversalFault.Misland,
            $"closing the landing must produce a misland, not {navigator.LastFault}");
        Require(navigator.StuckStrikes > 0,
            "a step the macro proof rejects before its first tick must price its tile, or the planner re-offers it for ever");
        Require(struckOnTheFaultingTick,
            "the strike must land on the tick the refusal is found, not forty ticks later when a stall monitor notices the body has not moved");
    }

    /// <summary>
    /// A floor with a two-row lip the body jumps onto, whose landing row can be filled after a
    /// route is planned. The revision never moves, which is the unannounced change the rejection
    /// memory is written to survive.
    /// </summary>
    private sealed class RaisableLedgeWorld : ITileWorld
    {
        public bool Raised;
        public bool InWorld(int x, int y) => x >= 0 && x < 40 && y >= 0 && y < 30;
        public TileShape Shape(int x, int y)
        {
            if (x >= 14 && y >= 8) return TileShape.Solid;       // the lip the body climbs onto
            if (x >= 14 && y == 7 && Raised) return TileShape.Solid;  // the landing, closed later
            return y >= 10 ? TileShape.Solid : TileShape.Air;    // the floor it walks along
        }
        public bool PassThrough(int x, int y) => false;
        public bool Water(int x, int y) => false;
        public bool Lava(int x, int y) => false;
    }

    private static void VerifyRetainedSearchAndExperience()
    {
        var world = new FloorWorld();
        NavGrid.World = world;
        AStar.InvalidateEdges(); AStar.Avoid.Clear();
        RememberExecutedRoutes.World.Clear();
        var start = new Point(6, 9); var goal = new Point(18, 9);
        using var sliced = new ContinueRouteSearch(start, goal, false, true);
        int previous = 0;
        for (int calls = 0; calls < 20000 && !sliced.Finished; calls++)
        {
            sliced.Advance(3);
            Require(sliced.Expansions >= previous, "yielding must retain expanded search work");
            previous = sliced.Expansions;
        }
        Require(sliced.Stop == AStar.SearchStopReason.Found, "tiny slices must eventually find the same reachable goal");
        using var whole = new ContinueRouteSearch(start, goal, false, true);
        whole.Advance(100000);
        Require(whole.Stop == sliced.Stop && whole.Result()!.Goal == sliced.Result()!.Goal,
            "search outcome must not depend on its time-slice size");
        NavGrid.World = new EnclosedFloorWorld();
        AStar.InvalidateEdges();
        using (var exhausted = new ContinueRouteSearch(start, new Point(start.X, -100), false, true))
        {
            for (int slice = 0; slice < 100000 && !exhausted.Finished; slice++)
            {
                exhausted.Advance(1);
            }
            Require(exhausted.Finished && exhausted.Stop == AStar.SearchStopReason.Exhausted && exhausted.Result() == null,
                $"an exhausted search with no closer tile must not keep offering its first away-going detour (finished={exhausted.Finished}, stop={exhausted.Stop}, end={exhausted.Result()?.Goal}, nodes={exhausted.Expansions})");
        }
        NavGrid.World = world;
        AStar.InvalidateEdges();
        var deferred = new Navigator();
        var initial = BodyState.Standing(NavGrid.StandAt(start.X, start.Y, false)!.Value);
        deferred.MoveTo(initial, NavGrid.FeetWorld(goal));
        var completedQuery = new ContinueRouteSearch(start, goal, false, true);
        completedQuery.Advance(100000);
        Point join = completedQuery.Result()!.Steps[0].Tile;
        void SetNavigator(string field, object? value) => typeof(Navigator).GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(deferred, value);
        SetNavigator("search", completedQuery);
        SetNavigator("onStep", null);
        SetNavigator("execution", null);
        SetNavigator("expectedBody", null);
        var endedPrefix = new NavPath(new List<NavStep> { completedQuery.Result()!.Steps[0] }, goal, true) { Index = 1 };
        typeof(Navigator).GetProperty("Path")!.SetValue(deferred, endedPrefix);
        long priorSearch = deferred.SearchId;
        deferred.MoveTo(BodyState.Standing(NavGrid.StandAt(join.X, join.Y, false)!.Value), NavGrid.FeetWorld(goal));
        Require(deferred.SearchId == priorSearch && deferred.Path is { Partial: false, Finished: false },
            "a query completed while an old prefix was walked must publish at the moved live join immediately");
        using (var region = new ContinueRouteSearch(start, null, false, true))
        {
            region.Advance(500);
            Require(region.CanReuseFrom(new Point(7, 9)), "walking within a proven two-way region must retain the flood");
            Require(region.EstimatedTicks(start, new Point(7, 9)) is > 0f
                && region.EstimatedTicks(new Point(7, 9), start) == null,
                "retained region membership cannot expose old-root travel times as current estimates");
            Require(!region.CanReuseFrom(new Point(7, 20)), "proximity cannot transfer reachability to a different pocket");
        }
        using (var starved = new ContinueRouteSearch(start, goal, false, true))
        {
            // This row is about the deadline, so it holds the wall clock while the case around it
            // runs with the allowances lifted. Without the save-and-restore it would pass for the
            // wrong reason: Begin on a lifted allowance sets no deadline at all, so nothing would
            // be starved and the query would do work because it was never stopped.
            bool lifted = LimitPlanningWork.Unbounded;
            LimitPlanningWork.Unbounded = false;
            try
            {
                LimitPlanningWork.Begin(-1);
                // Asserted before End, because End clears the deadline and Expired reads false
                // afterwards — the premise has to be taken while it still holds.
                Require(LimitPlanningWork.Expired, "the allowance must already be spent, or this row starves nothing");
                starved.Advance(10);
                LimitPlanningWork.End();
                Require(starved.WorkUnits > 0, "upstream work cannot starve a retained query forever");
            }
            finally { LimitPlanningWork.Unbounded = lifted; }
        }

        // Learn from an actually executed route, never from the query result alone.
        var navigator = new Navigator();
        BodyState state = BodyState.Standing(NavGrid.StandAt(start.X, start.Y, false)!.Value);
        for (int tick = 0; tick < 600 && !navigator.Arrived; tick++)
            state = BodyMotion.Step(world, state, navigator.MoveTo(state, NavGrid.FeetWorld(goal)));
        Require(navigator.Arrived && RememberExecutedRoutes.World.Count > 0,
            "completed controls must teach a directed connection");
        string saved = RememberExecutedRoutes.World.Save();
        int count = RememberExecutedRoutes.World.Count;
        RememberExecutedRoutes.World.Clear();
        Require(RememberExecutedRoutes.World.Load(saved) && RememberExecutedRoutes.World.Count == count,
            "world memory must round-trip without losing successful connections");
        var suffixes = RememberExecutedRoutes.World.Suffixes(world, goal);
        Require(suffixes.Count > 0, "remembered traversals must compose into suffixes ending at the destination");
        using var experienced = new ContinueRouteSearch(start, goal, false, true);
        experienced.Advance(100000);
        Require(experienced.Stop == AStar.SearchStopReason.Found && experienced.ExperienceRoutesUsed > 0,
            "the live search must consume learned connections, not only serialize them");
        RememberExecutedRoutes.World.Clear();
        using var cold = new ContinueRouteSearch(new Point(2, 9), goal, false, true);
        cold.Advance(100000);
        RememberExecutedRoutes.World.Load(saved);
        using var warm = new ContinueRouteSearch(new Point(2, 9), goal, false, true);
        warm.Advance(100000);
        Require(warm.Stop == AStar.SearchStopReason.Found && warm.WorkUnits < cold.WorkUnits,
            "C to A followed by remembered A to B must save search work");
        bool rememberedSuffix = false;
        foreach (var known in suffixes.Values)
        {
            var returned = warm.Result()!.Steps;
            if (known.Count <= returned.Count && System.Linq.Enumerable.SequenceEqual(known,
                System.Linq.Enumerable.Skip(returned, returned.Count - known.Count))) rememberedSuffix = true;
        }
        Require(rememberedSuffix, "the returned route must actually contain an executed suffix");
        Console.WriteLine($"experience C-to-A-to-B: cold {cold.WorkUnits} work units, warm {warm.WorkUnits}");
        RememberExecutedRoutes.World.TileChanged(90, 90);
        Require(RememberExecutedRoutes.World.Count == count, "distant terrain changes must preserve useful experience");
        NavStep first = suffixes[new List<Point>(suffixes.Keys)[0]][0];
        RememberExecutedRoutes.World.Forget(first);
        Require(RememberExecutedRoutes.World.Count < count, "a physical failure must retire its remembered connection");
        Require(!RememberExecutedRoutes.World.Load("{broken"), "corrupt memory must be rejected without breaking world load");
        RememberExecutedRoutes.World.Load(saved);
        var liquidChanged = new ChangedLiquidWorld();
        Require(RememberExecutedRoutes.World.Suffixes(liquidChanged, goal).Count == 0,
            "unannounced liquid quantity changes must invalidate affected route memory");
        RememberExecutedRoutes.World.Clear();
        navigator = new Navigator();
        state = BodyState.Standing(NavGrid.StandAt(start.X, start.Y, false)!.Value);
        navigator.MoveTo(state, NavGrid.FeetWorld(goal));
        Point knockedTo = navigator.Path!.Current.Tile;
        state = BodyState.Standing(NavGrid.StandAt(knockedTo.X, knockedTo.Y, false)!.Value);
        navigator.MoveTo(state, NavGrid.FeetWorld(goal));
        Require(RememberExecutedRoutes.World.Count == 0, "an external displacement onto a landing cannot teach an unexecuted route");
        var controlSearch = new SearchControlSequences();
        for (int slice = 0; slice < 8; slice++)
            Require(!controlSearch.TryChoose(world, state, _ => false,
                body => -body.Left, MovementCapabilities.Basic, 100, 0, out Controls incomplete,
                allowPartialProgress: false) && incomplete == Controls.None,
                "endpoint-certified clearance cannot execute a closer prefix without reaching its goal");
        controlSearch.Clear();
        controlSearch.TryChoose(world, state, body => body.FeetTile == goal,
            body => Vector2.Distance(body.Feet, NavGrid.FeetWorld(goal)), MovementCapabilities.Basic, 1, 0, out _);
        Require(!controlSearch.TryChoose(world, state, body => body.FeetTile == goal,
            body => Vector2.Distance(body.Feet, NavGrid.FeetWorld(goal)), MovementCapabilities.Basic, 100, 0, out Controls unsafeControls, (_, _) => true)
            && unsafeControls == Controls.None, "new threats invalidate both retained controls and frontier prefixes");
        RememberExecutedRoutes.World.Clear();
        var alternatives = new RememberExecutedRoutes();
        Point middle = new(12, 9);
        void Teach(Point a, Point b, MoveKind kind)
        {
            var entry = BodyState.Standing(NavGrid.StandAt(a.X, a.Y, false)!.Value);
            var end = BodyState.Standing(NavGrid.StandAt(b.X, b.Y, false)!.Value);
            alternatives.Record(world, new NavStep(b, kind, a, Ticks: 20), entry, end,
                Rectangle.Union(new Rectangle((int)entry.Left, (int)entry.Bottom - BodyPhysics.Height, BodyPhysics.Width, BodyPhysics.Height),
                    new Rectangle((int)end.Left, (int)end.Bottom - BodyPhysics.Height, BodyPhysics.Width, BodyPhysics.Height)));
        }
        Teach(start, goal, MoveKind.Jump);
        Teach(start, middle, MoveKind.Walk);
        Teach(middle, goal, MoveKind.Walk);
        var priced = alternatives.Suffixes(world, goal, price: step => step.Kind == MoveKind.Jump ? 100f : 1f);
        Require(priced[start].Count == 2, "current hazard price must be applied before pruning remembered alternatives");
        var permitted = alternatives.Suffixes(world, goal, permitted: step => step.Kind != MoveKind.Jump);
        Require(permitted[start].Count == 2, "returnability policy must filter before pruning remembered alternatives");
        Console.WriteLine("retained search and experience: tiny-slice completion, execution-only learning, reload, composition and failure invalidation passed");
    }

    private static void VerifyCapturedEntry()
    {
        string path = System.IO.Path.Combine("Tools", "Scenarios", "actual-entry-drop-run-9.txt");
        var world = TextTileWorld.Parse(new List<string>(System.IO.File.ReadAllLines(path)), out _, out _);
        NavGrid.World = world;
        AStar.InvalidateEdges();
        AStar.AllowOneWayDrops = true;
        var live = new BodyState(59834.5f, 8464f, 0, 0, true);
        var navigator = new Navigator();
        var goal = NavGrid.StandAt(3722, 541, false)!.Value;
        int stationary = 0, worstStationary = 0;
        string firstFault = "none";
        for (int tick = 0; tick < 250 && !navigator.Arrived; tick++)
        {
            int faultsBefore = navigator.FaultCount;
            Controls input = navigator.MoveTo(live, new Vector2(goal.CentreX, goal.Bottom));
            if (navigator.FaultCount > faultsBefore && firstFault == "none")
                firstFault = $"tick {tick}: {navigator.LastFault} on {navigator.LastEdge} because {navigator.LastFailure?.Reason ?? "-"}";
            BodyState next = BodyMotion.Step(world, live, input);
            stationary = Vector2.DistanceSquared(live.Feet, next.Feet) < .01f ? stationary + 1 : 0;
            worstStationary = Math.Max(worstStationary, stationary);
            live = next;
        }
        // The goal is still delivered, and it is delivered through one priced refusal rather than
        // through none. The body starts at a sub-tile offset the descent out of 3740,528 was not
        // proven from; the local prefix search used to find a short sequence that made the macro
        // simulate clean from there, and with that search deleted the macro proof refuses the step
        // from the live body instead. That refusal is the designed path: it prices the tile,
        // strikes, and the next plan is expanded from the body's own pose, which is a descent
        // proven from where the body actually is rather than one nudged toward where it was not.
        // The bound is deliberately one refusal and not zero — zero would only be true again if
        // something searched for the entry, and two would mean the replan is not learning.
        Require(navigator.Arrived, $"the recorded cave entry must still be delivered; faults={navigator.FaultCount} first fault {firstFault}");
        Require(navigator.FaultCount <= 1,
            $"a sub-tile entry the edge was not proven from may cost one priced refusal and a replan, never a run of them; faults={navigator.FaultCount} first fault {firstFault}");
        Require(worstStationary < 10, "the recorded entry must not stand the body still while it decides");
    }

    /// <summary>
    /// A voluntary release (a Hold, a method change) issued while the body is committed to a move
    /// waits for the move to land; a pre-emption does not. The 13:27 capture of 14 September is
    /// the case: keep-company read its follow box as satisfied one tick after take-off, handed
    /// movement a Hold, and the navigator dropped the jump in the air, so the body fell short and
    /// replanned the same jump for ever. The world here is a floor with a two-tile ledge, which a
    /// walk cannot climb and a jump can, so the route holds exactly one jump.
    /// </summary>
    private static void VerifyCommittedMoveSurvivesRelease()
    {
        var world = new LedgeWorld();
        NavGrid.World = world;
        AStar.InvalidateEdges();
        AStar.AllowLava = false;
        // Driven through the coordinator and not the navigator, because the coordinator is the
        // production seam: its Hold calls Interrupt on every held tick, including the tick the
        // deferred move lands, and the first version of this test (driving the navigator directly)
        // passed while that seam filed every landing under a held release as a cancellation.
        var move = new CoordinateMovement();
        Navigator nav = move.Navigator;
        var live = new BodyState(6 * 16, 10 * 16, 0, 0, true);
        var target = new Vector2(24 * 16 + 8, 8 * 16);
        int completedBefore = nav.CompletedAttempts, cancelledBefore = nav.CancelledAttempts;
        bool released = false; int airborneTicks = 0, ticks = 0, heldTicks = 0; NavStep? jump = null;
        for (; ticks < 1200 && !nav.Arrived; ticks++)
        {
            Controls controls;
            if (!released && nav.Path is { Finished: false } path && path.Current.Kind == MoveKind.Jump && !live.OnGround)
            {
                // The brain withdraws its request one tick into the flight, as keep-company did.
                jump = path.Current;
                controls = move.Hold(live);
                released = true;
                Require(nav.ReleasePending, "a release issued in the air must be deferred, not applied");
                Require(nav.Path is { Finished: false }, "a deferred release must leave the path in hand");
                Require(controls != Controls.None, "a body in the air keeps its in-flight steer while the release waits");
            }
            else if (released && nav.ReleasePending)
            {
                // Every later tick the brain still asks to hold, as it does in play.
                controls = move.Hold(live);
                heldTicks++;
            }
            else if (released)
                break;
            else
                controls = move.MoveTo(live, target);
            if (!live.OnGround) airborneTicks++;
            live = BodyMotion.Step(world, live, controls, nav.Capabilities);
        }
        Require(released, $"the route onto the ledge must contain a jump the body flies, or this measures nothing (ticks={ticks}, status={nav.Status})");
        Require(!nav.ReleasePending && nav.Path == null && nav.Status == Navigator.ExecutionStatus.Idle,
            $"the deferred release must apply once the jump lands (pending={nav.ReleasePending}, status={nav.Status})");
        Require(nav.LastEdge is { Kind: MoveKind.Jump, Outcome: TraversalFault.None } && nav.LastEnding == AttemptEnding.Completed,
            $"the landed jump must be the last edge reported and reported completed (last={nav.LastEdge}, ending={nav.LastEnding})");
        Require(nav.CancelledAttempts == cancelledBefore,
            $"a release deferred to the landing must not add a cancelled step to the census (cancelled +{nav.CancelledAttempts - cancelledBefore})");
        Require(nav.DeferredReleases == 1, $"one step's release was deferred however many ticks it was held, and the count reads {nav.DeferredReleases}");
        Require(live.OnGround && live.Bottom <= 8 * 16 + 1, $"the body must land on the ledge rather than fall short (bottom={live.Bottom})");
        Console.WriteLine($"   committed move survives release: jump {jump?.From.X},{jump?.From.Y}->{jump?.Tile.X},{jump?.Tile.Y} released in the air and held for {heldTicks} more ticks, {airborneTicks} airborne ticks, landed at bottom={live.Bottom} after {ticks} ticks; last edge {nav.LastEdge?.Kind} {nav.LastEnding}, cancelled +{nav.CancelledAttempts - cancelledBefore}, completed +{nav.CompletedAttempts - completedBefore}, deferred {nav.DeferredReleases}");

        // The counterparts: a release on a grounded walk applies at once; a pre-emption in the air
        // applies at once, because the pre-empting owner supplies the body's controls itself; and
        // a pinned body, which the engine reports as airborne because its vertical velocity is
        // not zero while it does not move, is not in flight and does not hold a release either.
        var walker = new Navigator();
        var onFloor = new BodyState(6 * 16, 10 * 16, 0, 0, true);
        Controls first = walker.MoveTo(onFloor, new Vector2(12 * 16, 10 * 16));
        onFloor = BodyMotion.Step(world, onFloor, first, walker.Capabilities);
        walker.MoveTo(onFloor, new Vector2(12 * 16, 10 * 16));
        walker.Interrupt(onFloor, AttemptEnding.Cancelled, "released");
        Require(!walker.ReleasePending && walker.Path == null, "a release on a grounded walk is applied at once");
        var airborne = onFloor with { OnGround = false, Vy = -6f };
        walker.MoveTo(onFloor, new Vector2(12 * 16, 10 * 16));
        walker.Interrupt(airborne, AttemptEnding.Preempted, "state-search");
        Require(!walker.ReleasePending && walker.Path == null, "a pre-emption is never deferred");
        var pinned = onFloor with { OnGround = false, Vy = 0.3f, Pinned = true };
        walker.MoveTo(onFloor, new Vector2(12 * 16, 10 * 16));
        walker.Interrupt(pinned, AttemptEnding.Cancelled, "released");
        Require(!walker.ReleasePending && walker.Path == null, "a pinned body is not in flight, so a release on it is applied at once");
    }

    /// <summary>A floor at row 10 with a two-tile-high ledge from column 20 onward, so a route east holds one jump.</summary>
    private sealed class LedgeWorld : ITileWorld
    {
        public bool InWorld(int x, int y) => x >= 0 && x < 60 && y >= 0 && y < 40;
        public TileShape Shape(int x, int y) => (x >= 20 ? y >= 8 : y >= 10) ? TileShape.Solid : TileShape.Air;
        public bool PassThrough(int x, int y) => false;
        public bool Water(int x, int y) => false;
        public bool Lava(int x, int y) => false;
    }

    private class FloorWorld : ITileWorld
    {
        public bool InWorld(int x, int y) => x >= 0 && x < 100 && y >= 0 && y < 100;
        public TileShape Shape(int x, int y) => y >= 10 ? TileShape.Solid : TileShape.Air;
        public bool PassThrough(int x, int y) => false;
        public bool Water(int x, int y) => false;
        public bool Lava(int x, int y) => false;
    }

    private sealed class EnclosedFloorWorld : ITileWorld
    {
        public bool InWorld(int x, int y) => x >= 0 && x < 20 && y >= 0 && y < 20;
        public TileShape Shape(int x, int y) => x <= 0 || x >= 19 || y <= 0 || y >= 10 ? TileShape.Solid : TileShape.Air;
        public bool PassThrough(int x, int y) => false;
        public bool Water(int x, int y) => false;
        public bool Lava(int x, int y) => false;
    }

    private sealed class ChangedLiquidWorld : FloorWorld, ITileWorld
    {
        public byte LiquidAmount(int x, int y) => 1;
    }

    private static void VerifyOffsetDescent()
    {
        string source = System.IO.File.ReadAllText(System.IO.Path.Combine("Tools", "Scenarios", "2026-09-08_13-48-44-plans-shaped.txt")).Replace("\r", "");
        string[] blocks = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(source.Split("\n\n", StringSplitOptions.RemoveEmptyEntries), block => block.TrimStart().StartsWith("tick ")));
        var world = TextTileWorld.Parse(new List<string>(blocks[28].Trim().Split('\n')), out _, out _);
        NavGrid.World = world;
        AStar.InvalidateEdges();
        AStar.AllowLava = false;
        AStar.Avoid.Clear();
        var live = new BodyState(55899.3f, 8240, .72f, 0, true);
        Vector2 goal = NavGrid.FeetWorld(new Point(3491, 517));
        var navigator = new Navigator();
        for (int tick = 0; tick < 500 && !navigator.Arrived; tick++)
            live = BodyMotion.Step(world, live, navigator.MoveTo(live, goal));
        Require(navigator.Arrived, "first-edge proposals must account for the recorded sub-tile descent entry");
    }

    private sealed class CountingStillWorld : FloorWorld, ITileWorld, IBodySimulationWorld
    {
        public bool Released;
        public int Simulations { get; private set; }
        public int Revision { get; set; }
        public float GravityAt(BodyState state) => BodyPhysics.Gravity;
        public BodyState Simulate(BodyState state, Controls controls, MovementCapabilities capabilities)
        {
            Simulations++;
            return Released ? state with { Left = state.Left + 4 } : state;
        }
    }

    private sealed class CountingTraversal : Traversal
    {
        private int count;
        public override MoveKind Kind => MoveKind.Walk;
        public override IEnumerable<NavEdge> Candidates(NavNode node, BodyPhysics.Pose? here, bool lava) => Array.Empty<NavEdge>();
        public override Controls Steer(BodyState live, NavStep step, NavStep? next) => new(++count);
        public override object CaptureExecutionState() => count;
        public override void RestoreExecutionState(object? state) => count = (int)state!;
    }
}
