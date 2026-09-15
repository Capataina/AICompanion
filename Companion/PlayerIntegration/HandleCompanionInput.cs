#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.Inventory;

namespace AICompanion.Companion.PlayerIntegration;

/// <summary>Companion input runs before item use so opening the bag consumes the click.</summary>
public partial class CompanionPlayer
{
    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        bool toggle = BrainOverlay.ToggleKey?.JustPressed == true;
        if (toggle && DiagnosticsConfiguration.CompanionDiagnosticsConfig.Current.EnableBrainInspector)
        {
            BrainOverlay.ToggleMenu();
        }
    }

    /// <summary>
    /// The card closes on the game's Inventory trigger, Escape unless the player rebound it, and the press is spent on that.
    /// This is the one place both halves can happen on the same tick: <c>Player.Update</c> reads the tick's controls, calls
    /// this hook, and then its own gate calls <c>ToggleInv</c> on a fresh press (Player.cs 23942-23954). Clearing
    /// <c>releaseInventory</c> is what the gate reads as "already handled"; clearing <c>controlInv</c> instead would re-arm
    /// the gate and toggle the inventory on the next held tick. The card used to close on a raw Escape in
    /// <c>UpdateUI</c>, which runs before the tick's keyboard is sampled, so the same press reached <c>ToggleInv</c> a tick
    /// before the card saw it and one Escape did two things.
    /// </summary>
    public override void SetControls()
    {
        if (!ProfileCard.CompanionProfileCardSystem.IsOpen || !Player.controlInv || !Player.releaseInventory) return;
        ProfileCard.CompanionProfileCardSystem.CloseOpenCard();
        Player.releaseInventory = false;
    }

    /// <summary>
    /// A shift-click while the card's Inventory page is open moves an item between the player and the companion, and never
    /// falls through to the game's trash. The page looks like a chest, but the game gives a shift-click a destination only
    /// while a real chest is open (<c>player.chest != -1</c>): with none, a player slot's shift-click picks the item up under
    /// the default settings and trashes it under the legacy shift-click-trash setting (<c>ItemSlot.LeftClick_SellOrTrash</c>),
    /// and a bag or gear slot the player has no room for is trashed the same way, a second trash destroying the first.
    /// <list type="bullet">
    /// <item>From the player's inventory, the slot is deposited by Deposit All's rule; coins and favourites stay.</item>
    /// <item>From a bag or gear slot, the item goes to the player through the game's own insertion, the move a chest slot's
    /// shift-click makes, and what does not fit stays.</item>
    /// </list>
    /// Either way the click is claimed, so a click that moves nothing does nothing, and nothing moves while an item is on the
    /// cursor. A bag slot's item is identified by reference, because the page hands the game its slot as a one-item array.
    /// The bag is not sorted after a take-out here: the game writes the one-item array back into the slot it came from once
    /// this returns, and a sort in between would put that write into a slot another item now occupies.
    /// </summary>
    public override bool ShiftClickSlot(Item[] inventory, int context, int slot)
    {
        if (!ProfileCard.CompanionProfileCardSystem.InventoryOpen) return false;
        if (inventory == Player.inventory)
        {
            if (context is not (ItemSlot.Context.InventoryItem or ItemSlot.Context.InventoryCoin or ItemSlot.Context.InventoryAmmo)) return false;
            if (Main.mouseItem.IsAir) Bag.DepositSlot(Player, slot);
            return true;
        }
        if (context != ItemSlot.Context.BankItem || inventory.Length != 1 || !(HoldsByReference(Bag.Items, inventory[0]) || HoldsByReference(Gear.Slots, inventory[0])))
            return false;
        if (Main.mouseItem.IsAir && !inventory[0].IsAir)
            inventory[0] = Player.GetItem(Player.whoAmI, inventory[0], GetItemSettings.InventoryEntityToPlayerInventorySettings);
        return true;
    }

    private static bool HoldsByReference(Item[] items, Item item)
    {
        foreach (Item held in items)
            if (ReferenceEquals(held, item)) return true;
        return false;
    }

    public override void PreUpdate()
    {
        BrainOverlay.CaptureInput();
        HeadsUpDisplay.CompanionHealthBar.CaptureInput(this);
        if (ProfileCard.CompanionProfileCardSystem.IsOpen) Player.mouseInterface = true;
        // Right-click on the companion, within reach, opens or closes the bag. This runs before
        // the player's item use for the tick, so setting mouseInterface here is what stops the
        // held item firing on the same click; in PostUpdate the item would already have been used.
        if (Main.mouseRight && Main.mouseRightRelease && !Player.mouseInterface && CompanionBagSystem.MouseIsOnCompanionInReach(Player))
        {
            CompanionBagSystem.Toggle();
            Player.mouseInterface = true;
        }
    }
}
