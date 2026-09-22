extern alias live;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;
using Telemetry = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainTelemetry;

/// <summary>
/// The mod's own recorder, running inside a world run, writing where nothing that reads real
/// captures will ever look.
///
/// Until 2026-09-22 this folder attached no recorder at all, and the reason given was a good one:
/// a recorder left running drops a synthetic session into <c>Telemetry/</c> beside the sessions
/// somebody actually played, and nothing inside the file would tell a later reader that nobody ever
/// played it. The reason was right and the conclusion was too strong. What a world run needs is the
/// recorder's own account of a scene it can reproduce — the same columns, the same occurrences, the
/// same reader — and what it must not do is let that account be mistaken for play. So both halves
/// are answered rather than one of them being paid for with the other:
///
/// <list type="bullet">
/// <item>the capture lands under a directory of this run's own choosing, outside the repository and
/// outside the folder <c>SessionReport</c>, <c>CombatAudit</c> and <c>backfill-capture.sh</c>
/// scan, so no reader can reach it by accident; and</item>
/// <item>its header carries <c>synthetic=world-run</c> and the capture it was replayed from, so a
/// reader who reaches it on purpose is told on the first screen.</item>
/// </list>
///
/// The one place this reaches into the mod is the header line, which goes in through the same
/// enqueue the recorder's own metadata uses. That method is internal, so it is called by reflection
/// and the absence of it is a refusal rather than a silently unmarked capture — an unmarked
/// synthetic capture is the exact thing the old trap paragraph was protecting against.
/// </summary>
internal static class AttachTheRecorder
{
    private static Telemetry? recorder;
    private static string? folder;
    private static string? sourceCapture;

    /// <summary>Where a capture lands when the caller names no directory: outside the repository and outside every reader's scan.</summary>
    private static string DefaultRoot => Path.Combine(Path.GetTempPath(), "aicompanion-world-run");

    /// <summary>What this run wrote, for the line the run prints and for every row's message.</summary>
    public static string Describe()
        => recorder == null
            ? "not attached"
            : $"writing a synthetic capture under {Telemetry.Folder}, marked synthetic=world-run;source-capture={sourceCapture}; "
            + "no reader of real captures scans that folder";

    /// <summary>
    /// Starts a recording, and returns the folder it will land in.
    ///
    /// The save path is moved first because <see cref="Telemetry.Folder"/> is derived from it, and
    /// it is moved rather than the recorder being told where to write because the recorder has no
    /// such parameter and inventing one would be a change to the mod for a tool's convenience.
    /// </summary>
    public static string Open(string? requestedFolder, string capture)
    {
        folder = requestedFolder ?? DefaultRoot;
        Directory.CreateDirectory(folder);
        sourceCapture = capture;
        // `Main.SavePath` is a read-only property over `Terraria.Program.SavePath`, which `Program.cs`
        // has already set to a temporary directory before anything touched `Main`. Moving it again
        // here is what puts the capture somewhere this run names and prints, rather than in whatever
        // the operating system's temporary directory happens to be.
        (typeof(Terraria.Program).GetField("SavePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("Terraria.Program.SavePath is gone, so a world-run capture could not be steered away from the real ones"))
            .SetValue(null, folder);

        recorder = new Telemetry();
        GiveItAModWithALogger(recorder);
        recorder.OnWorldLoad();
        RefuseARecordingThatAlreadyGaveUp();
        MarkTheCaptureSynthetic();
        return Telemetry.Folder;
    }

    /// <summary>
    /// Refuses a recorder that failed inside its own open, rather than letting the run finish and
    /// leave a capture nobody can read.
    ///
    /// <c>BrainTelemetry.OnWorldLoad</c> catches everything and closes, so a failed open is not an
    /// exception the caller sees: it is a file with five header lines, no column header, no rows and
    /// <c>end=recorder-initialization-failed</c> in its trailer — which is exactly what the first
    /// version of this attach produced, because it ran before <c>AttachCompanion</c> built
    /// <c>Main.player[0]</c> and the capabilities line reads the player's mount.
    ///
    /// The field checked is the diagnostic writer and not the <c>writer</c> beside it, and the
    /// difference is the kind of thing only a run teaches: a *successful* open reserves the file
    /// through <c>writer</c>, hands the path to <c>FlushDiagnosticRecords.Start</c> and then
    /// disposes and nulls <c>writer</c> itself, so a check on it refuses every healthy recorder
    /// there is. The diagnostic writer is the one that is non-null exactly when a recording is
    /// running.
    /// </summary>
    private static void RefuseARecordingThatAlreadyGaveUp()
    {
        FieldInfo diagnostics = typeof(Telemetry).GetField("diagnosticWriter", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("BrainTelemetry.diagnosticWriter is gone; a failed recorder open can no longer be told from a working one");
        if (diagnostics.GetValue(null) == null)
            throw new InvalidOperationException(
                $"the recorder closed itself during its own open and wrote no rows; look in {Telemetry.Folder} for a capture ending "
                + "`recorder-initialization-failed`, and check what its metadata reached for that this host has not built yet");
    }

    /// <summary>How many rows the recording wrote, which is the only thing that separates a capture from a header.</summary>
    private static int RowsWritten
        => (int)(typeof(Telemetry).GetField("rowsWritten", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) ?? 0);

    /// <summary>
    /// Closes the recording and says how much of one it was.
    ///
    /// The row count is printed rather than only the path, because a capture with a header and no
    /// rows is the shape every one of this attach's failures took and it is indistinguishable from
    /// a real one by its name, its timestamp or its existence.
    /// </summary>
    public static void Close()
    {
        if (recorder == null) return;
        int rows = RowsWritten;
        recorder.OnWorldUnload();
        recorder = null;
        Console.WriteLine($"RECORDER closed after {rows.ToString(System.Globalization.CultureInfo.InvariantCulture)} rows, under {Telemetry.Folder}");
    }

    /// <summary>
    /// Hands the recorder the mod this host pretends to have.
    ///
    /// <c>BrainTelemetry.OnWorldLoad</c> ends with <c>Mod.Logger.Info</c> and its own failure path
    /// ends with <c>Mod.Logger.Error</c>, so a recorder whose <c>Mod</c> is null throws out of the
    /// attach after having already opened the files — the worst of both, a started recording and a
    /// failed call. The loader normally sets it; this host has no loader, so it is set here from the
    /// one instance <see cref="PrepareTheHeadlessEngine.TheMod"/> registers, rather than from a
    /// second one, because two registered instances make <c>ContentInstance&lt;T&gt;.Instance</c> null
    /// and every production line that logs through it starts throwing instead.
    /// </summary>
    private static void GiveItAModWithALogger(Telemetry instance)
    {
        PropertyInfo owner = typeof(ModType).GetProperty("Mod", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException("ModType.Mod is gone; the recorder cannot be attached to a mod");
        owner.SetValue(instance, PrepareTheHeadlessEngine.TheMod);
    }

    /// <summary>
    /// The line that stops this capture from ever being read as play.
    ///
    /// It goes in immediately after the recorder's own metadata and before the column header, which
    /// is where the header comments live: the header row is not written until the first recorded
    /// tick. A missing enqueue is a refusal rather than a warning, because an unmarked synthetic
    /// capture is the whole hazard.
    /// </summary>
    private static void MarkTheCaptureSynthetic()
    {
        Type queue = typeof(Telemetry).Assembly
            .GetType("AICompanion.Companion.Brain.Infrastructure.Diagnostics.QueueDiagnosticRecords")
            ?? throw new MissingMemberException("QueueDiagnosticRecords is gone; a world-run capture could not be marked synthetic");
        MethodInfo enqueue = queue.GetMethod("TryEnqueueTsv", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException("QueueDiagnosticRecords.TryEnqueueTsv is gone; a world-run capture could not be marked synthetic");
        enqueue.Invoke(null, new object[]
        {
            $"# synthetic=world-run;source-capture={sourceCapture};"
            + "note=nobody played this; it is the world run replaying the source capture's player track, hostiles and drops"
        });
    }
}
