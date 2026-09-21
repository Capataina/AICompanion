#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// The native consequence provider: what a course costs, priced from captured facts.
///
/// It prices companionship and the return leg for real, through <see cref="ForecastCourseCompanionship"/>,
/// and it declares predicted contact harm <em>unresolved</em> rather than absent while the enemy-motion
/// pipeline it needs is unintegrated. That distinction is the whole safety property of this class and it
/// is not a placeholder: an unresolved tail cannot certify strict superiority, so a course whose harm is
/// unknown can be chosen as a legal first action and can never be proven better than a safer rival.
/// Returning <c>Complete</c> with an empty harm list instead would be the worst available lie — every
/// comparison would read "no predicted harm" and actively prefer the most dangerous course on the board,
/// which is exactly the zero-cost completed candidate the Courses contract forbids.
///
/// What is still missing before harm can be priced, and why it is not a small addition: contact harm
/// needs <see cref="ProjectMeleeContactGeometry"/> per hostile, which needs a modelled
/// <c>CapturedEnemyCourseMotion</c>, which arrives asynchronously through the observation owner's model
/// queue. So a complete provider is a two-kind pending-request state machine over travel and enemy
/// motion, with its own resumption and terrain-revalidation rules, rather than another call in a line.
/// </summary>
public sealed class ForecastCourseConsequences : ICourseConsequenceForecast
{
    private readonly CoursePoint reunionPose;
    private ForecastCourseCompanionship? companionship;
    /// <summary>The order the retained leg belongs to. A companionship leg is only ever valid for the
    /// exact steps and starting state it was built from, so it is keyed on them and rebuilt when they
    /// differ, rather than on a caller remembering to say a new order started.</summary>
    private string? orderKey;

    /// <summary>The travel this forecast is waiting on, for the observation owner's model queue. Null
    /// when nothing is outstanding; the domain cursor stays on the candidate that needs it, so a
    /// completed answer resumes that candidate rather than finding enumeration has moved past it.</summary>
    public CourseTravelRequest? MissingTravel => companionship?.MissingTravel;

    /// <param name="reunionPose">Where the companion is judged to return to. The forecast deliberately
    /// does not choose this — it prices a return to a destination someone else names — so the caller
    /// supplies the player's captured region rather than letting the cost model invent a home.</param>
    public ForecastCourseConsequences(CoursePoint reunionPose) => this.reunionPose = reunionPose;

    public CourseProjectionResult Continue(IReadOnlyList<StepBinding> steps, ProjectedCourseState successor,
        DecisionFactSnapshot facts, CourseComparisonEpisode episode, DecisionWorkCursor cursor,
        DecisionWorkBudget budget)
    {
        // The retained leg belongs to one candidate order and must never be handed to the next one.
        //
        // This is keyed rather than reset by a caller because a caller forgot. The first version of this
        // class exposed a BeginOrder() method for the binder to call, the binder never called it — the
        // method is not on ICourseConsequenceForecast and could not be — and ForecastCourseCompanionship
        // returns its cached terminal result the instant it has one. So every order after the first in a
        // search was handed the first order's intervals, end tick, reunion verdict and dependency
        // manifest verbatim, priced from a pose it had never seen and asking for no travel to reach it.
        // With harm unresolved, companionship is the only cost term there is, so that made every
        // candidate order cost the same and reduced the search to useful effects with no separation cost.
        // A key cannot be forgotten; a call can.
        string key = OrderKey(steps, successor);
        if (orderKey != key) { companionship = null; orderKey = key; }
        companionship ??= new ForecastCourseCompanionship(facts, steps, successor.Pose, successor.Velocity,
            reunionPose, successor.Tick);

        CourseCompanionshipResult company = companionship.Continue(facts, budget);
        if (company.Status == ProjectionStatus.Pending)
            // A suspended leg forwards its typed travel request rather than merely reporting that it
            // stopped. The observation owner answers it and the same candidate resumes; dropping the
            // request would leave the domain cursor parked on a candidate nobody is completing.
            return new(ProjectionStatus.Pending, null, company.Reason,
                companionship.MissingTravel is { } travel ? new[] { travel } : null);

        // Harm is unknown, and the tail says so on every course without exception.
        //
        // An earlier version resolved the tail when a finished census held no hostile, reasoning that
        // there was then nothing to be hurt by. That was wrong twice. The census is one instant of
        // Main.npc[] at the freeze, and the tail it would certify covers the whole projected journey
        // including the return leg — so an empty census is proof about a moment and was being read as
        // proof about a horizon, which is the exhausted-bound mistake this tree refuses everywhere else.
        // And it certified zero harm to the *player* as well, about which a hostile-slot census says
        // nothing at all. Being permanently uncertain until harm is genuinely modelled is the honest
        // state, and it costs only that no course can yet be preferred for being safer.
        const bool harmKnown = false;
        // The manifest carries what the pricing actually read. An empty one asserts a calculation with
        // no captured inputs, which publication is entitled to believe — so handing over companionship's
        // own dependency manifest is what lets a changed region or travel fact dirty this cost later.
        var projection = new CourseProjection(steps, Array.Empty<PredictedHarm>(), company.Intervals,
            company.EndTick, company.NominallyRejoined, tailUnresolved: !harmKnown,
            consequenceDependencies: company.Dependencies);
        return new(ProjectionStatus.Complete, projection,
            harmKnown ? "companionship-priced;no-hostile-in-census" : "companionship-priced;contact-harm-unmodelled");
    }

    /// <summary>What makes two calls the same order: the exact step sequence, and the state the pricing
    /// starts from. The successor's pose and tick are in it because an identical step list priced from a
    /// different body pose is a different journey, which is precisely the case the leak produced.</summary>
    private static string OrderKey(IReadOnlyList<StepBinding> steps, ProjectedCourseState successor)
    {
        var key = new System.Text.StringBuilder();
        key.Append(successor.Tick).Append('@').Append(successor.Pose.X).Append(',').Append(successor.Pose.Y)
            .Append('/').Append(successor.Velocity.X).Append(',').Append(successor.Velocity.Y).Append(':');
        foreach (StepBinding step in steps) key.Append(step.Id).Append('.');
        return key.ToString();
    }
}
