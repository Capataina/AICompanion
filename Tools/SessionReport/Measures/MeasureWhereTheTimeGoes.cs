#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AICompanion.Tools.Ledger;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Where inside the brain a capture's time and memory went, and which parts of it dominated the ticks that stood out
/// — as numbers and never as a verdict.
///
/// <para>It reads schema 0.48.0's six columns: <c>sections</c> (the section profiler's eight largest self times a
/// tick, <c>path=self-ms/calls</c> joined by <c>|</c>), <c>sections_other_ms</c> (everything else the profiler
/// timed that tick), <c>alloc_sections</c> (the four largest self allocators, <c>path=bytes</c>),
/// <c>brain_alloc_bytes</c>, <c>tick_alloc_bytes</c> and <c>cost_fence_ms</c> (the fence that
/// tick's <c>brain_ms</c> was judged against, computed by the recorder from the session's own previous 600 brain
/// ticks). A capture older than that declines by name rather than reporting zeros, because a zero here would read
/// as a brain that cost nothing.</para>
///
/// <para><b>A section absent from a tick's eight counts as zero on that tick</b>, which understates it by at most
/// that tick's eighth-largest self time — a bound the row's own message states, since the column is a ranking and
/// not the whole tree. The whole tree is in each <c>cost-spike</c> occurrence, which
/// <see cref="DescribeWhereTheTimeGoes"/> prints.</para>
///
/// <para><b>A spike is the recorder's own verdict read back</b> — <c>brain_ms</c> above <c>cost_fence_ms</c> on
/// that row — and never a line drawn here. The question the owner asked is which sections dominate those ticks,
/// so the section rows come in pairs: its share of the time on spike ticks against its share on ordinary ticks.
/// A section whose spike share is several times its ordinary share is where a hitch comes from.</para>
///
/// <para>Every row carries the timed and sampled tags, because every number here is a clock reading or derived from
/// one, and a second capture of the same build draws a different value.</para>
/// </summary>
public sealed class MeasureWhereTheTimeGoes : IMeasure
{
    /// <summary>The schema that first writes the section profile.</summary>
    internal static readonly Version First = new(0, 48, 0);

    /// <summary>How many sections get ledger rows of their own, largest share first. The rest are printed by the
    /// report block and not filed, because a ledger row is a history somebody compares and a section at a tenth of a
    /// percent has no history worth keeping.</summary>
    internal const int SectionsFiled = 12;

    private static readonly string[] Tags = { EmitLedgerRows.TimedTag, EmitLedgerRows.SampledTag };

    public string Name => "time";

    /// <summary>The columns every capture since 0.35.0 carries. The five this measure exists for are checked in
    /// <see cref="Missing"/> behind the schema gate, so an older capture is declined as predating the profile
    /// rather than as a list of absent column names.</summary>
    public string[] Needs => new[] { "tick", "wall_elapsed_ms", "brain_ms", "record_ms", "gc0", "gc1", "gc2" };

    /// <summary>The columns schema 0.48.0 added, all of which this measure reads.</summary>
    internal static readonly string[] ProfileColumns = { "sections", "sections_other_ms", "brain_alloc_bytes", "tick_alloc_bytes", "cost_fence_ms", "alloc_sections" };

    public string? Missing(Session session)
    {
        if (!CompletedTransferClaimsWereReceived.SchemaAtLeast(session, First))
            return $"section profile (written from schema {First}; this capture declares {(session.Metadata.TryGetValue("schema", out string? s) ? s : "no schema")})";
        string[] absent = ProfileColumns.Where(name => !session.Has(name)).ToArray();
        return absent.Length == 0 ? null : $"{string.Join(", ", absent)} (the section profile's columns, which a {First} capture carries)";
    }

    /// <summary>One capture's section profile, read once and shared by the measure and the report block.</summary>
    internal sealed record Profile(
        int BrainRows, int SpikeRows, double BrainTotal, double RecordTotal, double SpikeBrainTotal, double OrdinaryBrainTotal,
        IReadOnlyDictionary<string, double[]> SelfPerRow, IReadOnlyDictionary<string, double> SpikeSelf, IReadOnlyDictionary<string, double> OrdinarySelf,
        double OtherTotal, double[] BrainAlloc, double[] TickAlloc, long[] Collections, double Minutes, IReadOnlyList<int> SpikeTicks,
        IReadOnlyDictionary<string, double> AllocatedBySection);

    /// <summary>
    /// Reads the profile. A brain row is one whose <c>sections</c> cell is not a dash and whose <c>brain_ms</c>
    /// parsed; a spike row is a brain row whose cost is above its own fence.
    /// </summary>
    internal static Profile Read(Session session)
    {
        Column sections = session["sections"], other = session["sections_other_ms"], brain = session["brain_ms"], record = session["record_ms"];
        Column fence = session["cost_fence_ms"], brainAlloc = session["brain_alloc_bytes"], tickAlloc = session["tick_alloc_bytes"];
        Column wall = session["wall_elapsed_ms"];
        var brainRows = new List<int>();
        for (int i = 0; i < session.Count; i++)
            if (sections.Text[i] is { Length: > 0 } cell && cell != "-" && double.TryParse(brain.Text[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                brainRows.Add(i);
        var selfPerRow = new Dictionary<string, double[]>(StringComparer.Ordinal);
        var spikeSelf = new Dictionary<string, double>(StringComparer.Ordinal);
        var ordinarySelf = new Dictionary<string, double>(StringComparer.Ordinal);
        var spikeTicks = new List<int>();
        double brainTotal = 0, recordTotal = 0, spikeBrain = 0, ordinaryBrain = 0, otherTotal = 0;
        for (int k = 0; k < brainRows.Count; k++)
        {
            int i = brainRows[k];
            double cost = Parse(brain.Text[i]);
            bool spike = double.TryParse(fence.Text[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double f) && cost > f;
            brainTotal += cost;
            if (double.TryParse(record.Text[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double r)) recordTotal += r;
            otherTotal += Math.Max(0, Parse(other.Text[i]));
            if (spike) { spikeBrain += cost; spikeTicks.Add(session.Tick(i)); } else ordinaryBrain += cost;
            foreach ((string path, double self) in Entries(sections.Text[i]))
            {
                if (!selfPerRow.TryGetValue(path, out double[]? values)) selfPerRow[path] = values = new double[brainRows.Count];
                values[k] += self;
                var into = spike ? spikeSelf : ordinarySelf;
                into[path] = into.GetValueOrDefault(path) + self;
            }
        }
        double[] Bytes(Column column) => brainRows
            .Select(i => double.TryParse(column.Text[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN)
            .Where(v => !double.IsNaN(v)).ToArray();
        long[] collections = new long[3];
        for (int g = 0; g < 3; g++)
        {
            Column gc = session["gc" + g];
            for (int i = 0; i < session.Count; i++) if (gc.Number[i] > 0) collections[g] += (long)gc.Number[i];
        }
        double minutes = session.Count > 1 ? (wall.Number[session.Count - 1] - wall.Number[0]) / 60000.0 : 0;
        // Bytes by section from `alloc_sections`, each tick's four largest self allocators, summed over brain ticks.
        var allocated = new Dictionary<string, double>(StringComparer.Ordinal);
        Column allocators = session["alloc_sections"];
        foreach (int i in brainRows)
            foreach ((string path, double bytes) in Entries(allocators.Text[i]))
                allocated[path] = allocated.GetValueOrDefault(path) + bytes;
        return new Profile(brainRows.Count, spikeTicks.Count, brainTotal, recordTotal, spikeBrain, ordinaryBrain, selfPerRow,
            spikeSelf, ordinarySelf, otherTotal, Bytes(brainAlloc), Bytes(tickAlloc), collections, minutes, spikeTicks, allocated);
    }

    /// <summary>The <c>path=self-ms/calls</c> entries of one <c>sections</c> cell.</summary>
    internal static IEnumerable<(string Path, double Self)> Entries(string cell)
    {
        if (cell.Length == 0 || cell == "-") yield break;
        foreach (string entry in cell.Split('|'))
        {
            int equals = entry.LastIndexOf('=');
            if (equals <= 0) continue;
            string value = entry[(equals + 1)..];
            int slash = value.IndexOf('/');
            if (double.TryParse(slash < 0 ? value : value[..slash], NumberStyles.Float, CultureInfo.InvariantCulture, out double self))
                yield return (entry[..equals], self);
        }
    }

    /// <summary>Whether a path is the recorder's own rather than the brain's: the <c>record</c> subtree, which is
    /// the previous row's recorder and is shared against <c>record_ms</c> rather than <c>brain_ms</c>.</summary>
    internal static bool IsRecorder(string path) => path == "record" || path.StartsWith("record.", StringComparison.Ordinal);

    /// <summary>The brain sections by their share of the brain's time, largest first.</summary>
    internal static IEnumerable<(string Path, double Share)> ByShare(Profile profile)
        => profile.SelfPerRow.Where(pair => !IsRecorder(pair.Key))
            .Select(pair => (pair.Key, profile.BrainTotal > 0 ? pair.Value.Sum() / profile.BrainTotal : 0))
            .OrderByDescending(pair => pair.Item2).ThenBy(pair => pair.Key, StringComparer.Ordinal);

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        Profile profile = Read(session);
        if (profile.BrainRows == 0)
        {
            yield return PlayRow.Skipped(Name + "/sections", "the capture carries the section profile's columns and no brain row with a section in it");
            yield break;
        }
        string population = $"{profile.BrainRows:n0} brain tick(s); a tick where a section was not among the eight largest counts as zero, "
            + "which understates it by at most that tick's eighth-largest self time";
        foreach ((string path, double share) in ByShare(profile).Take(SectionsFiled))
        {
            var values = profile.SelfPerRow[path].ToList();
            yield return Row($"{Name}/share/{path}", 100.0 * share, "%", null,
                $"self time of {path} over the whole brain's time, {population}");
            yield return Row($"{Name}/p50/{path}", ReadPlay.FloorRank(values, 0.50), "ms", "lower-is-better", $"self time a tick, {population}");
            yield return Row($"{Name}/p95/{path}", ReadPlay.FloorRank(values, 0.95), "ms", "lower-is-better", $"self time a tick, {population}");
            yield return Row($"{Name}/p99/{path}", ReadPlay.FloorRank(values, 0.99), "ms", "lower-is-better", $"self time a tick, {population}");
            yield return Row($"{Name}/max/{path}", values.Max(), "ms", "lower-is-better", $"the worst single tick's self time, {population}");
        }
        yield return Row(Name + "/unlisted-share", profile.BrainTotal > 0 ? 100.0 * profile.OtherTotal / profile.BrainTotal : 0, "%", null,
            "the profiled time outside each tick's eight largest sections, over the whole brain's time: how much the per-row ranking leaves to the spike trees");

        var brainAlloc = profile.BrainAlloc.ToList();
        var tickAlloc = profile.TickAlloc.ToList();
        if (brainAlloc.Count > 0)
        {
            yield return Row(Name + "/brain-alloc-p50", ReadPlay.FloorRank(brainAlloc, 0.50), "bytes", "lower-is-better", $"bytes the brain tick allocated, over {brainAlloc.Count:n0} brain tick(s)");
            yield return Row(Name + "/brain-alloc-p99", ReadPlay.FloorRank(brainAlloc, 0.99), "bytes", "lower-is-better", $"as above, the 99th percentile");
        }
        if (tickAlloc.Count > 0)
        {
            yield return Row(Name + "/tick-alloc-p50", ReadPlay.FloorRank(tickAlloc, 0.50), "bytes", "lower-is-better", $"bytes the update thread allocated between two rows, the engine's included, over {tickAlloc.Count:n0} row(s)");
            yield return Row(Name + "/tick-alloc-p99", ReadPlay.FloorRank(tickAlloc, 0.99), "bytes", "lower-is-better", $"as above, the 99th percentile");
        }
        double brainAllocTotal = profile.BrainAlloc.Sum();
        foreach ((string path, double bytes) in profile.AllocatedBySection.Where(pair => !IsRecorder(pair.Key)).OrderByDescending(pair => pair.Value).Take(5))
            yield return Row($"{Name}/alloc-share/{path}", brainAllocTotal > 0 ? 100.0 * bytes / brainAllocTotal : 0, "%", null,
                $"bytes {path} allocated outside its children, over every byte the brain allocated; counted on the ticks it was among the four largest allocators, so a lower bound");
        for (int g = 0; g < 3; g++)
            yield return Row($"{Name}/gc{g}-per-minute", profile.Minutes > 0 ? profile.Collections[g] / profile.Minutes : 0, "per-minute", "lower-is-better",
                $"{profile.Collections[g]:n0} generation-{g} collection(s) over {profile.Minutes:0.0} minute(s) of wall clock, counted for the whole process");

        yield return Row(Name + "/spikes", profile.SpikeRows, "ticks", "lower-is-better",
            $"brain ticks whose cost crossed the fence the recorder computed from the session's own previous 600 brain ticks (upper quartile plus three interquartile ranges, never below twice the median), of {profile.BrainRows:n0}");
        yield return Row(Name + "/spikes-per-minute", profile.Minutes > 0 ? profile.SpikeRows / profile.Minutes : 0, "per-minute", "lower-is-better",
            $"{profile.SpikeRows:n0} spike tick(s) over {profile.Minutes:0.0} minute(s) of wall clock");
        if (profile.SpikeRows == 0) yield break;
        foreach ((string path, double spikeSelf) in profile.SpikeSelf.Where(pair => !IsRecorder(pair.Key)).OrderByDescending(pair => pair.Value).Take(5))
        {
            double ordinary = profile.OrdinaryBrainTotal > 0 ? 100.0 * profile.OrdinarySelf.GetValueOrDefault(path) / profile.OrdinaryBrainTotal : 0;
            yield return Row($"{Name}/spike-share/{path}", 100.0 * spikeSelf / profile.SpikeBrainTotal, "%", null,
                $"of the brain's time on the {profile.SpikeRows:n0} spike tick(s), spent in {path}'s own self time; {ordinary:0.0}% of the time on ordinary ticks");
            yield return Row($"{Name}/ordinary-share/{path}", ordinary, "%", null,
                $"the same section's share of the brain's time on the {profile.BrainRows - profile.SpikeRows:n0} ordinary tick(s), read beside its spike share");
        }
    }

    private static LedgerRow Row(string @case, double value, string unit, string? direction, string message)
        => PlayRow.Count(@case, value, unit, direction, message, Tags);

    private static double Parse(string cell)
        => double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
}
