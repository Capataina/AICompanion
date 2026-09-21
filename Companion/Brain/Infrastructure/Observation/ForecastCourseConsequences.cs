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
    private readonly int hostilesInCensus;
    private readonly bool censusComplete;
    private ForecastCourseCompanionship? companionship;

    /// <summary>The travel this forecast is waiting on, for the observation owner's model queue. Null
    /// when nothing is outstanding; the domain cursor stays on the candidate that needs it, so a
    /// completed answer resumes that candidate rather than finding enumeration has moved past it.</summary>
    public CourseTravelRequest? MissingTravel => companionship?.MissingTravel;

    /// <param name="reunionPose">Where the companion is judged to return to. The forecast deliberately
    /// does not choose this — it prices a return to a destination someone else names — so the caller
    /// supplies the player's captured region rather than letting the cost model invent a home.</param>
    /// <param name="hostilesInCensus">How many potential contact sources the frozen census held, and
    /// <paramref name="censusComplete"/> whether that census finished. Both are carried so the
    /// unresolved tail can be justified from the observation rather than asserted.</param>
    public ForecastCourseConsequences(CoursePoint reunionPose, int hostilesInCensus, bool censusComplete)
    {
        this.reunionPose = reunionPose;
        this.hostilesInCensus = hostilesInCensus;
        this.censusComplete = censusComplete;
    }

    public CourseProjectionResult Continue(IReadOnlyList<StepBinding> steps, ProjectedCourseState successor,
        DecisionFactSnapshot facts, CourseComparisonEpisode episode, DecisionWorkCursor cursor,
        DecisionWorkBudget budget)
    {
        // One forecast object per order, rebuilt when the caller starts a different one. Companionship
        // retains its partial leg across cuts, so it is kept rather than recreated between slices.
        companionship ??= new ForecastCourseCompanionship(facts, steps, successor.Pose, successor.Velocity,
            reunionPose, successor.Tick);

        CourseCompanionshipResult company = companionship.Continue(facts, budget);
        if (company.Status == ProjectionStatus.Pending)
            // A suspended leg forwards its typed travel request rather than merely reporting that it
            // stopped. The observation owner answers it and the same candidate resumes; dropping the
            // request would leave the domain cursor parked on a candidate nobody is completing.
            return new(ProjectionStatus.Pending, null, company.Reason,
                companionship.MissingTravel is { } travel ? new[] { travel } : null);

        // Harm is unknown rather than zero, and the tail says so. The census counts justify it: with no
        // hostile captured and a finished census there is genuinely nothing to be hurt by, and only then
        // may an empty harm list mean what it says.
        bool harmKnown = censusComplete && hostilesInCensus == 0;
        // The manifest carries what the pricing actually read. An empty one asserts a calculation with
        // no captured inputs, which publication is entitled to believe — so handing over companionship's
        // own dependency manifest is what lets a changed region or travel fact dirty this cost later.
        var projection = new CourseProjection(steps, Array.Empty<PredictedHarm>(), company.Intervals,
            company.EndTick, company.NominallyRejoined, tailUnresolved: !harmKnown,
            consequenceDependencies: company.Dependencies);
        return new(ProjectionStatus.Complete, projection,
            harmKnown ? "companionship-priced;no-hostile-in-census" : "companionship-priced;contact-harm-unmodelled");
    }

    /// <summary>Starting a different order abandons the previous order's retained legs, which is the
    /// same contract the binder keeps: hypothetical state is private to one candidate order.</summary>
    public void BeginOrder() => companionship = null;
}
