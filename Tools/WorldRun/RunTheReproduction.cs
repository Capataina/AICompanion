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
            bool expectEveryTick = args.Contains("--expect-every-tick");
            // A folder this command is told it owns: removed when the verdict passes, kept and named when it does not,
            // because a failed reproduction's capture is the reproduction of the failure. verify's soak line uses it.
            string? ownFolder = WorldRunEntry.Value(args, "--own-folder=");
            int exit = ReproduceOne(args, suite, reproduce, world, ticks, dropped, printEach, expectEveryTick);
            if (ownFolder != null && Directory.Exists(ownFolder))
            {
                if (exit == 0) { Directory.Delete(ownFolder, recursive: true); Console.WriteLine($"REMOVED {ownFolder}, owned by this command, which exited 0"); }
                else Console.WriteLine($"KEPT {ownFolder}, the capture the failed reproduction read");
            }
            return exit;
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
        // A folder this run chose is this run's to remove, and it is removed only on a pass: a failed verdict's capture is
        // the reproduction of the failure, so it stays and the row names it. A folder the caller named is the caller's.
        string? requestedFolder = WorldRunEntry.Value(args, "--record-to=");
        string recordFolder = requestedFolder ?? Path.Combine(Path.GetTempPath(), "aicompanion-self-consistency",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture));
        string written = RecordAScene(source, world, WorldRunEntry.Int(args, "--from-tick=", 1), WorldRunEntry.Int(args, "--ticks=", 600), recordFolder,
            WorldRunEntry.Value(args, "--companion-slot=") is { } slot ? int.Parse(slot, CultureInfo.InvariantCulture) : null,
            WorldRunEntry.Value(args, "--knock-at=") is { } knock ? int.Parse(knock, CultureInfo.InvariantCulture) : null);
        Console.WriteLine($"RECORDED {written}");
        var own = ReadReplayInputs.Read(written);
        if (own.Refusal == null)
            EmitLedgerRows.Measure(ScoreTheRun.Instrument, consistencySuite, "replay-input characters per recorded tick",
                own.Frames.Sum(frame => (double)frame.Characters) / own.Frames.Count, "chars", direction: "lower", mode: "self-consistency",
                tags: new[] { EmitLedgerRows.SampledTag },
                message: "the `replay-inputs` detail the recorder wrote per companion tick over the recording pass, before the sidecar's JSON envelope");

        // The reproduction runs in a process of its own, because the verdict is about what a capture carries and a second
        // brain in one process is a different question. That question had a real answer: reproduced in the process that
        // recorded it, a capture first came back 297 of 300 against a fresh process's 300, and the cause was the light
        // sense's world light surviving the harness's reset (`ForgetEverythingLearnedAboutTheWorld` forgets it now). The
        // next state of that kind would be charged to the record here if the passes shared a process; `--reproduce
        // --passes=N` is where it shows instead.
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
        if (WorldRunEntry.Value(args, "--drop-input-check=") is { } checks) child.ArgumentList.Add($"--drop-input-check={checks}");
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
            Console.WriteLine($"KEPT {recordFolder}, the capture the failed reproduction read");
            return 1;
        }
        if (process.ExitCode == 0 && requestedFolder == null)
        {
            Directory.Delete(recordFolder, recursive: true);
            Console.WriteLine($"REMOVED {recordFolder}, the capture this passing run recorded");
        }
        else Console.WriteLine($"KEPT {recordFolder}");
        return process.ExitCode;
    }

    private static string Tail(string text) => text.Length <= 600 ? text : text[^600..];

    /// <summary>
    /// One capture reproduced: the dropped-input checks first when asked for, then the verdict (with
    /// `--expect-every-tick`) or the share. Refusals are a fail for the verdict — the recorder under test wrote that capture —
    /// and a named skip for the share, because a played capture this host cannot reproduce is not a regression.
    /// </summary>
    private static int ReproduceOne(string[] args, string suite, string reproduce, string? world, int ticks,
        ReproduceTheCapture.DroppedInput dropped, bool printEach, bool expectEveryTick)
    {
        // The world first: a machine with no .wld skips the run that would have written the capture, so a missing capture
        // there is the same skip and never a red on a fresh clone.
        if (world == null || !File.Exists(world))
            return Skip(suite, expectEveryTick ? VerdictCase : ShareCase, $"no world file at {world ?? "<none given>"}; a .wld is never committed, so the world must be named with --world=");
        if (!File.Exists(reproduce))
        {
            string absent = $"no capture at {reproduce}; Telemetry/ is gitignored and lives only in the main checkout, so name it absolutely";
            if (!expectEveryTick) return Skip(suite, ShareCase, absent);
            // With the verdict expected and a world to run in, the capture was written by the run that called this one, so its
            // absence is that run failing to record, not a question that could not be asked.
            EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, VerdictCase, $"the verdict was expected and {absent}", mode: "self-consistency");
            return 1;
        }
        var record = ReadReplayInputs.Read(reproduce);
        string? refusal = record.Refusal ?? ReproduceTheCapture.Unplaceable(record);
        if (refusal != null)
        {
            if (!expectEveryTick) return Skip(suite, ShareCase, refusal);
            // The self-consistency child: the recorder under test wrote this capture a moment ago, so one it cannot
            // read back is the recorder failing rather than a missing input — a fail, not a skip.
            EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, VerdictCase,
                $"the capture this run just recorded cannot be read back as replay inputs: {refusal}", mode: "self-consistency");
            return 1;
        }
        Main.dedServ = true;
        // A played capture is reseated by default and a synthetic one compared strictly; the verdict is always strict.
        var mode = expectEveryTick || args.Contains("--strict") ? ReproduceTheCapture.Mode.Strict
            : args.Contains("--reseat") || !record.Synthetic ? ReproduceTheCapture.Mode.Reseat : ReproduceTheCapture.Mode.Strict;
        string? recordTo = WorldRunEntry.Value(args, "--record-to=");

        int failures = 0;
        if (WorldRunEntry.Value(args, "--drop-input-check=") is { } checks)
            failures += CheckEveryDroppedInputDiverges(suite, reproduce, world, record, ticks, checks);

        // `--passes=N` reproduces the same capture N times in this one process and files the last. A fresh process
        // is what the verdict uses; this is the instrument for the state that survives between two brains in one
        // process, which the first pass cannot show and every later pass does.
        ReproduceTheCapture.Result result = null!;
        for (int pass = 0, passes = Math.Max(1, WorldRunEntry.Int(args, "--passes=", 1)); pass < passes; pass++)
            result = Reproduce(reproduce, world, record, ticks, dropped, recordTo, printEach, mode, stopAtFirstDivergence: false);
        string message = ReproduceTheCapture.Describe(result);
        if (!expectEveryTick)
        {
            // A played capture's share is about decisions under inputs the record cannot hold, so the row says which.
            if (!record.Synthetic) message += "; a played capture: this reproduction cannot carry " + ReproduceTheCapture.UncarriedForAPlayedCapture;
            EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, ShareCase, 100.0 * result.Reproduced / Math.Max(1, result.Ticks), "%",
                direction: "higher", mode: mode == ReproduceTheCapture.Mode.Reseat ? "reproduce-reseat" : "reproduce", message: message);
            return failures;
        }
        if (result.Reproduced == result.Ticks && result.Ticks > 0)
        {
            EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, VerdictCase, message, mode: "self-consistency");
            return failures;
        }
        EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, VerdictCase, message, mode: "self-consistency");
        return failures + 1;
    }

    public const string DroppedInputCase = "leaving one input out of a reproduction turns it red";

    /// <summary>
    /// The standing check behind the self-consistency verdict: the same capture reproduced with each named input left out,
    /// each run stopping at its first divergence, and each required to diverge — one row per input. The runs share this
    /// process and precede the verdict's own full reproduction, so if a dropped run left state behind that made the next
    /// reproduction disagree, the verdict after them would go red rather than a dropped row going green for the wrong
    /// reason. An input the capture gives nothing to exercise is a named skip, never a pass: a scene with no world edit
    /// cannot show that leaving the edits out matters, and the world run pins the light scanner's seed and drives it the
    /// same way in the recording and the replay, so leaving the seed out of a synthetic capture changes nothing by design.
    /// </summary>
    private static int CheckEveryDroppedInputDiverges(string suite, string capture, string world, ReadReplayInputs.Record record, int ticks, string list)
    {
        int failures = 0;
        foreach (string name in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ReproduceTheCapture.DroppedInput input = ParseDropped(name);
            string @case = $"{DroppedInputCase}: {name}";
            if (Unexercised(record, input) is { } vacuous) { Skip(suite, @case, vacuous); continue; }
            var result = Reproduce(capture, world, record, ticks, input, null, false, ReproduceTheCapture.Mode.Strict, stopAtFirstDivergence: true);
            string message = ReproduceTheCapture.Describe(result);
            if (result.First != null)
                EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, @case, $"diverged as it must: {message}", durationMs: result.Seconds * 1000, mode: "drop-input");
            else
            {
                failures++;
                EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, @case,
                    $"the capture reproduced without {name}, so the verdict cannot tell whether the record carries it: {message}", durationMs: result.Seconds * 1000, mode: "drop-input");
            }
        }
        return failures;
    }

    /// <summary>Why leaving <paramref name="input"/> out of this record could not change anything, or null when it could.</summary>
    private static string? Unexercised(ReadReplayInputs.Record record, ReproduceTheCapture.DroppedInput input) => input switch
    {
        ReproduceTheCapture.DroppedInput.Edits when !record.Frames.Any(frame => frame.WorldEdits.Count > 0)
            => "the capture holds no world edit, so leaving the edits out cannot be seen; the seeded soak's capture is the one that exercises them",
        ReproduceTheCapture.DroppedInput.Random when !record.Frames.Any(frame => frame.Random != null || frame.GenRandom != null)
            => "no tick of the capture drew from Main.rand or WorldGen.genRand",
        ReproduceTheCapture.DroppedInput.Light when record.Synthetic
            => "a world-run capture pins the light scanner's seed and the replay drives the scanner the same way, so leaving the recorded seed out changes nothing by construction; only a played capture can exercise it",
        _ => null,
    };

    /// <summary>
    /// Play the source capture's scene with the mod's recorder attached, as `--play-measures` does, and return the capture
    /// it wrote. With <paramref name="companionSlot"/> the companion is moved to that NPC slot after it is attached and a
    /// zombie is stood in slot 0 a few tiles from the player — the shape of a played world, where a town NPC or a hostile
    /// holds the low slots — so the reproduction has to put the companion back in its own slot to place every actor. With
    /// <paramref name="knockAt"/> the body is shoved forty pixels sideways at six pixels a tick after that recorded tick,
    /// outside the companion's tick, as a hostile's knockback lands in play: the record cannot carry it, which is what the
    /// reproduction's reseat mode exists for, and a strict reproduction of such a capture diverges there by design.
    /// </summary>
    private static string RecordAScene(string source, string world, int fromTick, int ticks, string folder, int? companionSlot = null, int? knockAt = null)
    {
        if (knockAt is { } knockTick)
            RunTheWorld.AfterTheCompanionTicks = (knocked, tick) =>
            {
                if (tick != knockTick) return;
                knocked.NPC.position.X += 40f;
                knocked.NPC.velocity.X = 6f;
                Console.WriteLine($"KNOCKED the body 40 px sideways after recorded tick {tick}, outside the companion's tick");
            };
        var route = ReadRecordedRoute.Read(source, fromTick, ticks);
        LoadTheSavedWorld.Load(world);
        var cast = ReadRecordedActors.Read(source, route.Steps.Max(s => s.Loot));
        var stage = new StageRecordedActors(cast, StageRecordedActors.HostileMotion.Native);
        Console.WriteLine("PREFERENCES " + ApplyTheRecordedPreferences.From(cast.Configuration, route.ConfigLine));
        RunTheWorld.Actors = stage;
        RunTheWorld.ProductionClock = true;
        PrepareTheHeadlessEngine.ForgetEverythingLearnedAboutTheWorld();
        string telemetry = "";
        RunTheWorld.AfterTheCompanionIsAttached = () =>
        {
            if (companionSlot is { } slot)
            {
                if (slot <= 0 || cast.Hostiles.Any(hostile => hostile.Slot == slot))
                    throw new ArgumentException($"--companion-slot={slot} must be a slot above 0 that the source capture's cast does not use");
                var attached = (live::AICompanion.Companion.CharacterBody.CompanionNPC)Main.npc[0].ModNPC;
                PrepareTheHeadlessEngine.MoveTheCompanionToSlot(attached, slot);
                NPC stranger = Main.npc[0];
                stranger.SetDefaults(Terraria.ID.NPCID.Zombie);
                stranger.whoAmI = 0;
                stranger.Bottom = Main.player[0].Bottom + new Microsoft.Xna.Framework.Vector2(6 * 16f, 0f);
                stranger.active = true;
                Console.WriteLine($"COMPANION moved to NPC slot {slot}; a zombie stands in slot 0 at {stranger.Bottom}");
            }
            telemetry = AttachTheRecorder.Open(folder, route.Capture);
        };
        try
        {
            var run = RunTheWorld.Play(route, "self-consistency recording pass", seed: 1);
            Console.WriteLine($"RECORDING PASS {run.Ticks} ticks in {run.Seconds:0.0}s under the production clock; {stage.Describe()}");
        }
        finally
        {
            AttachTheRecorder.Close();
            RunTheWorld.AfterTheCompanionIsAttached = null;
            RunTheWorld.AfterTheCompanionTicks = null;
            RunTheWorld.Actors = null;
            RunTheWorld.ProductionClock = false;
        }
        return Directory.GetFiles(telemetry, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            ?? throw new InvalidOperationException($"the recording pass wrote no capture under {telemetry}");
    }

    private static ReproduceTheCapture.Result Reproduce(string capture, string world, ReadReplayInputs.Record record, int ticks,
        ReproduceTheCapture.DroppedInput dropped, string? recordTo, bool printEach, ReproduceTheCapture.Mode mode, bool stopAtFirstDivergence)
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
        var result = ReproduceTheCapture.Run(record, ticks, dropped, recordTo, printEach, seed, mode, stopAtFirstDivergence);
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
