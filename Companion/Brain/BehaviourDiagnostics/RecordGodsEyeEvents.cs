#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.SharedMovementSystem;

namespace AICompanion.Companion.Brain.BehaviourDiagnostics;

/// <summary>
/// Sparse, occurrence-time event companion to the per-tick TSV. Every method snapshots its
/// native arguments before returning: Terraria slots and entity state are mutable and may be
/// reused before a buffered file write happens.
/// </summary>
public static class GodsEyeEvents
{
    private static StreamWriter? writer;
    private static readonly Dictionary<int, int> npcGenerations = new();
    private static readonly Dictionary<int, int> projectileGenerations = new();
    private static readonly Dictionary<int, int> itemGenerations = new();
    private static int sequence;
    private static int lastMovementEdges = -1;
    private static Navigator.ExecutionStatus lastMovementStatus;
    private static PlanLocalMovement.Rejection? lastMovementRejection;
    internal static bool Active => writer != null;

    internal static void Open(string path)
    {
        Close();
        writer = new StreamWriter(path, false);
        npcGenerations.Clear(); projectileGenerations.Clear(); itemGenerations.Clear(); sequence = 0;
        RecordTerrainChunks.Reset();
        lastMovementEdges = -1;
        lastMovementRejection = null;
        Write("session", 0, "", "", "", Vector2.Zero, Vector2.Zero, Vector2.Zero, 0, "schema=1;capture=sparse-events;terrain=rolling-local-16x16-chunks;radius=3-chunks;nominal-scan=49-ticks;max-chunks-per-tick=2;unseen=unknown");
    }

    internal static void Close()
    {
        Write("session-end", 0, "", "", "", Vector2.Zero, Vector2.Zero, Vector2.Zero, sequence, "normal-close");
        try { writer?.Flush(); writer?.Dispose(); }
        catch (Exception error) { Disable(error); }
        finally { writer = null; }
    }

    internal static void Flush()
    {
        try { writer?.Flush(); }
        catch (Exception error) { Disable(error); }
    }

    public static void RecordNpcSpawn(NPC npc) => Write("npc-spawn", Next(npcGenerations, npc.whoAmI), npc.type.ToString(CultureInfo.InvariantCulture), npc.TypeName, "", npc.Center, npc.velocity, Vector2.Zero, npc.life, "");
    public static void RecordNpcDeath(NPC npc) => Write("npc-death", Stable(npcGenerations, npc.whoAmI), npc.type.ToString(CultureInfo.InvariantCulture), npc.TypeName, "", npc.Center, npc.velocity, Vector2.Zero, npc.life, "");

    public static void RecordShot(NPC shooter, NPC? target, int projectileIndex, Vector2 muzzle, Vector2 launchVelocity, Vector2 expectedImpact, string weapon, int impactTicks = -1)
    {
        int projectile = Stable(projectileGenerations, projectileIndex);
        Write("shot", Stable(npcGenerations, shooter.whoAmI), target == null ? "" : Stable(npcGenerations, target.whoAmI).ToString(CultureInfo.InvariantCulture), weapon, $"projectile={projectile}", muzzle, launchVelocity, expectedImpact, 0, $"expected-flight-ticks={impactTicks}");
    }

    public static void RecordProjectileOutcome(Projectile projectile, NPC? hit, string outcome)
        => Write("projectile-" + outcome, outcome == "spawn" ? Next(projectileGenerations, projectile.whoAmI) : Stable(projectileGenerations, projectile.whoAmI), hit == null ? "" : Stable(npcGenerations, hit.whoAmI).ToString(CultureInfo.InvariantCulture), projectile.type.ToString(CultureInfo.InvariantCulture), $"owner={projectile.owner}", projectile.Center, projectile.velocity, Vector2.Zero, projectile.damage, "");

    public static void RecordPickup(NPC companion, Item item, int amount, string destination)
        => Write("pickup", Stable(npcGenerations, companion.whoAmI), Stable(itemGenerations, item.whoAmI).ToString(CultureInfo.InvariantCulture), item.type.ToString(CultureInfo.InvariantCulture), destination, item.Center, Vector2.Zero, Vector2.Zero, amount, $"stack={item.stack}");

    public static void RecordItemSpawn(Item item) => Next(itemGenerations, item.whoAmI);

    public static void RecordEffectiveNpcDamage(NPC subject, NPC.HitInfo hit, int damageDone)
        => Write("npc-damage", Stable(npcGenerations, subject.whoAmI), "", subject.TypeName, "", subject.Center, subject.velocity, Vector2.Zero, Math.Max(0, damageDone), $"raw={hit.Damage};effective={Math.Max(0, damageDone)};life-now={subject.life};knockback={hit.Knockback:0.00}");

    public static void RecordPlayerDamage(Player subject, Player.HurtInfo hit, int lifeBefore, int lifeAfter)
        => Write("player-damage", 0, "", "player", "", subject.Center, subject.velocity, Vector2.Zero, Math.Min(hit.Damage, Math.Max(0, lifeBefore - lifeAfter)), $"raw={hit.Damage};before={lifeBefore};after={lifeAfter};knockback={hit.Knockback:0.00}");

    public static void RecordMovementOutcome(NPC companion, string step, string outcome, string detail)
        => Write("movement-" + outcome, Stable(npcGenerations, companion.whoAmI), "", step, "", companion.Center, companion.velocity, Vector2.Zero, 0, detail);

    public static void RecordDecision(NPC companion, string winner, string board, string request, string controls)
        => Write("decision", Stable(npcGenerations, companion.whoAmI), "", winner, request, companion.Center, companion.velocity, Vector2.Zero, 0, $"scores={board};controls={controls}");

    public static void RecordMovementState(NPC companion, Navigator navigator)
    {
        if (navigator.Status == lastMovementStatus && navigator.EdgeCount == lastMovementEdges
            && navigator.LastRejection == lastMovementRejection && Main.GameUpdateCount % 60 != 0) return;
        lastMovementStatus = navigator.Status;
        lastMovementEdges = navigator.EdgeCount;
        lastMovementRejection = navigator.LastRejection;
        string detail = $"status={navigator.Status};search={navigator.LastSearchStop};goal={navigator.GoalTile};partial={navigator.Path?.Partial};edges={navigator.EdgeCount};last-edge={navigator.LastEdge};preparation={navigator.PreparationResult};last-rejection={navigator.LastRejection}";
        RecordMovementOutcome(companion, navigator.Path is { Finished: false } path ? path.Current.ToString() : "none", "state", detail);
    }

    public static void RecordTerrainSnapshot(int x, int y, string data)
        => Write("terrain-snapshot", 0, "", "local-world", "post-update", new Vector2(x * 16, y * 16), Vector2.Zero, Vector2.Zero, 0, data);

    private static int Next(Dictionary<int, int> map, int slot) { int generation = map.TryGetValue(slot, out int prior) ? prior + 1 : 1; map[slot] = generation; return slot * 1_000_000 + generation; }
    private static int Stable(Dictionary<int, int> map, int slot) => map.TryGetValue(slot, out int generation) ? slot * 1_000_000 + generation : Next(map, slot);

    private static void Write(string kind, int subject, string related, string label, string channel, Vector2 position, Vector2 velocity, Vector2 expected, int amount, string detail)
    {
        if (writer == null) return;
        try
        {
            long tick = Main.GameUpdateCount; double elapsed = BrainTelemetry.ElapsedMilliseconds;
            writer.WriteLine(JsonSerializer.Serialize(new EventRecord(1, sequence++, tick, elapsed, kind, subject, related, label, channel,
                position.X, position.Y, velocity.X, velocity.Y, expected.X, expected.Y, amount, detail)));
        }
        catch (Exception error) { Disable(error); }
    }

    private static void Disable(Exception error)
    {
        var failed = writer;
        writer = null;
        try { failed?.Dispose(); } catch (IOException) { }
        Terraria.ModLoader.ModContent.GetInstance<AICompanion>().Logger.Error($"GodsEyeEvents: recording stopped: {error.GetType().Name}: {error.Message}");
    }

    private readonly record struct EventRecord(int v, int seq, long tick, double wall_elapsed_ms, string kind, int subject, string related, string label, string channel,
        float pos_x, float pos_y, float vel_x, float vel_y, float expected_x, float expected_y, int amount, string detail);
}
