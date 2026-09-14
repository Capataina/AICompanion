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
/// play is sparse and clustered — a player stands still for eleven thousand rows of this capture,
/// the reach flood is unfinished on nearly half of them, one activity owns three quarters of the
/// session — and the defects these measures exist to track live in exactly that shape. So the
/// fixture is the 13:27 capture of 14 September 2026, and the numbers below are the ones three
/// independent readings of it agreed on.
///
/// Two of the numbers quoted in the research around that capture are *not* reproduced here, and
/// both are recorded in the return and the folder file rather than fitted to. The commit body of
/// 8597628 says the companion led the travelling player by more than four tiles on 4.7% of moving
/// rows; the instrument says 10.8%, and 4.7% is the eight-tile figure, so a percentage was paired
/// with the wrong threshold. And the 13:27 read reports 23 stretches of arriving at a partial
/// destination; under the definition that read states — navigator Arrived, position reason
/// partial-progress-candidate, spot unchanged — this capture holds exactly one, of 328 rows, and
/// three neighbouring definitions yield at most nine. The longest stretch, 328, reproduces.
///
/// That is the point of pinning them at all. Both wrong numbers came from a reader's own filter
/// over the file rather than from an instrument, which is the failure this whole file exists to
/// close: after this, the number in a report is the number the harness produced, and a disagreement
/// with a hand reading is a defect in one of them that somebody can go and settle.
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
    private const string DefaultCapture = "Telemetry/2026-09-14_13-27-46-345.tsv";

    /// <summary>
    /// The before-numbers, by ledger case. A share is in percent and a count is a count, matching
    /// what the rows carry, so a figure here can be read straight against a figure in a report.
    /// </summary>
    private static readonly (string Case, double Expected, double Tolerance)[] Pinned =
    {
        // Following: the offset along the player's travel direction on rows where the player moves.
        ("ahead-share/moving-rows", 7901, 0),
        ("ahead-share/share-ahead-beyond-64px", 10.80, 0.01),
        ("ahead-share/share-behind-beyond-48px", 71.48, 0.01),
        ("ahead-share/median-offset-px", -181, 0),

        // Commitment: moves released by their own owner with the body off the ground.
        ("cancelled-in-flight/Jump-airborne-idle", 36, 0),
        ("cancelled-in-flight/Drop-airborne-idle", 8, 0),
        ("cancelled-in-flight/FallThrough-airborne-idle", 2, 0),
        ("cancelled-in-flight/Walk-airborne-idle", 0, 0),

        // The same defect seen from the body's side.
        ("airborne-no-sideways-speed-stops/stops", 30, 0),
        ("airborne-no-sideways-speed-stops/ticks", 114, 0),
        ("stops-by-reason/inside-walk-step", 215, 0),
        ("stops-by-reason/during-replan", 60, 0),

        // Choice: which half of the switches a commitment margin could hold and which it could not.
        ("validity-flips/switches", 250, 0),
        ("validity-flips/share-invalid", 51.60, 0.01),
        ("validity-flips/invalid/hunt", 64, 0),
        ("validity-flips/invalid/guard", 53, 0),
        ("validity-flips/outscored/keep-company", 101, 0),

        // A budget cut reported as a proven impossibility.
        ("hunt-known-unusable-share/share", 48.72, 0.01),

        // Knowledge, arrival and journeys.
        ("reach-complete-share/share-complete", 56.34, 0.01),
        ("journeys-reached/WithPlayer", 5.86, 0.01),
        ("arrived-with-follow-gap/stretches", 1, 0),
        ("arrived-with-follow-gap/longest-stretch", 328, 0),

        // The hands are not the defect; the feet never arrive.
        ("hands-by-activity/keep-company/no-target", 97.94, 0.01),
        ("hands-by-activity/hunt/fired-or-cooldown", 43.37, 0.01),
    };

    /// <summary>Cases that must report themselves skipped on this capture, never silently produce a number.</summary>
    private static readonly string[] MustSkip = { "terrain-revision-rate/revisions-per-minute" };

    public static int Run()
    {
        string path = Environment.GetEnvironmentVariable(CaptureVariable) is { Length: > 0 } named
            ? named
            : DefaultCapture;
        if (!File.Exists(path))
        {
            Console.WriteLine($"play measures: SKIPPED — no capture at {path}. Telemetry/ is gitignored, so set {CaptureVariable} "
                + "to a capture to run this. Until it runs, the twenty-four before-numbers these measures reproduce are unverified in this checkout, "
                + "which is missing coverage rather than a clean result.");
            return 0;
        }

        Session session;
        try { session = Session.Load(path); }
        catch (Exception e)
        {
            Console.Error.WriteLine($"play measures: could not read {path}: {e.Message}");
            return 1;
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
            return 1;
        }
        Console.WriteLine($"play measures: {Pinned.Length} before-numbers and {MustSkip.Length} named skip reproduced against {Path.GetFileName(path)} ({rows.Length} rows emitted).");
        return 0;
    }
}
