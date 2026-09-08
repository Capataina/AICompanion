#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using AICompanion.Companion;

namespace AICompanion.Inventory;

/// <summary>
/// Owns the bag window: opens on a right-click on the companion within reach, closes
/// on a second right-click, on the inventory key, or when the player walks away.
/// </summary>
public sealed class CompanionBagSystem : ModSystem
{
    private const float OpenReach = 160f;
    private const float CloseDistance = 320f;

    private UserInterface? ui;
    private CompanionBagUI? state;
    private GameTime? lastTime;

    public static bool IsOpen { get; private set; }

    public override void Load()
    {
        ui = new UserInterface();
        Mod.Logger.Info("CompanionBagSystem.Load: done");
    }

    public override void Unload()
    {
        ui?.SetState(null);
        ui = null;
        state = null;
        lastTime = null;
        IsOpen = false;
        Mod.Logger.Info("CompanionBagSystem.Unload: done");
    }

    public static void Toggle()
    {
        var system = ModContent.GetInstance<CompanionBagSystem>();
        if (IsOpen)
            system.Close();
        else
            system.Open();
    }

    private void Open()
    {
        state = new CompanionBagUI();
        state.Activate();
        ui?.SetState(state);
        IsOpen = true;
        // The bag is a container, and in this game a container only works while the player is
        // in item-management mode: with Main.playerInventory false, Player.dropItemCheck throws
        // whatever is on the cursor every tick, so nothing could ever be put into the bag.
        // Every vanilla chest opens the inventory for the same reason.
        Main.playerInventory = true;
    }

    private void Close()
    {
        ui?.SetState(null);
        state = null;
        IsOpen = false;
    }

    /// <summary>True when the mouse is over the companion and the player is close enough to reach it.</summary>
    public static bool MouseIsOnCompanionInReach(Player player)
    {
        NPC? npc = CompanionNPC.Find();
        if (npc == null)
            return false;
        Rectangle box = npc.Hitbox;
        box.Inflate(6, 6);
        return box.Contains(Main.MouseWorld.ToPoint()) && Vector2.Distance(player.Center, npc.Center) <= OpenReach;
    }

    public override void UpdateUI(GameTime gameTime)
    {
        lastTime = gameTime;
        if (!IsOpen)
            return;
        NPC? npc = CompanionNPC.Find();
        if (npc == null || Vector2.Distance(Main.LocalPlayer.Center, npc.Center) > CloseDistance || !Main.playerInventory)
        {
            Close();
            return;
        }
        ui?.Update(gameTime);
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
        if (index < 0)
            index = layers.Count;
        layers.Insert(index, new LegacyGameInterfaceLayer("AICompanion: Companion Bag", () =>
        {
            if (IsOpen && ui != null && lastTime != null)
                ui.Draw(Main.spriteBatch, lastTime);
            return true;
        }, InterfaceScaleType.UI));
    }
}
