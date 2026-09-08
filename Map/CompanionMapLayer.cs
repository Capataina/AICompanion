#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.UI;
using AICompanion.Companion;

namespace AICompanion.Map;

/// <summary>
/// Draws the companion on the minimap and the full-screen map as its own head, the way
/// the game draws players, so a companion sent away or trapped can be found. No
/// teleport ever, by ruling, is what makes this necessary.
/// </summary>
public sealed class CompanionMapLayer : ModMapLayer
{
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
        if (companion.Body.UsesPlayerRenderer)
        {
            try
            {
                Main.MapPlayerRenderer.DrawPlayerHead(Main.Camera, companion.Body.Player, screen, 1f, context.DrawScale, Color.White);
                drawn = true;
            }
            catch
            {
                drawn = false;
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
