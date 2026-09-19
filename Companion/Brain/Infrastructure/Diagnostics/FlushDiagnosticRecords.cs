#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

internal interface IDiagnosticSink : IDisposable { void WriteLine(string line); void Flush(); }
internal delegate IDiagnosticSink DiagnosticSinkFactory(string path, FileMode mode, FileAccess access, FileShare share);
internal sealed class StreamDiagnosticSink : IDiagnosticSink
{
    private readonly StreamWriter writer;
    public StreamDiagnosticSink(string path, FileMode mode, FileAccess access, FileShare share)
    {
        var stream = new FileStream(path, mode, access, share);
        if (mode == FileMode.Open) stream.Seek(0, SeekOrigin.End);
        writer = new StreamWriter(stream);
    }
    public void WriteLine(string line) => writer.WriteLine(line);
    public void Flush() => writer.Flush();
    public void Dispose() => writer.Dispose();
}

/// <summary>One worker owns both streams from opening through disposal. A timed-out close
/// keeps this owner registered; a new world cannot reuse its queue while it still drains.</summary>
public sealed class FlushDiagnosticRecords : IDisposable
{
    private static IDiagnosticSink DefaultSink(string path, FileMode mode, FileAccess access, FileShare share) => new StreamDiagnosticSink(path, mode, access, share);
    private static readonly object ownership = new();
    private static FlushDiagnosticRecords? active;
    private readonly Task worker;
    private int stopping;
    private string endReason = "";
    private long flushRequested, flushCompleted;
    private readonly object progress = new();
    private readonly object terminal = new();
    private bool terminalCommitted, closeTimedOut;
    private long? rows;
    public bool Completed => worker.IsCompleted;
    internal static FlushDiagnosticRecords? Active => active;

    private readonly DiagnosticSinkFactory factory;
    private FlushDiagnosticRecords(string tsvPath, string eventPath, DiagnosticSinkFactory factory)
    { this.factory = factory; worker = Task.Run(() => Drain(tsvPath, eventPath)); worker.ContinueWith(_ => { lock (ownership) if (ReferenceEquals(active, this)) active = null; }); }
    public static FlushDiagnosticRecords Start(string tsvPath, string eventPath)
        => Start(tsvPath, eventPath, DefaultSink);
    internal static FlushDiagnosticRecords Start(string tsvPath, string eventPath, DiagnosticSinkFactory sinkFactory)
    {
        lock (ownership)
        {
            if (active is { Completed: false }) throw new InvalidOperationException("Previous diagnostic worker has not terminated.");
            QueueDiagnosticRecords.Begin();
            return active = new(tsvPath, eventPath, sinkFactory);
        }
    }
    public bool Stop(TimeSpan allowance, string reason = "closed", long? rowCount = null)
    {
        lock (terminal)
        {
            if (stopping == 0)
            {
                endReason = reason;
                rows = rowCount;
                Volatile.Write(ref stopping, 1);
                QueueDiagnosticRecords.Seal();
            }
        }
        bool exited = worker.Wait(allowance);
        lock (terminal)
        {
            if (!exited && !terminalCommitted)
            { closeTimedOut = true; QueueDiagnosticRecords.Fail("diagnostic-close-deadline"); }
            return exited || terminalCommitted;
        }
    }

    /// <summary>Headless readers use a bounded flush fence. Gameplay producers never wait
    /// for disk and only enqueue immutable records.</summary>
    public bool FlushForReader(TimeSpan allowance)
    {
        long request = Interlocked.Increment(ref flushRequested);
        var clock = Stopwatch.StartNew();
        lock (progress) while (Volatile.Read(ref flushCompleted) < request && !Completed)
        {
            var remaining = allowance - clock.Elapsed;
            if (remaining <= TimeSpan.Zero || !Monitor.Wait(progress, remaining)) break;
        }
        return Volatile.Read(ref flushCompleted) >= request;
    }

    private void Drain(string tsvPath, string eventPath)
    {
        try
        {
            using IDiagnosticSink tsv = factory(tsvPath, FileMode.Open, FileAccess.Write, FileShare.Read);
            using IDiagnosticSink events = factory(eventPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var sinceFlush = Stopwatch.StartNew();
            while (true)
            {
                if (QueueDiagnosticRecords.TryDequeue(out var record))
                {
                    try { if (record.Tsv != null) tsv.WriteLine(record.Tsv); else events.WriteLine(JsonSerializer.Serialize(record.Event)); QueueDiagnosticRecords.Complete(record, true); }
                    catch (Exception error) { QueueDiagnosticRecords.Complete(record, false, "diagnostic-writer-" + error.GetType().Name); throw; }
                }
                else if (Volatile.Read(ref stopping) != 0) break;
                else QueueDiagnosticRecords.Signal.WaitOne(25);
                long requested = Volatile.Read(ref flushRequested);
                if (sinceFlush.ElapsedMilliseconds >= 100 || requested > Volatile.Read(ref flushCompleted))
                {
                    tsv.Flush(); events.Flush(); sinceFlush.Restart();
                    // A fence includes everything queued before the request, not merely
                    // whatever the worker had in its StreamWriter buffer.
                    if (QueueDiagnosticRecords.Written == QueueDiagnosticRecords.Enqueued)
                    { Volatile.Write(ref flushCompleted, requested); SignalReaders(); }
                }
            }
            tsv.WriteLine($"# diagnostics={JsonSerializer.Serialize(QueueDiagnosticRecords.Accounting)}");
            while (true)
            {
                bool timedOut;
                lock (terminal) timedOut = closeTimedOut;
                tsv.WriteLine($"# end={endReason}{(rows is null ? "" : ";rows=" + rows)};diagnostics-incomplete={QueueDiagnosticRecords.Incomplete || timedOut};diagnostics-failure={QueueDiagnosticRecords.Failure};diagnostics-dropped={QueueDiagnosticRecords.Dropped}");
                tsv.Flush(); events.Flush();
                lock (terminal)
                {
                    // A sink may block after receiving a clean footer. If Stop's deadline
                    // won during that write, append the authoritative incomplete footer.
                    if (timedOut != closeTimedOut) continue;
                    terminalCommitted = true;
                    break;
                }
            }
            Volatile.Write(ref flushCompleted, Volatile.Read(ref flushRequested)); SignalReaders();
        }
        catch (Exception error)
        {
            // Missing terminal evidence and a sticky failure remain observable. Recorder
            // failure must not tear down the game thread.
            QueueDiagnosticRecords.Fail($"diagnostic-writer-{error.GetType().Name}: {error.Message}");
            QueueDiagnosticRecords.Seal();
            SignalReaders();
        }
    }
    private void SignalReaders() { lock (progress) Monitor.PulseAll(progress); }
    public void Dispose() => Stop(TimeSpan.Zero);
}
