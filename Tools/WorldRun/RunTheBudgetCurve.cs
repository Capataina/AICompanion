extern alias live;

using System.Diagnostics;
using System.Globalization;
using AICompanion.Tools.Ledger;
using TickAllowance = live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation.TickAllowance;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;
using AuditDecisionContracts = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.AuditDecisionContracts;

/// <summary>
/// What a smaller decision allowance costs in behaviour: the play-measures scene replayed at several
/// allowances, filing per allowance the behaviour the play measures already compute beside what the
/// decision cost, so the trade between frame time and behaviour is a table rather than an argument.
///
/// <para><b>Measures only.</b> Nothing here is a verdict, because every row is a behaviour share or a time
/// under a live wall clock, and the owner ruled on 24 September 2026 that no time is a pass line. The
/// play-measures verdicts are deliberately not graded here: at 2 ms they would go red by construction, and a
/// red that is the question's own premise is noise on the scoreboard.</para>
///
/// <para><b>The allowance is moved through one seam.</b> <see cref="TickAllowance.OverrideMilliseconds"/> is
/// what <c>Brain.Tick</c> installs, and every nested slice takes the earlier of its own deadline and the
/// tick's, so the override reaches the whole decision. The production figure is run with the override
/// <em>unset</em> rather than set to itself, so the 12 ms point is the mod's own default path and the
/// header line printing every reader's figure is the proof that the seam is inert there.</para>
///
/// <para><b>Each point is a sample.</b> The clock is live, so how far each search gets depends on the
/// machine, and a replay under it forks from its first differently cut tick — the play measures measured
/// shares moving three to four points between runs of one tree. So each allowance runs <c>--repeats</c>
/// times and a row files the mean with every draw and the spread in its message. The order alternates,
/// ascending then descending, because this suite runs slower per operation the longer a process has been
/// running (root guide, the 2.3x trap), and an ascending-only order would credit that drift to the
/// allowance.</para>
/// </summary>
internal static class RunTheBudgetCurve
{
    /// <summary>The allowances the brief names: the production figure and three below it, each half the one above.</summary>
    public static readonly double[] DefaultAllowances = { 2, 4, 8, Weights.TotalPlanningMilliseconds };

    private const int WarmUpTicks = 300;

    private sealed record Point(double Allowance, int Repeat, double Seconds, int Ticks,
        double NotFightingShare, int WantedTicks, double EmptyCourseShare, double RefusedPerTick,
        int DropsCollected, int DropsPlaced, double DecideP50, double DecideP99, double DecideMax,
        double BrainP50, double BrainP99, double DecideOverAllowanceShare, int Gen2);

    public static int Run(string capturePath, string world, double[] allowances, int repeats, int ticks, string suite)
    {
        if (allowances.Length == 0) throw new ArgumentException("the curve needs at least one allowance", nameof(allowances));
        if (repeats < 1) throw new ArgumentOutOfRangeException(nameof(repeats), $"expected at least one repeat, got {repeats}");
        var route = ReadRecordedRoute.Read(capturePath, 1, ticks);
        Console.WriteLine($"ROUTE {route.Capture} steps={route.Count} from tick {route[0].Tick} to {route[^1].Tick}");
        Console.WriteLine("ALLOWANCE readers at the default: " + DescribeReaders());

        var order = new List<double>();
        for (int r = 0; r < repeats; r++)
            order.AddRange(r % 2 == 0 ? allowances.OrderBy(a => a) : allowances.OrderByDescending(a => a));

        // A discarded warm-up first, because the first run in a process pays the JIT for the whole planning
        // path: on the first smoke run the 2 ms point ran first and its worst decide tick was 227 ms against
        // 74 ms for the 12 ms point after it, which is compilation rather than allowance.
        var wall = Stopwatch.StartNew();
        RunOnce(capturePath, world, route with { Steps = route.Steps.Take(WarmUpTicks).ToList() },
            Weights.TotalPlanningMilliseconds, repeat: 0);
        Console.WriteLine($"CURVE warm-up of {Math.Min(WarmUpTicks, route.Count)} ticks discarded");
        string loadBefore = SummariseCostDistributions.MachineLoad();
        var points = new List<Point>();
        foreach (double allowance in order)
        {
            int repeat = points.Count(p => p.Allowance == allowance) + 1;
            points.Add(RunOnce(capturePath, world, route, allowance, repeat));
            Point last = points[^1];
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"CURVE {allowance:0.##} ms run {repeat}: {last.Ticks} ticks in {last.Seconds:0.0}s, not fighting {last.NotFightingShare:0.0}% of {last.WantedTicks} wanted, "
                + $"empty course {last.EmptyCourseShare:0.0}%, refused {last.RefusedPerTick:0.00}/tick, collected {last.DropsCollected} of {last.DropsPlaced}, "
                + $"decide p50/p99/max {last.DecideP50:0.00}/{last.DecideP99:0.00}/{last.DecideMax:0.00} ms, over the allowance on {last.DecideOverAllowanceShare:0.0}% of ticks, "
                + $"brain p50/p99 {last.BrainP50:0.00}/{last.BrainP99:0.00} ms, gen2 {last.Gen2}"));
        }
        wall.Stop();
        string load = $"load {loadBefore} before and {SummariseCostDistributions.MachineLoad()} after, other work building on this machine";
        Console.WriteLine($"CURVE {points.Count} run(s) in {wall.Elapsed.TotalSeconds:0}s, order {string.Join(",", order.Select(a => a.ToString("0.##", CultureInfo.InvariantCulture)))}; {load}");

        foreach (var group in points.GroupBy(p => p.Allowance).OrderBy(g => g.Key))
            File(suite, group.Key, group.ToList(), route.Capture, load);
        return 0;
    }

    /// <summary>Every place the tick's figure is read, so a run at the default shows them agreeing and a run
    /// under an override shows which ones moved.</summary>
    private static string DescribeReaders()
        => string.Create(CultureInfo.InvariantCulture,
            $"Brain.Tick installs TickAllowance.Milliseconds = {TickAllowance.Milliseconds:0.###} ms (override {(TickAllowance.OverrideMilliseconds is { } o ? o.ToString("0.###", CultureInfo.InvariantCulture) + " ms" : "unset")}); "
            + $"the tunable Weights.TotalPlanningMilliseconds = {Weights.TotalPlanningMilliseconds:0.###} ms; "
            + $"the overrun audit's allowance = {AuditDecisionContracts.DecideCeilingMilliseconds - Weights.RouteSearchMilliseconds:0.###} ms "
            + $"(its ceiling {AuditDecisionContracts.DecideCeilingMilliseconds:0.###} ms less the {Weights.RouteSearchMilliseconds:0.###} ms slice), read through the same seam");

    private static Point RunOnce(string capturePath, string world, ReadRecordedRoute.Route route, double allowance, int repeat)
    {
        // A fresh world and a forgotten brain per run, for the determinism row's reason: the companion mines
        // and places torches, so a run inherits the previous run's terrain unless the tiles are reloaded.
        LoadTheSavedWorld.Load(world);
        var cast = ReadRecordedActors.Read(capturePath, route.Steps.Max(s => s.Loot));
        var stage = new StageRecordedActors(cast, StageRecordedActors.HostileMotion.Native);
        ApplyTheRecordedPreferences.From(cast.Configuration, route.ConfigLine);
        RunTheWorld.Actors = stage;
        RunTheWorld.ProductionClock = true;
        RunTheWorld.AfterTheCompanionIsAttached = null;
        PrepareTheHeadlessEngine.ForgetEverythingLearnedAboutTheWorld();

        bool isDefault = allowance == Weights.TotalPlanningMilliseconds;
        TickAllowance.OverrideMilliseconds = isDefault ? null : allowance;
        if (!isDefault) Console.WriteLine("ALLOWANCE readers under the override: " + DescribeReaders());
        RunTheWorld.Outcome run;
        try
        {
            run = RunTheWorld.Play(route, "budget curve", seed: 1);
        }
        finally
        {
            TickAllowance.OverrideMilliseconds = null;
            RunTheWorld.Actors = null;
            RunTheWorld.ProductionClock = false;
        }

        var play = run.Play;
        int wanted = play.Count(t => t.AFightIsWanted);
        int notFighting = play.Count(t => t.AFightIsWanted && !t.Fighting);
        int empty = play.Count(t => t.Reason == "published-course-holds-no-step");
        long refused = play.Sum(t => (long)t.Refused);
        var decide = play.Select(t => t.DecideMs).ToList();
        var brain = play.Select(t => t.BrainMs).ToList();
        // The tick's deadline starts when Brain.Tick constructs its allowance, before the senses and the reflex run,
        // so the decide phase ended past it when those three laps together exceed the allowance; the decide lap
        // alone misses every overrun shorter than the time the senses and reflex took.
        int overAllowance = play.Count(t => t.SensesMs + t.ReflexMs + t.DecideMs > allowance);
        return new Point(allowance, repeat, run.Seconds, play.Count,
            wanted == 0 ? 0 : 100.0 * notFighting / wanted, wanted,
            play.Count == 0 ? 0 : 100.0 * empty / play.Count,
            play.Count == 0 ? 0 : (double)refused / play.Count,
            stage.DropsTakenByTheRun, stage.PlacedDrops,
            SummariseCostDistributions.Percentile(decide, 0.50), SummariseCostDistributions.Percentile(decide, 0.99),
            decide.Count == 0 ? 0 : decide.Max(),
            SummariseCostDistributions.Percentile(brain, 0.50), SummariseCostDistributions.Percentile(brain, 0.99),
            play.Count == 0 ? 0 : 100.0 * overAllowance / play.Count,
            play.Count == 0 ? 0 : play[^1].Gen2Collections - play[0].Gen2Collections);
    }

    private static void File(string suite, double allowance, List<Point> draws, string capture, string load)
    {
        string at = allowance.ToString("0.##", CultureInfo.InvariantCulture);
        string mode = $"production-clock; allowance {at} ms" + (allowance == Weights.TotalPlanningMilliseconds ? " (default, override unset)" : " (override)");
        string scene = $"{draws.Count} run(s) of {draws[0].Ticks} ticks replaying {capture} with its recorded hostiles and drops under the game's own clock, after a discarded warm-up; the mean is filed and every draw is in this message; {load}";
        string[] behaviour = { EmitLedgerRows.ProductionAllowancesTag, EmitLedgerRows.SampledTag, EmitLedgerRows.PerfTierTag };
        string[] timed = { EmitLedgerRows.ProductionAllowancesTag, EmitLedgerRows.SampledTag, EmitLedgerRows.TimedTag, EmitLedgerRows.PerfTierTag };

        void Row(string name, Func<Point, double> of, string unit, string direction, string[] tags, string format, string what)
        {
            var values = draws.Select(of).ToList();
            EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, $"{name} at a {at} ms allowance",
                SummariseCostDistributions.Mean(values), unit, direction, mode, tags,
                message: $"{what}; draws {SummariseCostDistributions.Draws(values, format)}; {scene}");
        }

        Row("share of wanted-fight ticks the companion was not fighting on", p => p.NotFightingShare, "%", "down", behaviour, "0.0",
            $"README's fight scenes want a fight on {SummariseCostDistributions.Draws(draws.Select(p => (double)p.WantedTicks).ToList(), "0")} tick(s); the same denominator and numerator as the play measures' residual");
        Row("share of ticks whose published course holds no step", p => p.EmptyCourseShare, "%", "down", behaviour, "0.0",
            "the course settled and bound nothing; a decision still in flight is not counted");
        Row("orders refused per tick", p => p.RefusedPerTick, "orders", "down", behaviour, "0.000",
            "every refusal the order search wrote, of any reason");
        Row("drops the companion collected", p => p.DropsCollected, "drops", "up", behaviour, "0",
            $"placed drops that left the world before the recording's own pickup, which in this host only the companion can cause; {draws[0].DropsPlaced} drop(s) were placed per run");
        Row("decide cost p50", p => p.DecideP50, "ms", "down", timed, "0.00", "the course search's phase per tick");
        Row("decide cost p99", p => p.DecideP99, "ms", "down", timed, "0.00",
            $"the same; the worst tick per run was {SummariseCostDistributions.Draws(draws.Select(p => p.DecideMax).ToList(), "0.00")} ms");
        Row("whole-brain cost p50", p => p.BrainP50, "ms", "down", timed, "0.00", "the whole brain tick, against a 16.67 ms frame");
        Row("whole-brain cost p99", p => p.BrainP99, "ms", "down", timed, "0.00", "the same");
        Row("share of ticks whose decide phase ended past the tick's deadline", p => p.DecideOverAllowanceShare, "%", "down", timed, "0.0",
            "the probe that the override reached the decision: the deadline starts at the tick's entry, so a tick counts when its senses, reflex and decide laps together exceed the allowance, which is how often a slice already under way carried the decision past it");
        Row("second-generation collections", p => p.Gen2, "collections", "down", behaviour, "0",
            "process-wide, so the harness's own allocation is inside it");
    }
}
