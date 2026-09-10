#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI;

namespace AICompanion.Companion.ProfileCard;

/// <summary>A navigable design preview. Selection is local UI state, never an earned upgrade.</summary>
public sealed class PreviewMasteryTree : UIElement
{
    public readonly record struct Node(string Name, string Description, Vector2 Position, int Parent, bool Ability, int MaxTier);
    public static readonly string[] Branches = { "Tools", "Ranged", "Movement", "Magic", "Survival", "Support", "Gathering", "Melee" };
    public static readonly Node[] Nodes = BuildNodes();
    private readonly int[] tiers = new int[Nodes.Length];
    private int selected = -1, weaponBranch = -1;
    private Vector2 pan, mouseDown, panDown;
    private float zoom = .7f;
    private bool dragging;
    private Point fittedSize;
    private readonly int[,] weaponTiers = new int[8, 4];

    private Rectangle Canvas
    {
        get { var r = GetDimensions().ToRectangle(); r.Y += 28; r.Height = Math.Max(0, r.Height - 106); return r; }
    }
    private static Vector2 Mouse => Main.MouseScreen / Main.UIScale;
    private Vector2 Screen(Vector2 position) => Canvas.Center.ToVector2() + pan + position * zoom;

    public override void Recalculate()
    {
        base.Recalculate();
        Point size = new(Canvas.Width, Canvas.Height);
        if (Canvas.Width > 0 && Canvas.Height > 0 && size != fittedSize)
        {
            fittedSize = size;
            FitTree();
        }
    }

    private void FitTree()
    {
        pan = Vector2.Zero;
        // Include the outer branch labels in the initial view, including short viewports.
        zoom = Math.Clamp(Math.Min(Canvas.Width, Canvas.Height) / 610f, .15f, 1f);
    }

    public PreviewMasteryTree()
    {
        OverflowHidden = true;
        var viewport = new UIElement { OverflowHidden = true };
        viewport.Top.Set(28f, 0f); viewport.Width.Set(0f, 1f); viewport.Height.Set(-106f, 1f);
        var graph = new Graph(this); graph.Width.Set(0f, 1f); graph.Height.Set(0f, 1f);
        viewport.Append(graph); Append(viewport);
        OnLeftMouseDown += (_, _) =>
        {
            if (!Canvas.Contains(Mouse.ToPoint())) return;
            mouseDown = Mouse; panDown = pan; dragging = true;
        };
        OnLeftMouseUp += (_, _) =>
        {
            if (dragging && Vector2.DistanceSquared(Mouse, mouseDown) < 16f) SelectAt(Mouse);
            dragging = false;
        };
    }

    public override void Update(GameTime time)
    {
        base.Update(time);
        if (dragging && Main.mouseLeft) pan = panDown + Mouse - mouseDown;
        if (!Main.mouseLeft && !IsMouseHovering) dragging = false;
        if (IsMouseHovering) PlayerInput.LockVanillaMouseScroll("AICompanion/Mastery");
        if (!Main.mouseLeft || !Main.mouseLeftRelease) return;
        Rectangle r = GetDimensions().ToRectangle();
        if (new Rectangle(r.Right - 96, r.Y, 96, 24).Contains(Mouse.ToPoint()))
        { FitTree(); weaponBranch = -1; selected = -1; return; }
        if (!new Rectangle(r.Right - 154, r.Bottom - 34, 150, 30).Contains(Mouse.ToPoint())) return;
        if (weaponBranch >= 0)
        {
            if (selected >= 0 && selected < 4) weaponTiers[weaponBranch, selected] = Math.Min(5, weaponTiers[weaponBranch, selected] + 1);
        }
        else if (selected >= 0 && CanPreview(selected))
            tiers[selected] = Math.Min(Nodes[selected].MaxTier, tiers[selected] + 1);
    }

    public override void ScrollWheel(UIScrollWheelEvent evt)
    {
        if (!Canvas.Contains(Mouse.ToPoint())) return;
        Vector2 at = (Mouse - Canvas.Center.ToVector2() - pan) / zoom;
        zoom = Math.Clamp(zoom * (evt.ScrollWheelValue > 0 ? 1.12f : .89f), .15f, 1.6f);
        pan = Mouse - Canvas.Center.ToVector2() - at * zoom;
    }

    private void SelectAt(Vector2 mouse)
    {
        if (weaponBranch >= 0)
        {
            for (int i = 0; i < 4; i++)
                if (Vector2.DistanceSquared(mouse, Screen(new Vector2((i - 1.5f) * 110, 0))) < 24 * 24) selected = i;
            return;
        }
        for (int i = 0; i < Nodes.Length; i++)
        {
            if (Vector2.DistanceSquared(mouse, Screen(Nodes[i].Position)) > 20 * 20) continue;
            if (selected == i && Nodes[i].Ability && i / 6 is 1 or 3 or 7)
            { weaponBranch = i / 6; selected = -1; pan = Vector2.Zero; }
            else selected = i;
            return;
        }
    }

    protected override void DrawSelf(SpriteBatch sb)
    {
        Rectangle r = GetDimensions().ToRectangle();
        Fill(sb, r, new Color(24, 32, 60));
        Text(sb, "Mastery preview  •  Drag to pan, scroll to zoom", new Vector2(r.X + 8, r.Y + 5), Color.LightSteelBlue, .65f);
        Text(sb, "Reset view", new Vector2(r.Right - 92, r.Y + 5), Color.Gold, .7f);
        // The graph is a child of an OverflowHidden viewport. Terraria flushes its
        // sprite batch around that clip; changing scissor state inside DrawSelf
        // alone would not clip sprites queued in a deferred batch.
        Fill(sb, new Rectangle(r.X, r.Bottom - 76, r.Width, 76), new Color(39, 51, 92));
        string title = "Select a node to inspect its tiers";
        string detail = "Preview only. No stats, skill points or materials are changed.";
        bool canUpgrade = false;
        if (weaponBranch >= 0 && selected >= 0)
        { title = $"{WeaponUpgrades[selected]}  {weaponTiers[weaponBranch, selected]}/5"; canUpgrade = weaponTiers[weaponBranch, selected] < 5; }
        else if (weaponBranch < 0 && selected >= 0)
        {
            Node n = Nodes[selected]; title = $"{n.Name}  {tiers[selected]}/{n.MaxTier}";
            detail = n.Description;
            canUpgrade = tiers[selected] < n.MaxTier && CanPreview(selected);
        }
        Text(sb, title, new Vector2(r.X + 10, r.Bottom - 67), Color.Gold, .8f);
        Text(sb, detail, new Vector2(r.X + 10, r.Bottom - 43), Color.LightSteelBlue, .6f);
        Text(sb, "No gameplay effects", new Vector2(r.X + 10, r.Bottom - 21), Color.Gray, .6f);
        Text(sb, canUpgrade ? "Preview next tier" : "Select / unlock path", new Vector2(r.Right - 150, r.Bottom - 26), canUpgrade ? Color.LightGreen : Color.Gray, .7f);
    }

    private void DrawMainTree(SpriteBatch sb)
    {
        for (int i = 0; i < Nodes.Length; i++)
        {
            Node n = Nodes[i];
            Line(sb, Screen(n.Parent < 0 ? Vector2.Zero : Nodes[n.Parent].Position), Screen(n.Position), tiers[i] > 0 ? Color.LightGreen * .7f : Color.SlateGray * .6f);
            // Shared middle paths connect neighbouring branches without requiring both.
            if (i % 6 == 2) Line(sb, Screen(n.Position), Screen(Nodes[((i / 6 + 1) % 8) * 6 + 2].Position), Color.SlateGray * .35f);
        }
        for (int i = 0; i < Nodes.Length; i++)
        {
            Node n = Nodes[i]; bool available = CanPreview(i);
            DrawNode(sb, Screen(n.Position), n.Ability, selected == i ? Color.White : tiers[i] > 0 ? Color.LightGreen : available ? Color.Gold : Color.SlateGray);
            if (i % 6 == 5)
            {
                Vector2 p = Screen(n.Position * 1.12f);
                Text(sb, Branches[i / 6], p - new Vector2(24, 6), Color.LightSteelBlue, .65f);
            }
        }
        DrawNode(sb, Screen(Vector2.Zero), true, Color.Gold);
    }
    private static readonly string[] WeaponUpgrades = { "Damage", "Attack speed", "Piercing", "Weapon mechanic" };
    private void DrawWeaponTree(SpriteBatch sb)
    {
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = Screen(new Vector2((i - 1.5f) * 110, 0));
            if (i > 0) Line(sb, Screen(new Vector2((i - 2.5f) * 110, 0)), p, Color.SlateGray);
            DrawNode(sb, p, i == 3, selected == i ? Color.White : weaponTiers[weaponBranch, i] > 0 ? Color.LightGreen : Color.Gold);
            Text(sb, WeaponUpgrades[i], p + new Vector2(-32, 22), Color.White, .6f);
        }
    }
    private static Node[] BuildNodes()
    {
        string[][] names = {
            new[] { "Tool reach", "Tool speed", "Careful work", "Vein reach", "Efficient tools", "Tool mastery" },
            new[] { "Ranged damage", "Ranged speed", "Projectile speed", "Piercing", "Bow mastery", "Ranged mastery" },
            new[] { "Movement speed", "Jump height", "Double jump", "Triple jump", "Dash", "Flight" },
            new[] { "Magic damage", "Mana reserve", "Mana recovery", "Spell reach", "Staff mastery", "Magic mastery" },
            new[] { "Maximum life", "Defence", "Recovery", "Breath reserve", "Swimming", "Resilience" },
            new[] { "Protection reach", "Threat awareness", "Revival speed", "Guard endurance", "Protective skill", "Support mastery" },
            new[] { "Pickup reach", "Cargo capacity", "Gathering speed", "Search reach", "Resource skill", "Gathering mastery" },
            new[] { "Melee damage", "Attack speed", "Melee reach", "Knockback", "Sword mastery", "Melee mastery" }
        };
        var result = new List<Node>();
        for (int b = 0; b < 8; b++)
        for (int n = 0; n < 6; n++)
        {
            float a = -MathF.PI / 2 + b * MathF.PI / 4 + (n % 2 == 0 ? -.05f : .05f);
            bool ability = b == 2 ? n >= 2 : b == 4 ? n == 4 : n >= 4;
            result.Add(new Node(names[b][n], ability ? "Ability unlock. Costs and progression requirements are undecided." : "Tier one opens the path. Later tiers will use upgrade requirements.",
                new Vector2(MathF.Cos(a), MathF.Sin(a)) * (55 + n * 40), n == 0 ? -1 : b * 6 + n - 1, ability, ability ? 1 : 5));
        }
        return result.ToArray();
    }
    private bool CanPreview(int i) => Nodes[i].Parent < 0 || tiers[Nodes[i].Parent] > 0
        || i % 6 == 2 && (tiers[((i / 6 + 1) % 8) * 6 + 2] > 0 || tiers[((i / 6 + 7) % 8) * 6 + 2] > 0);

    private sealed class Graph(PreviewMasteryTree owner) : UIElement
    {
        protected override void DrawSelf(SpriteBatch sb)
        {
            if (owner.weaponBranch < 0) owner.DrawMainTree(sb); else owner.DrawWeaponTree(sb);
        }
    }
    private static void DrawNode(SpriteBatch sb, Vector2 p, bool diamond, Color colour)
    {
        int sides = diamond ? 4 : 20; float radius = diamond ? 11 : 8;
        for (int i = 0; i < sides; i++)
        {
            float a = i * MathF.Tau / sides, b = (i + 1) * MathF.Tau / sides;
            Line(sb, p + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius, p + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius, colour);
        }
    }
    private static void Line(SpriteBatch sb, Vector2 a, Vector2 b, Color c)
    { Vector2 d = b - a; sb.Draw(TextureAssets.MagicPixel.Value, a, new Rectangle(0, 0, 1, 1), c, d.ToRotation(), Vector2.Zero, new Vector2(d.Length(), 1.5f), SpriteEffects.None, 0); }
    private static void Fill(SpriteBatch sb, Rectangle r, Color c) => sb.Draw(TextureAssets.MagicPixel.Value, r, c);
    private static void Text(SpriteBatch sb, string text, Vector2 p, Color c, float scale)
        => Utils.DrawBorderStringFourWay(sb, FontAssets.MouseText.Value, text, p.X, p.Y, c, Color.Black, Vector2.Zero, scale);
}
