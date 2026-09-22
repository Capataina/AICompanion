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
/// <summary>
/// Every purpose this tree can mint, declared in one place so the set is a fact rather than a sweep.
///
/// **It exists because the pin that guarded it was a spelling.** `G01` used to sweep `Companion/` for the
/// literal `new OpportunityKey(` and check the purposes in the files it found; `chop` is minted as a
/// literal inside a `GatheringOpportunityFact` in a file that constructs no key at all, so it was in the
/// pin's reach only because a human had typed its name into the row's own list. A sentinel added a
/// seventh purpose the same way on 22 September 2026 and the row stayed green, and it stayed green again
/// for a key built as `new Infrastructure.Selection.Opportunities.OpportunityKey(…)` — the same call
/// spelled out. A purpose that reaches a course with no executor is the defect this whole pin exists for,
/// so the check is moved off the text and onto the one object every discovered opportunity is built
/// through: <see cref="Opportunity"/> refuses a purpose that is not declared here, and the row checks
/// this list against the executor map rather than checking the tree against a list.
/// </summary>
public static class OpportunityPurposes
{
    public const string Fire = "fire";
    public const string Mine = "mine";
    public const string Chop = "chop";
    public const string Collect = "collect";
    public const string Light = "light";
    public const string BreakPot = "break-pot";

    /// <summary>Declared rather than reflected over the constants, because a constant added without being
    /// added here is exactly the silence this type replaced, and reflection would have swallowed it.</summary>
    public static readonly IReadOnlyCollection<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Fire, Mine, Chop, Collect, Light, BreakPot };
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
        // The choke point every discovered opportunity passes through, and the one place a purpose
        // minted anywhere in the tree — at a key construction, or as a literal in a fact record another
        // file turns into a key — can be caught before it reaches a course. It throws rather than
        // refusing, for the same reason `ExecuteCourseBinding.ActivityFor` does: a refusal here would be
        // a domain that discovers work and silently never does any of it, which is the exact silence
        // this guard exists to end.
        if (!OpportunityPurposes.All.Contains(key.Purpose))
            throw new ArgumentOutOfRangeException(nameof(key),
                $"'{key.Purpose}' is not a declared opportunity purpose, so nothing can say whether an activity "
                    + $"performs it: declare it in {nameof(OpportunityPurposes)} and either give it an executor in "
                    + "ExecuteCourseBinding or write its exemption into the pin.");
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
