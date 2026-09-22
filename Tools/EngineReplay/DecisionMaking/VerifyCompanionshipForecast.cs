extern alias live;

using System;
using System.Linq;
using System.Text.Json;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

internal static class VerifyCompanionshipForecast
{
    public static int Run()
        => RunOneRow.Case("G03 a region crossing charges its actual gap intervals", Crossing)
        + RunOneRow.Case("G03 companionship costing is invariant to trajectory subdivision", Subdivision)
        + RunOneRow.Case("G03 region forecast horizon remains nominal and dead-player cost is absent", Horizon)
        + RunOneRow.Case("G11 a whole companionship course resumes through its missing return leg", WholeCourse)
        + RunOneRow.Case("G15 companionship requires timed travel and actual arrival evidence", ArrivalEvidence)
        + RunOneRow.Case("G15 a way home a search proved absent is not a free way home", AbsentRouteIsNotFree);

    private static DecisionFact RegionFact() => new(CapturedCompanionshipRegion.Key, 1,
        new(Text: JsonSerializer.Serialize(new CapturedCompanionshipRegion(default, new(10, 10), default, 100, 100, true))),
        FactEvidence.Observed);
    private static DecisionFact Leg(CoursePoint from, CoursePoint to, double duration, CoursePoint? arrived = null, bool timed = true,
        OpportunityAdmission admission = OpportunityAdmission.KnownUsable, string reason = "fixture")
        => new(ReadCourseTravel.Key(from, default, to), 1, new(Text: JsonSerializer.Serialize(new CapturedCourseTravel(
            from, default, to, default, duration, admission, reason, new[] { from, to }, 1,
            timed ? new[] { new TimedCoursePose(0, from, default), new TimedCoursePose(duration, arrived ?? to, default) } : null))),
            FactEvidence.Modelled);
    private static DecisionFactSnapshot Snapshot(params DecisionFact[] facts) => new(1, 1, 100, 1, 0, facts);
    private static StepBinding Work(CoursePoint pose) => new(1, new("fixture", "use", "cost", 1), "use", pose, "tool", 1, 1,
        4, 3, 0, Array.Empty<ResourcePhase>(), Array.Empty<PredictedEffect>(), Array.Empty<long>(), DependencyManifest.Empty, true);

    private static void WholeCourse()
    {
        var from = new CoursePoint(20, 0); var work = new CoursePoint(60, 0);
        var outbound = Leg(from, work, 4); var home = Leg(work, default, 6);
        var partial = Snapshot(RegionFact(), outbound);
        var model = new ForecastCourseCompanionship(partial, new[] { Work(work) }, from, default, default);
        var pending = model.Continue(partial, new(double.PositiveInfinity));
        Require(pending.Status == ProjectionStatus.Pending && model.MissingTravel?.Key == home.Key && pending.EndTick == 7,
            "the forecast omitted work duration or could not identify its missing return query");
        var complete = Snapshot(partial.Facts.Append(home).ToArray());
        CourseCompanionshipResult result = pending;
        for (int slice = 0; slice < 8 && result.Status == ProjectionStatus.Pending; slice++)
            result = model.Continue(complete, new(double.PositiveInfinity, 1));
        Require(result.Status == ProjectionStatus.Complete && result.EndTick == 13 && result.NominallyRejoined
            && result.Dependencies.Complete && result.Dependencies.Reads.Count == 3,
            "return completion lost its clock, reunion evidence or fact provenance");
        var uninterrupted = new ForecastCourseCompanionship(complete, new[] { Work(work) }, from, default, default)
            .Continue(complete, new(double.PositiveInfinity));
        Require(result.Intervals.SequenceEqual(uninterrupted.Intervals)
            && result.BodyTrajectory.SequenceEqual(uninterrupted.BodyTrajectory)
            && result.Intervals.First().StartTick == 0 && result.Intervals.Last().EndTick == 13
            && result.Intervals.Zip(result.Intervals.Skip(1)).All(pair => pair.First.EndTick == pair.Second.StartTick),
            "suspending the forecast duplicated or omitted part of the complete course");
        Require(result.BodyTrajectory.Select(p => p.Tick).SequenceEqual(new double[] { 0, 4, 7, 13 })
            && pending.BodyTrajectory.Select(p => p.Tick).SequenceEqual(new double[] { 0, 4, 7 }),
            "the shared body timeline omitted waiting, changed a prior result or duplicated a resumed leg");
        var sampler = new SampleContactTrajectory(result.BodyTrajectory, 2, 2, 15);
        ContactTrajectoryResult? sampled = null;
        for (int i = 0; i < 30 && sampled == null; i++) sampled = sampler.Continue(new(double.PositiveInfinity, 1));
        Require(sampled is { Complete: false } && sampled.Boxes.Count == 14
            && sampled.Boxes[4] == sampled.Boxes[7], "contact sampling invented future coverage or lost the use interval");
        var contact = new ContactGeometry(Enumerable.Repeat(new ContactSample(new(-1, -1, 2, 2), 10, 0), 16).ToArray(), true);
        // The trajectory genuinely runs out — fourteen boxes against a horizon of fifteen — and the tail
        // is still resolved, which is a sharper answer rather than a lost one. The hit at tick 13 makes
        // this body immune for thirty ticks, so ticks 14 and 15 cannot hurt it whatever the geometry
        // there would have said, and reporting them as unknown would charge a course for uncertainty
        // about a window in which the answer is known. Before the immunity window existed this row
        // asserted the opposite and was right to: a hit ended the scan and the rest was unknown.
        // An actor whose immunity expires *inside* a short trajectory still reports unresolved, which is
        // the property `VerifyProjectionContracts` keeps.
        var harm = new ForecastContactHarm(new[] { new ContactActor(HarmActor.Companion, 100, 0, 30, 30, sampled!.Boxes) },
            new[] { new ContactThreat(1, 1, contact, contact) }, 15, true).Continue(new(double.PositiveInfinity));
        Require(harm is { TailUnresolved: false } && harm.Harm.Count == 1 && harm.Harm[0].Tick == 13,
            $"contact harm did not consume the companionship return trajectory, or reported an uncovered "
            + $"tail inside a window its own immunity answers; got {harm?.Harm.Count} hit(s), tail "
            + $"unresolved {harm?.TailUnresolved}");
    }

    private static void ArrivalEvidence()
    {
        var from = new CoursePoint(60, 0);
        var missing = Snapshot(RegionFact(), Leg(from, default, 6, timed: false));
        Require(new ForecastCourseCompanionship(missing, Array.Empty<StepBinding>(), from, default, default)
            .Continue(missing, new(double.PositiveInfinity)).Reason == "timed-travel-evidence-unresolved",
            "missing trajectory evidence became a free return");
        var outside = Snapshot(RegionFact(), Leg(from, default, 6, new(20, 0)));
        var result = new ForecastCourseCompanionship(outside, Array.Empty<StepBinding>(), from, default, default)
            .Continue(outside, new(double.PositiveInfinity));
        Require(result.Status == ProjectionStatus.Complete && !result.NominallyRejoined,
            "a destination inside the region overrode the body's actual outside arrival");
    }

    /// <summary>
    /// The third of the three answers a way home can give, and the one that had no witness anywhere until
    /// 22 September 2026. `WholeCourse` covers a travel fact that has not arrived yet — the forecast goes
    /// `Pending` and names the query it is waiting on — and `ArrivalEvidence` covers a fact that arrived
    /// without a timed trajectory. Neither covers a search that *finished* and proved there is no way
    /// home, which reaches `ForecastCourseCompanionship.Continue`'s admission branch and nothing else in
    /// this suite ever drove.
    ///
    /// <para>The property is the retired family chooser's, harvested when it was deleted: neither an
    /// unknown route nor an absent one may read as a free route. Unknown is the bounded-search third
    /// value this whole tree is built on, and absent is a proven negative — a course that treats either
    /// as costless prices an excursion it can never come back from at the price of one it can. So the
    /// row asserts both halves of the answer rather than only that it was refused: the projection is
    /// `Rejected`, and it is rejected carrying the *search's own* reason rather than a generic one,
    /// because a companion sealed off from its player and a companion whose query was malformed are
    /// different findings and a reader of a capture has only the reason string to tell them apart.</para>
    ///
    /// <para>Proved by mutation on 22 September 2026: deleting the admission test in
    /// `ForecastCourseCompanionship.Continue` reddens it with `status=Complete reason=nominal-reunion`,
    /// and the sibling rows stay green. That reason is the detail worth keeping: without the admission
    /// test the sealed-off course does not merely go uncosted, it reports a *nominal reunion* — the
    /// forecast says the body got home — so the failure this row prevents is a positive false claim
    /// rather than a missing one. Restored.</para>
    /// </summary>
    private static void AbsentRouteIsNotFree()
    {
        var from = new CoursePoint(60, 0);
        // A finished search with a negative answer, which is what the reach flood produces when it
        // exhausts: the fact is present and modelled, its trajectory is well formed, and the only thing
        // wrong with it is that there is no way home. Everything but the admission is deliberately valid,
        // so nothing else in the chain can be what refuses it.
        var sealedOff = Snapshot(RegionFact(),
            Leg(from, default, 6, admission: OpportunityAdmission.KnownUnusable, reason: "way-home-unreachable"));
        var result = new ForecastCourseCompanionship(sealedOff, Array.Empty<StepBinding>(), from, default, default)
            .Continue(sealedOff, new(double.PositiveInfinity));
        Require(result.Status == ProjectionStatus.Rejected,
            $"a way home no search could prove was priced as if the body could simply fly it; "
            + $"status={result.Status} reason={result.Reason}");
        Require(result.Reason == "way-home-unreachable",
            $"the refusal dropped the search's own reason, so a capture cannot tell a sealed-off companion "
            + $"from a malformed query; reason={result.Reason}");
    }

    private static double Cost(TimedCoursePose start, TimedCoursePose end, CapturedCompanionshipRegion region)
        => ForecastCompanionshipGap.Between(start, end, region).Sum(interval => CompareCourseOutcomes.GapIntegral(
            interval.StartTick, interval.EndTick, interval.GapAtStart, interval.GapAtEnd, 10));

    private static void Crossing()
    {
        var region = new CapturedCompanionshipRegion(default, new(10, 10), default, 100, 30, true);
        var start = new TimedCoursePose(0, new(-30, 0), default);
        var end = new TimedCoursePose(10, new(30, 0), default);
        double actual = Cost(start, end, region), reference = 0;
        const int slices = 10000;
        for (int i = 0; i < slices; i++)
        {
            double tick = (i + .5) * 10 / slices;
            double gap = Math.Clamp((Math.Abs(-30 + 6 * tick) - 10) / 20, 0, 1);
            reference += gap * Math.Exp(-tick / 10) / slices;
        }
        Require(Math.Abs(actual - reference) < 1e-6 && actual < CompareCourseOutcomes.GapIntegral(0, 10, 1, 1, 10),
            "endpoint interpolation charged separation while the drone crossed the region interior");
    }

    private static void Subdivision()
    {
        var region = new CapturedCompanionshipRegion(new(3, -7), new(10, 15), new(2, -1), 4, 50, true);
        var start = new TimedCoursePose(0, new(-60, 20), default);
        var middle = new TimedCoursePose(3, new(-18, 2), default);
        var end = new TimedCoursePose(10, new(80, -40), default);
        Require(Math.Abs(Cost(start, end, region) - Cost(start, middle, region) - Cost(middle, end, region)) < 1e-6,
            "adding an otherwise identical trajectory sample changed the course cost");
    }

    private static void Horizon()
    {
        var region = new CapturedCompanionshipRegion(default, new(1, 1), new(2, 0), 5, 20, true);
        var start = new TimedCoursePose(0, default, default);
        var end = new TimedCoursePose(10, default, default);
        var intervals = ForecastCompanionshipGap.Between(start, end, region);
        Require(intervals.Any(interval => interval.EndTick == 5) && intervals.All(interval => interval.Evidence == EstimateStatus.Nominal),
            "player travel horizon was ignored or turned into a certified stopping prediction");
        Require(Cost(start, end, region with { PlayerAlive = false }) == 0,
            "a dead player retained an ordinary companionship cost");
    }
    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }
}
