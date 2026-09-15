#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace AICompanion.Companion.ProfileCard;

/// <summary>
/// A joined segment control: one rounded bar split into segments that share their borders, every choice shown
/// at once, the current one selected. Used for the work preferences, where a click chooses, and as the mastery
/// panel's level bar, where it only displays. The game's nine-slice panel cannot join flush, so the shapes are
/// the shared rounded masks.
///
/// <para>A lone segment is rounded at both ends. The mock rounded a last segment on its right and a first on its
/// left, and a control with one segment took only the last-child rounding, square on the left; the owner ruled
/// on 15 September 2026 that a single segment is rounded on both sides wherever the control is drawn.</para>
/// </summary>
public sealed class JoinedSegments : UIElement
{
    public const float TextScale = .68f;
    /// <summary>The border every segment draws, and the width two neighbours overlap by so the shared edge is one line.</summary>
    public const int Border = 2;

    private static readonly Color Solid = new(43, 48, 153);

    private readonly string[] labels;
    private readonly Func<int, bool> selected;
    private readonly Action<int>? choose;
    private readonly List<Segment> segments = new();

    public IReadOnlyList<UIElement> Segments => segments;
    public string Label(int index) => labels[index];
    public bool IsSelected(int index) => selected(index);

    /// <param name="choose">What a click on a segment does; null draws a display that takes no clicks.</param>
    public JoinedSegments(string[] labels, Func<int, bool> selected, Action<int>? choose)
    {
        this.labels = labels;
        this.selected = selected;
        this.choose = choose;
        int n = labels.Length;
        for (int i = 0; i < n; i++)
        {
            int index = i;
            // Each segment is (W + B(n-1))/n wide and starts B pixels inside the previous one's right edge, so the last
            // segment's right edge lands exactly on the control's.
            var segment = new Segment(this, index);
            segment.Left.Set(-(float)Border * i / n, (float)i / n);
            segment.Width.Set((float)Border * (n - 1) / n, 1f / n);
            segment.Height.Set(0, 1f);
            if (choose != null) segment.OnLeftClick += (_, _) => choose(index);
            segments.Add(segment);
            Append(segment);
        }
    }

    /// <summary>Which corners a segment rounds: both ends of a lone segment, the outer end of the first and last, none in between.</summary>
    public static int Corners(int index, int count)
        => count == 1 ? DrawCardPrimitives.AllCorners
            : index == 0 ? DrawCardPrimitives.LeftCorners
            : index == count - 1 ? DrawCardPrimitives.RightCorners
            : 0;

    private sealed class Segment(JoinedSegments owner, int index) : UIElement
    {
        protected override void DrawSelf(SpriteBatch sb)
        {
            Rectangle r = GetDimensions().ToRectangle();
            int n = owner.labels.Length;
            int corners = Corners(index, n);
            bool on = owner.selected(index);
            bool hover = owner.choose != null && IsMouseHovering;
            int radius = r.Height / 2;
            DrawCardPrimitives.RoundedFill(sb, r, radius, corners, on || hover ? Color.Gold : DrawCardPrimitives.Edge);
            var inner = new Rectangle(r.X + Border, r.Y + Border, r.Width - 2 * Border, r.Height - 2 * Border);
            DrawCardPrimitives.RoundedFill(sb, inner, Math.Max(0, radius - Border), corners, on ? DrawCardPrimitives.Selected : Solid);
            string text = owner.labels[index];
            Vector2 size = FontAssets.MouseText.Value.MeasureString(text) * TextScale;
            DrawCardPrimitives.Text(sb, text, new Vector2(r.Center.X - size.X / 2, r.Center.Y - size.Y / 2 + 1), on ? Color.Gold : Color.White, TextScale);
        }
    }
}
