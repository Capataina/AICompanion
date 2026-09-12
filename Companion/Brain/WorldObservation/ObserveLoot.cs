#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AICompanion.Companion.Brain.WorldObservation;

/// <summary>Items on the ground worth walking to, nearest first, with a value so the loot action can rank them.</summary>
public sealed class LootSense
{
    public readonly record struct Pickup(Item Item, float Value, float DistanceToCompanion);

    public const float SearchRadius = 1200f;

    public readonly List<Pickup> Pickups = new();

    /// <summary>A retained object can outlive its world slot. Its active flag alone
    /// does not establish that the drop still exists in the world's item table.</summary>
    public static bool OccupiesWorldSlot(Item item)
        => item.whoAmI >= 0 && item.whoAmI < Main.maxItems
            && ReferenceEquals(Main.item[item.whoAmI], item);

    public static bool IsWorldDrop(Item item)
        => OccupiesWorldSlot(item) && item.active && !item.IsAir;

    public void Update(NPC companion, Player player)
    {
        Pickups.Clear();
        foreach (Item item in Main.ActiveItems)
        {
            if (item.IsAir || item.noGrabDelay > 0)
                continue;
            float d = Vector2.Distance(item.Center, companion.Center);
            if (d > SearchRadius)
                continue;
            Pickups.Add(new Pickup(item, ValueOf(item), d));
        }
        Pickups.Sort((a, b) => a.DistanceToCompanion.CompareTo(b.DistanceToCompanion));
    }

    /// <summary>Coins and anything with sell value rank above junk, but junk still gets picked up.</summary>
    private static float ValueOf(Item item)
    {
        if (item.type is ItemID.CopperCoin or ItemID.SilverCoin or ItemID.GoldCoin or ItemID.PlatinumCoin)
            return 1f;
        return MathHelper.Clamp(0.3f + item.value * item.stack / 50000f, 0.3f, 1f);
    }
}
