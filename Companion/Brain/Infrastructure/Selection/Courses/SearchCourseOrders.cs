#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

public enum ProjectionStatus { Complete, Pending, Rejected }
public sealed record CourseProjectionResult(ProjectionStatus Status, CourseProjection? Projection, string Reason,
    IReadOnlyList<CourseTravelRequest>? RequiredTravel = null,
    IReadOnlyList<CourseEnemyMotionRequest>? RequiredEnemyMotion = null);
public interface ICourseProjector
{
    /// <summary>The cursor and private projection state resume the same order. Predictions
    /// read the snapshot/overlay, never live engine state. Completed model answers may append
    /// to frozen inputs. A rejected order is not unfinished.</summary>
    CourseProjectionResult Continue(IReadOnlyList<OpportunityKey> order, DecisionFactSnapshot facts,
        CourseComparisonEpisode episode, DecisionWorkCursor cursor, DecisionWorkBudget budget);
}

/// <summary>Resumable local ordering, with a whole-region seed for every discovered site.
/// No factorial enumeration and no permanent first-N candidate filter. MaxDepth limits
/// one retained suffix, not which sites are ever examined.</summary>
public sealed class SearchCourseOrders
{
    // Profiler sections: projecting one order's consequences, and valuing a projection. What the search spends
    // outside both is enumerating orders and keeping the leaders, which is its own self time.
    private static readonly int ProjectSection = Diagnostics.BrainSections.Register("project");
    private static readonly int ValueSection = Diagnostics.BrainSections.Register("value");
    private IEnumerator<OpportunityKey[]>? orders;
    private OpportunityKey[]? pendingOrder;
    private readonly DecisionWorkCursor projectionCursor = new();
    private CourseComparisonEpisode? episode;
    private DecisionFactSnapshot? facts;
    private ICourseProjector? projector;
    private long generation;
    public SearchCourseOrders(int maxDepth)
    {
        if (maxDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));
        MaxDepth = maxDepth;
    }
    public int MaxDepth { get; }
    public CourseProjection? Best { get; private set; }
    public CourseValue? BestValue { get; private set; }

    /// <summary>
    /// The best order the search priced whose *first step* is not the one the winner takes, and what it
    /// was worth.
    ///
    /// The winner alone cannot answer the only question anybody asks of a losing decision, which is what
    /// it beat. A recording that carries the chosen order and nothing else leaves a reader unable to tell
    /// a fight that lost narrowly from one that was never on the board, and the recorder writes this
    /// column as `task_order_runner_up` for exactly that comparison. The first step rather than the whole
    /// order is the discriminator because the first step is the only part the tick performs: two orders
    /// that start the same way are one answer to that question however they differ afterwards.
    ///
    /// The best-per-first-step table is kept through the search, because the search discards every order
    /// it does not keep as it goes; **the runner-up itself is picked from that table on first read and
    /// cached until the next priced order**, so the cost of the column is one table entry per distinct
    /// first step and one scan per *decision* rather than one per order. It used to scan on every priced
    /// order, which was the same answer computed k times for k orders and thrown away k−1 times.
    ///
    /// Null when the search priced fewer than two distinct first steps, which is a real answer and not a
    /// missing one: a decision with one order on the board has no alternative and should not be recorded
    /// as though its alternative was unreadable.
    /// </summary>
    public CourseProjection? RunnerUp { get { SettleRunnerUp(); return runnerUp; } }
    public CourseValue? RunnerUpValue { get { SettleRunnerUp(); return runnerUpValue; } }
    private CourseProjection? runnerUp;
    private CourseValue? runnerUpValue;
    private bool runnerUpSettled = true;

    /// <summary>The best priced order for each distinct first step, which is what the runner-up is chosen
    /// from. The empty order sits beside the dictionary rather than in it — a dictionary refuses a null
    /// key, and the empty order has no first step and is a legitimate rival, so it needs a slot of its
    /// own rather than a sentinel key some real opportunity could one day collide with.</summary>
    private readonly Dictionary<OpportunityKey, (CourseProjection Projection, CourseValue Value)> byFirstStep = new();
    private (CourseProjection Projection, CourseValue Value)? idleOrder;
    private OpportunityKey? bestFirstStep;
    public long EvaluatedOrders { get; private set; }
    public long RejectedOrders { get; private set; }
    public bool Exhausted { get; private set; }
    public bool DepthTruncated { get; private set; }
    public IReadOnlyList<OpportunityKey> PendingOrder => pendingOrder ?? Array.Empty<OpportunityKey>();
    /// <summary>
    /// Why orders were refused this search, counted by reason, newest reason last.
    ///
    /// A rejection count on its own is the number that cannot be acted on. A scene with one ore site and
    /// thirteen discovered shots refused twenty of twenty-five orders and priced five, and nothing
    /// anywhere said why — so diagnosing it took three separate angles, each of which had to guess at a
    /// mechanism and then disprove it. Every refusal already carries a reason string from
    /// `BindCourseOrder`; it was simply discarded at this line.
    ///
    /// The tally is bounded rather than a log, because a search can refuse many orders for one reason
    /// and the useful shape is which reasons and in what proportion, not a row per order. A reason is
    /// recorded once with a count; a search that invents unbounded distinct reasons stops recording new
    /// ones rather than growing without limit, and says so through the `refusal-kinds-truncated` entry.
    /// </summary>
    public IReadOnlyDictionary<string, int> Refusals => refusals;
    private readonly Dictionary<string, int> refusals = new(StringComparer.Ordinal);
    private const int MaximumRefusalKinds = 16;

    /// <summary>The reason an opportunity no activity can perform is dropped before enumeration. It is
    /// a literal a row reads back out of this file, so renaming it fails that row rather than quietly
    /// making a capture's refusal tally unreadable.</summary>
    public const string StepPurposeHasNoExecutor = "step-purpose-has-no-executor";

    /// <summary>
    /// Refusals proved by a fact of the source tree rather than by the state of an observation, counted
    /// apart from <see cref="Refusals"/> and never mixed into it.
    ///
    /// **The split is a defect fix rather than a taxonomy.** `AuditDecisionContracts`' contract two,
    /// `empty-course-beside-usable-work`, fires only when *every* refusal the search recorded is one of
    /// the two not-observed strings, because a course refusing work on a preference is the brain
    /// working. `step-purpose-has-no-executor` went into the same tally, so on any decision where the
    /// census admitted a usable pot the predicate saw a third reason and the contract could not fire —
    /// and the case that matters is the mixed one, a settled empty course beside a usable light site
    /// *and* a refused pot, which is exactly the shape that tripwire exists to catch.
    ///
    /// The sharper half was the record rather than the predicate: the payload carries only the top four
    /// reasons by count and a pot refusal scales with the number of pots admitted, so several pots could
    /// push `target-capture-missing` and `assistance-target-unresolved` out of the written record
    /// entirely and blind contract *one* as well — silently, because the audit would be reading a
    /// payload that no longer carried the strings. Structural reasons are written uncut and under their
    /// own prefix now, and there are at most as many of them as there are unexecutable purposes.
    ///
    /// The line between the two classes is what would change the answer: an evidence refusal can become
    /// an acceptance when the world or the observation changes, and a structural one cannot, because it
    /// is settled by `ExecuteCourseBinding`'s map.
    /// </summary>
    public IReadOnlyDictionary<string, int> StructuralRefusals => structuralRefusals;
    private readonly Dictionary<string, int> structuralRefusals = new(StringComparer.Ordinal);

    private void Refuse(string reason)
    {
        string key = string.IsNullOrEmpty(reason) ? "unstated" : reason;
        if (refusals.ContainsKey(key)) { refusals[key]++; return; }
        if (refusals.Count >= MaximumRefusalKinds) key = "refusal-kinds-truncated";
        refusals[key] = refusals.GetValueOrDefault(key) + 1;
    }

    /// <summary>
    /// The best value any order led by each domain reached, so a losing domain can say what it lost by.
    ///
    /// A search reports one winner, and a winner alone cannot answer the question every adjudication of
    /// a behaviour row actually asks: not "what won" but "why did combat lose". Without this the only
    /// way to find out is to reason about the objective from the outside and then guess which of its
    /// terms dominated — which on the danger-over-work row produced three wrong causes in a row before
    /// the fourth measurement contradicted all of them.
    ///
    /// Keyed by the first step's domain because that is what a reader means by "the combat option": an
    /// order beginning with a shot, whatever it chains afterwards. An empty order is its own key, since
    /// doing nothing is a real candidate and its value is the floor every job has to clear. The map is
    /// bounded by the number of domains that exist, so it needs no truncation rule.
    /// </summary>
    public IReadOnlyDictionary<string, CourseValue> Leaders => leaders;
    private readonly Dictionary<string, CourseValue> leaders = new(StringComparer.Ordinal);
    public const string IdleOrderKey = "(idle)";

    public IReadOnlyList<CourseTravelRequest> RequiredTravel { get; private set; } = Array.Empty<CourseTravelRequest>();
    public IReadOnlyList<CourseEnemyMotionRequest> RequiredEnemyMotion { get; private set; } = Array.Empty<CourseEnemyMotionRequest>();

    public void ExtendModelFacts(DecisionFactSnapshot extended)
    {
        if (facts == null || !extended.IsModelExtensionOf(facts))
            throw new InvalidOperationException("Course search accepts only appended model answers for its frozen observation.");
        facts = extended;
    }

    public void Begin(DecisionFactSnapshot facts, CourseComparisonEpisode episode,
        IReadOnlyList<Opportunity> opportunities, IReadOnlyList<OpportunityKey> retained, ICourseProjector projector)
    {
        orders?.Dispose();
        this.facts = facts; this.episode = episode; this.projector = projector;
        // An episode is frozen even as the game proceeds; publication separately revalidates
        // the resulting prefix. Restart only when a read dependency actually changed.
        // A domain no registered activity declares cannot be a step, and that is a proven refusal rather
        // than the middle value: `ExecuteCourseBinding`'s map is built from the activities' own
        // declarations, a fact of this tree, so nothing about the world, the allowance or a later slice
        // can turn a no into a yes, and reading it as unresolved would park the opportunity for ever
        // instead of dropping it. The reason string keeps its old wording because captures carry it.
        //
        // It is refused here, before enumeration, rather than inside the projection, because an
        // unexecutable opportunity may sit at any position of any order: filtering the candidate set
        // removes it from every order at once where a per-step check would have to run inside each
        // projection and would still admit the order until it reached that step.
        //
        // The class it closes: any domain that gains discovery before an activity declares it fails
        // silently, because a course that publishes work nothing performs looks from outside like a
        // companion that decided something and then stood there. A pot was the instance, when a
        // hand-written purpose table disagreed with the activity that performed pots; the table is
        // derived from the declarations now, so this filter only ever refuses a domain that truly has
        // no performer.
        var executable = opportunities.Where(o => o.Admission == OpportunityAdmission.KnownUsable
            && ExecuteCourseBinding.HasExecutor(o.Key.Domain)).ToArray();
        int unexecutable = opportunities.Count(o => o.Admission == OpportunityAdmission.KnownUsable) - executable.Length;
        var usable = executable.OrderBy(o => o.Key).ToArray();
        DepthTruncated = usable.Length > MaxDepth;
        orders = Enumerate(usable, retained, MaxDepth).GetEnumerator();
        pendingOrder = null; Best = null; BestValue = null; Exhausted = false;
        runnerUp = null; runnerUpValue = null; runnerUpSettled = true;
        bestFirstStep = null; byFirstStep.Clear(); idleOrder = null;
        EvaluatedOrders = RejectedOrders = 0;
        refusals.Clear();
        structuralRefusals.Clear();
        // Recorded after the clear, so the count belongs to this search, and by name so a capture says
        // which reason it was rather than leaving a domain's whole census unaccounted for. It goes in
        // the structural tally rather than the evidence one for the reason that tally's docstring gives.
        if (unexecutable > 0) structuralRefusals[StepPurposeHasNoExecutor] = unexecutable;
        leaders.Clear();
        RequiredTravel = Array.Empty<CourseTravelRequest>();
        RequiredEnemyMotion = Array.Empty<CourseEnemyMotionRequest>();
        generation++;
    }

    public void Continue(DecisionWorkBudget budget)
    {
        if (orders == null || facts == null || episode == null || projector == null || Exhausted) return;
        while (!budget.Exhausted)
        {
            if (pendingOrder == null)
            {
                if (!budget.TrySpend("course-order")) return;
                if (!orders.MoveNext()) { Exhausted = true; orders.Dispose(); orders = null; return; }
                pendingOrder = orders.Current;
                projectionCursor.Bind(++generation, "next-course-order");
            }
            CourseProjectionResult result;
            using (Diagnostics.BrainSections.Enter(ProjectSection))
                result = projector.Continue(pendingOrder, facts, episode, projectionCursor, budget);
            RequiredTravel = result.Status == ProjectionStatus.Pending
                ? Array.AsReadOnly((result.RequiredTravel ?? Array.Empty<CourseTravelRequest>()).ToArray())
                : Array.Empty<CourseTravelRequest>();
            RequiredEnemyMotion = result.Status == ProjectionStatus.Pending
                ? Array.AsReadOnly((result.RequiredEnemyMotion ?? Array.Empty<CourseEnemyMotionRequest>()).ToArray())
                : Array.Empty<CourseEnemyMotionRequest>();
            if (result.Status == ProjectionStatus.Pending) return;
            if (result.Status == ProjectionStatus.Rejected || result.Projection == null)
            {
                RejectedOrders++;
                Refuse(result.Reason);
            }
            else
            {
                EvaluatedOrders++;
                CourseValue value;
                using (Diagnostics.BrainSections.Enter(ValueSection)) value = CompareCourseOutcomes.Evaluate(result.Projection, episode);
                string lead = pendingOrder.Length == 0 ? IdleOrderKey : pendingOrder[0].Domain;
                if (!leaders.TryGetValue(lead, out var best)
                    || CompareCourseOutcomes.NominalOrder(value, best, episode.Encounter) > 0)
                    leaders[lead] = value;
                OpportunityKey? first = pendingOrder.Length == 0 ? null : pendingOrder[0];
                if (first is { } key)
                {
                    if (!byFirstStep.TryGetValue(key, out var heldForFirst)
                        || CompareCourseOutcomes.NominalOrder(value, heldForFirst.Value, episode.Encounter) > 0)
                        byFirstStep[key] = (result.Projection, value);
                }
                else if (idleOrder == null
                    || CompareCourseOutcomes.NominalOrder(value, idleOrder.Value.Value, episode.Encounter) > 0)
                    idleOrder = (result.Projection, value);
                if (BestValue == null || CompareCourseOutcomes.NominalOrder(value, BestValue, episode.Encounter) > 0)
                { Best = result.Projection; BestValue = value; bestFirstStep = first; }
                runnerUpSettled = false;
            }
            pendingOrder = null;
        }
    }

    /// <summary>Pick the runner-up afresh from the best-per-first-step table, once per read rather than
    /// once per priced order. Recomputed from the table rather than carried forward because the winner
    /// can change under the search, and a runner-up maintained incrementally against a moving winner is
    /// the order that used to be second rather than the one that is second now — which is the wrong
    /// answer in exactly the case a reader opens the column for. Pricing an order therefore only marks
    /// this unsettled; the scan happens when somebody asks, and every answer between two priced orders
    /// is the same answer.</summary>
    private void SettleRunnerUp()
    {
        if (runnerUpSettled) return;
        runnerUp = null; runnerUpValue = null;
        foreach (var entry in byFirstStep)
        {
            if (bestFirstStep is { } winner && entry.Key.Equals(winner)) continue;
            Offer(entry.Value.Projection, entry.Value.Value);
        }
        if (bestFirstStep != null && idleOrder is { } idle) Offer(idle.Projection, idle.Value);
        runnerUpSettled = true;
    }

    private void Offer(CourseProjection projection, CourseValue value)
    {
        if (runnerUpValue != null
            && CompareCourseOutcomes.NominalOrder(value, runnerUpValue, episode!.Encounter) <= 0) return;
        runnerUp = projection;
        runnerUpValue = value;
    }

    private static IEnumerable<OpportunityKey[]> Enumerate(Opportunity[] opportunities,
        IReadOnlyList<OpportunityKey> retained, int depth)
    {
        var byKey = opportunities.ToDictionary(o => o.Key);
        var held = retained.Where(byKey.ContainsKey).Distinct().Take(depth).ToArray();
        // The same retained order is first on an exact tie. Cold starts use key ordering.
        if (held.Length > 0) yield return held;
        yield return Array.Empty<OpportunityKey>();
        foreach (var site in opportunities) yield return new[] { site.Key };
        for (int i = 0; i < held.Length; i++)
            yield return held.Where((_, index) => index != i).ToArray();
        for (int i = 0; i + 1 < held.Length; i++)
        {
            var exchanged = (OpportunityKey[])held.Clone();
            (exchanged[i], exchanged[i + 1]) = (exchanged[i + 1], exchanged[i]);
            yield return exchanged;
        }
        foreach (var site in opportunities)
        {
            if (held.Contains(site.Key)) continue;
            for (int i = 0; i <= held.Length && i < depth; i++)
                yield return held.Take(i).Append(site.Key).Concat(held.Skip(i)).Take(depth).ToArray();
            for (int i = 0; i < held.Length; i++)
            {
                var substituted = (OpportunityKey[])held.Clone();
                substituted[i] = site.Key;
                yield return substituted;
            }
        }
        // Each seed gets its own spatial chain. A distant cluster can therefore compete
        // as a trip even when none of its individual sites is the nearest task.
        foreach (var seed in opportunities)
        {
            var chain = new List<OpportunityKey> { seed.Key };
            var position = seed.Target;
            while (chain.Count < depth && chain.Count < opportunities.Length)
            {
                var next = opportunities.Where(o => !chain.Contains(o.Key))
                    .OrderBy(o => position.DistanceTo(o.Target)).ThenBy(o => o.Key).First();
                chain.Add(next.Key); position = next.Target;
                yield return chain.ToArray();
            }
        }
    }
}
