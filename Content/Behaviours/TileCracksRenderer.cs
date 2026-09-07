#nullable enable

using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace AICompanion.Content.Behaviours;

/// <summary>
/// Draws the companion's tile cracks. The game only draws cracks from the local
/// player's HitTile, so the companion's own table is handed to the same public
/// Main.DrawTileCracks call after the tiles are drawn.
/// </summary>
public class TileCracksRenderer : ModSystem
{
    public override void PostDrawTiles()
    {
        if (Companion.Find()?.ModNPC is not Companion companion)
            return;

        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
            DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
        Main.instance.DrawTileCracks(1, companion.Chopper.HitTile);
        Main.spriteBatch.End();
    }
}
