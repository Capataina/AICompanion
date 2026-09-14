#nullable enable
using System;
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
/// A navigable design preview of the wheel and of the tree any diamond opens. Selection is local
/// UI state, never an earned upgrade. The first click on a node selects it and says what putting a
/// point there would mean; the second click on a selected diamond of the wheel swaps the wheel for
/// that diamond's own tree, and so does the button that appears while a diamond is selected. In the
/// tree the same button reads back to the wheel, and so does Reset view.
/// </summary>
public sealed class PreviewMasteryTree : UIElement
{
    public static Node[] Nodes => DefineMasteryGraph.Nodes;
    private readonly int[] tiers = new int[Nodes.Length];
    /// <summary>Preview ranks inside each diamond's own tree, rowed by the diamond's wheel index; a stat's row is never used.</summary>
    private readonly int[,] subTiers = new int[Nodes.Length, SubNodes.Length];
    private int selected = -1, opened = -1;
    private Vector2 pan, mouseDown, panDown;
    private float zoom = .7f;
    private bool dragging;
    private Point fittedSize;
    private readonly UIElement viewport;
    private readonly UITextPanel<string> upgrade, open;
    public int OpenedCount => tiers.Count(tier => tier > 0);
    private Rectangle Canvas => viewport.GetDimensions().ToRectangle();
    private static Vector2 Mouse => Main.MouseScreen;
    private Vector2 Screen(Vector2 position) => Canvas.Center.ToVector2() + pan + position * zoom;
    private bool InTree => opened >= 0;
    private Node[] Current => InTree ? SubNodes : Nodes;
    private Edge[] CurrentEdges => InTree ? SubEdges : Edges;
    private int Tier(int index) => InTree ? subTiers[opened, index] : tiers[index];
    /// <summary>Every node of an opened tree wears its diamond's branch colour; on the wheel each node wears its own.</summary>
    private Color ColourOf(Node node) => Colors[InTree ? Nodes[opened].Branch : node.Branch];

    public PreviewMasteryTree()
    {
        OverflowHidden = true;
        viewport = new UIElement { OverflowHidden = true };
        viewport.Top.Set(32, 0); viewport.Width.Set(0, 1); viewport.Height.Set(-120, 1);
        var graph = new Graph(this); graph.Width.Set(0, 1); graph.Height.Set(0, 1);
        viewport.Append(graph); Append(viewport);
        viewport.OnLeftMouseDown += (_, _) => { mouseDown = Mouse; panDown = pan; dragging = true; };
        viewport.OnLeftMouseUp += (_, _) =>
        {
            if (dragging && Vector2.DistanceSquared(Mouse, mouseDown) < 16) SelectAt(Mouse);
            dragging = false;
        };
        var minus = DrawCardPrimitives.Button("-", 30, () => Zoom(.85f, Canvas.Center.ToVector2()));
        minus.HAlign = 1; minus.Left.Set(-138, 0); Append(minus);
        var plus = DrawCardPrimitives.Button("+", 30, () => Zoom(1.18f, Canvas.Center.ToVector2()));
        plus.HAlign = 1; plus.Left.Set(-103, 0); Append(plus);
        var reset = DrawCardPrimitives.Button("Reset view", 98, () => { opened = -1; selected = -1; FitTree(); });
        reset.HAlign = 1; Append(reset);
        upgrade = DrawCardPrimitives.Button("Preview rank", 140, PreviewRank);
        upgrade.HAlign = 1; upgrade.Top.Set(-32, 1); Append(upgrade);
        open = DrawCardPrimitives.Button("Open upgrades", 140, () => { if (InTree) CloseTree(); else OpenSelected(); });
        open.HAlign = 1; open.Top.Set(-68, 1);
    }

    public override void Recalculate()
    {
        base.Recalculate();
        Point size = new(Canvas.Width, Canvas.Height);
        if (size.X > 0 && size.Y > 0 && size != fittedSize) { fittedSize = size; FitTree(); }
    }

    /// <summary>
    /// Both graphs are round with their labels, so the fit is the smaller axis against the graph's
    /// own extent, less the height of a label's text, which is drawn at screen scale rather than
    /// graph scale and would otherwise hang off the bottom at a small fit.
    /// </summary>
    private void FitTree()
    {
        pan = Vector2.Zero;
        zoom = Math.Max(.1f, (Math.Min(Canvas.Width, Canvas.Height) - 2 * LabelTextHeight) / (InTree ? SubExtent : Extent));
    }

    private const float LabelScale = .65f;
    private static float LabelTextHeight => FontAssets.MouseText.Value.LineSpacing * LabelScale;

    /// <summary>A lane label is centred on its authored point, so the four read as spokes of one wheel rather than hanging off its right.</summary>
    private static void DrawLabel(SpriteBatch sb, string text, Vector2 at, Color colour)
    {
        Vector2 size = FontAssets.MouseText.Value.MeasureString(text) * LabelScale;
        DrawCardPrimitives.Text(sb, text, at - size / 2, colour, LabelScale);
    }

    public bool CanPreview(int index)
    {
        foreach (Edge edge in CurrentEdges)
            if (edge.To == index && (edge.From < 0 || Tier(edge.From) > 0)) return true;
        return false;
    }

    private void PreviewRank()
    {
        if (selected < 0 || !CanPreview(selected)) return;
        int next = Math.Min(Current[selected].MaxTier, Tier(selected) + 1);
        if (InTree) subTiers[opened, selected] = next; else tiers[selected] = next;
    }

    private void OpenSelected()
    {
        if (InTree || selected < 0 || Nodes[selected].Kind != NodeKind.Ability) return;
        opened = selected; selected = -1; FitTree();
    }

    private void CloseTree() { opened = -1; selected = -1; FitTree(); }

    public override void Update(GameTime time)
    {
        base.Update(time);
        if (dragging && Main.mouseLeft) pan = panDown + Mouse - mouseDown;
        if (!Main.mouseLeft) dragging = false;
        if (IsMouseHovering) PlayerInput.LockVanillaMouseScroll("AICompanion/Mastery");
        bool available = selected >= 0 && CanPreview(selected) && Tier(selected) < Current[selected].MaxTier;
        upgrade.TextColor = available ? Color.LightGreen : DrawCardPrimitives.Muted;
        bool diamondSelected = !InTree && selected >= 0 && Nodes[selected].Kind == NodeKind.Ability;
        string label = InTree ? "Back to wheel" : "Open upgrades";
        if (open.Text != label) open.SetText(label);
        if ((InTree || diamondSelected) && open.Parent == null) Append(open);
        if (!InTree && !diamondSelected) open.Remove();
    }

    public override void ScrollWheel(UIScrollWheelEvent evt)
    {
        if (Canvas.Contains(Mouse.ToPoint())) Zoom(evt.ScrollWheelValue > 0 ? 1.12f : .89f, Mouse);
    }

    private void Zoom(float factor, Vector2 mouse)
    {
        Vector2 at = (mouse - Canvas.Center.ToVector2() - pan) / zoom;
        zoom = Math.Clamp(zoom * factor, .1f, 2f);
        pan = mouse - Canvas.Center.ToVector2() - at * zoom;
    }

    /// <summary>A node is hit anywhere inside its shape; the nearest wins where shapes could overlap at a small zoom.</summary>
    private void SelectAt(Vector2 mouse)
    {
        int hit = -1;
        float nearest = float.MaxValue;
        Node[] nodes = Current;
        for (int i = 0; i < nodes.Length; i++)
        {
            float distance = Vector2.DistanceSquared(mouse, Screen(nodes[i].Position));
            float radius = Radius(nodes[i]) + 4;
            if (distance > radius * radius || distance >= nearest) continue;
            nearest = distance; hit = i;
        }
        if (hit >= 0 && hit == selected && !InTree && nodes[hit].Kind == NodeKind.Ability) { OpenSelected(); return; }
        selected = hit;
    }

    protected override void DrawSelf(SpriteBatch sb)
    {
        Rectangle r = GetDimensions().ToRectangle();
        string heading = InTree ? Branches[Nodes[opened].Branch] + " · " + Nodes[opened].Name + " · its own upgrades" : "Preview · drag to pan";
        DrawCardPrimitives.Text(sb, heading, new Vector2(r.X + 4, r.Y + 6), DrawCardPrimitives.Muted, .7f);
        DrawCardPrimitives.Fill(sb, new Rectangle(r.X, r.Bottom - 84, r.Width, 1), DrawCardPrimitives.Edge * .6f);
        string title = InTree ? "Circle: upgrade rank   Diamond: mastery   Small: shared" : "Circle: stat   Diamond: ability   Small: shared";
        string detail = InTree ? "Select an upgrade. Nothing is spent here." : "Select a node. Click a selected diamond again to open its upgrades.";
        if (selected >= 0)
        {
            Node n = Current[selected]; title = $"{n.Name}  {Tier(selected)}/{n.MaxTier}"; detail = n.Description;
        }
        DrawCardPrimitives.Text(sb, title, new Vector2(r.X + 4, r.Bottom - 76), Color.White, .73f);
        DrawCardPrimitives.WrappedText(sb, detail, new Rectangle(r.X + 4, r.Bottom - 54, r.Width - 160, 42), DrawCardPrimitives.Muted, .64f);
        DrawCardPrimitives.Text(sb, "No stats, points or materials changed", new Vector2(r.X + 4, r.Bottom - 17), DrawCardPrimitives.Muted, .6f);
    }

    private float Radius(Node node) => Math.Max(node.Kind == NodeKind.Shared ? 2.4f : 3.5f, (node.Kind == NodeKind.Ability ? 12 : node.Kind == NodeKind.Shared ? 5 : 9) * zoom);

    /// <summary>
    /// How far a node's boundary sits from its centre along a direction, so an edge stops at the
    /// side of the shape rather than running through to the centre. A circle is its radius
    /// everywhere; a diamond with its vertices a radius out along the axes has sides that pass
    /// radius over root two from the centre, and the distance along an angle is radius over the
    /// sum of the absolute cosine and sine.
    /// </summary>
    private static float Boundary(bool diamond, float radius, float angle)
        => diamond ? radius / (MathF.Abs(MathF.Cos(angle)) + MathF.Abs(MathF.Sin(angle))) : radius;

    private void DrawTree(SpriteBatch sb)
    {
        Node[] nodes = Current;
        float centreRadius = Math.Max(3, 14 * zoom);
        foreach (Edge edge in CurrentEdges)
        {
            Node to = nodes[edge.To];
            Vector2 a = Screen(edge.From < 0 ? Vector2.Zero : nodes[edge.From].Position), b = Screen(to.Position);
            float angle = (b - a).ToRotation();
            float fromBoundary = edge.From < 0 ? Boundary(true, centreRadius, angle) : Boundary(nodes[edge.From].Kind == NodeKind.Ability, Radius(nodes[edge.From]), angle);
            float toBoundary = Boundary(to.Kind == NodeKind.Ability, Radius(to), angle);
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            bool openEdge = edge.From < 0 || Tier(edge.From) > 0;
            Line(sb, a + direction * fromBoundary, b - direction * toBoundary, ColourOf(to) * (openEdge ? .85f : .33f));
        }
        for (int i = 0; i < nodes.Length; i++)
        {
            Node n = nodes[i];
            Color color = selected == i ? Color.White : Tier(i) > 0 ? Color.LightGreen : ColourOf(n) * (CanPreview(i) ? 1f : .65f);
            DrawNode(sb, Screen(n.Position), n.Kind == NodeKind.Ability, Radius(n), color, Tier(i) > 0);
        }
        if (InTree)
        {
            Color branch = Colors[Nodes[opened].Branch];
            for (int i = 0; i < SubLines; i++)
                DrawLabel(sb, SubLineNames[i], Screen(SubLabels[i]), branch);
            // The opened diamond sits filled at the centre where the wheel has its root.
            DrawNode(sb, Screen(Vector2.Zero), true, centreRadius, branch, true);
        }
        else
        {
            for (int i = 0; i < Branches.Length; i++)
                DrawLabel(sb, Branches[i], Screen(Labels[i]), Colors[i]);
            DrawNode(sb, Screen(Vector2.Zero), true, centreRadius, Color.Gold, true);
        }
    }

    private sealed class Graph(PreviewMasteryTree owner) : UIElement
    {
        protected override void DrawSelf(SpriteBatch sb) => owner.DrawTree(sb);
    }

    private static void DrawNode(SpriteBatch sb, Vector2 p, bool diamond, float radius, Color color, bool filled)
    {
        int sides = diamond ? 4 : 16;
        if (filled) DrawCardPrimitives.Fill(sb, new Rectangle((int)p.X - 2, (int)p.Y - 2, 4, 4), color);
        for (int i = 0; i < sides; i++)
        {
            float a = i * MathF.Tau / sides, b = (i + 1) * MathF.Tau / sides;
            Line(sb, p + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius, p + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius, color);
        }
    }

    private static void Line(SpriteBatch sb, Vector2 a, Vector2 b, Color c)
    {
        Vector2 d = b - a;
        sb.Draw(TextureAssets.MagicPixel.Value, a, new Rectangle(0, 0, 1, 1), c, d.ToRotation(), Vector2.Zero, new Vector2(d.Length(), 1.3f), SpriteEffects.None, 0);
    }
}
