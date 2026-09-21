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
        Row("G12 harm stays unresolved even with an empty census", HarmIsNeverProvenAbsent);
        Row("G12 a second candidate order is priced from its own pose, not the first's", EachOrderIsPricedFromItsOwnStart);
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

    private static ForecastCourseConsequences Forecast() => new(new CoursePoint(48, 80));

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
    private static CourseProjectionResult Price()
    {
        var owner = new RetainCourseModelQueries(Snapshot(), World(), 2, 4);
        return Settle(Forecast(), owner, new CoursePoint(240, 80));
    }

    /// <summary>A suspended pricing must name what it waits on. The domain cursor stays on the candidate
    /// needing the answer, so a request that is never forwarded parks that candidate for ever.</summary>
    private static void PendingForwardsItsTravelRequest()
    {
        DecisionFactSnapshot facts = Snapshot();
        CourseProjectionResult first = Forecast().Continue(Array.Empty<StepBinding>(), Successor(facts),
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
        CourseProjectionResult result = Price();
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

    /// <summary>
    /// Harm is never proven absent, and an empty census is not the exception it looks like.
    ///
    /// An earlier version of this provider resolved the tail when a finished census held no hostile.
    /// That read proof about one instant of `Main.npc[]` as proof about a whole projected journey
    /// including the return leg, and it certified zero harm to the *player* too, about which a
    /// hostile-slot census says nothing. Permanently uncertain is the honest state until harm is
    /// modelled, and it costs only that no course can yet be preferred for being safer.
    /// </summary>
    private static void HarmIsNeverProvenAbsent()
    {
        CourseProjectionResult empty = Price();
        Require(empty.Projection is { } priced && priced.TailUnresolved,
            "an empty census resolved the tail, which reads a moment's observation as proof about a horizon and says nothing about player harm at all");
        Require(empty.Projection!.Harm.Count == 0, "a harm was reported that no model produced");
    }

    /// <summary>
    /// The row the `BeginOrder` leak lived under. One forecast object prices two different candidate
    /// orders in a search, and each must be priced from its own starting state.
    ///
    /// It exists because the two fixtures either side of this seam each used a stand-in for the other:
    /// this file drove the real forecast against a hand-built snapshot and never through `BindCourseOrder`,
    /// while `VerifyCourseOrderProjection` drove the real binder against a fixture forecast. The leak sat
    /// exactly in the gap — a retained companionship leg handed to every later order verbatim, priced from
    /// a pose it had never seen and asking for no travel to reach it. With harm unresolved, companionship
    /// is the only cost term, so every candidate order cost the same.
    /// </summary>
    private static void EachOrderIsPricedFromItsOwnStart()
    {
        DecisionFactSnapshot facts = Snapshot();
        var owner = new RetainCourseModelQueries(facts, World(), 2, 4);
        var forecast = new ForecastCourseConsequences(new CoursePoint(48, 80));

        CourseProjectionResult first = Settle(forecast, owner, new CoursePoint(240, 80));
        CourseProjectionResult second = Settle(forecast, owner, new CoursePoint(64, 88));

        Require(first.Projection != null && second.Projection != null,
            "one of the two candidate orders never priced at all");
        Require(first.Projection!.Companionship.Count > 0 && second.Projection!.Companionship.Count > 0,
            "a candidate order priced with no companionship interval");
        // Two starting poses at genuinely different distances from the same reunion point cannot honestly
        // produce one identical cost. Equality here is the retained leg being reused, not a coincidence.
        Require(first.Projection!.ReunionTick != second.Projection!.ReunionTick
                || !first.Projection!.Companionship.SequenceEqual(second.Projection!.Companionship),
            $"two orders starting {240 - 64} px apart priced identically, so the second was handed the first's retained leg; end={first.Projection!.ReunionTick}");
    }

    /// <summary>Drives one order to a terminal answer through the model queue, from a given start.</summary>
    private static CourseProjectionResult Settle(ForecastCourseConsequences forecast,
        RetainCourseModelQueries owner, CoursePoint start)
    {
        for (int slice = 0; slice < 200; slice++)
        {
            var successor = new ProjectedCourseState(start, owner.Snapshot);
            CourseProjectionResult result = forecast.Continue(Array.Empty<StepBinding>(), successor,
                owner.Snapshot, Episode(), new DecisionWorkCursor(), new(double.PositiveInfinity));
            if (result.Status != ProjectionStatus.Pending) return result;
            foreach (CourseTravelRequest request in result.RequiredTravel ?? Array.Empty<CourseTravelRequest>())
                owner.RequestTravel(request);
            var budget = new DecisionWorkBudget(double.PositiveInfinity, 64);
            using (LimitPlanningWork.Own(budget)) owner.Continue(budget);
        }
        throw new InvalidOperationException("an order never settled within 200 model slices");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
