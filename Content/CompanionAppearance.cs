#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AICompanion.Content;

/// <summary>
/// Draws the companion as the game's own female starter body. A drawing-only
/// <see cref="Player"/> instance (never placed in Main.player, whoAmI set to the
/// unused last slot so no draw layer mistakes it for the local player) is kept in
/// step with the NPC each tick and handed to the game's player renderer. The held
/// item follows the vanilla use-style pose while an animation runs.
///
/// If the renderer ever throws, the failure is logged once and drawing falls back
/// to the Guide sprite for the rest of the session; by decision, a companion that
/// looks like the Guide beats a companion that crashes the draw loop.
/// </summary>
public class CompanionAppearance
{
    private readonly Player body;
    private bool rendererFailed;

    public bool UsesPlayerRenderer => !rendererFailed;

    public CompanionAppearance()
    {
        body = new Player
        {
            whoAmI = Main.maxPlayers,
            skinVariant = PlayerVariantID.FemaleStarter,
            hair = 2,
            width = 20,
            height = 42,
            gravDir = 1f,
            direction = 1,
        };
        body.selectedItem = 0;
    }

    /// <summary>Copy the NPC's motion into the body and run the game's own frame logic.</summary>
    public void Sync(NPC npc, int heldItemType, int itemAnimation, int itemAnimationMax, float itemRotation)
    {
        body.position = npc.Bottom - new Vector2(body.width / 2f, body.height);
        body.velocity = npc.velocity;
        body.direction = npc.direction == 0 ? 1 : npc.direction;
        body.dead = false;
        body.wet = npc.wet;
        body.gravDir = 1f;

        if (body.inventory[0].type != heldItemType)
        {
            body.inventory[0].SetDefaults(heldItemType);
            if (heldItemType > 0)
                Main.instance.LoadItem(heldItemType);
        }
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
            rendererFailed = true;
            ModContent.GetInstance<AICompanion>().Logger.Error("Player renderer refused the companion body; falling back to the Guide sprite.", e);
        }
    }
}
