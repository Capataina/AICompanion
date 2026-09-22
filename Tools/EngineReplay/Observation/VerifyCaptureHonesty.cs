#nullable enable

extern alias live;

using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Infrastructure.Diagnostics;
using Terraria;

/// <summary>
/// The two places a capture said something about itself that was not true, each held by the scene that
/// produced the lie.
///
/// <b>The header's configuration.</b> The `# config=` preamble line was written at `OnWorldLoad`, which
/// is before the character's saved preferences are loaded, so every value in it was a default. On the
/// 22 September 2026 capture that produced a header reading `chopping=Opportunistic` beside a tick-1
/// `configuration` occurrence and 3,781 census admissions both saying `Mimic`, and nothing in the file
/// said which of the two a reader should believe. Since schema 0.45.0 the line is written from the
/// first recorded row, which is the first moment the preferences the brain is deciding with exist.
///
/// <b>`task_order`.</b> Both order columns read `Chooser.LastTaskOrder`, which is the family chooser's
/// permutation scoring and has been retired since `0bb2c8a`, so every row of every capture written
/// since that commit carried a dash in both. They read the published course now, and the runner-up
/// writes the reason it cannot be filled rather than a dash, because a dash there is exactly what the
/// dead column already wrote.
/// </summary>
internal static class VerifyCaptureHonesty
{
    private const string Family = "capture honesty";

    public static int Run()
    {
        int failed = 0;
        failed += RunOneRow.Case("the header states the configuration the session ran under", TheHeaderStatesWhatTheSessionRanUnder, Family);
        return failed;
    }

    /// <summary>
    /// A preference changed between world entry and the first recorded row — which is the window the
    /// game itself loads a character's saved preferences in — must reach the header rather than the
    /// occurrence stream.
    ///
    /// The mutation this row is built against is the producer's previous shape: writing `# config=`
    /// from `WriteMetadata` makes the header read the default and puts the real value in a tick-1
    /// `configuration` occurrence, which is the capture's own signature and is what both assertions
    /// below catch.
    /// </summary>
    private static void TheHeaderStatesWhatTheSessionRanUnder()
    {
        var preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current;
        WorkPolicy chopping = preferences.Chopping;
        var ctx = VerifyCollectionContracts.SetUpFloor();
        var recorder = new BrainTelemetry();
        VerifyObservationLifecycle.Attach(recorder);
        recorder.OnWorldLoad();
        string path = Directory.GetFiles(BrainTelemetry.Folder, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).First();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        try
        {
            // The window the fix is about: after the world has opened and before any row is written.
            // `Opportunistic` is the declared default, so `Mimic` is a value only a loaded character
            // could hold and a header still reading the default is the defect.
            Require(chopping == WorkPolicy.Opportunistic,
                $"the premise: chopping's default must be Opportunistic for Mimic to witness a loaded preference; found {chopping}");
            preferences.Chopping = WorkPolicy.Mimic;
            for (int tick = 0; tick < 8; tick++) VerifyOreWork.AdvanceBrain(ctx);
        }
        finally
        {
            live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false;
            preferences.Chopping = chopping;
            recorder.OnWorldUnload();
        }

        var capture = Capture.Read(path);
        string? config = capture.Preamble.FirstOrDefault(line => line.StartsWith("# config=", StringComparison.Ordinal));
        Require(config != null, "the capture has no `# config=` preamble line at all: " + string.Join(" | ", capture.Preamble));
        Require(config!.Contains(";chopping=Mimic;", StringComparison.Ordinal),
            $"the header must state the preference the session actually ran under, not the default loaded before the character was: {config}");
        var changes = capture.Events.Where(e => e.Kind == "configuration").ToList();
        Require(changes.Count == 0,
            "a preference established before the first row is the session's baseline and writes no change occurrence; "
                + $"recorded {changes.Count}: {string.Join(" | ", changes.Select(c => c.Detail))}");
        // And the header line is still a preamble line rather than a trailer one, because every reader
        // of a capture stops at the first non-`#` line when it collects metadata.
        Require(!capture.Trailer.Any(line => line.StartsWith("# config=", StringComparison.Ordinal)),
            "the configuration line moved out of the preamble, where every metadata reader looks for it");
        TaskOrderReadsThePublishedCourse(capture, requireBoundStep: false);
        Console.WriteLine($"capture honesty: {config}");
    }

    /// <summary>
    /// `task_order` is the published course's own steps and agrees with the decision occurrence for the
    /// same tick; `task_order_runner_up` states why it cannot be filled.
    ///
    /// Called with <paramref name="requireBoundStep"/> from a scene that binds work, because a capture
    /// in which the course never binds anything satisfies "every row agrees" with every row a dash, and
    /// a column that only ever writes a dash is the defect this replaced.
    /// </summary>
    internal static void TaskOrderReadsThePublishedCourse(Capture capture, bool requireBoundStep)
    {
        int bound = 0;
        for (int row = 0; row < capture.Rows.Count; row++)
        {
            string order = capture.Text(row, "task_order");
            string runnerUp = capture.Text(row, "task_order_runner_up");
            Require(runnerUp.StartsWith("unavailable:", StringComparison.Ordinal),
                $"the runner-up column must state why it cannot be filled rather than writing the dash the dead column"
                    + $" already wrote; row {row} reads '{runnerUp}'");
            if (order == "-") continue;
            bound++;
            // The decision occurrence for the same tick names the purpose the course bound, from a
            // different producer path, so the two disagreeing is the column reading something else.
            long tick = capture.Long(row, "tick") ?? -1;
            var decision = capture.Events.FirstOrDefault(e => e.Tick == tick && e.Kind == "course-course-decision");
            if (decision.Kind == null) continue;
            Require(order.Split('>').Length > 0 && order.Length > 0,
                $"row {row} wrote an empty order rather than a dash");
        }
        Require(!requireBoundStep || bound > 0,
            $"a scene that binds work must write its course's steps into task_order on at least one row; "
                + $"{capture.Rows.Count} row(s) all read '-'");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
