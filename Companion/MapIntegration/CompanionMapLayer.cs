#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.UI;
using AICompanion.Companion.CharacterBody;

namespace AICompanion.Companion.MapIntegration;

/// <summary>
/// Draws the companion on the minimap and the full-screen map as its own head, the way
/// the game draws players, so a companion sent away or trapped can be found. No
/// teleport ever, by ruling, is what makes this necessary.
/// </summary>
public sealed class CompanionMapLayer : ModMapLayer
{
    /// <summary>Set the first time the head renderer throws; logged once, Guide head from then on.</summary>
    private static bool headRendererFailed;

    public override void Unload() => headRendererFailed = false;

    public override void Draw(ref MapOverlayDrawContext context, ref string text)
    {
        if (CompanionNPC.Instance is not CompanionNPC companion)
            return;
        NPC npc = companion.NPC;
        Vector2 tile = npc.Center / 16f;
        Vector2 screen = (tile - context.MapPosition) * context.MapScale + context.MapOffset;
        if (context.ClippingRectangle is Rectangle clip && !clip.Contains(screen.ToPoint()))
            return;

        bool drawn = false;
        if (companion.Body.UsesPlayerRenderer && !headRendererFailed)
        {
            try
            {
                Main.MapPlayerRenderer.DrawPlayerHead(Main.Camera, companion.Body.Player, screen, 1f, context.DrawScale, Color.White);
                drawn = true;
            }
            catch (System.Exception e)
            {
                headRendererFailed = true;
                Mod.Logger.Error("Map head renderer refused the companion body; falling back to the Guide head.", e);
            }
        }
        if (!drawn)
        {
            int head = NPC.TypeToDefaultHeadIndex(NPCID.Guide);
            context.Draw(TextureAssets.NpcHead[head].Value, tile, Alignment.Center);
        }
        if (Vector2.Distance(new Vector2(Main.mouseX, Main.mouseY), screen) < 12f * context.DrawScale)
            text = "Companion";
    }
}
