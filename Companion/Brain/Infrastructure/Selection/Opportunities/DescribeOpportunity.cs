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
        string reason, IEnumerable<UsefulNeed> needs, IEnumerable<string> methods, DependencyManifest dependencies)
    {
        Key = key; Revision = revision; Target = target; Admission = admission; Reason = reason;
        Needs = Array.AsReadOnly(needs.ToArray()); Methods = Array.AsReadOnly(methods.ToArray()); Dependencies = dependencies;
    }
    public OpportunityKey Key { get; }
    public long Revision { get; }
    public CoursePoint Target { get; }
    public OpportunityAdmission Admission { get; }
    public string Reason { get; }
    public IReadOnlyList<UsefulNeed> Needs { get; }
    public IReadOnlyList<string> Methods { get; }
    public DependencyManifest Dependencies { get; }
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
