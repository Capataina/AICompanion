#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// The shape of the run, printed above the findings, because a finding means nothing without the
/// session it sits in: five damage events is a disaster in ten seconds and a good afternoon in ten
/// minutes. It is deliberately all measurement and no judgement — every sentence here is a count,
/// and the reading of those counts is the checks' job.
/// </summary>
public static class DescribeSession
{
    public static string Of(Session session)
    {
        var sb = new StringBuilder();
        int first = session.Tick(0), last = session.Tick(session.Count - 1);
        int span = Math.Max(0, last - first);
        sb.Append($"file      {Path.GetFileName(session.Path)}\n");
        sb.Append($"rows      {session.Count:n0} over ticks {first:n0}..{last:n0}");
        sb.Append($"  ({span / 3600}m {span % 3600 / 60}s of play at sixty a tick)\n");
        sb.Append($"columns   {session.Names.Count}");
        if (session.Ragged > 0)
            sb.Append($", {session.Ragged} ragged row(s) dropped");
        sb.Append('\n');

        var whole = new Stretch(0, session.Count - 1);

        if (session.Has("action"))
        {
            var tally = FindStretches.Tally(session["action"], whole);
            sb.Append("actions   ");
            var parts = new List<string>();
            for (int i = 0; i < tally.Count && i < 6; i++)
                parts.Add($"{tally[i].Key} {100f * tally[i].Value / session.Count:0.0}%");
            sb.Append(string.Join("  ", parts)).Append('\n');
        }

        if (session.Has("npc_tile", "player_tile"))
        {
            Column distance = TheCompanionStaysUp.DistanceColumn(session);
            sb.Append($"distance  mean {FindStretches.Mean(distance, whole):0.0} tiles from him, worst {FindStretches.Max(distance, whole):0.0}\n");
        }

        if (session.Has("life"))
        {
            Column life = session["life"];
            int events = 0;
            float lost = 0f, low = float.MaxValue;
            for (int i = 0; i < session.Count; i++)
            {
                if (!float.IsNaN(life.Number[i]))
                    low = MathF.Min(low, life.Number[i]);
                if (i > 0 && !float.IsNaN(life.Number[i]) && !float.IsNaN(life.Number[i - 1]) && life.Number[i] < life.Number[i - 1])
                {
                    events++;
                    lost += life.Number[i - 1] - life.Number[i];
                }
            }
            sb.Append($"life      {events} damage event(s), {lost:0} life lost, lowest {(low == float.MaxValue ? 0f : low):0}\n");
        }

        if (session.Has("state"))
        {
            Column state = session["state"];
            int downs = 0, rowsDown = 0;
            for (int i = 0; i < session.Count; i++)
            {
                if (state.Text[i] != "downed")
                    continue;
                rowsDown++;
                if (i == 0 || state.Text[i - 1] != "downed")
                    downs++;
            }
            // Counted as transitions, because the ticks are time on the floor: 613 rows downed is
            // one down of ten seconds, and reading the rows as the count said "downed 613 times".
            sb.Append($"downed    {downs} time(s), {rowsDown} tick(s) on the floor\n");
        }

        if (session.Has("fire"))
        {
            var tally = FindStretches.Tally(session["fire"], whole);
            var parts = new List<string>();
            foreach (var pair in tally)
                parts.Add($"{pair.Key} {100f * pair.Value / session.Count:0.0}%");
            sb.Append("hands     ").Append(string.Join("  ", parts)).Append('\n');
        }

        if (session.Has("brain_ms"))
            sb.Append($"cost      brain {FindStretches.Mean(session["brain_ms"], whole):0.00} ms a tick mean, {FindStretches.Max(session["brain_ms"], whole):0.00} peak, against a 16.67 ms frame\n");

        string plans = PlansFileFor(session.Path);
        if (File.Exists(plans))
            sb.Append($"plans     {ReadPlanDumps.Describe(plans)}\n");
        else
            sb.Append("plans     no windows file beside this session, so no failed plan or detected scenario was dumped\n");

        return sb.ToString();
    }

    /// <summary>The tile-window dump the mod writes beside the session, named from the same stamp.</summary>
    public static string PlansFileFor(string tsv)
    {
        string directory = Path.GetDirectoryName(tsv) ?? ".";
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(tsv) + "-plans.txt");
    }
}

/// <summary>
/// The sibling windows file, read only for its headers. Every window carries a reason — a failed
/// plan, or one of the seven in-game detectors that turn a playtest failure into a replayable
/// block — and the tally of those reasons is the fastest statement of what went wrong in a run.
/// The blocks themselves belong to the replay tool, so this counts them and names the command.
/// </summary>
public static class ReadPlanDumps
{
    public static string Describe(string path)
    {
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        int windows = 0;
        foreach (string line in File.ReadLines(path))
        {
            if (!line.StartsWith("tick ", StringComparison.Ordinal))
                continue;
            windows++;
            // "tick 1234 stuck, body still for 120 ticks with a path: start x,y goal x,y ..." — the
            // reason runs from the space after the tick to the colon, and the detectors put their
            // own numbers after a comma inside it, so the tally keys on the part before that comma
            // or every window is its own category.
            int colon = line.IndexOf(':');
            int space = line.IndexOf(' ', 5);
            string reason = colon > space && space > 0 ? line[(space + 1)..colon] : "unnamed";
            int comma = reason.IndexOf(',');
            if (comma > 0)
                reason = reason[..comma];
            reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
        }
        if (windows == 0)
            return "the windows file is present and holds none";

        var sb = new StringBuilder($"{windows} window(s)");
        foreach (var pair in reasons)
            sb.Append($"\n            {pair.Key} ×{pair.Value}");
        sb.Append($"\n          replay with  dotnet run --project Tools/NavReplay -- {path}");
        return sb.ToString();
    }
}
