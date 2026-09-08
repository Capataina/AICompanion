#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AICompanion.Companion;

/// <summary>
/// Draws the companion as the game's own female starter body, and stands in for it
/// wherever the game only understands players. A <see cref="Player"/> instance is kept
/// in step with the NPC each tick and handed to the game's player renderer; the held
/// item follows the vanilla use-style pose while an animation runs. Its slot is the
/// highest one the map head renderer's per-player table accepts, one below the player
/// array's spare last slot, so no draw layer mistakes it for the local player and the
/// head renderer does not throw on it.
///
/// It also sits in that slot of <c>Main.player</c>, inactive, and is switched active
/// only for the span in which a hostile's AI runs (<see cref="CompanionAggro"/>). Every
/// enemy picks its target by one loop over the player slots that skips inactive, dead
/// and ghost players, so inside that span the companion is a second player to hunt and
/// a reason for a boss to stay, and outside it the slot is invisible to the player
/// update, the spawn waves, projectile-versus-player checks and every other reader of
/// the array. Downed mirrors to <c>dead</c>, so a boss leaves when the player is dead
/// and the companion is down, which is the rule Caner set on 2026-09-08.
///
/// If the renderer ever throws, the failure is logged once and drawing falls back
/// to the Guide sprite for the rest of the session; by decision, a companion that
/// looks like the Guide beats a companion that crashes the draw loop.
/// </summary>
public class CompanionBody
{
    private readonly Player body;
    private bool rendererFailed;

    /// <summary>The body currently placed in the player array, for the aggro window; null when none is.</summary>
    public static Player? Placed { get; private set; }

    public bool UsesPlayerRenderer => !rendererFailed;

    /// <summary>The stand-in player, for the map head renderer and the aggro window.</summary>
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
        body.active = false;
        body.dead = true;
        Main.player[body.whoAmI] = body;
        Placed = body;
    }

    /// <summary>Make the placed body visible (or not) to whatever reads the player array right now.</summary>
    public static void Expose(bool visible)
    {
        if (Placed != null)
            Placed.active = visible;
    }

    /// <summary>Put an ordinary empty player back in the slot; called on mod unload.</summary>
    public static void Withdraw()
    {
        if (Placed != null)
            Main.player[Placed.whoAmI] = new Player();
        Placed = null;
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
    public void Sync(NPC npc, Player player, int heldItemType, int itemAnimation, int itemAnimationMax, float itemRotation, bool downed)
    {
        // What enemy AI reads is mirrored whether or not the renderer works: where the body
        // is, how it moves, and whether it counts as alive.
        body.position = npc.Bottom - new Vector2(body.width / 2f, body.height);
        body.velocity = npc.velocity;
        body.dead = downed;
        body.ghost = false;
        body.statLifeMax2 = body.statLifeMax = npc.lifeMax;
        body.statLife = npc.life;
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
        body.direction = npc.direction == 0 ? 1 : npc.direction;
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
        if (heldItemType > 0)
        {
            Item item = body.inventory[0];
            Rectangle frame = Item.GetDrawHitbox(item.type, body);
            if (itemAnimation > 0)
                body.ItemCheck_ApplyUseStyle(0f, item, frame);
            else if (item.holdStyle != 0)
                ApplyHoldStyle?.Invoke(body, new object[] { 0f, item, frame });
        }
    }

    // The renderer draws a held item at itemLocation, which the game's ItemCheck sets every tick
    // through the use style while a swing plays and the hold style otherwise. The body never runs
    // ItemCheck, and only the use style is public: a torch (hold style 1) drawn without the hold
    // style sits at whatever itemLocation last was, floating beside the body.
    private static readonly System.Reflection.MethodInfo? ApplyHoldStyle = typeof(Player).GetMethod(
        "ItemCheck_ApplyHoldStyle",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
        null, new[] { typeof(float), typeof(Item), typeof(Rectangle) }, null);

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
