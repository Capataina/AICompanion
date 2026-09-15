#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace AICompanion.Companion.ProfileCard;

/// <summary>
/// Native panel assets, one hard-shadow text treatment, and the rounded shapes the game's interface has
/// no primitive for, shared by the card and the HUD notch so there is one texture cache and one release.
/// </summary>
public static class DrawCardPrimitives
{
    public static readonly Color Panel = new Color(43, 48, 153) * .86f;
    public static readonly Color Edge = new(143, 147, 240);
    public static readonly Color Muted = new(195, 198, 245);
    public static readonly Color Selected = new(86, 91, 194);

    public static void Fill(SpriteBatch sb, Rectangle r, Color color)
        => sb.Draw(TextureAssets.MagicPixel.Value, r, new Rectangle(0, 0, 1, 1), color);

    /// <summary>A straight line of the given thickness, from the atlas's one-pixel source, never the whole texture.</summary>
    public static void Line(SpriteBatch sb, Vector2 a, Vector2 b, Color color, float thickness = 1.3f)
    {
        Vector2 d = b - a;
        sb.Draw(TextureAssets.MagicPixel.Value, a, new Rectangle(0, 0, 1, 1), color, d.ToRotation(), new Vector2(0, .5f), new Vector2(d.Length(), thickness), SpriteEffects.None, 0);
    }

    public static void Text(SpriteBatch sb, string text, Vector2 position, Color color, float scale = .8f, DynamicSpriteFont? font = null)
    {
        font ??= FontAssets.MouseText.Value;
        sb.DrawString(font, text, position + new Vector2(2, 2), Color.Black, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        sb.DrawString(font, text, position, color, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }

    public static void WrappedText(SpriteBatch sb, string text, Rectangle bounds, Color color, float scale = .75f)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var font = FontAssets.MouseText.Value;
        string wrapped = font.CreateWrappedText(text, Math.Max(1, bounds.Width / scale));
        string[] lines = wrapped.Split('\n');
        float lineHeight = (font.MeasureString("A\nA").Y - font.MeasureString("A").Y) * scale;
        int limit = Math.Max(1, (int)(bounds.Height / lineHeight));
        if (lines.Length > limit)
        {
            string last = lines[limit - 1].TrimEnd();
            while (last.Length > 0 && font.MeasureString(last + "...").X * scale > bounds.Width) last = last[..^1];
            lines[limit - 1] = last + "...";
            wrapped = string.Join('\n', lines, 0, limit);
        }
        Text(sb, wrapped, bounds.TopLeft(), color, scale);
    }

    public static UITextPanel<string> Button(string text, float width, Action click)
    {
        var button = new CardButton(text);
        button.Width.Set(width, 0); button.Height.Set(30, 0); button.SetPadding(5);
        button.BackgroundColor = Panel; button.BorderColor = Edge;
        button.OnLeftClick += (_, _) => click();
        return button;
    }

    private sealed class CardButton(string text) : UITextPanel<string>(text, .8f)
    {
        protected override void DrawSelf(SpriteBatch sb)
        {
            // UITextPanel draws its native nine-slice panel, but text uses the same
            // down-right shadow as the graph and status rail, without a blurred outline.
            Color ink = TextColor;
            TextColor = Color.Transparent;
            base.DrawSelf(sb);
            TextColor = ink;
            Rectangle r = GetDimensions().ToRectangle();
            if (IsMouseHovering)
                Fill(sb, new Rectangle(r.X + 7, r.Bottom - 3, r.Width - 14, 1), Color.Gold);
            if (Text is "+" or "-")
            {
                // The game font at the card's text scale drops the vertical stroke of "+", so at UI scale 1 zoom in and
                // zoom out drew the same dash. Both are drawn as bars instead, with the text's own down-right shadow.
                const int arm = 6, stroke = 2;
                Point c = r.Center;
                foreach (var (offset, colour) in new[] { (2, Color.Black), (0, ink) })
                {
                    Fill(sb, new Rectangle(c.X - arm + offset, c.Y - stroke / 2 + offset, 2 * arm, stroke), colour);
                    if (Text == "+") Fill(sb, new Rectangle(c.X - stroke / 2 + offset, c.Y - arm + offset, stroke, 2 * arm), colour);
                }
                return;
            }
            Vector2 size = FontAssets.MouseText.Value.MeasureString(Text) * TextScale;
            DrawCardPrimitives.Text(sb, Text, new Vector2(r.Center.X - size.X / 2, r.Center.Y - size.Y / 2), ink, TextScale);
        }
    }

    // ---- rounded shapes: runtime-built white alpha masks, tinted when drawn and cached per pixel size ----

    /// <summary>Bits for <see cref="RoundedMask"/>'s corners: 1 top-left, 2 top-right, 4 bottom-left, 8 bottom-right.</summary>
    public const int AllCorners = 0b1111, BottomCorners = 0b1100, LeftCorners = 0b0101, RightCorners = 0b1010;

    private static readonly Dictionary<(int w, int h, int r, int corners), Texture2D> masks = new();

    /// <summary>A rounded rectangle filled with a colour; a radius larger than half the short side is clamped to it.</summary>
    public static void RoundedFill(SpriteBatch sb, Rectangle r, int radius, int corners, Color color)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        radius = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
        sb.Draw(RoundedMask(sb, r.Width, r.Height, radius, corners), r, color);
    }

    /// <summary>A white alpha mask of a rectangle with the chosen corners rounded and a one-pixel anti-aliased edge.</summary>
    public static Texture2D RoundedMask(SpriteBatch sb, int w, int h, int r, int corners)
    {
        var key = (w, h, r, corners);
        if (masks.TryGetValue(key, out Texture2D? cached))
            return cached;
        var data = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float a = 1f;
                (int cx, int cy)? corner = null;
                if (x < r && y < r && (corners & 1) != 0) corner = (r, r);
                else if (x >= w - r && y < r && (corners & 2) != 0) corner = (w - r - 1, r);
                else if (x < r && y >= h - r && (corners & 4) != 0) corner = (r, h - r - 1);
                else if (x >= w - r && y >= h - r && (corners & 8) != 0) corner = (w - r - 1, h - r - 1);
                if (corner is (int cx, int cy))
                {
                    float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    a = MathHelper.Clamp(r - d + 0.5f, 0f, 1f);
                }
                data[y * w + x] = Color.White * a;
            }
        }
        var tex = new Texture2D(sb.GraphicsDevice, w, h);
        tex.SetData(data);
        masks[key] = tex;
        return tex;
    }

    /// <summary>
    /// A square of side <paramref name="s"/> with a circle of <paramref name="radius"/> about its outer bottom
    /// corner removed, so it fills the concave gap beside a notch's top corner. A radius larger than the side
    /// leaves a thinner sliver about the same centre, which is how a border fillet and a body fillet nest.
    /// </summary>
    public static Texture2D FilletMask(SpriteBatch sb, int s, int radius, bool flipX)
    {
        var key = (s, radius, -1, flipX ? 1 : 0);
        if (masks.TryGetValue(key, out Texture2D? cached))
            return cached;
        var data = new Color[s * s];
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float cx = flipX ? s : 0f, cy = s;
                float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                data[y * s + x] = Color.White * MathHelper.Clamp(d - radius + 0.5f, 0f, 1f);
            }
        }
        var tex = new Texture2D(sb.GraphicsDevice, s, s);
        tex.SetData(data);
        masks[key] = tex;
        return tex;
    }

    /// <summary>
    /// Release every cached mask. Mod unload runs on a worker thread and FNA3D refuses to release a texture
    /// anywhere but the main thread (a ThreadStateException, after which tModLoader reports the mod unable to
    /// unload), so the textures are handed to the game's main-thread queue.
    /// </summary>
    public static void ReleaseMasks()
    {
        var toDispose = new List<Texture2D>(masks.Values);
        masks.Clear();
        Main.QueueMainThreadAction(() =>
        {
            foreach (Texture2D t in toDispose)
                t.Dispose();
        });
    }
}
