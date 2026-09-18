extern alias live;
using System.Reflection;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ID;
using Terraria.UI;
using CardSystem = live::AICompanion.Companion.ProfileCard.CompanionProfileCardSystem;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;
using Bag = live::AICompanion.Companion.Inventory.CompanionBagUI;

/// <summary>
/// With the card's Inventory page open, a shift-click moves an item between the player and the companion and never reaches
/// the game's trash, and no item is lost or made. The page looks like a chest, but the game only gives a shift-click a
/// destination while a chest is open: with none (<c>player.chest == -1</c>) a player slot has no shift target, so under the
/// default settings the click falls through to picking the item up, and under the legacy shift-click-trash setting it falls
/// to <c>ItemSlot.LeftClick_SellOrTrash</c>, which trashes it, as it does a bag or gear slot the player has no room for.
/// Every click goes through the game's own path, <c>ItemSlot.OverrideHover</c> then <c>ItemSlot.LeftClick</c> for a player
/// slot and the bag page's own slot handler for a bag or gear slot, with shift held on the keyboard state and the mod's
/// <c>ShiftClickSlot</c> reachable through its filled loader list.
/// </summary>
internal static class VerifyShiftClickFillsTheBag
{
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public static void Run()
    {
        VerifyEscapeClosesOneCompanionPanel.RegisteredSystem();
        Player player = Main.LocalPlayer;
        var save = player.GetModPlayer<CompanionPlayer>();
        var bag = save.Bag;
        var gear = save.Gear;
        MethodInfo cardSlotHandler = typeof(Bag).GetMethod("HandleSlot", BindingFlags.Static | BindingFlags.NonPublic)!;
        Item[] savedPlayer = player.inventory.Select(item => item.Clone()).ToArray();
        Item[] savedBag = bag.Items.Select(item => item.Clone()).ToArray();
        Item[] savedGear = gear.Slots.Select(item => item.Clone()).ToArray();
        Item[] savedBank = player.bank.item.Select(item => item.Clone()).ToArray();
        int savedChest = player.chest;
        Item savedTrash = player.trashItem.Clone(), savedCursor = Main.mouseItem.Clone();
        KeyboardState keys = Main.keyState;
        bool trashSetting = ItemSlot.Options.DisableLeftShiftTrashCan, left = Main.mouseLeft, release = Main.mouseLeftRelease;
        bool dedicated = Main.dedServ, itemText = Main.showItemText, inventory = Main.playerInventory;
        int cursorOverride = Main.cursorOverride;

        static void Clear(Item[] items) { for (int i = 0; i < items.Length; i++) items[i] = new Item(); }
        static void Put(Item[] items, int slot, int type, int stack, bool favourite = false)
        { items[slot] = new Item(); items[slot].SetDefaults(type); items[slot].stack = stack; items[slot].favorited = favourite; }
        static int Of(IEnumerable<Item> items, int type) => items.Where(item => item.type == type).Sum(item => item.stack);
        Dictionary<int, int> Totals() => player.inventory.Concat(bag.Items).Concat(gear.Slots).Concat(player.bank.item).Append(Main.mouseItem)
            .Where(item => !item.IsAir).GroupBy(item => item.type).ToDictionary(g => g.Key, g => g.Sum(item => item.stack));
        static string Show(Dictionary<int, int> totals) => string.Join(", ", totals.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}x{kv.Value}"));

        void ShiftClickPlayerSlot(int slot)
        {
            int context = slot < 50 ? ItemSlot.Context.InventoryItem : slot < 54 ? ItemSlot.Context.InventoryCoin : ItemSlot.Context.InventoryAmmo;
            Main.cursorOverride = -1;
            Main.mouseLeft = Main.mouseLeftRelease = true;
            ItemSlot.OverrideHover(player.inventory, context, slot);
            ItemSlot.LeftClick(player.inventory, context, slot);
            Main.mouseLeft = false;
        }
        void ShiftClickCardSlot(Item[] items, int index)
        {
            Main.cursorOverride = -1;
            Main.mouseLeft = Main.mouseLeftRelease = true;
            object[] slot = { items[index] };
            cardSlotHandler.Invoke(null, slot);
            items[index] = (Item)slot[0];
            Main.mouseLeft = false;
        }

        using var hook = EnableModPlayerHooks.For("HookShiftClickSlot", save);
        try
        {
            Main.dedServ = true;
            Main.showItemText = false;
            if (CardSystem.IsOpen) CardSystem.CloseOpenCard();
            CardSystem.OpenInventory();
            Require(CardSystem.InventoryOpen, "premise: the card's Inventory page must be open");
            Main.keyState = new KeyboardState(Keys.LeftShift);
            Require(ItemSlot.ShiftInUse, "premise: shift must read as held");
            var passed = new List<string>();
            foreach (bool legacyShiftTrash in new[] { true, false })
            {
                ItemSlot.Options.DisableLeftShiftTrashCan = !legacyShiftTrash;
                string setting = legacyShiftTrash ? "under the legacy shift-click trash setting" : "under the default settings";

                void Case(string name, Action arrange, Action click, Func<bool> placed, Func<string> describe)
                {
                    Clear(player.inventory); Clear(bag.Items); Clear(gear.Slots); Clear(player.bank.item);
                    player.chest = -1;
                    player.trashItem = new Item();
                    Main.mouseItem = new Item();
                    arrange();
                    var before = Totals();
                    click();
                    var after = Totals();
                    string label = $"{name} {setting}";
                    Require(player.trashItem.IsAir, $"{label}: the shift-click sent {player.trashItem.stack} of item {player.trashItem.type} to the trash");
                    Require(before.Count == after.Count && before.All(kv => after.TryGetValue(kv.Key, out int n) && n == kv.Value),
                        $"{label}: items across the bag, gear, player inventory and cursor were lost or made: before [{Show(before)}], after [{Show(after)}]");
                    Require(Main.mouseItem.IsAir, $"{label}: the shift-click put {Main.mouseItem.stack} of item {Main.mouseItem.type} on the cursor");
                    Require(placed(), $"{label}: {describe()}");
                    passed.Add(label);
                }

                Case("a stack into a bag with room", () => Put(player.inventory, 15, ItemID.Wood, 40), () => ShiftClickPlayerSlot(15),
                    () => player.inventory[15].IsAir && Of(bag.Items, ItemID.Wood) == 40,
                    () => $"the wood must go into the bag; the bag holds {Of(bag.Items, ItemID.Wood)} and the slot {player.inventory[15].stack}");
                Case("a stack into a full bag", () =>
                    {
                        for (int i = 0; i < bag.Items.Length; i++) Put(bag.Items, i, ItemID.DirtBlock, 9999);
                        Put(player.inventory, 16, ItemID.StoneBlock, 30);
                    }, () => ShiftClickPlayerSlot(16),
                    () => player.inventory[16].type == ItemID.StoneBlock && player.inventory[16].stack == 30,
                    () => $"nothing fits, so the stone must stay in its slot; it holds {player.inventory[16].stack} of item {player.inventory[16].type}");
                Case("a stack only part of which fits", () =>
                    {
                        for (int i = 1; i < bag.Items.Length; i++) Put(bag.Items, i, ItemID.DirtBlock, 9999);
                        Put(bag.Items, 0, ItemID.Gel, 9990);
                        Put(player.inventory, 17, ItemID.Gel, 20);
                    }, () => ShiftClickPlayerSlot(17),
                    () => player.inventory[17].stack == 11 && Of(bag.Items, ItemID.Gel) == 9999,
                    () => $"nine gel fit and eleven must stay; the slot holds {player.inventory[17].stack} and the bag {Of(bag.Items, ItemID.Gel)}");
                Case("a coin", () => Put(player.inventory, 50, ItemID.CopperCoin, 30), () => ShiftClickPlayerSlot(50),
                    () => Of(player.inventory, ItemID.CopperCoin) == 30 && Of(bag.Items, ItemID.CopperCoin) == 0,
                    () => $"the bag is cargo, not a purse, so the coins must stay with the player; he holds {Of(player.inventory, ItemID.CopperCoin)}");
                Case("a favourite", () => Put(player.inventory, 18, ItemID.StoneBlock, 7, favourite: true), () => ShiftClickPlayerSlot(18),
                    () => player.inventory[18].stack == 7 && Of(bag.Items, ItemID.StoneBlock) == 0,
                    () => $"a favourite stays with the player, as Deposit All keeps it; the slot holds {player.inventory[18].stack}");
                // With a container open beside the card the game has a shift-click destination of its own, and it keeps it.
                Case("a stack with the piggy bank also open", () => { Put(player.inventory, 19, ItemID.Wood, 40); player.chest = -2; },
                    () => ShiftClickPlayerSlot(19),
                    () => Of(player.bank.item, ItemID.Wood) == 40 && Of(bag.Items, ItemID.Wood) == 0,
                    () => $"the open piggy bank is the game's destination, so the wood must go there; it holds {Of(player.bank.item, ItemID.Wood)} and the bag {Of(bag.Items, ItemID.Wood)}");
                Case("a bag stack to a player with room", () => Put(bag.Items, 0, ItemID.Wood, 25), () => ShiftClickCardSlot(bag.Items, 0),
                    () => Of(player.inventory, ItemID.Wood) == 25 && Of(bag.Items, ItemID.Wood) == 0,
                    () => $"the wood must go to the player; he holds {Of(player.inventory, ItemID.Wood)} and the bag {Of(bag.Items, ItemID.Wood)}");
                // The game writes a bag slot's item back into the slot it came from after the click, so anything that reorders
                // the bag during the click puts that write over the item that moved in; a second stack behind the first is what
                // a reorder would move.
                Case("a bag stack with another stack behind it", () => { Put(bag.Items, 0, ItemID.Wood, 25); Put(bag.Items, 1, ItemID.StoneBlock, 10); },
                    () => ShiftClickCardSlot(bag.Items, 0),
                    () => Of(player.inventory, ItemID.Wood) == 25 && Of(bag.Items, ItemID.StoneBlock) == 10,
                    () => $"the wood must go to the player and the stone stay in the bag; he holds {Of(player.inventory, ItemID.Wood)} wood and the bag {Of(bag.Items, ItemID.StoneBlock)} stone");
                Case("a bag stack to a full player", () =>
                    {
                        for (int i = 0; i < 50; i++) Put(player.inventory, i, ItemID.DirtBlock, 9999);
                        Put(bag.Items, 0, ItemID.Wood, 25);
                    }, () => ShiftClickCardSlot(bag.Items, 0),
                    () => Of(bag.Items, ItemID.Wood) == 25,
                    () => $"nothing fits, so the wood must stay in the bag; it holds {Of(bag.Items, ItemID.Wood)}");
                Case("a gear pickaxe to the player", () => Put(gear.Slots, 2, ItemID.CopperPickaxe, 1), () => ShiftClickCardSlot(gear.Slots, 2),
                    () => Of(player.inventory, ItemID.CopperPickaxe) == 1 && gear.Slots[2].IsAir,
                    () => $"the pickaxe must go to the player; he holds {Of(player.inventory, ItemID.CopperPickaxe)} and the slot {gear.Slots[2].stack}");
            }
            Console.WriteLine($"shift-click with the Inventory page open: {passed.Count} cases, nothing trashed, lost, made or left on the cursor: {string.Join("; ", passed)}");
        }
        finally
        {
            if (CardSystem.IsOpen) CardSystem.CloseOpenCard();
            for (int i = 0; i < savedPlayer.Length; i++) player.inventory[i] = savedPlayer[i];
            for (int i = 0; i < savedBag.Length; i++) bag.Items[i] = savedBag[i];
            for (int i = 0; i < savedGear.Length; i++) gear.Slots[i] = savedGear[i];
            for (int i = 0; i < savedBank.Length; i++) player.bank.item[i] = savedBank[i];
            player.chest = savedChest;
            player.trashItem = savedTrash;
            Main.mouseItem = savedCursor;
            Main.keyState = keys;
            ItemSlot.Options.DisableLeftShiftTrashCan = trashSetting;
            Main.mouseLeft = left; Main.mouseLeftRelease = release;
            Main.dedServ = dedicated; Main.showItemText = itemText; Main.playerInventory = inventory;
            Main.cursorOverride = cursorOverride;
        }
    }
}
