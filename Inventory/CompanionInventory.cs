#nullable enable

using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;

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

    /// <summary>Whether a pickup has anywhere to go: the player's own inventory for a coin, a player stack with room, or a bag slot.</summary>
    public bool CanAccept(Item item, Player player)
        => (item.IsACoin && player.ItemSpace(item).CanTakeItem) || FindPlayerStack(item, player) >= 0 || FindBagSlot(item) >= 0;

    /// <summary>Take the world item. Returns true if anything was taken.</summary>
    public bool Collect(Item item, Player player)
    {
        int before = item.stack;
        // A coin is the player's, and it takes the player's own pickup path: the game fills the
        // purse first and then any slot, rolls a hundred into the next coin as it goes, plays the
        // sound and shows the popup, and hands back what did not fit. The bag's own merge did the
        // same on paper and left copper in the bag as stacks that never became silver in play
        // (run 5, 2026-09-08); the game's path is the one the player's own pickups already prove.
        if (item.IsACoin)
        {
            Item rest = player.GetItem(player.whoAmI, item, GetItemSettings.PickupItemFromWorld);
            if (rest.IsAir)
                item.stack = 0;
        }
        // Anything else tops up a stack the player already holds, asked again after every merge
        // because a merge can change what fits, and otherwise goes to the bag: new kinds of thing
        // are the companion's to carry, and the player's empty slots are left to the player.
        while (item.stack > 0)
        {
            int slot = FindPlayerStack(item, player);
            if (slot < 0)
                break;
            Item target = player.inventory[slot];
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
            DoCoins(bag);
        }

        if (item.stack <= 0)
        {
            item.active = false;
            item.TurnToAir();
        }
        bool took = item.stack < before;
        if (took)
        {
            SoundEngine.PlaySound(SoundID.Grab);
            Sort();
        }
        return took;
    }

    // The game's own chest sort, ItemSorting.Sort(Item[], params int[]), is private and ends by
    // glowing every slot it filled through ItemSlot.SetGlow, with the player's open chest deciding
    // which of two 58-entry glow arrays takes the index. The bag opens no chest, so that call painted
    // the player's own inventory slots with the bag's indices (the sort colour wash on the wrong
    // inventory), and would index past 58 on a fuller bag. The bag runs the same two passes the
    // game's sort does, merging partial stacks and then placing items in the order of the game's own
    // sorting layers, which carry the whole ordering knowledge, with the glow left out.
    private const System.Reflection.BindingFlags PrivateStatic = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
    private static readonly System.Reflection.MethodInfo? SetupSortingPriorities = typeof(ItemSorting).GetMethod("SetupSortingPriorities", PrivateStatic);
    private static readonly System.Reflection.FieldInfo? SortingLayers = typeof(ItemSorting).GetField("_layerList", PrivateStatic);

    /// <summary>Sort the bag the way the game sorts a chest. Never called while an item is on the cursor, so a drag is not disturbed.</summary>
    public void Sort()
    {
        if (!Main.mouseItem.IsAir || SetupSortingPriorities == null || SortingLayers == null)
            return;
        SetupSortingPriorities.Invoke(null, null);
        if (SortingLayers.GetValue(null) is not System.Collections.IEnumerable layers)
            return;

        var occupied = new List<int>();
        for (int i = 0; i < Slots; i++)
            if (!Items[i].IsAir && !Items[i].favorited)
                occupied.Add(i);
        MergeStacks(occupied);

        // Each layer takes the indices it recognises out of `occupied` and returns them in its order;
        // whatever no layer claimed follows at the end, as in the game.
        var order = new List<int>();
        foreach (object layer in layers)
        {
            if (layer.GetType().GetField("SortingMethod")?.GetValue(layer) is not System.Delegate method)
                continue;
            if (method.DynamicInvoke(layer, Items, occupied) is List<int> picked)
                order.AddRange(picked);
        }
        order.AddRange(occupied);

        var sorted = new List<Item>(order.Count);
        foreach (int i in order)
        {
            sorted.Add(Items[i]);
            Items[i] = new Item();
        }
        int next = 0;
        for (int i = 0; i < Slots && next < sorted.Count; i++)
            if (Items[i].IsAir)
                Items[i] = sorted[next++];
    }

    /// <summary>Fold partial stacks of one item type together, earlier slots first, as the game's sort does before ordering.</summary>
    private void MergeStacks(List<int> occupied)
    {
        for (int j = 0; j < occupied.Count; j++)
        {
            Item into = Items[occupied[j]];
            if (into.stack >= into.maxStack)
                continue;
            for (int k = j + 1; k < occupied.Count && into.stack < into.maxStack; k++)
            {
                Item from = Items[occupied[k]];
                if (into.type != from.type || from.stack == from.maxStack || !ItemLoader.TryStackItems(into, from, out _))
                    continue;
                if (from.stack == 0)
                {
                    Items[occupied[k]] = new Item();
                    occupied.RemoveAt(k);
                    k--;
                }
            }
        }
    }

    /// <summary>
    /// The game's Player.DoCoins for the bag: a hundred copper, silver or gold coins become one of
    /// the next coin, joining an existing stack of it when there is one, and that stack is checked
    /// in turn so a hundred silver made this way also roll up.
    /// </summary>
    private void DoCoins(int i)
    {
        Item coin = Items[i];
        if (coin.stack != 100 || (coin.type != ItemID.CopperCoin && coin.type != ItemID.SilverCoin && coin.type != ItemID.GoldCoin))
            return;
        coin.SetDefaults(coin.type + 1);
        for (int j = 0; j < Slots; j++)
        {
            if (j == i || Items[j].type != coin.type || Items[j].stack >= Items[j].maxStack)
                continue;
            Items[j].stack++;
            Items[i].TurnToAir();
            DoCoins(j);
            return;
        }
    }

    private const int AmmoSlotsStart = 54, AmmoSlotsEnd = 58;

    /// <summary>
    /// A player slot holding this item with room, else -1: the main 50 slots for everything and
    /// the ammo slots for ammo, the same places the game's own Player.ItemSpace looks. Coins never
    /// come here; they take the game's pickup path in Collect.
    /// </summary>
    private static int FindPlayerStack(Item item, Player player)
    {
        int stack = FindStackIn(item, player, 0, PlayerMainSlots);
        if (stack >= 0)
            return stack;
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
