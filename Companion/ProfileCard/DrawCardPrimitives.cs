#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace AICompanion.Companion.ProfileCard;

/// <summary>Native panel assets and one hard-shadow text treatment for the whole card.</summary>
public static class DrawCardPrimitives
{
    public static readonly Color Panel = new Color(43, 48, 153) * .86f;
    public static readonly Color Edge = new(143, 147, 240);
    public static readonly Color Muted = new(195, 198, 245);
    public static readonly Color Selected = new(86, 91, 194);

    public static void Fill(SpriteBatch sb, Rectangle r, Color color)
        => sb.Draw(TextureAssets.MagicPixel.Value, r, new Rectangle(0, 0, 1, 1), color);

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
            Vector2 size = FontAssets.MouseText.Value.MeasureString(Text) * TextScale;
            DrawCardPrimitives.Text(sb, Text, new Vector2(r.Center.X - size.X / 2, r.Center.Y - size.Y / 2), ink, TextScale);
        }
    }
}
