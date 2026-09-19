#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>One ordered transport with separately reserved capacities. Prioritising admission
/// must not reorder native events around the decisions which consumed them.</summary>
public static class QueueDiagnosticRecords
{
    internal enum Kind { Tsv, LegacyEvent, Course, Gap }
    internal sealed record Record(Kind Kind, string? Tsv, object? Event, bool Required, long Bytes);
    public readonly record struct Counts(long Offered, long Enqueued, long Dropped, long Written);
    private static readonly object gate = new();
    private static readonly Queue<Record> queue = new();
    private static readonly AutoResetEvent signal = new(false);
    private static readonly long[] bytes = new long[3];
    private static readonly Counts[] counts = new Counts[4];
    private static bool accepting, incomplete;
    private static string failure = "";
    internal const int RequiredByteLimit = 1_048_576, OptionalByteLimit = 2_097_152, GapByteLimit = 131_072;
    public static bool Incomplete { get { lock (gate) return incomplete; } }
    public static string Failure { get { lock (gate) return failure; } }
    public static long Offered { get { lock (gate) return counts.Sum(c => c.Offered); } }
    public static long Enqueued { get { lock (gate) return counts.Sum(c => c.Enqueued); } }
    public static long Dropped { get { lock (gate) return counts.Sum(c => c.Dropped); } }
    public static long Written { get { lock (gate) return counts.Sum(c => c.Written); } }
    public static IReadOnlyDictionary<string, Counts> Accounting
    {
        get { lock (gate) return Enum.GetValues<Kind>().ToDictionary(k => k.ToString(), k => counts[(int)k]); }
    }

    // Only the stream owner opens a generation after the previous worker has terminated.
    internal static void Begin()
    {
        lock (gate)
        {
            queue.Clear(); Array.Clear(bytes); Array.Clear(counts);
            failure = ""; incomplete = false; accepting = true; signal.Reset();
        }
    }
    internal static void Seal() { lock (gate) accepting = false; signal.Set(); }
    internal static void Fail(string reason)
    { lock (gate) { incomplete = true; if (failure.Length == 0) failure = reason; } }
    internal static WaitHandle Signal => signal;
    internal static bool TryEnqueueTsv(string line)
        => Enqueue(new(Kind.Tsv, line, null, true, 64L + line.Length * 2L));
    internal static bool TryEnqueueLegacy(object value, long size)
        => Enqueue(new(Kind.LegacyEvent, null, value, true, Math.Max(64L, size)));
    internal static long EstimateLegacy(string kind, string related, string label, string channel, string detail)
        => 256 + Text(kind, related, label, channel, detail);
    internal static bool TryEnqueueCourse(object envelope, bool required, long size)
        => Enqueue(new(Kind.Course, null, envelope, required, Math.Max(64L, size)));

    internal static long Estimate(CourseTraceContext context, CourseTracePayload payload, CourseDecisionSnapshot? snapshot)
    {
        // Charge retained UTF-16 data and object overhead, rather than pretending a text
        // value takes a fixed 64 bytes. Encoding happens only on the worker.
        long total = 512 + Text(context.NativePhase, context.Producer, context.WorldEpoch, context.SourceRevision,
            context.PolicyFingerprint, context.ConfigurationFingerprint, payload.Kind);
        foreach (var field in payload.Fields) total += 96 + Text(field.Key, field.Value.Kind, field.Value.Text);
        if (snapshot != null)
        {
            total += 256 + Text(snapshot.InputDigest, snapshot.ModelFingerprint, snapshot.SchedulerState, snapshot.RandomState);
            foreach (var read in snapshot.ExpectedReads.Concat(snapshot.CapturedReads))
                total += 96 + Text(read.Key, read.Digest, read.Status, read.Value ?? "");
            foreach (var key in snapshot.MissingOrEvictedKeys) total += 32 + Text(key);
        }
        return total;
    }
    private static long Text(params string[] values) => values.Sum(v => 24L + (v?.Length ?? 0) * 2L);
    private static int Pool(Record record) => record.Kind == Kind.Gap ? 2 : record.Required ? 0 : 1;

    private static bool Enqueue(Record record)
    {
        lock (gate)
        {
            int kind = (int)record.Kind, pool = Pool(record);
            var count = counts[kind]; counts[kind] = count with { Offered = count.Offered + 1 };
            long limit = pool == 0 ? RequiredByteLimit : pool == 1 ? OptionalByteLimit : GapByteLimit;
            if (!accepting || record.Bytes > limit - bytes[pool])
            {
                counts[kind] = counts[kind] with { Dropped = counts[kind].Dropped + 1 };
                incomplete = true;
                if (failure.Length == 0) failure = !accepting ? "record-after-seal" : "diagnostic-capacity-exhausted";
                if (accepting) EnqueueGap(record.Kind);
                return false;
            }
            bytes[pool] += record.Bytes;
            counts[kind] = counts[kind] with { Enqueued = counts[kind].Enqueued + 1 };
            queue.Enqueue(record); signal.Set(); return true;
        }
    }
    private static void EnqueueGap(Kind lostKind)
    {
        string line = $"# diagnostic-gap={lostKind};offered={counts[(int)lostKind].Offered};dropped={counts[(int)lostKind].Dropped}";
        var gap = new Record(Kind.Gap, line, null, false, 64L + line.Length * 2L);
        int kind = (int)Kind.Gap;
        counts[kind] = counts[kind] with { Offered = counts[kind].Offered + 1 };
        if (gap.Bytes > GapByteLimit - bytes[2])
        { counts[kind] = counts[kind] with { Dropped = counts[kind].Dropped + 1 }; return; }
        bytes[2] += gap.Bytes;
        counts[kind] = counts[kind] with { Enqueued = counts[kind].Enqueued + 1 };
        queue.Enqueue(gap); signal.Set();
    }
    internal static bool TryDequeue(out Record record)
    {
        lock (gate)
        {
            if (!queue.TryDequeue(out record!)) return false;
            return true;
        }
    }
    internal static void Complete(Record record, bool written, string? reason = null)
    { lock (gate) { bytes[Pool(record)] -= record.Bytes; int kind = (int)record.Kind; counts[kind] = written ? counts[kind] with { Written = counts[kind].Written + 1 } : counts[kind] with { Dropped = counts[kind].Dropped + 1 }; if (!written) { incomplete = true; if (failure.Length == 0) failure = reason ?? "diagnostic-write-failed"; } } }
}
