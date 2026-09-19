using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

public sealed record ContactTrajectoryResult(IReadOnlyList<ContactBox> Boxes, int RequestedHorizon, bool Complete);

/// <summary>Samples the course's coarse body timeline on the contact model's tick clock.
/// Interpolation is nominal; absent trajectory coverage never becomes stationary waiting.</summary>
public sealed class SampleContactTrajectory
{
    private readonly TimedCoursePose[] trajectory;
    private readonly double width, height;
    private readonly int horizon;
    private readonly List<ContactBox> boxes = new();
    private int segment;
    private ContactTrajectoryResult? result;

    public SampleContactTrajectory(IEnumerable<TimedCoursePose> trajectory, double width, double height, int horizon)
    {
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0 || horizon < 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Contact dimensions must be positive and the horizon nonnegative.");
        this.trajectory = trajectory.ToArray(); this.width = width; this.height = height; this.horizon = horizon;
        if (this.trajectory.Any(p => !double.IsFinite(p.Tick) || p.Tick < 0
                || !double.IsFinite(p.Position.X) || !double.IsFinite(p.Position.Y))
            || this.trajectory.Zip(this.trajectory.Skip(1)).Any(p => p.First.Tick >= p.Second.Tick)
            || (this.trajectory.Length > 0 && this.trajectory[0].Tick != 0))
            throw new ArgumentException("Contact trajectories require ordered finite poses starting at the projection origin.", nameof(trajectory));
    }

    public ContactTrajectoryResult? Continue(DecisionWorkBudget budget)
    {
        if (result != null) return result;
        while (trajectory.Length > 0 && boxes.Count <= horizon && boxes.Count <= trajectory[^1].Tick)
        {
            if (!budget.TrySpend("course-contact-trajectory")) return null;
            int tick = boxes.Count;
            // Advance one segment per operation too: a densely sampled timeline must
            // not hide an unbounded scan inside one nominal contact-tick operation.
            if (segment + 1 < trajectory.Length && trajectory[segment + 1].Tick < tick) { segment++; continue; }
            var start = trajectory[segment];
            var end = segment + 1 < trajectory.Length ? trajectory[segment + 1] : start;
            double fraction = end.Tick == start.Tick ? 0 : (tick - start.Tick) / (end.Tick - start.Tick);
            double x = start.Position.X + (end.Position.X - start.Position.X) * fraction;
            double y = start.Position.Y + (end.Position.Y - start.Position.Y) * fraction;
            boxes.Add(new(x - width * .5, y - height * .5, width, height));
        }
        return result = new(Array.AsReadOnly(boxes.ToArray()), horizon, boxes.Count > horizon);
    }
}
