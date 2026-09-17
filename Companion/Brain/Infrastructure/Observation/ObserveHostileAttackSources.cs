#nullable enable
using System;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>Projectile evidence belongs to its engine-provided source, never the nearest NPC.
/// Missing attribution stays unknown; the projectile itself is still observed as a hazard.</summary>
public static class HostileAttackSources
{
    private sealed record Shot(int Generation, uint Tick, int Damage);
    private static readonly int[] generations = new int[Main.maxNPCs];
    private static readonly Shot?[] shots = new Shot?[Main.maxNPCs];
    public static int Generation(NPC npc) => generations[npc.whoAmI];
    public static void Spawn(NPC npc)
    {
        generations[npc.whoAmI]++;
        shots[npc.whoAmI] = null;
        PredictObservedMotion.Forget(npc.whoAmI);
    }
    public static void Observe(Projectile projectile, IEntitySource source)
    {
        if (projectile.hostile && projectile.damage > 0 && source is EntitySource_Parent { Entity: NPC npc })
            shots[npc.whoAmI] = new Shot(Generation(npc), Main.GameUpdateCount, projectile.damage);
    }
    public static int RecentDamage(NPC npc)
    {
        Shot? shot = shots[npc.whoAmI];
        return shot != null && shot.Generation == Generation(npc) && unchecked(Main.GameUpdateCount - shot.Tick) <= 240
            ? shot.Damage : 0;
    }
    /// <summary>The recent shot a snapshot stamps for the slot, or null when none is recorded under this
    /// generation: the absolute tick, so the restore's 240-tick window reads what the live one read. Aged-out
    /// shots export too — the window gates them identically on both sides, so exporting unconditionally is exact.</summary>
    public static (uint Tick, int Damage)? ExportShot(NPC npc)
    {
        Shot? shot = shots[npc.whoAmI];
        return shot != null && shot.Generation == Generation(npc) ? (shot.Tick, shot.Damage) : null;
    }
    /// <summary>Install a stamped generation: the audit's bodies never spawned, so no hook stamped them.</summary>
    public static void AssumeGeneration(NPC npc, int generation) => generations[npc.whoAmI] = generation;
    /// <summary>Install a stamped recent shot after its generation: the window is read against the restored
    /// GameUpdateCount, so the stamped absolute tick reproduces the live answer.</summary>
    public static void AssumeShot(NPC npc, int generation, uint tick, int damage)
        => shots[npc.whoAmI] = new Shot(generation, tick, damage);
    public static void Clear() { Array.Clear(shots); Array.Clear(generations); }
}

public sealed class ObserveHostileProjectileSource : GlobalProjectile
{
    public override void OnSpawn(Projectile projectile, IEntitySource source) => HostileAttackSources.Observe(projectile, source);
}

public sealed class ObserveHostileGeneration : GlobalNPC
{
    public override void OnSpawn(NPC npc, IEntitySource source) => HostileAttackSources.Spawn(npc);
}

public sealed class ResetHostileAttackSources : ModSystem
{
    public override void OnWorldLoad() => HostileAttackSources.Clear();
    public override void OnWorldUnload() => HostileAttackSources.Clear();
}
