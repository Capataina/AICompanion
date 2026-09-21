extern alias live;

using System;
using System.Linq;
using System.Text.Json;
using AICompanion.Tools.Ledger;
using live::AICompanion.Companion.Brain.Infrastructure.Movement;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

/// <summary>
/// The native consequence provider's honesty, which is the only thing between a partially built cost
/// model and a companion that prefers danger.
///
/// `ForecastCourseConsequences` prices companionship and the return leg for real and cannot yet price
/// predicted contact harm, because that needs modelled enemy motion arriving asynchronously through the
/// observation owner's model queue. The dangerous way to ship that state is to return Complete with an
/// empty harm list: every comparison would read "no predicted harm" and actively prefer the course that
/// walks through the most hostiles. The safe way is an unresolved tail, which by the Courses contract
/// cannot certify strict superiority — so an unpriced course may be chosen as a legal first action and
/// can never be proven better than a safer rival.
///
/// Nothing about the code's shape says which of those two it is, which is why these rows exist.
/// </summary>
internal static class VerifyNativeConsequencePricing
{
    public static int Run()
    {
        int red = 0;
        void Row(string name, Action test)
        {
            try { test(); Console.WriteLine("GREEN " + name); }
            catch (Exception error) { red++; Console.WriteLine("RED " + name + ": " + error.Message); }
        }
        Row("G12 unanswered travel suspends the pricing and forwards its request", PendingForwardsItsTravelRequest);
        Row("G12 an unpriced harm is unresolved, never zero", HostilesLeaveTheTailUnresolved);
        Row("G12 an empty finished census is the one case harm may read as none", EmptyCensusMayResolve);
        return red;
    }

    private static TextTileWorld World() => new(0, 0, new[]
    {
        "####################", "#..................#", "#..................#", "#..................#",
        "#..................#", "#..................#", "#..................#", "#..................#",
        "#..................#", "####################"
    });

    /// <summary>The frozen observation these rows price against. It is hand-built rather than taken
    /// from `AssembleCourseSnapshot` because the travel below is solved in the text world above, and a
    /// snapshot whose region came from a native floor would be priced against geometry it never saw.
    /// The assembler has its own rows in `Observation/VerifyCourseSnapshotAssembly.cs`.</summary>
    private static DecisionFactSnapshot Snapshot() => new(1, 1, 100, 1, 0, new[]
    {
        new DecisionFact(CapturedCompanionshipRegion.Key, 1, new(Text: JsonSerializer.Serialize(
            new CapturedCompanionshipRegion(new(48, 80), new(20, 20), default, 100, 100, true))), FactEvidence.Observed)
    });

    private static ForecastCourseConsequences Forecast(int hostiles, bool censusComplete)
        => new(new CoursePoint(48, 80), hostiles, censusComplete);

    private static CourseComparisonEpisode Episode()
        => new(1, 1, 10, Array.Empty<UsefulNeed>(), true, false, "consequence-fixture");

    private static ProjectedCourseState Successor(DecisionFactSnapshot facts)
        => new(new CoursePoint(240, 80), facts);

    /// <summary>
    /// Prices an empty order, driving the observation owner's model queue exactly the way the live
    /// coordinator will have to: the forecast suspends on a travel it cannot compute inside hypothetical
    /// evaluation, the owner answers it against the frozen world, and the same forecast resumes on the
    /// model-extended snapshot rather than restarting.
    /// </summary>
    private static CourseProjectionResult Price(int hostiles, bool censusComplete)
    {
        DecisionFactSnapshot facts = Snapshot();
        var owner = new RetainCourseModelQueries(facts, World(), 2, 4);
        var forecast = Forecast(hostiles, censusComplete);

        for (int slice = 0; slice < 200; slice++)
        {
            CourseProjectionResult result = forecast.Continue(Array.Empty<StepBinding>(), Successor(owner.Snapshot),
                owner.Snapshot, Episode(), new DecisionWorkCursor(), new(double.PositiveInfinity));
            if (result.Status != ProjectionStatus.Pending) return result;

            Require(result.RequiredTravel is { Count: > 0 },
                "the pricing suspended without naming the travel it is waiting on, so nothing can ever answer it");
            foreach (CourseTravelRequest request in result.RequiredTravel!)
                owner.RequestTravel(request);
            var budget = new DecisionWorkBudget(double.PositiveInfinity, 64);
            // The owner's travel capture checks it is borrowing the active allowance, so the slice's
            // own budget has to be the standing one rather than sit beside it.
            using (LimitPlanningWork.Own(budget))
                owner.Continue(budget);
        }
        throw new InvalidOperationException("the pricing never settled within 200 model slices");
    }

    /// <summary>A suspended pricing must name what it waits on. The domain cursor stays on the candidate
    /// needing the answer, so a request that is never forwarded parks that candidate for ever.</summary>
    private static void PendingForwardsItsTravelRequest()
    {
        DecisionFactSnapshot facts = Snapshot();
        CourseProjectionResult first = Forecast(0, true).Continue(Array.Empty<StepBinding>(), Successor(facts),
            facts, Episode(), new DecisionWorkCursor(), new(double.PositiveInfinity));
        Require(first.Status == ProjectionStatus.Pending,
            $"a pricing with no captured travel should suspend; status={first.Status}");
        Require(first.RequiredTravel is { Count: > 0 },
            "the suspended pricing forwarded no travel request, so the observation owner has nothing to answer");
        Require(first.Projection == null, "a suspended pricing published a projection");
    }

    /// <summary>The load-bearing row. A hostile the census saw and no harm model means the tail is
    /// unresolved; the alternative is an empty harm list that reads as safety.</summary>
    private static void HostilesLeaveTheTailUnresolved()
    {
        CourseProjectionResult result = Price(hostiles: 3, censusComplete: true);
        Require(result.Status == ProjectionStatus.Complete,
            $"answered travel should complete the pricing; status={result.Status} reason={result.Reason}");
        Require(result.Projection is { } priced && priced.TailUnresolved,
            "three hostiles in the census were priced with a resolved tail, so an unmodelled harm reads as no harm");
        Require(result.Projection!.Harm.Count == 0, "a harm was reported that no model produced");
        Require(result.Projection!.Companionship.Count > 0,
            "an empty order produced no companionship interval, so idling would be free");
        Require(result.Projection!.ConsequenceDependencies.Reads.Count > 0,
            "the pricing published an empty consequence manifest, which asserts a calculation with no captured inputs and can never be dirtied when they change");
    }

    /// <summary>The one case an empty harm list is the truth, and the one beside it that is not.</summary>
    private static void EmptyCensusMayResolve()
    {
        CourseProjectionResult finished = Price(hostiles: 0, censusComplete: true);
        Require(finished.Projection is { } priced && !priced.TailUnresolved,
            "a finished census holding no hostile still refused to resolve its tail, which would leave every course permanently uncertain");

        CourseProjectionResult partial = Price(hostiles: 0, censusComplete: false);
        Require(partial.Projection is { } unfinished && unfinished.TailUnresolved,
            "an unfinished census with nothing found so far read as proven safety, which is the exhausted-bound mistake one layer up");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
