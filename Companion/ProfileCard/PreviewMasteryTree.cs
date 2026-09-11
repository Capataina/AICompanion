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

/// <summary>A navigable design preview. Selection is local UI state, never an earned upgrade.</summary>
public sealed class PreviewMasteryTree : UIElement
{
    public static Node[] Nodes => DefineMasteryGraph.Nodes;
    private readonly int[] tiers = new int[Nodes.Length];
    private readonly int[,] weaponTiers = new int[8, 4];
    private int selected = -1, weaponBranch = -1;
    private Vector2 pan, mouseDown, panDown;
    private float zoom = .7f;
    private bool dragging;
    private Point fittedSize;
    private readonly UIElement viewport;
    private readonly UITextPanel<string> upgrade, weapon;
    public int OpenedCount => tiers.Count(tier => tier > 0);
    private Rectangle Canvas => viewport.GetDimensions().ToRectangle();
    private static Vector2 Mouse => Main.MouseScreen;
    private Vector2 Screen(Vector2 position) => Canvas.Center.ToVector2() + pan + position * zoom;

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
        var reset = DrawCardPrimitives.Button("Reset view", 98, () => { weaponBranch = -1; selected = -1; FitTree(); });
        reset.HAlign = 1; Append(reset);
        upgrade = DrawCardPrimitives.Button("Preview rank", 140, PreviewRank);
        upgrade.HAlign = 1; upgrade.Top.Set(-32, 1); Append(upgrade);
        weapon = DrawCardPrimitives.Button("Weapon details", 140, () =>
        {
            if (weaponBranch < 0 && selected >= 0 && Nodes[selected].Weapon)
            { weaponBranch = Nodes[selected].Branch; selected = -1; FitTree(); }
        });
        weapon.HAlign = 1; weapon.Top.Set(-68, 1);
    }

    public override void Recalculate()
    {
        base.Recalculate();
        Point size = new(Canvas.Width, Canvas.Height);
        if (size.X > 0 && size.Y > 0 && size != fittedSize) { fittedSize = size; FitTree(); }
    }

    private void FitTree()
    {
        pan = Vector2.Zero;
        // Bounds include the hand-placed labels; fitting respects both axes rather
        // than shrinking a wide graph against its width twice.
        zoom = Math.Max(.1f, Math.Min(Canvas.Width / (weaponBranch < 0 ? 1720f : 440f), Canvas.Height / (weaponBranch < 0 ? 620f : 240f)));
    }

    public bool CanPreview(int index)
    {
        foreach (Edge edge in Edges)
            if (edge.To == index && (edge.From < 0 || tiers[edge.From] > 0)) return true;
        return false;
    }

    private bool CanPreviewWeapon(int index)
    {
        foreach (Edge edge in WeaponEdges)
            if (edge.To == index && (edge.From < 0 || weaponTiers[weaponBranch, edge.From] > 0)) return true;
        return false;
    }

    private void PreviewRank()
    {
        if (selected < 0) return;
        if (weaponBranch >= 0)
        {
            if (CanPreviewWeapon(selected))
                weaponTiers[weaponBranch, selected] = Math.Min(5, weaponTiers[weaponBranch, selected] + 1);
        }
        else if (CanPreview(selected))
            tiers[selected] = Math.Min(Nodes[selected].MaxTier, tiers[selected] + 1);
    }

    public override void Update(GameTime time)
    {
        base.Update(time);
        if (dragging && Main.mouseLeft) pan = panDown + Mouse - mouseDown;
        if (!Main.mouseLeft) dragging = false;
        if (IsMouseHovering) PlayerInput.LockVanillaMouseScroll("AICompanion/Mastery");
        bool available = selected >= 0 && (weaponBranch >= 0 ? CanPreviewWeapon(selected) && weaponTiers[weaponBranch, selected] < 5 : CanPreview(selected) && tiers[selected] < Nodes[selected].MaxTier);
        upgrade.TextColor = available ? Color.LightGreen : DrawCardPrimitives.Muted;
        bool hasWeapon = weaponBranch < 0 && selected >= 0 && Nodes[selected].Weapon;
        if (hasWeapon && weapon.Parent == null) Append(weapon);
        if (!hasWeapon) weapon.Remove();
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

    private void SelectAt(Vector2 mouse)
    {
        selected = -1;
        float nearest = float.MaxValue;
        int count = weaponBranch >= 0 ? WeaponPositions.Length : Nodes.Length;
        for (int i = 0; i < count; i++)
        {
            Vector2 position = weaponBranch >= 0 ? WeaponPositions[i] : Nodes[i].Position;
            float distance = Vector2.DistanceSquared(mouse, Screen(position));
            float radius = weaponBranch >= 0 ? 14 : Radius(Nodes[i]) + 4;
            if (distance > radius * radius || distance >= nearest) continue;
            nearest = distance; selected = i;
        }
    }

    protected override void DrawSelf(SpriteBatch sb)
    {
        Rectangle r = GetDimensions().ToRectangle();
        DrawCardPrimitives.Text(sb, weaponBranch < 0 ? "Preview · drag to pan" : Branches[weaponBranch] + " · weapon preview", new Vector2(r.X + 4, r.Y + 6), DrawCardPrimitives.Muted, .7f);
        DrawCardPrimitives.Fill(sb, new Rectangle(r.X, r.Bottom - 84, r.Width, 1), DrawCardPrimitives.Edge * .6f);
        string title = "Circle: stat   Diamond: ability   Small: path rank";
        string detail = "Select a node. Shared junctions open from either incoming path.";
        if (selected >= 0)
        {
            if (weaponBranch >= 0)
            { title = $"{WeaponUpgrades[selected]}  {weaponTiers[weaponBranch, selected]}/5"; detail = "Nested weapon rank. Preview only."; }
            else
            { Node n = Nodes[selected]; title = $"{n.Name}  {tiers[selected]}/{n.MaxTier}"; detail = n.Description; }
        }
        DrawCardPrimitives.Text(sb, title, new Vector2(r.X + 4, r.Bottom - 76), Color.White, .73f);
        DrawCardPrimitives.WrappedText(sb, detail, new Rectangle(r.X + 4, r.Bottom - 54, r.Width - 160, 42), DrawCardPrimitives.Muted, .64f);
        DrawCardPrimitives.Text(sb, "No stats, points or materials changed", new Vector2(r.X + 4, r.Bottom - 17), DrawCardPrimitives.Muted, .6f);
    }

    private float Radius(Node node) => Math.Max(node.Kind == NodeKind.Filler ? 2.4f : 3.5f, (node.Kind == NodeKind.Ability ? 12 : node.Kind == NodeKind.Filler ? 5 : 9) * zoom);

    private void DrawMainTree(SpriteBatch sb)
    {
        foreach (Edge edge in Edges)
        {
            Color color = Colors[Nodes[edge.To].Branch];
            bool open = edge.From < 0 || tiers[edge.From] > 0;
            Line(sb, Screen(edge.From < 0 ? Vector2.Zero : Nodes[edge.From].Position), Screen(Nodes[edge.To].Position), color * (open ? .85f : .33f));
        }
        for (int i = 0; i < Nodes.Length; i++)
        {
            Node n = Nodes[i];
            Color color = selected == i ? Color.White : tiers[i] > 0 ? Color.LightGreen : Colors[n.Branch] * (CanPreview(i) ? 1f : .65f);
            DrawNode(sb, Screen(n.Position), n.Kind == NodeKind.Ability, Radius(n), color, tiers[i] > 0);
        }
        for (int i = 0; i < Branches.Length; i++)
            DrawCardPrimitives.Text(sb, Branches[i], Screen(Labels[i]), Colors[i], .65f);
        DrawNode(sb, Screen(Vector2.Zero), true, Math.Max(3, 14 * zoom), Color.Gold, true);
    }

    private void DrawWeaponTree(SpriteBatch sb)
    {
        foreach (Edge edge in WeaponEdges)
            if (edge.From >= 0)
                Line(sb, Screen(WeaponPositions[edge.From]), Screen(WeaponPositions[edge.To]), Colors[weaponBranch] * (weaponTiers[weaponBranch, edge.From] > 0 ? .85f : .33f));
        for (int i = 0; i < WeaponPositions.Length; i++)
        {
            Vector2 p = Screen(WeaponPositions[i]);
            Color color = selected == i ? Color.White : weaponTiers[weaponBranch, i] > 0 ? Color.LightGreen : Colors[weaponBranch] * (CanPreviewWeapon(i) ? 1f : .65f);
            DrawNode(sb, p, i == 3, 9, color, weaponTiers[weaponBranch, i] > 0);
            DrawCardPrimitives.Text(sb, WeaponUpgrades[i], p + new Vector2(-28, 15), Color.White, .62f);
        }
    }

    private sealed class Graph(PreviewMasteryTree owner) : UIElement
    {
        protected override void DrawSelf(SpriteBatch sb)
        {
            if (owner.weaponBranch < 0) owner.DrawMainTree(sb); else owner.DrawWeaponTree(sb);
        }
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
