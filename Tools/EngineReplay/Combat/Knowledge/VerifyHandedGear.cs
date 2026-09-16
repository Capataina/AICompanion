extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Tools.Ledger;
using CompanionGear = live::AICompanion.Companion.Inventory.CompanionGear;
using CompanionInventory = live::AICompanion.Companion.Inventory.CompanionInventory;
using GearSlot = live::AICompanion.Companion.Inventory.GearSlot;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;

/// <summary>
/// The four gear slots against five real items: a bow, a sword, a magic wand, a copper pickaxe and
/// a yoyo. The slot predicate is the whole of what decides whether the companion can use a thing,
/// so each item is asked of each slot and the refusals carry their reason; the tool powers the slots
/// expose are then held against the game's own pick gate, because a power the slot reports that the
/// game would not gate a tile with is a number, not a capability.
/// </summary>
internal static class VerifyHandedGear
{
    public static int Run()
    {
        VerifyCompanionLifecycle.Create();
        FiveItemsFindTheirSlots();
        GearPersistsAndKeepsWhatNoLongerFits();
        AHundredSlotBagLoadsIntoTheLargerBag();
        ToolPowerIsWhatTheGameGatesWith();
        Console.WriteLine("handed gear: a bow, a sword and a wand fit a weapon slot, a pickaxe only its own, a yoyo nowhere, and the pick power the slot reads is the one the game gates a tile with");
        return 0;
    }

    /// <summary>
    /// The bag grew from 100 slots to 120. A character saved with a full 100-slot bag must load with every item in the
    /// slot it was saved in, at its stack, and the twenty new slots empty. The save is built the way a 100-slot bag wrote
    /// it, one entry per slot index; each stack is distinct so an item moved to another slot shows, and the loading bag
    /// starts with its last twenty slots full so a load that clears only the old hundred leaves them behind.
    /// </summary>
    private static void AHundredSlotBagLoadsIntoTheLargerBag()
    {
        const int oldSize = 100;
        var entries = new List<Terraria.ModLoader.IO.TagCompound>();
        for (int slot = 0; slot < oldSize; slot++)
        {
            // Built rather than cloned from the content samples, which the headless shell fills only for the items other rows read.
            var item = new Item();
            item.SetDefaults(ItemID.Wood);
            item.stack = slot + 1;
            var entry = Terraria.ModLoader.IO.ItemIO.Save(item);
            entry["slot"] = slot;
            entries.Add(entry);
        }
        var saved = new Terraria.ModLoader.IO.TagCompound { ["items"] = entries };

        var bag = new CompanionInventory();
        Require(CompanionInventory.Slots == 120 && bag.Items.Length == 120, $"the bag's base size is 120 slots; it is {CompanionInventory.Slots}");
        for (int slot = oldSize; slot < CompanionInventory.Slots; slot++) { bag.Items[slot] = new Item(); bag.Items[slot].SetDefaults(ItemID.DirtBlock); }
        bag.Load(saved);

        int misplaced = Enumerable.Range(0, oldSize).Count(slot => bag.Items[slot].type != ItemID.Wood || bag.Items[slot].stack != slot + 1);
        int filledPastOld = Enumerable.Range(oldSize, CompanionInventory.Slots - oldSize).Count(slot => !bag.Items[slot].IsAir);
        Require(misplaced == 0, $"a 100-slot save must load every item into the slot it was saved in, at its stack; {misplaced} of 100 are not");
        Require(filledPastOld == 0 && bag.Count == oldSize, $"the 20 slots past the old bag must load empty; {filledPastOld} hold an item, and the bag counts {bag.Count}");
        Console.WriteLine("bag load: a full 100-slot save loads all 100 items into their own slots of the 120-slot bag, stacks intact, and slots 101 to 120 empty");
    }

    private static CompanionGear Gear() => Main.player[0].GetModPlayer<CompanionPlayer>().Gear;

    private static Item Sample(int type) => ContentSamples.ItemsByType[type];

    /// <summary>
    /// Every item is asked of every slot. The table is written out rather than derived, because the
    /// predicate is the thing under test and a fixture that computed its expectations from it would
    /// pass whatever it did.
    /// </summary>
    private static void FiveItemsFindTheirSlots()
    {
        var bow = Sample(ItemID.WoodenBow);
        var sword = Sample(ItemID.CopperBroadsword);
        var wand = Sample(ItemID.WandofSparking);
        var pickaxe = Sample(ItemID.CopperPickaxe);
        var yoyo = Sample(ItemID.WoodYoyo);
        var pistol = Sample(ItemID.FlintlockPistol);

        // Premises about the items themselves, from their own defaults, so a changed vanilla item
        // reads as a changed premise rather than as a predicate defect.
        Require(bow.shoot > 0 && bow.useAmmo == AmmoID.Arrow, "the wooden bow shoots and uses arrows");
        Require(sword.useStyle == ItemUseStyleID.Swing && sword.shoot == 0 && sword.damage > 0, "the copper broadsword swings and shoots nothing");
        Require(wand.mana > 0 && wand.shoot > 0, "the wand of sparking costs mana and shoots");
        Require(pickaxe.pick > 0 && pickaxe.damage > 0 && pickaxe.useStyle == ItemUseStyleID.Swing,
            "the copper pickaxe has pick power, does damage and swings — the case a damage-and-swing rule alone would admit as a weapon");
        Require(ItemID.Sets.Yoyo[yoyo.type] && yoyo.shoot > 0, "the wood yoyo is in the game's yoyo set and shoots its projectile");
        Require(pistol.useAmmo == AmmoID.Bullet, "the flintlock pistol uses bullets");

        foreach (GearSlot weaponSlot in new[] { GearSlot.FirstWeapon, GearSlot.SecondWeapon })
        {
            Accepted(weaponSlot, bow);
            Accepted(weaponSlot, sword);
            Accepted(weaponSlot, wand);
            Accepted(weaponSlot, pistol);
            Refused(weaponSlot, pickaxe, "a tool goes in its tool slot");
            Refused(weaponSlot, yoyo, "yoyos are steered by the player");
            Accepted(weaponSlot, new Item());
        }
        Accepted(GearSlot.Pickaxe, pickaxe);
        Refused(GearSlot.Pickaxe, bow, "not a pickaxe");
        Refused(GearSlot.Pickaxe, yoyo, "not a pickaxe");
        Refused(GearSlot.Axe, pickaxe, "not an axe");
        Accepted(GearSlot.Axe, Sample(ItemID.CopperAxe));
        Accepted(GearSlot.Axe, new Item());

        // The default ammo the slot promises a gun or bow is the game's own first ammo of the class.
        Require(CompanionGear.DefaultAmmo(bow)?.type == ItemID.WoodenArrow, "a bow's free ammo is the wooden arrow");
        Require(CompanionGear.DefaultAmmo(pistol)?.type == ItemID.MusketBall, "a pistol's free ammo is the musket ball");
        Require(CompanionGear.DefaultAmmo(sword) == null, "a sword has no ammo class");

        // Filling the slots as the UI would, then reading what the arsenal and the tools will read.
        CompanionGear gear = Gear();
        gear.Slots[(int)GearSlot.FirstWeapon] = bow.Clone();
        gear.Slots[(int)GearSlot.SecondWeapon] = sword.Clone();
        gear.Slots[(int)GearSlot.Pickaxe] = pickaxe.Clone();
        gear.Slots[(int)GearSlot.Axe] = new Item();
        Require(gear.PickPower == pickaxe.pick, $"pick power reads the pickaxe's own power; got {gear.PickPower}, expected {pickaxe.pick}");
        Require(gear.AxePower == 0, "no axe means no axe power, so a trunk that needs any power is refused");
        gear.Slots[(int)GearSlot.Axe] = Sample(ItemID.CopperAxe).Clone();
        Require(gear.AxePower == Sample(ItemID.CopperAxe).axe, "axe power reads the axe's own power");
        int before = gear.Signature;
        gear.Slots[(int)GearSlot.SecondWeapon] = wand.Clone();
        Require(gear.Signature != before, "swapping a weapon changes the gear's signature, which is what tells the arsenal to re-enumerate");
    }

    /// <summary>
    /// Saved gear comes back item for item, and an item in the saved compound that its slot would
    /// refuse — a yoyo written by hand, standing in for an unloaded mod's placeholder — is kept in
    /// the slot on load rather than thrown away, and the arsenal is what skips it.
    /// </summary>
    private static void GearPersistsAndKeepsWhatNoLongerFits()
    {
        var gear = new CompanionGear();
        gear.Slots[0] = Sample(ItemID.WoodenBow).Clone();
        gear.Slots[1] = Sample(ItemID.CopperBroadsword).Clone();
        gear.Slots[2] = Sample(ItemID.CopperPickaxe).Clone();
        var loaded = new CompanionGear();
        loaded.Load(gear.Save());
        Require(loaded.Slots[0].type == ItemID.WoodenBow && loaded.Slots[1].type == ItemID.CopperBroadsword
            && loaded.Slots[2].type == ItemID.CopperPickaxe && loaded.Slots[3].IsAir,
            $"gear loads slot for slot; got {loaded.Slots[0].type},{loaded.Slots[1].type},{loaded.Slots[2].type},{loaded.Slots[3].type}");
        Require(loaded.Signature == gear.Signature, "a round trip through the save preserves the signature");

        var saved = gear.Save();
        // The saved bow's entry is rewritten as a yoyo, which no weapon slot accepts: the load must keep
        // it — it is the player's item — and the arsenal must not enumerate it.
        saved[GearSlot.FirstWeapon.ToString()] = Terraria.ModLoader.IO.ItemIO.Save(Sample(ItemID.WoodYoyo).Clone());
        var reloaded = new CompanionGear();
        reloaded.Load(saved);
        Require(reloaded.Slots[0].type == ItemID.WoodYoyo, $"a loaded slot keeps the item it was saved with; got {reloaded.Slots[0].type}");
        Require(!CompanionGear.Accepts(GearSlot.FirstWeapon, reloaded.Slots[0], out _), "the premise: the kept item is one the slot refuses, so the arsenal's own check is what idles it");
    }

    /// <summary>
    /// The number the slot reports is held against <c>Player.GetPickaxeDamage</c>, the private routine
    /// <c>PickTile</c> gates with: copper's power must produce no damage on obsidian, which needs 55,
    /// and gold's must produce some, while both damage plain stone. The same delegate the live miner
    /// binds is bound here, so this measures the game's gate and not a copy of it.
    /// </summary>
    private static void ToolPowerIsWhatTheGameGatesWith()
    {
        var player = Main.player[0];
        MethodInfo method = typeof(Player).GetMethod("GetPickaxeDamage", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(Player), "GetPickaxeDamage");
        int Damage(ushort tileType, int pickPower)
        {
            Tile tile = Main.tile[30, 70];
            tile.HasTile = true;
            tile.TileType = tileType;
            return (int)method.Invoke(player, new object[] { 30, 70, pickPower, 0, tile })!;
        }
        var copper = new CompanionGear();
        copper.Slots[(int)GearSlot.Pickaxe] = Sample(ItemID.CopperPickaxe).Clone();
        var gold = new CompanionGear();
        gold.Slots[(int)GearSlot.Pickaxe] = Sample(ItemID.GoldPickaxe).Clone();
        var bare = new CompanionGear();
        Require(copper.PickPower < 55 && gold.PickPower >= 55,
            $"the premise: copper ({copper.PickPower}) is under obsidian's 55 and gold ({gold.PickPower}) is not");
        Require(Damage(TileID.Obsidian, copper.PickPower) == 0, "copper's power must be refused by obsidian");
        Require(Damage(TileID.Obsidian, gold.PickPower) > 0, "gold's power must damage obsidian");
        Require(Damage(TileID.Stone, copper.PickPower) > 0, "copper's power must damage stone");
        Require(Damage(TileID.Stone, bare.PickPower) == 0, "no pickaxe is no power, and stone is refused");
        Main.tile[30, 70].ClearEverything();
    }

    private static void Accepted(GearSlot slot, Item item)
        => Require(CompanionGear.Accepts(slot, item, out string reason), $"{slot} must accept {Name(item)}; refused: {reason}");

    private static void Refused(GearSlot slot, Item item, string expectedReason)
    {
        bool accepted = CompanionGear.Accepts(slot, item, out string reason);
        Require(!accepted, $"{slot} must refuse {Name(item)}");
        Require(reason == expectedReason, $"{slot} refusing {Name(item)} must say '{expectedReason}'; said '{reason}'");
    }

    private static string Name(Item item) => item.IsAir ? "air" : $"item {item.type}";

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
