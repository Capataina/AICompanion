#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    /// <summary>Where a capture's code came from and whether it closed, in one line shared by this summary and the HTML coverage.</summary>
    internal static string CaptureStatement(Session session)
    {
        if (!CompletedTransferClaimsWereReceived.SchemaAtLeast(session, new Version(0, 28, 0)))
            return "source revision and closure unrecorded (both written from schema 0.28.0)";
        string source = session.Metadata.TryGetValue("source_revision", out string? revision)
            ? "source " + revision.Replace(";tree=", " tree ", StringComparison.Ordinal) : "source revision unrecorded";
        string closure = session.Metadata.TryGetValue("end", out string? end) ? "closed " + end : "no end marker: interrupted capture";
        return $"{source}; {closure}";
    }

    /// <summary>What recording cost and what the capture did not keep, from the running totals schema 0.29.0 writes on every
    /// row, in one line shared by this summary and the HTML coverage. The percentiles take the value at the floor of the
    /// rank, the definition MeasureBrainCost prints, so the two instruments read one distribution the same way.</summary>
    internal static string RecordingStatement(Session session)
    {
        if (!session.Has("record_ms", "events_written", "events_dropped", "events_coalesced", "terrain_evictions"))
            return "cost and loss counts unrecorded (written from schema 0.29.0)";
        if (session.Count == 0)
            return "no rows, so no measured cost";
        float[] costs = session["record_ms"].Number.Where(value => !float.IsNaN(value)).OrderBy(value => value).ToArray();
        string Percentile(double p) => costs[Math.Min(costs.Length - 1, (int)Math.Floor(p * (costs.Length - 1)))].ToString("0.000", CultureInfo.InvariantCulture);
        string cost = costs.Length == 0 ? "no measured row"
            : $"{Percentile(.5)} p50, {Percentile(.95)} p95, {costs[^1].ToString("0.000", CultureInfo.InvariantCulture)} max ms per row over {costs.Length} measured row(s)";
        int last = session.Count - 1;
        string Total(string column) => session[column].Text[last];
        return $"{cost}; by the last row {Total("events_written")} occurrence(s) written, {Total("events_dropped")} dropped, "
            + $"{Total("events_coalesced")} contact(s) coalesced, {Total("terrain_evictions")} terrain snapshot(s) evicted";
    }

    public static string Of(Session session)
    {
        var sb = new StringBuilder();
        if (session.Count == 0)
        {
            sb.Append($"file      {Path.GetFileName(session.Path)}\n");
            sb.Append("rows      0; the recorder wrote its header but no samples\n");
            sb.Append($"columns   {session.Names.Count}\n");
            sb.Append($"capture   {CaptureStatement(session)}\n");
            return sb.ToString();
        }
        int first = session.Tick(0), last = session.Tick(session.Count - 1);
        int span = Math.Max(0, last - first);
        sb.Append($"file      {Path.GetFileName(session.Path)}\n");
        sb.Append($"rows      {session.Count:n0} over ticks {first:n0}..{last:n0}");
        sb.Append($"  ({span / 3600}m {span % 3600 / 60}s of play at sixty a tick)\n");
        sb.Append($"columns   {session.Names.Count}");
        if (session.Ragged > 0)
            sb.Append($", {session.Ragged} ragged row(s) dropped");
        sb.Append('\n');
        sb.Append($"capture   {CaptureStatement(session)}\n");
        sb.Append($"recording {RecordingStatement(session)}\n");
        if (session.Metadata.TryGetValue("retention", out string? retention))
            sb.Append($"retention {retention}\n");

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

        // Each activity's candidate funnel: the share of rows on which the candidate that got furthest stopped at each
        // stage, so "why was the lighting never done" opens on a count per stage rather than on a search through offers.
        foreach (string name in session.Names)
        {
            if (!name.EndsWith("_funnel", StringComparison.Ordinal)) continue;
            var tally = FindStretches.Tally(session[name], whole);
            var parts = new List<string>();
            for (int i = 0; i < tally.Count && i < 5; i++)
                parts.Add($"{tally[i].Key} {100f * tally[i].Value / session.Count:0.0}%");
            sb.Append("funnel    ").Append(name[..^"_funnel".Length]).Append(": ").Append(string.Join("  ", parts)).Append('\n');
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
