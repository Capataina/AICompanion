#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using AICompanion.Companion.Brain.BehaviourDiagnostics;
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
