#nullable enable

using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;

namespace AICompanion.Companion.Inventory;

/// <summary>
/// The companion's bag: twice the player's main inventory. Pickup follows the game's
/// quick-stack rule: an item joins a stack the player already holds if there is room,
/// and otherwise goes into the bag. Weapons never live here; they are the arsenal's.
/// </summary>
public sealed class CompanionInventory
{
    /// <summary>
    /// The bag's base size, by the owner's ruling of 15 September 2026, which the mastery tree's Bag space levels would take
    /// to 200. A save records each item by its slot index, so a bag saved when this was 100 loads every item into its own
    /// slot and the new slots empty; lowering this would drop the items saved past the new end.
    /// </summary>
    public const int Slots = 120;
    private const int PlayerMainSlots = 50;

    public readonly Item[] Items = new Item[Slots];
    /// <summary>Transient pickup summary for the card; it is deliberately not save data.</summary>
    public string? LastPickup { get; private set; }

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

    /// <summary>Whether a pickup has anywhere to go: some of it would be taken now.</summary>
    public bool CanAccept(Item item, Player player) => AcceptableQuantity(item, player) > 0;

    /// <summary>
    /// How many of <paramref name="item"/>'s stack a pickup would take now, by the routes <see cref="Collect"/> uses: a coin
    /// through the player's own pickup path, otherwise player stacks of the item with room, then bag stacks of it with room and
    /// empty bag slots. Collection prices an offer by this rather than by the whole stack, because a partly full cargo takes
    /// part of a drop and leaves the rest in the world.
    /// </summary>
    public int AcceptableQuantity(Item item, Player player)
    {
        if (item.IsAir || item.stack <= 0)
            return 0;
        // The game's coin pickup rolls coins into higher denominations as it fills the purse, so its room is not a sum of
        // slot space; when the purse can take the coin at all, Collect's own path takes the stack.
        if (item.IsACoin && player.ItemSpace(item).CanTakeItem)
            return item.stack;
        long room = 0;
        for (int i = 0; i < PlayerMainSlots; i++)
            room += RoomFor(player.inventory[i], item);
        if (item.ammo > 0)
            for (int i = AmmoSlotsStart; i < AmmoSlotsEnd; i++)
                room += RoomFor(player.inventory[i], item);
        foreach (Item slot in Items)
            room += slot.IsAir ? item.maxStack : RoomFor(slot, item);
        return (int)System.Math.Min(room, item.stack);
    }

    private static int RoomFor(Item slot, Item item)
        => !slot.IsAir && slot.type == item.type && slot.prefix == item.prefix && slot.stack < slot.maxStack ? slot.maxStack - slot.stack : 0;

    /// <summary>Monotonic count of pickups this cargo accepted. A purpose marks it when it begins and asks
    /// <see cref="TransferredSince"/> when it ends, so only a transfer after the mark is credited to that purpose.</summary>
    public long TransferSequence { get; private set; }

    // The most recent accepted transfers, oldest overwritten first. A purpose that outlives more transfers than this can only
    // undercount what it received, never credit a transfer it did not see; collection attempts end long before that.
    internal const int RecentTransferCapacity = 32;
    private readonly (long Sequence, Item? Source, int Quantity)[] recentTransfers = new (long, Item?, int)[RecentTransferCapacity];

    /// <summary>How many of <paramref name="source"/>, the world item object itself, this cargo accepted after <paramref name="mark"/>.</summary>
    public int TransferredSince(long mark, Item source)
    {
        int total = 0;
        foreach (var transfer in recentTransfers)
            if (transfer.Sequence > mark && ReferenceEquals(transfer.Source, source))
                total += transfer.Quantity;
        return total;
    }

    /// <summary>Take the world item. Returns true if anything was taken.</summary>
    public bool Collect(Item item, Player player)
    {
        int before = item.stack;
        string pickupName = item.Name;
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
            // One line per coin pickup, so the next playtest's log measures where a coin went
            // instead of the report guessing at it.
            ModContent.GetInstance<AICompanion>().Logger.Info(
                $"coin pickup: {before} {item.Name} -> purse took {before - item.stack}, bag gets {item.stack}");
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
            // Every accepted pickup goes through here, contact pickup during any activity included, so this is the one
            // record of what the cargo received from which world item.
            TransferSequence++;
            recentTransfers[(int)(TransferSequence % recentTransfers.Length)] = (TransferSequence, item, before - item.stack);
            LastPickup = $"Last: {pickupName} x{before - item.stack}";
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

    // ---- the chest buttons: the game's four container transfers, against the bag instead of a chest ----
    //
    // Terraria.UI.ChestUI implements Loot All, Deposit All, Quick Stack and Restock, and none of them can be called for the
    // bag: every one switches on player.chest and indexes the fixed forty-slot arrays of a world chest or one of the player's
    // banks. These are the same transfers written against this array, with the same choices of which player slots take
    // part, read from the decompiled ChestUI rather than recalled. The one deliberate difference is coins: the game moves
    // coins between purse and container, and the bag is cargo rather than a purse, so its transfers leave coins where they
    // are. Each returns how many items moved and sorts the bag afterwards, because the card's bag sorts itself after every
    // transfer and has no Sort button.

    /// <summary>Loot All: every bag stack offered to the player through the game's own insertion, the remainder left in place.</summary>
    public int LootAll(Player player)
    {
        int moved = 0;
        for (int i = 0; i < Slots; i++)
        {
            if (Items[i].IsAir) continue;
            int before = Items[i].stack;
            Items[i].position = player.Center;
            Items[i] = player.GetItem(player.whoAmI, Items[i], GetItemSettings.LootAllSettings);
            moved += before - (Items[i].IsAir ? 0 : Items[i].stack);
        }
        Sort();
        return moved;
    }

    /// <summary>
    /// Deposit All: the player's main inventory below the hotbar, last slot first, favourites and coins kept back; each
    /// stack tops up matching bag stacks, and what is left takes the first empty bag slot.
    /// </summary>
    public int DepositAll(Player player)
    {
        int moved = 0;
        for (int slot = PlayerMainSlots - 1; slot >= HotbarSlots; slot--)
            moved += DepositFrom(player, slot);
        Sort();
        return moved;
    }

    /// <summary>
    /// One player slot into the bag by Deposit All's own rule, what a shift-click on that slot does while the card's Inventory
    /// page is open: favourites and coins stay, the stack tops up matching bag stacks and then takes an empty slot, and what
    /// does not fit stays in the player's slot. Any slot may be named, the hotbar and ammo slots included, because a
    /// shift-click is aimed at one item where Deposit All sweeps. Returns how many items moved.
    /// </summary>
    public int DepositSlot(Player player, int slot)
    {
        int moved = DepositFrom(player, slot);
        if (moved > 0) Sort();
        return moved;
    }

    private int DepositFrom(Player player, int slot)
    {
        Item from = player.inventory[slot];
        if (from.IsAir || from.stack <= 0 || from.favorited || from.IsACoin) return 0;
        int before = from.stack;
        if (from.maxStack > 1)
            for (int i = 0; i < Slots && from.stack > 0; i++)
                if (!Items[i].IsAir && Items[i].stack < Items[i].maxStack && Items[i].netID == from.netID)
                    ItemLoader.TryStackItems(Items[i], from, out _);
        if (from.stack > 0)
            for (int i = 0; i < Slots; i++)
                if (Items[i].IsAir) { Items[i] = from.Clone(); from.stack = 0; break; }
        if (from.stack <= 0) player.inventory[slot] = new Item();
        return before - System.Math.Max(0, from.stack);
    }

    /// <summary>
    /// Quick Stack: from the player's main inventory below the hotbar, favourites and coins kept back, only kinds the bag
    /// already holds move; they top up the bag's stacks, then take empty bag slots.
    /// </summary>
    public int QuickStack(Player player)
    {
        var kinds = new HashSet<int>();
        foreach (Item item in Items)
            if (!item.IsAir && !item.IsACoin) kinds.Add(item.netID);
        int moved = 0;
        for (int slot = HotbarSlots; slot < PlayerMainSlots; slot++)
        {
            Item from = player.inventory[slot];
            if (from.IsAir || from.favorited || from.IsACoin || !kinds.Contains(from.netID)) continue;
            int before = from.stack;
            for (int i = 0; i < Slots && from.stack > 0; i++)
                if (!Items[i].IsAir && Items[i].netID == from.netID && Items[i].stack < Items[i].maxStack)
                    ItemLoader.TryStackItems(Items[i], from, out _);
            if (from.stack > 0)
                for (int i = 0; i < Slots; i++)
                    if (Items[i].IsAir) { Items[i] = from.Clone(); from.stack = 0; break; }
            moved += before - System.Math.Max(0, from.stack);
            if (from.stack <= 0) player.inventory[slot] = new Item();
        }
        Sort();
        return moved;
    }

    /// <summary>
    /// Restock: bag items of a stackable kind the player already carries top up the player's partial stacks of that kind,
    /// hotbar and ammo slots included and coin slots excluded, each move asked of the game's own slot rules; ammo left over
    /// may also take an empty player slot the game would accept it in.
    /// </summary>
    public int Restock(Player player)
    {
        Item[] inventory = player.inventory;
        var kinds = new HashSet<int>();
        var partial = new List<int>();
        var empty = new List<int>();
        for (int n = AmmoSlotsEnd - 1; n >= 0; n--)
        {
            if (n >= PlayerMainSlots && n < AmmoSlotsStart) continue;
            if (inventory[n].IsACoin) continue;
            if (inventory[n].stack > 0 && inventory[n].maxStack > 1)
            {
                kinds.Add(inventory[n].netID);
                if (inventory[n].stack < inventory[n].maxStack) partial.Add(n);
            }
            else if (inventory[n].IsAir) empty.Add(n);
        }
        int moved = 0;
        for (int i = 0; i < Slots; i++)
        {
            if (Items[i].IsAir || !kinds.Contains(Items[i].netID)) continue;
            int before = Items[i].stack;
            bool emptied = false;
            for (int j = 0; j < partial.Count; j++)
            {
                int n = partial[j];
                int context = n >= PlayerMainSlots ? 2 : 0;
                if (inventory[n].netID != Items[i].netID || ItemSlot.PickItemMovementAction(inventory, context, n, Items[i]) == -1
                    || !ItemLoader.TryStackItems(inventory[n], Items[i], out _)) continue;
                if (inventory[n].stack == inventory[n].maxStack) { partial.RemoveAt(j); j--; }
                if (Items[i].stack == 0) { Items[i] = new Item(); emptied = true; break; }
            }
            if (!emptied && empty.Count > 0 && Items[i].ammo != 0)
                for (int k = 0; k < empty.Count; k++)
                {
                    int n = empty[k];
                    int context = n >= PlayerMainSlots ? 2 : 0;
                    if (ItemSlot.PickItemMovementAction(inventory, context, n, Items[i]) == -1) continue;
                    Utils.Swap(ref inventory[n], ref Items[i]);
                    partial.Add(n);
                    empty.RemoveAt(k);
                    break;
                }
            moved += before - (Items[i].IsAir ? 0 : Items[i].stack);
        }
        Sort();
        return moved;
    }

    /// <summary>The hotbar's ten slots, which the game's own Deposit All and Quick Stack never take from.</summary>
    private const int HotbarSlots = 10;
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
        LastPickup = null;
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
