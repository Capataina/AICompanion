#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>Forecasts the costs of the complete bound trajectory, including returning to
/// company. An empty order still needs this forecast; absence of work is not zero cost.</summary>
public interface ICourseConsequenceForecast
{
    CourseProjectionResult Continue(IReadOnlyList<StepBinding> steps, ProjectedCourseState successor,
        DecisionFactSnapshot facts, CourseComparisonEpisode episode, DecisionWorkCursor cursor,
        DecisionWorkBudget budget);
}

/// <summary>One frozen search's sequential binder. A cut retains completed bindings and
/// the current domain cursor; hypothetical state is private to one candidate order.</summary>
public sealed class BindCourseOrder : ICourseProjector
{
    private readonly Dictionary<OpportunityKey, Opportunity> opportunities;
    private readonly BindOpportunity binder;
    private readonly ProjectedCourseState initial;
    private readonly ICourseConsequenceForecast forecast;
    private readonly DecisionFactSnapshot snapshot;
    private readonly CourseComparisonEpisode comparison;
    private readonly List<StepBinding> steps = new();
    private readonly DecisionWorkCursor bindingCursor = new();
    private readonly DecisionWorkCursor forecastCursor = new();
    private DecisionWorkCursor? owner;
    private long epoch;
    private long bindingEpoch;
    private OpportunityKey[] order = Array.Empty<OpportunityKey>();
    private ProjectedCourseState state;
    private CourseProjectionResult? terminal;
    private StepBinding? awaitingApplication;

    public BindCourseOrder(DecisionFactSnapshot snapshot, CourseComparisonEpisode comparison,
        IEnumerable<Opportunity> opportunities, BindOpportunity binder, ProjectedCourseState initial,
        ICourseConsequenceForecast forecast)
    {
        if (snapshot.WorldEpoch != comparison.WorldEpoch)
            throw new ArgumentException("Projection snapshot and comparison must share a world epoch.");
        this.snapshot = snapshot; this.comparison = comparison;
        this.opportunities = opportunities.ToDictionary(opportunity => opportunity.Key);
        this.binder = binder; this.initial = initial.Fork(); this.forecast = forecast;
        state = this.initial.Fork();
    }

    public CourseProjectionResult Continue(IReadOnlyList<OpportunityKey> requested,
        DecisionFactSnapshot facts, CourseComparisonEpisode episode, DecisionWorkCursor cursor,
        DecisionWorkBudget budget)
    {
        // Equal numeric IDs are insufficient: a caller must not replace a frozen input
        // object while a domain's private search is still consuming its previous values.
        if (!ReferenceEquals(facts, snapshot) || !ReferenceEquals(episode, comparison))
            throw new InvalidOperationException("A course projector belongs to one frozen comparison snapshot.");
        if (!ReferenceEquals(owner, cursor) || epoch != cursor.Epoch)
        {
            owner = cursor; epoch = cursor.Epoch; order = requested.ToArray();
            steps.Clear(); state = initial.Fork(); terminal = null; awaitingApplication = null;
            bindingCursor.Bind(++bindingEpoch, "course-prefix");
            forecastCursor.Bind(bindingEpoch, "course-consequences");
        }
        else if (!order.SequenceEqual(requested))
            throw new InvalidOperationException("Changing a projected order requires a new cursor epoch.");
        if (terminal != null) return terminal;

        while (steps.Count < order.Length)
        {
            if (awaitingApplication == null)
            {
                if (!opportunities.TryGetValue(order[steps.Count], out var opportunity))
                    return Finish(ProjectionStatus.Rejected, null, "opportunity-not-in-frozen-census", cursor);
                var result = binder.Bind(opportunity, state, facts, bindingCursor, budget);
                if (result.Pending) return new(ProjectionStatus.Pending, null, result.Reason);
                if (result.Binding == null || result.Admission != OpportunityAdmission.KnownUsable)
                    return Finish(ProjectionStatus.Rejected, null, result.Reason, cursor);
                awaitingApplication = result.Binding;
            }
            // A binder may spend the final operation. Keep its accepted result so resuming
            // projection never repeats a native simulation or generates another use identity.
            if (!budget.TrySpend("apply-course-binding")) return new(ProjectionStatus.Pending, null, "budget-cut");
            var capacities = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var resource in awaitingApplication.Resources.Where(resource => resource.Resource is CourseResource.Mana or CourseResource.Cargo))
            {
                var key = new FactKey("capacity", resource.CapacityKey);
                if (!facts.TryRead(key, out var fact) || fact.Evidence != FactEvidence.Observed
                    || !awaitingApplication.Dependencies.Reads.Any(read => read.Key == key && read.Digest == fact.Digest))
                    return Finish(ProjectionStatus.Rejected, null, "capacity-input-unresolved:" + resource.CapacityKey, cursor);
                capacities[resource.CapacityKey] = fact.Value.Amount;
            }
            if (!state.TryApply(awaitingApplication, capacities, out string reason))
                return Finish(ProjectionStatus.Rejected, null, reason, cursor);
            steps.Add(awaitingApplication); awaitingApplication = null;
            bindingCursor.Bind(++bindingEpoch, "next-native-use");
        }

        var consequence = forecast.Continue(steps.AsReadOnly(), state.Fork(), facts, episode, forecastCursor, budget);
        if (consequence.Status == ProjectionStatus.Pending) return consequence;
        if (consequence.Status == ProjectionStatus.Complete)
        {
            if (consequence.Projection is not { } projection || !projection.Steps.SequenceEqual(steps))
                throw new InvalidOperationException("Consequence forecast replaced the bound course order.");
            // Publication validates again against current observations; this checks only
            // that the frozen candidate is internally causal before it can be compared.
            _ = CourseDependencyIndex.Build(projection);
        }
        return Finish(consequence.Status, consequence.Projection, consequence.Reason, cursor);
    }

    private CourseProjectionResult Finish(ProjectionStatus status, CourseProjection? projection,
        string reason, DecisionWorkCursor cursor)
    {
        cursor.Complete();
        return terminal = new(status, projection, reason);
    }
}
