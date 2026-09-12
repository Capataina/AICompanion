using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;

namespace AICompanion.Companion.HeadsUpDisplay;

/// <summary>Authored single-colour symbols in a common square. Geometry is independent of
/// item art, equipment and fonts, so changing a tool cannot change the activity's identity.</summary>
public static class DrawPurposeSymbol
{
    public static void Draw(SpriteBatch batch, Rectangle slot, HudSymbol symbol, Color colour)
    {
        float scale = slot.Width / 20f;
        Vector2 At(float x, float y) => new(slot.X + x * scale, slot.Y + y * scale);
        void Line(float x, float y, float endX, float endY)
        {
            Vector2 from = At(x, y), delta = At(endX, endY) - from;
            batch.Draw(TextureAssets.MagicPixel.Value, from, new Rectangle(0, 0, 1, 1), colour,
                MathF.Atan2(delta.Y, delta.X), new Vector2(0, .5f),
                new Vector2(delta.Length(), Math.Max(1, 1.5f * scale)), SpriteEffects.None, 0);
        }
        void Ring(float x, float y, float radius)
        {
            for (int i = 0; i < 20; i++)
            {
                float a = i * MathF.Tau / 20, b = (i + 1) * MathF.Tau / 20;
                Line(x + MathF.Cos(a) * radius, y + MathF.Sin(a) * radius,
                    x + MathF.Cos(b) * radius, y + MathF.Sin(b) * radius);
            }
        }
        switch (symbol)
        {
            case HudSymbol.Gathering:
                Line(3, 7, 17, 7); Line(3, 7, 5, 17); Line(5, 17, 15, 17); Line(15, 17, 17, 7);
                Line(6, 7, 8, 3); Line(8, 3, 12, 3); Line(12, 3, 14, 7); Line(8, 10, 8, 14); Line(12, 10, 12, 14);
                break;
            case HudSymbol.Combat:
                Line(3, 17, 16, 4); Line(16, 4, 12, 5); Line(16, 4, 15, 8); Line(4, 12, 8, 16);
                Line(17, 17, 4, 4); Line(4, 4, 8, 5); Line(4, 4, 5, 8); Line(12, 16, 16, 12);
                break;
            case HudSymbol.Assistance:
                Line(10, 2, 12, 8); Line(12, 8, 18, 10); Line(18, 10, 12, 12); Line(12, 12, 10, 18);
                Line(10, 18, 8, 12); Line(8, 12, 2, 10); Line(2, 10, 8, 8); Line(8, 8, 10, 2);
                break;
            case HudSymbol.Mining:
                Line(4, 17, 13, 6); Line(4, 7, 9, 4); Line(9, 4, 14, 5); Line(14, 5, 17, 10);
                break;
            case HudSymbol.Chopping:
                Line(6, 18, 12, 3); Line(12, 4, 17, 5); Line(17, 5, 15, 11); Line(15, 11, 9, 9);
                break;
            case HudSymbol.Guarding:
                Line(3, 4, 10, 2); Line(10, 2, 17, 4); Line(17, 4, 16, 12);
                Line(16, 12, 10, 18); Line(10, 18, 4, 12); Line(4, 12, 3, 4); Line(10, 5, 10, 14);
                break;
            case HudSymbol.Hunting:
                Ring(10, 10, 6); Line(10, 1, 10, 6); Line(10, 14, 10, 19);
                Line(1, 10, 6, 10); Line(14, 10, 19, 10);
                break;
            case HudSymbol.Lighting:
                Line(8, 17, 12, 17); Line(8, 17, 8, 11); Line(12, 17, 12, 11);
                Line(7, 11, 13, 11); Line(7, 11, 5, 7); Line(5, 7, 10, 2);
                Line(10, 2, 10, 6); Line(10, 6, 13, 4); Line(13, 4, 15, 8); Line(15, 8, 13, 11);
                break;
            case HudSymbol.Collecting:
                Ring(10, 10, 7); Line(10, 5, 10, 15); Line(7, 8, 10, 5); Line(10, 15, 13, 12);
                break;
            case HudSymbol.Company:
                Ring(6, 6, 2.5f); Ring(14, 6, 2.5f);
                Line(2, 17, 3, 12); Line(3, 12, 9, 12); Line(9, 12, 10, 17);
                Line(10, 17, 11, 12); Line(11, 12, 17, 12); Line(17, 12, 18, 17);
                break;
            default: Line(6, 10, 14, 10); break;
        }
    }
}
