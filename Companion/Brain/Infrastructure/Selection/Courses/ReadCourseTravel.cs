#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>A native movement-model result captured before pure binding. Arrival is a
/// nominal forecast of the existing steering/contact law, never a bound on future dodges.</summary>
public sealed record CapturedCourseTravel(CoursePoint From, CoursePoint InitialVelocity, CoursePoint To,
    CoursePoint ArrivalVelocity, double Ticks, OpportunityAdmission Admission, string Reason,
    IReadOnlyList<CoursePoint> Route, long CapabilityRevision);

public static class ReadCourseTravel
{
    public static FactKey Key(CoursePoint from, CoursePoint velocity, CoursePoint to)
        => new("course-travel", JsonSerializer.Serialize(new[] { from.X, from.Y, velocity.X, velocity.Y, to.X, to.Y }));

    public static CapturedCourseTravel? Read(ProjectedCourseState state, CoursePoint destination, TrackedFactReader facts)
    {
        var fact = facts.Read(Key(state.Pose, state.Velocity, destination));
        if (fact.Evidence is FactEvidence.Missing or FactEvidence.Unresolved) return null;
        var travel = JsonSerializer.Deserialize<CapturedCourseTravel>(fact.Value.Text);
        if (travel == null || travel.From != state.Pose || travel.InitialVelocity != state.Velocity || travel.To != destination
            || !double.IsFinite(travel.Ticks) || travel.Ticks < 0)
            throw new InvalidOperationException("Captured travel does not describe the requested physical leg.");
        return travel;
    }
}
