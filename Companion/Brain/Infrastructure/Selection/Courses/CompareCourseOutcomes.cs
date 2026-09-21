#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>The immutable census owns normalisation. Discovering another method or
/// splitting a target never changes another need's denominator or the traversal scale.</summary>
public sealed class CourseComparisonEpisode
{
    private readonly Dictionary<NeedKey, UsefulNeed> needs;
    public const string Policy = "discounted-normalised-effects-v1";
    /// <param name="protectionUrgency">How badly the player needs defending, on the threat sense's own
    /// 0..1 scale. It is the one route by which the player's danger reaches optional work, and it is the
    /// same quantity the family chooser used for exactly this, carried over rather than reinvented.</param>
    public CourseComparisonEpisode(long id, long worldEpoch, double timeScale, IEnumerable<UsefulNeed> needs,
        bool censusComplete, bool encounter, string relevanceFingerprint, double protectionUrgency = 0)
    {
        if (!double.IsFinite(timeScale) || timeScale < 1) throw new ArgumentOutOfRangeException(nameof(timeScale));
        if (!double.IsFinite(protectionUrgency) || protectionUrgency < 0) throw new ArgumentOutOfRangeException(nameof(protectionUrgency));
        Id = id; WorldEpoch = worldEpoch; TimeScale = timeScale; CensusComplete = censusComplete;
        Encounter = encounter; RelevanceFingerprint = relevanceFingerprint;
        ProtectionUrgency = Math.Clamp(protectionUrgency, 0, 1);
        this.needs = needs.ToDictionary(n => n.Key);
        foreach (var need in this.needs.Values) _ = need.Worth(0);
    }
    public long Id { get; }
    public long WorldEpoch { get; }
    public double TimeScale { get; }
    public bool CensusComplete { get; }
    public bool Encounter { get; }
    public string RelevanceFingerprint { get; }

    /// <summary>How badly the player needs defending, 0 to 1, from the threat sense's own urgency rule.</summary>
    public double ProtectionUrgency { get; }

    /// <summary>
    /// What a need is worth relative to its own census while the player is in danger, which is the one
    /// place the player's danger reaches the comparison at all.
    ///
    /// Before this the course had no term for it: <see cref="NeedKind"/> runs Illumination, Loot,
    /// NativeWork, HostileLife and Container, and nothing in any of them says the player is being hurt.
    /// So killing a zombie standing on a wounded player was worth exactly what killing one across the
    /// room was worth, and on a scene with a threat on a hurt player the companion mined for all 120
    /// ticks. The family chooser had this and the course did not inherit it.
    ///
    /// The shape is the chooser's own rather than a new invention: danger suppresses *work* rather than
    /// inflating combat, so a non-combat need pays <c>1 − urgency</c> and a hostile's life pays in full.
    /// Written the other way round — a bonus on combat — the same ordering would need a magnitude nobody
    /// could derive, and every tuning of it would move work's value too.
    ///
    /// Encounter intensity is deliberately *not* folded in here yet, though the chooser's rule was
    /// <c>1 − max(urgency, intensity)</c>. This episode carries the encounter as a boolean, and treating
    /// that as an intensity of one would zero every optional need for the whole of any recognised event —
    /// stronger than the rule it would be imitating, because inferred pressure ramps rather than
    /// arriving at full strength. Carrying the intensity through is its own change and wants measuring on
    /// a scene with a blood moon in it.
    /// </summary>
    public double RelevanceFor(NeedKind kind)
        => kind == NeedKind.HostileLife ? 1 : Math.Max(0, 1 - ProtectionUrgency);
    public IEnumerable<UsefulNeed> Needs => needs.Values;
    public bool TryNeed(NeedKey key, out UsefulNeed need) => needs.TryGetValue(key, out need!);

    public static double TimeScaleFor(double baseWidth, double baseHeight, double cruiseSpeed, double shortestLocalCycle)
    {
        if (!double.IsFinite(baseWidth) || !double.IsFinite(baseHeight) || baseWidth < 0 || baseHeight < 0
            || !double.IsFinite(cruiseSpeed) || cruiseSpeed < 0) throw new ArgumentOutOfRangeException(nameof(cruiseSpeed));
        if (cruiseSpeed > 0) return Math.Max(1, Math.Sqrt(baseWidth * baseWidth + baseHeight * baseHeight) / cruiseSpeed);
        // With no travel or positive interaction capability there is no comparison to make.
        return double.IsFinite(shortestLocalCycle) && shortestLocalCycle > 0 ? Math.Max(1, shortestLocalCycle) : 1;
    }
}

public sealed record CourseValue(OutcomeEstimate Total, double UsefulEffects, double Harm,
    double Companionship, double CompanionHarm, double ReunionTick, bool ReunionProven,
    IReadOnlyList<string> Unknowns, OutcomeEstimate SelfHarm);
public sealed record CourseComparison(long SnapshotId, long EpisodeId, long IncumbentBinding,
    long ChallengerBinding, CourseValue Incumbent, CourseValue Challenger, bool Replace, string Reason);

public static class CompareCourseOutcomes
{
    public static double Discount(double ticks, double scale) => Math.Exp(-ticks / scale);

    /// <summary>Exact integral of a linearly varying gap over one forecast interval.
    /// Gap is dimensionless; division by T keeps it comparable to effect/life fractions.</summary>
    public static double GapIntegral(double start, double end, double gapStart, double gapEnd, double scale)
    {
        if (!double.IsFinite(start) || start < 0 || !double.IsFinite(end) || end < start
            || !double.IsFinite(gapStart) || gapStart < 0 || !double.IsFinite(gapEnd) || gapEnd < 0)
            throw new ArgumentException("Companionship needs a finite nonnegative interval and gap.");
        if (end == start) return 0;
        double duration = end - start;
        double slope = (gapEnd - gapStart) / duration;
        double decay = Discount(duration, scale);
        return Discount(start, scale) * (gapStart * (1 - decay)
            + slope * (scale * (1 - decay) - duration * decay));
    }

    public static CourseValue Evaluate(CourseProjection course, CourseComparisonEpisode episode)
    {
        double useful = 0, lower = 0, upper = 0, harm = 0, selfHarm = 0, gap = 0;
        double harmLower = 0, harmUpper = 0, gapLower = 0, gapUpper = 0;
        double selfLower = 0, selfUpper = 0;
        bool selfBounded = true;
        var unknowns = new List<string>();
        if (!episode.CensusComplete) unknowns.Add("census-incomplete");
        var remaining = episode.Needs.ToDictionary(n => n.Key, n => n.RemainingAmount);
        var effectIds = new HashSet<long>();
        double timeScale = episode.TimeScale;
        // Earliest physical claim gets the units. Later overlapping claims receive only what
        // remains, independent of which domain enumerated them or how many targets split it.
        double origin = course.ProjectionStartTick;
        foreach (var effect in course.AllEffects.OrderBy(e => e.NominalTick).ThenBy(e => e.Id))
        {
            if (!effectIds.Add(effect.Id)) continue;
            if (!episode.TryNeed(effect.Need, out var need))
            { unknowns.Add("effect-outside-census:" + effect.Need); continue; }
            double amount = Math.Min(effect.Amount, remaining[effect.Need]);
            remaining[effect.Need] -= amount;
            double worth = need.Worth(amount) * episode.RelevanceFor(effect.Need.Kind);
            useful += worth * Discount(Math.Max(0, effect.NominalTick - origin), timeScale);
            if (effect.Evidence is EstimateStatus.NativeBound or EstimateStatus.ModelBound)
            {
                lower += worth * Discount(Math.Max(0, effect.LatestTick - origin), timeScale);
                upper += worth * Discount(Math.Max(0, effect.EarliestTick - origin), timeScale);
            }
            else unknowns.Add("effect-uncertain:" + effect.Id);
        }
        foreach (var hit in course.Harm)
        {
            if (hit.Tick < origin) continue;
            if (!double.IsFinite(hit.Damage) || hit.Damage < 0 || !double.IsFinite(hit.CurrentLife)
                || hit.CurrentLife <= 0 || !double.IsFinite(hit.Tick) || hit.Tick < 0)
                throw new ArgumentException("Harm must name positive observed life and finite native damage/time.");
            double cost = hit.Damage / hit.CurrentLife * Discount(hit.Tick - origin, timeScale);
            harm += cost;
            if (!Enum.IsDefined(hit.Actor)) throw new ArgumentException("Harm must identify the player or companion.");
            if (hit.Actor == HarmActor.Companion) selfHarm += cost;
            double beforeLower = harmLower, beforeUpper = harmUpper;
            if (hit.Evidence == EstimateStatus.NativeBound && hit.DamageRange == null && hit.TimeRange == null)
            { harmLower += cost; harmUpper += cost; }
            else if (hit.Evidence is EstimateStatus.ModelBound or EstimateStatus.NativeBound
                && hit.DamageRange is { } damage && damage.Contains(hit.Damage)
                && hit.TimeRange is { } time && time.Contains(hit.Tick))
            {
                harmLower += damage.Lower / hit.CurrentLife * Discount(Math.Max(0, time.Upper - origin), timeScale);
                harmUpper += damage.Upper / hit.CurrentLife * Discount(Math.Max(0, time.Lower - origin), timeScale);
            }
            else
            {
                unknowns.Add("harm-uncertain:" + hit.Actor);
                if (hit.Actor == HarmActor.Companion) selfBounded = false;
            }
            if (hit.Actor == HarmActor.Companion)
            { selfLower += harmLower - beforeLower; selfUpper += harmUpper - beforeUpper; }
        }
        double previousEnd = 0;
        foreach (var interval in course.Companionship.OrderBy(i => i.StartTick))
        {
            if (interval.StartTick < previousEnd) throw new ArgumentException("Companionship intervals overlap.");
            previousEnd = interval.EndTick;
            if (interval.EndTick <= origin) continue;
            double fraction = interval.StartTick >= origin ? 0 : (origin - interval.StartTick) / (interval.EndTick - interval.StartTick);
            double start = Math.Max(0, interval.StartTick - origin), end = interval.EndTick - origin;
            double Interpolate(double left, double right) => left + fraction * (right - left);
            double cost = GapIntegral(start, end, Interpolate(interval.GapAtStart, interval.GapAtEnd), interval.GapAtEnd, timeScale);
            gap += cost;
            if (interval.Evidence == EstimateStatus.NativeBound && interval.StartGapRange == null && interval.EndGapRange == null)
            { gapLower += cost; gapUpper += cost; }
            else if (interval.Evidence is EstimateStatus.ModelBound or EstimateStatus.NativeBound
                && interval.StartGapRange is { } startGap && startGap.Contains(interval.GapAtStart)
                && interval.EndGapRange is { } endGap && endGap.Contains(interval.GapAtEnd))
            {
                gapLower += GapIntegral(start, end, Interpolate(startGap.Lower, endGap.Lower), endGap.Lower, timeScale);
                gapUpper += GapIntegral(start, end, Interpolate(startGap.Upper, endGap.Upper), endGap.Upper, timeScale);
            }
            else
                unknowns.Add("companionship-uncertain");
        }
        if (!course.ReunionProven) unknowns.Add("reunion-unresolved");
        if (course.TailUnresolved)
        {
            unknowns.Add("tail-unresolved");
            // An untyped unknown future can contain companion harm. Encounter ordering
            // cannot treat an empty finite hit list as proof of zero future self-harm.
            selfBounded = false;
        }
        if (course.TailNominal != 0) unknowns.Add("tail-has-no-bounds");
        if (!double.IsFinite(course.TailNominal)) throw new ArgumentException("A nominal tail must be finite, with unknown evidence separate.");
        double total = useful - harm - gap + course.TailNominal;
        var estimate = unknowns.Count == 0
            ? new OutcomeEstimate(total, lower - harmUpper - gapUpper,
                upper - harmLower - gapLower, EstimateStatus.ModelBound)
            : OutcomeEstimate.Unknown(total);
        return new(estimate, useful, harm, gap, selfHarm, course.ReunionTick,
            course.ReunionProven, Array.AsReadOnly(unknowns.ToArray()), selfBounded
                ? new(selfHarm, selfLower, selfUpper, EstimateStatus.ModelBound) : OutcomeEstimate.Unknown(selfHarm));
    }

    public static CourseComparison Compare(long snapshotId, CourseComparisonEpisode episode,
        CourseProjection incumbent, CourseProjection challenger, ExecutionBoundary boundary, bool incumbentValid)
    {
        var oldValue = Evaluate(incumbent, episode);
        var newValue = Evaluate(challenger, episode);
        bool executable = challenger.Prefix == null || challenger.Prefix.SufficientlyModelled;
        bool replace = false;
        string reason;
        if (!executable) reason = "challenger-prefix-unresolved";
        else if (!incumbentValid) { replace = true; reason = "required-repair"; }
        else if (boundary != ExecutionBoundary.None)
        {
            int order = NominalOrder(newValue, oldValue, episode.Encounter);
            replace = order > 0;
            reason = order == 0 ? "equal-future-retain" : replace ? "boundary-better-future" : "boundary-incumbent-better";
        }
        else
        {
            bool proven = newValue.Total.HasJustifiedBounds && oldValue.Total.HasJustifiedBounds
                && newValue.Total.Lower > oldValue.Total.Upper;
            if (episode.Encounter)
                proven = proven && newValue.SelfHarm.HasJustifiedBounds && oldValue.SelfHarm.HasJustifiedBounds
                    && newValue.SelfHarm.Upper <= oldValue.SelfHarm.Lower;
            replace = proven;
            reason = proven ? "proven-better-future" : "valid-prefix-retained-uncertain-comparison";
        }
        return new(snapshotId, episode.Id, incumbent.Prefix?.Id ?? 0, challenger.Prefix?.Id ?? 0,
            oldValue, newValue, replace, reason);
    }

    public static int NominalOrder(CourseValue left, CourseValue right, bool encounter)
    {
        int order = encounter ? right.CompanionHarm.CompareTo(left.CompanionHarm) : 0;
        if (order == 0) order = left.Total.Nominal.CompareTo(right.Total.Nominal);
        if (order == 0) order = right.Harm.CompareTo(left.Harm);
        if (order == 0 && left.ReunionProven && right.ReunionProven)
            order = right.ReunionTick.CompareTo(left.ReunionTick);
        return order;
    }
}
