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
        + RunOneRow.Case("G08 projection refuses changed frozen inputs", FrozenInputs);

    private static Opportunity Site(string target) => new(new("fixture", "use", target, 1), 1, default,
        OpportunityAdmission.KnownUsable, "fixture", new[] { new UsefulNeed(new(NeedKind.Loot, target), 1, 1, 1) },
        new[] { "use" }, DependencyManifest.Empty);
    private static DecisionFactSnapshot Facts() => new(1, 1, 100, 1, 0, Array.Empty<DecisionFact>());
    private static CourseComparisonEpisode Episode(IEnumerable<Opportunity> sites)
        => new(1, 1, 10, sites.SelectMany(site => site.Needs), true, false, "fixture");

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
        public BindingValidation ValidateNextUse(StepBinding binding, DecisionFactSnapshot facts)
            => new(OpportunityAdmission.KnownUsable, "fixture", false);
        public BindingResult Bind(Opportunity opportunity, ProjectedCourseState state, TrackedFactReader facts,
            DecisionWorkCursor cursor, DecisionWorkBudget budget)
        {
            if (!budget.TrySpend("fixture-bind")) return new(null, OpportunityAdmission.Unresolved, "budget-cut", true);
            Uses++; long id = CourseIdentity.Next();
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
