extern alias live;

#nullable enable

using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using BrainSections = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainSections;
using DetectCostSpikes = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.DetectCostSpikes;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;

/// <summary>
/// The section profiler and the spike fence, proved on the properties a reader of a capture relies on rather than
/// on what the numbers happen to be.
///
/// <para><b>Nothing here asserts a time.</b> Every verdict is logic: entering and leaving allocate nothing, a
/// section's time never exceeds the section it was entered from, a phase's sections never add up to more than the
/// phase's own column, every path a real tick produces is made of registered names under a phase, and the fence
/// fires on an injected outlier and not on a steady, a constant or a slowly rising series. The profiler reads the
/// <c>live::</c> copy throughout, because that is the one the brain writes; this project compiles a second copy for
/// the movement core and nothing in a whole-brain scene touches it.</para>
///
/// <para>The two scenes are the ones <c>--brain-cost</c> and <c>--crowd-cost</c> already run — a short copper vein
/// with a player who stands, walks away and comes back, and the same floor with four stationary hostiles — so a
/// section tree printed here is a tree over work somebody has already measured the phases of.</para>
/// </summary>
internal static class VerifyBrainSectionProfiler
{
    private const string Family = "section profiler";

    /// <summary>The top-level sections a brain tick opens, one per phase column, and the recorder's own. A path that
    /// starts anywhere else was entered outside every phase.</summary>
    internal static readonly string[] Phases = { "senses", "reflex", "decide", "position", "navigate", "finalise", "record" };

    public static int Run()
    {
        int failed = 0;
        failed += RunOneRow.Case("entering and leaving sections and rolling a tick over allocate nothing", EnterExitAndRolloverAllocateNothing, Family);
        failed += RunOneRow.Case("a section re-entered while open is inert and an abandoned scope cannot corrupt the next tick", ReentryIsInertAndAbandonedScopesUnwind, Family);
        failed += RunOneRow.Case("a real tick's sections nest inside their parents and inside their phase columns", RealTicksNestInsideTheirPhases, Family);
        failed += RunOneRow.Case("every path a real tick produces is registered names under a phase", RealTicksProduceRegisteredPathsUnderPhases, Family);
        return failed;
    }

    public static int RunSpikeFence()
    {
        int failed = 0;
        failed += RunOneRow.Case("the spike fence fires on one injected outlier", TheFenceFiresOnAnInjectedOutlier, Family);
        failed += RunOneRow.Case("the spike fence stays silent on a steady, a constant and a slowly rising series", TheFenceIsSilentOnOrdinarySeries, Family);
        failed += RunOneRow.Case("a uniformly slower run reports exactly the spikes a faster one does", TheFenceIsScaleFree, Family);
        failed += RunOneRow.Case("the spike fence allocates nothing per tick", TheFenceAllocatesNothing, Family);
        return failed;
    }

    // ── the profiler alone ────────────────────────────────────────────────────────────────────────

    private static readonly int OuterSection = BrainSections.Register("profiler-test-outer");
    private static readonly int InnerSection = BrainSections.Register("profiler-test-inner");
    private static readonly int LeafSection = BrainSections.Register("profiler-test-leaf");

    private static void EnterExitAndRolloverAllocateNothing()
    {
        BrainSections.Reset();
        BrainSections.BeginTick();
        Span<int> top = stackalloc int[8];
        // Warm-up: the first pass creates the three nodes (writes into preallocated slots) and lets the JIT tier the
        // loop, and a node's path is built only when read, so neither is part of the measured pass.
        Nest(2_000);
        BrainSections.EndTick(1);
        BrainSections.TopBySelf(top, top.Length);

        // Measured five times and judged on the least, because the runtime may allocate on this thread while it
        // tiers up the loop it is timing — measured once at 4,920 bytes on a pass that allocated nothing of its own,
        // under load, and zero on the passes either side. A planted allocation in `Enter` allocates on every pass,
        // so the least is still what the profiler itself allocates.
        const int Passes = 50, Repeats = 5;
        var measured = new long[Repeats];
        for (int repeat = 0; repeat < Repeats; repeat++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int pass = 0; pass < Passes; pass++)
            {
                BrainSections.BeginTick();
                Nest(1_000);
                BrainSections.EndTick((ulong)(2 + pass));
                BrainSections.TopBySelf(top, top.Length);
                BrainSections.TopBySelfAllocation(top, top.Length);
            }
            measured[repeat] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        long allocated = measured.Min();
        int scopes = Passes * 1_000 * 3;
        Console.WriteLine($"  {scopes:n0} scopes entered and left and {Passes} rollovers allocated {allocated} bytes at least (passes: {string.Join(", ", measured)})");
        Require(allocated == 0, $"{scopes:n0} scopes and {Passes} rollovers allocated {allocated} bytes on every one of {Repeats} passes ({string.Join(", ", measured)}); enter, exit, EndTick and TopBySelf must allocate nothing");
        int leaf = FindNode("profiler-test-outer.profiler-test-inner.profiler-test-leaf");
        Require(leaf > 0 && BrainSections.Calls(leaf) == 1_000,
            $"the leaf was entered 1,000 times on the last tick and the snapshot says {(leaf > 0 ? BrainSections.Calls(leaf) : -1)}");
        Require(BrainSections.Overflowed == 0 && BrainSections.Unbalanced == 0,
            $"a balanced nest overflowed {BrainSections.Overflowed} and unbalanced {BrainSections.Unbalanced} scopes");
    }

    private static void Nest(int times)
    {
        for (int i = 0; i < times; i++)
        {
            using var outer = BrainSections.Enter(OuterSection);
            using var inner = BrainSections.Enter(InnerSection);
            using var leaf = BrainSections.Enter(LeafSection);
        }
    }

    private static void ReentryIsInertAndAbandonedScopesUnwind()
    {
        BrainSections.Reset();
        BrainSections.BeginTick();
        using (BrainSections.Enter(OuterSection))
        using (BrainSections.Enter(InnerSection))
        using (BrainSections.Enter(OuterSection)) // re-entry: inert, so no outer.inner.outer path
        using (BrainSections.Enter(LeafSection)) { }
        // A scope a throw walked past, which only a hand-held scope can suffer: the rollover closes it uncounted.
        _ = BrainSections.Enter(OuterSection);
        BrainSections.EndTick(1);
        Require(FindNode("profiler-test-outer.profiler-test-inner.profiler-test-outer") == 0,
            "a section re-entered while open made a node of its own, so its time would be counted inside itself");
        Require(FindNode("profiler-test-outer.profiler-test-inner.profiler-test-leaf") > 0,
            "the leaf entered under an inert re-entry should hang from the open instance, and it is missing");
        Require(BrainSections.Unbalanced == 1, $"one abandoned scope should be counted once at rollover and {BrainSections.Unbalanced} were");
        BrainSections.BeginTick();
        using (BrainSections.Enter(LeafSection)) { }
        BrainSections.EndTick(2);
        Require(FindNode("profiler-test-leaf") > 0 && BrainSections.Calls(FindNode("profiler-test-leaf")) == 1,
            "after an abandoned scope the next tick's section should be top-level, and it nested under the stale one");
    }

    // ── the fence alone ───────────────────────────────────────────────────────────────────────────

    private static void TheFenceFiresOnAnInjectedOutlier()
    {
        var fence = new DetectCostSpikes();
        var random = new Random(7);
        int spikes = 0, at = -1;
        for (int tick = 0; tick < 3 * fence.Window; tick++)
        {
            // A heavy-ish tailed cost around 4 ms with the occasional settling decision, like a real brain.
            double cost = 3.5 + random.NextDouble() + (random.Next(20) == 0 ? 2.0 * random.NextDouble() : 0);
            if (tick == 2 * fence.Window) cost = 40.0;
            if (fence.Observe(cost).Spike) { spikes++; at = tick; }
        }
        Require(spikes == 1 && at == 2 * fence.Window,
            $"one 40 ms tick among {3 * fence.Window} ordinary ones should fire once at tick {2 * fence.Window}; it fired {spikes} time(s), last at {at}");
    }

    private static void TheFenceIsSilentOnOrdinarySeries()
    {
        var random = new Random(11);
        var steady = new DetectCostSpikes();
        int steadySpikes = 0;
        for (int tick = 0; tick < 5 * steady.Window; tick++)
            if (steady.Observe(4.0 + 0.4 * (random.NextDouble() - 0.5)).Spike) steadySpikes++;
        var constant = new DetectCostSpikes();
        int constantSpikes = 0;
        for (int tick = 0; tick < 3 * constant.Window; tick++)
            if (constant.Observe(2.5).Spike) constantSpikes++;
        // Rising from 2 ms to 8 ms over five windows with jitter: a machine warming up, a world filling up.
        var rising = new DetectCostSpikes();
        int risingSpikes = 0, ticks = 5 * rising.Window;
        for (int tick = 0; tick < ticks; tick++)
            if (rising.Observe(2.0 + 6.0 * tick / ticks + 0.2 * random.NextDouble()).Spike) risingSpikes++;
        // Nearly constant with a small periodic step: nine ticks in ten at exactly 2.0 ms and one at 2.2. The quartiles
        // are both 2.0, so the interquartile range is zero and the Tukey fence alone sits on the upper quartile, where
        // every 2.2 ms tick — ten percent above typical, a cost nobody feels — would be a spike. This is the series the
        // median floor exists for, and the one that reddens when it is removed.
        var stepped = new DetectCostSpikes();
        int steppedSpikes = 0;
        for (int tick = 0; tick < 3 * stepped.Window; tick++)
            if (stepped.Observe(tick % 10 == 0 ? 2.2 : 2.0).Spike) steppedSpikes++;
        Require(steadySpikes == 0 && constantSpikes == 0 && risingSpikes == 0 && steppedSpikes == 0,
            $"ordinary series fired: steady {steadySpikes}, constant {constantSpikes}, slowly rising {risingSpikes}, nearly constant with a small step {steppedSpikes}");
    }

    private static void TheFenceIsScaleFree()
    {
        var random = new Random(3);
        var costs = new double[4 * DetectCostSpikes.WindowTicks];
        for (int i = 0; i < costs.Length; i++)
            costs[i] = 1.0 + random.NextDouble() + (random.Next(50) == 0 ? 12.0 * random.NextDouble() : 0);
        var fast = new DetectCostSpikes();
        var slow = new DetectCostSpikes();
        int fastSpikes = 0, disagreements = 0;
        for (int i = 0; i < costs.Length; i++)
        {
            bool a = fast.Observe(costs[i]).Spike;
            // A power of two, so the scaling is exact in binary and the comparison is about the rule, not rounding.
            bool b = slow.Observe(4.0 * costs[i]).Spike;
            if (a) fastSpikes++;
            if (a != b) disagreements++;
        }
        Require(fastSpikes > 0, "the series was built with rare large ticks and the fence found none, so the scale test compares nothing");
        Require(disagreements == 0, $"a run four times slower on every tick disagreed with the faster one on {disagreements} tick(s)");
    }

    private static void TheFenceAllocatesNothing()
    {
        var fence = new DetectCostSpikes();
        var random = new Random(5);
        for (int i = 0; i < 2 * fence.Window; i++) fence.Observe(random.NextDouble());
        // The least of five passes, for the reason the profiler's own allocation row gives.
        var measured = new long[5];
        for (int repeat = 0; repeat < measured.Length; repeat++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2 * fence.Window; i++) fence.Observe(random.NextDouble() + (i % 97 == 0 ? 10 : 0));
            measured[repeat] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Require(measured.Min() == 0, $"{2 * fence.Window:n0} observations allocated bytes on every pass: {string.Join(", ", measured)}");
    }

    // ── real ticks ────────────────────────────────────────────────────────────────────────────────

    private static void RealTicksNestInsideTheirPhases()
    {
        int checkedTicks = 0, nodesChecked = 0;
        foreach (bool crowd in new[] { false, true })
            Scene(crowd, ticks: 240, (brain, tick) =>
            {
                Require(BrainSections.LastTick == brain.LastTick,
                    $"tick {tick}: the snapshot describes tick {BrainSections.LastTick} and the brain ran tick {brain.LastTick}");
                for (int node = 1; node < BrainSections.LastNodeCount; node++)
                {
                    if (BrainSections.Calls(node) == 0) continue;
                    nodesChecked++;
                    int parent = BrainSections.Parent(node);
                    long children = 0, childBytes = 0;
                    for (int child = 1; child < BrainSections.LastNodeCount; child++)
                        if (BrainSections.Parent(child) == node)
                        {
                            children += BrainSections.InclusiveTimestamps(child);
                            childBytes += BrainSections.AllocatedBytes(child);
                        }
                    Require(children <= BrainSections.InclusiveTimestamps(node),
                        $"tick {tick}: the children of {BrainSections.Path(node)} took {children} clock ticks inside its {BrainSections.InclusiveTimestamps(node)}");
                    // Allocation nests the same way, and a section's self allocation is never negative.
                    Require(childBytes <= BrainSections.AllocatedBytes(node) && BrainSections.SelfAllocatedBytes(node) >= 0,
                        $"tick {tick}: the children of {BrainSections.Path(node)} allocated {childBytes} bytes inside its {BrainSections.AllocatedBytes(node)}");
                    if (parent != 0)
                        Require(BrainSections.InclusiveTimestamps(node) <= BrainSections.InclusiveTimestamps(parent),
                            $"tick {tick}: {BrainSections.Path(node)} took longer than {BrainSections.Path(parent)}");
                }
                // The phase columns are laps on the same clock that open before their section and close after it,
                // so the comparison is exact and carries no tolerance.
                RequireWithin(brain, "senses", brain.SensesMs, tick);
                RequireWithin(brain, "reflex", brain.ReflexMs, tick);
                RequireWithin(brain, "decide", brain.DecideMs, tick);
                RequireWithin(brain, "position", brain.PositionMs, tick);
                RequireWithin(brain, "navigate", brain.NavigateMs, tick);
                RequireWithin(brain, "finalise", brain.FinaliseMs, tick);
                checkedTicks++;
            });
        Require(BrainSections.Overflowed == 0 && BrainSections.Unbalanced == 0,
            $"real ticks overflowed {BrainSections.Overflowed} and unbalanced {BrainSections.Unbalanced} scopes");
        Console.WriteLine($"  {checkedTicks} real ticks, {nodesChecked:n0} entered nodes, every one inside its parent and every phase inside its column");
    }

    private static void RequireWithin(live::AICompanion.Companion.Brain.Brain brain, string phase, double column, int tick)
    {
        int node = FindNode(phase);
        if (node == 0 || BrainSections.Calls(node) == 0) return;
        Require(BrainSections.InclusiveMilliseconds(node) <= column,
            $"tick {tick}: the {phase} section took {BrainSections.InclusiveMilliseconds(node):0.000000} ms and its column says {column:0.000000}");
    }

    private static void RealTicksProduceRegisteredPathsUnderPhases()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (bool crowd in new[] { false, true })
            Scene(crowd, ticks: 120, (_, _) =>
            {
                for (int node = 1; node < BrainSections.LastNodeCount; node++)
                    if (BrainSections.Calls(node) > 0) paths.Add(BrainSections.Path(node));
            });
        // Read after the scenes rather than before: a call site registers from its class's static initialiser, which
        // runs on the class's first use, so a registry read before the first fight does not yet hold the combat
        // sections. The first version read it first and went red whenever it was the first case to fight.
        var registered = new HashSet<string>(StringComparer.Ordinal);
        for (int id = 0; id < BrainSections.SectionCount; id++) registered.Add(BrainSections.SectionName(id));
        Require(paths.Count > 0, "two scenes of real ticks produced no section at all");
        foreach (string path in paths)
        {
            string[] names = path.Split('.');
            Require(Phases.Contains(names[0]), $"{path} starts outside every phase, so it was entered where no phase column can hold it");
            foreach (string name in names)
                Require(registered.Contains(name), $"{path} carries {name}, which is not a registered section");
        }
        foreach (string expected in new[] { "senses", "decide.course", "decide.prepare", "navigate" })
            Require(paths.Contains(expected), $"no real tick entered {expected}; the paths were {string.Join(", ", paths.Order())}");
        Console.WriteLine($"  {paths.Count} distinct paths over both scenes, every one registered and under a phase");
    }

    // ── what the profiler costs, and that it changes nothing ─────────────────────────────────────────

    /// <summary>
    /// The profiler's own cost, as measures, and its invariance, as the verdict.
    ///
    /// <para>The verdict is logic: with the millisecond allowances lifted — so a search's reach depends on work counts
    /// and not on how fast the machine was — the crowd scene run with profiling on and with it off makes the same
    /// decision, request, applied controls, hand grant and body position on every tick. That is the proof the
    /// profiler changes no behaviour, and it would fail if a section ever sat on a path that decided anything.</para>
    ///
    /// <para>The cost is two measures and never a pass line. The arms alternate on, off, on, off, on, off over the
    /// same unbounded scene, so the work is identical tick for tick and drift in the machine lands on both arms alike;
    /// the figure is the mean brain tick of the on arms over the off arms. Beside it, a microbenchmark of one
    /// enter-and-leave pair, from the allocation row's own loop shape, divided out per scope.</para>
    /// </summary>
    internal static int MeasureProfilerOverhead()
    {
        const int Ticks = 300;
        (List<double>[] means, long scopesPerTick) = RunArms(Ticks, rounds: 4);
        return FileOverhead(Ticks, means, scopesPerTick);
    }

    /// <summary>
    /// The invariance half alone, short enough for every verify: the crowd scene with profiling on and off, each run
    /// twice so the scene's own determinism is proved before the two arms are compared.
    /// </summary>
    internal static int ProfilingChangesNoDecision()
    {
        RunArms(60, rounds: 2);
        return 0;
    }

    /// <summary>
    /// Alternating profiled and unprofiled arms of the crowd scene with the allowances lifted, each from a fresh case's
    /// state. Round zero pays JIT for both arms and is not timed; every arm must repeat round zero's trace tick for
    /// tick, and the two arms must agree with each other, or this throws.
    /// </summary>
    private static (List<double>[] Means, long ScopesPerTick) RunArms(int Ticks, int rounds)
    {
        var traces = new List<string>[2];
        var means = new List<double>[2] { new(), new() };
        long scopesPerTick = 0;
        for (int round = 0; round < rounds; round++)
            foreach (bool profiling in new[] { true, false })
            {
                var trace = new List<string>(Ticks);
                double total = 0;
                long scopes = 0;
                // Every arm starts from the state a fresh case starts from, because the crowd scene's weapons learn from
                // their own shots and a second arm would otherwise fight with the first arm's knowledge.
                ResetProcessState.BeforeCase(keepProductionAllowances: false);
                Scene(crowd: true, Ticks, (brain, _) =>
                {
                    total += brain.TotalMs;
                    if (profiling)
                        for (int node = 1; node < BrainSections.LastNodeCount; node++) scopes += BrainSections.Calls(node);
                    var companion = sceneCompanion!;
                    var grant = brain.ControlGrants.Last!.Value;
                    trace.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{brain.Activity.Current?.Name ?? "-"}|{brain.LastRequest.Kind}|{companion.Motor.AppliedControls}|{grant.AppliedOwner}|{grant.Hand}|{companion.NPC.position.X:R},{companion.NPC.position.Y:R}"));
                }, profiling);
                // Round zero pays JIT for both arms and is not measured.
                if (round == 0) { traces[profiling ? 0 : 1] = trace; continue; }
                means[profiling ? 0 : 1].Add(total / Ticks);
                if (profiling) scopesPerTick = scopes / Ticks;
                var reference = traces[profiling ? 0 : 1];
                for (int i = 0; i < Ticks; i++)
                    Require(trace[i] == reference[i],
                        $"with allowances lifted the {(profiling ? "profiled" : "unprofiled")} run diverged from its own first run at tick {i}, so the scene is not deterministic and nothing below can be compared:\n  {reference[i]}\n  {trace[i]}");
            }
        int firstDifference = Enumerable.Range(0, Ticks).FirstOrDefault(i => traces[0][i] != traces[1][i], -1);
        Require(firstDifference < 0,
            $"profiling changed the companion's behaviour at tick {firstDifference}:\n  on  {(firstDifference >= 0 ? traces[0][firstDifference] : "")}\n  off {(firstDifference >= 0 ? traces[1][firstDifference] : "")}");
        Console.WriteLine($"  {Ticks} crowd ticks, profiling on and off, each twice: decisions, requests, controls, grants and positions identical on every tick");
        return (means, scopesPerTick);
    }

    private static int FileOverhead(int Ticks, List<double>[] means, long scopesPerTick)
    {
        double on = means[0].Average(), off = means[1].Average();
        EmitTimingMeasures.Timing("the whole brain with the section profiler on, mean over the crowd scene", on,
            $"{Ticks} ticks × {means[0].Count} alternating arms, allowances lifted so both arms do identical work; {scopesPerTick} scopes a tick");
        EmitTimingMeasures.Timing("the whole brain with the section profiler off, mean over the crowd scene", off,
            $"{Ticks} ticks × {means[1].Count} alternating arms, allowances lifted so both arms do identical work");
        EmitTimingMeasures.Measure("the section profiler's share of the whole brain on the crowd scene", 100.0 * (on - off) / off, "%", "down",
            $"(on − off) / off over {means[0].Count} alternating pairs; {scopesPerTick} scopes a tick; a negative value is noise larger than the cost");

        // One enter-and-leave, nested three deep, with a rollover every thousand: the allocation row's loop timed.
        BrainSections.Reset();
        BrainSections.BeginTick();
        Nest(20_000);
        const int Pairs = 200_000;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < Pairs / 3000; i++) { Nest(1_000); BrainSections.EndTick((ulong)i); BrainSections.BeginTick(); }
        double perScopeNs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1e9 / System.Diagnostics.Stopwatch.Frequency / (Pairs / 3000 * 3000);
        EmitTimingMeasures.Measure("one section scope entered and left", perScopeNs, "ns", "down",
            $"{Pairs / 3000 * 3000:n0} scopes nested three deep with a rollover every thousand; the clock, the thread's allocation counter and the node lookup on each side");
        // The arm difference above is the honest end-to-end figure and also the noisiest one: two means of a
        // heavy-tailed cost on a shared machine differ by more than the profiler costs. The scopes a tick actually
        // entered times what one scope costs is the same quantity without the noise, and the two are read together.
        EmitTimingMeasures.Measure("the section profiler's estimated share of the whole brain on the crowd scene",
            100.0 * scopesPerTick * perScopeNs / 1e6 / off, "%", "down",
            $"{scopesPerTick} scopes a tick × {perScopeNs:0} ns a scope against a {off:0.000} ms tick");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  profiler on {on:0.000} ms, off {off:0.000} ms a tick ({100.0 * (on - off) / off:+0.0;-0.0}%), {scopesPerTick} scopes a tick, {perScopeNs:0} ns a scope — decisions identical on every tick"));
        return 0;
    }

    // ── the scenes ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>--brain-cost</c>'s ore scene, or <c>--crowd-cost</c>'s four hostiles on the same floor, ticked through the
    /// real brain; <paramref name="afterTick"/> reads the snapshot after each brain tick.
    /// </summary>
    /// <summary>The companion the running scene ticks, for a callback that needs its body as well as its brain. The fixture's
    /// companion is built without a slot in <c>Main.npc</c>, so <c>CompanionNPC.Instance</c> cannot find it.</summary>
    private static CompanionNPC? sceneCompanion;

    internal static void Scene(bool crowd, int ticks, Action<live::AICompanion.Companion.Brain.Brain, int> afterTick, bool profiling = true)
    {
        BrainSections.Reset();
        BrainSections.Enabled = profiling;
        var (_, context) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper,
            new Point(30, 59), new Point(31, 59), new Point(32, 59), new Point(33, 59));
        CompanionNPC companion = context.Companion;
        sceneCompanion = companion;
        var world = new live::AICompanion.Companion.Brain.Infrastructure.Movement.ResetTerrainChanges();
        world.OnWorldLoad();
        world.LoadWorldData(new Terraria.ModLoader.IO.TagCompound());
        VerifyObservedMotion.SetTick(10_000);
        if (crowd)
        {
            void Hostile(int slot, int type, Vector2 bottom, int life = 0)
            {
                NPC npc = Main.npc[slot];
                npc.SetDefaults(type);
                npc.whoAmI = slot; npc.active = true; npc.velocity = Vector2.Zero;
                npc.Bottom = bottom;
                if (life > 0) npc.life = life;
            }
            Hostile(30, NPCID.Zombie, new Vector2(44 * 16 + 8, 60 * 16));
            Hostile(31, NPCID.Zombie, new Vector2(12 * 16 + 8, 60 * 16), life: 3);
            Hostile(32, NPCID.BlueSlime, new Vector2(62 * 16 + 8, 60 * 16));
            Hostile(33, NPCID.EyeofCthulhu, new Vector2(36 * 16, 50 * 16));
        }
        Player player = Main.player[0];
        for (int tick = 0; tick < ticks; tick++)
        {
            player.velocity = new Vector2(tick < ticks / 3 ? 0 : tick < 2 * ticks / 3 ? 3 : -3, 0);
            player.position += player.velocity;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
            VerifyResponsiveFollowing.AdvanceNative(companion);
            afterTick(companion.Brain, tick);
        }
    }

    /// <summary>
    /// <c>--section-profile</c>: both scenes under the production clock, after a discarded warm-up, with the section
    /// tree aggregated over their ticks — mean and percentiles of each path's inclusive time, its self time's share
    /// of the brain, and how often it ran. This is the instrument the sections' granularity was chosen with, and it
    /// asserts nothing.
    /// </summary>
    internal static int PrintSectionProfile()
    {
        bool lifted = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false;
        try
        {
            foreach (bool crowd in new[] { false, true })
            {
                Scene(crowd, 600, (_, _) => { });
                var inclusive = new Dictionary<string, List<double>>(StringComparer.Ordinal);
                var self = new Dictionary<string, double>(StringComparer.Ordinal);
                var callCount = new Dictionary<string, long>(StringComparer.Ordinal);
                double brainTotal = 0;
                int ticks = 600;
                Scene(crowd, ticks, (brain, _) =>
                {
                    brainTotal += brain.TotalMs;
                    for (int node = 1; node < BrainSections.LastNodeCount; node++)
                    {
                        if (BrainSections.Calls(node) == 0) continue;
                        string path = BrainSections.Path(node);
                        if (!inclusive.TryGetValue(path, out var list)) inclusive[path] = list = new List<double>();
                        list.Add(BrainSections.InclusiveMilliseconds(node));
                        self[path] = self.GetValueOrDefault(path) + BrainSections.SelfMilliseconds(node);
                        callCount[path] = callCount.GetValueOrDefault(path) + BrainSections.Calls(node);
                    }
                });
                Console.WriteLine($"section profile, {(crowd ? "four hostiles" : "ore and following")}, {ticks} ticks under the production clock; brain mean {brainTotal / ticks:0.000} ms a tick");
                Console.WriteLine("  path                                                          ran%   incl mean   p50      p95      max    self%  calls/tick");
                foreach (string path in inclusive.Keys.Order(StringComparer.Ordinal))
                {
                    var values = inclusive[path];
                    var sorted = values.Order().ToArray();
                    double P(double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Floor(p * (sorted.Length - 1)))];
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"  {path,-60} {100.0 * values.Count / ticks,5:0} {values.Sum() / ticks,9:0.000} {P(.5),8:0.000} {P(.95),8:0.000} {sorted[^1],8:0.000} {100.0 * self[path] / brainTotal,6:0.0} {callCount[path] / (double)ticks,8:0.0}"));
                }
            }
        }
        finally { live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = lifted; }
        return 0;
    }

    private static int FindNode(string path)
    {
        for (int node = 1; node < BrainSections.LastNodeCount; node++)
            if (string.Equals(BrainSections.Path(node), path, StringComparison.Ordinal)) return node;
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
