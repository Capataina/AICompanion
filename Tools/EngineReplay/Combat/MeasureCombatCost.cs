extern alias live;

using System.Diagnostics;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;

/// <summary>
/// The seeded brain-cost scene has no hostiles, so it cannot price anything threat consequence, target
/// choice, protection or safety spend. This scene runs the same full brain over the same ore floor with a
/// fixed crowd: a full-health zombie in weapon reach, a nearly dead zombie behind the companion, a slime
/// and a stationary boss-flagged Eye above the floor, while the player stands, walks away and comes back.
/// No enemy AI runs and spawned projectiles are never advanced, so it prices the brain's queries against
/// one arrangement rather than a fight. It reports distributions and names its costliest ticks; it asserts
/// nothing, because a threshold chosen on one machine would be negotiated by the next.
/// </summary>
internal static class MeasureCombatCost
{
    private const int Ticks = 600;

    public static int Execute()
    {
        // The first run pays JIT compilation for every combat path and is discarded.
        Scenario(print: false, productionClock: true);
        Scenario(print: true, productionClock: true);
        Scenario(print: true, productionClock: false);
        return 0;
    }

    /// <summary>
    /// <paramref name="productionClock"/> decides which of two different questions this scene answers,
    /// and conflating them is how the number in AIC-445 came to be quoted against a frame budget it was
    /// never measured against. `Program.cs` lifts the millisecond allowances for the whole process — the
    /// harness-wide rule that stops a wall clock deciding any verdict — so every figure this scene printed
    /// before 21 September 2026 was the brain running with **no deadline at all**. That is a real and
    /// useful quantity: it is the work the brain would like to do, and how far above a frame it sits is
    /// how much production is cutting. It is not what a frame costs.
    ///
    /// With the clock in, `LimitPlanningWork` bounds the tick the way it does in a game, so the printed
    /// distribution is the one a player would feel. Both are printed, labelled, because a reader who sees
    /// only the first cannot tell a brain that is slow from a brain that is being cut hard, and those want
    /// opposite fixes: the first is an optimisation, the second is a search returning less than it should.
    /// The regime is restored to what was found rather than to a literal, because a literal here is the
    /// harness default written down twice.
    /// </summary>
    private static void Scenario(bool print, bool productionClock)
    {
        var (_, context) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper,
            new Point(30, 59), new Point(31, 59), new Point(32, 59), new Point(33, 59));
        CompanionNPC companion = context.Companion;
        var world = new live::AICompanion.Companion.Brain.Infrastructure.Movement.ResetTerrainChanges();
        world.OnWorldLoad();
        world.LoadWorldData(new Terraria.ModLoader.IO.TagCompound());
        VerifyObservedMotion.SetTick(10_000);
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

        var timings = new Dictionary<string, List<double>>();
        var trace = new List<string>(Ticks);
        void Sample(string phase, double ms)
        {
            if (!timings.TryGetValue(phase, out var values)) timings[phase] = values = new List<double>(Ticks);
            values.Add(ms);
        }
        Player player = Main.player[0];
        var stopwatch = new Stopwatch();
        // Both copies, because EngineReplay compiles the movement core a second time beside the `live`
        // alias and setting one leaves the brain reading the other.
        bool liftedHere = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded;
        bool liftedThere = AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = !productionClock;
        AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = !productionClock;
        try
        {
        for (int tick = 0; tick < Ticks; tick++)
        {
            player.velocity = new Vector2(tick < 200 ? 0 : tick < 400 ? 3 : -3, 0);
            player.position += player.velocity;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            stopwatch.Restart();
            VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
            double ai = stopwatch.Elapsed.TotalMilliseconds;
            VerifyResponsiveFollowing.AdvanceNative(companion);
            var brain = companion.Brain;
            trace.Add($"{brain.Chooser.Current?.Name ?? "-"}|{brain.LastRequest.Kind}|evade={brain.Movement.LastEvade.Reason}|aim={brain.EngageTarget?.whoAmI ?? -1}|encounter={brain.Senses.Encounter.Source}");
            Sample("senses", brain.SensesMs);
            Sample("reflex+safety", brain.ReflexMs);
            Sample("decide", brain.DecideMs);
            Sample("position", brain.PositionMs);
            Sample("navigate", brain.NavigateMs);
            Sample("finalise (incl. hands)", brain.FinaliseMs);
            Sample("brain total", brain.TotalMs);
            Sample("AI total", ai);
        }
        }
        finally
        {
            live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = liftedHere;
            AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = liftedThere;
        }
        if (!print) return;
        double allowance = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.TotalPlanningMilliseconds;
        string regime = productionClock
            ? $"under the production clock of {allowance} ms a tick — what a frame costs"
            : "with the millisecond allowances lifted — the work the brain would like to do, not what a frame costs";
        Console.WriteLine($"combat cost, {Ticks} ticks, four stationary hostiles, {regime}, milliseconds (lower is better):");
        Console.WriteLine("  phase                                   p50      p95      max     mean");
        foreach (var (phase, values) in timings)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            double Percentile(double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Floor(p * (sorted.Length - 1)))];
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {phase,-36} {Percentile(.5),8:0.000} {Percentile(.95),8:0.000} {sorted[^1],8:0.000} {sorted.Average(),8:0.000}"));
        }
        var total = timings["AI total"];
        foreach (int tick in Enumerable.Range(0, total.Count).OrderByDescending(i => total[i]).Take(3))
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  costly tick {tick}: AI {total[tick]:0.000} decide {timings["decide"][tick]:0.000} position {timings["position"][tick]:0.000} navigate {timings["navigate"][tick]:0.000}; {trace[tick]}"));
        var activities = trace.GroupBy(t => t.Split('|')[0]).Select(g => $"{g.Key}={g.Count()}");
        Console.WriteLine($"  activity ticks: {string.Join(", ", activities)}");
        var encounters = trace.GroupBy(t => t.Split('|')[4]).Select(g => $"{g.Key}={g.Count()}");
        Console.WriteLine($"  encounter ticks: {string.Join(", ", encounters)}");
    }
}
