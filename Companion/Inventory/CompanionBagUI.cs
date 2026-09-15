#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.UI;
using AICompanion.Companion.ProfileCard;

namespace AICompanion.Companion.Inventory;

/// <summary>
/// The card's Inventory page: a top row of two bordered boxes, one holding the weapon slots and one the pickaxe and
/// axe, together spanning the page; one rhythm below, the bag's grid of fixed native slots with its scrollbar; and a
/// bottom line with the last transfer's message and the count. Terraria's chest buttons, Loot All, Deposit All,
/// Quick Stack and Restock, are the page's title-bar actions, with the game's names and the game's semantics
/// (<see cref="CompanionInventory"/>), and the bag sorts itself after every transfer.
///
/// <para>There is no picture of the companion, no caption on either box, no filter, no detail panel and no hover text
/// anywhere on the page, by the owner's rulings of 15 September 2026: information shown twice, or not needed at a
/// glance, is clutter. The slots therefore take the game's click handling without its hover tooltip.</para>
/// </summary>
public sealed class CompanionBagUI : UIState, ICardPage
{
    public const float SlotSize = CardRegions.Slot;
    private const float Pitch = CardRegions.SlotPitch;
    public const float GearBoxHeight = 64, GridLeft = 23, GridWidth = 620, ScrollbarLeft = 653, ScrollbarWidth = 20, FooterHeight = 24;
    public static float GridTop => GearBoxHeight + CardRegions.Rhythm;

    /// <summary>The two boxes' slots, in drawn order: the weapons, then the tools.</summary>
    public static readonly GearSlot[][] GearBoxes = { new[] { GearSlot.FirstWeapon, GearSlot.SecondWeapon }, new[] { GearSlot.Pickaxe, GearSlot.Axe } };

    private UIScrollbar scrollbar = null!;
    private UIElement viewport = null!, grid = null!;
    private CompanionInventory bag = null!;
    private CompanionGear gear = null!;
    private readonly List<BagSlot> slots = new();
    private int signature;
    private string message = "";

    public string Title => "Inventory";
    public IReadOnlyList<CardAction> Actions { get; }
    public UIElement[] Boxes { get; } = new UIElement[2];
    public string Message => message;

    public CompanionBagUI()
    {
        Actions = new[]
        {
            new CardAction("Loot All", () => Transfer(p => bag.LootAll(p), n => bag.Count == 0 ? "Looted everything" : $"Looted {n}; the rest has no room", "The bag is empty")),
            new CardAction("Deposit All", () => Transfer(p => bag.DepositAll(p), n => $"Deposited {n}", "Nothing to deposit")),
            new CardAction("Quick Stack", () => Transfer(p => bag.QuickStack(p), n => $"Quick stacked {n} into the bag", "Nothing you carry matches the bag")),
            new CardAction("Restock", () => Transfer(p => bag.Restock(p), n => $"Restocked {n} to you", "Nothing in the bag matches what you carry")),
        };
    }

    public override void OnInitialize()
    {
        var companionPlayer = Main.LocalPlayer.GetModPlayer<PlayerIntegration.CompanionPlayer>();
        bag = companionPlayer.Bag;
        gear = companionPlayer.Gear;
        // The gear row spans exactly what the bag below it spans, the grid's left edge to the scrollbar's right edge, as the
        // mock lays it out; with two weapon slots and two tools the two boxes are equal, one rhythm apart.
        float boxWidth = (ScrollbarLeft + ScrollbarWidth - GridLeft - CardRegions.Rhythm) / 2;
        var weapons = new GearBox(this, GearBoxes[0]);
        weapons.Left.Set(GridLeft, 0); weapons.Width.Set(boxWidth, 0); weapons.Height.Set(GearBoxHeight, 0);
        var tools = new GearBox(this, GearBoxes[1]);
        tools.Left.Set(GridLeft + boxWidth + CardRegions.Rhythm, 0); tools.Width.Set(boxWidth, 0); tools.Height.Set(GearBoxHeight, 0);
        Boxes[0] = weapons; Boxes[1] = tools;
        Append(weapons); Append(tools);
        viewport = new UIElement { OverflowHidden = true };
        viewport.Left.Set(GridLeft, 0); viewport.Top.Set(GridTop, 0); viewport.Width.Set(GridWidth, 0);
        viewport.Height.Set(-(GridTop + CardRegions.Rhythm + FooterHeight), 1f);
        Append(viewport);
        grid = new UIElement(); viewport.Append(grid);
        for (int i = 0; i < CompanionInventory.Slots; i++) slots.Add(new BagSlot(this, i));
        scrollbar = new UIScrollbar();
        scrollbar.Left.Set(ScrollbarLeft, 0); scrollbar.Top.Set(GridTop, 0); scrollbar.Width.Set(ScrollbarWidth, 0);
        scrollbar.Height.Set(-(GridTop + CardRegions.Rhythm + FooterHeight), 1f);
        Append(scrollbar);
        foreach (var slot in slots) grid.Append(slot);
    }

    /// <summary>The bottom line's count, what the page draws on the right: occupied slots against the bag's size.</summary>
    public string CountLine => $"{bag.Count} / {CompanionInventory.Slots}";
    public UIElement Grid => grid;
    public UIElement Viewport => viewport;
    public UIScrollbar Scrollbar => scrollbar;

    private void Transfer(Func<Player, int> operation, Func<int, string> moved, string nothing)
    {
        if (!Main.mouseItem.IsAir) { message = "Put down the cursor item first"; return; }
        int count = operation(Main.LocalPlayer);
        message = count > 0 ? moved(count) : nothing;
        Recalculate();
    }

    public override void Recalculate()
    {
        base.Recalculate();
        if (viewport == null) return;
        CalculatedStyle view = viewport.GetDimensions();
        int columns = Math.Max(1, (int)((GridWidth + Pitch - SlotSize) / Pitch));
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].Left.Set(i % columns * Pitch, 0);
            slots[i].Top.Set(i / columns * Pitch, 0);
        }
        float height = Math.Max(view.Height, (int)Math.Ceiling(slots.Count / (float)columns) * Pitch - (Pitch - SlotSize));
        grid.Width.Set(GridWidth, 0); grid.Height.Set(height, 0);
        scrollbar.SetView(view.Height, height);
        viewport.Recalculate(); scrollbar.Recalculate();
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
        next = unchecked(next * 31 + gear.Signature);
        if (next != signature) { signature = next; Recalculate(); }
        grid.Top.Set(-scrollbar.GetValue(), 0); grid.Recalculate();
    }

    protected override void DrawSelf(SpriteBatch sb)
    {
        Rectangle r = GetDimensions().ToRectangle();
        int footerY = r.Bottom - (int)FooterHeight;
        string count = CountLine;
        Vector2 countSize = FontAssets.MouseText.Value.MeasureString(count) * .8f;
        DrawCardPrimitives.Text(sb, count, new Vector2(r.Right - GridLeft - countSize.X, footerY + 3), DrawCardPrimitives.Muted, .8f);
        string last = message.Length > 0 ? message : bag.LastPickup ?? "";
        DrawCardPrimitives.Text(sb, last, new Vector2(r.X + GridLeft, footerY + 4), DrawCardPrimitives.Muted, .68f);
    }

    /// <summary>The game's slot handling without its hover tooltip: shift-click overrides, then the left and right clicks.</summary>
    private static void HandleWithoutTooltip(ref Item item)
    {
        ItemSlot.OverrideHover(ref item, ItemSlot.Context.BankItem);
        ItemSlot.LeftClick(ref item, ItemSlot.Context.BankItem);
        ItemSlot.RightClick(ref item, ItemSlot.Context.BankItem);
    }

    private static void DrawSlotEdge(SpriteBatch sb, Rectangle area, Color edge)
    {
        DrawCardPrimitives.Fill(sb, new Rectangle(area.X + 5, area.Y, area.Width - 10, 1), edge);
        DrawCardPrimitives.Fill(sb, new Rectangle(area.X + 5, area.Bottom - 1, area.Width - 10, 1), edge);
        DrawCardPrimitives.Fill(sb, new Rectangle(area.X, area.Y + 5, 1, area.Height - 10), edge);
        DrawCardPrimitives.Fill(sb, new Rectangle(area.Right - 1, area.Y + 5, 1, area.Height - 10), edge);
    }

    /// <summary>A bordered box of gear slots, the slots at the fixed slot size and evenly spaced across the box, centred top to bottom.</summary>
    private sealed class GearBox : UIPanel
    {
        public GearBox(CompanionBagUI owner, GearSlot[] contents)
        {
            BackgroundColor = DrawCardPrimitives.Panel;
            BorderColor = DrawCardPrimitives.Edge;
            SetPadding(0);
            foreach (GearSlot slot in contents)
            {
                var element = new GearSlotElement(owner, slot);
                element.Top.Set((GearBoxHeight - SlotSize) / 2, 0);
                Append(element);
            }
        }

        public override void Recalculate()
        {
            base.Recalculate();
            float width = GetDimensions().Width;
            int n = 0;
            foreach (UIElement _ in Children) n++;
            float spacing = (width - n * SlotSize) / (n + 1);
            int i = 0;
            foreach (UIElement child in Children)
            {
                child.Left.Set(MathF.Round(spacing + i * (SlotSize + spacing)), 0);
                child.Recalculate();
                i++;
            }
        }
    }

    /// <summary>
    /// One gear slot: a native item slot that asks the gear's predicate before letting the game's slot handling swap the
    /// cursor's item in. A refused item never reaches the handler, so it stays on the cursor and the slot dims to say so;
    /// taking an item out, or putting a slot's own item back, is always allowed. Bank context is used for the same reason
    /// the cargo slots use it: it neither stamps a hotbar shortcut nor treats the slot as armour.
    /// </summary>
    private sealed class GearSlotElement : UIElement
    {
        private readonly CompanionBagUI owner;
        private readonly GearSlot slot;
        public GearSlotElement(CompanionBagUI owner, GearSlot slot)
        {
            this.owner = owner; this.slot = slot;
            Width.Set(SlotSize, 0); Height.Set(SlotSize, 0);
        }

        protected override void DrawSelf(SpriteBatch sb)
        {
            Item[] items = owner.gear.Slots;
            int index = (int)slot;
            float previous = Main.inventoryScale;
            try
            {
                Main.inventoryScale = SlotSize / TextureAssets.InventoryBack.Value.Width;
                Rectangle area = GetDimensions().ToRectangle();
                bool refused = false, hovering = ContainsPoint(Main.MouseScreen) && !PlayerInput.IgnoreMouseInterface;
                if (hovering)
                {
                    Main.LocalPlayer.mouseInterface = true;
                    refused = !Main.mouseItem.IsAir && !CompanionGear.Accepts(slot, Main.mouseItem, out _);
                    if (!refused) HandleWithoutTooltip(ref items[index]);
                }
                // An item already in the slot that its predicate no longer accepts — a saved item whose mod has since
                // unloaded — is kept and drawn dim, the same dim a refused cursor item gets, so the player sees it is idle
                // without it being thrown away.
                bool idle = !items[index].IsAir && !CompanionGear.Accepts(slot, items[index], out _);
                Texture2D background = items[index].favorited ? TextureAssets.InventoryBack10.Value : TextureAssets.InventoryBack.Value;
                sb.Draw(background, area, refused || idle ? Color.White * .45f : Color.White);
                if (!items[index].IsAir)
                    ItemSlot.DrawItemIcon(items[index], ItemSlot.Context.BankItem, sb, area.Center.ToVector2(), Main.inventoryScale, 32f, refused || idle ? Color.White * .45f : Color.White);
                DrawSlotEdge(sb, area, hovering && !refused ? Color.Gold : refused ? Color.LightSalmon : DrawCardPrimitives.Edge * .75f);
            }
            finally { Main.inventoryScale = previous; }
        }
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
                bool hovering = ContainsPoint(mouse) && owner.viewport.ContainsPoint(mouse) && !PlayerInput.IgnoreMouseInterface;
                if (hovering)
                {
                    Main.LocalPlayer.mouseInterface = true;
                    HandleWithoutTooltip(ref items[Index]);
                }
                // Bank Draw selects brown art, and Inventory Draw assumes a player hotbar index and stamps a shortcut on
                // single-item slots, so the game's blue texture and icon path are used directly, keeping native item hooks.
                Texture2D background = items[Index].favorited ? TextureAssets.InventoryBack10.Value : TextureAssets.InventoryBack.Value;
                sb.Draw(background, area, Color.White);
                if (!items[Index].IsAir)
                {
                    ItemSlot.DrawItemIcon(items[Index], ItemSlot.Context.BankItem, sb, area.Center.ToVector2(), Main.inventoryScale, 32f, Color.White);
                    if (items[Index].stack > 1)
                        DrawCardPrimitives.Text(sb, items[Index].stack.ToString(), area.TopLeft() + new Vector2(10, 26) * Main.inventoryScale, Color.White, Main.inventoryScale, FontAssets.ItemStack.Value);
                }
                DrawSlotEdge(sb, area, hovering ? Color.Gold : DrawCardPrimitives.Edge * .75f);
            }
            finally { Main.inventoryScale = previous; }
        }
    }
}
