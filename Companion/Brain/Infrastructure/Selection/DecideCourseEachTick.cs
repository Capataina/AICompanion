#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;
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
/// <param name="Settled">Whether the brain actually reached a decision this tick. False while a decision
/// is still running, which is a real state rather than a rare one: a decision spans ticks by design, and
/// every tick inside one asks the body to keep the player company because that is what it would be doing
/// anyway. The flag exists because companionship-while-deciding and companionship-as-the-answer are
/// indistinguishable from the request alone, and one of them must not be allowed to start recovery
/// flight — the walker's own law, that a fallback triggered by the absence of the ordinary path's
/// precondition fires hardest while the planner is still thinking.</param>
public readonly record struct CourseDecision(string Activity, StepBinding? Binding, string Reason, bool Settled);

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
    public CourseDecision Last { get; private set; } = new("keep-company", null, "not-yet-decided", Settled: false);

    /// <summary>The observation this tick's decision was made against, for the recorder and the overlay.
    /// Null before the first tick of a companion's life.</summary>
    public DecisionFactSnapshot? Facts => observation.Current;

    /// <summary>What discovery found this tick, and how completely. A source that could not finish its
    /// census is the difference between "nothing to do" and "nobody looked", which is the distinction
    /// every three-valued answer in this tree exists to preserve.</summary>
    public IReadOnlyList<OpportunityCoverage> Coverage => discovery.Coverage;

    /// <summary>Every candidate discovery is currently serving the search, for a reader that needs the
    /// individual keys rather than the per-domain counts <see cref="Admitted"/> aggregates. A count says
    /// three combat opportunities are usable; only the keys say which three, and which observation each
    /// was admitted against — which is the difference between diagnosing a census and diagnosing a
    /// store.</summary>
    public IReadOnlyList<Opportunity> Candidates => discovery.Candidates;

    /// <summary>
    /// What discovery found, by domain and by whether the search may actually order it.
    ///
    /// Coverage alone answers "was the census finished", which is a different question from "is there
    /// anything here the brain could choose". `SearchCourseOrders.Begin` enumerates only `KnownUsable`
    /// candidates, so a domain can report a complete census of thirteen examined sites and contribute
    /// nothing whatever to the comparison — and a funnel showing 13/13 reads as health while the brain
    /// has no shot to weigh. Reading examined counts without admission counts is how a scene gets
    /// diagnosed three times and stays unexplained.
    /// </summary>
    public IReadOnlyList<(string Domain, int Usable, int Unresolved, int Unusable, string Reason)> Admitted
        => discovery.Candidates.GroupBy(candidate => candidate.Key.Domain).OrderBy(group => group.Key)
            .Select(group => (group.Key,
                group.Count(c => c.Admission == OpportunityAdmission.KnownUsable),
                group.Count(c => c.Admission == OpportunityAdmission.Unresolved),
                group.Count(c => c.Admission == OpportunityAdmission.KnownUnusable),
                // The reason of the commonest non-usable admission, because "combat is unresolved" is
                // the symptom and the source's own reason string is the cause. A domain with nothing
                // refused reports an empty reason rather than inventing one.
                group.Where(c => c.Admission != OpportunityAdmission.KnownUsable)
                    .GroupBy(c => c.Reason, StringComparer.Ordinal)
                    .OrderByDescending(reason => reason.Count()).ThenBy(reason => reason.Key, StringComparer.Ordinal)
                    .Select(reason => reason.Key).FirstOrDefault() ?? string.Empty))
            .ToArray();

    /// <summary>How the last search ended, for the cost strip: orders priced, orders refused, and
    /// whether enumeration ran out or the allowance did.</summary>
    public (long Evaluated, long Rejected, bool Exhausted) LastSearch { get; private set; }

    /// <summary>Why the last search refused the orders it refused, counted by reason. A rejection count
    /// with no reason beside it is the number that cannot be acted on, and reading it is how a course
    /// that never chooses the obvious work gets diagnosed in one pass instead of three.</summary>
    public IReadOnlyDictionary<string, int> LastRefusals { get; private set; }
        = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>What the best order led by each domain scored in the last search, so a losing kind of
    /// work says what it lost by rather than leaving the reader to reconstruct the objective by hand.</summary>
    public IReadOnlyDictionary<string, CourseValue> LastLeaders { get; private set; }
        = new Dictionary<string, CourseValue>(StringComparer.Ordinal);

    /// <summary>
    /// Which decision produced the action being carried out, and when it was reached.
    ///
    /// The recorder writes these as the capture's `choice_id` and `choice_tick`, and every `tool-effect`
    /// occurrence carries the comparison identity that selected the executing action, so that a strike
    /// can be joined back to the decision that chose it. Under the family chooser they came from
    /// `Chooser.EvaluationId`, which the course brain never advances — so from the tick switch until
    /// this was added, every effect in a played session claimed the same comparison, identity zero, and
    /// the join was silently meaningless rather than absent.
    ///
    /// It advances when a decision *settles*, not when one begins, because a decision legitimately
    /// spans several ticks here: a travel query can cost more than a tick's leftover allowance, so the
    /// observation is frozen for the life of the decision rather than for one tick. Counting starts
    /// would make a reader see several identities for one choice and read them as churn.
    /// </summary>
    public long DecisionId { get; private set; }
    public ulong? DecisionTick { get; private set; }
    /// <summary>The one reason that means the tick carried a course rather than decided one. Named
    /// because two places must agree on it: the site that publishes it and the identity that must not
    /// advance for it.</summary>
    internal const string RetainedReason = "course-retained";

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
            Advance(deciding, models, budget);
            LastSearch = (deciding.EvaluatedOrders, deciding.RejectedOrders, deciding.Exhausted);
            LastRefusals = deciding.Refusals;
            LastLeaders = deciding.Leaders;
            // Still working. The body keeps the player company while the brain thinks, which is what it
            // would be doing anyway and is strictly better than holding still for the answer.
            if (!deciding.Exhausted) return Companionship("deciding", settled: false);
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

        // Danger arriving mid-course does not interrupt a running course, and that is recorded as a gap
        // rather than patched here. A threshold on "how much more dangerous is enough" was built and
        // removed: the magnitude was invented rather than derived, it did not fix the scene it was built
        // for — the measured case has the threat present from the first tick, so a course published in
        // the dangerous world shows no rise at all — and a tuned constant of that kind is exactly what
        // makes two similar situations behave differently for reasons nobody can see later. The honest
        // mechanism is opportunistic replacement, which needs the incumbent reprojected from the current
        // observation so two futures can be compared, and that is `AIC-439`'s territory rather than a
        // number. Protection urgency still reaches the comparison at every decision, through the
        // episode's own discount on non-combat needs.
        if (NextStep(out StepBinding? held) && held != null)
        {
            BindingValidation validation = binder.ValidateNextUse(held, facts);
            if (validation.CanUse) return Carry(held, RetainedReason);
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
            // The sense's own reading, passed as the float it is: one for any recognised boss or event,
            // a ramp for inferred pressure. It reaches optional work through `RelevanceFor`, beside the
            // player's urgency and under a max rather than a product, because a boss raises both and
            // multiplying would charge one danger twice. Passed as a boolean before 21 September 2026,
            // which is why a blood moon over the player valued his copper exactly as a quiet sky did.
            encounterIntensity: context.Senses.Encounter.Intensity,
            relevanceFingerprint: CourseComparisonEpisode.Policy,
            // The one route by which the player's danger reaches optional work. Without it the course
            // has no term for him being hurt at all — its need kinds are illumination, loot, native
            // work, a hostile's life and a container, and none of them says anything about the player —
            // so a zombie standing on a wounded player was worth exactly what one across the room was
            // worth and the companion kept mining. This is the threat sense's own urgency, the same
            // quantity the family chooser discounted excursions by, read rather than recomputed.
            protectionUrgency: context.Senses.Threats.ProtectionUrgency);

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
        Advance(search, models, budget);
        LastSearch = (search.EvaluatedOrders, search.RejectedOrders, search.Exhausted);
        LastRefusals = search.Refusals;
        LastLeaders = search.Leaders;
        deciding = search;
        decidingFacts = facts;
        decidingEpisode = episode;
        if (!search.Exhausted) return Companionship("deciding", settled: false);
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

    /// <summary>
    /// Spend the tick's remaining allowance on the decision rather than one round of it.
    ///
    /// `ContinueSearch` is one round — answer the models already queued, extend the frozen catalogue,
    /// resume the search — and a search that discovers a *new* model request during that round had to
    /// wait a whole tick for it under a single call. That is a latency the design does not ask for: the
    /// budget is the bound, not the round, and a decision that could have settled inside one tick was
    /// being spread across several while the body kept company through all of them. Measured as mining
    /// holding the body for 30 ticks of 120 on a scene whose whole premise is that mining keeps it.
    ///
    /// The loop terminates on the budget rather than on progress, which is deliberate: `ContinueSearch`
    /// leaves a request queued when the model store is at capacity, so a round that answers nothing is a
    /// legitimate state and the allowance running out is what ends the tick's share of the work.
    /// </summary>
    private static void Advance(SearchCourseOrders search, RetainCourseModelQueries models, DecisionWorkBudget budget)
    {
        while (!search.Exhausted && !budget.Exhausted) models.ContinueSearch(search, budget);
    }

    /// <summary>The step the course is about to perform, which is the first one no receipt has closed.
    /// A course whose steps are all done is finished rather than current, and the caller treats that the
    /// same way it treats having no course at all.</summary>
    private bool NextStep(out StepBinding? step)
    {
        step = Course.Current?.Projection.Steps.FirstOrDefault();
        return step != null;
    }

    /// <param name="settled">False only while a decision is still running. Keeping the player company
    /// because nothing is worth doing is a decision; keeping him company because the brain has not
    /// finished is not, and the tick must be able to tell them apart before it lets the body commit to
    /// flying home.</param>
    private CourseDecision Companionship(string reason, bool settled = true)
        => Trace(Last = new("keep-company", null, reason, settled));

    private CourseDecision Carry(StepBinding step, string reason)
    {
        string activity = ExecuteCourseBinding.ActivityFor(step.Opportunity.Purpose);
        Course.BeginExecution(step.Id);
        return Trace(Last = new(activity, step, reason, Settled: true));
    }

    /// <summary>
    /// One God's-eye record per decision, which is what makes a played session readable at all.
    ///
    /// `RecordCourseTrace` was built with the rest of the course machinery and had no caller anywhere in
    /// the tree, so a playtest of the course brain produced no course evidence whatever: the recorder's
    /// own comment says schema 0.41.0 begins retained-course evidence, and nothing was writing any. The
    /// whole argument for this brain was that a poor decision, a stale model, an invalid binding and an
    /// effect that never arrived would be distinguishable afterwards, and that distinction lives in what
    /// gets written here.
    ///
    /// The fields are chosen so the four states that look identical from outside can be told apart, and
    /// all four ask the body to keep the player company: the course is running, the course found nothing
    /// worth doing, the brain has not finished deciding, and a proposal was refused publication. The
    /// reason separates them; the order counts and refusal tally say what the search did to get there.
    /// </summary>
    private CourseDecision Trace(CourseDecision decision)
    {
        // The identity advances once per decision actually *reached*, and carrying a retained course is
        // not reaching one. A course is kept until its next use stops validating, so those ticks are
        // one choice outliving its rescore by design; advancing there would report the design as churn
        // and break the join from a strike back to the decision that chose it. A tick that genuinely
        // decides advances it even when the outcome repeats, because a fresh comparison that lands on
        // the same answer is still a fresh comparison — which is what the chooser's `EvaluationId`
        // meant, and what readers of `choice_id` have always been told it means.
        //
        // An unsettled tick advances nothing: a decision still running has not chosen anything yet.
        if (decision.Settled && decision.Reason != RetainedReason)
        {
            DecisionId++;
            DecisionTick = Terraria.Main.GameUpdateCount;
        }
        DecisionFactSnapshot? facts = observation.Current;
        if (facts == null) return decision;
        RetainedCourse? course = Course.Current;
        var context = new CourseTraceContext(
            SourceTick: facts.Tick,
            NativePhase: "brain-decide",
            CourseId: course?.Id ?? 0,
            CourseRevision: course?.Revision ?? 0,
            StepId: decision.Binding?.Id ?? 0,
            BindingId: decision.Binding?.Id ?? 0,
            AttemptId: 0,
            Producer: nameof(DecideCourseEachTick),
            ObservationOrdinal: facts.ObservationOrdinal,
            ReceiptWatermark: facts.ReceiptWatermark,
            WorldEpoch: facts.WorldEpoch.ToString(CultureInfo.InvariantCulture),
            SourceRevision: facts.Id.ToString(CultureInfo.InvariantCulture),
            PolicyFingerprint: CourseComparisonEpisode.Policy,
            ConfigurationFingerprint: MotionModelFingerprint);
        var fields = new List<KeyValuePair<string, CourseTraceValue>>
        {
            new("reason", CourseTraceValue.TextValue(decision.Reason)),
            new("activity", CourseTraceValue.TextValue(decision.Activity)),
            new("settled", CourseTraceValue.Flag(decision.Settled)),
            new("purpose", CourseTraceValue.TextValue(decision.Binding?.Opportunity.Purpose ?? "")),
            new("steps", CourseTraceValue.Integer(course?.Projection.Steps.Count ?? 0)),
            new("orders-priced", CourseTraceValue.Integer(LastSearch.Evaluated)),
            new("orders-refused", CourseTraceValue.Integer(LastSearch.Rejected)),
            new("search-exhausted", CourseTraceValue.Flag(LastSearch.Exhausted)),
            new("release-reason", CourseTraceValue.TextValue(Course.ReleaseReason)),
            new("facts", CourseTraceValue.Integer(facts.Facts.Count)),
        };
        // The refusal tally rides in the same record rather than a second one, because a rejection count
        // without its reasons is the number nobody can act on — the thing that made one scene take three
        // separate diagnostic angles instead of one read.
        foreach (var refusal in LastRefusals.OrderByDescending(entry => entry.Value).Take(4))
            fields.Add(new("refused:" + refusal.Key, CourseTraceValue.Integer(refusal.Value)));
        RecordCourseTrace.Record(CourseTracePhase.Brain, context,
            new CourseTracePayload("course-decision", 1, fields));
        return decision;
    }

    /// <summary>Names the prediction law this brain decided under, so a recording says which rules
    /// produced it rather than leaving a reader to assume the current ones.</summary>
    private static string MotionModelFingerprint =>
        "motion:" + ForecastCourseConsequences.MotionModelRevision.ToString(CultureInfo.InvariantCulture);
}
