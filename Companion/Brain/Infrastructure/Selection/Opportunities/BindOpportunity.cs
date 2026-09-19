#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

public sealed class BindOpportunity
{
    private readonly Dictionary<string, IOpportunityBinder> binders;
    public BindOpportunity(IEnumerable<IOpportunityBinder> binders)
        => this.binders = binders.ToDictionary(b => b.Domain, StringComparer.Ordinal);

    public BindingResult Bind(Opportunity opportunity, ProjectedCourseState state,
        DecisionFactSnapshot facts, DecisionWorkCursor cursor, DecisionWorkBudget budget)
    {
        if (opportunity.Admission != OpportunityAdmission.KnownUsable)
            return new(null, opportunity.Admission, opportunity.Reason, opportunity.Admission == OpportunityAdmission.Unresolved);
        if (budget.Exhausted) return new(null, OpportunityAdmission.Unresolved, "budget-cut", true);
        if (!binders.TryGetValue(opportunity.Key.Domain, out var binder))
            throw new InvalidOperationException("No binder for opportunity source " + opportunity.Key.Domain);
        var reads = facts.Track();
        state.BeginReadTracking();
        var result = binder.Bind(opportunity, state, reads, cursor, budget);
        if (result.RequiredTravel is { Count: > 0 } requests)
        {
            var pendingReads = reads.Manifest().Reads.Where(read => read.Evidence == FactEvidence.Missing).Select(read => read.Key).ToHashSet();
            if (!result.Pending || result.Binding != null || requests.Any(request => !pendingReads.Contains(request.Key)))
                throw new InvalidOperationException("A binding's travel requests must identify its pending missing fact reads.");
        }
        if (result.Binding is not { } binding) return result;
        if (binding.Opportunity != opportunity.Key || binding.WorldEpoch != facts.WorldEpoch || binding.SnapshotId != facts.Id)
            throw new InvalidOperationException("The binder returned a different opportunity or observation epoch.");
        // The returned manifest must include every read, including missing facts. A cached
        // model read is still a read and cannot disappear from replay inputs.
        var declared = binding.Dependencies.Reads.ToDictionary(r => r.Key);
        if (reads.Manifest().Reads.Any(r => !declared.TryGetValue(r.Key, out var captured) || captured != r))
            throw new InvalidOperationException("Binding omitted tracked input dependencies.");
        var effectReads = state.ReadEffects.ToHashSet();
        if (effectReads.Any(effectId => !binding.Parents.Contains(effectId)))
            throw new InvalidOperationException("Binding omitted hypothetical effect dependencies.");
        return binding.SufficientlyModelled ? result
            : new(null, OpportunityAdmission.Unresolved, "prefix-not-sufficiently-modelled", result.Pending);
    }

    public BindingValidation ValidateNextUse(StepBinding binding, DecisionFactSnapshot facts)
    {
        if (binding.WorldEpoch != facts.WorldEpoch) return new(OpportunityAdmission.KnownUnusable, "world-epoch-changed", true);
        if (!binders.TryGetValue(binding.Opportunity.Domain, out var binder))
            return new(OpportunityAdmission.KnownUnusable, "binder-missing", true);
        // Version changes require domain validation, not an automatic veto: a moving target
        // can still be a legal target from the same firing region.
        return binder.ValidateNextUse(binding, facts);
    }
}
