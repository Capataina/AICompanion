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
        Scenario(print: false);
        Scenario(print: true);
        return 0;
    }

    private static void Scenario(bool print)
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
            trace.Add($"{brain.Chooser.Current?.Name ?? "-"}|{brain.LastRequest.Kind}|safety={brain.Safety.Kind}|aim={brain.EngageTarget?.whoAmI ?? -1}|encounter={brain.Senses.Encounter.Source}");
            Sample("senses", brain.SensesMs);
            Sample("reflex+safety", brain.ReflexMs);
            Sample("decide", brain.DecideMs);
            Sample("position", brain.PositionMs);
            Sample("navigate", brain.NavigateMs);
            Sample("finalise (incl. hands)", brain.FinaliseMs);
            Sample("brain total", brain.TotalMs);
            if (brain.ChoiceEvaluated)
                foreach (var family in brain.Chooser.Queries.LastFamilies)
                    if (family.Family.ToString() == "Combat") Sample("prepare Combat", family.Milliseconds);
            Sample("AI total", ai);
        }
        if (!print) return;
        Console.WriteLine($"combat cost, {Ticks} ticks, four stationary hostiles, milliseconds (lower is better):");
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
