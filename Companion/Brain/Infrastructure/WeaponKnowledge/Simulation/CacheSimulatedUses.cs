#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;

/// <summary>
/// Simulated uses, cached within the tick: the positioner scores many stands with the same weapons, and the
/// chooser re-asks what the positioner priced. The key is the weapon, its modifiers, the stand's tile, the aim's
/// cell, and the two revisions — the knowledge's and the terrain's. Enemy positions enter only through the aims,
/// which is sound because the forecast is built once per decision and the cache is cleared every tick: within one
/// tick the enemies a sim read are the enemies every sim reads.
/// </summary>
public static class CacheSimulatedUses
{
    private readonly record struct Key(int ItemType, int Slot, int ExtraProjectiles, int AddedPierce,
        int StandX, int StandY, int AimX, int AimY, int FireTick, int Knowledge, int Terrain);

    private static readonly Dictionary<Key, SimulatedUse> cached = new();
    private static int tick = -1;

    public static void ClearAtTick(int nowTick)
    {
        if (nowTick == tick) return;
        tick = nowTick;
        cached.Clear();
    }

    public static bool TryGet(WeaponId weapon, ModifierState modifiers, Vector2 muzzle, Vector2 aim, int fireTick,
        int knowledge, int terrain, out SimulatedUse? use)
        => cached.TryGetValue(ToKey(weapon, modifiers, muzzle, aim, fireTick, knowledge, terrain), out use);

    public static void Store(WeaponId weapon, ModifierState modifiers, Vector2 muzzle, Vector2 aim, int fireTick,
        int knowledge, int terrain, SimulatedUse use)
    {
        Key key = ToKey(weapon, modifiers, muzzle, aim, fireTick, knowledge, terrain);
        if (cached.Count < 256)
            cached[key] = use;
    }

    public static IReadOnlyList<SimulatedUse> Cached()
    {
        var uses = new List<SimulatedUse>(cached.Count);
        foreach (SimulatedUse use in cached.Values)
            uses.Add(use);
        return uses;
    }

    private static Key ToKey(WeaponId weapon, ModifierState modifiers, Vector2 muzzle, Vector2 aim, int fireTick,
        int knowledge, int terrain)
        => new(weapon.ItemType, weapon.Slot, modifiers.ExtraProjectiles, modifiers.AddedPierce,
            (int)(muzzle.X / 16f), (int)(muzzle.Y / 16f), (int)(aim.X / 8f), (int)(aim.Y / 8f), fireTick,
            knowledge, terrain);

    public static void Clear()
    {
        tick = -1;
        cached.Clear();
    }
}
