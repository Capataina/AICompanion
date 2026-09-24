extern alias live;
using System.Globalization;
using Terraria;
using AICompanion.Tools.Ledger;

/// <summary>
/// The two commands that play a capture back as its own inputs, and the rows they file.
///
/// `--reproduce=&lt;capture&gt;` reproduces one capture and files what share of its ticks came back. That is a
/// measure and never a pass line, because a played capture carries inputs no recorder here can hold — the game's
/// light scan over a screen this host does not have, the player's own chop and mine events — and how many of those a
/// window depends on is the thing being measured.
///
/// `--self-consistency` is the one verdict. It plays the source capture's scene under the production clock with the
/// mod's own recorder attached, exactly as `--play-measures` does, and then reproduces the capture that recorder just
/// wrote. Every input that capture depends on was written by the recorder under test into a world this process
/// loaded, so a tick that does not come back is an input the recorder does not carry, or one the reproduction does
/// not put back — never the play being unrecordable. So it must be every tick, and the row names the first tick and
/// field that is not.
/// </summary>
internal static class RunTheReproduction
{
    public const string VerdictCase = "a world-run capture reproduces its own decisions at every tick";
    public const string ShareCase = "ticks a capture reproduces from its own recorded inputs";

    public static int Run(string[] args)
    {
        string? world = WorldRunEntry.Value(args, "--world=");
        string? reproduce = WorldRunEntry.Value(args, "--reproduce=");
        int ticks = WorldRunEntry.Value(args, "--ticks=") is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : 0;
        var dropped = ParseDropped(WorldRunEntry.Value(args, "--drop-input="));
        bool printEach = args.Contains("--print-reproduction");
        RunTheWorld.DriveLight = !args.Contains("--no-light");

        if (reproduce != null)
        {
            string suite = WorldRunEntry.Value(args, "--suite=") ?? $"reproduce {Path.GetFileNameWithoutExtension(reproduce)}";
            if (!File.Exists(reproduce))
                return Skip(suite, ShareCase, $"no capture at {reproduce}; Telemetry/ is gitignored and lives only in the main checkout, so name it absolutely");
            if (world == null || !File.Exists(world))
                return Skip(suite, ShareCase, $"no world file at {world ?? "<none given>"}; a .wld is never committed, so the world must be named with --world=");
            bool expectEveryTick = args.Contains("--expect-every-tick");
            var record = ReadReplayInputs.Read(reproduce);
            if (record.Refusal != null)
            {
                if (!expectEveryTick) return Skip(suite, ShareCase, record.Refusal);
                // The self-consistency child: the recorder under test wrote this capture a moment ago, so one it cannot
                // read back is the recorder failing rather than a missing input — a fail, not a skip.
                EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, VerdictCase,
                    $"the capture this run just recorded cannot be read back as replay inputs: {record.Refusal}", mode: "self-consistency");
                return 1;
            }
            Main.dedServ = true;
            var result = Reproduce(reproduce, world, record, ticks, dropped, WorldRunEntry.Value(args, "--record-to="), printEach);
            if (!expectEveryTick)
            {
                EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, ShareCase, 100.0 * result.Reproduced / Math.Max(1, result.Ticks), "%",
                    direction: "higher", mode: "reproduce", message: ReproduceTheCapture.Describe(result));
                return 0;
            }
            string message = ReproduceTheCapture.Describe(result);
            if (result.Reproduced == result.Ticks && result.Ticks > 0)
            {
                EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, VerdictCase, message, mode: "self-consistency");
                return 0;
            }
            EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, VerdictCase, message, mode: "self-consistency");
            return 1;
        }

        string? source = WorldRunEntry.Value(args, "--route=");
        string consistencySuite = WorldRunEntry.Value(args, "--suite=")
            ?? $"self-consistency {Path.GetFileNameWithoutExtension(source ?? "no capture")}";
        if (source == null || !File.Exists(source))
            return Skip(consistencySuite, VerdictCase, $"no source capture at {source ?? "<none given>"}; the self-consistency run needs a recording "
                + "whose player track and scene it plays, and Telemetry/ is gitignored, so name it absolutely");
        if (world == null || !File.Exists(world))
            return Skip(consistencySuite, VerdictCase, $"no world file at {world ?? "<none given>"}; a .wld is never committed, so the world must be named with --world=");
        Main.dedServ = true;

        // Six hundred ticks unless told otherwise, the recorded route's own default; `--ticks=0` is the whole capture.
        string written = RecordAScene(source, world, WorldRunEntry.Int(args, "--from-tick=", 1), WorldRunEntry.Int(args, "--ticks=", 600),
            WorldRunEntry.Value(args, "--record-to=") ?? Path.Combine(Path.GetTempPath(), "aicompanion-self-consistency", DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture)));
        Console.WriteLine($"RECORDED {written}");
        var own = ReadReplayInputs.Read(written);
        if (own.Refusal == null)
            EmitLedgerRows.Measure(ScoreTheRun.Instrument, consistencySuite, "replay-input characters per recorded tick",
                own.Frames.Sum(frame => (double)frame.Characters) / own.Frames.Count, "chars", direction: "lower", mode: "self-consistency",
                tags: new[] { EmitLedgerRows.SampledTag },
                message: "the `replay-inputs` detail the recorder wrote per companion tick over the recording pass, before the sidecar's JSON envelope");

        // The reproduction runs in a process of its own. Reproduced in the process that recorded it, the same capture
        // disagreed with itself at its first tick (297 of 300 on 24 September 2026) while a fresh process reproduced all
        // 300: a second brain in one process inherits state the harness's reset does not clear, which is the
        // determinism section's cold-pass class, and a verdict about what a capture carries must not be charged for it.
        var child = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        child.ArgumentList.Add(typeof(RunTheReproduction).Assembly.Location);
        foreach (string argument in new[] { $"--reproduce={written}", $"--world={world}", $"--suite={consistencySuite}", "--expect-every-tick" })
            child.ArgumentList.Add(argument);
        if (dropped != ReproduceTheCapture.DroppedInput.None) child.ArgumentList.Add($"--drop-input={WorldRunEntry.Value(args, "--drop-input=")}");
        if (printEach) child.ArgumentList.Add("--print-reproduction");
        if (!RunTheWorld.DriveLight) child.ArgumentList.Add("--no-light");
        using var process = System.Diagnostics.Process.Start(child)
            ?? throw new InvalidOperationException($"the reproduction process could not be started from {child.FileName}");
        Console.WriteLine($"CHILD pid {process.Id} reproducing {Path.GetFileName(written)}");
        var errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Console.Write(output);
        Console.Write(errors.Result);
        Console.WriteLine($"CHILD pid {process.Id} exited {process.ExitCode}");
        // The child files its rows to the same ledger run through AIC_LEDGER_RUN; a child that died without filing
        // the verdict is a failure of this command, named here so the scoreboard does not read a silence.
        if (!output.Contains(VerdictCase, StringComparison.Ordinal))
        {
            EmitLedgerRows.Fail(ScoreTheRun.Instrument, consistencySuite, VerdictCase,
                $"the reproduction process exited {process.ExitCode} without filing the verdict; its last output: {Tail(output + errors.Result)}",
                mode: "self-consistency");
            return 1;
        }
        return process.ExitCode;
    }

    private static string Tail(string text) => text.Length <= 600 ? text : text[^600..];

    /// <summary>Play the source capture's scene with the mod's recorder attached, as `--play-measures` does, and return
    /// the capture it wrote.</summary>
    private static string RecordAScene(string source, string world, int fromTick, int ticks, string folder)
    {
        var route = ReadRecordedRoute.Read(source, fromTick, ticks);
        LoadTheSavedWorld.Load(world);
        var cast = ReadRecordedActors.Read(source, route.Steps.Max(s => s.Loot));
        var stage = new StageRecordedActors(cast, StageRecordedActors.HostileMotion.Native);
        Console.WriteLine("PREFERENCES " + ApplyTheRecordedPreferences.From(cast.Configuration, route.ConfigLine));
        RunTheWorld.Actors = stage;
        RunTheWorld.ProductionClock = true;
        PrepareTheHeadlessEngine.ForgetEverythingLearnedAboutTheWorld();
        string telemetry = "";
        RunTheWorld.AfterTheCompanionIsAttached = () => telemetry = AttachTheRecorder.Open(folder, route.Capture);
        try
        {
            var run = RunTheWorld.Play(route, "self-consistency recording pass", seed: 1);
            Console.WriteLine($"RECORDING PASS {run.Ticks} ticks in {run.Seconds:0.0}s under the production clock; {stage.Describe()}");
        }
        finally
        {
            AttachTheRecorder.Close();
            RunTheWorld.AfterTheCompanionIsAttached = null;
            RunTheWorld.Actors = null;
            RunTheWorld.ProductionClock = false;
        }
        return Directory.GetFiles(telemetry, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            ?? throw new InvalidOperationException($"the recording pass wrote no capture under {telemetry}");
    }

    private static ReproduceTheCapture.Result Reproduce(string capture, string world, ReadReplayInputs.Record record, int ticks,
        ReproduceTheCapture.DroppedInput dropped, string? recordTo, bool printEach)
    {
        // The tiles as the recording found them: the saved world, reloaded, because a pass before this one may have
        // mined and placed torches in it, and every retained search forgotten.
        var loaded = LoadTheSavedWorld.Load(world);
        var route = ReadRecordedRoute.Read(capture, 1, 1);
        var cast = ReadRecordedActors.Read(capture, 0);
        Console.WriteLine("PREFERENCES " + ApplyTheRecordedPreferences.From(cast.Configuration, route.ConfigLine));
        PrepareTheHeadlessEngine.ForgetEverythingLearnedAboutTheWorld();
        foreach (Item item in Main.item) if (item != null) item.active = false;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false;
        int seed = SeedOf(record.SyntheticLine);
        Console.WriteLine($"REPRODUCE {record.Capture} schema={record.Schema} frames={record.Frames.Count} world={loaded.Name} {loaded.Hash}"
            + (record.Synthetic ? $" ({record.SyntheticLine.Split(';')[0]}, seed {seed})" : " (a played capture)")
            + (dropped == ReproduceTheCapture.DroppedInput.None ? "" : $" leaving out {dropped}"));
        var result = ReproduceTheCapture.Run(record, ticks, dropped, recordTo, printEach, seed);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"REPRODUCED in {result.Seconds:0.0}s: ") + ReproduceTheCapture.Describe(result));
        return result;
    }

    /// <summary>The random seed a world-run capture's recording pass pinned; every world run pins one, and it is 1.</summary>
    private static int SeedOf(string syntheticLine)
    {
        foreach (string part in syntheticLine.Split(';'))
            if (part.StartsWith("seed=", StringComparison.Ordinal) && int.TryParse(part[5..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
                return seed;
        return 1;
    }

    private static ReproduceTheCapture.DroppedInput ParseDropped(string? text) => text switch
    {
        null or "" => ReproduceTheCapture.DroppedInput.None,
        "actors" => ReproduceTheCapture.DroppedInput.Actors,
        "clock" => ReproduceTheCapture.DroppedInput.Clock,
        "random" => ReproduceTheCapture.DroppedInput.Random,
        "light" => ReproduceTheCapture.DroppedInput.Light,
        "player" => ReproduceTheCapture.DroppedInput.Player,
        "edits" => ReproduceTheCapture.DroppedInput.Edits,
        _ => throw new ArgumentException($"--drop-input= takes actors, clock, random, light, player or edits; got \"{text}\""),
    };

    private static int Skip(string suite, string @case, string reason)
    {
        EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, @case, reason);
        Console.WriteLine($"SKIP {reason}");
        return 0;
    }
}
