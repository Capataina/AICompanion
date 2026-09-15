#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
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
