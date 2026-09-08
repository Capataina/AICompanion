#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using AICompanion.Companion;

namespace AICompanion.Brain.Work;

/// <summary>
/// Draws the companion's tile cracks. The game only draws cracks from the local
/// player's HitTile, so the companion's own table is handed to the same public
/// Main.DrawTileCracks call after the tiles are drawn.
/// </summary>
public class TileCracksRenderer : ModSystem
{
    public override void PostDrawTiles()
    {
        if (CompanionNPC.Instance is not CompanionNPC companion)
            return;

        // DrawTileCracks adds Main.offScreenRange to every position unless drawToScreen, because
        // vanilla calls it inside the oversized tile render target. PostDrawTiles is screen space,
        // so that offset is taken back out through the batch transform.
        float offset = Main.drawToScreen ? 0f : -Main.offScreenRange;
        Matrix transform = Matrix.CreateTranslation(offset, offset, 0f) * Main.GameViewMatrix.TransformationMatrix;
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
            DepthStencilState.None, Main.Rasterizer, null, transform);
        Main.instance.DrawTileCracks(1, companion.Chopper.HitTile);
        Main.instance.DrawTileCracks(1, companion.Miner.HitTile);
        Main.spriteBatch.End();
    }
}
