#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Companion.Brain.BehaviourSelection;

/// <summary>Values captured after opportunity discovery. No live entity or activity reference
/// enters evaluation, so comparing a prepared board cannot acquire, prune or advance a job.</summary>
public readonly record struct PreparedActivity(int Index, string Name, float RawValue, float ForecastTicks,
    bool IsExcursion, bool HasTarget, bool IsFollowing, bool IsIncumbent,
    Behaviours.OfferEligibility Eligibility = Behaviours.OfferEligibility.Usable);

public readonly record struct ActivityComparisonContext(float ProtectionUrgency, bool Stranded,
    float ThreatHorizonTicks, float InterruptibleTicks, float HorizonOverrunTicks, float Commitment,
    bool WithinActivityAllowance, float FollowDuringUsefulWork, float ReunionDelayCostPerTick = 0);

public readonly record struct EvaluatedActivity(int Index, string Name, float Raw, float Final,
    float Protection, float Commitment, float Horizon, float UsefulWork, string Error, float Reunion = 1);

/// <summary>Shared utility arithmetic over a captured board. Discovery and activation belong
/// to their callers; repeated evaluation of the same values has no side effects.</summary>
public static class EvaluatePreparedActivities
{
    public static EvaluatedActivity[] Evaluate(IReadOnlyList<PreparedActivity> candidates, in ActivityComparisonContext context)
    {
        var results = new EvaluatedActivity[candidates.Count];
        bool useful = false;
        for (int i = 0; i < candidates.Count; i++)
        {
            PreparedActivity candidate = candidates[i];
            string error = Validate(candidate, context);
            if (error.Length != 0)
            {
                results[i] = new(candidate.Index, candidate.Name, candidate.RawValue, 0, 1, 1, 1, 1, error);
                continue;
            }
            float protection = candidate.IsExcursion && !context.Stranded ? 1 - context.ProtectionUrgency : 1;
            float commitment = candidate.RawValue > 0 && candidate.IsIncumbent ? context.Commitment : 1;
            float horizon = 1;
            if (candidate.RawValue > 0)
            {
                float forecast = candidate.IsExcursion ? Math.Min(context.InterruptibleTicks, candidate.ForecastTicks) : candidate.ForecastTicks;
                if (forecast > context.ThreatHorizonTicks)
                    horizon = Math.Max(0, 1 - (forecast - context.ThreatHorizonTicks) / context.HorizonOverrunTicks);
            }
            float reunion = candidate.IsExcursion ? 1 / (1 + context.ReunionDelayCostPerTick * candidate.ForecastTicks) : 1;
            float final = candidate.RawValue * protection * commitment * horizon * reunion;
            if (!float.IsFinite(final))
            {
                results[i] = new(candidate.Index, candidate.Name, candidate.RawValue, 0, protection, commitment, horizon, 1, "non-finite-product");
                continue;
            }
            results[i] = new(candidate.Index, candidate.Name, candidate.RawValue, final, protection, commitment, horizon, 1, "", reunion);
            useful |= candidate.IsExcursion && candidate.HasTarget && final > .1f;
        }
        if (useful && context.WithinActivityAllowance)
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i].IsFollowing && results[i].Error.Length == 0)
                    results[i] = results[i] with { Final = results[i].Final * context.FollowDuringUsefulWork, UsefulWork = context.FollowDuringUsefulWork };
        return results;
    }

    private static string Validate(in PreparedActivity candidate, in ActivityComparisonContext context)
    {
        if (!float.IsFinite(candidate.RawValue) || candidate.RawValue < 0) return "invalid-raw-value";
        // Value is the answer to "how much is this worth", eligibility to "is there an offer at all".
        // A positive value beside an absent, forbidden or unusable offer is an adapter defect, and
        // rejecting it here is what keeps an absent offer from ever winning.
        if (candidate.RawValue > 0 && candidate.Eligibility is not (Behaviours.OfferEligibility.Usable or Behaviours.OfferEligibility.Unresolved))
            return "value-without-eligible-offer";
        if (!float.IsFinite(candidate.ForecastTicks) || candidate.ForecastTicks < 0) return "invalid-forecast";
        if (!float.IsFinite(context.ReunionDelayCostPerTick) || context.ReunionDelayCostPerTick < 0) return "invalid-reunion-cost";
        if (!float.IsFinite(context.ProtectionUrgency) || context.ProtectionUrgency < 0 || context.ProtectionUrgency > 1) return "invalid-protection";
        // Positive infinity means no observed threat deadline; every other timing value is finite.
        if (float.IsNaN(context.ThreatHorizonTicks) || context.ThreatHorizonTicks < 0) return "invalid-threat-horizon";
        if (!float.IsFinite(context.InterruptibleTicks) || context.InterruptibleTicks < 0
            || !float.IsFinite(context.HorizonOverrunTicks) || context.HorizonOverrunTicks <= 0) return "invalid-timing-context";
        if (!float.IsFinite(context.Commitment) || context.Commitment < 0
            || !float.IsFinite(context.FollowDuringUsefulWork) || context.FollowDuringUsefulWork < 0 || context.FollowDuringUsefulWork > 1) return "invalid-value-factor";
        return "";
    }
}
