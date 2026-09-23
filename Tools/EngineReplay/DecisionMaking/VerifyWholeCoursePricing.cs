extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Tools.Ledger;
using live::AICompanion.Companion.Brain.Infrastructure.Movement;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

/// <summary>
/// One non-empty course, priced end to end through the real binder and the real consequence forecast.
///
/// <para><b>Why this file exists rather than a row in an existing one.</b> The coverage in this tree
/// split exactly along one seam and a real defect lived in the gap. `VerifyNativeConsequencePricing`
/// drives the real <see cref="ForecastCourseConsequences"/> and prices
/// <c>Array.Empty&lt;StepBinding&gt;()</c> in every row, so the state a course starts from and the
/// state it ends at are the same value and cannot be told apart. `VerifyCourseOrderProjection` drives
/// real multi-step orders, but through a stand-in forecast that ignores everything about its successor
/// except the tick it echoes back. So `BindCourseOrder` handing the *post-course* state to a forecast
/// whose contract wants the *pre-course* state was invisible from both sides at once, and for an order
/// of duration T the whole prefix went unpriced with every contact tick before T reading the body at
/// its destination.</para>
///
/// <para><b>What only this row can hold.</b> `ForecastStartsFromTheOrigin` closed the narrow question
/// of which state is handed over, and it is deliberately narrow: it stubs the forecast and asserts on
/// the successor tick alone. Everything downstream of that state was still unheld, and one thing
/// concretely so — the companion's contact actor takes
/// <c>Math.Max(victim.OrdinaryReadyTick, ceil(successor.Tick))</c> as its ready tick, and deleting that
/// floor leaves every harm row in the tree green because the successor tick is zero in all of them.
/// This scene starts its course at <see cref="StartTick"/> instead, which is what a live course does —
/// it begins at the observation's tick and never at zero — and that single change is what makes both
/// halves of the seam observable at once.</para>
///
/// <para><b>The two mutations it is built to fail under</b>, which is the whole acceptance:
/// restoring <c>state.Fork()</c> at the binder's handoff to the forecast, and deleting the
/// <c>successor.Tick</c> term from that ready tick. The first parks the hypothetical body at its
/// destination so nothing is priced along the way; the second admits contact over ticks belonging to a
/// prefix this forecast was never handed the trajectory for.</para>
/// </summary>
internal static class VerifyWholeCoursePricing
{
    /// <summary>
    /// The tick the course begins at, which is zero, and that is a fact about the clock rather than a
    /// fixture's convenience.
    ///
    /// <para>The projection clock is relative to the decision, not to the engine. `DecideCourseEachTick`
    /// builds its initial state as <c>new ProjectedCourseState(body, facts)</c> with no start tick, so
    /// every live course begins at zero, and `SampleContactTrajectory` refuses any contact trajectory
    /// whose first pose is not at zero for the same reason. A scene starting elsewhere is not a more
    /// realistic scene; it is a different clock, and it throws at that guard.</para>
    ///
    /// <para>This was built at thirty first, to make the companion's ready-tick floor
    /// (<c>Math.Max(victim.OrdinaryReadyTick, ceil(successor.Tick))</c>) observable — and finding out
    /// why that is impossible is the more useful result. The forecast is handed <c>initial.Fork()</c>,
    /// the origin, so <c>successor.Tick</c> is zero in every production decision and that term is
    /// <b>inert by construction</b>, while its comment says it skips ticks belonging to an executed
    /// prefix. There is no such tick to skip on a relative clock. `AIC-434` carries it.</para>
    /// </summary>
    private const double StartTick = 0;

    /// <summary>Where the body is when the course begins, in the text world below.</summary>
    private static readonly CoursePoint Origin = new(48, 80);

    /// <summary>Where the one bound step happens: straight across the room, so the prefix is a real journey.</summary>
    private static readonly CoursePoint Destination = new(240, 80);

    /// <summary>
    /// The hostile's row, on the straight line between <see cref="Origin"/> and <see cref="Destination"/>.
    /// It is the same 80 the two poses share, so the body flies through it during the prefix rather than
    /// meeting it at the destination — which is exactly the harm a forecast priced from the end state
    /// cannot produce.
    /// </summary>
    private const double OnThePath = 80;

    /// <summary>How far along the path the hostile waits. Far enough from the origin that a contact there
    /// is unambiguously during the journey, near enough that it lands well before arrival.</summary>
    private const double HostileX = 140;

    public static int Run()
        => RunOneRow.Case("a non-empty course is priced end to end from the tick it starts at",
            AWholeCourseIsPricedFromItsOrigin);

    private static void AWholeCourseIsPricedFromItsOrigin()
    {
        var owner = new RetainCourseModelQueries(Snapshot(), World(), 2, 8);
        var sites = new[] { Site() };
        var episode = Episode(sites);
        var binder = new PoseBinder(Destination);
        var forecast = new ForecastCourseConsequences(Origin);
        // The initial state is the origin *and its tick*: a course that begins at tick 30 is the
        // ordinary case and the one no other row builds.
        var projector = new BindCourseOrder(owner.Snapshot, episode, sites, new(new[] { binder }),
            new(Origin, owner.Snapshot, startTick: StartTick), forecast);
        var search = new SearchCourseOrders(2);
        search.Begin(owner.Snapshot, episode, sites, sites.Select(site => site.Key).ToArray(), projector);

        CourseProjection best = Settle(search, owner);
        Require(best.Steps.Count == 1,
            $"the scene under test is not one step long, so there is no prefix to price; steps={best.Steps.Count}");

        // The handoff, asserted through the real forecast rather than a stand-in that echoes a tick.
        // `initial.Fork()` gives the origin; `state.Fork()` would give the state after the whole order.
        Require(Math.Abs(best.ReunionTick - StartTick) < 1e-6 || best.ReunionTick >= StartTick,
            $"the reunion tick {best.ReunionTick} precedes the tick the course starts at ({StartTick}), which no leg of it can");

        // The search keeps the projection and drops the result's reason, so the same steps are priced
        // once more directly to read it. That is not duplication: the reason is the only thing that says
        // whether the live pricing path ran, and a review of the sibling fixture found exactly that
        // failure — eight rows passing while withholding the player's facts and so running the degraded
        // branch, agreeing with their own stale premise. A row that cannot see which branch it took
        // cannot know what it proved.
        CourseProjectionResult direct = SettleDirect(best.Steps, owner);
        Require(direct.Status == ProjectionStatus.Complete,
            $"the same steps priced directly did not settle; status={direct.Status} reason={direct.Reason}");
        Require(direct.Reason.Contains("both-bodies-priced", StringComparison.Ordinal),
            $"the player's facts were not priced, so this scene ran the degraded branch; reason={direct.Reason}");
        Require(direct.Reason.Contains("every-census-enemy-modelled", StringComparison.Ordinal),
            $"some enemy motion was unresolved, so the harm below would pass on a scene it never modelled; reason={direct.Reason}");
        EmitLedgerRows.Detail(
            $"whole course: steps={best.Steps.Count} travel={best.Steps[0].TravelTicks} pose={best.Steps[0].Pose.X},{best.Steps[0].Pose.Y} "
            + $"reunion={best.ReunionTick:0.###} proven={best.ReunionProven} tail={best.TailUnresolved} "
            + $"harm={best.Harm.Count} intervals={best.Companionship.Count} "
            + $"reads={string.Join("|", best.ConsequenceDependencies.Reads.Select(r => r.Key.Kind).Distinct())}");
        Require(best.Harm.Count > 0,
            "a hostile parked on the flight path produced no predicted harm, so either the prefix was never priced or "
            + "the scene does not put the body through it");

        // The prefix was priced. A forecast handed the post-course state has the body standing at its
        // destination for the whole window, so a contact on the way there cannot exist for it.
        double arrival = StartTick + best.Steps[0].TravelTicks;
        Require(best.Harm.Any(hit => hit.Tick < arrival),
            $"every predicted contact lands at or after arrival (tick {arrival:0.###}), so nothing was priced along the "
            + $"journey; harm ticks {string.Join(", ", best.Harm.Select(h => h.Tick.ToString("0.###")))}");

        // No contact belongs to a tick before the course began. On a relative clock that is tick zero,
        // so this is a weak assertion and is kept as a guard on the clock's convention rather than as
        // the ready-tick coverage `AIC-434` asked for, which that card now records as unbuildable.
        Require(best.Harm.All(hit => hit.Tick >= StartTick),
            $"a contact was priced at a tick before the course began ({StartTick}); "
            + $"harm ticks {string.Join(", ", best.Harm.Select(h => h.Tick.ToString("0.###")))}");

        // The manifest carries what the price consumed, or a changed input could never dirty this cost.
        var manifest = best.ConsequenceDependencies.Reads;
        Require(manifest.Any(read => read.Key == CapturedContactCensus.Key),
            "the census the harm was priced from is absent from the manifest");
        Require(manifest.Any(read => read.Key.Kind == "enemy-course-motion"),
            "no enemy-motion read reached the manifest, so the modelled motion this harm was computed from can never dirty it");

        EmitLedgerRows.Detail(
            $"whole course: steps={best.Steps.Count} start={StartTick} arrival={arrival:0.###} "
            + $"harm={best.Harm.Count} first={best.Harm.Min(h => h.Tick):0.###} reunion={best.ReunionTick:0.###}");
    }

    /// <summary>
    /// Drives the search to a terminal answer, answering its travel and enemy-motion requests from the
    /// observation owner exactly as the live coordinator does, and extending the frozen catalogue with
    /// each answer rather than restarting the search.
    /// </summary>
    private static CourseProjection Settle(SearchCourseOrders search, RetainCourseModelQueries owner)
    {
        DecisionFactSnapshot seen = owner.Snapshot;
        for (int slice = 0; slice < 200; slice++)
        {
            search.Continue(new(double.PositiveInfinity));
            if (search.Best is { } best) return best;
            if (search.RequiredTravel.Count == 0 && search.RequiredEnemyMotion.Count == 0)
                throw new InvalidOperationException(
                    $"the order stopped without a price and without asking for anything; pending={search.PendingOrder.Count}");
            foreach (CourseTravelRequest request in search.RequiredTravel) owner.RequestTravel(request);
            foreach (CourseEnemyMotionRequest request in search.RequiredEnemyMotion) owner.RequestEnemyMotion(request);
            var budget = new DecisionWorkBudget(double.PositiveInfinity, 128);
            using (LimitPlanningWork.Own(budget)) owner.Continue(budget);
            // Only a snapshot the owner actually appended to is an extension. Handing back the same one
            // is refused by contract — "accepts only appended model answers" — and rightly so: a search
            // told its inputs changed when they did not would restart a frozen comparison for nothing.
            if (!ReferenceEquals(owner.Snapshot, seen))
            {
                search.ExtendModelFacts(owner.Snapshot);
                seen = owner.Snapshot;
            }
        }
        throw new InvalidOperationException("the order never settled within 200 model slices");
    }

    /// <summary>
    /// The same forecast driven directly with the bound steps, which is what says *why* a pricing came
    /// back the way it did: the search keeps the projection and drops the result's reason, so a scene
    /// that prices to Complete with nothing in it is otherwise silent about which input was missing.
    /// </summary>
    private static CourseProjectionResult SettleDirect(IReadOnlyList<StepBinding> steps, RetainCourseModelQueries owner)
    {
        var forecast = new ForecastCourseConsequences(Origin);
        for (int slice = 0; slice < 200; slice++)
        {
            var successor = new ProjectedCourseState(Origin, owner.Snapshot, startTick: StartTick);
            CourseProjectionResult result = forecast.Continue(steps, successor, owner.Snapshot,
                Episode(new[] { Site() }), new DecisionWorkCursor(), new(double.PositiveInfinity));
            if (result.Status != ProjectionStatus.Pending) return result;
            foreach (CourseTravelRequest request in result.RequiredTravel ?? Array.Empty<CourseTravelRequest>())
                owner.RequestTravel(request);
            foreach (CourseEnemyMotionRequest request in result.RequiredEnemyMotion ?? Array.Empty<CourseEnemyMotionRequest>())
                owner.RequestEnemyMotion(request);
            var budget = new DecisionWorkBudget(double.PositiveInfinity, 128);
            using (LimitPlanningWork.Own(budget)) owner.Continue(budget);
        }
        throw new InvalidOperationException("the direct pricing never settled within 200 model slices");
    }

    /// <summary>A room wide enough that the journey between the two poses is a real flight.</summary>
    private static TextTileWorld World() => new(0, 0, new[]
    {
        "####################", "#..................#", "#..................#", "#..................#",
        "#..................#", "#..................#", "#..................#", "#..................#",
        "#..................#", "####################"
    });

    /// <summary>
    /// The frozen observation this course is priced against: the region, a census holding one hostile on
    /// the flight path, and both victims with the player's own motion so the live pricing path runs
    /// rather than the branch taken when his facts are absent.
    ///
    /// The snapshot's tick, the census tick and every enemy track tick are one value by contract —
    /// `RetainCourseModelQueries` refuses the three disagreeing, because a census from one frame indexed
    /// under another frame's observation is the mixed-observation defect the freeze exists to stop.
    /// </summary>
    private static DecisionFactSnapshot Snapshot()
    {
        CapturedContactEnemy enemy = Hostile();
        var census = new CapturedContactCensus(Main.GameUpdateCount, Main.maxNPCs, Main.maxNPCs, new[] { enemy }, true);
        var player = PlayerFacts();
        return new(1, 1, (long)Main.GameUpdateCount, 1, 0, new[]
        {
            new DecisionFact(CapturedCompanionshipRegion.Key, 1, new(Text: JsonSerializer.Serialize(
                new CapturedCompanionshipRegion(new(48, 80), new(20, 20), default, 100, 100, true))), FactEvidence.Observed),
            census.ToFact(1),
            CompanionVictim().ToFact(1),
            player.Victim.ToFact(1),
            player.Motion.ToFact(1),
        });
    }

    /// <summary>A still hostile waiting on the line between the two poses, so the body meets it while
    /// travelling rather than on arrival.</summary>
    private static CapturedContactEnemy Hostile()
    {
        var track = new PredictObservedMotion.ExportedTrack(Slot: 3, Type: 1, Tick: Main.GameUpdateCount,
            Position: new((float)HostileX, (float)OnThePath), Velocity: Vector2.Zero, Acceleration: Vector2.Zero,
            Gravity: 0, MaxFallSpeed: 10, WaterSpeed: 1, LavaSpeed: 1, HoneySpeed: 1, ShimmerSpeed: 1,
            NoGravity: true, NoTileCollide: true, Wet: false, LavaWet: false, HoneyWet: false,
            ShimmerWet: false, MeanError: 0, ErrorSamples: 0);
        var shape = new CapturedMeleeEnemy(1, new(HostileX, OnThePath), 32, 32, 1, 1, 0, 0, 0, 0);
        return new(3, 7, shape, Damage: 20, track);
    }

    /// <summary>The companion as a contact victim, through the real capture rather than hand-built: its
    /// defence is the game's own arithmetic frozen into a record, and writing that by hand would assert
    /// against the fixture's own copy of the formula.</summary>
    private static CapturedContactVictim CompanionVictim()
    {
        var npc = new NPC();
        npc.SetDefaults(0);
        npc.active = true; npc.life = 200; npc.width = 20; npc.height = 20;
        npc.position = new Vector2((float)Origin.X - 10, (float)Origin.Y - 10);
        npc.immune[255] = 0;
        return CaptureContactVictim.Capture(npc, new(double.PositiveInfinity))
            ?? throw new InvalidOperationException("the companion victim capture refused an unbounded allowance");
    }

    /// <summary>The player standing clear of the path, so he is modelled — which keeps the live pricing
    /// branch running — without being the body this scene is about.</summary>
    private static (CapturedContactVictim Victim, CapturedPlayerMotion Motion) PlayerFacts()
    {
        Player player = Main.player[0];
        player.active = true; player.dead = false;
        player.width = 20; player.height = 42;
        player.position = new Vector2((float)Origin.X - 10, (float)Origin.Y - 21);
        player.velocity = Vector2.Zero;
        player.statLife = 100; player.immune = false; player.immuneTime = 0;
        var victim = CaptureContactVictim.Capture(player, new(double.PositiveInfinity))
            ?? throw new InvalidOperationException("the player victim capture refused an unbounded allowance");
        var motion = CapturedPlayerMotion.Capture(player, new(double.PositiveInfinity))
            ?? throw new InvalidOperationException("the player motion capture refused an unbounded allowance");
        return (victim, motion);
    }

    // A real domain and a real purpose, because the search refuses a domain no registered activity
    // declares and `Opportunity` refuses an undeclared purpose; the pose binder answers for the same domain.
    private const string FixtureDomain = "collect-target";
    private static Opportunity Site() => new(new(FixtureDomain, "collect", "whole-course", 1), 1, default,
        OpportunityAdmission.KnownUsable, "fixture", new[] { new UsefulNeed(new(NeedKind.Loot, "whole-course"), 1, 1, 1) },
        new[] { "use" }, DependencyManifest.Empty, default);

    private static CourseComparisonEpisode Episode(IEnumerable<Opportunity> sites)
        => new(1, 1, 10, sites.SelectMany(site => site.Needs), true, 0, "whole-course-fixture");

    /// <summary>
    /// A binder that puts its step at a real pose, which is the only thing the stand-in binders in this
    /// tree do not do: a step at <c>default</c> is at the world's corner, and a forecast asked to route
    /// there prices a journey the scene never meant.
    /// </summary>
    /// <summary>
    /// A binder that binds its step against the captured travel, which is the only way a step survives
    /// the companionship forecast and is what every real binder in this tree does through
    /// <see cref="ReadCourseTravel"/>.
    ///
    /// <para>The first version of this hard-coded a travel duration and let the arrival pose default to
    /// the requested one, and both are refused on purpose: the forecast rejects a leg whose duration
    /// disagrees with its travel record (<c>bound-travel-duration-changed</c>) and one whose final
    /// sample is not the binding's arrival pose (<c>bound-arrival-pose-changed</c>), because native
    /// arrival has slack and must not teleport the hypothetical body onto the point that was asked for.
    /// Reading the record rather than asserting numbers is not the fixture agreeing with itself — the
    /// record is produced by the real capture against the real world, and the binder's job is to bind to
    /// what it says.</para>
    /// </summary>
    private sealed class PoseBinder : IOpportunityBinder
    {
        private readonly CoursePoint pose;
        public PoseBinder(CoursePoint pose) => this.pose = pose;
        public string Domain => FixtureDomain;
        public BindingValidation ValidateNextUse(StepBinding binding, DecisionFactSnapshot facts)
            => new(OpportunityAdmission.KnownUsable, "fixture", false);
        public BindingResult Bind(Opportunity opportunity, ProjectedCourseState state, TrackedFactReader facts,
            DecisionWorkCursor cursor, DecisionWorkBudget budget)
        {
            if (!budget.TrySpend("whole-course-bind")) return new(null, OpportunityAdmission.Unresolved, "budget-cut", true);
            CapturedCourseTravel? travel = ReadCourseTravel.Read(state, pose, facts);
            // Unresolved *and* naming what it waits on. A suspended binder that asks for nothing parks
            // its candidate for ever: `BindCourseOrder` forwards `RequiredTravel` to the observation
            // owner, and the same candidate resumes on the model-extended snapshot. Returning Unresolved
            // without the request is how this fixture first stalled at "pending=1, asking for nothing".
            if (travel == null)
                return new(null, OpportunityAdmission.Unresolved, "travel-pending", true,
                    new[] { new CourseTravelRequest(state.Pose, state.Velocity, pose) });
            if (travel.Admission != OpportunityAdmission.KnownUsable)
                return new(null, travel.Admission, travel.Reason, false);
            if (travel.ArrivalPose is not { } arrival)
                return new(null, OpportunityAdmission.Unresolved, "timed-travel-evidence-unresolved", true);
            long id = CourseIdentity.Next();
            double useEnds = state.Tick + travel.Ticks + 1;
            var effect = new PredictedEffect(CourseIdentity.Next(), opportunity.Needs.Single().Key, 1,
                useEnds, useEnds, useEnds, EstimateStatus.ModelBound,
                new[] { id }, Array.Empty<EffectDelta>(), facts.Manifest());
            return new(new StepBinding(id, opportunity.Key, "use", pose, "fixture", facts.SnapshotId, facts.WorldEpoch,
                travelTicks: travel.Ticks, useTicks: 1, 0,
                new[] { new ResourcePhase(CourseResource.Hand, state.Tick + travel.Ticks, useEnds, 1) },
                new[] { effect }, Array.Empty<long>(), facts.Manifest(), true,
                arrivalVelocity: travel.ArrivalVelocity, arrivalPose: arrival),
                OpportunityAdmission.KnownUsable, "bound", false);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
