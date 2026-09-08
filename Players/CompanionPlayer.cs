#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
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
        bool spawned = false;
        if (HasCompanion && CompanionNPC.Find() == null)
            spawned = CompanionNPC.Spawn(Player) < Main.maxNPCs;
        Mod.Logger.Info($"OnEnterWorld: hasCompanion={HasCompanion} spawned={spawned} bagItems={Bag.Count}");
    }

    private KeyboardState previousKeys;

    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        // The keybind is the proper path; the raw keys are the fallback for the key left of 1,
        // which SDL reports as either the grave or the ISO-section scancode depending on the
        // keyboard, and the log line names any Oem key the moment it is pressed so the right one
        // can be read off client.log rather than guessed.
        KeyboardState keys = Main.keyState;
        bool JustDown(Keys k) => keys.IsKeyDown(k) && !previousKeys.IsKeyDown(k);
        bool toggle = BrainOverlay.ToggleKey?.JustPressed == true || JustDown(Keys.F6) || JustDown(Keys.OemTilde) || JustDown(Keys.OemBackslash);
        foreach (Keys k in keys.GetPressedKeys())
            if (!previousKeys.IsKeyDown(k) && k.ToString().StartsWith("Oem"))
                Mod.Logger.Info($"Key pressed: {k}");
        previousKeys = keys;

        if (toggle)
        {
            BrainOverlay.Enabled = !BrainOverlay.Enabled;
            Mod.Logger.Info($"BrainOverlay toggled {(BrainOverlay.Enabled ? "on" : "off")}");
        }
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
