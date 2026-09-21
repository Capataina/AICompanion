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

    private void Refuse(string reason)
    {
        string key = string.IsNullOrEmpty(reason) ? "unstated" : reason;
        if (refusals.ContainsKey(key)) { refusals[key]++; return; }
        if (refusals.Count >= MaximumRefusalKinds) key = "refusal-kinds-truncated";
        refusals[key] = refusals.GetValueOrDefault(key) + 1;
    }

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
        var usable = opportunities.Where(o => o.Admission == OpportunityAdmission.KnownUsable).OrderBy(o => o.Key).ToArray();
        DepthTruncated = usable.Length > MaxDepth;
        orders = Enumerate(usable, retained, MaxDepth).GetEnumerator();
        pendingOrder = null; Best = null; BestValue = null; Exhausted = false;
        EvaluatedOrders = RejectedOrders = 0;
        refusals.Clear();
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
            var result = projector.Continue(pendingOrder, facts, episode, projectionCursor, budget);
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
                var value = CompareCourseOutcomes.Evaluate(result.Projection, episode);
                if (BestValue == null || CompareCourseOutcomes.NominalOrder(value, BestValue, episode.Encounter) > 0)
                { Best = result.Projection; BestValue = value; }
            }
            pendingOrder = null;
        }
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
