#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Tools.Ledger;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// A move that was cancelled while the body was in the air, which is the platform jump failing
/// counted rather than watched.
///
/// The record shape is exact and each part of it is load-bearing. The navigator's last edge reads
/// <c>Outcome = Interrupted</c> and the attempt ended <c>Cancelled</c>, so the move was released by
/// its owner rather than failing physically. The sample's own vertical velocity is non-zero and the
/// navigator's status is <c>Idle</c>, so the release happened with the body off the ground and the
/// navigator holding nothing — the path and the step nulled mid-arc, the motor handed no controls,
/// and the body flying on ballistics to land short of where the edge was proven to reach.
///
/// Both halves of the filter are needed. A cancellation on the ground is an ordinary route
/// replacement and there are plenty; an airborne sample with the navigator still executing is a
/// move in progress. Only the pair is the defect.
///
/// The last-edge field is sticky — it stands until another edge replaces it — so consecutive
/// identical reports are one cancellation observed repeatedly rather than a cancellation happening
/// repeatedly, and counting rows instead of changes would multiply one defect by however often the
/// sampler ran.
/// </summary>
public sealed class MeasureCancelledInFlight : IMeasure
{
    public string Name => "cancelled-in-flight";
    public string[] Needs => new[] { "tick" };

    public string? Missing(Session session)
        => CheckEvents.SidecarUnavailable(session, "the navigator's last edge and attempt ending beside the body's vertical velocity");

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
        var airborne = new Dictionary<string, int>();
        var all = new Dictionary<string, int>();
        string lastReport = "";

        foreach (GodsEyeEvent e in log.Events)
        {
            if (e.kind != "movement-state") continue;
            string edge = Between(e.detail, "last-edge=", ";preparation=");
            string ending = Between(e.detail, "attempt-ending=", ";");
            if (edge.Length == 0 || !edge.Contains("Outcome = Interrupted", StringComparison.Ordinal) || ending != "Cancelled")
            {
                lastReport = "";
                continue;
            }
            string report = edge + "|" + ending;
            if (report == lastReport) continue;
            lastReport = report;

            string kind = Between(edge, "Kind = ", ",");
            if (kind.Length == 0) kind = "unknown";
            all[kind] = all.GetValueOrDefault(kind) + 1;
            if (Math.Abs(e.vel_y) > 0 && Between(e.detail, "status=", ";") == "Idle")
                airborne[kind] = airborne.GetValueOrDefault(kind) + 1;
        }

        // Every kind the capture saw cancelled gets a row, including the ones at zero, so a kind
        // that stops being cancelled reads as a fixed number rather than as a vanished case — a
        // measure that only emits where something happened cannot show anything reaching zero.
        foreach (string kind in all.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            yield return PlayRow.Count($"{Name}/{kind}-airborne-idle", airborne.GetValueOrDefault(kind), "cancellations", "down",
                $"{kind} moves released by their own owner while the body was off the ground and the navigator was left Idle, out of {all[kind]:n0} cancelled in total",
                "R4", "pass-line:zero");
        }
        if (all.Count == 0)
            yield return PlayRow.Count($"{Name}/none", 0, "cancellations", "down",
                "no interrupted-and-cancelled edge was recorded in this capture at all", "R4", "pass-line:zero");
    }

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
/// <c>airborne-no-sideways-speed</c> is the one the plan names a pass line for, because it is the
/// same defect as a cancellation in flight seen from the body's side: a move released mid-arc
/// leaves a body with no horizontal drive and nothing steering it. The others are emitted beside it
/// because a reason that stops firing while another starts is a defect that moved rather than one
/// that was fixed, and only the whole split shows that.
/// </summary>
public sealed class MeasureStopsByReason : IMeasure
{
    public string Name => "airborne-no-sideways-speed-stops";
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
            string reason = MeasureCancelledInFlight.Between(e.detail, "reason=", ";");
            if (reason.Length == 0) reason = "unattributed";
            stops[reason] = stops.GetValueOrDefault(reason) + 1;
            string span = MeasureCancelledInFlight.Between(e.detail, "ticks=", ";");
            ticks[reason] = ticks.GetValueOrDefault(reason) + (int.TryParse(span, out int t) ? t : 0);
        }

        const string Airborne = "airborne-no-sideways-speed";
        yield return PlayRow.Count($"{Name}/stops", stops.GetValueOrDefault(Airborne), "stops", "down",
            "stops the producer attributed to a body in the air with no sideways drive, which is a move released mid-arc seen from the body's side",
            "R4", "pass-line:zero");
        yield return PlayRow.Count($"{Name}/ticks", ticks.GetValueOrDefault(Airborne), "ticks", "down",
            "ticks spent in those stops", "R4", "pass-line:zero");
        foreach (string reason in stops.Keys.Where(r => r != Airborne).OrderBy(r => r, StringComparer.Ordinal))
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
            string factors = MeasureCancelledInFlight.Between(e.detail, "factors:hunt=", ";factors:");
            if (factors.Length == 0) { unreadable++; continue; }
            string offer = MeasureCancelledInFlight.Between(factors, "offer:", ",");
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
        }
        foreach ((string activity, int rows) in total.OrderByDescending(p => p.Value).Take(5))
        {
            yield return PlayRow.Share($"{Name}/{activity}/no-target", noTarget.GetValueOrDefault(activity), rows, "down",
                $"ticks under {activity} on which the hands had nothing to shoot at", "R7");
            yield return PlayRow.Share($"{Name}/{activity}/fired-or-cooldown", working.GetValueOrDefault(activity), rows, "up",
                $"ticks under {activity} on which the gun fired or was reloading", "R7");
        }
    }
}
