using System.Diagnostics;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Tools.Ledger;

/// <summary>
/// How the brain's cost grows with load: one seeded scene held at 0, 5, 10, 20 and 40 hostiles near the
/// player, each rung long enough for a stable p50 and p99, filing per rung the whole brain's percentiles,
/// every phase column's p50 and p99, the brain's own allocation per tick and the gen-2 collections, and
/// across rungs the cost per added hostile with the exponent of its growth.
///
/// <para><b>One scene, varied in one thing.</b> Every rung replays the same bot track from the same seed
/// — <c>DriveASeededScene</c>, materialised for the rung's length — in a freshly reloaded world with a
/// freshly attached companion, so the hostile count is the only input that differs between two rungs. The
/// hostiles are vanilla zombies placed beside the bot on the first tick, held alive by the stage for the
/// whole rung (nothing here deals damage, so nothing kills them), and moved by the game's own AI. A zombie
/// walks a pixel a tick and the bot three, so the stage's leash puts any zombie further than
/// <see cref="LeashPixels"/> back beside him, and the rows carry how many re-placements that took and the
/// load the threat sense actually held, because a rung is named for what it staged and measured by what
/// it delivered.</para>
///
/// <para><b>The rungs double</b> above five, so the log-log fit of marginal cost on hostile count is spaced
/// evenly and the forty rung — a large event crowd, the case the owner's "a lot of mods" worry is about —
/// is reached in five rungs rather than nine. <b>Each rung runs twice</b>, ascending then descending,
/// because this process runs slower per operation the longer it has been running (the root guide's 2.3x
/// trap), and an ascending-only ladder would credit that drift to the load. A discarded warm-up rung runs
/// first for the JIT.</para>
///
/// <para><b>The bot does not mine here</b>, unlike the soak: materialising its track drops the tile breaks,
/// and terrain churn is not the question a ladder asks. A standing phase stays standing.</para>
///
/// <para><b>The cave rungs</b> put a standing player in the densest open pocket underground near the
/// world's spawn, at zero and ten hostiles, because the surface bot never leaves the surface and the play
/// that motivated the profiler was a descent; the reach flood and the route search pay for terrain the
/// surface does not have. A pocket is found by a scan, not recorded, and the rows say where it was.</para>
/// </summary>
internal static class RunTheLoadLadder
{
    public static readonly int[] DefaultRungs = { 0, 5, 10, 20, 40 };

    /// <summary>Ticks per rung run. Twelve hundred is twenty seconds of play; nearest-rank p99 over it sits
    /// twelve ticks from the top, so one freak tick moves the p99 by one rank rather than defining it.</summary>
    public const int DefaultDwellTicks = 1200;

    private const int WarmUpTicks = 300;

    /// <summary>How far a hostile may stray before the leash re-places it: ten seconds at a zombie's pace,
    /// which is the horizon inside which README's fight scenes want a fight, so every staged hostile stays a
    /// wanted one.</summary>
    private const float LeashPixels = 600f;

    /// <summary>The hostile count of the loaded cave rung: the ladder's middle rung, so it pairs with a
    /// surface rung taken the same way.</summary>
    private const int CaveHostiles = 10;

    private const int FirstHostileSlot = 5;

    private sealed record RungRun(string Rung, int Hostiles, int Pass, IReadOnlyList<RunTheWorld.PlayTick> Play,
        double Seconds, int LeashReplacements, int Gen2);

    public static int Run(string world, int seed, int dwell, int[] rungs, string suite, bool driveLight, bool cave)
    {
        if (dwell < 1) throw new ArgumentOutOfRangeException(nameof(dwell), $"expected a positive number of ticks per rung, got {dwell}");
        if (rungs.Length == 0 || rungs.Any(r => r < 0 || FirstHostileSlot + r >= Main.maxNPCs))
            throw new ArgumentException($"rungs must be between 0 and {Main.maxNPCs - FirstHostileSlot - 1} hostiles; got {string.Join(",", rungs)}");
        rungs = rungs.Distinct().OrderBy(r => r).ToArray();
        var wall = Stopwatch.StartNew();
        string loadBefore = SummariseCostDistributions.MachineLoad();

        LoadTheSavedWorld.Load(world);
        ReadRecordedRoute.Route surface = MaterialiseTheBot(seed, dwell);
        Point? pocket = cave ? FindADenseCave() : null;
        Console.WriteLine($"LADDER rungs {string.Join(",", rungs)} x2 passes, {dwell} ticks each, seed {seed}, leash {LeashPixels:0} px, load {loadBefore}; "
            + (cave ? pocket is { } p ? $"cave pocket at tile {p.X},{p.Y}" : "no dense cave found near spawn, cave rungs skipped" : "cave rungs off"));

        RunOne(world, surface, "warm-up", 0, 0, seed, driveLight, leash: true, ticks: Math.Min(WarmUpTicks, dwell));
        var runs = new List<RungRun>();
        foreach (int hostiles in rungs) runs.Add(RunOne(world, surface, "surface", hostiles, 1, seed, driveLight, leash: true));
        foreach (int hostiles in rungs.Reverse()) runs.Add(RunOne(world, surface, "surface", hostiles, 2, seed, driveLight, leash: true));
        if (pocket is { } at)
        {
            ReadRecordedRoute.Route standing = StandInTheCave(at, dwell);
            foreach (int pass in new[] { 1, 2 })
                foreach (int hostiles in pass == 1 ? new[] { 0, CaveHostiles } : new[] { CaveHostiles, 0 })
                    runs.Add(RunOne(world, standing, "cave", hostiles, pass, seed, driveLight, leash: false));
        }
        wall.Stop();
        string loadAfter = SummariseCostDistributions.MachineLoad();
        double overheadNs = MeasureTheInstrumentsOwnCost();
        Console.WriteLine($"LADDER {runs.Count} rung run(s) in {wall.Elapsed.TotalSeconds:0}s; load {loadBefore} before, {loadAfter} after; "
            + $"the per-tick sampling this mode adds costs {overheadNs:0} ns a tick");

        File(suite, runs, seed, dwell, $"load {loadBefore} before and {loadAfter} after, other work building on this machine", overheadNs, pocket);
        return 0;
    }

    private static RungRun RunOne(string world, ReadRecordedRoute.Route route, string rung, int hostiles, int pass,
        int seed, bool driveLight, bool leash, int? ticks = null)
    {
        if (ticks is { } limit && limit < route.Count) route = route with { Steps = route.Steps.Take(limit).ToList() };
        LoadTheSavedWorld.Load(world);
        PrepareTheHeadlessEngine.ForgetEverythingLearnedAboutTheWorld();
        var stage = new StageRecordedActors(Crowd(route, hostiles, seed, rung == "cave"), StageRecordedActors.HostileMotion.Native)
        {
            LeashPixels = leash ? LeashPixels : null,
        };
        RunTheWorld.Actors = stage;
        RunTheWorld.ProductionClock = true;
        RunTheWorld.TakeThePlanPhase = true;
        RunTheWorld.DriveLight = driveLight;
        RunTheWorld.AfterTheCompanionIsAttached = null;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        RunTheWorld.Outcome run;
        try
        {
            run = RunTheWorld.Play(route, rung, seed);
        }
        finally
        {
            RunTheWorld.Actors = null;
            RunTheWorld.ProductionClock = false;
            RunTheWorld.TakeThePlanPhase = false;
        }
        var play = run.Play;
        var result = new RungRun(rung, hostiles, pass, play, run.Seconds, stage.LeashReplacements,
            play.Count == 0 ? 0 : play[^1].Gen2Collections - play[0].Gen2Collections);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"RUNG {rung} {hostiles} pass {pass}: {play.Count} ticks in {run.Seconds:0.0}s, brain p50/p99/max {Pct(play, t => t.BrainMs, 0.5):0.00}/{Pct(play, t => t.BrainMs, 0.99):0.00}/{(play.Count == 0 ? 0 : play.Max(t => t.BrainMs)):0.00} ms, "
            + $"threats sensed mean {(play.Count == 0 ? 0 : play.Average(t => t.ThreatsSensed)):0.0}, alloc mean {(play.Count == 0 ? 0 : play.Average(t => (double)t.BrainAllocatedBytes)) / 1024:0.0} KB/tick, "
            + $"gen2 {result.Gen2}, leash re-placements {stage.LeashReplacements}"));
        return result;
    }

    /// <summary>The bot's track for one rung, drawn once and replayed by every rung. Its tile breaks are
    /// dropped: terrain churn is the soak's question and not the ladder's.</summary>
    private static ReadRecordedRoute.Route MaterialiseTheBot(int seed, int ticks)
    {
        var scene = new DriveASeededScene(seed, 1);
        var steps = new List<ReadRecordedRoute.Step>(ticks);
        for (int i = 0; i < ticks; i++) steps.Add(scene.Advance(1 + i, out _));
        return new ReadRecordedRoute.Route($"generated-ladder-seed-{seed.ToString(CultureInfo.InvariantCulture)}", "generated", "n/a",
            new ReadRecordedRoute.Kits(false, "", false, false, false, false), "", "", steps);
    }

    private static ReadRecordedRoute.Route StandInTheCave(Point pocket, int ticks)
    {
        var feet = new Vector2(pocket.X * 16f + 8f, (pocket.Y + 1) * 16f);
        var steps = Enumerable.Range(1, ticks).Select(tick => new ReadRecordedRoute.Step(tick, feet, Vector2.Zero, PlayerGrounded: true,
            CompanionCentre: feet - new Vector2(0f, 40f), PlayerLife: 400, Loot: -1, Threats: -1)).ToList();
        return new ReadRecordedRoute.Route("generated-ladder-cave", "generated", "n/a",
            new ReadRecordedRoute.Kits(false, "", false, false, false, false), "", "", steps);
    }

    /// <summary>
    /// N zombies placed on the first tick, six to twenty tiles either side of the player and held for the
    /// whole rung. Underground they are placed only where a zombie's body fits in air, because one placed
    /// inside rock is a threat the sense counts and nothing can ever reach, which is a lighter load than the
    /// rung names; a hostile no free spot was found for is left out and the realised count says so.
    /// </summary>
    private static ReadRecordedActors.Cast Crowd(ReadRecordedRoute.Route route, int hostiles, int seed, bool underground)
    {
        var place = new Random(seed ^ 0x1ADDE5);
        Vector2 feet = route[0].PlayerFeet;
        var cast = new List<ReadRecordedActors.Hostile>(hostiles);
        for (int i = 0; i < hostiles; i++)
        {
            Vector2? centre = underground ? FreeSpotNear(feet, place) : feet + new Vector2((place.Next(2) == 0 ? -1 : 1) * (6 + place.Next(15)) * 16f, -24f);
            if (centre is not { } at) continue;
            cast.Add(new ReadRecordedActors.Hostile(1, FirstHostileSlot + i, 1, NPCID.Zombie, "Zombie", at, 45, int.MaxValue));
        }
        return new ReadRecordedActors.Cast("(generated)", cast, Array.Empty<ReadRecordedActors.Drop>(),
            MostDropsCountedAtOnce: 0, Shortfall: 0, LastCompanionKillTick: -1, StoppedReadingAt: null, Configuration: "",
            Note: $"a load-ladder crowd of {cast.Count} zombie(s) generated from seed {seed}, held for the whole rung");
    }

    private static Vector2? FreeSpotNear(Vector2 feet, Random place)
    {
        for (int attempt = 0; attempt < 400; attempt++)
        {
            int tx = (int)(feet.X / 16f) + (place.Next(2) == 0 ? -1 : 1) * (4 + place.Next(17));
            int ty = (int)(feet.Y / 16f) - 12 + place.Next(24);
            if (AirBox(tx, ty - 2, 2, 3) && Solid(tx, ty + 1)) return new Vector2(tx * 16f + 16f, (ty + 1) * 16f - 22f);
        }
        return null;
    }

    /// <summary>
    /// The densest open pocket underground near the spawn: a standing spot (a body's width of air three
    /// tall, room for the companion above it, rock underfoot) whose surrounding screen-sized box is between
    /// 35 and 75 percent solid, preferring the one closest to half. Solid is the tile's own full-block flag
    /// rather than the movement core's shape test, because this only chooses where to stand; the brain
    /// still reads the tiles through its own rules.
    /// </summary>
    private static Point? FindADenseCave()
    {
        int top = (int)Main.rockLayer + 10, bottom = Math.Min(Main.maxTilesY - 60, (int)Main.rockLayer + 260);
        int from = Math.Max(50, Main.spawnTileX - 400), to = Math.Min(Main.maxTilesX - 50, Main.spawnTileX + 400);
        Point? best = null;
        double bestGap = double.MaxValue;
        for (int x = from; x < to; x += 6)
            for (int y = top; y < bottom; y += 3)
            {
                if (!AirBox(x, y - 2, 2, 3) || !AirBox(x - 1, y - 6, 4, 4) || !Solid(x, y + 1) || !Solid(x + 1, y + 1)) continue;
                double density = SolidShare(x - 30, y - 17, 60, 34);
                if (density < 0.35 || density > 0.75) continue;
                double gap = Math.Abs(density - 0.55);
                if (gap < bestGap) { bestGap = gap; best = new Point(x, y); }
            }
        return best;
    }

    private static bool Solid(int x, int y)
        => x > 0 && y > 0 && x < Main.maxTilesX && y < Main.maxTilesY
            && Main.tile[x, y].HasTile && Main.tileSolid[Main.tile[x, y].TileType] && !Main.tileSolidTop[Main.tile[x, y].TileType];

    private static bool AirBox(int x, int y, int width, int height)
    {
        for (int i = x; i < x + width; i++)
            for (int j = y; j < y + height; j++)
                if (i <= 0 || j <= 0 || i >= Main.maxTilesX || j >= Main.maxTilesY || Solid(i, j) || Main.tile[i, j].LiquidAmount > 0) return false;
        return true;
    }

    private static double SolidShare(int x, int y, int width, int height)
    {
        int solid = 0;
        for (int i = x; i < x + width; i++)
            for (int j = y; j < y + height; j++)
                if (Solid(i, j)) solid++;
        return (double)solid / (width * height);
    }

    /// <summary>What the sampling this mode adds to every tick costs: two reads of the thread's allocation
    /// counter and a destructive read of the route-search clock, timed over a million repetitions. The field
    /// reads beside them are a few nanoseconds and are not worth a separate figure.</summary>
    private static double MeasureTheInstrumentsOwnCost()
    {
        const int repetitions = 1_000_000;
        long sink = 0;
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < repetitions; i++)
            sink += GC.GetAllocatedBytesForCurrentThread() - GC.GetAllocatedBytesForCurrentThread();
        clock.Stop();
        GC.KeepAlive(sink);
        return clock.Elapsed.TotalMilliseconds * 1_000_000d / repetitions;
    }

    private static double Pct(IReadOnlyList<RunTheWorld.PlayTick> play, Func<RunTheWorld.PlayTick, double> of, double fraction)
        => SummariseCostDistributions.Percentile(play.Select(of), fraction);

    private static readonly (string Name, Func<RunTheWorld.PlayTick, double> Of)[] Phases =
    {
        ("senses", t => t.SensesMs), ("reflex", t => t.ReflexMs), ("decide", t => t.DecideMs), ("position", t => t.PositionMs),
        ("navigate", t => t.NavigateMs), ("route search", t => t.PlanMs), ("reach flood", t => t.FloodMs), ("finalise", t => t.FinaliseMs),
    };

    private static void File(string suite, List<RungRun> runs, int seed, int dwell, string load, double overheadNs, Point? pocket)
    {
        string[] timed = { EmitLedgerRows.ProductionAllowancesTag, EmitLedgerRows.SampledTag, EmitLedgerRows.TimedTag, EmitLedgerRows.PerfTierTag };
        string[] sampled = { EmitLedgerRows.ProductionAllowancesTag, EmitLedgerRows.SampledTag, EmitLedgerRows.PerfTierTag };
        const string mode = "production-clock";

        var rungs = runs.GroupBy(r => (r.Rung, r.Hostiles)).OrderBy(g => g.Key.Rung == "cave" ? 1 : 0).ThenBy(g => g.Key.Hostiles).ToList();
        var surfaceMeans = new List<(int Hostiles, double Threats, double Mean, double P99)>();
        foreach (var group in rungs)
        {
            var draws = group.OrderBy(r => r.Pass).ToList();
            string at = group.Key.Rung == "cave"
                ? $"cave rung of {group.Key.Hostiles} hostiles"
                : $"{group.Key.Hostiles}-hostile rung";
            double threats = draws.Average(r => r.Play.Count == 0 ? 0 : r.Play.Average(t => t.ThreatsSensed));
            // Concatenated pieces each formatted invariantly: two interpolated strings joined with `+` are a
            // plain string by the time `string.Create` sees them, which binds its span overload and fails.
            string scene = FormattableString.Invariant($"{draws.Count} pass(es) of {dwell} ticks, seed {seed}; the threat sense held {threats:0.0} hostile(s) on average, ")
                + $"{string.Join(" and ", draws.Select(r => r.LeashReplacements))} leash re-placement(s); "
                + (group.Key.Rung == "cave" && pocket is { } p ? FormattableString.Invariant($"a standing player in the pocket at tile {p.X},{p.Y}; ") : "the seeded surface bot, not mining; ")
                + FormattableString.Invariant($"{load}; this mode's own sampling costs {overheadNs:0} ns a tick");

            void Row(string name, Func<RungRun, double> of, string unit, string[] tags, string format, string what)
            {
                var values = draws.Select(of).ToList();
                EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, $"{name} on the {at}",
                    SummariseCostDistributions.Mean(values), unit, "down", mode, tags,
                    message: $"{what}; passes {SummariseCostDistributions.Draws(values, format)}; {scene}");
            }

            Row("whole-brain cost p50", r => Pct(r.Play, t => t.BrainMs, 0.50), "ms", timed, "0.00", "the whole brain tick, against a 16.67 ms frame");
            Row("whole-brain cost p95", r => Pct(r.Play, t => t.BrainMs, 0.95), "ms", timed, "0.00", "the same");
            Row("whole-brain cost p99", r => Pct(r.Play, t => t.BrainMs, 0.99), "ms", timed, "0.00", "the same");
            Row("whole-brain cost max", r => r.Play.Count == 0 ? 0 : r.Play.Max(t => t.BrainMs), "ms", timed, "0.00", "the single worst tick of the pass");
            Row("whole-brain cost mean", r => r.Play.Count == 0 ? 0 : r.Play.Average(t => t.BrainMs), "ms", timed, "0.000", "the mean, which is what the growth fit reads because costs add");
            foreach ((string phase, var of) in Phases)
            {
                Row($"{phase} phase p50", r => Pct(r.Play, of, 0.50), "ms", timed, "0.00", $"the brain's {phase} clock per tick");
                Row($"{phase} phase p99", r => Pct(r.Play, of, 0.99), "ms", timed, "0.00", $"the same"
                    + (phase == "reach flood" ? "; the flood column repeats its last slice's cost on ticks that ran none, as the recorder's flood_ms does" : ""));
            }
            Row("brain allocation per tick", r => r.Play.Count == 0 ? 0 : r.Play.Average(t => (double)t.BrainAllocatedBytes), "bytes", sampled, "0",
                "bytes this thread allocated inside the companion's update and the body's move, mean per tick; the harness's own allocation is outside the bracket");
            Row("brain allocation p99", r => Pct(r.Play, t => t.BrainAllocatedBytes, 0.99), "bytes", sampled, "0", "the same at p99");
            Row("second-generation collections", r => r.Gen2, "collections", sampled, "0",
                "process-wide over the pass, so the harness's allocation and the staged hostiles' native updates are inside it");
            if (group.Key.Rung == "surface")
                surfaceMeans.Add((group.Key.Hostiles, threats,
                    draws.Average(r => r.Play.Count == 0 ? 0 : r.Play.Average(t => t.BrainMs)),
                    draws.Average(r => Pct(r.Play, t => t.BrainMs, 0.99))));
        }
        FileTheGrowth(suite, surfaceMeans, timed, mode, load);
    }

    /// <summary>
    /// Cost per added hostile between consecutive rungs, and two exponents of growth, each fitted by least
    /// squares on log-log axes against the realised hostile count the threat sense held rather than the count
    /// staged, on the mean because costs add, and at p99 beside it because a spike is what a player feels.
    ///
    /// <para>The <b>marginal</b> exponent is the slope of log(cost(N) − cost(0)) on log(N): one is linear, two is
    /// what a pairwise interaction between hostiles would cost. It is only fitted when every loaded rung costs
    /// more than the empty one and there are three of them, and otherwise skips with the table, because the
    /// first full ladder (24 September 2026) had the empty rung costing <em>more</em> than five and ten — the
    /// decision fills its allowance whatever the load, so cost is not a sum of per-hostile terms at small
    /// loads — and a fit through the two rungs left over printed r² 1.00, which is what any line through two
    /// points prints.</para>
    ///
    /// <para>The <b>elasticity</b> is the slope of log(cost(N)) on log(N) over the loaded rungs, which exists
    /// whenever three are loaded: under one means a fixed cost dominates and each hostile adds less than its
    /// share, one means cost grows in proportion, above one means worse than linear.</para>
    /// </summary>
    private static void FileTheGrowth(string suite, List<(int Hostiles, double Threats, double Mean, double P99)> rungs,
        string[] tags, string mode, string load)
    {
        rungs = rungs.OrderBy(r => r.Hostiles).ToList();
        for (int i = 1; i < rungs.Count; i++)
        {
            var (low, high) = (rungs[i - 1], rungs[i]);
            double added = high.Threats - low.Threats;
            EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, $"whole-brain cost per added hostile from {low.Hostiles} to {high.Hostiles}",
                added > 0 ? (high.Mean - low.Mean) / added : 0, "ms/hostile", "down", mode, tags,
                message: string.Create(CultureInfo.InvariantCulture,
                    $"mean whole-brain cost {low.Mean:0.000} ms at {low.Threats:0.0} sensed hostile(s) and {high.Mean:0.000} ms at {high.Threats:0.0}; {load}"));
        }
        const int FitPoints = 3;
        var loaded = rungs.Where(r => r.Hostiles > 0 && r.Threats > 0).ToList();
        (int Hostiles, double Threats, double Mean, double P99)? zero = rungs.Any(r => r.Hostiles == 0) ? rungs.First(r => r.Hostiles == 0) : null;
        foreach ((string label, Func<(int Hostiles, double Threats, double Mean, double P99), double> of) in
            new (string, Func<(int Hostiles, double Threats, double Mean, double P99), double>)[] { ("", r => r.Mean), (" at p99", r => r.P99) })
        {
            string marginal = $"exponent of the whole brain's marginal cost in hostiles{label}";
            if (zero is not { } empty || loaded.Count < FitPoints)
                EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, marginal,
                    $"the fit needs the empty rung and {FitPoints} loaded rungs; this ladder had {(zero is null ? "no empty rung" : "the empty rung")} and {loaded.Count} loaded");
            else
            {
                var points = loaded.Select(r => (X: r.Threats - empty.Threats, Y: of(r) - of(empty))).ToList();
                string table = string.Join(", ", points.Select(p => FormattableString.Invariant($"+{p.X:0.0} hostiles: {p.Y:+0.000;-0.000} ms")));
                if (points.Any(p => p.Y <= 0) || SummariseCostDistributions.LogLogSlope(points) is not { } fit)
                    EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, marginal,
                        $"not every loaded rung cost more than the empty one ({table}), so the cost is not a sum of per-hostile terms at these loads and a "
                        + "marginal exponent through the rungs left over would be a line through whichever points happened to be positive; the elasticity row beside this is the fit that exists");
                else
                    EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, marginal, fit.Exponent, "exponent", "down", mode, tags,
                        message: FormattableString.Invariant($"least-squares slope of log(marginal cost) on log(added hostiles) over {fit.Points} rungs, r² {fit.RSquared:0.00}: {table}; 1 is linear, 2 quadratic; ") + load);
            }

            string elasticity = $"elasticity of the whole brain's cost in hostiles{label}";
            var totals = loaded.Select(r => (X: r.Threats, Y: of(r))).ToList();
            string totalsTable = string.Join(", ", totals.Select(p => FormattableString.Invariant($"{p.X:0.0} hostiles: {p.Y:0.000} ms")));
            if (totals.Count < FitPoints || SummariseCostDistributions.LogLogSlope(totals) is not { } whole)
                EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, elasticity,
                    $"the fit needs {FitPoints} loaded rungs with a positive cost; this ladder had {totals.Count} ({totalsTable})");
            else
                EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, elasticity, whole.Exponent, "exponent", "down", mode, tags,
                    message: FormattableString.Invariant($"least-squares slope of log(cost) on log(hostiles) over the {whole.Points} loaded rungs, r² {whole.RSquared:0.00}: {totalsTable}; under 1 a fixed cost dominates, 1 is proportional, above 1 worse than linear; ") + load);
        }
    }
}
