#nullable enable

extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Activities.Gathering;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>
/// How a gathering fixture gets a step to perform, now that mining and chopping perform only what the course
/// bound. Every row that used to prepare one of them on its own and read the target its private search found
/// asks the course instead: the production census, the production binder and the course's own decision, with
/// the body held still. A row that then swings hands the step over through <c>OwnCurrentActivity.Select</c>,
/// exactly as the tick does, so the activity under test is the brain's registered instance with the step it
/// would have been handed in play.
///
/// <para>Nothing here keeps a private search alive for a fixture, which is the rule the wave that deleted them
/// set: a fixture needing a target gets it from the course or it tests something that no longer exists.</para>
/// </summary>
internal static class DriveGatheringThroughTheCourse
{
    /// <summary>The step the course binds for <paramref name="activity"/>, or null when it chose something else
    /// or bound nothing. Deciding prepares every activity first, as the tick does.</summary>
    public static StepBinding? Decide(ActionContext ctx, string activity)
    {
        string chosen = VerifyOreWork.DecideThroughTheCourse(ctx);
        return chosen == activity ? ctx.Companion.Brain.Course.Last.Binding : null;
    }

    /// <summary>The step the course binds for <paramref name="activity"/>, required to exist, with the course's
    /// own account of what it admitted when it does not.</summary>
    public static StepBinding Bind(ActionContext ctx, string activity, string why)
        => Decide(ctx, activity) ?? throw new InvalidOperationException(
            $"{why}: the course did not bind {activity}; {Account(ctx)}");

    /// <summary>What the course chose and why, and what each domain's census admitted, for a failure message.</summary>
    public static string Account(ActionContext ctx)
    {
        var course = ctx.Companion.Brain.Course;
        return $"chose '{course.Last.Activity}' reason '{course.Last.Reason}' bound '{course.Last.Binding?.NativeUseId ?? "-"}'; admitted "
            + string.Join(" | ", course.Admitted.Select(a => $"{a.Domain}:u{a.Usable}/n{a.Unresolved}/x{a.Unusable}:{a.Reason}"));
    }

    /// <summary>The exact tile a gathering step swings at.</summary>
    public static Point Tile(StepBinding step)
        => GatheringOpportunityBinder.TryReadUse(step, step.Opportunity.Domain, out Point tile, out _)
            ? tile
            : throw new InvalidOperationException($"a gathering step names no tile: '{step.NativeUseId}'");

    /// <summary>Hand <paramref name="step"/> to its activity as the tick does and run one execution. Returns the
    /// activity, so a row can read what its hand did.</summary>
    public static CompanionAction Perform(ActionContext ctx, StepBinding step)
    {
        var brain = ctx.Companion.Brain;
        string name = step.Opportunity.Domain == "chop-target" ? "chop" : "mine";
        CompanionAction activity = brain.Actions.Single(a => a.Name == name);
        brain.Activity.Select(activity, ctx, step);
        brain.Activity.BeginExecution();
        activity.Execute(ctx);
        return activity;
    }

    /// <summary>Every gathering site a fresh native census publishes for <paramref name="domain"/>, read as the
    /// course would read it. A row that asks why the course did not bind a target reads the census's own answer
    /// here rather than an activity's, because the census is the only discovery.</summary>
    public static IReadOnlyList<GatheringOpportunityFact> Census(ActionContext ctx, string domain)
    {
        var capture = new CaptureGatheringOpportunities();
        return capture.Capture(ctx, new(double.PositiveInfinity))
            .Where(fact => fact.Key.Kind == domain)
            .Select(fact => JsonSerializer.Deserialize<GatheringOpportunityFact>(fact.Value.Text)!)
            .ToList();
    }

    /// <summary>The census's site whose admitted tile or vein holds <paramref name="tile"/>.</summary>
    public static GatheringOpportunityFact? Site(ActionContext ctx, string domain, Point tile)
        => Census(ctx, domain).FirstOrDefault(site => (site.TileX == tile.X && site.TileY == tile.Y)
            || site.Target.EndsWith($":{tile.X},{tile.Y}", StringComparison.Ordinal));
}
