#nullable enable

using Terraria;
using Terraria.ModLoader;

namespace AICompanion.Companion;

/// <summary>
/// Doubles the natural enemy spawn rate while a companion is up, for balance: two fighters
/// clear a screen twice as fast, so the world sends twice as much. The game rolls a spawn
/// once every <c>spawnRate</c> ticks per player and refuses one while <c>maxSpawns</c>
/// enemies are near, so halving the interval alone is eaten by the cap; both move together.
/// The hook runs after vanilla's own arithmetic (candles, potions, depth, events) and
/// after any mod loaded before this one, on the same two integers, so it composes by
/// multiplication with anything else that scales the rate. A downed companion is not
/// fighting, and the rate returns to normal until it is revived.
/// </summary>
public sealed class CompanionSpawnRate : GlobalNPC
{
    public const float RateMultiplier = 2f;

    public override void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns)
    {
        CompanionNPC? companion = CompanionNPC.Instance;
        if (companion == null || companion.IsDowned)
            return;
        spawnRate = (int)(spawnRate / RateMultiplier);
        maxSpawns = (int)(maxSpawns * RateMultiplier);
    }
}
