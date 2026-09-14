#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// A movement refusal the body never moves away from, which is the companion standing at a
/// take-off doing nothing while the same step is refused over and over.
///
/// It is worth a check of its own because a refusal is the one movement failure the rest of the
/// record cannot see. A fault is counted, named and sampled in the chronicle; a refusal happens
/// before the attempt's first tick, so it sets no fault, and the navigator's <c>Rejected</c>
/// status is replaced within the same brain tick by whatever the next stage sets. The 09:28
/// capture of 14 September holds 684 physical-fault rejections and one jump fault in its census,
/// and its per-tick chronicle shows <c>nav_status=Rejected</c> on exactly three ticks out of
/// fifteen thousand. What does survive is the god's-eye <c>movement-state</c> sample, which
/// carries the navigator's last rejection beside the body's position, and a refusal the body is
/// not moving away from is visible there as the same rejection at the same pixel, sample after
/// sample.
///
/// Three consecutive samples is the bar. One is ordinary — a step is refused, the tile is priced
/// and the next plan goes another way, which is the designed path and leaves the body moving or
/// the rejection changed by the next sample. Three means neither happened, and the capture that
/// prompted this check reaches ten on the edge it froze at.
/// </summary>
public sealed class PersistentRejectionsAreFindings : ICheck, ICheckCoverage
{
    /// <summary>How many consecutive samples of one refusal at one pixel stop being a replan and start being a park.</summary>
    private const int Persistent = 3;

    // Deliberately not worded with "refused": the multi-run report says "refused cross-run joins"
    // when two captures were recorded by different code, and its self-test asserts that word is
    // absent from a clean join by plain substring — so a check whose *name* carries it fails that
    // assertion from the coverage block, in a report about something else entirely.
    public string Name => "did one rejected step hold the body in place";
    public string[] Needs => new[] { "tick" };

    public string? Missing(Session session)
        => CheckEvents.SidecarUnavailable(session, "the navigator's last rejection beside the body's position");

    public IEnumerable<Finding> Run(Session session)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
        var held = new List<GodsEyeEvent>();
        string key = "";
        // The rejection this check last saw, which is what turns a retained field into an event.
        string previous = "";
        int issuedCount = 0;
        var findings = new List<Finding>();

        foreach (GodsEyeEvent e in log.Events)
        {
            if (e.kind != "movement-state") continue;
            string rejection = Rejection(e.detail);
            // A refusal *issued* on this sample, as opposed to one still standing in the field from
            // some earlier tick. The navigator's last rejection is retained until another replaces
            // it, so a body that stops beside a refusal it walked away from minutes ago reads
            // exactly like a body frozen at the take-off of one — and this check could not tell
            // them apart, which is why it raised 23 stretches against the census's 4 refusals.
            //
            // Freshness is the *change* of the field. Nothing new had to be recorded to get it:
            // the sample already carries the value, and two consecutive samples carrying different
            // values is the only evidence a refusal happened between them. A stretch may therefore
            // open only where the text changed, and a stale field standing over a still body opens
            // nothing however long it stands.
            bool issued = rejection.Length > 0 && rejection != previous;
            previous = rejection;
            if (issued) issuedCount++;
            // The position is part of the key on purpose. The navigator's last rejection is a
            // sticky property — it stands until another one replaces it — so the same text
            // repeating while the body walks away is a stale reading rather than a stuck body,
            // and only a rejection that repeats at an unchanged pixel says the refusal is live.
            string current = rejection.Length == 0
                ? ""
                : $"{rejection}@{e.pos_x:0.##},{e.pos_y:0.##}";
            if (current.Length > 0 && current == key)
            {
                held.Add(e);
                continue;
            }
            Close(findings, held);
            held.Clear();
            // Only a freshly issued refusal starts a stretch. The other way to arrive here is the
            // body having moved while the field stood unchanged, which ends the stretch it was in
            // and must not begin another: the refusal it names was answered by walking away from
            // it, and that is the designed path rather than a park.
            key = issued ? current : "";
            if (issued) held.Add(e);
        }
        Close(findings, held);
        // Zero findings and zero coverage are the two things this whole tool exists to keep apart,
        // and after the freshness rule they look identical here. So the check says what it saw: a
        // capture in which refusals were issued and none of them held the body is a clean result
        // about a question that was actually asked, where silence would be a reader's guess.
        //
        // It also states the sensitivity this check does not have, because that is now the more
        // likely reason for a zero. A `movement-state` sample is written when the navigator's
        // verdict *changes*, so a body frozen re-refusing one step writes its refusal once and then
        // nothing — the samples that would prove it persisted are never taken. This check can
        // therefore only catch a park whose opening refusal is followed by further samples at the
        // same pixel, and the plan's own remedy for the rest is a refusal occurrence carrying its
        // own tick, which is a recorder change and not this one.
        if (findings.Count == 0 && issuedCount > 0)
            findings.Add(new Finding(Severity.Oddity, Name,
                $"{issuedCount:n0} refusal(s) were issued and none of them held the body",
                "A refusal counts from the sample on which the navigator's last rejection changed, because that field is retained "
                    + "and a stale one standing over a still body is not a refusal happening. None of these was followed by further "
                    + "samples at an unchanged pixel, so nothing was parked on a refused step. Read the zero with this check's own limit "
                    + "beside it: a movement-state sample is written when the verdict changes, so a body re-refusing one step writes it "
                    + "once and the samples that would show it persisting are never taken. Proving a park of that shape needs a refusal "
                    + "occurrence carrying its own tick.",
                0, 0, issuedCount));
        return findings;
    }

    private static void Close(List<Finding> findings, List<GodsEyeEvent> held)
    {
        if (held.Count < Persistent) return;
        GodsEyeEvent first = held[0];
        var events = new List<GodsEyeEvent>(held);
        findings.Add(CheckEvents.Aggregate(
            Severity.Definitive,
            "did one rejected step hold the body in place",
            $"one rejected step held the body at {first.pos_x:0.#},{first.pos_y:0.#} across {events.Count} samples",
            events,
            "A rejected step is meant to price its tile and be replanned around, so the body moves or the "
                + "refusal changes by the next sample. The same rejection at the same pixel, sample after sample, "
                + "means neither happened: the plan is returning an entry the proof refuses and nothing is "
                + "learning from the refusal. This is the one movement failure the chronicle cannot show — a "
                + "refusal precedes the attempt's first tick, so it raises no fault, and the navigator's Rejected "
                + "status is overwritten inside the same brain tick."));
    }

    private static string Rejection(string detail)
    {
        const string marker = "last-rejection=";
        int start = detail.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "";
        string value = detail[(start + marker.Length)..];
        int end = value.IndexOf(";failure=", StringComparison.Ordinal);
        return end >= 0 ? value[..end] : value;
    }
}
