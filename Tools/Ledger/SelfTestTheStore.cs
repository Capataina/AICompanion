#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AICompanion.Tools.Ledger;

/// <summary>
/// The ledger's own self-test, which exists because the ledger became the verdict for every other
/// instrument and had nothing checking it.
///
/// It was written from a defect rather than from an imagined failure. <see cref="RunStore.Baseline"/>
/// compared the short commit hash a run header stores against the full hashes <c>git rev-list</c>
/// prints, in one direction only, so it could never match and every full run reported "no baseline"
/// while the store held a perfectly good clean run at the parent commit. Nothing failed: the
/// scoreboard printed, the suite exited 0, and the entire comparison the ledger exists to provide
/// was absent. A feature that silently does nothing is the failure mode this whole lane is about,
/// so each rule below is one property with a temporary store built for it.
/// </summary>
public static class SelfTestTheStore
{
    private const string Instrument = "ledger";
    private const string Suite = "Store";

    public static int Run()
    {
        int failed = 0;
        failed += EmitLedgerRows.Case(Instrument, Suite,
            "a short commit and its full hash name one commit in both directions", CommitWidths);
        failed += EmitLedgerRows.Case(Instrument, Suite,
            "a baseline is the nearest clean, unfiltered, non-dirty ancestor run", BaselineRules);
        failed += EmitLedgerRows.Case(Instrument, Suite,
            "a run file round-trips its header, including the case filter it ran under", HeaderRoundTrip);
        failed += EmitLedgerRows.Case(Instrument, Suite,
            "the interval figures quoted about this suite are the ones the arithmetic returns", QuotedIntervals);
        failed += EmitLedgerRows.Case(Instrument, Suite,
            "a narrower run is never the baseline for a wider one", CoverageRules);
        failed += EmitLedgerRows.Case(Instrument, Suite,
            "a deliberately red case can be produced on demand, so the rerun loop can be exercised", ForcedRed);
        failed += EmitLedgerRows.Case(Instrument, Suite,
            "a measure the producer declared a sample reports its delta without being called drift", SampledMeasures);
        // The last line is what verify.sh shows for this instrument, and a self-test that prints
        // nothing when it passes reads exactly like one that ran nothing.
        Console.WriteLine(failed == 0
            ? "ledger self-test passed: commit widths, the baseline refusals, coverage eligibility, the header round trip, the quoted intervals and the sampled-measure reading."
            : $"ledger self-test: {failed} of 7 store properties failed.");
        return failed;
    }

    /// <summary>
    /// Every interval this repository quotes about itself, checked against what <see cref="Wilson.Of"/>
    /// actually returns rather than left standing in prose.
    ///
    /// It exists because the prose was wrong in two places at once while the arithmetic was right:
    /// the folder file said five green runs bound the failure rate below 32.6 percent and named the
    /// verification plan's "about 43 percent" as the suspect figure, when 43.45 is what Wilson
    /// returns and 32.6 is not a bound on anything — it is the half-width of the three-of-five
    /// interval, which is the neighbouring sentence's number. Two quoted figures, one transposition,
    /// and both survived because a number in a comment is checked by nobody. These are checked now.
    /// </summary>
    private static int QuotedIntervals()
    {
        int failed = 0;
        failed += Near("three green of five bounds the pass rate", Wilson.Of(3, 5).Low * 100, 23.1);
        failed += Near("three green of five bounds the pass rate", Wilson.Of(3, 5).High * 100, 88.2);
        failed += Near("five green bound the failure rate", (1 - Wilson.Of(5, 5).Low) * 100, 43.45);
        failed += Near("thirty green bound the failure rate", (1 - Wilson.Of(30, 30).Low) * 100, 11.4);
        failed += Near("a hundred green bound the failure rate", (1 - Wilson.Of(100, 100).Low) * 100, 3.7);
        // Zero width at the boundary is the normal approximation's failure and the reason Wilson is
        // used at all: without this row, swapping the formula back would leave every sentence above
        // reading as proof.
        if (Wilson.Of(5, 5).Low >= 1)
            { Console.WriteLine("a perfect batch must still carry a real interval, or green reads as proof"); failed++; }
        return failed;
    }

    private static int Near(string what, double got, double expected)
    {
        if (Math.Abs(got - expected) <= 0.05) return 0;
        Console.WriteLine($"{what}: the arithmetic says {got:0.##}% where the quoted figure is {expected:0.##}%");
        return 1;
    }

    /// <summary>
    /// The two coverage rules that decide whether a past run is a fair yardstick, each written from
    /// a run that actually reached the scoreboard rather than from a worry.
    /// </summary>
    private static int CoverageRules()
    {
        int failed = 0;
        Run wide = Synthetic(("a", "pass"), ("b", "pass"), ("c", "pass"));
        // measure-flake.sh: one case run many times, every other case skipped, no header filter.
        Run flakeBatch = Synthetic(("a", "pass"), ("b", "skipped"), ("c", "skipped"));
        // backfill-capture.sh: play measures over a capture and no fixtures at all.
        Run backfill = Synthetic(("x", "measure"), ("y", "measure"));

        if (wide.CoversRunsOf(flakeBatch))
            { Console.WriteLine("a run that skipped what the new run measured was accepted as its baseline"); failed++; }
        if (wide.CoversRunsOf(backfill))
            { Console.WriteLine("a run measuring cases the new run never reports was accepted as its baseline"); failed++; }
        if (!wide.CoversRunsOf(Synthetic(("a", "pass"), ("b", "pass"))))
            { Console.WriteLine("a baseline covering fewer cases than the new run adds must still be eligible, or no case can ever be added"); failed++; }
        if (!wide.CoversRunsOf(wide))
            { Console.WriteLine("a run of identical coverage must be eligible"); failed++; }
        return failed;
    }

    /// <summary>
    /// One measure, one commit apart, read twice: once as an ordinary measurement and once as a
    /// sample the producer declared. The same delta must be reported both times and must be called
    /// drift only in the first reading.
    ///
    /// The pair is the whole test, and it is written as a pair deliberately. A row asserting only
    /// that a tagged measure reports <c>Sampled</c> would pass against a scoreboard that reported
    /// every measure that way, which is the failure worth catching here — the play-measures suite
    /// is fourteen rows of one instrument and the ordinary measures are fifty rows of every other,
    /// so a tag read too widely would silence exactly the drift the scoreboard exists to print. The
    /// untagged arm is what stops that, and the identical values in both arms are what make the
    /// tag the only variable.
    /// </summary>
    private static int SampledMeasures()
    {
        int failed = 0;
        Run before = Measured(120.0, sampled: false), after = Measured(180.0, sampled: false);
        CaseChange plain = Only(Scoreboard.Compare(before, after, Array.Empty<Run>()));
        if (plain.Change != Change.MeasureDrift)
            failed += Failed($"an untagged measure that moved 120 -> 180 reported {plain.Change}, not MeasureDrift, "
                + "so the tag is being read for rows that never carried it and real drift has stopped printing");

        CaseChange sampled = Only(Scoreboard.Compare(Measured(120.0, sampled: true), Measured(180.0, sampled: true), Array.Empty<Run>()));
        if (sampled.Change != Change.Sampled)
            failed += Failed($"a measure tagged '{EmitLedgerRows.SampledTag}' that moved 120 -> 180 reported {sampled.Change}, "
                + "so every run of a play-measures suite reads as a regression against the run before it");
        if (sampled.Before != plain.Before || sampled.After != plain.After)
            failed += Failed($"the sampled reading printed {sampled.Before} -> {sampled.After} where the same numbers printed "
                + $"{plain.Before} -> {plain.After} untagged; the delta is the one thing the tag must not hide");
        if (!sampled.Detail.Contains("not drift", StringComparison.Ordinal))
            failed += Failed($"the sampled reading's detail '{sampled.Detail}' does not say why the delta is not a move");

        // A sample that did not move is still unchanged. Without this the tag would be a licence to
        // print a line for every play measure on every run, which is the noise the change removes.
        CaseChange still = Only(Scoreboard.Compare(Measured(120.0, sampled: true), Measured(120.0, sampled: true), Array.Empty<Run>()));
        if (still.Change != Change.Unchanged)
            failed += Failed($"a sampled measure that did not move reported {still.Change} rather than Unchanged");
        return failed;
    }

    private static CaseChange Only(IReadOnlyList<CaseChange> changes)
        => changes.Count == 1 ? changes[0]
            : throw new InvalidOperationException($"the synthetic pair produced {changes.Count} changes, so the row is not comparing one case");

    private static Run Measured(double value, bool sampled)
        => new("synthetic", RunHeader.Now("0000000", false, "0000000", "", ""),
            new[]
            {
                new LedgerRow("world-run", "play measures", "median distance while he moved", "measure",
                    value, "px", "down", "in-suite; production-allowances",
                    sampled ? new[] { EmitLedgerRows.SampledTag } : Array.Empty<string>()),
            }, 0);

    private static Run Synthetic(params (string Case, string Verdict)[] rows)
        => new("synthetic", RunHeader.Now("0000000", false, "0000000", "", ""),
            rows.Select(r => new LedgerRow("i", "s", r.Case, r.Verdict,
                Value: r.Verdict == "measure" ? 1 : null)).ToArray(), 0);

    /// <summary>
    /// A red on demand, and only on demand: this case passes unless <c>AIC_LEDGER_FORCE_RED</c> is
    /// set in the environment.
    ///
    /// It is here because <c>--rerun-red</c> shipped with its loop never once executed — the reds
    /// list was empty on every run it was asked on, so the dispatch-by-instrument it depends on was
    /// verified by reading rather than by running, which is the same evidence as none. A harness
    /// whose failure path can only be exercised by breaking something real is a harness whose
    /// failure path stays unexercised, so the switch stays rather than being deleted after one use:
    /// the next change to the rerun machinery can be tested the same way, in one command, without
    /// anybody having to find a genuinely broken fixture first.
    /// </summary>
    private static int ForcedRed()
    {
        if (Environment.GetEnvironmentVariable("AIC_LEDGER_FORCE_RED") is not { Length: > 0 } mode) return 0;
        // "flaky" fails only on the first process of a batch, so a rerun of it grades as flaky
        // rather than as red — which is the other half of what the rerun loop has to be able to say.
        if (mode == "flaky" && Environment.GetEnvironmentVariable(EmitLedgerRows.RunPathVariable) is { Length: > 0 } path
            && File.ReadAllText(path).Contains("\"case\":\"a deliberately red case", StringComparison.Ordinal))
            return 0;
        EmitLedgerRows.Detail($"AIC_LEDGER_FORCE_RED={mode} asked this case to fail, and it did");
        return 1;
    }

    /// <summary>The defect itself: two hash widths, compared one way, matching nothing.</summary>
    private static int CommitWidths()
    {
        string root = Git.Root(Directory.GetCurrentDirectory());
        string shortHead = Git.Head(root);
        string[] ancestry = Git.Ancestry(root, shortHead);
        if (shortHead.Length == 0 || ancestry.Length == 0)
            return Failed("git named neither HEAD nor any ancestor, so this property could not be asked");

        string fullHead = ancestry[0];
        int failures = 0;
        if (fullHead.Length <= shortHead.Length)
            failures += Failed($"rev-list returned '{fullHead}', no longer than the short hash '{shortHead}', "
                + "so this test is no longer exercising the two widths it was written for");
        if (!Git.Same(shortHead, fullHead))
            failures += Failed($"the stored short hash '{shortHead}' did not match its own full hash '{fullHead}'");
        if (!Git.Same(fullHead, shortHead))
            failures += Failed($"the full hash '{fullHead}' did not match its own short hash '{shortHead}' — "
                + "the comparison must hold whichever side is abbreviated");
        if (Git.Same(shortHead, "")) failures += Failed("an empty identifier matched a real commit, which would make every run a baseline");
        if (Git.Same("", fullHead)) failures += Failed("a real commit matched an empty identifier");
        if (Git.Same(fullHead, new string('0', 40))) failures += Failed("two unrelated hashes of one length matched");
        return failures;
    }

    /// <summary>
    /// The four reasons a stored run is refused as a baseline, each asserted on its own run so a
    /// rule that stopped firing cannot hide behind another rule that still does. The commits are
    /// invented names: <see cref="Git.Ancestry"/> answers an unknown commit with itself, which is
    /// the documented behaviour that lets a capture's own revision resolve, and it is what makes a
    /// store this test fully controls possible without standing up a repository.
    /// </summary>
    private static int BaselineRules()
    {
        string root = TemporaryRoot();
        try
        {
            string commit = "aic-selftest-" + Guid.NewGuid().ToString("N")[..8];
            Write(root, new RunHeader(commit, Dirty: true, "2026-01-01T00:00:00Z", "test", 1, 1), Pass());
            if (RunStore.Baseline(root, commit) != null) return Failed("a dirty run became a baseline, and nothing identifies what it ran against");

            Write(root, new RunHeader(commit, false, "2026-01-01T00:01:00Z", "test", 1, 1), Red());
            if (RunStore.Baseline(root, commit) != null) return Failed("a run with a red row became a baseline");

            Write(root, new RunHeader(commit, false, "2026-01-01T00:02:00Z", "test", 1, 1, Filter: "ore work"), Pass());
            if (RunStore.Baseline(root, commit) != null)
                return Failed("a run taken under --case became a baseline, so every case the filter excluded would read as a case that disappeared");

            string newestPath = Write(root, new RunHeader(commit, false, "2026-01-01T00:03:00Z", "test", 1, 1), Pass());
            Run? resolved = RunStore.Baseline(root, commit);
            if (resolved == null) return Failed("a clean, unfiltered, non-dirty run at the commit itself did not resolve as its baseline");

            // The run being scored is excluded inside the walk, which has to *continue* past it. A
            // guard that instead nulls a self-match afterwards abandons the search at the first
            // ancestor, so a clean run at a new commit is compared against nothing while its
            // parent's run sits in the store — which is what happened, and it made the whole
            // comparison work only for dirty runs.
            string olderPath = Write(root, new RunHeader(commit, false, "2026-01-01T00:02:30Z", "test", 1, 1), Pass());
            Run? excluded = RunStore.Baseline(root, commit, excluding: newestPath);
            if (excluded == null)
                return Failed("excluding the run being scored abandoned the search instead of continuing it, so a clean run would be compared against nothing");
            if (excluded.Path == newestPath)
                return Failed("the excluded run was returned as its own baseline");
            if (excluded.Path != olderPath)
                return Failed($"expected the next clean run back, and got {Path.GetFileName(excluded.Path)}");

            // A skip is missing coverage rather than a verdict, and refusing a run for holding one
            // would leave this repository with no baseline at all: its captures are gitignored, so
            // the play measures skip in every fresh checkout.
            string skipped = "aic-selftest-" + Guid.NewGuid().ToString("N")[..8];
            Write(root, new RunHeader(skipped, false, "2026-01-01T00:04:00Z", "test", 1, 1),
                new LedgerRow(Instrument, Suite, "a case", "skipped", Message: "no capture"));
            if (RunStore.Baseline(root, skipped) == null)
                return Failed("a run whose only non-pass row was a skip was refused as a baseline");
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }

    private static int HeaderRoundTrip()
    {
        string root = TemporaryRoot();
        try
        {
            var header = new RunHeader("abc1234", false, "2026-01-01T00:00:00Z", "machine", 3.5, 4, "def5678", "a note", "ore work");
            string path = Write(root, header, Pass());
            Run? read = RunStore.Read(path);
            if (read == null) return Failed("a run file this store had just written did not read back");
            RunHeader back = read.Header;
            int failures = 0;
            if (back != header) failures += Failed($"the header changed across a write and a read: wrote {header}, read {back}");
            if (!back.Filtered) failures += Failed("the case filter did not survive the round trip, so a filtered run would silently become a baseline");
            if (read.Rows.Count != 1) failures += Failed($"expected one row back and got {read.Rows.Count}");

            // A file with rows and no header is not a run: reading one as a run would file its rows
            // under a commit nobody ran them at, which is the one lie the store must not tell.
            string headless = Path.Combine(RunStore.Folder(root), "headless.jsonl");
            File.WriteAllText(headless, EmitLedgerRows.Serialise(Pass()) + "\n");
            if (RunStore.Read(headless) != null) failures += Failed("a file with rows and no header parsed as a run");
            return failures;
        }
        finally { Directory.Delete(root, true); }
    }

    private static LedgerRow Pass() => new(Instrument, Suite, "a case", "pass");

    private static LedgerRow Red() => new(Instrument, Suite, "a case", "fail", Message: "red");

    private static string TemporaryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "aic-ledger-selftest-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(RunStore.Folder(root));
        return root;
    }

    private static string Write(string root, RunHeader header, params LedgerRow[] rows)
    {
        string path = RunStore.Begin(root, header);
        File.AppendAllLines(path, rows.Select(EmitLedgerRows.Serialise));
        return path;
    }

    private static int Failed(string message)
    {
        Console.Error.WriteLine($"  ledger self-test: {message}");
        return 1;
    }
}
