#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AICompanion.Companion;

/// <summary>
/// Draws the companion as the game's own female starter body. A drawing-only
/// <see cref="Player"/> instance (never placed in Main.player; whoAmI is the highest
/// slot the map head renderer's per-player table accepts, which is one below the
/// player array's spare last slot, so no draw layer mistakes it for the local player
/// and the head renderer does not throw on it) is kept in
/// step with the NPC each tick and handed to the game's player renderer. The held
/// item follows the vanilla use-style pose while an animation runs.
///
/// If the renderer ever throws, the failure is logged once and drawing falls back
/// to the Guide sprite for the rest of the session; by decision, a companion that
/// looks like the Guide beats a companion that crashes the draw loop.
/// </summary>
public class CompanionBody
{
    private readonly Player body;
    private bool rendererFailed;

    public bool UsesPlayerRenderer => !rendererFailed;

    /// <summary>The drawing-only player, for the map head renderer.</summary>
    public Player Player => body;

    public CompanionBody()
    {
        body = new Player
        {
            whoAmI = Main.maxPlayers - 1,
            width = 20,
            height = 42,
            gravDir = 1f,
            direction = 1,
        };
        body.selectedItem = 0;
    }

    /// <summary>
    /// The companion is the female version of the player's own character: same hair,
    /// colours and body variant, with a male variant swapped to its female counterpart
    /// through the game's own gender table.
    /// </summary>
    private void CopyLook(Player player)
    {
        body.hair = player.hair;
        body.hairColor = player.hairColor;
        body.skinColor = player.skinColor;
        body.eyeColor = player.eyeColor;
        body.shirtColor = player.shirtColor;
        body.underShirtColor = player.underShirtColor;
        body.pantsColor = player.pantsColor;
        body.shoeColor = player.shoeColor;
        body.skinVariant = PlayerVariantID.Sets.Male[player.skinVariant]
            ? PlayerVariantID.Sets.AltGenderReference[player.skinVariant]
            : player.skinVariant;
    }

    /// <summary>Copy the NPC's motion into the body and run the game's own frame logic.</summary>
    public void Sync(NPC npc, Player player, int heldItemType, int itemAnimation, int itemAnimationMax, float itemRotation)
    {
        if (rendererFailed)
            return;
        try
        {
            SyncBody(npc, player, heldItemType, itemAnimation, itemAnimationMax, itemRotation);
        }
        catch (Exception e)
        {
            Fail("Player frame logic refused the companion body; falling back to the Guide sprite.", e);
        }
    }

    private void SyncBody(NPC npc, Player player, int heldItemType, int itemAnimation, int itemAnimationMax, float itemRotation)
    {
        CopyLook(player);
        body.position = npc.Bottom - new Vector2(body.width / 2f, body.height);
        body.velocity = npc.velocity;
        body.direction = npc.direction == 0 ? 1 : npc.direction;
        body.dead = false;
        body.wet = npc.wet;
        body.gravDir = 1f;

        if (body.inventory[0].type != heldItemType)
            body.inventory[0].SetDefaults(heldItemType);
        // The held-item draw layer reads lastVisualizedSelectedItem, which only the
        // game's own player update sets; the body never runs that update.
        body.lastVisualizedSelectedItem = body.inventory[0];
        body.itemAnimation = itemAnimation;
        body.itemAnimationMax = itemAnimationMax;
        body.itemRotation = itemRotation;

        body.PlayerFrame();
        if (itemAnimation > 0 && heldItemType > 0)
        {
            Item item = body.inventory[0];
            body.ItemCheck_ApplyUseStyle(0f, item, Item.GetDrawHitbox(item.type, body));
        }
    }

    private void Fail(string message, Exception e)
    {
        rendererFailed = true;
        ModContent.GetInstance<AICompanion>().Logger.Error(message, e);
    }

    /// <summary>Draw the body at the NPC's position; <paramref name="rotation"/> lays it down while downed.</summary>
    public void Draw(float rotation)
    {
        if (rendererFailed)
            return;
        try
        {
            Main.PlayerRenderer.DrawPlayer(Main.Camera, body, body.position, rotation, body.Size / 2f);
        }
        catch (Exception e)
        {
            Fail("Player renderer refused the companion body; falling back to the Guide sprite.", e);
        }
    }
}
