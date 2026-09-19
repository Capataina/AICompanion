using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>Frozen inputs to the existing region forecast. The travel horizon is the
/// captured observation model's limit, not a claim that the player stops afterwards.</summary>
public readonly record struct CapturedCompanionshipRegion(CoursePoint Centre, CoursePoint HalfSize,
    CoursePoint Travel, double TravelHorizon, float RecoveryRadius, bool PlayerAlive);

/// <summary>Splits a coarse linear body segment wherever the shared rectangular gap curve
/// changes slope. The resulting intervals feed the canonical discounted gap integral.</summary>
public static class ForecastCompanionshipGap
{
    public static IReadOnlyList<CompanionshipInterval> Between(TimedCoursePose start, TimedCoursePose end,
        CapturedCompanionshipRegion region)
    {
        if (!double.IsFinite(start.Tick) || !double.IsFinite(end.Tick) || start.Tick < 0 || end.Tick < start.Tick
            || !Finite(start.Position) || !Finite(end.Position) || !Finite(region.Centre) || !Finite(region.HalfSize)
            || !Finite(region.Travel) || region.HalfSize.X < 0 || region.HalfSize.Y < 0
            || !double.IsFinite(region.TravelHorizon) || region.TravelHorizon < 0
            || !float.IsFinite(region.RecoveryRadius) || region.RecoveryRadius < 0)
            throw new ArgumentException("Companionship forecast requires finite geometry and an ordered nonnegative clock.");
        var intervals = new List<CompanionshipInterval>();
        if (end.Tick == start.Tick) return intervals.AsReadOnly();
        if (!region.PlayerAlive)
        {
            intervals.Add(new(start.Tick, end.Tick, 0, 0, EstimateStatus.NativeBound));
            return intervals.AsReadOnly();
        }
        double split = region.TravelHorizon;
        if (start.Tick < split && split < end.Tick)
        {
            var middle = new TimedCoursePose(split, Interpolate(start, end, split), default);
            AddLinear(start, middle, region, intervals);
            AddLinear(middle, end, region, intervals);
        }
        else AddLinear(start, end, region, intervals);
        return intervals.AsReadOnly();
    }

    private static void AddLinear(TimedCoursePose start, TimedCoursePose end, CapturedCompanionshipRegion region,
        List<CompanionshipInterval> intervals)
    {
        CoursePoint a = Relative(start, region), b = Relative(end, region);
        if (!Finite(a) || !Finite(b))
            throw new ArgumentException("Projected player travel exceeds representable region geometry.");
        double span = Math.Max(1, region.RecoveryRadius - Math.Max(region.HalfSize.X, region.HalfSize.Y));
        // Gap is max(0, x-hx, -x-hx, y-hy, -y-hy), capped at span after scaling.
        // Every possible change of the active line occurs at a pairwise intersection.
        // Including the cap as a line also locates entry to the flat outer plateau.
        var lines = new (double A, double B)[]
        {
            (0, 0), (a.X - region.HalfSize.X, b.X - a.X),
            (-a.X - region.HalfSize.X, a.X - b.X),
            (a.Y - region.HalfSize.Y, b.Y - a.Y),
            (-a.Y - region.HalfSize.Y, a.Y - b.Y), (span, 0),
        };
        var cuts = new SortedSet<double> { 0, 1 };
        for (int i = 0; i < lines.Length; i++)
        for (int j = i + 1; j < lines.Length; j++)
        {
            double slope = lines[i].B - lines[j].B;
            if (slope == 0) continue;
            double crossing = (lines[j].A - lines[i].A) / slope;
            if (crossing > 0 && crossing < 1) cuts.Add(crossing);
        }
        double previous = 0;
        foreach (double next in cuts)
        {
            if (next == 0) continue;
            double left = start.Tick + (end.Tick - start.Tick) * previous;
            double right = start.Tick + (end.Tick - start.Tick) * next;
            intervals.Add(new(left, right, Pull(a, b, previous, region), Pull(a, b, next, region), EstimateStatus.Nominal));
            previous = next;
        }
    }

    private static double Pull(CoursePoint start, CoursePoint end, double fraction, CapturedCompanionshipRegion region)
    {
        float x = (float)(start.X + (end.X - start.X) * fraction);
        float y = (float)(start.Y + (end.Y - start.Y) * fraction);
        float gap = MeasureCompanionshipGap.Beyond(x, y, (float)region.HalfSize.X, (float)region.HalfSize.Y);
        return MeasureCompanionshipGap.Pull(gap, (float)region.HalfSize.X, (float)region.HalfSize.Y, region.RecoveryRadius);
    }

    private static CoursePoint Relative(TimedCoursePose sample, CapturedCompanionshipRegion region)
    {
        double travel = Math.Min(sample.Tick, region.TravelHorizon);
        return new(sample.Position.X - region.Centre.X - region.Travel.X * travel,
            sample.Position.Y - region.Centre.Y - region.Travel.Y * travel);
    }
    private static CoursePoint Interpolate(TimedCoursePose start, TimedCoursePose end, double tick)
    {
        double fraction = (tick - start.Tick) / (end.Tick - start.Tick);
        return new(start.Position.X + (end.Position.X - start.Position.X) * fraction,
            start.Position.Y + (end.Position.Y - start.Position.Y) * fraction);
    }
    private static bool Finite(CoursePoint point) => double.IsFinite(point.X) && double.IsFinite(point.Y)
        && Math.Abs(point.X) <= float.MaxValue && Math.Abs(point.Y) <= float.MaxValue;
}
