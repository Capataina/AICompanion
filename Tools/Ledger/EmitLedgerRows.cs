#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AICompanion.Tools.Ledger;

/// <summary>
/// What one named case did on one run. Everything in the harness reduces to this: a fixture's
/// verdict, a measurement's value, a check's skip and its reason.
///
/// The field that does the most work is <see cref="Verdict"/>, and its six values are not
/// interchangeable. <c>pass</c> and <c>fail</c> are the thing under test. <c>error</c> is the
/// check itself breaking, which is a different repair and must never be read as the thing under
/// test failing. <c>skipped</c> carries its reason and is never a pass, because zero coverage and
/// a clean run look identical in any report that folds them together. <c>sealed</c> is the
/// corpus's model-closed answer — the search proved nothing rather than proving impossibility.
/// <c>measure</c> carries a number instead of a verdict and is never graded here: a measure that
/// decided pass or fail from a threshold nobody declared is the thing this schema exists to
/// refuse, so a pass line lives in the plan and is cited in <see cref="Tags"/>.
/// </summary>
public sealed record LedgerRow(
    string Instrument,
    string Suite,
    string Case,
    string Verdict,
    double? Value = null,
    string? Unit = null,
    /// <summary>Which way is good for a measure: "up", "down", or null where neither is better.</summary>
    string? Direction = null,
    /// <summary>
    /// How the row was taken, because the suite's own rule is that a timing is comparable only to
    /// another taken the same way: "in-suite" or "alone", and "production-allowances" or
    /// "unbounded-allowances". A row compared against one of another mode is not a comparison.
    /// </summary>
    string Mode = "in-suite",
    IReadOnlyList<string>? Tags = null,
    /// <summary>
    /// The mutation that fails this row — the rival rule it was written to reject. A row without
    /// one is reported by the scoreboard as unkilled, which makes the practice the suite already
    /// follows by hand visible on the runs where it was skipped.
    /// </summary>
    string? KilledBy = null,
    string Message = "",
    double DurationMs = 0);

/// <summary>
/// The one writer of ledger rows. Every instrument reports through this and nothing else, which is
/// the boundary the whole harness rests on: while a fixture decides pass or fail by printing its
/// own line, "every case reports" is a habit that holds until somebody forgets, and a case that
/// forgets is invisible rather than red.
///
/// Two consequences of that boundary are worth stating because they are easy to undo by accident.
/// Nothing here parses another process's stdout: a print is for a person reading a terminal, and
/// the moment a second program reads it, every print becomes an undeclared contract that a
/// clearer message breaks. And the emitter prints as well as records, so a tool run by hand with
/// no run file open behaves exactly as it did before — the recording is an addition, never a
/// replacement for the output a person already reads.
///
/// The run file is named by <c>AIC_LEDGER_RUN</c>. One verify run opens one file and every
/// instrument in it appends, which is why the file is line-delimited JSON rather than a document:
/// appending a line needs no read of what is already there, so a process that dies mid-suite
/// leaves every row it had already emitted intact and readable.
/// </summary>
public static class EmitLedgerRows
{
    public const string RunPathVariable = "AIC_LEDGER_RUN";

    /// <summary>The run file this process appends to, or null when nobody opened one.</summary>
    public static string? RunPath => Environment.GetEnvironmentVariable(RunPathVariable) is { Length: > 0 } path ? path : null;

    /// <summary>Rows emitted by this process, kept so a tool can print its own tally without re-reading the file.</summary>
    public static IReadOnlyList<LedgerRow> Emitted => emitted;
    private static readonly List<LedgerRow> emitted = new();

    /// <summary>
    /// The one case filter. <c>AIC_LEDGER_CASE</c> holds a case name; a tool asks
    /// <see cref="Selected"/> before running anything, so --case and --rerun-red reach every
    /// instrument through one mechanism rather than through a flag each tool parses its own way.
    /// Matching is substring and case-insensitive, because the case names are the sentences the
    /// fixtures already print and retyping one exactly is not a thing a person will do. Several
    /// fragments separated by <c>|</c> select a case matching any of them, in the suite's own order,
    /// which is how a case that only goes red after a particular neighbour is bisected: run the pair,
    /// then the pair with the other neighbour, without driving the whole suite each time.
    /// </summary>
    public static string? CaseFilter => Environment.GetEnvironmentVariable("AIC_LEDGER_CASE") is { Length: > 0 } name ? name : null;

    public static bool Selected(string name)
        => CaseFilter is not { } filter
           || filter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A case that must keep the production wall-clock allowances in force, because the allowance is
    /// the thing it exercises. Tagging is the whole mechanism: every other case runs with the
    /// allowances lifted, so a fixture cannot end up timing the machine by omission.
    /// </summary>
    public const string ProductionAllowancesTag = "production-allowances";

    /// <summary>
    /// A measure whose value is one draw from a distribution rather than a property of the tree:
    /// a run that keeps the wall clock, a play-measures run over a live-staged scene, anything
    /// whose number would differ on a second run of the same commit. The scoreboard prints such a
    /// measure's delta and does not call it drift, because a difference between two samples is not
    /// evidence that anything moved.
    ///
    /// It carries no number on purpose. A tolerance would be a pass line, and a measure graded
    /// against a threshold nobody declared is exactly what this schema refuses — so the tag says
    /// which question the delta cannot answer, and leaves the answering to whoever declares a
    /// pass line in the plan. The noise band from repeats is still computed and still printed,
    /// because repeats of one commit are the honest way to bound a sample and a tag is not.
    /// </summary>
    public const string SampledTag = "sampled";

    /// <summary>
    /// A case whose subject is wall-clock time, or a measure that is a time. The owner ruled on 24
    /// September 2026 that no time is a pass line: "two milliseconds for a specific playthrough might look
    /// a lot different than another playthrough", so a timing is a measure compared against its own
    /// history, never a threshold that goes red. The tag also decides where a case runs: a timed case
    /// never shares the machine with a parallel shard, because a timing taken under competition measures
    /// the competition.
    /// </summary>
    public const string TimedTag = "timed";

    /// <summary>
    /// A case heavy enough that an ordinary verify skips it. It runs in the perf tier, which verify runs
    /// when anything the brain is built from has changed since the last perf run, and on `--perf`. The
    /// crowd planning row, 138 s of a 232 s suite at 987319df, is the case this exists for.
    /// </summary>
    public const string PerfTierTag = "perf-tier";

    /// <summary>
    /// What an instrument does to the process before each case: put every static a case can reach
    /// back to a known state, and set the wall-clock allowances for the regime this case asked for
    /// (the argument is true where the case keeps the production allowances).
    ///
    /// It is a delegate rather than a call because this file is compiled into every Tools project
    /// and only some of them can name the mod's statics at all — NavReplay has no Terraria in it.
    /// So the hook lives here, where every case already passes through, and the body lives in the
    /// instrument that knows what its own process holds. The alternative, each fixture resetting
    /// what it remembers to reset, is what produced a suite where the courtesy fixture's stillness
    /// depended on which fixtures ran before it.
    /// </summary>
    public static Action<bool>? ResetBeforeCase;

    /// <summary>
    /// A line a fixture wants a person to read, which is also the reason a red row exists.
    ///
    /// Before this, a fixture printed its own reason to stdout and returned a failure count, so the
    /// ledger row said "3 failure(s) reported by the fixture" and the only copy of what actually
    /// went wrong was in a terminal nobody kept. The detail is printed exactly as it was and also
    /// folded into the row, so the run file can be read months later without the console beside it.
    /// </summary>
    public static void Detail(string line)
    {
        Console.WriteLine(line);
        details.Add(line);
    }

    private static readonly List<string> details = new();

    /// <summary>
    /// Keep recording rows in memory but write none to the run file.
    ///
    /// It exists for one case and the case matters. A self-test that runs the play measures over a
    /// fixed old capture is proving the instrument, not measuring this commit — and a row from it
    /// lands under whatever commit is checked out, so the scoreboard would read a capture from last
    /// week as this commit's play numbers and report them as changed every time the commit changed.
    /// A backfill stores those same measures under the capture's own revision instead, which is
    /// where they belong.
    /// </summary>
    public static bool Suspended { get; set; }

    public static void Row(LedgerRow row)
    {
        emitted.Add(row);
        if (Suspended || RunPath is not { } path) return;
        try
        {
            File.AppendAllText(path, Serialise(row) + "\n", new UTF8Encoding(false));
            // Appended, never rewritten: a process that dies mid-suite leaves every row it had
            // already emitted intact, which is the difference between a partial run and no run.
        }
        catch (IOException e)
        {
            // A ledger that cannot write must say so on the stream a person is reading rather than
            // throw, because taking the suite down over a failed bookkeeping write would destroy
            // the result the run existed to produce.
            Console.Error.WriteLine($"ledger: could not append to {path}: {e.Message}");
        }
    }

    public static void Pass(string instrument, string suite, string @case, string message = "", double durationMs = 0, string mode = "in-suite", IReadOnlyList<string>? tags = null, string? killedBy = null)
        => Row(new LedgerRow(instrument, suite, @case, "pass", Mode: mode, Tags: tags, KilledBy: killedBy, Message: message, DurationMs: durationMs));

    public static void Fail(string instrument, string suite, string @case, string message, double durationMs = 0, string mode = "in-suite", IReadOnlyList<string>? tags = null, string? killedBy = null)
        => Row(new LedgerRow(instrument, suite, @case, "fail", Mode: mode, Tags: tags, KilledBy: killedBy, Message: message, DurationMs: durationMs));

    /// <summary>A reason, never a pass. The reason is the whole value of the row.</summary>
    public static void Skipped(string instrument, string suite, string @case, string reason, IReadOnlyList<string>? tags = null)
        => Row(new LedgerRow(instrument, suite, @case, "skipped", Tags: tags, Message: reason));

    /// <summary>The check itself broke. Nothing was measured about the thing under test.</summary>
    public static void Error(string instrument, string suite, string @case, string message)
        => Row(new LedgerRow(instrument, suite, @case, "error", Message: message));

    /// <summary>
    /// A number, with which way is good and how it was taken. No verdict: a measure is graded by
    /// the scoreboard against a baseline and a noise band, never against a threshold invented here.
    /// </summary>
    public static void Measure(string instrument, string suite, string @case, double value, string unit, string? direction = null, string mode = "in-suite", IReadOnlyList<string>? tags = null, string message = "")
        => Row(new LedgerRow(instrument, suite, @case, "measure", value, unit, direction, mode, tags, Message: message));

    /// <summary>
    /// Run one named case, emit its row, and never let it take the rest of the suite with it.
    ///
    /// The body returns a failure count, which is how every fixture in this repository already
    /// reports, and it may also throw, which is how every fixture in this repository reports an
    /// assertion. Both are the thing under test failing and both become <c>fail</c>. That mapping
    /// is deliberate and is the one place it is decided: an exception out of a fixture here is its
    /// assertion mechanism rather than the harness breaking, so grading a throw as <c>error</c>
    /// would hide every real red behind a word that means "ignore this, the instrument is broken".
    ///
    /// The return is the failure count, so a caller still sums exactly what it summed before and
    /// the suite's own exit code is unchanged by being recorded.
    /// </summary>
    public static int Case(string instrument, string suite, string name, Func<int> body, string mode = "in-suite", IReadOnlyList<string>? tags = null, string? killedBy = null)
    {
        if (!Selected(name))
        {
            Skipped(instrument, suite, name, $"not selected by --case {CaseFilter}", tags);
            return 0;
        }
        // The regime is stamped rather than passed, because the suite's own rule is that a row is
        // comparable only to one taken the same way, and a mode a caller had to remember to write
        // is a mode that will disagree with what the process was actually doing.
        bool keepProductionAllowances = tags?.Contains(ProductionAllowancesTag) == true;
        mode = $"{mode}; {(keepProductionAllowances ? "production-allowances" : "unbounded-allowances")}";
        details.Clear();
        ResetBeforeCase?.Invoke(keepProductionAllowances);
        var clock = Stopwatch.StartNew();
        try
        {
            int failures = body();
            clock.Stop();
            if (failures == 0)
                Pass(instrument, suite, name, durationMs: clock.Elapsed.TotalMilliseconds, mode: mode, tags: tags, killedBy: killedBy);
            else
                Fail(instrument, suite, name, Reason($"{failures} failure(s) reported by the fixture"), clock.Elapsed.TotalMilliseconds, mode, tags, killedBy);
            return failures;
        }
        catch (Exception e)
        {
            clock.Stop();
            Console.WriteLine($"{name} failed: {e.GetType().Name}: {e.Message}");
            Fail(instrument, suite, name, Reason($"{e.GetType().Name}: {Flatten(e.Message)}"), clock.Elapsed.TotalMilliseconds, mode, tags, killedBy);
            return 1;
        }
    }

    /// <summary>The failure headline with whatever the fixture said on its way there, capped so one
    /// noisy case cannot make a run file unreadable.</summary>
    private static string Reason(string headline)
        => details.Count == 0 ? headline
            : headline + " — " + string.Join(" | ", details.Take(8).Select(Flatten))
              + (details.Count > 8 ? $" (+{details.Count - 8} more on the console)" : "");

    private static string Flatten(string message)
        => message.Replace('\n', ' ').Replace('\r', ' ').Trim();

    // Hand-written rather than System.Text.Json so the emitter is one file with no package and no
    // serializer configuration to drift, and so the field order in the file is the field order a
    // person reads. The row's own strings are the only untrusted input and they are escaped here.
    internal static string Serialise(LedgerRow row)
    {
        var text = new StringBuilder("{\"kind\":\"row\"");
        Text(text, "instrument", row.Instrument);
        Text(text, "suite", row.Suite);
        Text(text, "case", row.Case);
        Text(text, "verdict", row.Verdict);
        if (row.Value is { } value) Number(text, "value", value);
        if (row.Unit is { } unit) Text(text, "unit", unit);
        if (row.Direction is { } direction) Text(text, "direction", direction);
        Text(text, "mode", row.Mode);
        text.Append(",\"tags\":[").Append(string.Join(",", (row.Tags ?? Array.Empty<string>()).Select(Quote))).Append(']');
        if (row.KilledBy is { } killed) Text(text, "killed_by", killed);
        Text(text, "message", row.Message);
        Number(text, "duration_ms", Math.Round(row.DurationMs, 3));
        return text.Append('}').ToString();
    }

    private static void Text(StringBuilder into, string key, string value)
        => into.Append(",\"").Append(key).Append("\":").Append(Quote(value));

    private static void Number(StringBuilder into, string key, double value)
        => into.Append(",\"").Append(key).Append("\":")
               .Append(double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "null");

    internal static string Quote(string value)
    {
        var text = new StringBuilder("\"");
        foreach (char c in value)
            text.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        return text.Append('"').ToString();
    }
}
