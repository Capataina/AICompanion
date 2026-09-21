#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Activities.Combat;
using AICompanion.Companion.Brain.Activities.Combat.Planning;
using AICompanion.Companion.Brain.Activities.Gathering;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Selection;

/// <summary>What the course wants done this tick: an activity name and the binding behind it.</summary>
/// <param name="Activity">The activity that performs the step, empty when the course wants no activity
/// change — a pot broken in passing names no executor of its own.</param>
/// <param name="Binding">The published step, null when there is no course, which is companionship.</param>
/// <param name="Reason">Why this is what the tick got, for the record and the overlay.</param>
public readonly record struct CourseDecision(string Activity, StepBinding? Binding, string Reason);

/// <summary>
/// The course owner on the live tick: one frozen observation, one discovery pass, one bounded order
/// search, and one published course whose next step the rest of the tick carries out.
///
/// This is the component the whole retained-course design was built toward, and what makes it a
/// *retained* course rather than a per-tick choice is the first thing it does: a published course whose
/// next use still validates is kept, and no search runs at all. The brain re-decides when the course
/// runs out of steps or when the step it was about to perform stops being usable — a drop that rolled,
/// a tile somebody else mined, a torch item swapped out of the slot — which is the binder's own
/// <see cref="BindOpportunity.ValidateNextUse"/> answering about live facts rather than a timer.
///
/// <b>What is deliberately not here, so nobody looks for it as a bug.</b> Opportunistic replacement
/// mid-course is not built: a rival order that would be worth more than the course already running is
/// never considered while that course is valid, because <see cref="RetainCourse.Consider"/> requires the
/// incumbent to be reprojected from the current observation before two futures can be compared, and that
/// reprojection is its own piece of work. The consequence is bounded and worth stating plainly — the
/// companion finishes what it started and picks again at the end, rather than abandoning a half-flown
/// journey for something better that appeared beside it. Everything the plan builds underneath — the
/// dependency manifests, the repair frontier, the comparison policy — is present and exercised by the
/// publish path; what is missing is the caller that reprojects an incumbent.
///
/// The three legacy things it replaces are named for removal in the plan's migration table:
/// `ChooseBehaviour`'s family nomination, `EvaluatePreparedActivities`' prepared-score ranking, and
/// `OrderNearbyTasks`' factorial permutation scoring. None is deleted here, because deleting the live
/// chooser in the same change that first runs its replacement would leave no way to tell which of the
/// two broke anything; they go once this has been played.
/// </summary>
public sealed class DecideCourseEachTick
{
    /// <summary>How many discovered sites one order may hold. It bounds the search rather than what is
    /// ever examined — every site stays discoverable and comparable, and this caps only how long one
    /// retained chain of work may be before the body is asked to come back.</summary>
    private const int MaximumCourseDepth = 3;

    /// <summary>How many native model questions may be outstanding at once. Each is one travel query or
    /// one enemy-motion simulation sharing the tick's own allowance, so the cap is about how much work
    /// can be half-finished across ticks rather than about how much runs in one.</summary>
    private const int ModelQueueCapacity = 8;

    // One instance per source and per binder, held rather than rebuilt, because each keeps a cursor that
    // resumes a sliced census across ticks. Rebuilding them per tick would restart every census every
    // tick and no slow one would ever finish.
    private readonly DiscoverOpportunities discovery = new(new IOpportunitySource[]
    {
        new GatheringOpportunitySource("mine-target"),
        new GatheringOpportunitySource("chop-target"),
        new DiscoverAssistanceOpportunities("collect-target"),
        new DiscoverAssistanceOpportunities("light-target"),
        new DiscoverAssistanceOpportunities("pot-target"),
        new CombatOpportunitySource(),
    }, capacity: 64);

    private readonly BindOpportunity binder = new(new IOpportunityBinder[]
    {
        new GatheringOpportunityBinder("mine-target"),
        new GatheringOpportunityBinder("chop-target"),
        new AssistanceOpportunityBinder("collect-target"),
        new AssistanceOpportunityBinder("light-target"),
        new AssistanceOpportunityBinder("pot-target"),
        new CombatOpportunityBinder(),
    });

    private readonly AssembleCourseSnapshot observation = new();
    private readonly ObserveDecisionCapabilities capabilities = new();
    private readonly SearchCourseOrders search = new(MaximumCourseDepth);
    private long episodes;
    private long epoch = -1;
    // The decision in flight, if any. All four move together: the search, the frozen observation it is
    // pricing against, the episode that normalises its needs, and the model owner answering its queries.
    // A decision spanning ticks is the normal case rather than the exception, because one travel query
    // can cost more than a tick's leftover allowance.
    private SearchCourseOrders? deciding;
    private DecisionFactSnapshot? decidingFacts;
    private CourseComparisonEpisode? decidingEpisode;
    private RetainCourseModelQueries? models;

    /// <summary>The published course. Diagnostics read it; nothing outside may publish to it.</summary>
    public RetainCourse Course { get; } = new();

    /// <summary>What the last tick decided and why, for the recorder, the overlay and the profile card.
    /// The reason is the one string that distinguishes "the course is running" from "the course found
    /// nothing worth doing" from "the brain is still deciding" — three states that look identical from
    /// outside, because all three ask the body to keep the player company.</summary>
    public CourseDecision Last { get; private set; } = new("keep-company", null, "not-yet-decided");

    /// <summary>The observation this tick's decision was made against, for the recorder and the overlay.
    /// Null before the first tick of a companion's life.</summary>
    public DecisionFactSnapshot? Facts => observation.Current;

    /// <summary>What discovery found this tick, and how completely. A source that could not finish its
    /// census is the difference between "nothing to do" and "nobody looked", which is the distinction
    /// every three-valued answer in this tree exists to preserve.</summary>
    public IReadOnlyList<OpportunityCoverage> Coverage => discovery.Coverage;

    /// <summary>How the last search ended, for the cost strip: orders priced, orders refused, and
    /// whether enumeration ran out or the allowance did.</summary>
    public (long Evaluated, long Rejected, bool Exhausted) LastSearch { get; private set; }

    public void ResetWorld()
    {
        observation.ResetWorld();
        capabilities.Reset();
        Course.Release("world-reset");
    }

    /// <summary>
    /// Decide what the body should be doing, from this tick's world alone.
    /// </summary>
    /// <param name="budget">The tick's own shared allowance. Observation, discovery, binding, the
    /// consequence forecasts and the native model queue all spend from it, which is the point: a census
    /// that runs long takes its time from the same place the search does rather than minting its own,
    /// and whatever is cut publishes partial coverage instead of a confident empty answer.</param>
    public CourseDecision Decide(in ActionContext context, CompanionCombat combat,
        SearchAttackPlans.SearchResult? plans, DecisionWorkBudget budget)
    {
        // A world change is noticed here rather than by a caller remembering to say so. The receipt
        // epoch advances on world load and unload, and every capture below holds a cursor into the
        // previous world's census, so a reload without this returns sites in a world that no longer has
        // them. Keying it on the epoch cannot be forgotten; a `ResetWorld` call from the lifecycle can,
        // and that is exactly how the retained companionship leg leaked before it was keyed on its order.
        if (epoch != CollectNativeEffectReceipts.WorldEpoch)
        {
            epoch = CollectNativeEffectReceipts.WorldEpoch;
            observation.ResetWorld();
            capabilities.Reset();
            // A decision in flight is priced against a world that no longer exists, so it is abandoned
            // rather than continued. Its model owner refuses further requests once abandoned, which is
            // the guard that would catch this if the line below were ever removed.
            models?.Abandon();
            deciding = null; models = null; decidingFacts = null; decidingEpisode = null;
        }

        // A decision in flight keeps its own frozen observation, and this is the single most important
        // structural fact about this class.
        //
        // The first wiring captured a fresh observation every tick and built a fresh model owner with
        // it, which meant any travel query needing more operations than one tick's leftover allowance
        // was thrown away and restarted the next tick. Measured on an empty floor with one drop beside
        // the body: the drop was discovered every tick (`collect-target:1/1`) and the search reported
        // `evaluated=0 rejected=0 exhausted=False` for ever, so nothing was ever published and the
        // companion kept company on a floor with work on it. That is the walker's own law arriving in a
        // new place — a bound that ran out is a third value and not a refusal — and the fix is that an
        // observation is frozen for the life of one *decision* rather than one tick.
        if (deciding != null && models != null)
        {
            models.ContinueSearch(deciding, budget);
            LastSearch = (deciding.EvaluatedOrders, deciding.RejectedOrders, deciding.Exhausted);
            // Still working. The body keeps the player company while the brain thinks, which is what it
            // would be doing anyway and is strictly better than holding still for the answer.
            if (!deciding.Exhausted) return Companionship("deciding");
            return Settle();
        }

        DecisionFactSnapshot facts = observation.Capture(context, combat, plans, budget);
        Course.ObserveEpoch(facts.WorldEpoch);
        Course.RetireReceiptsThrough(facts.ReceiptWatermark);

        // A retained course is kept while the step it is about to perform is still usable. This is the
        // whole retention property, and it is checked every tick against a freshly frozen observation
        // rather than against the one the course was chosen from, because the question is whether the
        // next native call would succeed *now* — a drop that rolled, a tile somebody else mined, a torch
        // swapped out of the slot — not whether it looked right when it was planned.
        // A course with no steps left is finished, and finishing is not the same as being retained.
        // An empty order is a legal and sometimes winning course — it is what "keep the player company
        // and do nothing else" looks like when nothing on the board is worth the journey — and it
        // publishes like any other. Without this it would sit as the current course for ever: it has no
        // next use to validate, so the retained branch below never sees it, and the next decision then
        // meets `Consider`'s reprojection guard and throws rather than comparing anything.
        if (Course.Current is { } finished && finished.Projection.Steps.Count == 0)
            Course.Release("course-complete");

        if (NextStep(out StepBinding? held) && held != null)
        {
            BindingValidation validation = binder.ValidateNextUse(held, facts);
            if (validation.CanUse) return Carry(held, "course-retained");
            // The use is recorded as invalid and then the course is released, and the release is not
            // optional. `Consider` refuses to compare two futures unless the incumbent was reprojected
            // from the observation doing the comparing, and throws rather than guessing — which is the
            // right contract and exactly what this owner cannot satisfy, because reprojecting an
            // incumbent is the piece named as not built. Keeping a course whose next use has gone
            // invalid therefore does not preserve anything: it makes the next decision impossible.
            Course.InvalidNextUse(validation.Reason);
            Course.Release("next-use-invalid:" + validation.Reason);
        }

        // Nothing usable is retained, so a decision starts here. Discovery runs first because the search
        // can only order sites that have been found, and its pinned set keeps whatever the course still
        // holds discoverable even if its source has moved past it.
        discovery.Continue(facts, budget, Course.Current?.Projection.Steps.Select(step => step.Opportunity) ?? Array.Empty<OpportunityKey>());
        IReadOnlyList<Opportunity> candidates = discovery.Candidates;

        var episode = new CourseComparisonEpisode(++episodes, facts.WorldEpoch,
            // The time scale is what makes a near opportunity and a far one comparable at all: it is how
            // long crossing the player's own resting region takes at cruising speed, so an effect a
            // region away and an effect here are discounted against the same journey rather than against
            // a constant somebody chose.
            // The resting half-size and not the current one: the intent region grows with the player's
            // lead, and a time scale that moved because he broke into a run would re-price every
            // opportunity on the board for a reason that has nothing to do with any of them. The speed is
            // the motor's own answer rather than the arithmetic behind it, so a body multiplier or a pair
            // of boots changes both together.
            CourseComparisonEpisode.TimeScaleFor(
                context.Senses.Intent.BaseRestingHalfSize.X * 2, context.Senses.Intent.BaseRestingHalfSize.Y * 2,
                context.Companion.Motor.LiveMaxSpeed, shortestLocalCycle: 1),
            candidates.SelectMany(candidate => candidate.Needs),
            censusComplete: discovery.Coverage.All(coverage => coverage.Exhausted),
            // Any recognised source reads one, so a positive intensity is the encounter being on rather
            // than a threshold anybody picked.
            encounter: context.Senses.Encounter.Intensity > 0,
            relevanceFingerprint: CourseComparisonEpisode.Policy);

        models = new RetainCourseModelQueries(facts, MovementQueries.World,
            capabilities.CapabilityRevision, ModelQueueCapacity);
        var body = new CoursePoint(context.Npc.Center.X, context.Npc.Center.Y);
        var projector = new BindCourseOrder(facts, episode, candidates, binder,
            new ProjectedCourseState(body, facts),
            // The reunion pose is the player's own region centre rather than his feet, because that is
            // where the companion lives and the return leg is priced to where it is going back to.
            new ForecastCourseConsequences(new CoursePoint(
                context.Senses.Intent.Region.Centre.X, context.Senses.Intent.Region.Centre.Y)));

        search.Begin(facts, episode, candidates,
            Course.Current?.Projection.Steps.Select(step => step.Opportunity).ToArray() ?? Array.Empty<OpportunityKey>(),
            projector);
        // The owner drives the search rather than the other way round: it answers whatever models the
        // search asked for, extends the frozen catalogue with the answers, and lets the search resume on
        // the extended observation. A search suspended on a model nobody answers never advances.
        models.ContinueSearch(search, budget);
        LastSearch = (search.EvaluatedOrders, search.RejectedOrders, search.Exhausted);
        deciding = search;
        decidingFacts = facts;
        decidingEpisode = episode;
        if (!search.Exhausted) return Companionship("deciding");
        return Settle();
    }

    /// <summary>
    /// The end of one decision: take the best order the search found, or admit it found none.
    ///
    /// Publication happens here rather than at the first complete order because the search is comparing
    /// futures and a better one may still be two orders away; taking the first would make the search's
    /// own ordering advisory. `Consider` revalidates the prefix against live facts before it publishes,
    /// so an answer that went stale while the decision ran is refused rather than committed.
    /// </summary>
    private CourseDecision Settle()
    {
        SearchCourseOrders search = deciding!;
        CourseComparisonEpisode episode = decidingEpisode!;
        // The model-extended observation, not the one the decision started from. Every travel answer and
        // every enemy-motion answer the forecast consumed was appended to the owner's snapshot as it
        // completed, and `ReadyToPublish` refuses a proposal whose consequence manifest names a fact the
        // snapshot it is handed does not carry. Publishing against the pre-extension facts therefore
        // refused every course that had ever waited on a model — which is every course with travel in
        // it, so it refused all of them, silently, under the reason `proposal-refused-publication`.
        DecisionFactSnapshot facts = models!.Snapshot;
        models.Abandon();
        deciding = null; models = null; decidingFacts = null; decidingEpisode = null;

        if (search.Best is not { } best) return Companionship("no-order-worth-taking");

        bool published = Course.Consider(best, episode, facts,
            // There is no incumbent at this point — a valid one returned before the decision started,
            // and an invalid one was released — so the current validation is the refusal that got us here.
            new BindingValidation(OpportunityAdmission.KnownUnusable, "no-valid-incumbent", RequiresRepair: false),
            proposed => binder.ValidateNextUse(proposed, facts));

        if (published && NextStep(out StepBinding? fresh) && fresh != null) return Carry(fresh, "course-published");
        return Companionship(published ? "published-course-holds-no-step" : "proposal-refused-publication");
    }

    /// <summary>The step the course is about to perform, which is the first one no receipt has closed.
    /// A course whose steps are all done is finished rather than current, and the caller treats that the
    /// same way it treats having no course at all.</summary>
    private bool NextStep(out StepBinding? step)
    {
        step = Course.Current?.Projection.Steps.FirstOrDefault();
        return step != null;
    }

    private CourseDecision Companionship(string reason) => Last = new("keep-company", null, reason);

    private CourseDecision Carry(StepBinding step, string reason)
    {
        string activity = ExecuteCourseBinding.ActivityFor(step.Opportunity.Purpose);
        Course.BeginExecution(step.Id);
        return Last = new(activity, step, reason);
    }
}
