extern alias live;

using System;
using System.Linq;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

internal static class VerifyCompanionshipForecast
{
    public static int Run()
        => RunOneRow.Case("G03 a region crossing charges its actual gap intervals", Crossing)
        + RunOneRow.Case("G03 companionship costing is invariant to trajectory subdivision", Subdivision)
        + RunOneRow.Case("G03 region forecast horizon remains nominal and dead-player cost is absent", Horizon);

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
