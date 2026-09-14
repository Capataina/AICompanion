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
        var findings = new List<Finding>();

        foreach (GodsEyeEvent e in log.Events)
        {
            if (e.kind != "movement-state") continue;
            string rejection = Rejection(e.detail);
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
            key = current;
            if (current.Length > 0) held.Add(e);
        }
        Close(findings, held);
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
