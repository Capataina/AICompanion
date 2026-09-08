#nullable enable

using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace AICompanion.Inventory;

/// <summary>
/// The companion's bag: twice the player's main inventory. Pickup follows the game's
/// quick-stack rule: an item joins a stack the player already holds if there is room,
/// and otherwise goes into the bag. Weapons never live here; they are the arsenal's.
/// </summary>
public sealed class CompanionInventory
{
    /// <summary>The player's main inventory is 50 slots; the bag is double.</summary>
    public const int Slots = 100;
    private const int PlayerMainSlots = 50;

    public readonly Item[] Items = new Item[Slots];

    /// <summary>Occupied slots, for the log line on world enter.</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (Item item in Items)
                if (!item.IsAir) n++;
            return n;
        }
    }

    public CompanionInventory()
    {
        for (int i = 0; i < Slots; i++)
            Items[i] = new Item();
    }

    /// <summary>Whether a pickup has anywhere to go: a player stack with room, or a bag slot.</summary>
    public bool CanAccept(Item item, Player player)
        => FindPlayerStack(item, player) >= 0 || FindBagSlot(item) >= 0;

    /// <summary>Take the world item. Returns true if anything was taken.</summary>
    public bool Collect(Item item, Player player)
    {
        int before = item.stack;
        int slot = FindPlayerStack(item, player);
        if (slot >= 0)
        {
            Item target = player.inventory[slot];
            if (target.IsAir)
            {
                player.inventory[slot] = item.Clone();
                player.inventory[slot].stack = 0;
                target = player.inventory[slot];
            }
            int room = target.maxStack - target.stack;
            int moved = System.Math.Min(room, item.stack);
            target.stack += moved;
            item.stack -= moved;
        }
        while (item.stack > 0)
        {
            int bag = FindBagSlot(item);
            if (bag < 0)
                break;
            if (Items[bag].IsAir)
            {
                Items[bag] = item.Clone();
                Items[bag].stack = 0;
            }
            int room = Items[bag].maxStack - Items[bag].stack;
            int moved = System.Math.Min(room, item.stack);
            Items[bag].stack += moved;
            item.stack -= moved;
        }

        if (item.stack <= 0)
        {
            item.active = false;
            item.TurnToAir();
        }
        bool took = item.stack < before;
        if (took)
            SoundEngine.PlaySound(SoundID.Grab);
        return took;
    }

    private const int CoinSlotsStart = 50, CoinSlotsEnd = 54;
    private const int AmmoSlotsStart = 54, AmmoSlotsEnd = 58;

    /// <summary>
    /// A player slot the item belongs in with room, else -1. Same routing as the game's own
    /// Player.ItemSpace: the main 50 slots for everything, the purse for coins, the ammo slots
    /// for ammo. A coin also takes an empty purse slot, because the purse is where coins live.
    /// </summary>
    private static int FindPlayerStack(Item item, Player player)
    {
        int stack = FindStackIn(item, player, 0, PlayerMainSlots);
        if (stack >= 0)
            return stack;
        if (item.IsACoin)
        {
            stack = FindStackIn(item, player, CoinSlotsStart, CoinSlotsEnd);
            if (stack >= 0)
                return stack;
            for (int i = CoinSlotsStart; i < CoinSlotsEnd; i++)
                if (player.inventory[i].IsAir)
                    return i;
        }
        if (item.ammo > 0)
            return FindStackIn(item, player, AmmoSlotsStart, AmmoSlotsEnd);
        return -1;
    }

    private static int FindStackIn(Item item, Player player, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            Item slot = player.inventory[i];
            if (!slot.IsAir && slot.type == item.type && slot.prefix == item.prefix && slot.stack < slot.maxStack)
                return i;
        }
        return -1;
    }

    /// <summary>A bag slot with the same item and room, else the first empty slot, else -1.</summary>
    private int FindBagSlot(Item item)
    {
        for (int i = 0; i < Slots; i++)
            if (!Items[i].IsAir && Items[i].type == item.type && Items[i].prefix == item.prefix && Items[i].stack < Items[i].maxStack)
                return i;
        for (int i = 0; i < Slots; i++)
            if (Items[i].IsAir)
                return i;
        return -1;
    }

    public TagCompound Save()
    {
        var list = new List<TagCompound>();
        for (int i = 0; i < Slots; i++)
        {
            if (Items[i].IsAir)
                continue;
            TagCompound entry = ItemIO.Save(Items[i]);
            entry["slot"] = i;
            list.Add(entry);
        }
        return new TagCompound { ["items"] = list };
    }

    public void Load(TagCompound tag)
    {
        for (int i = 0; i < Slots; i++)
            Items[i] = new Item();
        foreach (TagCompound entry in tag.GetList<TagCompound>("items"))
        {
            int slot = entry.GetInt("slot");
            if (slot < 0 || slot >= Slots)
                continue;
            Items[slot] = ItemIO.Load(entry);
        }
    }
}
