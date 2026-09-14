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
        // The last line is what verify.sh shows for this instrument, and a self-test that prints
        // nothing when it passes reads exactly like one that ran nothing.
        Console.WriteLine(failed == 0
            ? "ledger self-test passed: commit widths, the four baseline refusals and the header round trip."
            : $"ledger self-test: {failed} of 3 store properties failed.");
        return failed;
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

            Write(root, new RunHeader(commit, false, "2026-01-01T00:03:00Z", "test", 1, 1), Pass());
            Run? resolved = RunStore.Baseline(root, commit);
            if (resolved == null) return Failed("a clean, unfiltered, non-dirty run at the commit itself did not resolve as its baseline");

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
