#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;

namespace AICompanion.Companion.ProfileCard;

/// <summary>Draws the native inventory while shielding its pointer input under the card.</summary>
public sealed class BlockCoveredInventoryInput(GameInterfaceLayer inventory, Func<bool> coversPointer)
    : GameInterfaceLayer(inventory.Name, inventory.ScaleType)
{
    protected override bool DrawSelf()
    {
        bool covered = coversPointer();
        Point pointer = default;
        // GameInterfaceLayer.Draw owns a Begin/End pair. End our wrapper's batch
        // before invoking that real layer, then restore it for our caller's End.
        Main.spriteBatch.End();
        if (covered)
        {
            // Each layer reapplies zoom from cached raw coordinates. Moving only
            // Main.mouseX/Y is undone before the underlying inventory can see it.
            PlayerInput.SetZoom_Unscaled();
            pointer = new Point(Main.mouseX, Main.mouseY);
            Main.mouseX = Main.mouseY = -10000;
            PlayerInput.CacheMousePositionForZoom();
        }
        try { return inventory.Draw(); }
        finally
        {
            if (covered)
            {
                Main.mouseX = pointer.X; Main.mouseY = pointer.Y;
                PlayerInput.CacheMousePositionForZoom();
            }
            PlayerInput.SetZoom_UI();
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
        }
    }
}
