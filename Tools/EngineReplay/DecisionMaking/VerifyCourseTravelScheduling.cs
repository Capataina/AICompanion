extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using live::AICompanion.Companion.Brain.Infrastructure.Movement;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

internal static class VerifyCourseTravelScheduling
{
    public static int Run()
        => RunOneRow.Case("G11 native travel queries share one-operation turns without evicting work", FairNativeQueries)
        + RunOneRow.Case("G11 native model completion resumes a frozen companionship forecast", ModelOwner)
        + RunOneRow.Case("G08 deferred queries cannot certify terrain edited since observation", DeferredTerrainEdit)
        + RunOneRow.Case("G11 course search drives missing native models with one shared operation", SearchOwner);

    private static void SearchOwner()
    {
        var original = Snapshot();
        var owner = new RetainCourseModelQueries(original, World(), 1, 1);
        var projector = new AwaitTravel();
        var search = new SearchCourseOrders(1);
        search.Begin(original, new(1, 1, 10, Array.Empty<UsefulNeed>(), true, false, "fixture"),
            Array.Empty<Opportunity>(), Array.Empty<OpportunityKey>(), projector);
        for (int tick = 0; tick < 1000 && !search.Exhausted; tick++)
        {
            var budget = new DecisionWorkBudget(double.PositiveInfinity, 1);
            owner.ContinueSearch(search, budget);
            Require(budget.OperationsUsed <= 1, "search and native model invented separate operation allowances");
        }
        Require(search.Exhausted && search.RejectedOrders == 1 && projector.Answered
            && owner.CompletedCount == 1 && search.RequiredTravel.Count == 0,
            "the pending order failed to request, resume or retire its native query");
        Require(!original.TryRead(projector.Query.Key, out _), "search completion mutated its original observation");
    }

    private sealed class AwaitTravel : ICourseProjector
    {
        public readonly CourseTravelRequest Query = new(new(48, 80), default, new(49, 80));
        public bool Answered;
        public CourseProjectionResult Continue(IReadOnlyList<OpportunityKey> order, DecisionFactSnapshot facts,
            CourseComparisonEpisode episode, DecisionWorkCursor cursor, DecisionWorkBudget budget)
        {
            if (!budget.TrySpend("fixture-projector")) return new(ProjectionStatus.Pending, null, "budget-cut");
            if (!facts.TryRead(Query.Key, out var answer))
                return new(ProjectionStatus.Pending, null, "native-travel-pending", new[] { Query });
            Answered = answer.Evidence == FactEvidence.Modelled;
            // This fixture proves transport and resumption, not a complete consequence model.
            return new(ProjectionStatus.Rejected, null, "fixture-transport-complete");
        }
    }

    private static TextTileWorld World() => new(0, 0, new[]
    {
        "####################", "#..................#", "#..................#", "#..................#",
        "#..................#", "#..................#", "#..................#", "#..................#",
        "#..................#", "####################"
    });
    private static DecisionFactSnapshot Snapshot() => new(1, 1, 100, 1, 0, new[]
    {
        new DecisionFact(CapturedCompanionshipRegion.Key, 1, new(Text: JsonSerializer.Serialize(
            new CapturedCompanionshipRegion(new(48, 80), new(20, 20), default, 100, 100, true))), FactEvidence.Observed)
    });

    private static void ModelOwner()
    {
        var original = Snapshot();
        var owner = new RetainCourseModelQueries(original, World(), 1, 2);
        var forecast = new ForecastCourseCompanionship(original, Array.Empty<StepBinding>(), new(48, 80), default, new(49, 80));
        Require(forecast.Continue(original, new(double.PositiveInfinity)).Status == ProjectionStatus.Pending
            && forecast.MissingTravel.HasValue, "the empty course did not request its native return model");
        var query = forecast.MissingTravel!.Value;
        Require(owner.RequestTravel(query), "model owner refused a free pending slot");
        for (int i = 0; i < 1000 && owner.PendingCount > 0; i++) owner.Continue(new(double.PositiveInfinity, 1));
        Require(owner.CompletedCount == 1 && owner.Snapshot.IsModelExtensionOf(original)
            && !original.TryRead(query.Key, out _), "native completion mutated the old snapshot or failed to append its model");
        Require(forecast.Continue(owner.Snapshot, new(double.PositiveInfinity)) is
            { Status: ProjectionStatus.Complete, NominallyRejoined: true }, "native query completion failed to resume consequence costing");
        Require(owner.RequestTravel(query) && owner.PendingCount == 0 && !owner.Continue(new(double.PositiveInfinity)),
            "an already captured query was rescheduled or republished");
        owner.Abandon();
        bool refused = false;
        try { owner.RequestTravel(query); } catch (InvalidOperationException) { refused = true; }
        Require(refused, "an abandoned observation accepted more native work");
    }

    private static void DeferredTerrainEdit()
    {
        var world = World(); var owner = new RetainCourseModelQueries(Snapshot(), world, 1, 1);
        world.Set(3, 5, '#');
        var query = new CourseTravelRequest(new(48, 80), default, new(49, 80));
        Require(owner.RequestTravel(query), "the deferred terrain fixture needs a pending query");
        for (int i = 0; i < 1000 && owner.PendingCount > 0; i++) owner.Continue(new(double.PositiveInfinity, 1));
        Require(owner.Snapshot.TryRead(query.Key, out var fact) && fact.Evidence == FactEvidence.Unresolved
            && fact.Value.Text.Contains("travel-observation-terrain-changed", StringComparison.Ordinal),
            "a query created after a terrain edit certified the new world as the earlier observation");
    }

    private static void FairNativeQueries()
    {
        var world = new TextTileWorld(0, 0, new[]
        {
            "####################", "#..................#", "#..................#", "#........##........#",
            "#........##........#", "#........##........#", "#........##........#", "#..................#",
            "#..................#", "####################"
        });
        var far = new CourseTravelRequest(new(48, 80), default, new(272, 80));
        var near = new CourseTravelRequest(new(48, 80), default, new(49, 80));
        var scheduler = new ScheduleCourseTravel(world, 1, 2);
        Require(scheduler.Request(far) && scheduler.Request(near) && scheduler.Request(far) && scheduler.PendingCount == 2,
            "duplicate pending requests reset or duplicate work");
        Require(!scheduler.Request(new(new(48, 80), default, new(48, 96))) && scheduler.CapacityRefusals == 1,
            "capacity pressure silently displaced an unfinished route");
        Require(scheduler.Continue(new(double.PositiveInfinity, 0)).Count == 0 && scheduler.PendingCount == 2,
            "an empty allowance performed or erased pending work");
        var completed = new List<DecisionFact>();
        for (int slice = 0; slice < 20000 && scheduler.PendingCount > 0; slice++)
        {
            var budget = new DecisionWorkBudget(double.PositiveInfinity, 1);
            completed.AddRange(scheduler.Continue(budget));
            Require(budget.OperationsUsed <= 1, "a query invented an allowance outside the shared budget");
        }
        Require(completed.Count == 2 && completed[0].Key == near.Key && completed[1].Key == far.Key
            && completed.All(fact => fact.Evidence == FactEvidence.Modelled) && scheduler.CompletedCount == 2,
            "the long first query starved the short query or lost a retained route");
        Require(scheduler.Request(far), "a released pending slot remained permanently occupied");
        scheduler.Clear();
        Require(scheduler.PendingCount == 0 && scheduler.Continue(new(double.PositiveInfinity)).Count == 0,
            "world reset retained a queued native query");
    }
    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }
}
