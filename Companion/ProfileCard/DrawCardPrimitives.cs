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

    /// <summary>
    /// Glyphs handed to the game's font by <see cref="Text"/> since the process started, shadow and ink both counted and line
    /// breaks not, because the font skips those before drawing. The font boxes two enums for every glyph it draws
    /// (<c>ReLogic.Graphics.DynamicSpriteFont.InternalDraw</c> calls <c>Enum.HasFlag</c> on boxed values), so text costs
    /// the heap a fixed amount per glyph that no caller can avoid; the render fixture multiplies this count by that cost to
    /// tell the font's allocation from the card's own.
    /// </summary>
    public static long GlyphsDrawn { get; private set; }

    public static void Text(SpriteBatch sb, string text, Vector2 position, Color color, float scale = .8f, DynamicSpriteFont? font = null)
    {
        font ??= FontAssets.MouseText.Value;
        int glyphs = 0;
        foreach (char c in text)
            if (c != '\n' && c != '\r') glyphs++;
        GlyphsDrawn += 2 * glyphs;
        sb.DrawString(font, text, position + new Vector2(2, 2), Color.Black, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        sb.DrawString(font, text, position, color, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }

    /// <summary>
    /// Text wrapped to a box, cut with an ellipsis at the last line that fits. The wrapping is kept per text, width, scale and
    /// line count, because wrapping and splitting build new strings and arrays, and a panel that wrapped its name and effect on
    /// every frame allocated for as long as it stayed open; the texts a card wraps are a small fixed set.
    /// </summary>
    public static void WrappedText(SpriteBatch sb, string text, Rectangle bounds, Color color, float scale = .75f)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var font = FontAssets.MouseText.Value;
        float lineHeight = (font.MeasureString("A\nA").Y - font.MeasureString("A").Y) * scale;
        int limit = Math.Max(1, (int)(bounds.Height / lineHeight));
        var key = (text, bounds.Width, scale, limit);
        if (!wrappedTexts.TryGetValue(key, out string? wrapped))
        {
            wrapped = font.CreateWrappedText(text, Math.Max(1, bounds.Width / scale));
            string[] lines = wrapped.Split('\n');
            if (lines.Length > limit)
            {
                string last = lines[limit - 1].TrimEnd();
                while (last.Length > 0 && font.MeasureString(last + "...").X * scale > bounds.Width) last = last[..^1];
                lines[limit - 1] = last + "...";
                wrapped = string.Join('\n', lines, 0, limit);
            }
            // A bound rather than an eviction policy: the card wraps a few hundred distinct texts at most, so this only
            // clears if something starts wrapping text that changes every frame, and then it stays bounded.
            if (wrappedTexts.Count >= 512) wrappedTexts.Clear();
            wrappedTexts[key] = wrapped;
        }
        Text(sb, wrapped, bounds.TopLeft(), color, scale);
    }

    private static readonly Dictionary<(string Text, int Width, float Scale, int Lines), string> wrappedTexts = new();

    public static CardButton Button(string text, float width, Action click)
    {
        var button = new CardButton(text);
        button.Width.Set(width, 0); button.Height.Set(30, 0); button.SetPadding(5);
        button.BackgroundColor = Panel; button.BorderColor = Edge;
        button.OnLeftClick += (_, _) => click();
        return button;
    }

    // ---- rounded shapes: runtime-built white alpha masks, tinted when drawn, one per radius and never one per size ----

    /// <summary>Bits for <see cref="RoundedFill"/>'s corners: 1 top-left, 2 top-right, 4 bottom-left, 8 bottom-right.</summary>
    public const int AllCorners = 0b1111, BottomCorners = 0b1100, LeftCorners = 0b0101, RightCorners = 0b1010;

    /// <summary>
    /// Every mask the card and the notch have built, keyed by what shapes it: a corner quadrant by its radius, a fillet by
    /// its side, radius and side of the notch. No key carries a width or a height, so the number of textures does not
    /// depend on how wide a bar's fill is; a cache keyed by pixel size uploaded a texture for every new fill width a bar
    /// reached in play and kept it until unload.
    /// </summary>
    private static readonly Dictionary<(int Kind, int A, int B, int C), Texture2D> masks = new();
    private const int CornerKind = 0, FilletKind = 1;

    /// <summary>
    /// A rounded rectangle filled with a colour; a radius larger than half the short side is clamped to it. Only the four
    /// radius-sized corners of a rounded rectangle carry anti-aliasing and everything between them is solid, so the shape is
    /// drawn as its corners, each the radius's one quadrant mask flipped into place or a solid square where that corner is
    /// not rounded, around three solid bands. That is pixel-for-pixel the image of a whole mask built at the shape's size,
    /// which the render fixture holds against a copy of that per-size mask.
    /// </summary>
    public static void RoundedFill(SpriteBatch sb, Rectangle r, int radius, int corners, Color color)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        radius = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
        if (radius == 0) { Fill(sb, r, color); return; }
        Texture2D quadrant = CornerMask(sb, radius);
        Corner(sb, quadrant, new Rectangle(r.X, r.Y, radius, radius), (corners & 1) != 0, SpriteEffects.None, color);
        Corner(sb, quadrant, new Rectangle(r.Right - radius, r.Y, radius, radius), (corners & 2) != 0, SpriteEffects.FlipHorizontally, color);
        Corner(sb, quadrant, new Rectangle(r.X, r.Bottom - radius, radius, radius), (corners & 4) != 0, SpriteEffects.FlipVertically, color);
        Corner(sb, quadrant, new Rectangle(r.Right - radius, r.Bottom - radius, radius, radius), (corners & 8) != 0,
            SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically, color);
        int middle = r.Width - 2 * radius;
        if (middle > 0)
        {
            Fill(sb, new Rectangle(r.X + radius, r.Y, middle, radius), color);
            Fill(sb, new Rectangle(r.X + radius, r.Bottom - radius, middle, radius), color);
        }
        if (r.Height - 2 * radius > 0)
            Fill(sb, new Rectangle(r.X, r.Y + radius, r.Width, r.Height - 2 * radius), color);
    }

    private static void Corner(SpriteBatch sb, Texture2D quadrant, Rectangle at, bool rounded, SpriteEffects flip, Color color)
    {
        if (rounded) sb.Draw(quadrant, at, null, color, 0f, Vector2.Zero, flip, 0f);
        else Fill(sb, at, color);
    }

    /// <summary>
    /// The top-left quadrant of a rounded corner, <paramref name="r"/> pixels square, with a one-pixel anti-aliased edge about
    /// the centre (r, r); the other three corners are this quadrant flipped, because a corner's distance from its own two
    /// edges is the same in every direction.
    /// </summary>
    private static Texture2D CornerMask(SpriteBatch sb, int r)
    {
        var key = (CornerKind, r, 0, 0);
        if (masks.TryGetValue(key, out Texture2D? cached))
            return cached;
        var data = new Color[r * r];
        for (int y = 0; y < r; y++)
            for (int x = 0; x < r; x++)
            {
                float d = MathF.Sqrt((x - r) * (x - r) + (y - r) * (y - r));
                data[y * r + x] = Color.White * MathHelper.Clamp(r - d + 0.5f, 0f, 1f);
            }
        var tex = new Texture2D(sb.GraphicsDevice, r, r);
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
        var key = (FilletKind, s, radius, flipX ? 1 : 0);
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

/// <summary>
/// A card button: the game's nine-slice panel with its label in the card's hard-shadow text, the same down-right shadow as
/// the graph and the status strip, without a blurred outline. It is a panel carrying its own label rather than a
/// <c>UITextPanel</c>, because that element draws its label through <c>Utils.DrawBorderString</c> on every frame whatever colour
/// it is given, and that parses the label into chat snippets: 26,876 bytes a call for a nine-letter label, measured headless on
/// 15 September 2026, for text this button had made transparent in order to draw it again itself.
/// </summary>
public sealed class CardButton(string text) : UIPanel
{
    public string Text { get; private set; } = text;
    public Color TextColor { get; set; } = Color.White;
    public float TextScale => .8f;

    public void SetText(string text) => Text = text;

    protected override void DrawSelf(SpriteBatch sb)
    {
        base.DrawSelf(sb);
        Rectangle r = GetDimensions().ToRectangle();
        if (IsMouseHovering)
            DrawCardPrimitives.Fill(sb, new Rectangle(r.X + 7, r.Bottom - 3, r.Width - 14, 1), Color.Gold);
        if (Text is "+" or "-")
        {
            // The game font at the card's text scale drops the vertical stroke of "+", so at UI scale 1 zoom in and
            // zoom out drew the same dash. Both are drawn as bars instead, with the text's own down-right shadow.
            Bars(sb, r.Center, 2, Color.Black, Text == "+");
            Bars(sb, r.Center, 0, TextColor, Text == "+");
            return;
        }
        Vector2 size = FontAssets.MouseText.Value.MeasureString(Text) * TextScale;
        DrawCardPrimitives.Text(sb, Text, new Vector2(r.Center.X - size.X / 2, r.Center.Y - size.Y / 2), TextColor, TextScale);
    }

    private static void Bars(SpriteBatch sb, Point c, int offset, Color colour, bool plus)
    {
        const int arm = 6, stroke = 2;
        DrawCardPrimitives.Fill(sb, new Rectangle(c.X - arm + offset, c.Y - stroke / 2 + offset, 2 * arm, stroke), colour);
        if (plus) DrawCardPrimitives.Fill(sb, new Rectangle(c.X - stroke / 2 + offset, c.Y - arm + offset, stroke, 2 * arm), colour);
    }
}

/// <summary>
/// A label made from two numbers, rebuilt only when either number changes, so a reading drawn on every frame builds its string
/// once per change instead of once per frame. The format must capture nothing (write it as a <c>static</c> lambda), so the
/// delegate is the compiler's cached one and asking for the label allocates nothing.
/// </summary>
public sealed class NumberLabel
{
    private long first = long.MinValue, second = long.MinValue;
    private string text = "";

    public string Of(long a, long b, Func<long, long, string> format)
    {
        if (a != first || b != second)
        {
            first = a; second = b;
            text = format(a, b);
        }
        return text;
    }
}
