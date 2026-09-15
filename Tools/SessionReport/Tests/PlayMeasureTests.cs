#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AICompanion.Tools.Ledger;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Every play measure run against a real recording, with the number it must produce written down.
///
/// The reason this is not a synthetic fixture is the one the whole harness rests on: a measure
/// tuned on generated rows returns a perfect number against a capture shape that never occurs. Real
/// play is sparse and clustered — a player stands still for eleven thousand rows, the reach flood is
/// unfinished on nearly half of them, one activity owns three quarters of the session — and the
/// defects these measures exist to track live in exactly that shape.
///
/// <b>Nothing is pinned at present, and that is a statement about the evidence rather than a gap
/// somebody forgot to fill.</b> Every number this file used to hold was read off the 13:27 capture
/// of 14 September 2026, which recorded the walking body at schema 0.33.0. The orb's row is a
/// different row: the measures that fed four of those numbers are deleted because the quantities
/// they counted do not exist for this body, and the ones that survive read columns that capture does
/// not carry. Re-pinning them against it would be pinning the instrument to a body the game no
/// longer has.
///
/// So the gate is the capture's own schema, and a capture below <see cref="OrbSchema"/> files a
/// <c>skipped</c> row naming the schema it found and the schema the pins want. That is the same rule
/// the absent-capture branch already followed, extended to the case that is worse because it looks
/// fine: an old capture has every column name a surviving measure asks for, so it would produce
/// numbers, and those numbers would be a walking body's. The first orb playtest is what fills the
/// table below, and until it exists these measures are unverified against real play and this file
/// says so on every run.
///
/// The reason to pin them at all is worth keeping while the table is empty. Two numbers quoted in
/// the research around the 13:27 capture were wrong, both because they came from a reader's own
/// filter over the file rather than from an instrument: a share of rows on which the companion led
/// the travelling player was paired with the wrong threshold, and a count of stretches at a partial
/// destination was 23 by hand and 1 by the measure. After a pin exists, the number in a report is
/// the number the harness produced, and a disagreement with a hand reading is a defect in one of
/// them that somebody can go and settle.
/// </summary>
public static class PlayMeasureTests
{
    /// <summary>
    /// Where the recording lives. <c>Telemetry/</c> is gitignored, so a fresh clone and every agent
    /// worktree has none and this cannot be a hard requirement — but it must not be a silent pass
    /// either, which is the same rule the checks follow for a missing column. An absent capture
    /// prints what is missing and what it would have proved; it never reads as green.
    /// </summary>
    private const string CaptureVariable = "AIC_PLAY_CAPTURE";
    /// <summary>The recorder names each capture for the moment it was written, so the default is the
    /// folder and the newest capture in it, resolved exactly as the tool's own folder argument is.</summary>
    private const string DefaultCapture = "Telemetry";

    /// <summary>
    /// The schema whose row this reader is built for. A capture below it was written by the walking
    /// body, whose columns this reader no longer names, so its numbers cannot be this instrument's
    /// before-numbers however cleanly they come out.
    /// </summary>
    private static readonly Version OrbSchema = new(0, 34, 0);

    /// <summary>
    /// The before-numbers, by ledger case. A share is in percent and a count is a count, matching
    /// what the rows carry, so a figure here can be read straight against a figure in a report.
    ///
    /// Empty until an orb capture exists. An empty table never passes: the run below files a skip
    /// naming what is unpinned, because a green row for a comparison of nothing against nothing is
    /// exactly the hollow result this whole file was built to refuse.
    /// </summary>
    private static readonly (string Case, double Expected, double Tolerance)[] Pinned = Array.Empty<(string, double, double)>();

    /// <summary>Cases that must report themselves skipped on the pinning capture, never silently produce a number.</summary>
    private static readonly string[] MustSkip = Array.Empty<string>();

    /// <summary>The ledger case this half of the self-test reports under, in the reader's own vocabulary.</summary>
    private const string CaseName = "the pinned before-numbers reproduce against a real capture";

    // The suite is the reader's own self-test rather than PlayRow.Suite: the measure rows say what a
    // capture held, and this one says whether the instrument that produced them still works.
    private const string Instrument = PlayRow.Instrument;
    private const string Suite = "SelfTest";

    public static int Run()
    {
        // This half files its own row rather than being wrapped in EmitLedgerRows.Case, and the
        // reason is the absent-capture branch: Telemetry/ is gitignored, so the ordinary outcome in
        // a fresh checkout is "could not look", and Case can only turn a returned zero into a pass.
        // A pass row for a run that read no capture is precisely the hollow green this ledger was
        // built to stop — it would make an unverified instrument indistinguishable from a verified
        // one on every scoreboard afterwards.
        if (!EmitLedgerRows.Selected(CaseName))
        {
            EmitLedgerRows.Skipped(Instrument, Suite, CaseName, $"not selected by --case {EmitLedgerRows.CaseFilter}");
            return 0;
        }

        string wanted = Environment.GetEnvironmentVariable(CaptureVariable) is { Length: > 0 } named
            ? named
            : DefaultCapture;
        string? path = Program.Resolve(wanted);
        if (path is null || !File.Exists(path))
        {
            string reason = $"no capture at {wanted}; Telemetry/ is gitignored, so set {CaptureVariable} to run this";
            Console.WriteLine($"play measures: SKIPPED — {reason}. Until it runs, the before-numbers these measures reproduce are "
                + "unverified in this checkout, which is missing coverage rather than a clean result.");
            EmitLedgerRows.Skipped(Instrument, Suite, CaseName, reason);
            return 0;
        }

        Session session;
        try { session = Session.Load(path); }
        catch (Exception e)
        {
            Console.Error.WriteLine($"play measures: could not read {path}: {e.Message}");
            EmitLedgerRows.Error(Instrument, Suite, CaseName, $"could not read {path}: {e.Message}");
            return 1;
        }

        // A capture older than the orb's row is the dangerous case rather than the missing one. Every
        // column a surviving measure names still exists in it, so it would run to the end and produce
        // a full set of confident numbers — about a body the game does not have. It is named as a skip
        // for the same reason an absent capture is: a number nobody can act on must never read green.
        string found = session.Metadata.TryGetValue("schema", out string? declared) ? declared : "unlabelled";
        if (!Version.TryParse(found, out Version? schema) || schema < OrbSchema)
        {
            string reason = $"the capture at {Path.GetFileName(path)} declares schema {found} and the pinned numbers are the orb's, from {OrbSchema} onward; "
                + "a walking-body capture still carries every column the surviving measures read, so it would produce numbers about a body this reader no longer describes";
            Console.WriteLine($"play measures: SKIPPED — {reason}. The measures are unverified against real play in this checkout.");
            EmitLedgerRows.Skipped(Instrument, Suite, CaseName, reason);
            return 0;
        }

        // An empty pin table cannot pass. Every assertion below is a loop over `Pinned`, so a run with
        // nothing in it satisfies all of them and files a pass for having compared nothing.
        if (Pinned.Length == 0)
        {
            const string reason = "no before-number is pinned yet: every earlier pin was read off a walking-body capture, and the first orb "
                + "playtest is what fills the table. Until then this instrument has never been run against real play";
            Console.WriteLine($"play measures: SKIPPED — {reason}.");
            EmitLedgerRows.Skipped(Instrument, Suite, CaseName, reason);
            return 0;
        }

        // Rows are read out of the emitter's own record rather than out of the printed text,
        // because reading a tool's own printout back is the stdout-parsing the plan refuses and it
        // would make every change to a print a change to this test.
        int before = EmitLedgerRows.Emitted.Count;
        // Proving the instrument against a fixed capture is not measuring this commit, so these
        // rows stay out of the run file; a backfill stores them under the capture's own revision.
        EmitLedgerRows.Suspended = true;
        try { Program.RunMeasures(session); }
        finally { EmitLedgerRows.Suspended = false; }
        var rows = EmitLedgerRows.Emitted.Skip(before).ToArray();
        var byCase = new Dictionary<string, LedgerRow>(StringComparer.Ordinal);
        foreach (LedgerRow row in rows) byCase[row.Case] = row;

        var failures = new List<string>();
        foreach ((string name, double expected, double tolerance) in Pinned)
        {
            if (!byCase.TryGetValue(name, out LedgerRow? row))
            {
                failures.Add($"{name}: the measure emitted no row for this case at all");
                continue;
            }
            if (row.Verdict != "measure" || row.Value is not { } value)
            {
                failures.Add($"{name}: expected a measured value and got verdict '{row.Verdict}' ({row.Message})");
                continue;
            }
            if (Math.Abs(Math.Round(value, 2) - expected) > tolerance)
                failures.Add($"{name}: expected {expected.ToString(CultureInfo.InvariantCulture)}, measured {value.ToString("0.####", CultureInfo.InvariantCulture)}");
        }
        foreach (string name in MustSkip)
        {
            if (!byCase.TryGetValue(name, out LedgerRow? row)) { failures.Add($"{name}: no row, and a measure that cannot run must say so rather than vanish"); continue; }
            if (row.Verdict != "skipped")
                failures.Add($"{name}: expected a skip naming what the capture lacks, and got '{row.Verdict}' — a column this capture does not carry must never read as a number");
        }

        // A measure that throws is an error row, and an error row here means the twenty-four numbers
        // above were measured by an instrument that is partly broken.
        foreach (LedgerRow row in rows.Where(r => r.Verdict == "error"))
            failures.Add($"{row.Case}: the measure itself failed — {row.Message}");

        if (failures.Count > 0)
        {
            Console.Error.WriteLine($"play measures: {failures.Count} of {Pinned.Length + MustSkip.Length} pinned numbers did not reproduce against {Path.GetFileName(path)}:");
            foreach (string failure in failures) Console.Error.WriteLine($"  {failure}");
            EmitLedgerRows.Fail(Instrument, Suite, CaseName,
                $"{failures.Count} of {Pinned.Length + MustSkip.Length} pinned numbers did not reproduce against {Path.GetFileName(path)}: {failures[0]}");
            return 1;
        }
        Console.WriteLine($"play measures: {Pinned.Length} before-numbers and {MustSkip.Length} named skip reproduced against {Path.GetFileName(path)} ({rows.Length} rows emitted).");
        EmitLedgerRows.Pass(Instrument, Suite, CaseName,
            $"{Pinned.Length} before-numbers and {MustSkip.Length} named skip reproduced against {Path.GetFileName(path)}");
        return 0;
    }
}
