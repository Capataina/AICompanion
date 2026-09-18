#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;

/// <summary>
/// Simulated uses cached across ticks for the planner: the per-tick cache is cleared before every
/// decision because the hands must aim from this tick's forecast, but a search prices the same stands
/// sixty times a second against enemies that mostly stand still, and re-flying every aim every tick
/// spent whole frames deciding not to fight. Entries carry the enemy content the sim read — every
/// forecast body's slot, tile, life, velocity, defence and burning flag — so a moved, wounded or newly
/// burning body misses and re-flies, while a frozen crowd hits. What the key cannot see is the
/// observed-motion tracker's drift inside one tile of travel: predictions shift sub-tile while the key
/// holds, which is planning-grade noise the dominance tolerances absorb, never an aim, because the
/// hands read only the per-tick cache. Fresh sims are dual-written to the per-tick cache, so the
/// overlay and the hands read this tick exactly as they did before this cache existed.
/// </summary>
public static class CachePlannedSims
{
    /// <summary>
    /// How many simulated uses the cross-tick cache holds before a wholesale clear: the seven
    /// generators' probe working set on a crowd runs past six hundred distinct aims, and a smaller
    /// cap clears mid-decision and re-flies everything every tick — the thrash this size absorbs.
    /// </summary>
    private const int Capacity = 2048;

    private readonly record struct Key(int ItemType, int Slot, int ExtraProjectiles, int AddedPierce,
        int StandX, int StandY, int AimX, int AimY, int FireTick, int Knowledge, int Terrain, int Enemies);

    /// <summary>When false, every lookup misses and every store is skipped, so C1 can price the same
    /// decision with the cache and without it. Production never turns this off.</summary>
    public static bool Enabled { get; set; } = true;

    private static readonly Dictionary<Key, SimulatedUse> cached = new();

    private readonly record struct BestKey(int ItemType, int Slot, int ExtraProjectiles, int AddedPierce,
        int TargetSlot, int TargetGeneration, int StandX, int StandY, int FireTick, int Knowledge, int Terrain,
        int Enemies);

    private static readonly Dictionary<BestKey, ForecastUses.AimedUse?> bestCached = new();

    public static bool TryGet(WeaponId weapon, ModifierState modifiers, Vector2 muzzle, Vector2 aim,
        int fireTick, int knowledge, int terrain, IReadOnlyList<EnemyForecast> enemies, out SimulatedUse? use)
    {
        use = null;
        return Enabled && cached.TryGetValue(ToKey(weapon, modifiers, muzzle, aim, fireTick, knowledge, terrain, enemies), out use);
    }

    public static void Store(WeaponId weapon, ModifierState modifiers, Vector2 muzzle, Vector2 aim,
        int fireTick, int knowledge, int terrain, IReadOnlyList<EnemyForecast> enemies, SimulatedUse use)
    {
        if (!Enabled)
            return;
        if (cached.Count >= Capacity)
            cached.Clear();
        cached[ToKey(weapon, modifiers, muzzle, aim, fireTick, knowledge, terrain, enemies)] = use;
    }

    private static Key ToKey(WeaponId weapon, ModifierState modifiers, Vector2 muzzle, Vector2 aim,
        int fireTick, int knowledge, int terrain, IReadOnlyList<EnemyForecast> enemies)
        => new(weapon.ItemType, weapon.Slot, modifiers.ExtraProjectiles, modifiers.AddedPierce,
            (int)(muzzle.X / 16f), (int)(muzzle.Y / 16f), (int)(aim.X / 8f), (int)(aim.Y / 8f), fireTick,
            knowledge, terrain, EnemyContentMemoized(enemies));

    private static IReadOnlyList<EnemyForecast>? hashedEnemies;
    private static int hashedContent;

    /// <summary>
    /// The enemy content hash, memoized by list reference: one decision asks it hundreds of times
    /// about the same list, and re-sorting and re-folding nine bodies per probe spent most of a
    /// hopeless crowd's decision. Safe because the list is built once per tick and nothing mutates
    /// a forecast mid-decision — the same guarantee the simulator already relies on — so the same
    /// reference always carries the same content.
    /// </summary>
    private static int EnemyContentMemoized(IReadOnlyList<EnemyForecast> enemies)
    {
        if (!ReferenceEquals(enemies, hashedEnemies))
        {
            hashedEnemies = enemies;
            hashedContent = EnemyContent(enemies);
        }
        return hashedContent;
    }

    /// <summary>
    /// Every forecast body folded to one hash in slot order, so a reordered threat list still hits:
    /// where it stands, how much life it has left, how fast it moves, what armour it wears, whether it
    /// burns, and what it is. Anything the sim reads that moves or changes must be here, or a stale
    /// flight prices a fight that moved on.
    /// </summary>
    private static int EnemyContent(IReadOnlyList<EnemyForecast> enemies)
    {
        var slots = new List<int>(enemies.Count);
        foreach (EnemyForecast enemy in enemies)
            slots.Add(enemy.Slot);
        slots.Sort();
        unchecked
        {
            int hash = (int)2166136261;
            foreach (int slot in slots)
            {
                foreach (EnemyForecast enemy in enemies)
                {
                    if (enemy.Slot != slot)
                        continue;
                    hash = (hash ^ slot) * 16777619;
                    hash = (hash ^ (int)(enemy.Box.Center.X / 16f)) * 16777619;
                    hash = (hash ^ (int)(enemy.Box.Center.Y / 16f)) * 16777619;
                    hash = (hash ^ (int)enemy.Life) * 16777619;
                    hash = (hash ^ (int)(enemy.Velocity.X * 2f)) * 16777619;
                    hash = (hash ^ (int)(enemy.Velocity.Y * 2f)) * 16777619;
                    hash = (hash ^ enemy.Defense) * 16777619;
                    hash = (hash ^ (enemy.OnFire2 ? 1 : 0)) * 16777619;
                    hash = (hash ^ enemy.NpcType) * 16777619;
                    break;
                }
            }
            return hash;
        }
    }

    /// <summary>
    /// One weapon's best use at one target from one muzzle tile, as <see cref="ForecastUses.BestAimUse"/>
    /// priced it: the solver and every aim's flight skipped while the key holds. A stored null is a proven
    /// miss — no aim struck anything — so a hopeless pair re-solves only when something it reads moves.
    /// </summary>
    public static bool TryGetBest(WeaponId weapon, ModifierState modifiers, int targetSlot, int targetGeneration,
        Vector2 muzzle, int fireTick, int knowledge, int terrain, IReadOnlyList<EnemyForecast> enemies,
        out ForecastUses.AimedUse? aimed)
    {
        var key = new BestKey(weapon.ItemType, weapon.Slot, modifiers.ExtraProjectiles, modifiers.AddedPierce,
            targetSlot, targetGeneration, (int)(muzzle.X / 16f), (int)(muzzle.Y / 16f), fireTick,
            knowledge, terrain, EnemyContentMemoized(enemies));
        if (Enabled && bestCached.TryGetValue(key, out ForecastUses.AimedUse? found))
        {
            aimed = found;
            return true;
        }
        aimed = null;
        return false;
    }

    public static void StoreBest(WeaponId weapon, ModifierState modifiers, int targetSlot, int targetGeneration,
        Vector2 muzzle, int fireTick, int knowledge, int terrain, IReadOnlyList<EnemyForecast> enemies,
        ForecastUses.AimedUse? aimed)
    {
        if (!Enabled)
            return;
        if (bestCached.Count >= Capacity)
            bestCached.Clear();
        bestCached[new BestKey(weapon.ItemType, weapon.Slot, modifiers.ExtraProjectiles, modifiers.AddedPierce,
            targetSlot, targetGeneration, (int)(muzzle.X / 16f), (int)(muzzle.Y / 16f), fireTick,
            knowledge, terrain, EnemyContentMemoized(enemies))] = aimed;
    }

    public static void Clear()
    {
        cached.Clear();
        bestCached.Clear();
        hashedEnemies = null;
    }
}
