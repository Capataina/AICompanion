#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Companion.Brain.Infrastructure.Selection;

/// <summary>Values captured after opportunity discovery. No live entity or activity reference
/// enters evaluation, so comparing a prepared board cannot acquire, prune or advance a job.</summary>
public readonly record struct PreparedActivity(int Index, string Name, float RawValue, float ForecastTicks,
    bool IsExcursion, bool HasTarget, bool IsFollowing, bool IsIncumbent,
    Activities.OfferEligibility Eligibility = Activities.OfferEligibility.NoOpportunity, bool ServesEncounter = false,
    float TaskTicks = 0, Microsoft.Xna.Framework.Vector2? Site = null, float PlayerFit = 1, bool ServesPlayerDirectly = false,
    float Separation = 1);

/// <param name="TaskWindowTicks">The time term's half-life; zero turns the time term off, which is what a board built
/// before the term existed means.</param>
public readonly record struct ActivityComparisonContext(float ProtectionUrgency, bool Stranded,
    float ThreatHorizonTicks, float InterruptibleTicks, float HorizonOverrunTicks, float Commitment,
    bool WithinActivityAllowance, float FollowDuringUsefulWork, float EncounterIntensity = 0,
    float TaskWindowTicks = 0);

/// <param name="Order">What <see cref="OrderNearbyTasks"/> did to <paramref name="Final"/> after the product was taken: the
/// placed value over the value before ordering, one where ordering did not apply. Recorded as a factor so the recorded
/// factors multiply to the recorded final on the ticks ordering reshapes, which are the ticks a reader asks about.</param>
public readonly record struct EvaluatedActivity(int Index, string Name, float Raw, float Final,
    float Protection, float Commitment, float Horizon, float UsefulWork, string Error, float Reunion = 1,
    float Time = 1, float PlayerFit = 1, float Order = 1);

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
            // The player's need for help reaches optional work only here. Protection urgency already
            // weighs each threat against how soon the companion could intervene, so an activity that
            // also multiplied its raw value by player danger charged the same threat twice and could
            // not tell a shot the companion can take from across the room from one it cannot.
            // A boss fight or world event is the second reason optional work stops mattering, and it is a
            // reading of the same danger rather than an independent one: a boss is also a threat raising
            // urgency, and crowd pressure is the threats themselves. So the stronger of the two readings is
            // charged, never their product, and combat — the thing an encounter is about — pays urgency only.
            float danger = candidate.ServesEncounter ? context.ProtectionUrgency : Math.Max(context.ProtectionUrgency, context.EncounterIntensity);
            // A fight that still serves him is not leaving him. Discounting it as an excursion when
            // he is in danger (or when the path to the stand is long) is how a hitting plan at raw 1.02
            // became final 0.00 the next tick and company 0.05 took the body. The surface-zombie drop
            // is ServesPlayerDirectly false, so it still pays.
            bool withHim = candidate.ServesPlayerDirectly;
            float protection = candidate.IsExcursion && !context.Stranded && !withHim ? 1 - danger : 1;
            float commitment = candidate.RawValue > 0 && candidate.IsIncumbent ? context.Commitment : 1;
            float horizon = 1;
            if (candidate.RawValue > 0)
            {
                float forecast = candidate.IsExcursion ? Math.Min(context.InterruptibleTicks, candidate.ForecastTicks) : candidate.ForecastTicks;
                if (forecast > context.ThreatHorizonTicks)
                    horizon = Math.Max(0, 1 - (forecast - context.ThreatHorizonTicks) / context.HorizonOverrunTicks);
            }
            // A job somewhere pays for the time it takes and for the time it keeps the companion apart, whether or not
            // it is an excursion. A hunt that can shoot from where the orb hovers is not an excursion for the threat
            // horizon, and it used to pay no separation either, so it held the orb over a surface zombie while the
            // player dropped four hundred pixels into a cave (15 September 2026, ticks 7308-7735). It pays by how far
            // outside the player's region its stand will be: nothing inside, rising with distance beyond the edge to all of
            // it at fly-home distance, keeping company's own slope before its cap, measured by the chooser against the region carried along the
            // player's travel for the job's own duration, so a player leaving makes a long job's separation grow and a quick
            // job's not. It replaced a factor priced from a per-tick delay cost, which charged a hunt beside an idle player
            // for a long return estimate and was a second separation cost beside the work allowance.
            bool task = candidate.HasTarget && !candidate.IsFollowing && !candidate.ServesPlayerDirectly;
            float taskTicks = Math.Max(candidate.ForecastTicks, candidate.TaskTicks);
            float reunion = !withHim && (candidate.IsExcursion || task) ? candidate.Separation : 1;
            // Worth per time rather than flat worth: a job nearly done or on the way is worth nearly all of its value,
            // the same job across the room less, and no job reaches zero for being long.
            float time = task && candidate.RawValue > 0 && context.TaskWindowTicks > 0
                ? context.TaskWindowTicks / (context.TaskWindowTicks + taskTicks) : 1;
            // Whether the job will still be near the player by the time it is done, read from where he is heading.
            float fit = task ? candidate.PlayerFit : 1;
            float final = candidate.RawValue * protection * commitment * horizon * reunion * time * fit;
            if (!float.IsFinite(final))
            {
                results[i] = new(candidate.Index, candidate.Name, candidate.RawValue, 0, protection, commitment, horizon, 1, "non-finite-product");
                continue;
            }
            results[i] = new(candidate.Index, candidate.Name, candidate.RawValue, final, protection, commitment, horizon, 1, "", reunion, time, fit);
            useful |= !candidate.IsFollowing && candidate.HasTarget && final > .1f;
        }
        // The discount on following while useful work exists stays unconditional, and that was decided rather than left:
        // lifting it while the player leaves was tried on 15 September 2026 to answer a hunt that held the orb over a
        // surface zombie as the player dropped into a cave, and it also pulled the companion off a quick ore job beside
        // a walking player, which Expected Behaviour asks it to take. What answers the zombie is the separation charge
        // above applying to every job somewhere, a hunt shooting from where the orb hovers included.
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
        // A positive value beside an absent, forbidden or unusable offer is a behaviour defect, and
        // rejecting it here is what keeps an absent offer from ever winning.
        if (candidate.RawValue > 0 && candidate.Eligibility is not (Activities.OfferEligibility.Usable or Activities.OfferEligibility.Unresolved))
            return "value-without-eligible-offer";
        if (!float.IsFinite(candidate.ForecastTicks) || candidate.ForecastTicks < 0) return "invalid-forecast";
        if (!float.IsFinite(candidate.TaskTicks) || candidate.TaskTicks < 0) return "invalid-task-time";
        if (!float.IsFinite(candidate.PlayerFit) || candidate.PlayerFit < 0 || candidate.PlayerFit > 1) return "invalid-player-fit";
        if (!float.IsFinite(context.TaskWindowTicks) || context.TaskWindowTicks < 0) return "invalid-task-window";
        if (!float.IsFinite(candidate.Separation) || candidate.Separation < 0 || candidate.Separation > 1) return "invalid-separation";
        if (!float.IsFinite(context.ProtectionUrgency) || context.ProtectionUrgency < 0 || context.ProtectionUrgency > 1) return "invalid-protection";
        if (!float.IsFinite(context.EncounterIntensity) || context.EncounterIntensity < 0 || context.EncounterIntensity > 1) return "invalid-encounter";
        // Positive infinity means no observed threat deadline; every other timing value is finite.
        if (float.IsNaN(context.ThreatHorizonTicks) || context.ThreatHorizonTicks < 0) return "invalid-threat-horizon";
        if (!float.IsFinite(context.InterruptibleTicks) || context.InterruptibleTicks < 0
            || !float.IsFinite(context.HorizonOverrunTicks) || context.HorizonOverrunTicks <= 0) return "invalid-timing-context";
        if (!float.IsFinite(context.Commitment) || context.Commitment < 0
            || !float.IsFinite(context.FollowDuringUsefulWork) || context.FollowDuringUsefulWork < 0 || context.FollowDuringUsefulWork > 1) return "invalid-value-factor";
        return "";
    }
}
