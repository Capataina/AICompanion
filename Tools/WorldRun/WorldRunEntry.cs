extern alias live;
using System.Globalization;
using Terraria;
using AICompanion.Tools.Ledger;

/// <summary>
/// The flag table, kept out of <c>Program.cs</c> so nothing here runs before the save path is
/// redirected and the library resolver is attached — a top-level statement file's statics are the
/// first thing the runtime touches, and <see cref="Main"/>'s static constructor must not be that
/// thing.
/// </summary>
internal static class WorldRunEntry
{
    /// <summary>
    /// How many ticks the default run plays.
    ///
    /// The full 13:27 capture is 22,473 ticks and every one of them is the whole brain with its
    /// planning allowances lifted, run twice for the determinism row. That is a long time to add to
    /// a check people run before every commit, so the suite plays a slice and the whole capture is
    /// a command someone runs on purpose. The slice is long enough for the body to travel, replan
    /// and be interrupted several times over, which is what the rows read.
    /// </summary>
    private const int DefaultTicks = 600;

    /// <summary>
    /// How often a checkpoint is taken along the player's track, in ticks.
    ///
    /// Consecutive ticks are the same tile, so a checkpoint per tick would weight a doorway the
    /// player stood in as heavily as a whole journey. At a walking pace this is a few tiles apart.
    /// </summary>
    private const int CheckpointCadence = 30;

    public static int Run(string[] args)
    {
        if (args.Contains("--help")) { Usage(); return 0; }

        string? capture = Value(args, "--route=");
        string? scenario = Value(args, "--scenario=");
        string? world = Value(args, "--world=");
        int fromTick = Int(args, "--from-tick=", 1);
        int ticks = Int(args, "--ticks=", DefaultTicks);
        string suite = Value(args, "--suite=") ?? (scenario != null ? "scenario " + Path.GetFileNameWithoutExtension(scenario) : "recorded route");

        // The soak: no capture at all, because its player is generated. It is the one command here whose
        // length is the point — the climb it looks for took thirty-three seconds of play to become
        // visible and the longest recording this machine holds is six minutes — so it takes its track
        // from a seed rather than from a recording, and `--ticks` is how long it runs for.
        if (args.Contains("--soak"))
        {
            if (world == null || !File.Exists(world))
            {
                string reason = $"no world file at {world ?? "<none given>"}; a .wld is never committed, so the soak "
                    + "cannot run on a machine without one";
                EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, "the frozen observation's floor does not climb over a long run", reason);
                Console.WriteLine($"SKIP {reason}");
                return 0;
            }
            Main.dedServ = true;
            int soakFailures = RunTheSoak.Run(world, Int(args, "--seed=", 1),
                Value(args, "--ticks=") is null ? 7200 : ticks,
                Value(args, "--suite=") ?? "soak", Value(args, "--record-to="), !args.Contains("--no-light"));
            foreach (LedgerRow row in EmitLedgerRows.Emitted)
                Console.WriteLine($"{row.Verdict.ToUpperInvariant()} {row.Case}"
                    + (row.Value is { } soakValue ? $" = {soakValue.ToString("0.###", CultureInfo.InvariantCulture)} {row.Unit}" : "")
                    + (row.Message.Length > 0 ? $" :: {row.Message}" : ""));
            return soakFailures == 0 ? 0 : 1;
        }

        // The three cost modes. Each files measures only — the owner's ruling of 24 September 2026 is that
        // no time is a pass line — and each is a command someone runs on purpose, reached from verify only
        // through its perf tier, because each costs minutes of the machine.
        if (args.Contains("--load-ladder"))
        {
            if (MissingWorld(world, Value(args, "--suite=") ?? "load ladder", "brain cost per tick by hostiles near the player") is { } skipped) return skipped;
            Main.dedServ = true;
            int ladderFailures = RunTheLoadLadder.Run(world!, Int(args, "--seed=", 1),
                Value(args, "--ticks=") is null ? RunTheLoadLadder.DefaultDwellTicks : ticks,
                Value(args, "--rungs=") is { } rungs ? Numbers(rungs, "--rungs=", zeroAllowed: true).Select(r => (int)r).ToArray() : RunTheLoadLadder.DefaultRungs,
                Value(args, "--suite=") ?? "load ladder", !args.Contains("--no-light"), !args.Contains("--no-cave"));
            PrintEmittedRows();
            return ladderFailures == 0 ? 0 : 1;
        }
        if (args.Contains("--budget-curve") || Value(args, "--calibrate=") is not null)
        {
            bool calibrating = Value(args, "--calibrate=") is not null;
            string mode = calibrating ? "calibrate" : "budget curve";
            string? source = calibrating ? Value(args, "--calibrate=") : capture;
            string defaultSuite = $"{mode} {Path.GetFileNameWithoutExtension(source ?? "no capture")}";
            string modeSuite = Value(args, "--suite=") ?? defaultSuite;
            string anchor = calibrating ? "in-game over headless brain cost at p50" : "decide cost p50 at a 12 ms allowance";
            if (source == null || !File.Exists(source))
            {
                string reason = $"no capture at {source ?? "<none given>"}; Telemetry/ is gitignored and lives only in the main checkout, so name it absolutely";
                EmitLedgerRows.Skipped(ScoreTheRun.Instrument, modeSuite, anchor, reason);
                Console.WriteLine($"SKIP {reason}");
                return 0;
            }
            if (MissingWorld(world, modeSuite, anchor) is { } skipped) return skipped;
            Main.dedServ = true;
            int costFailures = calibrating
                ? RunTheCalibration.Run(source, world!, Value(args, "--ticks=") is null ? 0 : ticks, modeSuite, Value(args, "--record-to="))
                : RunTheBudgetCurve.Run(source, world!,
                    Value(args, "--allowances=") is { } list ? Numbers(list, "--allowances=", zeroAllowed: false) : RunTheBudgetCurve.DefaultAllowances,
                    Int(args, "--repeats=", 2), Value(args, "--ticks=") is null ? 0 : ticks, modeSuite);
            PrintEmittedRows();
            return costFailures == 0 ? 0 : 1;
        }

        // A capture played back as its own inputs, or a capture this run writes and then plays back: both are
        // RunTheReproduction's, which owns the rows, the refusals and the two passes.
        if (Value(args, "--reproduce=") is not null || args.Contains("--self-consistency"))
        {
            int reproductionFailures = RunTheReproduction.Run(args);
            PrintEmittedRows();
            return reproductionFailures == 0 ? 0 : 1;
        }

        if (capture == null && scenario == null) { Usage(); return 2; }

        // A committed scenario as a checkpoint. The scenario is in the repository, so only the world
        // can be absent, and an absent world is the same skip the recorded route files.
        if (scenario != null)
        {
            if (!File.Exists(scenario))
            {
                EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, "the orb reaches the player from where the recording left it", $"no scenario at {scenario}");
                Console.WriteLine($"SKIP no scenario at {scenario}");
                return 0;
            }
            if (world == null || !File.Exists(world))
            {
                string reason = $"no world file at {world ?? "<none given>"}; a .wld is never committed, so the world must be named with --world=";
                EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, "the orb reaches the player from where the recording left it", reason);
                Console.WriteLine($"SKIP {reason}");
                return 0;
            }
            Main.dedServ = true;
            int scenarioFailures = RunTheScenario.Run(scenario, world, suite, Value(args, "--ticks=") is null ? RunTheScenario.DefaultTickCount : ticks, !args.Contains("--no-light"));
            foreach (LedgerRow row in EmitLedgerRows.Emitted)
                Console.WriteLine($"{row.Verdict.ToUpperInvariant()} {row.Case}"
                    + (row.Value is { } value ? $" = {value.ToString("0.###", CultureInfo.InvariantCulture)} {row.Unit}" : "")
                    + (row.Message.Length > 0 ? $" :: {row.Message}" : ""));
            return scenarioFailures == 0 ? 0 : 1;
        }

        // A fresh clone has neither a capture nor a world: Telemetry/ is gitignored and a .wld is
        // never committed. That is an absence of evidence rather than a failure, so it files a skip
        // carrying its reason — a skip does not disqualify a run as a ledger baseline, and an error
        // would, which would leave the store with no baseline at all on any machine but this one.
        if (!File.Exists(capture))
        {
            EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, "a recorded route is replayed in its own world",
                $"no capture at {capture}; Telemetry/ is gitignored, so a fresh clone has nothing to replay until a playtest writes one");
            Console.WriteLine($"SKIP no capture at {capture}");
            return 0;
        }
        if (world == null || !File.Exists(world))
        {
            EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, "a recorded route is replayed in its own world",
                $"no world file at {world ?? "<none given>"}; a .wld is never committed, so the world must be named with --world=");
            Console.WriteLine($"SKIP no world file at {world ?? "<none given>"}");
            return 0;
        }

        Main.dedServ = true;
        RunTheWorld.DriveLight = !args.Contains("--no-light");
        RunTheWorld.CombatVariant = args.Contains("--combat");
        var route = ReadRecordedRoute.Read(capture, fromTick, ticks);
        var loaded = LoadTheSavedWorld.Load(world);
        string worldNote = DescribeWorldMatch(route, loaded);

        // The play measures: one pass, under the game's own clock, with the recording's hostiles and
        // drops in the world and the real recorder writing a quarantined capture. It is its own
        // command rather than extra rows on the run above for two reasons that are both about what a
        // number means. The rows above lift the planning allowances so two passes are comparable;
        // these rows are about a brain being cut by its deadline, which is the thing a player met.
        // And a second pass would double the cost of the longest run this instrument has.
        if (args.Contains("--play-measures"))
            return PlayMeasures(args, route, worldNote, suite, capture!);

        Console.WriteLine($"ROUTE {route.Capture} schema={route.Schema} source={route.SourceRevision[..Math.Min(7, route.SourceRevision.Length)]} "
            + $"steps={route.Count} from tick {route[0].Tick} to {route[^1].Tick}");
        Console.WriteLine($"KITS {(route.Kits.Known ? route.Kits.Raw : "not recorded by this schema; checkpoint verdicts will skip")}");
        Console.WriteLine($"WORLD {loaded.Name} {loaded.Width}x{loaded.Height} hash={loaded.Hash} loaded in {loaded.Seconds:0.0}s; {worldNote}");

        // Two passes, each starting from a world that has been forgotten, because route memory, the
        // terrain revision and the edge cache are process-wide and a second pass that inherited
        // them would agree with the first for reasons unconnected to determinism.
        //
        // The tiles are reloaded between them as well, and that is not belt-and-braces. The brain
        // mines and places torches, so a pass can leave the world it ran in a different world from
        // the one it started in — and the second pass would then read a real difference as
        // nondeterminism, in a row whose whole job is to tell those two apart. Reloading is cheap
        // next to the run itself, so the check costs a fraction of a second and closes the class.
        //
        // More than two passes is a diagnostic rather than the row. When two passes disagree, the
        // question that separates the two possible causes is whether the *third* agrees with the
        // second: a one-time first-pass effect — a lazily built table, a sample mutated once, a
        // cache warmed — makes passes two and three identical to each other and different from
        // one, while state that keeps growing makes all three differ. That distinction decides
        // where to look, and asking it any other way costs a day.
        var passes = new List<RunTheWorld.Outcome>();
        double reloadSeconds = 0;
        for (int pass = 0; pass < Math.Max(2, Int(args, "--passes=", 2)); pass++)
        {
            if (pass > 0) reloadSeconds = LoadTheSavedWorld.Load(world).Seconds;
            PrepareTheHeadlessEngine.ForgetEverythingLearnedAboutTheWorld();
            passes.Add(RunTheWorld.Play(route, worldNote, seed: 1));
        }
        RunTheWorld.Outcome first = passes[0], second = passes[1];
        Console.WriteLine($"WORLD reloaded between passes in {reloadSeconds:0.0}s, "
            + $"so an edit a pass made to the tiles cannot read as nondeterminism");
        if (passes.Count > 2)
            Console.WriteLine("PASSES " + string.Join(", ", passes.Select((p, i) => $"{i + 1}:{p.TraceHash}")));

        Console.WriteLine($"RUN {first.Ticks} ticks in {first.Seconds:0.0}s and {second.Seconds:0.0}s "
            + $"({first.Seconds / Math.Max(1, first.Ticks) * 1000:0.0} ms/tick); trace {first.TraceHash} and {second.TraceHash}");

        // The census is the whole run's movement accounting — jumps begun, completed, cancelled,
        // refused — and it is the only place the question "did this build finish that jump" has an
        // answer that is not inferred from a position graph. It is reset before the second pass, so
        // what it holds now describes that pass alone rather than both of them summed.
        // What the light sense made of the world, printed because driving the light engine is a
        // claim this instrument makes and an inert engine looks exactly like a dark world from
        // outside. MeasuredSamples is the discriminator: zero means the sense read nothing the
        // engine presented, whatever the brightness figures say.
        Console.WriteLine($"LIGHT sense readTick={second.LightReadTick?.ToString(CultureInfo.InvariantCulture) ?? "never"} "
            + $"measuredSamples={second.LightMeasuredSamples} atCompanion={second.LightAtCompanion:0.000} atPlayer={second.LightAtPlayer:0.000}");

        Console.WriteLine("CENSUS of the second pass");
        Console.WriteLine(live::AICompanion.Companion.Brain.Infrastructure.Movement.BehaviourCensus.Report());

        if (args.Contains("--print-trace"))
        {
            Console.WriteLine("TRACE tick|action|request|controls|nav status|position");
            foreach (string line in second.Trace) Console.WriteLine("  " + line);
        }

        int failures = ScoreTheRun.Determinism(suite, first, second);
        ScoreTheRun.RecordedTrackDivergence(suite, route, first, worldNote);
        // A fight run is asked to fight, not to follow: the checkpoint row would fail a companion
        // for standing its ground beside a zombie, which is the behaviour the combat rows grade as
        // a pass. The rejoin row is the travel grade on this variant.
        if (RunTheWorld.CombatVariant)
            failures += ScoreTheRun.Combat(suite, first);
        else
            ScoreTheRun.Checkpoints(suite, route, first, CheckpointCadence);

        if (Value(args, "--explore=") is { } budget)
            ExploreWithoutTheTrack.Run(suite, route, int.Parse(budget, CultureInfo.InvariantCulture));

        foreach (LedgerRow row in EmitLedgerRows.Emitted)
            Console.WriteLine($"{row.Verdict.ToUpperInvariant()} {row.Case}"
                + (row.Value is { } value ? $" = {value.ToString("0.###", CultureInfo.InvariantCulture)} {row.Unit}" : "")
                + (row.Message.Length > 0 ? $" :: {row.Message}" : ""));
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// The morning's play, reproduced: the recorded route, the recorded hostiles, the recorded drops,
    /// the game's own clock, and a recorder writing where no reader of real captures will find it.
    /// </summary>
    private static int PlayMeasures(string[] args, ReadRecordedRoute.Route route,
        string worldNote, string suite, string capturePath)
    {
        var cast = ReadRecordedActors.Read(capturePath, route.Steps.Max(s => s.Loot));
        var motion = Value(args, "--hostile-motion=") == "synthetic"
            ? StageRecordedActors.HostileMotion.Synthetic
            : StageRecordedActors.HostileMotion.Native;
        var stage = new StageRecordedActors(cast, motion);
        string preferences = ApplyTheRecordedPreferences.From(cast.Configuration, route.ConfigLine);
        Console.WriteLine("PREFERENCES " + preferences);
        // The cast before the run and the staging after it, because `Describe` reports what was
        // actually placed and before the first tick that is nothing: printing it here said "0 of 18
        // placed" on a healthy run.
        Console.WriteLine($"CAST {cast.Note}");

        RunTheWorld.Actors = stage;
        RunTheWorld.ProductionClock = true;
        PrepareTheHeadlessEngine.ForgetEverythingLearnedAboutTheWorld();

        // Opened from inside the loop, after the companion and the player exist: the recorder's own
        // metadata reads the player's kit, and a recorder opened before that closes itself and
        // leaves a capture with no rows in it.
        RunTheWorld.AfterTheCompanionIsAttached = args.Contains("--no-recorder") ? null : () =>
        {
            AttachTheRecorder.Open(Value(args, "--record-to="), route.Capture);
            Console.WriteLine("RECORDER " + AttachTheRecorder.Describe());
        };
        if (args.Contains("--no-recorder")) Console.WriteLine("RECORDER not attached (--no-recorder)");

        RunTheWorld.Outcome run;
        try
        {
            run = RunTheWorld.Play(route, worldNote, seed: 1);
        }
        finally
        {
            AttachTheRecorder.Close();
            RunTheWorld.AfterTheCompanionIsAttached = null;
        }

        Console.WriteLine($"RUN {run.Ticks} ticks in {run.Seconds:0.0}s "
            + $"({run.Seconds / Math.Max(1, run.Ticks) * 1000:0.0} ms/tick) under the production clock; trace {run.TraceHash}");
        Console.WriteLine("ACTORS " + stage.Describe());

        // The per-tick dump, for the same reason `--print-trace` exists on the run above: a share is
        // a summary, and the question a red raises is always which ticks and what stood in the world
        // while they happened. It is the first thing to reach for when a row disagrees with the
        // capture it was built from, because a thin scene and a fixed brain produce the same number.
        if (args.Contains("--print-play"))
        {
            Console.WriteLine("PLAY tick|reason|activity|step|usable|unresolved|refused|hostiles|drops|decide ms|soonest arrival at player|combat accept|refusals|leaders");
            foreach (RunTheWorld.PlayTick t in run.Play)
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {t.Tick}|{t.Reason}|{t.Activity}|{(t.HasStep ? "step" : "-")}|{t.UsableAdmitted}|{t.UnresolvedAdmitted}|{t.Refused}|{t.HostilesAlive}|{t.DropsPresent}|{t.DecideMs:0.0}|{(float.IsFinite(t.SoonestArrivalTicks) ? t.SoonestArrivalTicks.ToString("0", CultureInfo.InvariantCulture) : "-")}|{t.CombatAccept}{(t.Fired ? "+fired" : "")}|")
                    + string.Join(",", t.Refusals.Select(r => $"{r.Key}={r.Value}")) + "|" + t.Leaders);
        }

        int failures = GradeThePlayMeasures.Grade(suite, route, run, stage, cast, preferences);

        foreach (LedgerRow row in EmitLedgerRows.Emitted)
            Console.WriteLine($"{row.Verdict.ToUpperInvariant()} {row.Case}"
                + (row.Value is { } value ? $" = {value.ToString("0.###", CultureInfo.InvariantCulture)} {row.Unit}" : "")
                + (row.Message.Length > 0 ? $" :: {row.Message}" : ""));
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Whether the world being replayed in is the world the route was recorded in, said plainly.
    ///
    /// The plan wants a hash match and a mismatch to be a skipped row, because replaying a route
    /// against a world since mined through is a false positive nobody would catch: the route is the
    /// same, the terrain is not, and every unreachable answer reads as a regression. Captures older
    /// than the recorder's world line cannot be matched at all, and saying so is the honest
    /// alternative to asserting a match nobody checked.
    /// </summary>
    private static string DescribeWorldMatch(ReadRecordedRoute.Route route, LoadTheSavedWorld.Loaded loaded)
    {
        if (route.WorldLine.Length == 0)
            return $"identity unverified — {route.Capture} predates the recorder's world line, so nothing says this is the world it was recorded in";
        string? recorded = route.WorldLine.Split(';')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && p[0] == "hash")
            .Select(p => p[1]).FirstOrDefault();
        if (recorded == null) return $"identity unverified — the capture's world line carries no hash: {route.WorldLine}";
        return recorded == loaded.Hash
            ? $"identity matches the capture ({recorded})"
            : $"IDENTITY MISMATCH — the capture was recorded in world {recorded} and this is {loaded.Hash}";
    }

    /// <summary>A skipped row and exit 0 when the world is absent, which is the same absence of evidence every
    /// other command here files as a skip; null when the world is there.</summary>
    private static int? MissingWorld(string? world, string suite, string @case)
    {
        if (world != null && File.Exists(world)) return null;
        string reason = $"no world file at {world ?? "<none given>"}; a .wld is never committed, so the world must be named with --world=";
        EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, @case, reason);
        Console.WriteLine($"SKIP {reason}");
        return 0;
    }

    private static void PrintEmittedRows()
    {
        foreach (LedgerRow row in EmitLedgerRows.Emitted)
            Console.WriteLine($"{row.Verdict.ToUpperInvariant()} {row.Case}"
                + (row.Value is { } value ? $" = {value.ToString("0.###", CultureInfo.InvariantCulture)} {row.Unit}" : "")
                + (row.Message.Length > 0 ? $" :: {row.Message}" : ""));
    }

    /// <summary>A comma-separated list of positive numbers, refused with the shape of what arrived rather than
    /// half-parsed, because a curve or a ladder run at a silently dropped point is filed as a complete one.
    /// <para>Zero is a real rung (the empty scene) and never a real allowance, which the seam would refuse only once
    /// the curve had reached that point and lost every row it had not yet filed, so the caller says which it is.</para></summary>
    private static double[] Numbers(string text, string flag, bool zeroAllowed)
    {
        var parsed = new List<double>();
        foreach (string part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value)
                || value < 0 || (value == 0 && !zeroAllowed))
                throw new ArgumentException($"{flag} expects comma-separated {(zeroAllowed ? "non-negative" : "positive")} numbers; \"{part}\" is not one");
            parsed.Add(value);
        }
        if (parsed.Count == 0) throw new ArgumentException($"{flag} expects at least one number and got \"{text}\"");
        return parsed.Distinct().ToArray();
    }

    internal static string? Value(string[] args, string flag)
        => args.FirstOrDefault(a => a.StartsWith(flag, StringComparison.Ordinal)) is { } found ? found[flag.Length..] : null;

    internal static int Int(string[] args, string flag, int fallback)
        => Value(args, flag) is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;

    private static void Usage()
    {
        Console.WriteLine("""
            The world run: the whole brain and the native body, in a real world, behind a player who moves.

              --route=<capture.tsv>   the recording whose player track is replayed
              --scenario=<block.txt>  a committed scenario played as a checkpoint: the orb starts at its
                                      recorded centre, the player stands at his recorded feet, and the rows
                                      is whether the orb reaches him
              --world=<world.wld>     the saved world it is replayed in
              --from-tick=N           seed both bodies at this recorded tick instead of the first
              --ticks=N               how many ticks to play (default 600 for a route, 900 for a scenario; 0 plays the whole capture)
              --suite=<name>          the ledger suite these rows belong to
              --no-light              leave the light engine undriven
              --combat                the combat variant: a frozen zombie waits at his recorded feet
                                      thirty steps ahead, retired after five fired ticks; the rows
                                      are combat winning, no silence while threatened, and rejoining
              --play-measures         the recording's own scene, reproduced: its hostiles and drops
                                      placed at their recorded ticks, its settings applied, the game's
                                      own millisecond allowances kept rather than lifted, one pass,
                                      and a recorder writing a capture marked synthetic
              --print-play            one line per tick of that run: the course's reason, whether a
                                      step was bound, what the census admitted, what was refused and
                                      why, what stood in the world, and what the decision cost
              --record-to=<dir>       where the synthetic capture lands (default: a temporary folder)
              --no-recorder           run the play measures without recording anything
              --soak                  the long run: a seeded bot player walking, stopping and mining a
                                      real world with hostiles and drops arriving on a seeded schedule,
                                      sampled once per decision for the frozen observation's size, the
                                      opportunity store, managed memory and what deciding cost. Needs
                                      only --world; takes --seed and --ticks (7,200 by default, about
                                      two minutes of play)
              --seed=N                which world the soak generates; two runs at one seed agree
              --hostile-motion=native|synthetic
                                      how a placed hostile moves; native is the game's own
                                      NPC.UpdateNPC and synthetic is a straight walk at the player
              --reproduce=<capture>   a schema 0.49.0+ capture played back as its own inputs: every
                                      actor, the player, the clock's answers and the random state put
                                      back per tick, the companion's decisions compared with the
                                      recorded ones; files the share reproduced (a measure)
              --self-consistency      record --route's scene with the mod's recorder, then reproduce
                                      that capture in a fresh process; the verdict is every tick
              --drop-input=<name>     leave one input out of a reproduction: actors, clock, random,
                                      light, player or edits
              --print-reproduction    one line per reproduced tick: same, or what differed
              --expect-every-tick     file the self-consistency verdict rather than the share

            Three cost modes, each filing measures only (no time is a pass line):
              --load-ladder           the seeded bot held at 0, 5, 10, 20 and 40 zombies near him, each
                                      rung twice (ascending, then descending), plus a standing player in
                                      a dense cave at 0 and 10; per rung the whole brain's p50/p95/p99/max,
                                      every phase's p50/p99, allocation per tick, gen-2 collections, and
                                      the exponent of cost growth. Needs --world; takes --seed, --ticks
                                      (per rung, 1,200 by default), --rungs=0,5,… and --no-cave
              --budget-curve          --route's scene replayed at several decision allowances, filing per
                                      allowance the play measures' behaviour beside the decide cost.
                                      Takes --allowances=2,4,8,12 and --repeats=N (2 by default)
              --calibrate=<capture>   that capture replayed headlessly with the recorder attached, filing
                                      per phase the ratio in-game over headless at p50 and p99

            The world is never committed and Telemetry/ is gitignored, so both paths are named rather
            than discovered, and an absent one is a skipped row rather than a failure.
            """);
    }
}
