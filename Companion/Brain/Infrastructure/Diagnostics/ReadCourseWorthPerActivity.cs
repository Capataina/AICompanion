#nullable enable

using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Activities;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>
/// What the course thought each registered activity's work was worth this tick, in the one shape every
/// diagnostic surface reads: the recorder's columns, the overlay's decision panel and the inspector's
/// evidence lines.
///
/// **It exists because those three surfaces each read <c>Chooser.LastScores</c> directly, and that list
/// has been empty in every played session since <c>0bb2c8a</c> put the course on the tick.** The symptom
/// differed per surface and none of them failed: the recorder wrote <c>0.00</c> and <c>not-compared</c>,
/// the overlay's Decisions panel drew zero rows, and the evidence panel printed "no comparison has run
/// yet" for an entire session. Three copies of one lookup is also why the fix had to be a shared reader
/// rather than three repairs — the next surface would have been the fourth copy.
///
/// The quantity is the course's, not a translation of the chooser's. <see cref="Worth.Raw"/> is what the
/// best order that domain leads was worth nominally and <see cref="Worth.Final"/> is what survives its
/// harm and companionship terms; the offer is the census admission in the three values every bounded
/// search in this tree answers in. An activity the course mints no domain for reports zero and
/// <c>not-compared</c>, which is honest rather than missing: keeping company declares no domain, because
/// an empty course <em>is</em> companionship.
/// </summary>
public static class ReadCourseWorthPerActivity
{
    /// <summary>One activity's worth as the course priced it, and how its census was admitted.</summary>
    public readonly record struct Worth(CompanionAction Action, float Raw, float Final,
        string Offer, string OfferReason, bool Priced);

    /// <summary>The default an activity with no course domain reports, kept as one literal because the
    /// recorder's column format and the report check that parses it both rest on this exact word.</summary>
    public const string NotCompared = "not-compared";

    public static List<Worth> Of(Brain brain)
    {
        var worths = new List<Worth>(brain.Chooser.Actions.Count);
        foreach (CompanionAction action in brain.Chooser.Actions)
            worths.Add(Of(brain, action));
        return worths;
    }

    public static Worth Of(Brain brain, CompanionAction action)
    {
        float raw = 0f, fin = 0f;
        bool priced = false;
        foreach (string domain in action.CourseDomains)
            if (brain.Course.LastLeaders.TryGetValue(domain, out var leader))
            {
                float nominal = (float)leader.Total.Nominal;
                // The better of several domains wins, the way collection's two methods always did: a
                // worthwhile drop is not hidden behind a worthless pot.
                if (priced && nominal <= raw) continue;
                priced = true;
                raw = nominal;
                fin = (float)(leader.UsefulEffects - leader.Harm - leader.Companionship);
            }

        string offer = NotCompared, reason = "-";
        foreach (string domain in action.CourseDomains)
            foreach (var admitted in brain.Course.Admitted)
                if (StringComparer.Ordinal.Equals(admitted.Domain, domain))
                {
                    string verdict = admitted.Usable > 0 ? "Usable"
                        : admitted.Unresolved > 0 ? "Unresolved"
                        : admitted.Unusable > 0 ? "KnownUnusable" : "NoOpportunity";
                    if (offer != NotCompared && verdict != "Usable") continue;
                    offer = verdict;
                    reason = admitted.Reason.Length == 0 ? "-" : admitted.Reason;
                }
        return new Worth(action, raw, fin, offer, reason, priced);
    }
}
