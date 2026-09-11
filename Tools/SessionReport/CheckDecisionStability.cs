#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// How long a decision survives, which nothing here measured until a session had to be read by
/// hand to find it. Every behaviour the companion has is worth several seconds — walking to a
/// vein, crossing a room to a pot, flying an arc — and on 2026-09-11 the chosen behaviour changed
/// every 55.7 ticks across 480 switches, with 45% of runs lasting ten ticks or fewer. A behaviour
/// that holds for a sixth of a second cannot finish anything it was chosen for, and the visible
/// result is a companion that looks like it is choosing badly when it is mostly choosing again.
///
/// Three things separate a real finding here from ordinary responsiveness, and all three are in
/// the numbers rather than in a feeling about them. A short run is only interesting when the
/// behaviour that lost comes straight back, because "picked something, changed mind, changed back"
/// is churn while "picked something, finished, moved on" is the system working. A narrow margin
/// between the winner and the runner-up is what makes the flip cheap. And the incumbency bonus is
/// supposed to be exactly what stops a near-tie oscillating, so a flip that happens anyway is
/// evidence about the bonus rather than about the scores.
///
/// The thresholds are deliberately loose. This is built to catch a brain re-electing itself three
/// times a second, not to police a companion that switched twice in a minute, and a session that
/// is genuinely settled should produce nothing at all from it.
/// </summary>
public sealed class DecisionsSurviveLongEnoughToPayOff : ICheck
{
    /// <summary>Under this, the behaviour cannot have completed anything with travel in it.</summary>
    private const int ShortRunTicks = 30;

    /// <summary>A switch every this-many ticks or faster, sustained, is a brain that is re-electing rather than deciding.</summary>
    private const float ChurnTicksPerSwitch = 90f;

    /// <summary>Below this share of short runs the pattern is ordinary responsiveness.</summary>
    private const float ShortRunShareForFinding = .35f;

    /// <summary>A flip back to the behaviour just abandoned, within this many ticks, is oscillation rather than sequencing.</summary>
    private const int ReturnWindowTicks = 180;

    private const string CheckName = "did a chosen behaviour last long enough to accomplish anything";
    public string Name => CheckName;
    public string[] Needs => new[] { "tick", "action" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column action = session["action"], tick = session["tick"];
        if (session.Count < 2) yield break;

        var runs = new List<(string Name, int Start, int End)>();
        int start = 0;
        for (int i = 1; i < session.Count; i++)
        {
            if (action.Text[i] == action.Text[start]) continue;
            runs.Add((action.Text[start], start, i - 1));
            start = i;
        }
        runs.Add((action.Text[start], start, session.Count - 1));
        if (runs.Count < 2) yield break;

        int switches = runs.Count - 1;
        int shortRuns = 0, returns = 0;
        var heldTicks = new Dictionary<string, (int Runs, long Ticks)>();
        for (int r = 0; r < runs.Count; r++)
        {
            int length = runs[r].End - runs[r].Start + 1;
            if (length <= ShortRunTicks) shortRuns++;
            heldTicks.TryGetValue(runs[r].Name, out (int Runs, long Ticks) held);
            heldTicks[runs[r].Name] = (held.Runs + 1, held.Ticks + length);
            // A behaviour that comes straight back is the shape this check exists for: the
            // decision was reversed and then reversed again, so neither reversal was warranted.
            if (r >= 2 && runs[r].Name == runs[r - 2].Name
                && runs[r].Start - runs[r - 2].End <= ReturnWindowTicks) returns++;
        }

        int first = (int)tick.Number[0], last = (int)tick.Number[session.Count - 1];
        float ticksPerSwitch = (float)session.Count / switches;
        float shortShare = (float)shortRuns / runs.Count;
        if (ticksPerSwitch > ChurnTicksPerSwitch && shortShare < ShortRunShareForFinding)
        {
            yield return new Finding(Severity.Oddity, CheckName,
                $"the chosen behaviour changed {switches} time(s), holding {ticksPerSwitch:0.0} ticks on average",
                Detail(runs.Count, shortRuns, shortShare, returns, ticksPerSwitch, heldTicks)
                    + " This is the settled baseline, not a finding: decisions are lasting long enough to be acted on.",
                first, last, session.Count);
            yield break;
        }

        yield return new Finding(Severity.Potential, CheckName,
            $"the chosen behaviour changed every {ticksPerSwitch:0.0} ticks ({ticksPerSwitch / 60f:0.00}s), {switches} times over the session",
            Detail(runs.Count, shortRuns, shortShare, returns, ticksPerSwitch, heldTicks)
                + $" A behaviour under {ShortRunTicks} ticks cannot have completed anything that needed travel, so a run of them is the brain"
                + " re-electing rather than deciding, and every destination abandoned mid-approach is charged to it."
                + " What would settle it: read the per-tick scores of the winner and the runner-up across one of these"
                + " flips. A margin near zero means the incumbency bonus is failing to hold a near-tie, which is the"
                + " one job it has; a wide margin means a scorer is genuinely oscillating and the flip is honest.",
            first, last, session.Count);
    }

    private static string Detail(int runs, int shortRuns, float shortShare, int returns, float ticksPerSwitch,
        Dictionary<string, (int Runs, long Ticks)> heldTicks)
    {
        var names = new List<string>(heldTicks.Keys);
        names.Sort((a, b) => (heldTicks[a].Ticks / heldTicks[a].Runs).CompareTo(heldTicks[b].Ticks / heldTicks[b].Runs));
        var parts = new List<string>();
        foreach (string name in names)
        {
            (int Runs, long Ticks) held = heldTicks[name];
            parts.Add(FormattableString.Invariant($"{name} {held.Runs}x mean {(float)held.Ticks / held.Runs:0.0}"));
        }
        string means = string.Join(", ", parts);
        return FormattableString.Invariant($"{runs} run(s) of a behaviour, {shortRuns} of them {ShortRunTicks} ticks or shorter ({shortShare * 100:0}%), one switch every {ticksPerSwitch:0.0} ticks. {returns} run(s) returned to the behaviour abandoned immediately before the last one, within {ReturnWindowTicks} ticks, which is a reversal that was itself reversed. Mean ticks held, shortest first: {means}.");
    }
}
