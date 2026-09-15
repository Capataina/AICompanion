#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Map;
using Terraria.UI;
using AICompanion.Companion.PlayerIntegration;

namespace AICompanion.Companion.ProfileCard;

/// <summary>
/// The Mining list page: a scrolling grid of every ore this character has held, bright where the companion will
/// mine it and dim where it will leave it, whatever the mode; and beside it the hovered ore as it looks in the
/// wall, its name, and Will mine or Will leave. Skip marked and Only marked are the page's title-bar actions. A
/// click on an ore toggles its mark. There is no count line and no instruction sentence: the tile on the overview
/// carries the count, and the owner ruled on 15 September 2026 that information shown twice, or not needed at a
/// glance, is clutter.
///
/// <para>The grid shows what the list knows and nothing else, so it never spoils which ores a world or a mod holds
/// before the player has found one. The picture in the grid and in the preview is the ore's tile, not its item,
/// because the in-wall look is what the player has to recognise while digging.</para>
/// </summary>
public sealed class ShowMiningList : UIElement, ICardPage
{
    public const int Columns = 8;
    public const float GridWidth = Columns * CardRegions.SlotPitch - (CardRegions.SlotPitch - CardRegions.Slot);
    public const float ScrollbarLeft = GridWidth + 4, ScrollbarWidth = 20, PreviewLeft = ScrollbarLeft + ScrollbarWidth + 20;
    public const float PreviewPadding = 12, PreviewSwatch = 96;

    private static readonly Color WillMine = new(102, 221, 116), WillLeave = new(255, 107, 107);

    private readonly UIElement viewport;
    private readonly UIScrollbar scrollbar;
    private readonly UIPanel preview;
    private int hovered = -1, shown;

    public string Title => "Mining list";
    public IReadOnlyList<CardAction> Actions { get; }

    private static CompanionMiningList List => CompanionPreferences.Current.MiningList;

    public ShowMiningList()
    {
        Actions = new[]
        {
            new CardAction("Skip marked", () => List.Mode = MiningListMode.SkipMarked, () => List.Mode == MiningListMode.SkipMarked),
            new CardAction("Only marked", () => List.Mode = MiningListMode.OnlyMarked, () => List.Mode == MiningListMode.OnlyMarked),
        };
        viewport = new UIElement { OverflowHidden = true };
        viewport.Width.Set(GridWidth, 0); viewport.Height.Set(0, 1f);
        var grid = new OreGrid(this);
        grid.Width.Set(0, 1f); grid.Height.Set(0, 1f);
        viewport.Append(grid);
        Append(viewport);
        scrollbar = new UIScrollbar();
        scrollbar.Left.Set(ScrollbarLeft, 0); scrollbar.Width.Set(ScrollbarWidth, 0); scrollbar.Height.Set(0, 1f);
        Append(scrollbar);
        preview = new Preview(this) { BackgroundColor = DrawCardPrimitives.Panel, BorderColor = DrawCardPrimitives.Edge };
        preview.Left.Set(PreviewLeft, 0); preview.Width.Set(-PreviewLeft, 1f); preview.Height.Set(0, 1f);
        Append(preview);
    }

    /// <summary>The ore the preview is showing: the hovered one, else the last one hovered or clicked.</summary>
    public int Shown => shown;

    /// <summary>The on-screen box of the ore at <paramref name="index"/> in the list's known order, scrolled.</summary>
    public Rectangle TileBounds(int index)
    {
        CalculatedStyle v = viewport.GetDimensions();
        return new Rectangle((int)(v.X + index % Columns * CardRegions.SlotPitch),
            (int)(v.Y + index / Columns * CardRegions.SlotPitch - scrollbar.GetValue()), (int)CardRegions.Slot, (int)CardRegions.Slot);
    }

    private int TileAt(Vector2 point)
    {
        if (!viewport.ContainsPoint(point)) return -1;
        for (int i = 0; i < List.Known.Count; i++)
            if (TileBounds(i).Contains(point.ToPoint())) return i;
        return -1;
    }

    public override void Recalculate()
    {
        base.Recalculate();
        if (scrollbar == null) return;
        int rows = (List.Known.Count + Columns - 1) / Columns;
        float height = viewport.GetDimensions().Height;
        scrollbar.SetView(height, Math.Max(height, rows * CardRegions.SlotPitch - (CardRegions.SlotPitch - CardRegions.Slot)));
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        hovered = TileAt(Main.MouseScreen);
        if (hovered >= 0) shown = hovered;
        if (shown >= List.Known.Count) shown = 0;
        if (IsMouseHovering)
        {
            Main.LocalPlayer.mouseInterface = true;
            PlayerInput.LockVanillaMouseScroll("AICompanion/MiningList");
        }
    }

    public override void LeftClick(UIMouseEvent evt)
    {
        base.LeftClick(evt);
        ToggleMark(TileAt(evt.MousePosition));
    }

    /// <summary>What a click on an ore does: flip its mark in the live list the brain reads.</summary>
    public void ToggleMark(int index)
    {
        if (index < 0 || index >= List.Known.Count) return;
        int type = List.Known[index];
        List.SetMarked(type, !List.IsMarked(type));
        shown = index;
    }

    public override void ScrollWheel(UIScrollWheelEvent evt)
    {
        base.ScrollWheel(evt);
        scrollbar.ViewPosition -= evt.ScrollWheelValue;
    }

    /// <summary>An ore's name for the preview: the game's map name for its tile, else its tile id name.</summary>
    public static string OreName(int type)
    {
        string name = Lang.GetMapObjectName(MapHelper.TileToLookup(type, 0));
        if (!string.IsNullOrWhiteSpace(name)) return name;
        return TileID.Search.TryGetName(type, out string id) ? id : "Ore";
    }

    /// <summary>
    /// A square patch of the ore's own tile texture, <paramref name="across"/> tiles to a side, each tile the texture's
    /// fully surrounded frame, which is how a vein reads in a wall.
    /// </summary>
    public static void DrawTileSwatch(SpriteBatch sb, int type, Rectangle area, int across, Color tint)
    {
        Main.instance?.LoadTiles(type);
        var asset = type < TextureAssets.Tile.Length ? TextureAssets.Tile[type] : null;
        if (asset?.IsLoaded != true) return;
        int cell = area.Width / across;
        for (int y = 0; y < across; y++)
            for (int x = 0; x < across; x++)
                sb.Draw(asset.Value, new Rectangle(area.X + x * cell, area.Y + y * cell, cell, cell), new Rectangle(18, 18, 16, 16), tint);
    }

    private sealed class OreGrid(ShowMiningList page) : UIElement
    {
        protected override void DrawSelf(SpriteBatch sb)
        {
            var list = List;
            float previous = Main.inventoryScale;
            try
            {
                Main.inventoryScale = CardRegions.Slot / TextureAssets.InventoryBack.Value.Width;
                for (int i = 0; i < list.Known.Count; i++)
                {
                    Rectangle area = page.TileBounds(i);
                    int type = list.Known[i];
                    bool mines = list.Allows(type), marked = list.IsMarked(type);
                    sb.Draw(TextureAssets.InventoryBack.Value, area, Color.White);
                    DrawTileSwatch(sb, type, new Rectangle(area.X + 8, area.Y + 8, 32, 32), 1, Color.White * (mines ? 1f : .35f));
                    Color edge = page.hovered == i ? Color.Gold : DrawCardPrimitives.Edge * .75f;
                    DrawCardPrimitives.Fill(sb, new Rectangle(area.X + 5, area.Y, area.Width - 10, 1), edge);
                    DrawCardPrimitives.Fill(sb, new Rectangle(area.X + 5, area.Bottom - 1, area.Width - 10, 1), edge);
                    DrawCardPrimitives.Fill(sb, new Rectangle(area.X, area.Y + 5, 1, area.Height - 10), edge);
                    DrawCardPrimitives.Fill(sb, new Rectangle(area.Right - 1, area.Y + 5, 1, area.Height - 10), edge);
                    if (!marked) continue;
                    // The mark says what it means in the current mode: a red cross where marked ores are skipped, a gold tick
                    // where only marked ores are mined. Drawn as lines, because the game's font has neither glyph.
                    var corner = new Vector2(area.Right - 13, area.Y + 4);
                    if (list.Mode == MiningListMode.SkipMarked)
                    {
                        DrawCardPrimitives.Line(sb, corner, corner + new Vector2(8, 8), WillLeave, 2);
                        DrawCardPrimitives.Line(sb, corner + new Vector2(0, 8), corner + new Vector2(8, 0), WillLeave, 2);
                    }
                    else
                    {
                        DrawCardPrimitives.Line(sb, corner + new Vector2(0, 4), corner + new Vector2(3, 8), Color.Gold, 2);
                        DrawCardPrimitives.Line(sb, corner + new Vector2(3, 8), corner + new Vector2(9, 0), Color.Gold, 2);
                    }
                }
            }
            finally { Main.inventoryScale = previous; }
        }
    }

    private sealed class Preview(ShowMiningList page) : UIPanel
    {
        protected override void DrawSelf(SpriteBatch sb)
        {
            base.DrawSelf(sb);
            var list = List;
            if (list.Known.Count == 0) return;
            int type = list.Known[Math.Clamp(page.shown, 0, list.Known.Count - 1)];
            Rectangle r = GetDimensions().ToRectangle();
            var swatch = new Rectangle(r.X + (int)PreviewPadding, r.Y + (int)PreviewPadding, (int)PreviewSwatch, (int)PreviewSwatch);
            DrawTileSwatch(sb, type, swatch, 3, Color.White);
            var frame = swatch; frame.Inflate(2, 2);
            DrawCardPrimitives.Fill(sb, new Rectangle(frame.X, frame.Y, frame.Width, 2), DrawCardPrimitives.Edge);
            DrawCardPrimitives.Fill(sb, new Rectangle(frame.X, frame.Bottom - 2, frame.Width, 2), DrawCardPrimitives.Edge);
            DrawCardPrimitives.Fill(sb, new Rectangle(frame.X, frame.Y, 2, frame.Height), DrawCardPrimitives.Edge);
            DrawCardPrimitives.Fill(sb, new Rectangle(frame.Right - 2, frame.Y, 2, frame.Height), DrawCardPrimitives.Edge);
            float y = swatch.Bottom + PreviewPadding;
            DrawCardPrimitives.Text(sb, OreName(type), new Vector2(swatch.X, y), Color.White, 1f);
            bool mines = list.Allows(type);
            DrawCardPrimitives.Text(sb, mines ? "Will mine" : "Will leave", new Vector2(swatch.X, y + 30), mines ? WillMine : WillLeave, .8f);
        }
    }
}
