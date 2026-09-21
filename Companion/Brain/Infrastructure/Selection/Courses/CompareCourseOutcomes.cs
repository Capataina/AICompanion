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
    /// <param name="encounterIntensity">How strongly the world is about one thing, on the encounter
    /// sense's own 0..1 scale: one for any recognised boss or event, a ramp for inferred pressure, zero
    /// otherwise. It is carried as that number rather than as a boolean because the two are not the same
    /// claim — a boolean read as an intensity of one would zero every optional need the instant pressure
    /// was inferred at all, which is stronger than the rule it imitates, and the ramp is precisely what
    /// stops an ordinary busy cave reading like a blood moon.</param>
    public CourseComparisonEpisode(long id, long worldEpoch, double timeScale, IEnumerable<UsefulNeed> needs,
        bool censusComplete, double encounterIntensity, string relevanceFingerprint, double protectionUrgency = 0)
    {
        if (!double.IsFinite(timeScale) || timeScale < 1) throw new ArgumentOutOfRangeException(nameof(timeScale));
        if (!double.IsFinite(protectionUrgency) || protectionUrgency < 0) throw new ArgumentOutOfRangeException(nameof(protectionUrgency));
        if (!double.IsFinite(encounterIntensity) || encounterIntensity < 0 || encounterIntensity > 1)
            throw new ArgumentOutOfRangeException(nameof(encounterIntensity),
                "Encounter intensity is the sense's own 0..1 reading; anything else is an adapter defect rather than a value to clamp.");
        Id = id; WorldEpoch = worldEpoch; TimeScale = timeScale; CensusComplete = censusComplete;
        EncounterIntensity = encounterIntensity; RelevanceFingerprint = relevanceFingerprint;
        ProtectionUrgency = Math.Clamp(protectionUrgency, 0, 1);
        this.needs = needs.ToDictionary(n => n.Key);
        foreach (var need in this.needs.Values) _ = need.Worth(0);
    }
    public long Id { get; }
    public long WorldEpoch { get; }
    public double TimeScale { get; }
    public bool CensusComplete { get; }
    /// <summary>How strongly the world is about one thing, 0 to 1, from the encounter sense's own reading.</summary>
    public double EncounterIntensity { get; }
    /// <summary>Whether an encounter is on at all, which is the only thing the ordering and interruption
    /// rules need; any recognised source reads one, so a positive intensity is the encounter being on
    /// rather than a threshold anybody picked.</summary>
    public bool Encounter => EncounterIntensity > 0;
    public string RelevanceFingerprint { get; }

    /// <summary>How badly the player needs defending, 0 to 1, from the threat sense's own urgency rule.</summary>
    public double ProtectionUrgency { get; }

    /// <summary>
    /// What a need is worth relative to its own census while the player is in danger.
    ///
    /// **This is still a stopgap, and what it stands in for changed on 21 September 2026.** It used to
    /// stand in for predicted harm to the player, because no course priced that at all; the player is a
    /// contact actor now, with his own forecast path, and a course's projected kills truncate the
    /// threats they remove. Deleting it on that basis was tried the same day and measured wrong, which
    /// is the finding worth keeping rather than the term itself.
    ///
    /// On `danger lifts combat over work`, a real zombie walking into a player at forty life, with the
    /// harm term live and this multiplier removed:
    ///
    ///     mine-target  0.5353   useful 0.8853  harm 0.3500
    ///     combat      -0.3323   useful 0.0177  harm 0.3500
    ///     (idle)      -0.3500   useful 0.0000  harm 0.3500
    ///
    /// The harm is priced, correctly, at fourteen damage against forty life — and it is *identical on
    /// every course*, combat included, so it cannot separate them. <see cref="ForecastContactHarm"/>
    /// prices first contact and stops that actor's continuation there, by design, because immunity,
    /// knockback and hit hooks need a successor model before a second overlap can be a second hit. The
    /// zombie reaches the player about thirty ticks in and no course kills it first, so every course
    /// predicts the same single hit and defending is worth nothing. In the game the player is hit again
    /// every immunity window until something kills it, which is the quantity this term now approximates.
    ///
    /// So the two are not two terms for one quantity: the forecast owns *one predicted hit*, and this
    /// owns *a threat that keeps hitting*. Where a course does kill the threat inside the horizon the
    /// forecast carries it properly and this multiplier is not what decides — the truncation is.
    ///
    /// **Delete this when a course's own kill truncates the harm it prevents inside the forecast's
    /// horizon.** The condition used to name repeated hits, and that model was built on 21 September
    /// 2026 — the immunity window is in `ForecastContactHarm` and the harm term does discriminate now,
    /// where it was pinned identical on every course before. It is still not what decides the behaviour,
    /// measured single-variable on `danger lifts combat over work` that evening: with the urgency half
    /// of this term removed and the encounter half left standing, combat takes **0 of 120 ticks**
    /// against **109 of 120** with it.
    ///
    /// The numbers say where the gap moved to rather than that it closed. Idling reads a harm of 0.6184
    /// where any course that moves the body reads 0.9082, so repeated hits are being priced; but combat
    /// and mining read 0.9082 *as each other*, to four decimals, which means the player is hit the same
    /// number of times whether the companion fights the zombie or mines beside it. A course's predicted
    /// kill never lands inside the horizon on that scene, so the truncation that would pay for defending
    /// never fires and combat's entire advantage is a useful-effects margin of 0.0067.
    ///
    /// So the failure case has changed with the condition: this term is wrong wherever urgency and a
    /// *truncated* harm forecast disagree, and the thing to investigate is the kill rather than the hits
    /// — whether the horizon is shorter than the time to kill, whether a partial kill should contribute
    /// at all, or whether the fixture's companion is simply under-armed. `AIC-439` carries the table.
    ///
    /// The shape is the chooser's own rather than a new invention: danger suppresses *work* rather than
    /// inflating combat, so a non-combat need pays <c>1 − urgency</c> and a hostile's life pays in full.
    /// Written the other way round — a bonus on combat — the same ordering would need a magnitude nobody
    /// could derive, and every tuning of it would move work's value too.
    ///
    /// Encounter intensity joins urgency here as of 21 September 2026, under the chooser's own rule
    /// <c>1 − max(urgency, intensity)</c>, and the max is the whole of why it is one term and not two
    /// factors multiplied. A boss fight raises the player's danger *and* the encounter, so multiplying
    /// would charge one situation twice and price a wounded player in a blood moon at a quarter of what
    /// either alone says; taking the larger reads them as two views of one danger, which is what they
    /// are. It was left out until this date because the episode carried the encounter as a boolean, and a
    /// boolean read as an intensity of one zeroes every optional need the instant pressure is inferred at
    /// all — so the fix was upstream, in what the episode is handed, rather than in this expression. The
    /// sense has published a ramping float the whole time.
    ///
    /// What this buys, measured on the mining scene: a blood moon over a surface player now stops the
    /// same copper job the quiet scene takes, while the same blood moon with the player underground —
    /// where the game does not run it, so the sense reads <c>none</c> — leaves mining valued to within a
    /// ten-thousandth of the quiet scene. Both halves matter, because a term that stopped work under any
    /// blood moon anywhere would pass the first assertion by being wrong everywhere.
    /// </summary>
    public double RelevanceFor(NeedKind kind)
        => kind == NeedKind.HostileLife ? 1 : Math.Max(0, 1 - Math.Max(ProtectionUrgency, EncounterIntensity));

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
