#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// The report's "where the time goes" block: which sections of the brain the capture's time went to, what it
/// allocated and collected, and the ticks that stood out with the sections that dominated them.
///
/// <para>It is a reading and never a verdict, the same as the frame block above it. The numbers are
/// <see cref="MeasureWhereTheTimeGoes"/>'s, read from the same profile so the report and the ledger cannot disagree;
/// what this adds is the spike ticks themselves, from the <c>cost-spike</c> occurrences in the events sidecar, each
/// with the largest self times of its whole section tree — the one place a tick's complete tree is kept.</para>
///
/// <para>A capture older than schema 0.48.0 prints one line saying the profile is unrecorded and which schema it
/// declares, because an absent block and a block of zeros read the same to anyone skimming.</para>
/// </summary>
public static class DescribeWhereTheTimeGoes
{
    /// <summary>How many sections the block lists by share, and how many of a spike's tree it names.</summary>
    private const int SectionsListed = 12, SpikeSectionsNamed = 4, SpikesListed = 8;

    public static string Of(Session session, string path)
    {
        var text = new StringBuilder();
        text.AppendLine();
        var measure = new MeasureWhereTheTimeGoes();
        string[] missing = measure.Needs.Where(name => !session.Has(name)).ToArray();
        string? absent = missing.Length > 0 ? string.Join(", ", missing) : measure.Missing(session);
        if (absent != null)
        {
            text.AppendLine($"where the time goes  unrecorded — the file has no {absent}");
            return text.ToString();
        }
        var profile = MeasureWhereTheTimeGoes.Read(session);
        if (profile.BrainRows == 0)
        {
            text.AppendLine("where the time goes  the capture carries the section profile and no brain tick wrote a section into it");
            return text.ToString();
        }
        var brain = Enumerable.Range(0, session.Count).Where(i => session["brain_fresh"].Number[i] == 1)
            .Select(i => (double)session["brain_ms"].Number[i]).Where(v => !double.IsNaN(v)).ToList();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"where the time goes  {profile.BrainRows:n0} brain tick(s), brain p50 {ReadPlay.FloorRank(brain, 0.5):0.00} ms, p99 {ReadPlay.FloorRank(brain, 0.99):0.00} ms; self time by section, largest share of the brain first"));
        foreach ((string sectionPath, double share) in MeasureWhereTheTimeGoes.ByShare(profile).Take(SectionsListed))
        {
            var values = profile.SelfPerRow[sectionPath].ToList();
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {100.0 * share,5:0.0}%  {sectionPath,-58} p50 {ReadPlay.FloorRank(values, 0.5),6:0.000}  p95 {ReadPlay.FloorRank(values, 0.95),6:0.000}  p99 {ReadPlay.FloorRank(values, 0.99),6:0.000}  max {values.Max(),7:0.000} ms"));
        }
        int more = MeasureWhereTheTimeGoes.ByShare(profile).Count() - SectionsListed;
        if (more > 0) text.AppendLine($"  … {more} more section(s) below these");
        if (profile.BrainTotal > 0)
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {100.0 * profile.OtherTotal / profile.BrainTotal:0.0}% of the brain's time was profiled outside each tick's eight largest sections, and a tick's sections read as zero where they were not among its eight"));
        var recorder = profile.SelfPerRow.Where(pair => MeasureWhereTheTimeGoes.IsRecorder(pair.Key))
            .Select(pair => (pair.Key, pair.Value.Sum())).OrderByDescending(pair => pair.Item2).ToList();
        if (recorder.Count > 0 && profile.RecordTotal > 0)
            text.AppendLine("  the recorder's own time, against record_ms (the previous row's): "
                + string.Join(", ", recorder.Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key} {100.0 * pair.Item2 / profile.RecordTotal:0.0}%"))));

        var brainAlloc = profile.BrainAlloc.ToList();
        var tickAlloc = profile.TickAlloc.ToList();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  allocation  brain p50 {Kilobytes(ReadPlay.FloorRank(brainAlloc, 0.5))}, p99 {Kilobytes(ReadPlay.FloorRank(brainAlloc, 0.99))} a tick; update thread p50 {Kilobytes(ReadPlay.FloorRank(tickAlloc, 0.5))}, p99 {Kilobytes(ReadPlay.FloorRank(tickAlloc, 0.99))} between rows"));
        double brainAllocTotal = profile.BrainAlloc.Sum();
        var allocators = profile.AllocatedBySection.Where(pair => !MeasureWhereTheTimeGoes.IsRecorder(pair.Key))
            .OrderByDescending(pair => pair.Value).Take(5).ToList();
        if (allocators.Count > 0 && brainAllocTotal > 0)
            text.AppendLine("  allocated most by (outside their children, a lower bound from each tick's four largest): "
                + string.Join(", ", allocators.Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key} {100.0 * pair.Value / brainAllocTotal:0.0}%"))));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  collections  gen0 {PerMinute(profile.Collections[0], profile.Minutes)}, gen1 {PerMinute(profile.Collections[1], profile.Minutes)}, gen2 {PerMinute(profile.Collections[2], profile.Minutes)} a minute over {profile.Minutes:0.0} minute(s)"));

        // The profiler's two failure counts, from the closing line: a section past the node or depth cap has its time
        // left in its parent's self time, and a scope left open nests the next tick's tree under it. Either makes the
        // shares above misattribute, so a nonzero count is printed; a capture that never closed says so.
        if (ClosingCount(session, "profiler-overflowed") is { } overflowed && ClosingCount(session, "profiler-unbalanced") is { } unbalanced)
        {
            if (overflowed > 0 || unbalanced > 0)
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  profiler  {overflowed:n0} section(s) past the profiler's capacity, their time left in the parent; {unbalanced:n0} scope(s) left open and closed by the tick's end — the shares above misattribute that much"));
        }
        else text.AppendLine("  profiler  the capture carries no profiler-overflowed count on a closing line, so whether any section overflowed is unknown");

        var log = ReadGodsEyeEvents.Read(path);
        var dumps = log.Events.Where(e => e.kind == "cost-spike").ToList();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  spikes  {profile.SpikeRows:n0} brain tick(s) above the fence the recorder drew from the session's own previous 600 ticks; {(log.Present ? $"{dumps.Count:n0} cost-spike occurrence(s) in the sidecar, one per sixty-tick window, the worst of each" : "no events sidecar beside the capture, so no spike's whole tree")}"));
        foreach (var dump in dumps.OrderByDescending(e => Number(e.Field("brain-ms"))).Take(SpikesListed))
        {
            string tree = dump.Field("tree") ?? "-";
            var largest = TreeEntries(tree).OrderByDescending(entry => entry.Self).Take(SpikeSectionsNamed)
                .Select(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Path} {entry.Self:0.00}"));
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"    tick {dump.Field("tick") ?? "?"}  {Number(dump.Field("brain-ms")):0.00} ms against a {Number(dump.Field("fence-ms")):0.00} ms fence ({dump.Field("spikes-in-window") ?? "?"} in its window): {string.Join(", ", largest)} ms self"));
        }
        if (profile.SpikeRows > 0)
        {
            text.AppendLine("  dominating the spike ticks, as a share of the brain's time on them against the share on ordinary ticks:");
            foreach ((string sectionPath, double spikeSelf) in profile.SpikeSelf.Where(pair => !MeasureWhereTheTimeGoes.IsRecorder(pair.Key)).OrderByDescending(pair => pair.Value).Take(5))
            {
                double ordinary = profile.OrdinaryBrainTotal > 0 ? 100.0 * profile.OrdinarySelf.GetValueOrDefault(sectionPath) / profile.OrdinaryBrainTotal : 0;
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"    {100.0 * spikeSelf / profile.SpikeBrainTotal,5:0.0}% against {ordinary,5:0.0}%  {sectionPath}"));
            }
        }
        return text.ToString();
    }

    /// <summary>One integer from the `# closing=` line, or null where the capture never closed or predates the key.</summary>
    private static long? ClosingCount(Session session, string key)
    {
        if (!session.Metadata.TryGetValue("closing", out string? closing)) return null;
        foreach (string part in closing.Split(';'))
            if (part.StartsWith(key + "=", StringComparison.Ordinal)
                && long.TryParse(part[(key.Length + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                return value;
        return null;
    }

    /// <summary>The whole-tree entries of one <c>cost-spike</c> occurrence: <c>path=inclusive/self/calls/bytes</c>
    /// joined by <c>|</c>.</summary>
    internal static IEnumerable<(string Path, double Inclusive, double Self)> TreeEntries(string tree)
    {
        if (tree.Length == 0 || tree == "-") yield break;
        foreach (string entry in tree.Split('|'))
        {
            int equals = entry.LastIndexOf('=');
            if (equals <= 0) continue;
            string[] parts = entry[(equals + 1)..].Split('/');
            if (parts.Length >= 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double inclusive)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double self))
                yield return (entry[..equals], inclusive, self);
        }
    }

    private static double Number(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

    private static string Kilobytes(double bytes) => double.IsNaN(bytes) ? "-" : string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.0} KB");

    private static string PerMinute(long count, double minutes) => minutes > 0 ? string.Create(CultureInfo.InvariantCulture, $"{count / minutes:0.0}") : "-";
}
