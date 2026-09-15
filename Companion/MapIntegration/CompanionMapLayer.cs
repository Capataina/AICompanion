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
/// Draws the companion on the minimap and the full-screen map as the orb's own sprite, the way
/// the game draws players' heads, so a companion sent away or trapped can be found. No
/// teleport ever, by ruling, is what makes this necessary.
/// </summary>
public sealed class CompanionMapLayer : ModMapLayer
{
    /// <summary>The probe sprite is thirty pixels across; on the map it is drawn at head size.</summary>
    private const float MapScale = 0.8f;

    public override void Draw(ref MapOverlayDrawContext context, ref string text)
    {
        if (CompanionNPC.Instance is not CompanionNPC companion)
            return;
        NPC npc = companion.NPC;
        Vector2 tile = npc.Center / 16f;
        Vector2 screen = (tile - context.MapPosition) * context.MapScale + context.MapOffset;
        if (context.ClippingRectangle is Rectangle clip && !clip.Contains(screen.ToPoint()))
            return;

        if (TextureAssets.Npc[NPCID.Probe]?.IsLoaded == true)
        {
            var frame = new Terraria.DataStructures.SpriteFrame(1, (byte)System.Math.Max(1, Main.npcFrameCount[NPCID.Probe]));
            context.Draw(TextureAssets.Npc[NPCID.Probe].Value, tile, Color.White, frame, MapScale, MapScale, Alignment.Center);
        }
        else Main.instance.LoadNPC(NPCID.Probe);
        if (Vector2.Distance(new Vector2(Main.mouseX, Main.mouseY), screen) < 12f * context.DrawScale)
            text = "Companion";
    }
}
