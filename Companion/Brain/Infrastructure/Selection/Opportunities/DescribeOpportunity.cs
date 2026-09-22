#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

public readonly record struct OpportunityKey(string Domain, string Purpose, string Target, long Generation)
    : IComparable<OpportunityKey>
{
    public int CompareTo(OpportunityKey other)
    {
        int result = StringComparer.Ordinal.Compare(Domain, other.Domain);
        if (result == 0) result = StringComparer.Ordinal.Compare(Purpose, other.Purpose);
        if (result == 0) result = StringComparer.Ordinal.Compare(Target, other.Target);
        return result == 0 ? Generation.CompareTo(other.Generation) : result;
    }
    public override string ToString() => $"{Domain}/{Purpose}/{Target}@{Generation}";
}
public enum OpportunityAdmission { KnownUsable, KnownUnusable, Unresolved }
public enum NeedKind { Illumination, Loot, NativeWork, HostileLife, Container }
public readonly record struct NeedKey(NeedKind Kind, string Identity, long Generation = 0);
public readonly record struct CoursePoint(double X, double Y)
{
    public double DistanceTo(CoursePoint other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));
}
/// <summary>Physical amount, its frozen census denominator, and context relevance are
/// distinct. Splitting a target keeps these same original units and denominator.</summary>
public sealed record UsefulNeed(NeedKey Key, double RemainingAmount, double CensusAmount, double Relevance)
{
    public double Worth(double amount)
    {
        if (!double.IsFinite(amount) || amount < 0 || !double.IsFinite(RemainingAmount) || RemainingAmount < 0
            || !double.IsFinite(CensusAmount) || CensusAmount <= 0 || !double.IsFinite(Relevance) || Relevance < 0)
            throw new InvalidOperationException($"Invalid physical need or census for {Key}.");
        return Math.Min(amount, RemainingAmount) / CensusAmount * Relevance;
    }
}

public sealed record Opportunity
{
    public Opportunity(OpportunityKey key, long revision, CoursePoint target, OpportunityAdmission admission,
        string reason, IEnumerable<UsefulNeed> needs, IEnumerable<string> methods, DependencyManifest dependencies,
        FactKey admissionEvidence)
    {
        Key = key; Revision = revision; Target = target; Admission = admission; Reason = reason;
        Needs = Array.AsReadOnly(needs.ToArray()); Methods = Array.AsReadOnly(methods.ToArray()); Dependencies = dependencies;
        AdmissionEvidence = admissionEvidence;
    }
    public OpportunityKey Key { get; }
    public long Revision { get; }
    public CoursePoint Target { get; }
    public OpportunityAdmission Admission { get; }
    public string Reason { get; }
    public IReadOnlyList<UsefulNeed> Needs { get; }
    public IReadOnlyList<string> Methods { get; }
    public DependencyManifest Dependencies { get; }

    /// <summary>
    /// The one fact this admission rests on, which is the same fact this domain's binder reads first.
    ///
    /// It is required rather than optional because it is what stops the census and the binder
    /// disagreeing about one target inside one observation. Discovery keeps candidates in a bounded
    /// store across decisions, so an admission decided against an earlier snapshot outlives the world
    /// it was true in: measured 22 September 2026 on the tail scene, a drop taken out of the world at
    /// tick 150 was still served as <c>KnownUsable:observed-drop</c> at tick 499 while its
    /// <c>collect-target/item:10</c> fact had been absent from every snapshot in between, and every
    /// order built from it was refused <c>assistance-target-unresolved</c> — nine refusals a decision,
    /// for ever, with the funnel reporting three usable drops. The owner's play of 0.38.13 ended in
    /// exactly that state on both domains at once.
    ///
    /// <see cref="DiscoverOpportunities"/> re-reads this key against the current observation before it
    /// serves anything, so a candidate cannot claim an evidence the decision does not hold.
    ///
    /// The parameter is required rather than defaulted so that a source added later has to say what its
    /// admission rests on rather than inherit an exemption. A stand-in source with no facts behind it at
    /// all — the harness has several, driving fairness and eviction rather than any domain — passes
    /// <c>default</c>, which names no kind and is skipped by the sweep. That is the one exemption and it
    /// is visible at the call site, which a nullable parameter would not have been.
    /// </summary>
    public FactKey AdmissionEvidence { get; }
}

public readonly record struct OpportunityCoverage(string Source, long Epoch, long Examined, long Total,
    bool Exhausted, bool BudgetCut, string Bounds, long Evicted = 0);
public sealed record OpportunitySlice(IReadOnlyList<Opportunity> Examined, OpportunityCoverage Coverage);

public interface IOpportunitySource
{
    string Name { get; }
    OpportunitySlice Continue(DecisionFactSnapshot facts, DecisionWorkCursor cursor, DecisionWorkBudget budget);
}

public interface IOpportunityBinder
{
    string Domain { get; }
    BindingResult Bind(Opportunity opportunity, ProjectedCourseState state, TrackedFactReader facts,
        DecisionWorkCursor cursor, DecisionWorkBudget budget);
    BindingValidation ValidateNextUse(StepBinding binding, DecisionFactSnapshot facts);
}
