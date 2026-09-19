#nullable enable

using AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>Focused fixture entry points for the ordered, bounded diagnostics transport.</summary>
internal static class VerifyDiagnosticTransport
{
    public static int NormalClosureWritesCompleteTerminalFooter() => Exercise("normal", writer =>
    {
        QueueDiagnosticRecords.TryEnqueueTsv("normal-row");
        return writer.FlushForReader(TimeSpan.FromSeconds(1));
    });
    public static int RequiredAndOptionalRecordsPreserveOfferOrder() => Exercise("order", writer =>
    {
        QueueDiagnosticRecords.TryEnqueueTsv("first");
        QueueDiagnosticRecords.TryEnqueueCourse(new { kind = "optional" }, false, 64);
        QueueDiagnosticRecords.TryEnqueueTsv("last");
        return writer.FlushForReader(TimeSpan.FromSeconds(1));
    });

    public static int LargeStringOverflowStaysSticky() => Exercise("overflow", writer =>
    {
        bool accepted = QueueDiagnosticRecords.TryEnqueueTsv(new string('x', QueueDiagnosticRecords.RequiredByteLimit));
        return !accepted && QueueDiagnosticRecords.Incomplete && QueueDiagnosticRecords.Dropped == 1;
    });

    public static int ClosingWriterRejectsSameGenerationReopen() => Exercise("close", writer =>
    {
        try { FlushDiagnosticRecords.Start("/tmp/aic-unreachable.tsv", "/tmp/aic-unreachable.jsonl"); return false; }
        catch (InvalidOperationException) { return true; }
    });

    public static int HeldWriteTimeoutStaysIncompleteAfterRelease()
    {
        string stem = Path.Combine(Path.GetTempPath(), "aic-held-" + Guid.NewGuid().ToString("N"));
        string tsv = stem + ".tsv", events = stem + ".jsonl";
        using var held = new HoldSink("# end=");
        try
        {
            File.WriteAllText(tsv, "");
            using var writer = FlushDiagnosticRecords.Start(tsv, events, (_, _, _, _) => held);
            QueueDiagnosticRecords.TryEnqueueTsv("held-row");
            // Hold the terminal write itself: holding an earlier row cannot expose a
            // clean footer computed just before the deadline and written afterwards.
            var stopping = Task.Run(() => writer.Stop(TimeSpan.FromMilliseconds(100), "held-timeout"));
            if (!held.Entered.Wait(TimeSpan.FromSeconds(1))) throw new InvalidOperationException("Terminal write was never reached.");
            if (stopping.GetAwaiter().GetResult()) return 1;
            held.Release.Set();
            if (!writer.Stop(TimeSpan.FromSeconds(1), "held-timeout")) return 1;
            return held.Lines.Last(line => line.StartsWith("# end=", StringComparison.Ordinal))
                .Contains("diagnostics-incomplete=True", StringComparison.Ordinal) ? 0 : 1;
        }
        finally { held.Release.Set(); if (File.Exists(tsv)) File.Delete(tsv); if (File.Exists(events)) File.Delete(events); }
    }

    public static int InjectedWriteFailureStaysSticky()
    {
        string stem = Path.Combine(Path.GetTempPath(), "aic-fault-" + Guid.NewGuid().ToString("N")); string tsv = stem + ".tsv", events = stem + ".jsonl";
        try
        {
            File.WriteAllText(tsv, "");
            using var writer = FlushDiagnosticRecords.Start(tsv, events, (_, _, _, _) => new ThrowSink());
            QueueDiagnosticRecords.TryEnqueueTsv("fault"); writer.Stop(TimeSpan.FromSeconds(1), "fault");
            return QueueDiagnosticRecords.Incomplete && QueueDiagnosticRecords.Failure.Contains("diagnostic-writer", StringComparison.Ordinal) ? 0 : 1;
        }
        finally { if (File.Exists(tsv)) File.Delete(tsv); if (File.Exists(events)) File.Delete(events); }
    }

    public static int InFlightBytesAndLegacyStringsAreCharged()
    {
        using var held = new HoldSink("held-row");
        using var writer = FlushDiagnosticRecords.Start("unused-tsv", "unused-events", (_, _, _, _) => held);
        string row = "held-row" + new string('x', 249_992);
        if (!QueueDiagnosticRecords.TryEnqueueTsv(row) || !held.Entered.Wait(TimeSpan.FromSeconds(1))) return 1;
        string large = new('y', 150_000);
        long reserved = QueueDiagnosticRecords.EstimateLegacy("native", large, large, "", "");
        bool refused = !QueueDiagnosticRecords.TryEnqueueLegacy(new { kind = "native", related = large, label = large }, reserved);
        held.Release.Set();
        if (!writer.Stop(TimeSpan.FromSeconds(1), "accounting")) return 1;
        return refused && QueueDiagnosticRecords.Incomplete ? 0 : 1;
    }

    private sealed class HoldSink(string blockedPrefix) : IDiagnosticSink
    {
        public readonly ManualResetEventSlim Release = new(false);
        public readonly ManualResetEventSlim Entered = new(false);
        public readonly List<string> Lines = new();
        public void WriteLine(string line)
        {
            if (line.StartsWith(blockedPrefix, StringComparison.Ordinal)) { Entered.Set(); Release.Wait(); }
            lock (Lines) Lines.Add(line);
        }
        public void Flush() { }
        // Both stream slots may share this fixture sink. Test-owned events outlive both
        // worker disposals and are released by the test after Stop has joined the worker.
        public void Dispose() { }
    }
    private sealed class ThrowSink : IDiagnosticSink
    { public void WriteLine(string line) => throw new IOException("fixture failure"); public void Flush() { } public void Dispose() { } }

    private static int Exercise(string name, Func<FlushDiagnosticRecords, bool> assertion)
    {
        string stem = Path.Combine(Path.GetTempPath(), "aic-diagnostic-" + Guid.NewGuid().ToString("N"));
        string tsv = stem + ".tsv", events = stem + ".jsonl";
        try
        {
            File.WriteAllText(tsv, "# schema=fixture\n");
            using var writer = FlushDiagnosticRecords.Start(tsv, events);
            bool ok = assertion(writer);
            bool stopped = writer.Stop(TimeSpan.FromSeconds(1), "fixture-complete", 1);
            bool footer = File.ReadAllText(tsv).Contains("# end=", StringComparison.Ordinal);
            if (!ok || !footer || !stopped) throw new InvalidOperationException($"Diagnostic transport {name}: condition={ok}, terminal={footer}, stopped={stopped}, failure={QueueDiagnosticRecords.Failure}");
            if (name == "normal" && (!File.ReadAllText(tsv).Contains("# end=fixture-complete;rows=1;diagnostics-incomplete=False", StringComparison.Ordinal)
                || !File.ReadAllText(tsv).StartsWith("# schema=fixture\n", StringComparison.Ordinal)))
                throw new InvalidOperationException("Normal closure lost its existing header or row count, or reported incomplete.");
            if (name == "order")
            {
                var lines = File.ReadAllLines(tsv);
                if (Array.IndexOf(lines, "first") >= Array.IndexOf(lines, "last") || !File.ReadAllText(events).Contains("optional"))
                    throw new InvalidOperationException("The queued records did not reach their stream in order.");
            }
            return 0;
        }
        finally { if (File.Exists(tsv)) File.Delete(tsv); if (File.Exists(events)) File.Delete(events); }
    }
}
