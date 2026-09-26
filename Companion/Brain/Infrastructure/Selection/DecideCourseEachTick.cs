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
using AICompanion.Companion.Brain.Infrastructure.Interactions;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Position;
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
/// is still running, which is a real state rather than a rare one: a decision spans ticks by design. The
/// flag exists because a continuation-while-deciding and the same thing as the answer are
/// indistinguishable from the request alone, and one of them must not be allowed to start recovery
/// flight — the walker's own law, that a fallback triggered by the absence of the ordinary path's
/// precondition fires hardest while the planner is still thinking.</param>
/// <param name="Request">Where the body goes when there is no published step but the tick is not idle:
/// the cheap legal continuation an unsettled decision carries, which is the plan's tick-order step 5.
/// Null on every settled tick, where the binding or companionship answers instead. It is a request
/// rather than a fabricated <see cref="StepBinding"/> deliberately — a step id no course ever published
/// would join effects in the record to a course that does not contain them.</param>
public readonly record struct CourseDecision(string Activity, StepBinding? Binding, string Reason, bool Settled,
    PositionRequest? Request = null);

/// <summary>The course's answer about one in-passing use: the census site proposed, the one-step binding accepted for
/// it or null, and the reason. An accepted binding's id is what the interaction event names, so a reader can join the
/// native effect back to the step that licensed it.</summary>
public readonly record struct IncidentalAcceptance(FactKey Site, StepBinding? Binding, string Reason)
{
    public bool Accepted => Binding != null;
}

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
/// The three legacy things it replaced — the family nomination, the prepared-score ranking and the
/// factorial permutation scoring of close jobs — were deleted on 22 September 2026 (`AIC-419`), a day
/// after they stopped being reachable. They stayed that day so a defect met in play could be attributed
/// to one brain or the other, and what closed that condition is the world run's play-measures instrument
/// grading this brain on the capture that motivated it.
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
    private readonly DiscoverOpportunities discovery = new(ProductionSources(), capacity: 64);

    // The profiler sections a decision opens (see `Diagnostics/ProfileBrainSections.cs`): freezing the observation,
    // checking the held step, the censuses, and publishing. The search and its models open theirs in
    // `RetainCourseModelQueries`, and each census its own under `discovery`.
    private static readonly int SnapshotSection = BrainSections.Register("snapshot");
    private static readonly int ValidateSection = BrainSections.Register("validate");
    private static readonly int DiscoverySection = BrainSections.Register("discovery");
    private static readonly int SettleSection = BrainSections.Register("settle");
    private static readonly int BeginSection = BrainSections.Register("begin");
    private static readonly int TraceSection = BrainSections.Register("trace");

    private readonly BindOpportunity binder = new(ProductionBinders());

    /// <summary>The sources the live brain discovers through, fresh instances each call. Public so a pin
    /// can ask the production list rather than keep a second copy of it: two hand-kept lists of the same
    /// domains are how the executor table and the activities came to disagree about pots.</summary>
    public static IOpportunitySource[] ProductionSources() => new IOpportunitySource[]
    {
        new GatheringOpportunitySource("mine-target"),
        new GatheringOpportunitySource("chop-target"),
        new DiscoverAssistanceOpportunities("collect-target"),
        new DiscoverAssistanceOpportunities("light-target"),
        new CombatOpportunitySource(),
    };

    /// <summary>The binders the live brain binds through, one per source domain; public for the same reason.</summary>
    public static IOpportunityBinder[] ProductionBinders() => new IOpportunityBinder[]
    {
        new GatheringOpportunityBinder("mine-target"),
        new GatheringOpportunityBinder("chop-target"),
        new AssistanceOpportunityBinder("collect-target"),
        new AssistanceOpportunityBinder("light-target"),
        new CombatOpportunityBinder(),
    };

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
    // The observation a decision was frozen on while discovery has not yet asked every source about it. The
    // search begins only once it has; until then the tick is a decision in flight like any other.
    private DecisionFactSnapshot? discoveringFacts;
    // Where the body was when that observation was frozen. The model owner is built on the same tick, because it
    // binds itself to the tick, the terrain revision and the captured motion current at construction and refuses
    // an enemy captured at any other tick; a search that begins later must still price from the frozen moment.
    private CoursePoint discoveringBody;
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

    /// <summary>How many model queries — travel and enemy motion — the decision in flight is waiting on, zero with
    /// none in flight. Read by the recorder's cost-spike account and nothing that decides.</summary>
    public int PendingModelQueries => models?.PendingCount ?? 0;

    /// <summary>What discovery found this tick, and how completely. A source that could not finish its
    /// census is the difference between "nothing to do" and "nobody looked", which is the distinction
    /// every three-valued answer in this tree exists to preserve.</summary>
    public IReadOnlyList<OpportunityCoverage> Coverage => discovery.Coverage;

    /// <summary>Every candidate discovery is currently holding, before the ones a performing hand refused are withheld
    /// (<see cref="RefusedByPerformer"/> names those, and the decision's structural tally counts them), for a reader that needs the
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
        => admitted ??= CountAdmissions();

    // Candidates change only inside discovery, so the grouping is kept until discovery next runs. The
    // recorder reads it once per activity per row and the audit once more; recomputed on every read it
    // was 5.9% of the brain thread on the replay of the 22 September 2026 capture (profiled 23 September).
    private IReadOnlyList<(string Domain, int Usable, int Unresolved, int Unusable, string Reason)>? admitted;

    private IReadOnlyList<(string Domain, int Usable, int Unresolved, int Unusable, string Reason)> CountAdmissions()
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

    /// <summary>What the last search refused for a reason nothing about the world can change — a purpose
    /// with no executor in `ExecuteCourseBinding`'s map. Kept apart from <see cref="LastRefusals"/>, and
    /// written apart from it, because the audit's contracts ask whether every *evidence* refusal was a
    /// not-observed one and a structural reason mixed into that answer silences them;
    /// `SearchCourseOrders.StructuralRefusals` carries the whole argument.</summary>
    public IReadOnlyDictionary<string, int> LastStructuralRefusals { get; private set; }
        = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>What the best order led by each domain scored in the last search, so a losing kind of
    /// work says what it lost by rather than leaving the reader to reconstruct the objective by hand.</summary>
    public IReadOnlyDictionary<string, CourseValue> LastLeaders { get; private set; }
        = new Dictionary<string, CourseValue>(StringComparer.Ordinal);

    /// <summary>
    /// The best order the last search priced whose first step is not the winner's, as the purposes it
    /// would have performed and what it was worth.
    ///
    /// The recorder writes this as `task_order_runner_up`, which is the column that separates a fight
    /// that lost narrowly from one that was never on the board — and until the search retained it, that
    /// column could only say `unavailable:the-search-retains-only-its-best-order`. Purposes rather than
    /// keys, because a reader wants to know it nearly went mining, not which tile; the value is the
    /// nominal total, the same number `LastLeaders` is read by.
    ///
    /// Null when the search priced fewer than two distinct first steps, which is a real answer: a
    /// decision with one order on the board has no alternative, and recording "none" as though the
    /// alternative were unreadable is the confusion the three-valued answers in this tree exist to stop.
    /// </summary>
    public (IReadOnlyList<string> Purposes, double Value)? LastRunnerUpOrder { get; private set; }

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
        admitted = null;
        refusedByPerformer.Clear();
        observation.ResetWorld();
        capabilities.Reset();
        Course.Release("world-reset");
    }

    /// <summary>
    /// Something outside the course has taken the body — downing, or recovery flight home — so the
    /// course and any decision in flight are dropped, and the next ordinary tick decides afresh from
    /// where the body actually is. Kept, a course would resume a step chosen from where the companion
    /// used to be, and a decision in flight would publish against an observation frozen before the
    /// body was carried somewhere else. Physical effects already launched survive the release, as they
    /// do for any release. Cheap when there is nothing to drop, so the owner of the body calls it on
    /// every tick it holds it.
    /// </summary>
    public void Interrupt(string reason)
    {
        if (deciding != null || models != null)
        {
            models?.Abandon();
            deciding = null; models = null; decidingFacts = null; decidingEpisode = null;
        }
        discoveringFacts = null;
        if (Course.Current != null) Course.Release(reason);
    }

    /// <summary>
    /// Opportunities a performing hand refused, withheld from the search until something the hand's check reads has
    /// changed: an announced edit within tool reach of the target, the companion's gear, cargo or the player's policies,
    /// or the wait every proven site refusal in this tree already keeps (`Weights.NearbyWorkNoReturnRetryTicks`), which
    /// is the backstop for a change nothing announces. It is not keyed on the census fact's revision, and that was tried
    /// first: an ore fact carries a stand computed from where the body is, so a released body drifting re-published the
    /// vein at a new revision every few ticks and the refusing hand was handed it 13 times in 120.
    /// </summary>
    private readonly record struct PerformerRefusal(Point? Tile, int TerrainRevision, long Capability, long Policy, ulong Until);
    private readonly Dictionary<OpportunityKey, PerformerRefusal> refusedByPerformer = new();
    /// <summary>Its own observer rather than the decision's, because advancing the revision the model owner reads would
    /// change what invalidates a travel query, which is not this mechanism's decision to make.</summary>
    private readonly ObserveDecisionCapabilities refusalMeans = new();
    /// <summary>Past this many remembered refusals, those whose opportunity discovery no longer serves are dropped.
    /// It bounds the pass, not what is remembered about a target still in front of the course.</summary>
    private const int PruneRefusedAbove = 256;

    /// <summary>The opportunities withheld from the search because a performing hand refused them, for the recorder
    /// and the fixtures.</summary>
    public IReadOnlyCollection<OpportunityKey> RefusedByPerformer => refusedByPerformer.Keys;

    /// <summary>
    /// The hand performing the course's step refused it by name, which is a proof the course cannot reach any other
    /// way: its own next-use check reads the frozen observation, and a target can stop being workable in ways that
    /// observation does not carry — a tile edited without an announcement, a placer refusing a site the census
    /// admitted, cargo that stopped taking the drop. Until 23 September 2026 the course never heard, so it bound the
    /// same step again on the next tick and the hand refused it again: 111 invalid attempts in 115 ticks on the lane
    /// B review's silent-edit scene, with the body parked at the stand. The course is released at once and the
    /// opportunity is withheld until something the hand's check reads has changed. The tick only forwards a refusal of a
    /// step it carried, which happens with no decision in flight, so the in-flight drop inside `Interrupt` is never
    /// reached from the tick today; it stays because a caller refusing mid-decision would otherwise let that decision
    /// publish the step just refused (the lone-sentinel review of ad012f2 traced this).
    /// </summary>
    public void PerformerRefused(in ActionContext context, StepBinding step, string reason)
    {
        // Only tile work names a tile; a drop or a hostile is re-admitted by its own identity moving on (a replaced
        // drop is a new generation, so a new key) or by the means and the wait below.
        Point? tile = step.Opportunity.Domain is "mine-target" or "chop-target" || step.Opportunity.Target.StartsWith("tile:", StringComparison.Ordinal)
            ? ExecuteCourseBinding.WorkTileOf(step) : null;
        ObservedDecisionCapabilities means = ObserveMeans(context);
        refusedByPerformer[step.Opportunity] = new(tile, TerrainChanges.Revision, means.CapabilityRevision, means.PolicyRevision,
            Terraria.Main.GameUpdateCount + (ulong)Weights.NearbyWorkNoReturnRetryTicks);
        if (refusedByPerformer.Count > PruneRefusedAbove)
            foreach (var (key, entry) in refusedByPerformer.Where(pair => pair.Value.Until <= Terraria.Main.GameUpdateCount).ToList())
                refusedByPerformer.Remove(key);
        if (Course.Current != null) Course.InvalidNextUse(reason);
        Interrupt("performer-refused:" + reason);
    }

    private ObservedDecisionCapabilities ObserveMeans(in ActionContext context)
        => refusalMeans.Observe(CollectNativeEffectReceipts.WorldEpoch,
            context.Player.GetModPlayer<PlayerIntegration.CompanionPlayer>().Gear, context.Companion.Bag,
            PlayerIntegration.CompanionPreferences.Current);

    /// <summary>The reason candidates withheld after a performer's refusal are written under in the capture's
    /// `structurally-refused:` entries. It counts candidates rather than orders, which the name says, and it rides the
    /// structural tally rather than the evidence one because the audit's contracts read only the evidence refusals and a
    /// withholding is not one. Without it a withheld vein read in the record as usable work beside an empty course with
    /// no refusal at all, which is the one record nobody could explain after a play.</summary>
    public const string WithheldAfterPerformerRefusal = "candidates-withheld-after-performer-refusal";
    private int withheldThisDecision;

    private IReadOnlyDictionary<string, int> WithWithheld(IReadOnlyDictionary<string, int> structural)
    {
        if (withheldThisDecision == 0) return structural;
        var merged = new Dictionary<string, int>(structural, StringComparer.Ordinal) { [WithheldAfterPerformerRefusal] = withheldThisDecision };
        return merged;
    }

    private IReadOnlyList<Opportunity> WithoutRefusedByPerformer(in ActionContext context, IReadOnlyList<Opportunity> candidates)
    {
        withheldThisDecision = 0;
        if (refusedByPerformer.Count == 0) return candidates;
        ObservedDecisionCapabilities means = ObserveMeans(context);
        ulong now = Terraria.Main.GameUpdateCount;
        var (reachX, reachY) = FindToolAccess.Reach;
        foreach (var (key, entry) in refusedByPerformer.ToList())
        {
            bool edited = entry.Tile is Point tile && TerrainChanges.Edits.ChangedSince(entry.TerrainRevision,
                (x, y) => Math.Abs(x - tile.X) <= reachX && Math.Abs(y - tile.Y) <= reachY) != TerrainEditVerdict.Unchanged;
            if (edited || now >= entry.Until || means.CapabilityRevision != entry.Capability || means.PolicyRevision != entry.Policy)
                refusedByPerformer.Remove(key);
        }
        if (refusedByPerformer.Count == 0) return candidates;
        var kept = candidates.Where(candidate => !refusedByPerformer.ContainsKey(candidate.Key)).ToList();
        withheldThisDecision = candidates.Count - kept.Count;
        return kept;
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
            refusedByPerformer.Clear();
            observation.ResetWorld();
            capabilities.Reset();
            // A decision in flight is priced against a world that no longer exists, so it is abandoned
            // rather than continued. Its model owner refuses further requests once abandoned, which is
            // the guard that would catch this if the line below were ever removed.
            models?.Abandon();
            deciding = null; models = null; decidingFacts = null; decidingEpisode = null;
            discoveringFacts = null;
        }

        // A decision still discovering keeps the observation it was frozen on, the same way a searching one
        // does below, and begins its search the tick every source has answered for it.
        if (discoveringFacts != null)
        {
            using (BrainSections.Enter(DiscoverySection))
                discovery.Continue(discoveringFacts, budget, PinnedSteps());
            if (!discovery.EverySourceSlicedSinceMark) return Deciding(context);
            DecisionFactSnapshot frozen = discoveringFacts;
            discoveringFacts = null;
            return BeginSearch(context, frozen, budget);
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
            LastStructuralRefusals = WithWithheld(deciding.StructuralRefusals);
            LastLeaders = deciding.Leaders;
            LastRunnerUpOrder = RunnerUpOf(deciding);
            if (!deciding.Exhausted) return Deciding(context);
            return Settle();
        }

        DecisionFactSnapshot facts;
        using (BrainSections.Enter(SnapshotSection)) facts = observation.Capture(context, combat, plans, budget);
        Course.ObserveEpoch(facts.WorldEpoch);
        Course.RetireReceiptsThrough(facts.ReceiptWatermark);
        // The store is brought to this observation on every tick that captured one, a retained one included, so
        // nothing read from it — the recorder's census counts, the inspector, the next decision — describes a world
        // this observation has withdrawn. Discovery below repeats it at no cost when a decision starts.
        using (BrainSections.Enter(DiscoverySection))
            discovery.Retire(facts, Course.Current?.Projection.Steps.Select(step => step.Opportunity) ?? Array.Empty<OpportunityKey>());

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
        // observation so two futures can be compared, and that is `AIC-480`'s territory rather than a
        // number. Protection urgency still reaches the comparison at every decision, through the
        // episode's own discount on non-combat needs.
        if (NextStep(out StepBinding? held) && held != null)
        {
            BindingValidation validation;
            using (BrainSections.Enter(ValidateSection)) validation = binder.ValidateNextUse(held, facts);
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
        //
        // A search over a catalogue discovery never refreshed against this observation would order last
        // observation's work and call the result complete: every source's coverage outlives the pass that
        // wrote it, and a candidate whose evidence left the world has already been retired. So a decision
        // that starts after the tick's allowance is spent — the ordinary case under the game's clock, because
        // every activity prepares first — waits in flight until each source has examined something, which is
        // the third value this tree keeps for an unanswered search rather than a proven absence.
        discovery.MarkObservation();
        models = new RetainCourseModelQueries(facts, MovementQueries.World,
            capabilities.CapabilityRevision, ModelQueueCapacity);
        discoveringBody = new CoursePoint(context.Npc.Center.X, context.Npc.Center.Y);
        using (BrainSections.Enter(DiscoverySection))
            discovery.Continue(facts, budget, PinnedSteps());
        if (!discovery.EverySourceSlicedSinceMark)
        {
            discoveringFacts = facts;
            return Deciding(context);
        }
        return BeginSearch(context, facts, budget);
    }

    private IEnumerable<OpportunityKey> PinnedSteps()
        => Course.Current?.Projection.Steps.Select(step => step.Opportunity) ?? Array.Empty<OpportunityKey>();

    /// <summary>Freezes the catalogue discovery holds for <paramref name="facts"/> and starts the order search over
    /// it, settling in this tick if the allowance lets it.</summary>
    private CourseDecision BeginSearch(in ActionContext context, DecisionFactSnapshot facts, DecisionWorkBudget budget)
    {
        admitted = null;
        IReadOnlyList<Opportunity> candidates = WithoutRefusedByPerformer(context, discovery.Candidates);

        var beginning = BrainSections.Enter(BeginSection);
        var episode = EpisodeFor(context, ++episodes, facts.WorldEpoch, candidates.SelectMany(candidate => candidate.Needs),
            censusComplete: discovery.Coverage.All(coverage => coverage.Exhausted));

        RetainCourseModelQueries decisionModels = models
            ?? throw new InvalidOperationException("A search began without the model owner frozen with its observation.");
        CoursePoint body = discoveringBody;
        var projector = new BindCourseOrder(facts, episode, candidates, binder,
            new ProjectedCourseState(body, facts),
            // The reunion pose is the player's own region centre rather than his feet, because that is
            // where the companion lives and the return leg is priced to where it is going back to.
            new ForecastCourseConsequences(new CoursePoint(
                context.Senses.Intent.Region.Centre.X, context.Senses.Intent.Region.Centre.Y)));

        search.Begin(facts, episode, candidates,
            Course.Current?.Projection.Steps.Select(step => step.Opportunity).ToArray() ?? Array.Empty<OpportunityKey>(),
            projector);
        beginning.Dispose();
        // The owner drives the search rather than the other way round: it answers whatever models the
        // search asked for, extends the frozen catalogue with the answers, and lets the search resume on
        // the extended observation. A search suspended on a model nobody answers never advances.
        Advance(search, decisionModels, budget);
        LastSearch = (search.EvaluatedOrders, search.RejectedOrders, search.Exhausted);
        LastRefusals = search.Refusals;
        LastStructuralRefusals = WithWithheld(search.StructuralRefusals);
        LastLeaders = search.Leaders;
        LastRunnerUpOrder = RunnerUpOf(search);
        deciding = search;
        decidingFacts = facts;
        decidingEpisode = episode;
        if (!search.Exhausted) return Deciding(context);
        return Settle();
    }

    /// <summary>
    /// The comparison episode a decision prices against, built in one place so the in-passing acceptance below reads
    /// optional work's relevance from exactly the inputs a course decision does — the same encounter intensity and the
    /// same protection urgency, through the same <see cref="CourseComparisonEpisode.RelevanceFor"/>.
    /// </summary>
    private static CourseComparisonEpisode EpisodeFor(in ActionContext context, long id, long worldEpoch,
        IEnumerable<UsefulNeed> needs, bool censusComplete)
        => new(id, worldEpoch,
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
            needs,
            censusComplete: censusComplete,
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

    /// <summary>What the course last answered about an in-passing use: the one-step binding it accepted, or why it
    /// refused. Null before anything has been proposed.</summary>
    public IncidentalAcceptance? LastIncidental { get; private set; }

    /// <summary>
    /// Accept or refuse one in-passing use — a pot broken or a torch placed from where the body already is, with no
    /// journey — before its native call. This is the plan's grants row: an incidental interaction needs an accepted
    /// one-step binding, so the post-grant scan is a proposal source the course answers rather than a second chooser
    /// acting on its own.
    ///
    /// <para>Three things must hold, and each is the course's own test rather than a copy of it. The site's binding is
    /// built by the domain's real binder from the current frozen observation and validated by the same
    /// <c>ValidateNextUse</c> a retained course step is held to, so a pot the census no longer holds, or holds as unusable,
    /// is refused exactly as a planned visit to it would be. Its need must be relevant under the danger the course prices
    /// optional work against — the same <see cref="CourseComparisonEpisode.RelevanceFor"/> over the same encounter
    /// intensity and protection urgency a decision reads — so a boss, a blood moon over the surface or a threatened player
    /// that stops the course breaking a pot stops the hand breaking one in passing. And the hand must be free, which the
    /// caller has already read off the grant.</para>
    ///
    /// <para>What acceptance does not do is as deliberate. It does not replace the running course or change the
    /// activity holding the body: the step is zero travel from the current pose, so nothing about where the body goes is
    /// decided here, and a mid-course replacement is the unbuilt reprojection this owner names as out of scope.</para>
    /// </summary>
    /// <param name="site">The census fact of the site, under its own domain.</param>
    public IncidentalAcceptance AcceptIncidental(in ActionContext context, FactKey site)
    {
        IncidentalAcceptance Refuse(string reason) => Record(new IncidentalAcceptance(site, null, reason));
        if (observation.Current is not { } facts) return Refuse("incidental-no-observation");
        if (site.Kind is not ("collect-target" or "light-target")) return Refuse("incidental-not-assistance-work");
        if (!facts.TryRead(site, out DecisionFact fact) || fact.Evidence != FactEvidence.Observed)
            return Refuse("incidental-site-not-observed");
        if (site.Kind == "light-target" && SpoilsAHeldLightSite(context, site))
            return Refuse(IncidentalTorchSpoilsHeldSite);
        Opportunity opportunity = DiscoverAssistanceOpportunities.Read(facts, site.Kind, site);
        if (opportunity.Admission != OpportunityAdmission.KnownUsable) return Refuse(opportunity.Reason);
        var body = new CoursePoint(context.Npc.Center.X, context.Npc.Center.Y);
        BindingResult bound = new AssistanceOpportunityBinder(site.Kind).BindInPlace(opportunity, facts, body);
        if (bound.Binding is not { } step) return Refuse(bound.Reason);
        BindingValidation validation = binder.ValidateNextUse(step, facts);
        if (!validation.CanUse) return Refuse(validation.Reason);
        // Relevance is priced for this one site's need and nothing else, so the episode holds that need alone; the
        // census-completeness flag feeds normalisation, which relevance does not read.
        CourseComparisonEpisode episode = EpisodeFor(context, episodes, facts.WorldEpoch, opportunity.Needs, censusComplete: true);
        if (opportunity.Needs.Any(need => episode.RelevanceFor(need.Key.Kind) <= 0))
            return Refuse(OptionalWorkSuppressed);
        return Record(new IncidentalAcceptance(site, step, "incidental-accepted"));
    }

    /// <summary>The refusal an in-passing use gets when the danger the course prices optional work against leaves its
    /// need worth nothing — the same rule that stops a course choosing that work, applied to the hand.</summary>
    public const string OptionalWorkSuppressed = "incidental-optional-work-suppressed-by-danger";

    /// <summary>The refusal an in-passing torch gets when it would sit within the placer's spacing of a light site the
    /// course holds, which would make the placer refuse that held site and so replace the running course from the hand —
    /// the one thing acceptance may not do. Torches are the one domain where a use in passing can spoil a held step: a pot
    /// broken on the way changes no other pot and no drop.</summary>
    public const string IncidentalTorchSpoilsHeldSite = "incidental-torch-would-spoil-a-held-light-site";

    private bool SpoilsAHeldLightSite(in ActionContext context, FactKey site)
    {
        Point tile = ExecuteCourseBinding.WorkTileOf(new OpportunityKey(site.Kind, OpportunityPurposes.Light, site.Identity, site.Generation));
        IEnumerable<StepBinding> held = Course.Current?.Projection.Steps ?? (IEnumerable<StepBinding>)Array.Empty<StepBinding>();
        // An unsettled tick's executing activity may carry a step the course has not published, the same case the scan
        // itself adds to its planned set.
        if (context.Companion.Brain.Activity.Binding is { } own) held = held.Append(own);
        foreach (StepBinding step in held)
        {
            if (step.Opportunity.Domain != "light-target" || step.Opportunity.Target == site.Identity) continue;
            Point other = ExecuteCourseBinding.WorkTileOf(step);
            if (Math.Abs(other.X - tile.X) <= Interactions.Torch.CompanionTorches.SpacingTiles
                && Math.Abs(other.Y - tile.Y) <= Interactions.Torch.CompanionTorches.SpacingTiles)
                return true;
        }
        return false;
    }

    /// <summary>Keep the answer for readers, and write it to the course trace when it differs from the last one, so a
    /// pot in reach refused every scan through a boss fight is one record rather than one per scan.</summary>
    private IncidentalAcceptance Record(IncidentalAcceptance answer)
    {
        bool changed = LastIncidental is not { } last || last.Site != answer.Site || last.Reason != answer.Reason
            || (last.Binding == null) != (answer.Binding == null);
        LastIncidental = answer;
        if (!changed || observation.Current is not { } facts) return answer;
        RetainedCourse? course = Course.Current;
        var context = new CourseTraceContext(
            SourceTick: facts.Tick,
            NativePhase: "grant-incidental",
            CourseId: course?.Id ?? 0,
            CourseRevision: course?.Revision ?? 0,
            StepId: answer.Binding?.Id ?? 0,
            BindingId: answer.Binding?.Id ?? 0,
            AttemptId: 0,
            Producer: nameof(DecideCourseEachTick),
            ObservationOrdinal: facts.ObservationOrdinal,
            ReceiptWatermark: facts.ReceiptWatermark,
            WorldEpoch: facts.WorldEpoch.ToString(CultureInfo.InvariantCulture),
            SourceRevision: facts.Id.ToString(CultureInfo.InvariantCulture),
            PolicyFingerprint: CourseComparisonEpisode.Policy,
            ConfigurationFingerprint: MotionModelFingerprint);
        RecordCourseTrace.Record(CourseTracePhase.Brain, context, new CourseTracePayload("incidental-acceptance", 1,
            new List<KeyValuePair<string, CourseTraceValue>>
            {
                new("reason", CourseTraceValue.TextValue(answer.Reason)),
                new("accepted", CourseTraceValue.Flag(answer.Binding != null)),
                new("domain", CourseTraceValue.TextValue(answer.Site.Kind)),
                new("target", CourseTraceValue.TextValue(answer.Site.Identity)),
                new("purpose", CourseTraceValue.TextValue(answer.Binding?.Opportunity.Purpose ?? "")),
            }));
        return answer;
    }

    /// <summary>
    /// What the body does on a tick whose decision has not finished: the plan's tick-order step 5, a
    /// cheap legal continuation established from the actual pose before deeper search.
    ///
    /// This asked for companionship until 22 September 2026, and that was not the neutral thing it looks
    /// like. The tick selects the decision's activity, so companionship-while-deciding *selects keeping
    /// company*, which exits whatever was running; combat's <see cref="FightEnemies.Exit"/> releases its
    /// committed plan with <c>activity-exited</c>; the course's accepted use is then absent, so the
    /// course is released; releasing the course starts a decision; and a decision in flight asks for
    /// companionship. That loop is what the play of 0.38.13 recorded as 212 combat attempts at a median
    /// of one tick, 181 of them <c>replaced-before-attacking</c>, 211 plans invalidated
    /// <c>activity-exited</c> and 483 releases reading <c>accepted-use-not-present</c>.
    /// `68f07bd` had already fixed the *identity* half of the same loop, and the docstring on
    /// <c>CombatCourseFacts.UseId</c> describes it; this is the other half, which that fix did not reach
    /// because no fixture ran a decision spanning ticks with an activity already holding the body.
    ///
    /// Combat is the only domain that has a continuation to offer, and that is a property of the domains
    /// rather than a special case: it is the one whose opportunities are a tactical search it has already
    /// run, so the next thing to do is standing there in a committed plan. A tile job's next step is a
    /// course step and comes back the moment the decision settles. A body that was working holds where it
    /// is until then, and one that was already keeping the player company goes on doing so.
    ///
    /// The tick stays unsettled, so nothing here advances the decision identity and nothing here can
    /// start recovery flight (<c>RecoverDistantCompanion.ReunionRequested</c> reads the flag).
    /// </summary>
    private CourseDecision Deciding(in ActionContext context)
    {
        if (context.Companion.Brain.Fighting?.Continuation is { } continuation)
            return Trace(Last = new("combat", null, "deciding-holds-the-fight", Settled: false, continuation));
        // A body that was working when the decision began holds where it is until the decision settles,
        // rather than flying back to the player between two jobs. The owner ruled on 25 September 2026 that
        // keeping company is what the companion does only when there is nothing else to do, and a decision in
        // flight has not found that yet: on the replay of that evening's capture the deciding tick sent the
        // body home for 32 and 38 ticks after a fight plan the world invalidated, with hostiles still there,
        // and each time it then had to fly back out. Keeping company's activity still carries the tick, because
        // no other performer has a step to work; only where the body goes differs, and a companion that was
        // already keeping company keeps doing so.
        if (WasWorking(Last))
            return Trace(Last = new("keep-company", null, DecidingHoldsThePlace, Settled: false, PositionRequest.Hold));
        return Companionship("deciding", settled: false);
    }

    private const string DecidingHoldsThePlace = "deciding-holds-the-place";

    /// <summary>Whether the tick before this one had the body doing work — a bound step, a held fight, or an earlier
    /// tick of this same hold — as opposed to keeping company or having decided nothing yet.</summary>
    private static bool WasWorking(CourseDecision last)
        => last.Reason == DecidingHoldsThePlace || (last.Activity.Length > 0 && last.Activity != "keep-company");

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
        using var settling = BrainSections.Enter(SettleSection);
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

    /// <summary>The search's runner-up as the recorder reads it: the purposes that order would have
    /// performed, in order, and its nominal worth. An empty order keeps an empty purpose list rather
    /// than becoming null, because "the runner-up was doing nothing" is a different answer from "there
    /// was no runner-up" and the first is a real and frequent outcome.</summary>
    private static (IReadOnlyList<string> Purposes, double Value)? RunnerUpOf(SearchCourseOrders search)
        => search.RunnerUp is { } order && search.RunnerUpValue is { } value
            ? (order.Steps.Select(step => step.Opportunity.Purpose).ToArray(), value.Total.Nominal)
            : null;

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
        string activity = ExecuteCourseBinding.ActivityFor(step.Opportunity.Domain);
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
    /// The fields are chosen so the four unpublished states that look identical from outside can be told
    /// apart: the course is running, the course found nothing worth doing, the brain has not finished
    /// deciding, and a proposal was refused publication. Three of the four ask the body to keep the
    /// player company. **The deciding state no longer always does**, since 22 September 2026: it asks
    /// combat for a continuation first and carries that plan's own stand when one exists, because
    /// selecting companionship mid-decision exits a running fight and releases its committed plan. So a
    /// reader distinguishing these states by the *request* will merge deciding with combat on exactly
    /// the ticks that matter; the reason string is what separates them, and the order counts and refusal
    /// tally say what the search did to get there.
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
        // The decision's own record, built every tick whether or not a session is recording, is a cost of
        // deciding the recorder does not see, so it is a section of its own.
        using var tracing = BrainSections.Enter(TraceSection);
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
        // Structural refusals are written under their own prefix and are never cut, for two reasons that
        // are separate defects. The audit's contracts ask whether every refusal was one of the two
        // not-observed strings, so a structural reason sharing the prefix silences them on any decision
        // where a pot was admitted. And the cut above is top-four-by-count while a pot refusal scales
        // with the number of pots, so several pots could push the two strings the contracts key on out of
        // the record entirely — blinding a contract by deleting its evidence rather than by failing a
        // predicate. There is at most one entry here per unexecutable purpose, so uncut is bounded.
        foreach (var refusal in LastStructuralRefusals.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            fields.Add(new("structurally-refused:" + refusal.Key, CourseTraceValue.Integer(refusal.Value)));
        RecordCourseTrace.Record(CourseTracePhase.Brain, context,
            new CourseTracePayload("course-decision", 1, fields));
        return decision;
    }

    /// <summary>Names the prediction law this brain decided under, so a recording says which rules
    /// produced it rather than leaving a reader to assume the current ones.</summary>
    private static string MotionModelFingerprint =>
        "motion:" + ForecastCourseConsequences.MotionModelRevision.ToString(CultureInfo.InvariantCulture);
}
