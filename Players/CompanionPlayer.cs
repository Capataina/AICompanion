#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using AICompanion.Brain.Debug;
using AICompanion.Companion;
using AICompanion.Inventory;

namespace AICompanion.Players;

/// <summary>
/// Per-character state that has to outlive a session: whether this character has
/// summoned a companion (so it spawns on every world enter without the command),
/// where the health bar was dragged to, and the companion's bag. Also the place
/// player input about the companion is read: the right-click that opens the bag and
/// the overlay key. Singleplayer only: the one instance that matters is Main.LocalPlayer's.
/// </summary>
public class CompanionPlayer : ModPlayer
{
    public bool HasCompanion;

    /// <summary>Health bar position in screen pixels, or null for the default top-centre.</summary>
    public Vector2? HealthBarPosition;

    public CompanionInventory Bag { get; private set; } = new();

    public override void SaveData(TagCompound tag)
    {
        tag["hasCompanion"] = HasCompanion;
        if (HealthBarPosition is Vector2 p)
            tag["healthBar"] = p;
        tag["bag"] = Bag.Save();
    }

    public override void LoadData(TagCompound tag)
    {
        HasCompanion = tag.GetBool("hasCompanion");
        HealthBarPosition = tag.ContainsKey("healthBar") ? tag.Get<Vector2>("healthBar") : null;
        Bag = new CompanionInventory();
        if (tag.ContainsKey("bag"))
            Bag.Load(tag.GetCompound("bag"));
    }

    public override void OnEnterWorld()
    {
        if (HasCompanion && CompanionNPC.Find() == null)
            CompanionNPC.Spawn(Player);
    }

    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        if (BrainOverlay.ToggleKey?.JustPressed == true)
            BrainOverlay.Enabled = !BrainOverlay.Enabled;
    }

    public override void PreUpdate()
    {
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
