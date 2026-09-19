#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>The only owner allowed to publish the next binding. Execution remains with
/// OwnCurrentActivity; speculative repair cannot change that executor or its grants.</summary>
public sealed class RetainCourse
{
    public RetainedCourse? Current { get; private set; }
    public CourseDependencyIndex Dependencies { get; private set; } = new();
    public RepairCourse Repair { get; } = new();
    public CourseEffectLedger Effects { get; } = new();
    public ExecutionBoundary Boundary { get; private set; } = ExecutionBoundary.ColdStart;
    public CourseComparison? LastComparison { get; private set; }
    public string ReleaseReason { get; private set; } = "cold-start";
    private long worldEpoch = -1;
    private readonly HashSet<long> executionReceipts = new();
    private long retiredReceipt;
    private readonly Dictionary<long, (long Binding, PredictedEffect Forecast, long OriginTick)> dispatched = new();
    private long currentOriginTick;
    private readonly HashSet<long> completedCauses = new();
    public IReadOnlyList<PredictedEffect> OutstandingEffects => Array.AsReadOnly(dispatched.Values.Select(value => value.Forecast).ToArray());

    public void ObserveEpoch(long epoch)
    {
        if (epoch == worldEpoch) return;
        worldEpoch = epoch;
        dispatched.Clear(); completedCauses.Clear();
        Release("world-epoch-changed");
        Effects.Clear(); executionReceipts.Clear(); retiredReceipt = 0;
        Boundary = ExecutionBoundary.ColdStart;
    }

    public void ObserveExecution(ExecutionReceipt receipt, IEnumerable<long>? dispatchedEffectIds = null)
    {
        if (Current?.Projection.Prefix is not { } prefix || prefix.Id != receipt.BindingId) return;
        var launched = (dispatchedEffectIds ?? Array.Empty<long>()).Distinct().ToArray();
        if (launched.Any(id => !prefix.Effects.Any(effect => effect.Id == id))
            || launched.Length > 0 && (receipt.NativeReceiptId <= 0 || string.IsNullOrEmpty(receipt.NativeUse)))
            throw new ArgumentException("A native dispatch may name only effects of its accepted binding.", nameof(dispatchedEffectIds));
        if (receipt.NativeReceiptId != 0 && (receipt.NativeReceiptId <= retiredReceipt || !executionReceipts.Add(receipt.NativeReceiptId))) return;
        foreach (var effect in prefix.Effects.Where(effect => launched.Contains(effect.Id)).OrderBy(effect => effect.NominalTick).ThenBy(effect => effect.Id))
            dispatched.TryAdd(effect.Id, (prefix.Id, effect, currentOriginTick));
        if (launched.Length > 0) completedCauses.Add(prefix.Id);
        if (receipt.Boundary != ExecutionBoundary.None) Boundary = receipt.Boundary;
        else BeginExecution(receipt.BindingId);
        if (receipt.Boundary is ExecutionBoundary.NativeUseComplete or ExecutionBoundary.BindingExhausted)
        {
            // Only observed execution advances this cursor. Re-observation cannot silently
            // skip a use, and a released projectile never keeps its firing use executable.
            completedCauses.Add(prefix.Id);
            var before = Current.Projection;
            var remaining = new CourseProjection(before.Steps.Skip(1), before.Harm, before.Companionship,
                before.ReunionTick, before.ReunionProven, before.TailNominal, before.TailUnresolved,
                projectionStartTick: before.ProjectionStartTick + prefix.TravelTicks + prefix.UseTicks,
                consequenceDependencies: before.ConsequenceDependencies);
            ReplaceObservedProjection(WithPhysicalEffects(remaining, currentOriginTick));
            Current = Current! with { SourceSnapshot = -1 };
        }
    }

    /// <summary>The selection opportunity at the previous completion ends when the next
    /// accepted use starts. Keeping that use must not retain permission to switch mid-flight.</summary>
    public void BeginExecution(long bindingId)
    {
        if (Current?.Projection.Prefix?.Id == bindingId && Boundary != ExecutionBoundary.Invalidated)
            Boundary = ExecutionBoundary.None;
    }

    /// <summary>Only a native terminal observation retires a physical effect. Selection,
    /// prediction error and resource pressure cannot despawn a projectile or erase its credit.</summary>
    public void ObserveEffectTerminal(long effectId, bool realised, string reason)
    {
        if (!dispatched.Remove(effectId)) return;
        if (realised) completedCauses.Add(effectId);
        else
        {
            // A native miss does not despawn descendants already issued from the same
            // forecast. Their old conditional state is no longer evidence, though, so
            // remove only the broken causal branch and retain each physical identity.
            var invalid = new HashSet<long> { effectId };
            // Publication and native dispatch keep parents before their children.
            // One forward pass therefore reaches every issued descendant.
            foreach (var pair in dispatched)
                if (pair.Value.Forecast.Parents.Any(invalid.Contains)) invalid.Add(pair.Key);
            foreach (long descendantId in invalid.Where(id => id != effectId).ToArray())
            {
                var issued = dispatched[descendantId];
                var forecast = issued.Forecast;
                dispatched[descendantId] = (issued.Binding, new PredictedEffect(forecast.Id, forecast.Need, forecast.Amount,
                    forecast.NominalTick, forecast.EarliestTick, forecast.LatestTick, EstimateStatus.Unresolved,
                    forecast.Parents.Where(parent => !invalid.Contains(parent)), Array.Empty<EffectDelta>(), forecast.Dependencies),
                    issued.OriginTick);
            }
            Repair.Invalidate(effectId, reason);
        }
        // Keep the old graph until its dependent repair completes; it is the route from
        // this now-invalid physical prediction to the future uses which relied on it.
    }

    public ReceiptAllocation ApplyEffectReceipt(long receiptId, NeedKey need, double amount)
        => Effects.Apply(receiptId, need, amount, dispatched.Values.Select(value => value.Forecast));

    public bool ReobserveOutstandingEffect(PredictedEffect forecast, DecisionFactSnapshot facts)
    {
        if (facts.WorldEpoch != worldEpoch || !dispatched.TryGetValue(forecast.Id, out var issued)
            || forecast.Need != issued.Forecast.Need || forecast.Amount != issued.Forecast.Amount
            || !forecast.Dependencies.Complete || forecast.Dependencies.Changed(facts).Count != 0) return false;
        dispatched[forecast.Id] = (issued.Binding, forecast, facts.Tick);
        return true;
    }

    private CourseProjection WithPhysicalEffects(CourseProjection projection, long originTick, DecisionFactSnapshot? facts = null)
    {
        var scheduled = projection.Steps.SelectMany(step => step.Effects).Select(effect => effect.Id).ToHashSet();
        var executing = projection.Steps.Select(step => step.Id).ToHashSet();
        var knownCauses = completedCauses.Concat(executing).Concat(scheduled).Concat(dispatched.Keys).ToHashSet();
        var physical = dispatched.Where(pair => !scheduled.Contains(pair.Key)).ToArray();
        var censored = physical.Where(pair =>
        {
            var effect = pair.Value.Forecast;
            double rebasedLatest = effect.LatestTick + pair.Value.OriginTick - originTick;
            return effect.Evidence == EstimateStatus.Unresolved || rebasedLatest < projection.ProjectionStartTick
                || facts != null && (!effect.Dependencies.Complete || effect.Dependencies.Changed(facts).Count != 0);
        }).Select(pair => pair.Key).ToHashSet();
        foreach (var pair in physical)
            if (pair.Value.Forecast.Parents.Any(censored.Contains)) censored.Add(pair.Key);
        var outstanding = physical.Select(pair =>
        {
            var effect = pair.Value.Forecast;
            double shift = pair.Value.OriginTick - originTick;
            double Rebase(double time) => Math.Max(0, time + shift);
            var parents = effect.Parents.Where(knownCauses.Contains).ToArray();
            bool brokenCausality = parents.Length != effect.Parents.Count;
            if (brokenCausality)
                throw new InvalidOperationException("Outstanding physical effects require a live causal parent or terminal censoring.");
            var evidence = censored.Contains(effect.Id) ? EstimateStatus.Unresolved : effect.Evidence;
            return new PredictedEffect(effect.Id, effect.Need, Math.Max(0, effect.Amount - Effects.Confirmed(effect.Id)),
                Rebase(effect.NominalTick), Rebase(effect.EarliestTick), Rebase(effect.LatestTick), evidence,
                parents.Append(pair.Value.Binding).Distinct(), evidence == EstimateStatus.Unresolved ? Array.Empty<EffectDelta>() : effect.Delta,
                effect.Dependencies);
        }).ToArray();
        var referenced = projection.Steps.SelectMany(step => step.Parents)
            .Concat(projection.Steps.SelectMany(step => step.Effects).SelectMany(effect => effect.Parents))
            .Concat(outstanding.SelectMany(effect => effect.Parents)).ToHashSet();
        return new(projection.Steps, projection.Harm, projection.Companionship, projection.ReunionTick,
            projection.ReunionProven, projection.TailNominal, projection.TailUnresolved, outstanding,
            completedCauses.Where(id => referenced.Contains(id) && !executing.Contains(id)), projection.ProjectionStartTick,
            projection.ConsequenceDependencies);
    }

    private void ReplaceObservedProjection(CourseProjection projection)
    {
        var dependencies = CourseDependencyIndex.Build(projection);
        Current = Current! with { Projection = projection };
        Dependencies = dependencies;
        PruneCompletedCauses(projection);
    }

    private void PruneCompletedCauses(CourseProjection projection)
    {
        var needed = projection.CompletedCauses.ToHashSet();
        needed.UnionWith(dispatched.Values.Select(value => value.Binding));
        completedCauses.RemoveWhere(id => !needed.Contains(id));
    }

    /// <summary>The observation owner supplies only an issued-ID floor whose dispatches
    /// have all completed; an in-flight lower ID prevents advancing this floor.</summary>
    public void RetireReceiptsThrough(long completedIssuedWatermark)
    {
        if (completedIssuedWatermark < retiredReceipt) throw new ArgumentOutOfRangeException(nameof(completedIssuedWatermark));
        retiredReceipt = completedIssuedWatermark;
        executionReceipts.RemoveWhere(id => id <= retiredReceipt);
        var live = dispatched.Keys.ToHashSet();
        if (Current is { } current) live.UnionWith(current.Projection.AllEffects.Select(effect => effect.Id));
        Effects.RetireThroughReceipt(retiredReceipt, live);
    }

    public bool Consider(CourseProjection proposal, CourseComparisonEpisode episode, DecisionFactSnapshot facts,
        BindingValidation currentValidation, Func<StepBinding, BindingValidation> validateProposed)
    {
        ObserveEpoch(facts.WorldEpoch);
        if (episode.WorldEpoch != worldEpoch) throw new InvalidOperationException("Comparison episode belongs to a different world.");
        if (Repair.Pending != 0) return false;
        proposal = WithPhysicalEffects(proposal, facts.Tick, facts);
        if (!ReadyToPublish(proposal, facts, validateProposed)) return false;
        if (Current == null)
        {
            Publish(proposal, episode, facts, "cold-start-legal-prefix");
            return true;
        }
        if (Current.SourceSnapshot != facts.Id)
            throw new InvalidOperationException("Reproject the remaining incumbent from this observation before comparing futures.");
        LastComparison = CompareCourseOutcomes.Compare(facts.Id, episode, Current.Projection, proposal,
            Boundary, currentValidation.CanUse);
        if (!LastComparison.Replace) return false;
        Publish(proposal, episode, facts, LastComparison.Reason);
        return true;
    }

    /// <summary>Repair can publish a changed tail only with the same still-valid prefix.
    /// Replacing the current use must go through Consider, including its switching costs.</summary>
    public bool PublishTail(CourseProjection repaired, CourseComparisonEpisode episode, DecisionFactSnapshot facts,
        Func<StepBinding, BindingValidation> validate)
    {
        if (Current?.Projection.Prefix is not { } held || repaired.Prefix is not { } next
            || !SameUse(held, next) || !validate(next).CanUse)
            return false;
        if (Current.SourceSnapshot != facts.Id || episode.WorldEpoch != worldEpoch) return false;
        repaired = WithPhysicalEffects(repaired, facts.Tick, facts);
        if (Repair.Pending != 0 || !ReadyToPublish(repaired, facts, validate)) return false;
        if (Repair.Dirty.Count == 0 && Repair.Pending == 0 && CompareCourseOutcomes.NominalOrder(
            CompareCourseOutcomes.Evaluate(repaired, episode), CompareCourseOutcomes.Evaluate(Current.Projection, episode), episode.Encounter) <= 0)
            return false;
        Publish(repaired, episode, facts, "dependent-tail-repaired");
        return true;
    }

    private void Publish(CourseProjection projection, CourseComparisonEpisode episode, DecisionFactSnapshot facts, string reason)
    {
        var old = Current;
        bool samePurposes = old != null && old.Projection.Steps.Select(s => (s.Opportunity.Domain, s.Opportunity.Purpose))
            .ToHashSet().SetEquals(projection.Steps.Select(s => (s.Opportunity.Domain, s.Opportunity.Purpose)));
        long id = samePurposes ? old!.Id : CourseIdentity.Next();
        var next = new RetainedCourse(id, samePurposes ? old!.Revision + 1 : 1, episode.Id,
            samePurposes ? old!.PredecessorId : old?.Id ?? 0, projection, reason, facts.Id, facts.ReceiptWatermark);
        var dependencies = CourseDependencyIndex.Build(projection);
        Dependencies = dependencies;
        Current = next;
        currentOriginTick = facts.Tick;
        PruneCompletedCauses(projection);
        Repair.Published();
        Boundary = ExecutionBoundary.None;
        ReleaseReason = "";
    }

    public void InvalidNextUse(string reason)
    {
        Boundary = ExecutionBoundary.Invalidated;
        if (Current?.Projection.Prefix is { } step) Repair.Invalidate(step.Id, reason);
    }

    /// <summary>Observation replaces the predicted future with its remaining work, using
    /// realised state and receipt allocations. This does not select a rival or create a
    /// boundary: travelling through a waypoint cannot license switching purposes.</summary>
    public bool ObserveRemaining(CourseProjection remaining, DecisionFactSnapshot facts,
        Func<StepBinding, BindingValidation> validate)
    {
        if (Current == null) throw new InvalidOperationException("There is no retained course to reproject.");
        if (facts.WorldEpoch != worldEpoch) throw new InvalidOperationException("A remaining course cannot cross world epochs.");
        if (Repair.Pending != 0) throw new InvalidOperationException("Finish dependency invalidation before replacing its graph.");
        var held = Current.Projection.Steps;
        if (remaining.Steps.Count != held.Count || remaining.Steps.Where((step, i) => !SameUse(held[i], step)).Any())
            throw new ArgumentException("Re-observation cannot replace, remove or reorder an accepted use; publish that change through comparison.", nameof(remaining));
        if (remaining.Steps.Any(step => step.WorldEpoch != facts.WorldEpoch || step.SnapshotId != facts.Id))
            throw new ArgumentException("Every remaining binding must name the observation used to reproject it.", nameof(remaining));
        remaining = WithPhysicalEffects(remaining, facts.Tick, facts);
        if (!ReadyToPublish(remaining, facts, validate, out long failedNode))
        {
            if (failedNode == remaining.Prefix?.Id) InvalidNextUse("remaining-prefix-unresolved");
            else Repair.Invalidate(failedNode, "remaining-dependent-forecast-unresolved");
            return false;
        }
        var dependencies = CourseDependencyIndex.Build(remaining);
        bool repaired = Repair.Dirty.Count > 0;
        Current = Current with { Projection = remaining, SourceSnapshot = facts.Id, ReceiptWatermark = facts.ReceiptWatermark,
            Revision = Current.Revision + (repaired ? 1 : 0), Reason = repaired ? "observation-repaired-forecast" : Current.Reason };
        currentOriginTick = facts.Tick;
        Dependencies = dependencies;
        PruneCompletedCauses(remaining);
        if (repaired)
        {
            Repair.Published();
            if (Boundary == ExecutionBoundary.Invalidated) Boundary = ExecutionBoundary.None;
        }
        return true;
    }

    private static bool SameUse(StepBinding held, StepBinding next) => held.Id == next.Id
        && held.Opportunity == next.Opportunity && held.Method == next.Method && held.Tool == next.Tool && held.Pose == next.Pose
        && held.NativeUseId == next.NativeUseId;

    private static bool ReadyToPublish(CourseProjection proposal, DecisionFactSnapshot facts,
        Func<StepBinding, BindingValidation> validate)
        => ReadyToPublish(proposal, facts, validate, out _);

    private static bool ReadyToPublish(CourseProjection proposal, DecisionFactSnapshot facts,
        Func<StepBinding, BindingValidation> validate, out long failedNode)
    {
        failedNode = proposal.Prefix?.Id ?? 0;
        if (proposal.Prefix is { } prefix && !validate(prefix).CanUse) return false;
        bool costsAccountedFor = proposal.ConsequenceDependencies.Complete
            || (proposal.TailUnresolved && proposal.ConsequenceDependencies.Recorded);
        if (!costsAccountedFor || proposal.ConsequenceDependencies.Changed(facts).Count != 0)
        { failedNode = proposal.ConsequenceId; return false; }
        foreach (var effect in proposal.OutstandingEffects)
            if (effect.Evidence != EstimateStatus.Unresolved
                && (!effect.Dependencies.Complete || effect.Dependencies.Changed(facts).Count != 0))
            { failedNode = effect.Id; return false; }
        var capacities = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var step in proposal.Steps)
        {
            failedNode = step.Id;
            if (step.WorldEpoch != facts.WorldEpoch || step.SnapshotId != facts.Id || !step.SufficientlyModelled
                || step.Dependencies.Changed(facts).Count != 0) return false;
            if (step.Effects.Any(effect => !effect.Dependencies.Complete || effect.Dependencies.Changed(facts).Count != 0)) return false;
            foreach (var phase in step.Resources.Where(phase => phase.Resource is CourseResource.Mana or CourseResource.Cargo))
            {
                var key = new FactKey("capacity", phase.CapacityKey);
                if (!facts.TryRead(key, out var capacity) || capacity.Evidence != FactEvidence.Observed
                    || !step.Dependencies.Reads.Any(read => read.Key == key && read.Digest == capacity.Digest)) return false;
                capacities[phase.CapacityKey] = capacity.Value.Amount;
            }
        }
        var projected = new ProjectedCourseState(default, outstandingEffects: proposal.OutstandingEffects,
            startTick: proposal.ProjectionStartTick, completedCauses: proposal.CompletedCauses);
        foreach (var step in proposal.Steps)
        {
            failedNode = step.Id;
            if (!projected.TryApply(step, capacities, out _)) return false;
        }
        // Causal integrity is checked before publication, after identifying ordinary
        // resource or timing refusal at its owning step rather than revoking the prefix.
        _ = CourseDependencyIndex.Build(proposal);
        failedNode = 0;
        return true;
    }

    public void Release(string reason)
    {
        Current = null;
        var physical = WithPhysicalEffects(new(Array.Empty<StepBinding>(), Array.Empty<PredictedHarm>(),
            Array.Empty<CompanionshipInterval>(), 0, false), currentOriginTick);
        Dependencies = CourseDependencyIndex.Build(physical); Repair.Published();
        PruneCompletedCauses(physical);
        Boundary = ExecutionBoundary.Invalidated; ReleaseReason = reason;
    }
}
