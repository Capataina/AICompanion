#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.ProfileCard;

namespace AICompanion.Companion.Inventory;

/// <summary>Right-click entry into the profile's inventory page; the profile owns its lifetime.</summary>
public sealed class CompanionBagSystem : ModSystem
{
    private const float OpenReach = 160f;
    public static bool IsOpen => CompanionProfileCardSystem.InventoryOpen;
    public static void Toggle()
    {
        if (IsOpen) CompanionProfileCardSystem.CloseOpenCard();
        else CompanionProfileCardSystem.OpenInventory();
    }

    public static bool MouseIsOnCompanionInReach(Player player)
    {
        NPC? npc = CompanionNPC.Find();
        if (npc == null) return false;
        Rectangle box = npc.Hitbox;
        box.Inflate(6, 6);
        return box.Contains(Main.MouseWorld.ToPoint()) && Vector2.Distance(player.Center, npc.Center) <= OpenReach;
    }
}
