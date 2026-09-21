#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Tools.Ledger;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// What the course brain did with a play, as numbers and never as a verdict.
///
/// <para>These are the questions a capture of the retained-course brain has to be able to answer
/// before a playtest can tell a poor decision from a stale model: how often it decided at all, how
/// often it kept what it had, how much of the order space it priced before the tick's allowance cut
/// it, how often it had concrete work bound rather than falling back to companionship, and what it
/// spent its refusals on. Every one is a value with a direction and no threshold, because whether
/// forty per cent retained is good is a question about the design rather than about the recording.</para>
///
/// <para>The one number with a stated direction and no obvious reading is <c>orders-priced</c>. More
/// is not better on its own — a companion that prices two hundred orders a tick in an empty field is
/// spending a frame on nothing — so it carries no direction at all and is read beside the refusal
/// split and the cut share, which together say whether the search stopped because it was finished or
/// because it ran out.</para>
/// </summary>
public sealed class MeasureCourseWork : IMeasure
{
    public string Name => "course";
    public string[] Needs => new[] { "tick" };

    public string? Missing(Session session)
        => CheckEvents.SidecarUnavailable(session, "the typed course decisions")
            ?? (ReadCourseChronicle.Read(session, session.Path).Coverage == "historical-unavailable"
                ? "typed course decisions (first written by schema 0.41.0)" : null);

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        CourseDecisionLog log = ReadCourseDecisions.From(ReadGodsEyeEvents.Read(session.Path));
        IReadOnlyList<CourseDecision> all = log.Decisions;
        if (all.Count == 0)
        {
            yield return PlayRow.Skipped(Name + "/decisions",
                "the capture carries the schema for typed course decisions and holds none");
            yield break;
        }

        yield return PlayRow.Count(Name + "/decisions", all.Count, "decisions", null,
            $"{all.Count:n0} course decision(s) recorded across the capture"
            + (log.Unreadable > 0 ? $"; {log.Unreadable:n0} payload(s) unreadable and excluded" : ""));

        yield return PlayRow.Share(Name + "/settled", all.Count(d => d.Settled), all.Count, null,
            "decisions the owner called settled, so the course it published was one it had finished deciding");

        yield return PlayRow.Share(Name + "/retained", all.Count(d => d.Reason == ReadCourseDecisions.RetainedReason),
            all.Count, null,
            "decisions that kept the course already running rather than publishing a new one — the retention the whole "
            + "design is named for, and equally what a companion stuck on a stale course looks like");

        yield return PlayRow.Share(Name + "/bound-step", all.Count(d => d.Purpose.Length > 0), all.Count, null,
            "decisions carrying a bound step, so the companion had concrete work to perform; the remainder is an empty "
            + "order, which is companionship with its own projected costs rather than an idle");

        yield return PlayRow.Share(Name + "/search-exhausted", all.Count(d => d.SearchExhausted), all.Count, null,
            "decisions whose order search finished rather than being cut by the tick's allowance — an exhausted search "
            + "is the only one whose refusals are a proven absence");

        // Percentiles rather than a mean, because a crowd makes this distribution long-tailed and a
        // mean over it describes no tick that happened.
        long[] priced = all.Select(d => d.OrdersPriced).OrderBy(v => v).ToArray();
        long[] refused = all.Select(d => d.OrdersRefused).OrderBy(v => v).ToArray();
        yield return PlayRow.Count(Name + "/orders-priced-median", Percentile(priced, 0.5), "orders", null,
            "orders priced per decision, median. No direction: pricing more is not better on its own, and this is read "
            + "beside the cut share, which says whether the search stopped because it finished or because it ran out");
        yield return PlayRow.Count(Name + "/orders-priced-p90", Percentile(priced, 0.9), "orders", null,
            "orders priced per decision, 90th percentile — the busy ticks, where the frame budget is actually spent");
        yield return PlayRow.Count(Name + "/orders-refused-median", Percentile(refused, 0.5), "orders", null,
            "orders refused per decision, median; the reasons are split across the rows below");

        yield return PlayRow.Count(Name + "/steps-median", Percentile(all.Select(d => d.Steps).OrderBy(v => v).ToArray(), 0.5),
            "steps", null, "steps in the published course, median — how far ahead the companion was actually committed");

        // The refusal split is the number that says what the search spent itself on, and it is the one
        // a reader of a bad capture reaches for first: a search cut by its allowance and a search that
        // refused every order for want of a route look identical in a count of empties.
        var byReason = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (CourseDecision decision in all)
            foreach (KeyValuePair<string, long> refusal in decision.Refusals)
                byReason[refusal.Key] = byReason.GetValueOrDefault(refusal.Key) + refusal.Value;
        long tallied = byReason.Values.Sum();
        if (tallied == 0)
            yield return PlayRow.Skipped(Name + "/refusal-split",
                "no decision published a refusal tally, so nothing here can say what the search refused");
        else
            foreach (KeyValuePair<string, long> reason in byReason.OrderByDescending(r => r.Value).Take(6))
                yield return PlayRow.Share(Name + "/refusal/" + reason.Key, reason.Value, tallied, null,
                    $"of every tallied refusal, those refused for '{reason.Key}'. The producer publishes its four largest "
                    + "reasons per decision, so this is a share of what was published rather than of every refusal");

        var activities = all.Where(d => d.Activity.Length > 0).GroupBy(d => d.Activity, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ToList();
        yield return PlayRow.Count(Name + "/activities", activities.Count, "activities", null,
            activities.Count == 0 ? "no decision named an activity"
                : "distinct activities chosen: " + string.Join(", ", activities.Select(g => $"{g.Key} {100.0 * g.Count() / all.Count:0.0}%")));
    }

    /// <summary>
    /// Floor-of-rank, the same convention <c>MeasureBrainCost</c> and the planning rows use, so a
    /// percentile quoted about a capture means the same thing wherever it was taken.
    /// </summary>
    private static double Percentile(long[] sorted, double fraction)
        => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)Math.Floor(fraction * (sorted.Length - 1)))];
}
