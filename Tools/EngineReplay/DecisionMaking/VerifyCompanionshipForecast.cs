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
        + RunOneRow.Case("G15 companionship requires timed travel and actual arrival evidence", ArrivalEvidence);

    private static DecisionFact RegionFact() => new(CapturedCompanionshipRegion.Key, 1,
        new(Text: JsonSerializer.Serialize(new CapturedCompanionshipRegion(default, new(10, 10), default, 100, 100, true))),
        FactEvidence.Observed);
    private static DecisionFact Leg(CoursePoint from, CoursePoint to, double duration, CoursePoint? arrived = null, bool timed = true)
        => new(ReadCourseTravel.Key(from, default, to), 1, new(Text: JsonSerializer.Serialize(new CapturedCourseTravel(
            from, default, to, default, duration, OpportunityAdmission.KnownUsable, "fixture", new[] { from, to }, 1,
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
        var harm = new ForecastContactHarm(new[] { new ContactActor(HarmActor.Companion, 100, 0, sampled!.Boxes) },
            new[] { new ContactThreat(1, 1, contact, contact) }, 15, true).Continue(new(double.PositiveInfinity));
        Require(harm is { TailUnresolved: true } && harm.Harm.Count == 1 && harm.Harm[0].Tick == 13,
            "contact harm did not consume the companionship return trajectory or concealed its uncovered tail");
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
