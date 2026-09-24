#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AICompanion.Tools.Ledger;

/// <summary>
/// A fixed piece of work timed at the start and the end of every run, so the run itself says how fast the
/// machine was while it ran.
///
/// <b>Why it exists.</b> On 23 September 2026 the engine-replay rows of one run were 1.00× the previous run's
/// durations for their first sixteen cases and then a median 4.76× for the remaining 42, a step inside one
/// process, and a timed row went red on it. Establishing that the machine had slowed took an hour: the parent
/// commit had to be built and timed in the same minute to rule the code out. A benchmark taken at both ends
/// answers the same question in the run file, for every run.
///
/// <b>What it is.</b> Sorting a shuffled array, hashing a buffer and allocating short-lived arrays: the three
/// things the suite's cases spend their time on (search, arithmetic, and garbage collection), in one
/// single-threaded workload of fixed size. It is the median of five repetitions after one warm-up, so one
/// preempted repetition does not move the reading. The absolute number means nothing on its own; only its
/// ratio to another reading on the same machine does.
/// </summary>
public static class MachineBenchmark
{
    private const int Repetitions = 5;
    private const int SortLength = 200_000;
    private const int HashBytes = 1 << 20;

    public static double Measure()
    {
        Work();
        var times = new double[Repetitions];
        for (int i = 0; i < Repetitions; i++)
        {
            long start = Stopwatch.GetTimestamp();
            Work();
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        Array.Sort(times);
        return times[Repetitions / 2];
    }

    private static int sink;

    private static void Work()
    {
        uint state = 2463534242;
        var numbers = new int[SortLength];
        for (int i = 0; i < numbers.Length; i++)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            numbers[i] = (int)state;
        }
        Array.Sort(numbers);
        var buffer = new byte[HashBytes];
        for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)(numbers[i % numbers.Length] >> 8);
        byte[] hash = SHA256.HashData(buffer);
        int churn = 0;
        for (int i = 0; i < 2_000; i++) churn += new int[256].Length;
        sink = numbers[SortLength / 2] ^ hash[0] ^ churn;
    }
}

/// <summary>
/// The part of the scoreboard about the conditions a run was taken in rather than the code it tested: how fast
/// the machine was, how long each instrument's cases took against the baseline's, and what the cases cost in
/// memory. It grades nothing. Every figure is a ratio to another reading on the same machine or to the
/// baseline, because the owner ruled on 24 September 2026 that a time is compared against its own history and
/// never against a fixed number.
/// </summary>
public static class MachineReport
{
    /// <summary>How many earlier runs on this machine set the range a reading is placed in.</summary>
    private const int HistoryRuns = 10;

    /// <summary>Rows faster than this are left out of the duration ratio, because a few milliseconds of process
    /// jitter on a case that takes five is a large ratio about nothing.</summary>
    private const double RatioFloorMs = 100;

    public static string Render(Run after, Run? before, IReadOnlyList<Run> store)
    {
        var text = new StringBuilder();
        RenderBenchmark(text, after, before, store);
        if (before != null) RenderDurations(text, after, before);
        RenderMemory(text, after);
        return text.ToString();
    }

    private static void RenderBenchmark(StringBuilder text, Run after, Run? before, IReadOnlyList<Run> store)
    {
        BenchmarkReading? start = after.BenchmarkAt("start"), end = after.BenchmarkAt("end");
        if (start == null)
        {
            text.AppendLine("  machine   no benchmark in this run (it was opened before runs recorded one), so how fast the machine was is unknown");
            return;
        }
        string endText = end == null ? "no end reading"
            : $"{Ms(end.Milliseconds)} at end, {Ratio(end.Milliseconds / start.Milliseconds)} the start";
        text.AppendLine($"  machine   benchmark {Ms(start.Milliseconds)} at start, {endText}");
        double[] history = store
            .Where(run => run.Path != after.Path && run.Header.Machine == after.Header.Machine)
            .Select(run => run.BenchmarkAt("start")?.Milliseconds)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Take(HistoryRuns)
            .ToArray();
        if (history.Length < 3)
            text.AppendLine($"            {history.Length} earlier run(s) on this machine carry a benchmark, too few to place these readings in a range");
        else
        {
            double low = history.Min(), high = history.Max();
            var outside = new List<string>();
            if (start.Milliseconds < low || start.Milliseconds > high) outside.Add("the start");
            if (end != null && (end.Milliseconds < low || end.Milliseconds > high)) outside.Add("the end");
            text.AppendLine(outside.Count == 0
                ? $"            inside this machine's range over its last {history.Length} runs ({Ms(low)}–{Ms(high)})"
                : $"            OUTSIDE this machine's range over its last {history.Length} runs ({Ms(low)}–{Ms(high)}) at {string.Join(" and ", outside)}, so timings in this run were taken on a machine running unlike its usual self");
        }
        if (before?.BenchmarkAt("start") is { } baselineStart)
            text.AppendLine($"            the baseline's start was {Ms(baselineStart.Milliseconds)}, so this run started {Ratio(start.Milliseconds / baselineStart.Milliseconds)} as long");
    }

    private static void RenderDurations(StringBuilder text, Run after, Run before)
    {
        var baseline = before.Rows
            .Where(r => r.DurationMs >= RatioFloorMs && r.Verdict != "skipped")
            .GroupBy(Run.Key)
            .ToDictionary(g => g.Key, g => g.Average(r => r.DurationMs));
        var ratios = after.Rows
            .Where(r => r.DurationMs > 0 && r.Verdict != "skipped" && baseline.ContainsKey(Run.Key(r)))
            .GroupBy(r => r.Instrument)
            .Select(g => (Instrument: g.Key, Ratios: g.Select(r => r.DurationMs / baseline[Run.Key(r)]).OrderBy(x => x).ToArray()))
            .Where(g => g.Ratios.Length > 0)
            .OrderBy(g => g.Instrument, StringComparer.Ordinal)
            .ToArray();
        if (ratios.Length == 0) return;
        text.AppendLine("  durations " + string.Join("; ", ratios.Select(g =>
            $"{g.Instrument} {Ratio(g.Ratios[g.Ratios.Length / 2])} the baseline (median of {g.Ratios.Length} case(s) over {RatioFloorMs:0} ms)")));
        text.AppendLine("            a ratio every case of an instrument shares is the machine; one case far from its instrument's median is that case");
    }

    private static void RenderMemory(StringBuilder text, Run after)
    {
        var withCost = after.Rows.Where(r => r.Cost != null).GroupBy(r => r.Instrument).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        foreach (var group in withCost)
        {
            var rows = group.ToArray();
            double peak = rows.Max(r => r.Cost!.HeapAfterMb);
            int gen2 = rows.Sum(r => r.Cost!.Gen2);
            var heaviest = rows.OrderByDescending(r => r.Cost!.AllocatedMb).Take(3)
                .Select(r => $"{r.Case} ({r.Cost!.AllocatedMb.ToString("0", CultureInfo.InvariantCulture)} MB)");
            text.AppendLine($"  memory    {group.Key}: heap after a case peaked at {peak.ToString("0", CultureInfo.InvariantCulture)} MB, {gen2} gen-2 collection(s) across {rows.Length} case(s); most allocated: {string.Join(", ", heaviest)}");
        }
    }

    private static string Ms(double value) => $"{value.ToString("0.#", CultureInfo.InvariantCulture)} ms";

    private static string Ratio(double value) => $"{value.ToString("0.00", CultureInfo.InvariantCulture)}×";
}
