#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>A sparse branch: realised facts stay immutable, hypothetical changes retain
/// the effects that caused them. Copying a branch never copies or mutates the game world.</summary>
public sealed class ProjectedCourseState
{
    private readonly Dictionary<FactKey, List<(FactValue Value, long Effect, double At)>> overlay;
    private readonly Dictionary<NeedKey, double> claimed;
    private readonly HashSet<long> effects;
    private readonly Dictionary<long, StepBinding?> bindings;
    private readonly Dictionary<long, double> effectTicks;
    private readonly List<ResourcePhase> reservations;
    private readonly HashSet<long> readEffects = new();
    public ProjectedCourseState(CoursePoint pose, DecisionFactSnapshot? observed = null,
        IEnumerable<PredictedEffect>? outstandingEffects = null, double startTick = 0,
        IEnumerable<long>? completedCauses = null, CoursePoint velocity = default)
    {
        if (!double.IsFinite(startTick) || startTick < 0) throw new ArgumentOutOfRangeException(nameof(startTick));
        Pose = pose; Tick = startTick; overlay = new(); claimed = new(); effects = new(); bindings = new(); effectTicks = new(); reservations = new();
        Velocity = velocity;
        foreach (long causeId in completedCauses ?? Array.Empty<long>())
            if (!bindings.TryAdd(causeId, null)) throw new ArgumentException("Completed cause IDs must be unique.", nameof(completedCauses));
        foreach (var effect in outstandingEffects ?? Array.Empty<PredictedEffect>())
        {
            if (!effects.Add(effect.Id)) throw new ArgumentException("Outstanding effect IDs must be unique.", nameof(outstandingEffects));
            bool certified = effect.Evidence != EstimateStatus.Unresolved;
            if (effect.Parents.Any(parent => !effects.Contains(parent) && !bindings.ContainsKey(parent)))
                throw new ArgumentException("Outstanding effects require already known causal parents.", nameof(outstandingEffects));
            if (certified && effect.Parents.Any(parent => effectTicks.TryGetValue(parent, out double available) && available > effect.EarliestTick))
                throw new ArgumentException("Outstanding effects cannot precede a parent's guaranteed outcome.", nameof(outstandingEffects));
            double availableAt = GuaranteedAvailability(effect);
            effectTicks.Add(effect.Id, availableAt);
            if (!double.IsFinite(availableAt)) continue;
            foreach (var delta in effect.Delta)
            {
                if (!overlay.TryGetValue(delta.Key, out var changes)) overlay[delta.Key] = changes = new();
                changes.Add((delta.Value, effect.Id, availableAt));
            }
        }
    }
    public ProjectedCourseState(CoursePoint pose, double tick) : this(pose, startTick: tick) { }
    private ProjectedCourseState(ProjectedCourseState other)
    {
        Pose = other.Pose; Velocity = other.Velocity; Tick = other.Tick;
        overlay = other.overlay.ToDictionary(pair => pair.Key, pair => new List<(FactValue Value, long Effect, double At)>(pair.Value));
        claimed = new(other.claimed);
        effects = new(other.effects); bindings = new(other.bindings); effectTicks = new(other.effectTicks); reservations = new(other.reservations);
    }
    public CoursePoint Pose { get; private set; }
    public CoursePoint Velocity { get; private set; }
    public double Tick { get; private set; }
    public ProjectedCourseState Fork() => new(this);
    public IReadOnlyList<long> ReadEffects => Array.AsReadOnly(readEffects.OrderBy(x => x).ToArray());
    /// <summary>A binder owns one causal read journal. Forked branches start with no
    /// pending journal because a later binder must declare only the effects it read.</summary>
    public void BeginReadTracking() => readEffects.Clear();
    /// <summary>A nominal successor cannot be replaced by the old observation when a later
    /// use depends on the value it might have changed.</summary>
    public bool HasUnresolvedChange(FactKey key)
        => overlay.TryGetValue(key, out var changes) && changes.Any(change => !double.IsFinite(change.At));
    public FactValue Read(FactKey key, TrackedFactReader facts)
    {
        if (overlay.TryGetValue(key, out var changes))
        {
            // A released projectile has not already moved its target. Only effects due
            // by the hypothetical read time contribute to that successor state.
            var due = changes.Where(change => change.At <= Tick).OrderByDescending(change => change.At)
                .ThenByDescending(change => change.Effect).ToArray();
            if (due.Length > 0) { readEffects.Add(due[0].Effect); return due[0].Value; }
        }
        return facts.Read(key).Value;
    }
    public void AdvanceTo(double tick)
    {
        if (!double.IsFinite(tick) || tick < Tick) throw new ArgumentOutOfRangeException(nameof(tick));
        Tick = tick;
    }
    public double Remaining(UsefulNeed need) => Math.Max(0, need.RemainingAmount - claimed.GetValueOrDefault(need.Key));

    public bool TryApply(StepBinding binding, IReadOnlyDictionary<string, double> capacities, out string reason)
    {
        if (!binding.SufficientlyModelled) { reason = "binding-unresolved"; return false; }
        if (bindings.TryGetValue(binding.Id, out var previous))
        {
            if (ReferenceEquals(previous, binding)) { reason = "projected"; return true; }
            reason = "binding-id-reused"; return false;
        }
        if (binding.Resources.Any(phase => phase.StartTick < Tick || phase.EndTick < Tick))
        { reason = "resource-phase-in-past"; return false; }
        double bindingEnd = Tick + binding.TravelTicks + binding.UseTicks;
        if (!double.IsFinite(bindingEnd)) { reason = "binding-unresolved"; return false; }
        if (binding.Resources.Any(phase => phase.Resource is CourseResource.Body or CourseResource.Hand
            && (phase.StartTick < Tick || phase.EndTick > bindingEnd)))
        { reason = "resource-phase-outside-binding"; return false; }
        var combined = reservations.Concat(binding.Resources).ToArray();
        foreach (var phase in binding.Resources)
        {
            double capacity = phase.Resource is CourseResource.Body or CourseResource.Hand ? 1d
                : capacities.TryGetValue(phase.CapacityKey, out double available) ? available : double.NaN;
            if (!double.IsFinite(capacity) || capacity < 0) { reason = "capacity-unresolved:" + phase.CapacityKey; return false; }
            // Sweep every endpoint: testing only the new start misses an existing reservation
            // which begins halfway through this one. Endpoints are half-open native phases.
            var points = combined.Where(r => SameCapacity(r, phase))
                .SelectMany(r => new[] { r.StartTick, r.EndTick }).Distinct();
            foreach (double point in points)
            {
                double amount = combined.Where(r => SameCapacity(r, phase)
                    && r.StartTick <= point && point < r.EndTick).Sum(r => r.Amount);
                if (amount > capacity) { reason = "resource-conflict:" + phase.Resource; return false; }
            }
        }
        var pending = new HashSet<long>(effects); pending.UnionWith(bindings.Keys);
        if (binding.Parents.Any(parent => !pending.Contains(parent))) { reason = "binding-parent-unresolved"; return false; }
        if (binding.Parents.Any(parent => effectTicks.TryGetValue(parent, out double due) && due > Tick))
        { reason = "binding-parent-not-due"; return false; }
        pending.Add(binding.Id);
        var pendingTicks = new Dictionary<long, double>(effectTicks);
        foreach (var effect in binding.Effects.OrderBy(e => e.NominalTick).ThenBy(e => e.Id))
        {
            if (effect.EarliestTick < Tick) { reason = "effect-before-binding"; return false; }
            if (effect.Parents.Any(parent => !pending.Contains(parent))) { reason = "effect-parent-unresolved"; return false; }
            if (effect.Parents.Any(parent => pendingTicks.TryGetValue(parent, out double due) && due > effect.EarliestTick))
            { reason = "effect-parent-not-due"; return false; }
            pending.Add(effect.Id);
            pendingTicks[effect.Id] = GuaranteedAvailability(effect);
        }
        bindings.Add(binding.Id, binding);
        foreach (var effect in binding.Effects.OrderBy(e => e.NominalTick).ThenBy(e => e.Id))
        {
            if (!effects.Add(effect.Id)) continue;
            double availableAt = GuaranteedAvailability(effect);
            effectTicks[effect.Id] = availableAt;
            claimed[effect.Need] = claimed.GetValueOrDefault(effect.Need) + effect.Amount;
            foreach (var delta in effect.Delta)
            {
                if (!overlay.TryGetValue(delta.Key, out var changes)) overlay[delta.Key] = changes = new();
                changes.Add((delta.Value, effect.Id, availableAt));
            }
        }
        reservations.AddRange(binding.Resources);
        Pose = binding.Pose;
        Velocity = binding.ArrivalVelocity;
        Tick += binding.TravelTicks + binding.UseTicks;
        reason = "projected";
        return true;
    }

    private static bool SameCapacity(ResourcePhase left, ResourcePhase right)
        => left.Resource == right.Resource && (left.Resource is CourseResource.Body or CourseResource.Hand
            || StringComparer.Ordinal.Equals(left.CapacityKey, right.CapacityKey));

    private static double GuaranteedAvailability(PredictedEffect effect)
        => effect.Evidence is EstimateStatus.NativeBound or EstimateStatus.ModelBound && double.IsFinite(effect.LatestTick)
            ? effect.LatestTick : double.PositiveInfinity;
}

public readonly record struct EffectAllocation(long ReceiptId, long EffectId, double Amount);
public sealed record ReceiptAllocation(IReadOnlyList<EffectAllocation> Allocated, double Remainder, bool Duplicate);

/// <summary>Receipts allocate physical units once in effect-ID order. This ledger does not
/// infer attribution or create receipts from predicted effects.</summary>
public sealed class CourseEffectLedger
{
    private readonly HashSet<long> receipts = new();
    private readonly Dictionary<long, double> confirmed = new();
    private long retiredReceiptFloor;
    public ReceiptAllocation Apply(long receiptId, NeedKey need, double amount, IEnumerable<PredictedEffect> claims)
    {
        if (!double.IsFinite(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (receiptId <= retiredReceiptFloor) return new(Array.Empty<EffectAllocation>(), 0, true);
        if (!receipts.Add(receiptId)) return new(Array.Empty<EffectAllocation>(), 0, true);
        var result = new List<EffectAllocation>();
        double left = amount;
        foreach (var claim in claims.Where(c => c.Need == need).OrderBy(c => c.Id))
        {
            double accepted = Math.Min(left, Math.Max(0, claim.Amount - confirmed.GetValueOrDefault(claim.Id)));
            if (accepted == 0) continue;
            confirmed[claim.Id] = confirmed.GetValueOrDefault(claim.Id) + accepted;
            result.Add(new(receiptId, claim.Id, accepted));
            left -= accepted;
            if (left == 0) break;
        }
        return new(Array.AsReadOnly(result.ToArray()), left, false);
    }
    public double Confirmed(long effectId) => confirmed.GetValueOrDefault(effectId);
    /// <summary>Forgets receipts that the caller has proved complete while retaining a
    /// monotonic duplicate floor; confirmations survive only for effect identities still live.</summary>
    public void RetireThroughReceipt(long completedIssuedWatermark, IReadOnlySet<long> liveEffectIds)
    {
        if (completedIssuedWatermark < retiredReceiptFloor)
            throw new ArgumentOutOfRangeException(nameof(completedIssuedWatermark));
        receipts.RemoveWhere(receiptId => receiptId <= completedIssuedWatermark);
        foreach (long effectId in confirmed.Keys.Where(effectId => !liveEffectIds.Contains(effectId)).ToArray())
            confirmed.Remove(effectId);
        retiredReceiptFloor = completedIssuedWatermark;
    }
    public void Clear() { receipts.Clear(); confirmed.Clear(); retiredReceiptFloor = 0; }
}
