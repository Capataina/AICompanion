#nullable enable
extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

internal static class VerifyCourseOrderProjection
{
    public static int Run()
        => RunOneRow.Case("G11 order projection retains native bindings across one-operation cuts", SlicedOrder)
        + RunOneRow.Case("G03 empty orders retain unresolved companionship costs", EmptyOrder)
        + RunOneRow.Case("G08 projection refuses changed frozen inputs", FrozenInputs)
        + RunOneRow.Case("G11 completed model answers resume a suspended course order", CompletedModelAnswer)
        + RunOneRow.Case("G03 captured companionship uses the live region curve", SharedCompanionshipCurve)
        + RunOneRow.Case("G11 the consequence forecast is handed the state the course starts from", ForecastStartsFromTheOrigin);

    /// <summary>
    /// Which end of the course the consequence forecast is priced from, which nobody had asked.
    ///
    /// `BindCourseOrder` applies each binding to a running `ProjectedCourseState` — `TryApply` sets the
    /// pose to the step's arrival and adds its travel and use to the tick — and then handed *that* state
    /// to the forecast. But `ForecastCourseCompanionship` walks the same steps again from the state it is
    /// given, so it was pricing a journey that begins where the journey ends: for an order of duration T
    /// the whole prefix goes unpriced, and every contact tick before T reads the body at its destination
    /// rather than on the way. `VerifyCompanionshipForecast.WholeCourse` pins the contract the other way,
    /// passing the pre-course pose with a start tick of zero.
    ///
    /// Two sides of one seam, and neither fixture could see it. Every row that drove a real forecast
    /// priced an *empty* order, where the start state and the end state are the same value; every row
    /// that drove a non-empty order used a stand-in forecast that ignored its successor. This row is the
    /// stand-in made to care about exactly one thing, because the forecast's own behaviour is already
    /// pinned elsewhere and what was never pinned is what the caller hands it.
    /// </summary>
    private static void ForecastStartsFromTheOrigin()
    {
        var sites = new[] { Site("first"), Site("second") };
        var facts = Facts(); var episode = Episode(sites);
        var forecast = new UnknownForecast();
        // The initial state is the origin: tick zero, and the binder is told so explicitly.
        var projector = new BindCourseOrder(facts, episode, sites, new(new[] { new FixtureBinder() }),
            new(default(CoursePoint)), forecast);
        var search = new SearchCourseOrders(2);
        search.Begin(facts, episode, sites, sites.Select(site => site.Key).ToArray(), projector);
        search.Continue(new(double.PositiveInfinity));

        // Each fixture binding is one tick of use, so the applied state stands at tick 2 by the time the
        // order is complete. `UnknownForecast` echoes whatever successor tick it was handed, so the
        // projection's reunion tick is the measurement: 0 is the origin the course starts from and 2 is
        // the state it ends at, which is what this row exists to tell apart.
        CourseProjection best = search.Best
            ?? throw new InvalidOperationException($"the two-step order never priced at all; pending={search.PendingOrder.Count}");
        Require(best.Steps.Count == 2,
            $"the order under test is not two steps long, so the two ends of it cannot differ; steps={best.Steps.Count}");
        Require(best.ReunionTick == 0,
            $"the forecast was handed the state the course ends at rather than the one it starts from, so every tick of the journey is priced from its own destination; successor tick={best.ReunionTick}, expected 0");
    }

    private static void CompletedModelAnswer()
    {
        var sites = new[] { Site("first"), Site("second") };
        var observed = new DecisionFact(new("world", "fixture"), 1, new(Amount: 1), FactEvidence.Observed);
        var facts = new DecisionFactSnapshot(1, 1, 100, 1, 0, new[] { observed });
        var episode = Episode(sites);
        var binder = new FixtureBinder { NeedsSecondModel = true };
        var projector = new BindCourseOrder(facts, episode, sites, new(new[] { binder }), new(default(CoursePoint)), new UnknownForecast());
        var search = new SearchCourseOrders(2);
        search.Begin(facts, episode, sites, sites.Select(site => site.Key).ToArray(), projector);
        search.Continue(new(double.PositiveInfinity));
        Require(binder.Uses == 1 && search.Best == null && search.PendingOrder.Count == 2,
            "an unanswered derived query did not preserve the partly bound order");
        var model = new DecisionFact(new("fixture-model", "second"), 1, new(Amount: 1), FactEvidence.Modelled);
        var extended = new DecisionFactSnapshot(1, 1, 100, 1, 0, new[] { observed, model });
        Require(extended.IsModelExtensionOf(facts), "a derived answer changed no observations but was refused");
        search.ExtendModelFacts(extended);
        search.Continue(new(double.PositiveInfinity, 3));
        Require(binder.Uses == 2 && search.Best?.Steps.Count == 2 && search.Best.Prefix!.Id == binder.FirstUse,
            "model completion restarted or stranded the accepted prefix");
        var changed = new DecisionFact(observed.Key, 2, new(Amount: 2), FactEvidence.Observed);
        Require(!new DecisionFactSnapshot(1, 1, 100, 1, 0, new[] { changed, model }).IsModelExtensionOf(facts)
            && !new DecisionFactSnapshot(1, 1, 101, 1, 0, new[] { observed, model }).IsModelExtensionOf(facts)
            && !new DecisionFactSnapshot(1, 1, 100, 2, 0, new[] { observed, model }).IsModelExtensionOf(facts)
            && !new DecisionFactSnapshot(1, 1, 100, 1, 1, new[] { observed, model }).IsModelExtensionOf(facts)
            && !new DecisionFactSnapshot(1, 1, 100, 1, 0, new[] { observed,
                new DecisionFact(model.Key, 1, model.Value, FactEvidence.Observed) }).IsModelExtensionOf(facts),
            "model extension admitted a changed world, clock, receipt stream or new observation");
    }

    private static void SharedCompanionshipCurve()
    {
        foreach (float halfWidth in new[] { 0f, 32f, 120f })
        foreach (float halfHeight in new[] { 0f, 48f, 160f })
        foreach (float recovery in new[] { 0f, 100f, 800f })
        foreach (float x in new[] { -900f, -120f, 0f, 120f, 900f })
        foreach (float y in new[] { -500f, 0f, 500f })
        {
            var region = new live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerIntentRegion(
                new Microsoft.Xna.Framework.Vector2(17, -23), new(halfWidth, halfHeight), default, false);
            var point = region.Centre + new Microsoft.Xna.Framework.Vector2(x, y);
            float expectedGap = MathF.Max(0, MathF.Max(MathF.Abs(x) - halfWidth, MathF.Abs(y) - halfHeight));
            float expectedPull = Math.Clamp(expectedGap / MathF.Max(1, recovery - MathF.Max(halfWidth, halfHeight)), 0, 1);
            Require(region.GapBeyond(point) == expectedGap
                && MeasureCompanionshipGap.Pull(expectedGap, halfWidth, halfHeight, recovery) == expectedPull,
                "extracting the shared curve changed the native rectangular gap or recovery scaling");
        }
        Require(MeasureCompanionshipGap.Pull(100, 50, 50, 250) > MeasureCompanionshipGap.Pull(100, 50, 50, 450),
            "the curve did not use the explicitly captured recovery preference");
    }

    private static Opportunity Site(string target) => new(new("fixture", "use", target, 1), 1, default,
        OpportunityAdmission.KnownUsable, "fixture", new[] { new UsefulNeed(new(NeedKind.Loot, target), 1, 1, 1) },
        new[] { "use" }, DependencyManifest.Empty);
    private static DecisionFactSnapshot Facts() => new(1, 1, 100, 1, 0, Array.Empty<DecisionFact>());
    private static CourseComparisonEpisode Episode(IEnumerable<Opportunity> sites)
        => new(1, 1, 10, sites.SelectMany(site => site.Needs), true, 0, "fixture");

    private static void SlicedOrder()
    {
        var sites = new[] { Site("first"), Site("second") };
        var facts = Facts(); var episode = Episode(sites);
        var binder = new FixtureBinder(); var forecast = new UnknownForecast();
        var projector = new BindCourseOrder(facts, episode, sites, new(new[] { binder }), new(default(CoursePoint)), forecast);
        var cursor = new DecisionWorkCursor(); cursor.Bind(1, "fixture");
        CourseProjectionResult result = new(ProjectionStatus.Pending, null, "not-started");
        for (int slice = 0; slice < 8 && result.Status == ProjectionStatus.Pending; slice++)
            result = projector.Continue(sites.Select(site => site.Key).ToArray(), facts, episode, cursor, new(double.PositiveInfinity, 1));
        Require(result.Status == ProjectionStatus.Complete && result.Projection?.Steps.Count == 2,
            "one-operation slices failed to finish the same order");
        Require(binder.Uses == 2 && result.Projection!.Steps.Select(step => step.Id).Distinct().Count() == 2,
            "a cut repeated native binding or reused a concrete use identity");
        Require(result.Projection!.Steps[1].Effects.Single().NominalTick == 2,
            "the later binding did not inherit its predecessor's projected clock");
        Require(!CompareCourseOutcomes.Evaluate(result.Projection, episode).Total.HasJustifiedBounds,
            "missing future costs became a certified course value");
        Require(ReferenceEquals(result, projector.Continue(sites.Select(site => site.Key).ToArray(), facts,
            episode, cursor, new(double.PositiveInfinity, 0))) && forecast.Completed == 1,
            "reading a completed projection repeated its work");
    }

    private static void EmptyOrder()
    {
        var facts = Facts(); var episode = Episode(Array.Empty<Opportunity>()); var forecast = new UnknownForecast();
        var projector = new BindCourseOrder(facts, episode, Array.Empty<Opportunity>(),
            new(Array.Empty<IOpportunityBinder>()), new(default(CoursePoint)), forecast);
        var cursor = new DecisionWorkCursor(); cursor.Bind(1, "empty");
        Require(projector.Continue(Array.Empty<OpportunityKey>(), facts, episode, cursor,
            new(double.PositiveInfinity, 0)).Status == ProjectionStatus.Pending, "empty work bypassed its consequence forecast");
        var result = projector.Continue(Array.Empty<OpportunityKey>(), facts, episode, cursor, new(double.PositiveInfinity, 1));
        Require(result.Projection is { TailUnresolved: true, ReunionProven: false } && forecast.Completed == 1,
            "empty work was treated as a proven free reunion");
    }

    private static void FrozenInputs()
    {
        var sites = new[] { Site("first") }; var facts = Facts(); var episode = Episode(sites);
        var projector = new BindCourseOrder(facts, episode, sites, new(new[] { new FixtureBinder() }),
            new(default(CoursePoint)), new UnknownForecast());
        var cursor = new DecisionWorkCursor(); cursor.Bind(1, "first");
        projector.Continue(new[] { sites[0].Key }, facts, episode, cursor, new(double.PositiveInfinity, 0));
        bool changedOrder = false, changedFacts = false;
        try { projector.Continue(Array.Empty<OpportunityKey>(), facts, episode, cursor, new(double.PositiveInfinity)); }
        catch (InvalidOperationException) { changedOrder = true; }
        try { projector.Continue(new[] { sites[0].Key }, Facts(), episode, cursor, new(double.PositiveInfinity)); }
        catch (InvalidOperationException) { changedFacts = true; }
        Require(changedOrder && changedFacts, "projection reused a cursor across changed inputs");
        cursor.Bind(2, "new-order");
        Require(projector.Continue(Array.Empty<OpportunityKey>(), facts, episode, cursor,
            new(double.PositiveInfinity)).Projection?.Steps.Count == 0, "new order inherited its predecessor's state");
    }

    private sealed class FixtureBinder : IOpportunityBinder
    {
        public string Domain => "fixture";
        public int Uses { get; private set; }
        public bool NeedsSecondModel { get; init; }
        public long FirstUse { get; private set; }
        public BindingValidation ValidateNextUse(StepBinding binding, DecisionFactSnapshot facts)
            => new(OpportunityAdmission.KnownUsable, "fixture", false);
        public BindingResult Bind(Opportunity opportunity, ProjectedCourseState state, TrackedFactReader facts,
            DecisionWorkCursor cursor, DecisionWorkBudget budget)
        {
            if (!budget.TrySpend("fixture-bind")) return new(null, OpportunityAdmission.Unresolved, "budget-cut", true);
            if (NeedsSecondModel && opportunity.Key.Target == "second"
                && facts.Read(new("fixture-model", "second")).Evidence != FactEvidence.Modelled)
                return new(null, OpportunityAdmission.Unresolved, "model-pending", true);
            Uses++; long id = CourseIdentity.Next();
            if (Uses == 1) FirstUse = id;
            var effect = new PredictedEffect(CourseIdentity.Next(), opportunity.Needs.Single().Key, 1,
                state.Tick + 1, state.Tick + 1, state.Tick + 1, EstimateStatus.ModelBound,
                new[] { id }, Array.Empty<EffectDelta>(), facts.Manifest());
            return new(new StepBinding(id, opportunity.Key, "use", default, "fixture", facts.SnapshotId, facts.WorldEpoch,
                0, 1, 0, new[] { new ResourcePhase(CourseResource.Hand, state.Tick, state.Tick + 1, 1) },
                new[] { effect }, Array.Empty<long>(), facts.Manifest(), true), OpportunityAdmission.KnownUsable, "bound", false);
        }
    }

    private sealed class UnknownForecast : ICourseConsequenceForecast
    {
        public int Completed { get; private set; }
        public CourseProjectionResult Continue(IReadOnlyList<StepBinding> steps, ProjectedCourseState successor,
            DecisionFactSnapshot facts, CourseComparisonEpisode episode, DecisionWorkCursor cursor, DecisionWorkBudget budget)
        {
            if (!budget.TrySpend("fixture-consequence")) return new(ProjectionStatus.Pending, null, "budget-cut");
            Completed++;
            return new(ProjectionStatus.Complete, new(steps, Array.Empty<PredictedHarm>(),
                Array.Empty<CompanionshipInterval>(), successor.Tick, false, tailUnresolved: true), "unknown-future");
        }
    }
    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }
}
