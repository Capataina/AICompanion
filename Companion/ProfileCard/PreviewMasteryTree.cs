#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.UI;
using static AICompanion.Companion.ProfileCard.DefineMasteryGraph;

namespace AICompanion.Companion.ProfileCard;

/// <summary>
/// The Mastery page: the tree, and while a node is picked a panel beside it with the node's name, its effect, Learn
/// and a level bar. It is simple by the owner's ruling of 15 September 2026, which called the earlier page "so
/// crowded for no reason": no kind or rank line, no purpose sentence, no "sized against", no needs, no hint above the
/// graph, no legend and no status line under it, and no Unlearn, because a learned level stays learned. −, + and
/// Reset view are the page's title-bar actions. Learn names what the next level costs, or says Learned once the node is
/// full, and the gold centre is Core, picked by a click like any node. Everything here is local preview state: the
/// points a level costs are counted for the Mastery tile, but nothing is gated on them, spent from a budget or saved.
///
/// <para>The view is a scale and the screen position of the graph's origin measured from the canvas's top-left corner.
/// Anchoring at the corner rather than the centre is what lets the panel open without anything moving: the canvas
/// narrows by the panel's width and every node keeps its place, and only if the panel would cover the picked node is
/// the view panned to bring it back.</para>
/// </summary>
public sealed class PreviewMasteryTree : UIElement, ICardPage
{
    public const float PanelWidth = 220, PanelGap = 12, PanelPadding = 12, LearnHeight = 34, LevelHeight = 22, LearnGap = 8;
    /// <summary>A press that travels less than this before release is a click; farther, it is a pan.</summary>
    public const float DragThreshold = 6;
    private const float FitMargin = 12;

    /// <summary>Every node's learned level, and Core's in the last place.</summary>
    private readonly int[] levels = new int[Nodes.Length + 1];
    private static readonly Color Gold = new(255, 224, 102);
    private readonly UIElement canvas;
    private readonly DetailPanel panel;
    private int picked = -1;
    private float zoom = .3f, fittedZoom = .3f;
    private Vector2 origin;
    private bool pressing, panning;
    private Vector2 fittedFor;
    private Vector2 pressAt, originAtPress;

    public string Title => "Mastery";
    public IReadOnlyList<CardAction> Actions { get; }

    public int Picked => picked;
    public float Zoom => zoom;
    public Vector2 Origin => origin;
    public int Level(int node) => levels[node];
    /// <summary>How many of the tree's nodes have a level, Core not counted, for the tile's fill; a loop, because the tile asks every frame.</summary>
    public int LearnedCount
    {
        get
        {
            int learned = 0;
            for (int node = 0; node < Nodes.Length; node++)
                if (levels[node] > 0) learned++;
            return learned;
        }
    }
    /// <summary>The points every learned level cost, Core's included, for the tile's reading; a loop, because the tile asks every frame.</summary>
    public int Spent
    {
        get
        {
            int spent = 0;
            for (int node = 0; node < levels.Length; node++) spent += ContentOf(node).PointsIn(levels[node]);
            return spent;
        }
    }
    /// <summary>What Learn says: the next level's cost, or Learned once the node is full; the panel asks every frame, so the words are kept per node and level.</summary>
    public string LearnLabel => picked < 0 ? "Learn" : learnLabel.Of(picked, levels[picked], static (node, level) => LearnLabelFor((int)node, (int)level));
    private readonly NumberLabel learnLabel = new();

    private static string LearnLabelFor(int node, int level)
    {
        NodeContent content = ContentOf(node);
        if (level >= content.Levels) return "Learned";
        int cost = content.CostOf(level + 1);
        return $"Learn · {cost} point{(cost == 1 ? "" : "s")}";
    }

    /// <summary>How many tree nodes, the gold centre included, the last frame drew; a node whose shape lies wholly outside the canvas is not drawn.</summary>
    public int NodesDrawn { get; private set; }
    public UIElement Canvas => canvas;
    public UIElement Panel => panel;
    public JoinedSegments? LevelBar => panel.LevelBar;
    public CardButton LearnButton => panel.Learn;
    /// <summary>The effect line the panel draws now; the fixture reads the same method the drawing does.</summary>
    public string EffectLine => panel.EffectLine();

    private static Vector2 Mouse => Main.MouseScreen;

    public PreviewMasteryTree()
    {
        canvas = new UIElement { OverflowHidden = true };
        canvas.Width.Set(0, 1f); canvas.Height.Set(0, 1f);
        var graph = new Graph(this);
        graph.Width.Set(0, 1f); graph.Height.Set(0, 1f);
        canvas.Append(graph);
        Append(canvas);
        canvas.OnLeftMouseDown += (_, _) => { pressing = true; panning = false; pressAt = Mouse; originAtPress = origin; };
        canvas.OnLeftMouseUp += (_, _) =>
        {
            if (pressing && !panning) ClickAt(Mouse);
            pressing = false; panning = false;
        };
        panel = new DetailPanel(this);
        panel.HAlign = 1; panel.Width.Set(PanelWidth, 0); panel.Height.Set(0, 1f);
        Actions = new[]
        {
            new CardAction("-", () => ZoomBy(.85f, CanvasCentre)),
            new CardAction("+", () => ZoomBy(1.15f, CanvasCentre)),
            new CardAction("Reset view", Fit),
        };
    }

    private Vector2 CanvasTopLeft => canvas.GetDimensions().Position();
    private Vector2 CanvasCentre => canvas.GetDimensions().Center();

    /// <summary>Where a graph position is drawn on screen under the current view.</summary>
    public Vector2 Screen(Vector2 position) => CanvasTopLeft + origin + position * zoom;

    /// <summary>
    /// Fit whenever the page itself changes size, never when only the canvas does. The card appends a page before it grows
    /// the frame to the page's height, and a UI element's height is capped at its parent's, so the first recalculation sees
    /// the overview's short frame; a fit taken once at that moment left the tree centred in a canvas less than half the
    /// page's height. Picking a node narrows the canvas and leaves the page's size alone, so it never refits.
    /// </summary>
    public override void Recalculate()
    {
        base.Recalculate();
        if (canvas == null) return;
        CalculatedStyle page = GetDimensions();
        var size = new Vector2(page.Width, page.Height);
        if (size.X > 0 && size.Y > 0 && size != fittedFor) { fittedFor = size; Fit(); }
    }

    /// <summary>A lane label's box in graph units: its text measured once at the label height, placed by its pivot.</summary>
    public static (Vector2 Min, Vector2 Max) LabelBox(int lane)
    {
        LaneLabel label = Labels[lane];
        Vector2 size = FontAssets.MouseText.Value.MeasureString(label.Text) * (LabelHeight / FontAssets.MouseText.Value.MeasureString("A").Y);
        Vector2 min = label.Anchor - label.Pivot * size;
        return (min, min + size);
    }

    /// <summary>The tree's real extent in graph units, every node's shape and every lane label included.</summary>
    public static (Vector2 Min, Vector2 Max) TreeBounds()
    {
        Vector2 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (Node node in Nodes)
        {
            float extent = node.Kind == NodeKind.Diamond ? NodeRadius * MathF.Sqrt(2) : NodeRadius;
            min = Vector2.Min(min, node.Position - new Vector2(extent));
            max = Vector2.Max(max, node.Position + new Vector2(extent));
        }
        for (int lane = 0; lane < Lanes.Length; lane++)
        {
            var (lo, hi) = LabelBox(lane);
            min = Vector2.Min(min, lo);
            max = Vector2.Max(max, hi);
        }
        return (min, max);
    }

    /// <summary>Reset view: the whole tree, labels included, centred in the canvas as it is now.</summary>
    public void Fit()
    {
        CalculatedStyle c = canvas.GetDimensions();
        var (min, max) = TreeBounds();
        Vector2 size = max - min;
        zoom = fittedZoom = Math.Max(.01f, Math.Min((c.Width - 2 * FitMargin) / size.X, (c.Height - 2 * FitMargin) / size.Y));
        origin = new Vector2(c.Width, c.Height) / 2 - (min + max) / 2 * zoom;
    }

    private void ZoomBy(float factor, Vector2 about)
    {
        Vector2 at = (about - CanvasTopLeft - origin) / zoom;
        zoom = Math.Clamp(zoom * factor, fittedZoom * .2f, fittedZoom * 4f);
        origin = about - CanvasTopLeft - at * zoom;
    }

    public override void Update(GameTime time)
    {
        base.Update(time);
        if (pressing && Main.mouseLeft)
        {
            if (!panning && Vector2.Distance(Mouse, pressAt) >= DragThreshold) panning = true;
            if (panning) origin = originAtPress + Mouse - pressAt;
        }
        if (IsMouseHovering)
        {
            Main.LocalPlayer.mouseInterface = true;
            PlayerInput.LockVanillaMouseScroll("AICompanion/Mastery");
        }
    }

    public override void ScrollWheel(UIScrollWheelEvent evt)
    {
        if (canvas.ContainsPoint(Mouse)) ZoomBy(evt.ScrollWheelValue > 0 ? 1.12f : .89f, Mouse);
    }

    /// <summary>The node under a screen point, the gold centre (Core) included, the nearest where shapes could overlap at a small zoom, else -1.</summary>
    public int NodeAt(Vector2 point)
    {
        int hit = -1;
        float nearest = float.MaxValue;
        for (int i = 0; i <= Nodes.Length; i++)
        {
            float distance = Vector2.Distance(point, Screen(PositionOf(i)));
            bool diamond = i == Core || Nodes[i].Kind == NodeKind.Diamond;
            float radius = Math.Max(6, NodeRadius * zoom * (diamond ? 1.3f : 1f) + 3);
            if (distance > radius || distance >= nearest) continue;
            nearest = distance; hit = i;
        }
        return hit;
    }

    /// <summary>A click on a node picks it; a click on empty background unpicks and closes the panel.</summary>
    private void ClickAt(Vector2 point) => Pick(NodeAt(point));

    /// <summary>Pick a node, or -1 for none. Opening the panel keeps the view's left edge and scale, then pans only if the panel would cover the node.</summary>
    public void Pick(int node)
    {
        picked = node;
        if (node < 0)
        {
            panel.Remove();
            canvas.Width.Set(0, 1f);
            Recalculate();
            return;
        }
        if (panel.Parent == null) Append(panel);
        canvas.Width.Set(-(PanelWidth + PanelGap), 1f);
        Recalculate();
        panel.Refresh();
        CalculatedStyle c = canvas.GetDimensions();
        Vector2 at = Screen(PositionOf(node));
        float margin = NodeRadius * zoom * 1.5f + 8;
        if (at.X + margin > c.X + c.Width) origin.X -= at.X + margin - (c.X + c.Width);
        if (at.X - margin < c.X) origin.X += c.X - (at.X - margin);
        if (at.Y + margin > c.Y + c.Height) origin.Y -= at.Y + margin - (c.Y + c.Height);
        if (at.Y - margin < c.Y) origin.Y += c.Y - (at.Y - margin);
    }

    /// <summary>Whether a node can take a level in this preview; the rule is the graph's own.</summary>
    public bool CanLearn(int node) => DefineMasteryGraph.CanLearn(levels, node);

    public void Learn()
    {
        if (picked >= 0 && CanLearn(picked)) levels[picked]++;
        panel.Refresh();
    }

    private static float Boundary(NodeKind kind, float radius, float angle)
        => kind == NodeKind.Diamond ? radius * MathF.Sqrt(2) / (MathF.Abs(MathF.Cos(angle)) + MathF.Abs(MathF.Sin(angle))) : radius;

    /// <summary>
    /// The tree at the current view, skipping what cannot show: an edge whose extent lies wholly outside the canvas, a node or
    /// the centre whose shape does, and a label whose box does. Zoomed in, most of the tree is outside the canvas and was drawn
    /// span by span under the canvas's clip for nothing, at a cost that grew with the square of the zoom.
    /// </summary>
    private void DrawTree(SpriteBatch sb)
    {
        float r = NodeRadius * zoom;
        CalculatedStyle view = canvas.GetDimensions();
        float left = view.X, top = view.Y, right = view.X + view.Width, bottom = view.Y + view.Height;
        // One pixel of slack, because a shape's spans are rounded outward to whole pixels.
        bool Shows(float minX, float minY, float maxX, float maxY) => maxX + 1 >= left && minX - 1 <= right && maxY + 1 >= top && minY - 1 <= bottom;
        NodesDrawn = 0;
        foreach (Edge edge in Edges)
        {
            Node to = Nodes[edge.To];
            Vector2 a = Screen(edge.From < 0 ? Vector2.Zero : Nodes[edge.From].Position), b = Screen(to.Position);
            if (!Shows(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y))) continue;
            float angle = (b - a).ToRotation();
            float fromBoundary = edge.From < 0 ? Boundary(NodeKind.Diamond, r, angle) : Boundary(Nodes[edge.From].Kind, r, angle);
            float toBoundary = Boundary(to.Kind, r, angle);
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            bool lit = (edge.From < 0 || levels[edge.From] > 0) && levels[edge.To] > 0;
            DrawCardPrimitives.Line(sb, a + direction * fromBoundary, b - direction * toBoundary,
                Colors[to.Lane] * (lit ? 1f : .6f), Math.Max(1f, (lit ? 5f : 2.5f) * zoom));
        }
        for (int i = 0; i < Nodes.Length; i++)
        {
            Node node = Nodes[i];
            Color colour = Colors[node.Lane];
            int level = levels[i];
            float alpha = level > 0 || CanLearn(i) ? 1f : .45f;
            Color fill = level > 0 ? colour * (level < node.Content.Levels ? .45f : 1f)
                : node.Kind == NodeKind.Shared ? Color.White * .12f : Color.Transparent;
            bool diamond = node.Kind == NodeKind.Diamond;
            Vector2 at = Screen(node.Position);
            float extent = diamond ? r * MathF.Sqrt(2) : r;
            if (!Shows(at.X - extent, at.Y - extent, at.X + extent, at.Y + extent)) continue;
            NodesDrawn++;
            if (fill.A > 0) DrawShape(sb, at, r, float.MaxValue, diamond, fill * alpha);
            DrawShape(sb, at, r, Math.Max(1f, (i == picked ? 6f : 3.5f) * zoom), diamond, colour * alpha);
        }
        Vector2 centre = Screen(Vector2.Zero);
        float centreExtent = r * MathF.Sqrt(2);
        if (Shows(centre.X - centreExtent, centre.Y - centreExtent, centre.X + centreExtent, centre.Y + centreExtent))
        {
            NodesDrawn++;
            DrawShape(sb, centre, r, float.MaxValue, true, Gold);
        }
        float textScale = LabelHeight * zoom / FontAssets.MouseText.Value.MeasureString("A").Y;
        for (int lane = 0; lane < Lanes.Length; lane++)
        {
            var (min, max) = LabelBox(lane);
            Vector2 from = Screen(min), to = Screen(max);
            if (!Shows(from.X, from.Y, to.X, to.Y)) continue;
            DrawCardPrimitives.Text(sb, Labels[lane].Text, from, Colors[lane], textScale);
        }
    }

    /// <summary>
    /// A circle, or a diamond whose vertices sit radius times root two out (a square of half-size radius turned a
    /// quarter), drawn as horizontal spans; <paramref name="stroke"/> is the ring's thickness, and a stroke wider than the
    /// shape fills it.
    /// </summary>
    private static void DrawShape(SpriteBatch sb, Vector2 centre, float radius, float stroke, bool diamond, Color colour)
    {
        float outer = diamond ? radius * MathF.Sqrt(2) : radius;
        float inner = outer - (diamond ? stroke * MathF.Sqrt(2) : stroke);
        for (int y = (int)MathF.Floor(centre.Y - outer); y <= (int)MathF.Ceiling(centre.Y + outer); y++)
        {
            float dy = MathF.Abs(y + .5f - centre.Y);
            if (dy > outer) continue;
            float o = diamond ? outer - dy : MathF.Sqrt(outer * outer - dy * dy);
            float i = inner <= 0 || dy > inner ? -1 : diamond ? inner - dy : MathF.Sqrt(inner * inner - dy * dy);
            int left = (int)MathF.Round(centre.X - o), right = (int)MathF.Round(centre.X + o);
            if (i < 0)
            {
                DrawCardPrimitives.Fill(sb, new Rectangle(left, y, Math.Max(1, right - left), 1), colour);
                continue;
            }
            int innerLeft = (int)MathF.Round(centre.X - i), innerRight = (int)MathF.Round(centre.X + i);
            DrawCardPrimitives.Fill(sb, new Rectangle(left, y, Math.Max(1, innerLeft - left), 1), colour);
            DrawCardPrimitives.Fill(sb, new Rectangle(innerRight, y, Math.Max(1, right - innerRight), 1), colour);
        }
    }

    private sealed class Graph(PreviewMasteryTree owner) : UIElement
    {
        protected override void DrawSelf(SpriteBatch sb) => owner.DrawTree(sb);
    }

    /// <summary>The picked node's name and effect at the top, Learn and the level bar at the very bottom.</summary>
    private sealed class DetailPanel : UIPanel
    {
        private readonly PreviewMasteryTree owner;
        private int built = -2, builtLevel = -1;
        public CardButton Learn { get; }
        public JoinedSegments? LevelBar { get; private set; }

        public DetailPanel(PreviewMasteryTree owner)
        {
            this.owner = owner;
            BackgroundColor = DrawCardPrimitives.Panel;
            BorderColor = DrawCardPrimitives.Edge;
            SetPadding(PanelPadding);
            Learn = DrawCardPrimitives.Button("Learn", 0, owner.Learn);
            Learn.Width.Set(0, 1f); Learn.Height.Set(LearnHeight, 0);
            Learn.VAlign = 1; Learn.Top.Set(-(LevelHeight + LearnGap), 0);
            Append(Learn);
        }

        /// <summary>
        /// Name Learn's next cost, and rebuild the level bar for the picked node, one segment per level. Core's bar is one
        /// segment naming the level it has reached, because an unbounded node has no last segment to draw, so it is rebuilt
        /// whenever that level changes.
        /// </summary>
        public void Refresh()
        {
            string label = owner.LearnLabel;
            if (Learn.Text != label) Learn.SetText(label);
            int node = owner.picked;
            int reached = node >= 0 ? owner.levels[node] : 0;
            if (built == node && (node != Core || builtLevel == reached)) return;
            Rebuild(node, reached);
        }

        /// <summary>
        /// The level bar rebuilt for a newly picked node, or for Core's new level. It is its own method because its lambdas
        /// capture the node and the level count, and a closure is created where the captured variables come into scope: inside
        /// <see cref="Refresh"/>, which runs every frame and nearly always returns before rebuilding, it allocated on every frame.
        /// </summary>
        private void Rebuild(int node, int reached)
        {
            built = node; builtLevel = reached;
            LevelBar?.Remove();
            if (node < 0) { LevelBar = null; return; }
            // "Level N" fits a segment of a three-level bar; a fifth of the panel's width fits only the number.
            int count = ContentOf(node).Levels;
            string[] labels = node == Core ? new[] { $"Level {reached}" }
                : Enumerable.Range(1, count).Select(level => count > DiamondLevels ? $"{level}" : $"Level {level}").ToArray();
            LevelBar = new JoinedSegments(labels, segment => segment < owner.levels[node], null);
            LevelBar.Width.Set(0, 1f); LevelBar.Height.Set(LevelHeight, 0); LevelBar.VAlign = 1;
            Append(LevelBar);
            Recalculate();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            Refresh();
            bool can = owner.picked >= 0 && owner.CanLearn(owner.picked);
            Learn.TextColor = can ? Color.White : DrawCardPrimitives.Muted * .6f;
            Learn.BorderColor = can && Learn.IsMouseHovering ? Color.Gold : can ? DrawCardPrimitives.Edge : DrawCardPrimitives.Edge * .4f;
        }

        /// <summary>
        /// The one effect line the panel says: a node whose levels step unevenly says the next level's line, or its last
        /// once full, and a hovered level segment says that level's own line until the pointer leaves it.
        /// </summary>
        public string EffectLine()
        {
            if (owner.picked < 0) return "";
            NodeContent content = ContentOf(owner.picked);
            if (content.LevelLines is { } lines && LevelBar?.HoveredIndex is int hovered) return lines[hovered];
            return NextLevelLine(owner.picked, owner.levels[owner.picked]);
        }

        protected override void DrawSelf(SpriteBatch sb)
        {
            base.DrawSelf(sb);
            if (owner.picked < 0) return;
            int node = owner.picked;
            Rectangle r = GetInnerDimensions().ToRectangle();
            DrawCardPrimitives.WrappedText(sb, ContentOf(node).Name, new Rectangle(r.X, r.Y, r.Width, 26), node == Core ? Gold : Colors[Nodes[node].Lane], 1f);
            DrawCardPrimitives.WrappedText(sb, EffectLine(), new Rectangle(r.X, r.Y + 32, r.Width, 90), Gold, .75f);
        }
    }
}
