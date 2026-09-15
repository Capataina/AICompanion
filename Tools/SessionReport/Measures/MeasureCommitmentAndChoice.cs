#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Tools.Ledger;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Reading one field out of an occurrence's packed <c>key=value;key=value</c> detail.
///
/// This is all that survives of <c>cancelled-in-flight</c>, the measure that counted moves released
/// by their own owner while the body was in the air. It read <c>last-edge=</c> and
/// <c>preparation=</c> out of a <c>movement-state</c> sample, and the producer writes neither: the
/// orb's navigator carries a route rather than a proved edge with a preparation in front of it, so
/// there is no interrupted-and-cancelled edge to count and no ballistic arc to be released into.
/// A measure left reading those keys would have returned zero for ever, which is the one outcome
/// this harness treats as worse than a red.
/// </summary>
internal static class EventDetail
{
    /// <summary>
    /// The text between a key and the next delimiter. Written by hand rather than as a regular
    /// expression because the payload nests braces — an <c>EdgeReport</c> holds <c>From = {X:… Y:…}</c>
    /// — so the obvious pattern of "everything up to the closing brace" stops at the first inner
    /// one and silently matches nothing, which reads in a report exactly like a capture with no
    /// cancellations in it.
    /// </summary>
    internal static string Between(string text, string key, string terminator)
    {
        int start = text.IndexOf(key, StringComparison.Ordinal);
        if (start < 0) return "";
        string rest = text[(start + key.Length)..];
        int end = rest.IndexOf(terminator, StringComparison.Ordinal);
        return (end < 0 ? rest : rest[..end]).Trim();
    }
}

/// <summary>
/// The body stopping on its own route, counted by the reason the producer attributed at the moment
/// it happened rather than by a reason reconstructed afterwards from retained state.
///
/// <c>other</c> is the one the plan names a pass line for, and it is named by exclusion on purpose.
/// The producer attributes a stop in a strict precedence — a route replaced under a still body is
/// <c>during-replan</c> and explains the stillness outright, a body pressed against a wall for the
/// whole stop is <c>against-wall</c> and names the steering aiming through a wall — so anything
/// reaching <c>other</c> is a body that had a segment to fly, was not waiting for a plan, and was not
/// touching anything. That is a stall with nothing in the record accounting for it, which is the
/// strongest thing a stop can say about this body.
///
/// The three are emitted together because a reason that stops firing while another starts is a
/// defect that moved rather than one that was fixed, and only the whole split shows that.
/// </summary>
public sealed class MeasureStopsByReason : IMeasure
{
    public string Name => "unexplained-stops";
    public string[] Needs => new[] { "tick" };

    public string? Missing(Session session)
        => CheckEvents.SidecarUnavailable(session, "the stop occurrences with the reason attributed as each stop happened");

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
        var stops = new Dictionary<string, int>();
        var ticks = new Dictionary<string, int>();
        foreach (GodsEyeEvent e in log.Events)
        {
            if (e.kind != "stop") continue;
            string reason = EventDetail.Between(e.detail, "reason=", ";");
            if (reason.Length == 0) reason = "unattributed";
            stops[reason] = stops.GetValueOrDefault(reason) + 1;
            string span = EventDetail.Between(e.detail, "ticks=", ";");
            ticks[reason] = ticks.GetValueOrDefault(reason) + (int.TryParse(span, out int t) ? t : 0);
        }

        const string Unexplained = "other";
        yield return PlayRow.Count($"{Name}/stops", stops.GetValueOrDefault(Unexplained), "stops", "down",
            "stops the producer could attribute neither to a replan nor to a wall the body was pressed against, which is a body with a segment to fly that stopped for nothing the record names",
            "R4", "pass-line:zero");
        yield return PlayRow.Count($"{Name}/ticks", ticks.GetValueOrDefault(Unexplained), "ticks", "down",
            "ticks spent in those stops", "R4", "pass-line:zero");
        foreach (string reason in stops.Keys.Where(r => r != Unexplained).OrderBy(r => r, StringComparer.Ordinal))
            yield return PlayRow.Count($"stops-by-reason/{reason}", stops[reason], "stops", "down",
                $"stops attributed to {reason}, {ticks.GetValueOrDefault(reason):n0} ticks in total", "R9");
    }
}

/// <summary>
/// Why the chosen activity changed, split into the two answers that want opposite fixes.
///
/// When the action label changes, either the activity that was running could still have run and
/// simply scored lower — it was outscored, and the repair is a commitment margin — or it could not
/// have run at all, because its own offer on that tick says it had no usable destination. Those
/// are different defects and a raw switch count cannot tell them apart, which is why "it flickers
/// between hunting and following" survived as an impression for as long as it did.
///
/// The offer is read on the flip tick and belongs to the *outgoing* activity, and both halves of
/// that matter: reading the previous row's offer measures the state of the world one tick before
/// the decision that used it, which split the same 250 flips 203-to-47 instead of 121-to-129 and
/// sent a reader looking for a chooser defect that was not there.
///
/// A raw score of zero counts as invalid alongside the four eligibilities, because the evaluator
/// multiplies its considerations and any zero vetoes the activity outright — a zero-valued offer
/// was never selectable whatever its eligibility word says.
/// </summary>
public sealed class MeasureValidityFlips : IMeasure
{
    public string Name => "validity-flips";
    public string[] Needs => new[] { "action" };

    public string? Missing(Session session)
        => session.Names.Any(name => name.EndsWith("_offer", StringComparison.Ordinal))
            ? null
            : "<activity>_offer columns, which carry the eligibility each activity reported on the tick it lost";

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        Column action = session["action"];
        int invalid = 0, outscored = 0;
        var invalidBy = new Dictionary<string, int>();
        var outscoredBy = new Dictionary<string, int>();
        var pairs = new Dictionary<string, int>();

        for (int row = 1; row < session.Count; row++)
        {
            string was = action.Text[row - 1], now = action.Text[row];
            if (was == now || was.Length == 0 || was == "-" || now.Length == 0) continue;
            pairs[$"{was}->{now}"] = pairs.GetValueOrDefault($"{was}->{now}") + 1;

            string? eligibility = ReadPlay.Eligibility(session, was, row);
            Column? raw = session.Find($"{was}_raw");
            bool zeroValued = raw != null && !float.IsNaN(raw.Number[row]) && raw.Number[row] == 0f;
            if (ReadPlay.Invalid(eligibility) || zeroValued)
            {
                invalid++;
                invalidBy[was] = invalidBy.GetValueOrDefault(was) + 1;
            }
            else
            {
                outscored++;
                outscoredBy[was] = outscoredBy.GetValueOrDefault(was) + 1;
            }
        }

        int total = invalid + outscored;
        yield return PlayRow.Count($"{Name}/switches", total, "switches", "down",
            "changes of the chosen activity between consecutive rows", "R5");
        yield return PlayRow.Share($"{Name}/share-invalid", invalid, total, "down",
            "switches where the outgoing activity's own offer on the flip tick says it could not have run — a budget cut or an absence reported as a proven impossibility, not a chooser preference",
            "R5");
        yield return PlayRow.Share($"{Name}/share-outscored", outscored, total, null,
            "switches where the outgoing activity was still valid and simply scored lower, which is the half a commitment margin could hold", "R5");
        foreach ((string activity, int count) in invalidBy.OrderByDescending(p => p.Value).Take(4))
            yield return PlayRow.Count($"{Name}/invalid/{activity}", count, "switches", "down",
                $"switches away from {activity} where {activity}'s own offer said it was not runnable", "R5");
        foreach ((string activity, int count) in outscoredBy.OrderByDescending(p => p.Value).Take(4))
            yield return PlayRow.Count($"{Name}/outscored/{activity}", count, "switches", null,
                $"switches away from {activity} where it was still runnable and lost on score", "R5");
        foreach ((string pair, int count) in pairs.OrderByDescending(p => p.Value).Take(5))
            yield return PlayRow.Count($"{Name}/pair/{pair}", count, "switches", null,
                $"the {pair} transition, one of the most frequent pairs in the capture", "R5");
    }
}

/// <summary>
/// How often hunting reported that no firing position exists, which is the label for a proven
/// impossibility being used for a search that merely ran out of budget.
///
/// It is read from the decision occurrences rather than from the rows on purpose. The offer column
/// is retained between comparisons, so it stands unchanged across every row until the next one and
/// counting rows would weight each decision by how long it happened to be held. A decision
/// occurrence is one comparison.
/// </summary>
public sealed class MeasureHuntKnownUnusableShare : IMeasure
{
    public string Name => "hunt-known-unusable-share";
    public string[] Needs => new[] { "hunt_offer" };

    public string? Missing(Session session)
        => CheckEvents.SidecarUnavailable(session, "the decision occurrences, one per completed comparison");

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
        int decisions = 0, knownUnusable = 0, unresolved = 0, unreadable = 0;
        foreach (GodsEyeEvent e in log.Events)
        {
            if (e.kind != "decision") continue;
            decisions++;
            string factors = EventDetail.Between(e.detail, "factors:hunt=", ";factors:");
            if (factors.Length == 0) { unreadable++; continue; }
            string offer = EventDetail.Between(factors, "offer:", ",");
            if (offer.Length == 0) { unreadable++; continue; }
            switch (offer.Split('/')[0])
            {
                case "KnownUnusable": knownUnusable++; break;
                case "Unresolved": unresolved++; break;
            }
        }
        yield return PlayRow.Share($"{Name}/share", knownUnusable, decisions, "down",
            "comparisons in which hunting reported a proven absence of any firing position",
            "R5");
        // The two shares are reported side by side because the word moved under them, not the
        // behaviour. On the 13:27 capture a stand search that ran out of solves reported
        // KnownUnusable — the same word as a proven absence — so that capture's share counts both
        // and is pinned as such. R5 split them: a cut search now reports Unresolved. So the
        // KnownUnusable share on the next capture falls partly because searches were relabelled and
        // not because fewer of them ran out, and only the sum of these two rows is comparable across
        // that change. A single row here would read as a large improvement that nobody made.
        yield return PlayRow.Share($"{Name}/unresolved-share", unresolved, decisions, "down",
            "comparisons in which hunting's stand search ran out of solves or time rather than proving anything; zero on any capture recorded before R5 split this from the row above, where it was counted there",
            "R5");
        if (unreadable > 0)
            yield return PlayRow.Count($"{Name}/unreadable", unreadable, "comparisons", "down",
                "comparisons whose hunt factor carried no readable offer, which is measured-nothing rather than a hunt that was usable", "R5");
    }
}

/// <summary>
/// What the hands were doing under each activity, which is what settles whether a silent gun is a
/// hands defect or a feet defect.
///
/// The cross-tab is the measure rather than the total, and that is the whole point: a session-wide
/// "fired on 72 ticks" reads as a broken arsenal. Split by the activity that owned the feet, the
/// same rows say the gun fires or reloads on nearly half the ticks where hunting sent the body to a
/// stand and has no target at all on almost every tick of keeping company — so the hands work
/// whenever the feet arrive, and the defect is upstream in what the feet were told to do.
/// </summary>
public sealed class MeasureHandsByActivity : IMeasure
{
    public string Name => "hands-by-activity";
    public string[] Needs => new[] { "fire", "action" };

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        Column fire = session["fire"], action = session["action"];
        var total = new Dictionary<string, int>();
        var noTarget = new Dictionary<string, int>();
        var working = new Dictionary<string, int>();
        // An unarmed companion is neither idle nor working: both weapon slots are empty, so the
        // hands have nothing to fire with whatever the feet were told to do. It is its own share
        // so a session played before any weapon was handed over reads as that rather than as a
        // companion that never found a target.
        var unarmed = new Dictionary<string, int>();
        for (int row = 0; row < session.Count; row++)
        {
            string activity = action.Text[row];
            if (activity.Length == 0 || activity == "-") continue;
            total[activity] = total.GetValueOrDefault(activity) + 1;
            string outcome = fire.Text[row];
            if (outcome == "no-target") noTarget[activity] = noTarget.GetValueOrDefault(activity) + 1;
            // Fired and cooling down are one state for this question: a reload between two shots is
            // the gun working, and an allowance that folded it into "nothing fired" once turned a
            // healthy two-minute exchange into one enormous finding whose own tally listed the
            // shots it fired.
            if (outcome is "fired" or "cooldown") working[activity] = working.GetValueOrDefault(activity) + 1;
            if (outcome == "no-weapon") unarmed[activity] = unarmed.GetValueOrDefault(activity) + 1;
        }
        foreach ((string activity, int rows) in total.OrderByDescending(p => p.Value).Take(5))
        {
            yield return PlayRow.Share($"{Name}/{activity}/no-target", noTarget.GetValueOrDefault(activity), rows, "down",
                $"ticks under {activity} on which the hands had nothing to shoot at", "R7");
            yield return PlayRow.Share($"{Name}/{activity}/fired-or-cooldown", working.GetValueOrDefault(activity), rows, "up",
                $"ticks under {activity} on which the gun fired or was reloading", "R7");
            yield return PlayRow.Share($"{Name}/{activity}/no-weapon", unarmed.GetValueOrDefault(activity), rows, "down",
                $"ticks under {activity} on which both weapon slots were empty", "R7");
        }
    }
}
