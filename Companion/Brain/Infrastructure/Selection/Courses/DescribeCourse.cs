#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

public enum EstimateStatus { NativeBound, ModelBound, Nominal, Unresolved }
public readonly record struct OutcomeEstimate(double Nominal, double Lower, double Upper, EstimateStatus Status)
{
    public bool HasJustifiedBounds => Status is EstimateStatus.NativeBound or EstimateStatus.ModelBound
        && !double.IsNaN(Lower) && !double.IsNaN(Upper) && Lower <= Nominal && Nominal <= Upper;
    public static OutcomeEstimate Exact(double value) => new(value, value, value, EstimateStatus.NativeBound);
    public static OutcomeEstimate Unknown(double nominal) => new(nominal, double.NegativeInfinity, double.PositiveInfinity, EstimateStatus.Unresolved);
}
public enum ExecutionBoundary { None, NativeEffect, NativeUseComplete, WeaponReady, BindingExhausted, Invalidated, ColdStart }
public enum CourseResource { Body, Hand, Mana, Cargo }
public readonly record struct ResourcePhase(CourseResource Resource, double StartTick, double EndTick,
    double Amount, string CapacityKey = "");
public enum HarmActor { Companion, Player }
public readonly record struct PredictionRange(double Lower, double Upper)
{
    public bool Contains(double value) => double.IsFinite(Lower) && double.IsFinite(Upper)
        && Lower >= 0 && Lower <= value && value <= Upper;
}
public readonly record struct PredictedHarm(HarmActor Actor, double Damage, double CurrentLife, double Tick,
    EstimateStatus Evidence, PredictionRange? DamageRange = null, PredictionRange? TimeRange = null);
public readonly record struct CompanionshipInterval(double StartTick, double EndTick, double GapAtStart, double GapAtEnd,
    EstimateStatus Evidence, PredictionRange? StartGapRange = null, PredictionRange? EndGapRange = null);
public readonly record struct EffectDelta(FactKey Key, FactValue Value);

public sealed record PredictedEffect
{
    public PredictedEffect(long id, NeedKey need, double amount, double nominalTick, double earliestTick,
        double latestTick, EstimateStatus evidence, IEnumerable<long> parents, IEnumerable<EffectDelta> delta,
        DependencyManifest dependencies)
    {
        Id = id; Need = need; Amount = amount; NominalTick = nominalTick; EarliestTick = earliestTick;
        LatestTick = latestTick; Evidence = evidence; Parents = Array.AsReadOnly(parents.ToArray());
        Delta = Array.AsReadOnly(delta.ToArray()); Dependencies = dependencies;
        if (!double.IsFinite(amount) || amount < 0 || !double.IsFinite(nominalTick) || nominalTick < 0
            || earliestTick < 0 || double.IsNaN(earliestTick) || double.IsNaN(latestTick)
            || earliestTick > nominalTick || latestTick < nominalTick)
            throw new ArgumentException("Effect amount and time interval must be valid native units.");
    }
    public long Id { get; }
    public NeedKey Need { get; }
    public double Amount { get; }
    public double NominalTick { get; }
    public double EarliestTick { get; }
    public double LatestTick { get; }
    public EstimateStatus Evidence { get; }
    public IReadOnlyList<long> Parents { get; }
    public IReadOnlyList<EffectDelta> Delta { get; }
    public DependencyManifest Dependencies { get; }
}

public sealed record StepBinding
{
    public StepBinding(long id, OpportunityKey opportunity, string method, CoursePoint pose, string tool,
        long snapshotId, long worldEpoch, double travelTicks, double useTicks, double cancellationTicks,
        IEnumerable<ResourcePhase> resources, IEnumerable<PredictedEffect> effects,
        IEnumerable<long> parents, DependencyManifest dependencies, bool useProven, CoursePoint arrivalVelocity = default,
        string nativeUseId = "")
    {
        Id = id; Opportunity = opportunity; Method = method; Pose = pose; Tool = tool;
        SnapshotId = snapshotId; WorldEpoch = worldEpoch; TravelTicks = travelTicks; UseTicks = useTicks;
        CancellationTicks = cancellationTicks; Resources = Array.AsReadOnly(resources.ToArray());
        Effects = Array.AsReadOnly(effects.ToArray()); Parents = Array.AsReadOnly(parents.ToArray());
        Dependencies = dependencies; UseProven = useProven;
        ArrivalVelocity = arrivalVelocity;
        NativeUseId = nativeUseId;
    }
    public long Id { get; }
    public OpportunityKey Opportunity { get; }
    public string Method { get; }
    public CoursePoint Pose { get; }
    public string Tool { get; }
    public long SnapshotId { get; }
    public long WorldEpoch { get; }
    public double TravelTicks { get; }
    public double UseTicks { get; }
    public double CancellationTicks { get; }
    public IReadOnlyList<ResourcePhase> Resources { get; }
    public IReadOnlyList<PredictedEffect> Effects { get; }
    public IReadOnlyList<long> Parents { get; }
    public DependencyManifest Dependencies { get; }
    public bool UseProven { get; }
    public CoursePoint ArrivalVelocity { get; }
    /// <summary>Domain-owned immutable trigger selected during binding.  Opportunity identity stays
    /// stable across repricing; dispatch must still receive the one use it accepted.</summary>
    public string NativeUseId { get; }
    public bool SufficientlyModelled => UseProven && Dependencies.Complete
        && double.IsFinite(TravelTicks) && TravelTicks >= 0 && double.IsFinite(UseTicks) && UseTicks >= 0
        && double.IsFinite(CancellationTicks) && CancellationTicks >= 0
        && double.IsFinite(ArrivalVelocity.X) && double.IsFinite(ArrivalVelocity.Y)
        && Resources.All(r => double.IsFinite(r.Amount) && r.Amount >= 0 && double.IsFinite(r.StartTick)
            && double.IsFinite(r.EndTick) && r.StartTick >= 0 && r.EndTick >= r.StartTick)
        && Effects.All(e => e.Evidence != EstimateStatus.Unresolved && e.Dependencies.Complete);
}
public readonly record struct BindingValidation(OpportunityAdmission Admission, string Reason, bool RequiresRepair)
{
    public bool CanUse => Admission == OpportunityAdmission.KnownUsable && !RequiresRepair;
}
public sealed record BindingResult(StepBinding? Binding, OpportunityAdmission Admission, string Reason, bool Pending,
    IReadOnlyList<CourseTravelRequest>? RequiredTravel = null);
public readonly record struct ExecutionReceipt(long BindingId, long AttemptId, long ObservationOrdinal,
    string Requested, string Granted, string NativeUse, long NativeReceiptId, ExecutionBoundary Boundary, string Outcome);

public sealed record CourseProjection
{
    public CourseProjection(IEnumerable<StepBinding> steps, IEnumerable<PredictedHarm> harm,
        IEnumerable<CompanionshipInterval> companionship, double reunionTick, bool reunionProven,
        double tailNominal = 0, bool tailUnresolved = false,
        IEnumerable<PredictedEffect>? outstandingEffects = null, IEnumerable<long>? completedCauses = null,
        double projectionStartTick = 0)
    {
        Steps = Array.AsReadOnly(steps.ToArray()); Harm = Array.AsReadOnly(harm.ToArray());
        Companionship = Array.AsReadOnly(companionship.ToArray()); ReunionTick = reunionTick;
        ReunionProven = reunionProven; TailNominal = tailNominal; TailUnresolved = tailUnresolved;
        OutstandingEffects = Array.AsReadOnly((outstandingEffects ?? Array.Empty<PredictedEffect>()).ToArray());
        CompletedCauses = Array.AsReadOnly((completedCauses ?? Array.Empty<long>()).ToArray());
        if (!double.IsFinite(projectionStartTick) || projectionStartTick < 0)
            throw new ArgumentOutOfRangeException(nameof(projectionStartTick));
        ProjectionStartTick = projectionStartTick;
    }
    public IReadOnlyList<StepBinding> Steps { get; }
    public IReadOnlyList<PredictedHarm> Harm { get; }
    public IReadOnlyList<CompanionshipInterval> Companionship { get; }
    public double ReunionTick { get; }
    public bool ReunionProven { get; }
    public double TailNominal { get; }
    public bool TailUnresolved { get; }
    /// <summary>Native dispatch outlives selection: these effects no longer own an executable
    /// binding, but remain causal inputs until the native object resolves or expires.</summary>
    public IReadOnlyList<PredictedEffect> OutstandingEffects { get; }
    public IReadOnlyList<long> CompletedCauses { get; }
    public double ProjectionStartTick { get; }
    public IEnumerable<PredictedEffect> AllEffects => OutstandingEffects.Concat(Steps.SelectMany(s => s.Effects));
    public StepBinding? Prefix => Steps.Count > 0 ? Steps[0] : null;
}

public sealed record RetainedCourse(long Id, long Revision, long Episode, long PredecessorId,
    CourseProjection Projection, string Reason, long SourceSnapshot, long ReceiptWatermark);

/// <summary>Process lifetime identifiers cannot collide when an NPC gets a new Brain.</summary>
public static class CourseIdentity
{
    private static long next;
    public static long Next() => Interlocked.Increment(ref next);
}
