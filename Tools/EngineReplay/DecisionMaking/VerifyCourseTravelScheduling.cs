extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using live::AICompanion.Companion.Brain.Infrastructure.Movement;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

internal static class VerifyCourseTravelScheduling
{
    public static int Run()
        => RunOneRow.Case("G11 native travel queries share one-operation turns without evicting work", FairNativeQueries);

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
