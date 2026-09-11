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
using Terraria.UI;
using AICompanion.Companion.ProfileCard;

namespace AICompanion.Companion.Inventory;

/// <summary>Fixed-size native bank slots, filtered without rearranging the backing inventory.</summary>
public sealed class CompanionBagUI : UIState
{
    // The native texture is 52 pixels. Scale its complete slot to the agreed 48 pixels;
    // the pitch includes a gap and never stretches to consume spare panel width.
    public const float SlotSize = 48;
    private const float Pitch = 52;
    private UIScrollbar scrollbar = null!;
    private UIElement viewport = null!, grid = null!, details = null!;
    private CompanionInventory bag = null!;
    private readonly List<BagSlot> slots = new();
    private readonly List<UITextPanel<string>> filters = new();
    private int filter, inspected = -1, signature;
    private string transferMessage = "";

    public override void OnInitialize()
    {
        bag = Main.LocalPlayer.GetModPlayer<PlayerIntegration.CompanionPlayer>().Bag;
        string[] names = { "All", "Ore", "Wood", "Loot" };
        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            var button = DrawCardPrimitives.Button(names[i], 58, () => { filter = index; scrollbar.ViewPosition = 0; Recalculate(); });
            button.Left.Set(i * 62, 0); Append(button); filters.Add(button);
        }
        viewport = new UIElement { OverflowHidden = true };
        viewport.Top.Set(38, 0); Append(viewport);
        grid = new UIElement(); viewport.Append(grid);
        for (int i = 0; i < CompanionInventory.Slots; i++) slots.Add(new BagSlot(this, i));
        scrollbar = new UIScrollbar(); scrollbar.Top.Set(38, 0); Append(scrollbar);
        details = new ItemDetails(this); details.HAlign = 1; details.Top.Set(38, 0); Append(details);
        var handOver = DrawCardPrimitives.Button("Hand everything over", 220, HandEverythingOver);
        handOver.HAlign = .5f; handOver.Top.Set(-32, 1); Append(handOver);
    }

    private static bool IsOre(Item item) => item.createTile > -1 && item.createTile < TileID.Sets.Ore.Length && TileID.Sets.Ore[item.createTile];
    private static bool IsWood(Item item) => RecipeGroup.recipeGroups.TryGetValue(RecipeGroupID.Wood, out var wood) && wood.ValidItems.Contains(item.type);
    private bool Matches(Item item) => filter == 0 || !item.IsAir && (filter == 1 ? IsOre(item) : filter == 2 ? IsWood(item) : !IsOre(item) && !IsWood(item));

    public override void Recalculate()
    {
        base.Recalculate();
        if (viewport == null) return;
        float width = GetInnerDimensions().Width;
        float detailWidth = width >= 560 ? 180 : 142;
        float gridWidth = Math.Max(SlotSize, width - detailWidth - 30);
        float gridHeight = Math.Max(SlotSize, MathF.Floor((GetInnerDimensions().Height - 76) / Pitch) * Pitch - 4);
        viewport.Width.Set(gridWidth, 0); viewport.Height.Set(gridHeight, 0);
        scrollbar.Left.Set(gridWidth + 4, 0); scrollbar.Height.Set(gridHeight, 0);
        details.Width.Set(detailWidth, 0); details.Height.Set(gridHeight, 0);
        int columns = Math.Max(1, (int)((gridWidth + Pitch - SlotSize) / Pitch));
        grid.RemoveAllChildren();
        int visible = 0;
        foreach (var slot in slots)
        {
            if (!Matches(bag.Items[slot.Index])) continue;
            slot.Left.Set(visible % columns * Pitch, 0); slot.Top.Set(visible / columns * Pitch, 0);
            grid.Append(slot); visible++;
        }
        float height = Math.Max(gridHeight, (int)Math.Ceiling(visible / (float)columns) * Pitch);
        grid.Width.Set(gridWidth, 0); grid.Height.Set(height, 0);
        scrollbar.SetView(gridHeight, height);
        viewport.Recalculate(); scrollbar.Recalculate(); details.Recalculate();
    }

    public override void ScrollWheel(UIScrollWheelEvent evt)
    {
        base.ScrollWheel(evt);
        scrollbar.ViewPosition -= evt.ScrollWheelValue;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (IsMouseHovering) PlayerInput.LockVanillaMouseScroll("AICompanion/Bag");
        int next = 17;
        foreach (Item item in bag.Items) next = unchecked(next * 31 + item.type);
        if (next != signature) { signature = next; Recalculate(); }
        grid.Top.Set(-scrollbar.GetValue(), 0); grid.Recalculate();
        for (int i = 0; i < filters.Count; i++)
        {
            filters[i].TextColor = i == filter ? Color.Gold : Color.White;
            filters[i].BackgroundColor = i == filter ? DrawCardPrimitives.Selected : DrawCardPrimitives.Panel;
        }
    }

    private void HandEverythingOver()
    {
        if (!Main.mouseItem.IsAir) { transferMessage = "Put down the cursor item first."; return; }
        Player player = Main.LocalPlayer;
        for (int i = 0; i < bag.Items.Length; i++)
        {
            if (bag.Items[i].IsAir) continue;
            // Native insertion returns what did not fit. Keep that remainder in
            // its original bag slot; a full player inventory never deletes an item.
            bag.Items[i] = player.GetItem(player.whoAmI, bag.Items[i], GetItemSettings.InventoryEntityToPlayerInventorySettings);
        }
        transferMessage = bag.Count == 0 ? "Everything handed over." : "The rest stays here until you have room.";
        Recalculate();
    }

    protected override void DrawSelf(SpriteBatch sb)
    {
        Rectangle r = GetDimensions().ToRectangle();
        DrawCardPrimitives.Text(sb, $"{bag.Count} / {CompanionInventory.Slots}", new Vector2(r.Right - 84, r.Y + 6), DrawCardPrimitives.Muted, .75f);
        if (filter != 0 && !slots.Exists(slot => Matches(bag.Items[slot.Index])))
            DrawCardPrimitives.WrappedText(sb, "No items in this category.", viewport.GetDimensions().ToRectangle(), DrawCardPrimitives.Muted);
    }

    private sealed class BagSlot : UIElement
    {
        private readonly CompanionBagUI owner;
        public int Index { get; }
        public BagSlot(CompanionBagUI owner, int index)
        {
            this.owner = owner; Index = index;
            Width.Set(SlotSize, 0); Height.Set(SlotSize, 0);
        }
        protected override void DrawSelf(SpriteBatch sb)
        {
            Item[] items = owner.bag.Items;
            float previous = Main.inventoryScale;
            try
            {
                Main.inventoryScale = SlotSize / TextureAssets.InventoryBack.Value.Width;
                Rectangle area = GetDimensions().ToRectangle();
                Vector2 mouse = Main.MouseScreen;
                if (ContainsPoint(mouse) && owner.viewport.ContainsPoint(mouse) && !PlayerInput.IgnoreMouseInterface)
                {
                    Main.LocalPlayer.mouseInterface = true;
                    if (!items[Index].IsAir) owner.inspected = Index;
                    ItemSlot.Handle(ref items[Index], ItemSlot.Context.BankItem);
                }
                // Bank Draw selects brown art. Inventory Draw assumes a player hotbar
                // index and stamps a shortcut on single-item slots. Use the game's
                // blue texture and icon path directly, retaining native item hooks.
                Texture2D background = items[Index].favorited ? TextureAssets.InventoryBack10.Value : TextureAssets.InventoryBack.Value;
                sb.Draw(background, area, Color.White);
                if (!items[Index].IsAir)
                {
                    ItemSlot.DrawItemIcon(items[Index], ItemSlot.Context.BankItem, sb, area.Center.ToVector2(), Main.inventoryScale, 32f, Color.White);
                    if (items[Index].stack > 1)
                        DrawCardPrimitives.Text(sb, items[Index].stack.ToString(), area.TopLeft() + new Vector2(10, 26) * Main.inventoryScale, Color.White, Main.inventoryScale, FontAssets.ItemStack.Value);
                }
                Color edge = owner.inspected == Index ? Color.Gold : DrawCardPrimitives.Edge * .75f;
                DrawCardPrimitives.Fill(sb, new Rectangle(area.X + 5, area.Y, area.Width - 10, 1), edge);
                DrawCardPrimitives.Fill(sb, new Rectangle(area.X + 5, area.Bottom - 1, area.Width - 10, 1), edge);
                DrawCardPrimitives.Fill(sb, new Rectangle(area.X, area.Y + 5, 1, area.Height - 10), edge);
                DrawCardPrimitives.Fill(sb, new Rectangle(area.Right - 1, area.Y + 5, 1, area.Height - 10), edge);
            }
            finally { Main.inventoryScale = previous; }
        }
    }

    private sealed class ItemDetails(CompanionBagUI owner) : UIPanel
    {
        protected override void DrawSelf(SpriteBatch sb)
        {
            BackgroundColor = DrawCardPrimitives.Panel; BorderColor = DrawCardPrimitives.Edge;
            base.DrawSelf(sb);
            Rectangle r = GetInnerDimensions().ToRectangle();
            Item? item = owner.inspected >= 0 ? owner.bag.Items[owner.inspected] : null;
            if (item == null || item.IsAir)
                DrawCardPrimitives.WrappedText(sb, "Point to an item to inspect it.\n\nUse the slots to move or split stacks.", r, DrawCardPrimitives.Muted, .72f);
            else
            {
                Color rarity = Terraria.GameContent.UI.ItemRarity.GetColor(item.rare);
                DrawCardPrimitives.WrappedText(sb, item.Name, new Rectangle(r.X, r.Y, r.Width, 40), rarity, .85f);
                DrawCardPrimitives.Text(sb, $"x{item.stack}", new Vector2(r.X, r.Y + 44), Color.White, .8f);
                if (IsOre(item) && r.Height >= 120 && owner.transferMessage.Length == 0)
                {
                    int labelY = r.Height > 140 ? 80 : 64;
                    DrawCardPrimitives.Text(sb, "In the wall", new Vector2(r.X, r.Y + labelY), DrawCardPrimitives.Muted, .7f);
                    Main.instance?.LoadTiles(item.createTile);
                    var tile = TextureAssets.Tile[item.createTile];
                    if (tile?.IsLoaded == true)
                        sb.Draw(tile.Value, new Rectangle(r.X, r.Y + labelY + 24, 32, 32), new Rectangle(18, 18, 16, 16), Color.White);
                }
            }
            if (owner.transferMessage.Length > 0 && r.Height >= 116)
                DrawCardPrimitives.WrappedText(sb, owner.transferMessage, new Rectangle(r.X, r.Bottom - 50, r.Width, 50), Color.LightGreen, .65f);
        }
    }
}
