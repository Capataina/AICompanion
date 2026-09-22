extern alias live;

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using BrainTelemetry = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainTelemetry;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using DiagnosticsConfig = live::AICompanion.Companion.DiagnosticsConfiguration.CompanionDiagnosticsConfig;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;

/// <summary>
/// One seeded full-brain scenario — mining a short vein, then following a player who walks away
/// and back — run three times: a discarded warm-up with recording off, a measured run with recording
/// off, and a measured run with recording on. It reports each brain phase as a distribution and the
/// recorder's own cost as the AI time the brain did not account for. Every tick's activity, request,
/// applied controls, hand grant and resulting body position must match across all three runs:
/// matching warm-up and measured runs show the scenario is deterministic on this machine, and only
/// then does a matching recording run prove that recording changes no decision.
/// </summary>
internal static class MeasureBrainCost
{
    private const int Ticks = 600;

    private sealed record Run(string Name, List<string> Trace, Dictionary<string, List<double>> Timings);

    public static int Execute()
    {
        FieldInfo savePath = typeof(Terraria.Program).GetField("SavePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        object? priorSavePath = savePath.GetValue(null);
        string root = Path.Combine(Path.GetTempPath(), "aic-brain-cost-" + Guid.NewGuid().ToString("N"));
        savePath.SetValue(null, root);
        try
        {
            // Cost is measured under the production millisecond allowances, after a discarded run
            // has paid for JIT compilation.
            // Recording on, so the recorder's own code is compiled before either measured run.
            Scenario("warm-up (recording on)", recording: true);
            var off = Scenario("recording off", recording: false);
            var on = Scenario("recording on", recording: true);
            Print(off);
            Print(on);
            // Invariance is proven with those allowances lifted, because under them the machine's
            // load decides how far a search gets and two identical runs need not agree.
            live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
            try
            {
                var first = Scenario("unbounded, recording off", recording: false);
                var repeat = Scenario("unbounded, recording off, repeated", recording: false);
                var recorded = Scenario("unbounded, recording on", recording: true);
                int deterministic = FirstMismatch(first, repeat);
                if (deterministic >= 0)
                {
                    AICompanion.Tools.Ledger.EmitLedgerRows.Detail($"brain cost: with wall-clock allowances lifted the scenario is still not deterministic, so process state leaks between runs; first mismatch at tick {deterministic}:\n  {first.Trace[deterministic]}\n  {repeat.Trace[deterministic]}");
                    return 1;
                }
                int changed = FirstMismatch(repeat, recorded);
                if (changed >= 0)
                {
                    AICompanion.Tools.Ledger.EmitLedgerRows.Detail($"brain cost: recording changed the companion's behaviour; first mismatch at tick {changed}:\n  off {repeat.Trace[changed]}\n  on  {recorded.Trace[changed]}");
                    return 1;
                }
                int budgeted = FirstMismatch(repeat, off);
                Console.WriteLine(budgeted < 0
                    ? "brain cost: production allowances changed no decision in this scenario on this run"
                    : $"brain cost: production allowances first changed a decision at tick {budgeted} on this run (machine-dependent, reported rather than asserted):\n  unbounded  {repeat.Trace[budgeted]}\n  production {off.Trace[budgeted]}");
            }
            finally { live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false; }
            Console.WriteLine($"brain cost: {Ticks} ticks, identical decisions, requests, controls, grants and positions across repeated runs and with recording off and on");
            return 0;
        }
        finally
        {
            // Close takes the reason it writes into the capture's end marker; this measurement names itself.
            typeof(BrainTelemetry).GetMethod("Close", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, new object[] { "fixture-close" });
            savePath.SetValue(null, priorSavePath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static Run Scenario(string name, bool recording)
    {
        // A flat vein: SetUp places the floor one row below the highest ore tile, so a stacked tile
        // would raise the floor into the vein and bury the exposed face its line probe requires.
        var (_, context) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper,
            new Point(30, 59), new Point(31, 59), new Point(32, 59), new Point(33, 59));
        CompanionNPC companion = context.Companion;
        // Route memory and tick-keyed caches are process-wide; each run starts from the same world
        // clock and an empty archive so the second run cannot inherit what the first one learned.
        var world = new live::AICompanion.Companion.Brain.Infrastructure.Movement.ResetTerrainChanges();
        world.OnWorldLoad();
        world.LoadWorldData(new Terraria.ModLoader.IO.TagCompound());
        VerifyObservedMotion.SetTick(10_000);

        var config = ModContent.GetInstance<DiagnosticsConfig>();
        if (config == null) { config = new DiagnosticsConfig(); ContentInstance.Register(config); }
        config.RecordTelemetry = recording;
        config.OnChanged();
        var recorder = new BrainTelemetry();
        VerifyObservationLifecycle.Attach(recorder);
        if (recording) recorder.OnWorldLoad();

        var run = new Run(name, new List<string>(Ticks), new Dictionary<string, List<double>>());
        void Sample(string phase, double ms)
        {
            if (!run.Timings.TryGetValue(phase, out var values)) run.Timings[phase] = values = new List<double>(Ticks);
            values.Add(ms);
        }
        Player player = Main.player[0];
        var stopwatch = new Stopwatch();
        for (int tick = 0; tick < Ticks; tick++)
        {
            // Stand while the vein is worked, walk away along the floor, then come back.
            player.velocity = new Vector2(tick < 200 ? 0 : tick < 400 ? 3 : -3, 0);
            player.position += player.velocity;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            stopwatch.Restart();
            VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
            double ai = stopwatch.Elapsed.TotalMilliseconds;
            VerifyResponsiveFollowing.AdvanceNative(companion);
            var brain = companion.Brain;
            var grant = brain.ControlGrants.Last!.Value;
            run.Trace.Add(string.Create(CultureInfo.InvariantCulture,
                $"{brain.Chooser.Current?.Name ?? "-"}|{brain.LastRequest.Kind}|{companion.Motor.AppliedControls}|{grant.AppliedOwner}|{grant.Hand}|{companion.NPC.position.X:R},{companion.NPC.position.Y:R}"));
            Sample("senses", brain.SensesMs);
            Sample("reflex+safety", brain.ReflexMs);
            Sample("decide", brain.DecideMs);
            Sample("position", brain.PositionMs);
            Sample("navigate", brain.NavigateMs);
            Sample("finalise", brain.FinaliseMs);
            Sample("brain total", brain.TotalMs);
            Sample("AI outside brain (incl. recording)", Math.Max(0, ai - brain.TotalMs));
            // The recorder's own measurement of the same call, so the outside-brain figure can be split into recording and the rest.
            if (recording && !double.IsNaN(BrainTelemetry.LastRecordMilliseconds)) Sample("recording (BrainTelemetry.Record)", BrainTelemetry.LastRecordMilliseconds);
            Sample("AI total", ai);
        }
        if (recording) recorder.OnWorldUnload();
        config.RecordTelemetry = false;
        config.OnChanged();
        return run;
    }

    private static int FirstMismatch(Run a, Run b)
    {
        for (int i = 0; i < Math.Min(a.Trace.Count, b.Trace.Count); i++)
            if (a.Trace[i] != b.Trace[i]) return i;
        return a.Trace.Count == b.Trace.Count ? -1 : Math.Min(a.Trace.Count, b.Trace.Count);
    }

    private static void Print(Run run)
    {
        Console.WriteLine($"brain cost, {run.Name}, {Ticks} ticks, milliseconds (lower is better):");
        Console.WriteLine("  phase                                   p50      p95      max     mean");
        foreach (var (phase, values) in run.Timings)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            double Percentile(double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Floor(p * (sorted.Length - 1)))];
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {phase,-36} {Percentile(.5),8:0.000} {Percentile(.95),8:0.000} {sorted[^1],8:0.000} {sorted.Average(),8:0.000}"));
        }
        // Where the cost concentrates: the three most expensive AI ticks and what was happening.
        var total = run.Timings["AI total"];
        foreach (int tick in Enumerable.Range(0, total.Count).OrderByDescending(i => total[i]).Take(3))
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  costly tick {tick}: AI {total[tick]:0.000} decide {run.Timings["decide"][tick]:0.000} position {run.Timings["position"][tick]:0.000} navigate {run.Timings["navigate"][tick]:0.000} outside-brain {run.Timings["AI outside brain (incl. recording)"][tick]:0.000}; {run.Trace[tick]}"));
    }
}
