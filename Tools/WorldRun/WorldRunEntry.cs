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
        string? world = Value(args, "--world=");
        int fromTick = Int(args, "--from-tick=", 1);
        int ticks = Int(args, "--ticks=", DefaultTicks);
        string suite = Value(args, "--suite=") ?? "recorded route";

        if (capture == null) { Usage(); return 2; }

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
        var route = ReadRecordedRoute.Read(capture, fromTick, ticks);
        var loaded = LoadTheSavedWorld.Load(world);
        string worldNote = DescribeWorldMatch(route, loaded);

        Console.WriteLine($"ROUTE {route.Capture} schema={route.Schema} source={route.SourceRevision[..Math.Min(7, route.SourceRevision.Length)]} "
            + $"steps={route.Count} from tick {route[0].Tick} to {route[^1].Tick}");
        Console.WriteLine($"KITS {(route.Kits.Known ? route.Kits.Raw : "not recorded by this schema; checkpoint verdicts will skip")}");
        Console.WriteLine($"WORLD {loaded.Name} {loaded.Width}x{loaded.Height} hash={loaded.Hash} loaded in {loaded.Seconds:0.0}s; {worldNote}");

        // Two passes, each starting from a world that has been forgotten, because route memory, the
        // terrain revision and the edge cache are process-wide and a second pass that inherited
        // them would agree with the first for reasons unconnected to determinism.
        PrepareTheHeadlessEngine.ForgetEverythingLearnedAboutTheWorld();
        var first = RunTheWorld.Play(route, worldNote, seed: 1);
        PrepareTheHeadlessEngine.ForgetEverythingLearnedAboutTheWorld();
        var second = RunTheWorld.Play(route, worldNote, seed: 1);

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

    internal static string? Value(string[] args, string flag)
        => args.FirstOrDefault(a => a.StartsWith(flag, StringComparison.Ordinal)) is { } found ? found[flag.Length..] : null;

    private static int Int(string[] args, string flag, int fallback)
        => Value(args, flag) is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;

    private static void Usage()
    {
        Console.WriteLine("""
            The world run: the whole brain and the native body, in a real world, behind a player who moves.

              --route=<capture.tsv>   the recording whose player track is replayed
              --world=<world.wld>     the saved world it is replayed in
              --from-tick=N           seed both bodies at this recorded tick instead of the first
              --ticks=N               how many ticks to play (default 600)
              --suite=<name>          the ledger suite these rows belong to

            The world is never committed and Telemetry/ is gitignored, so both paths are named rather
            than discovered, and an absent one is a skipped row rather than a failure.
            """);
    }
}
