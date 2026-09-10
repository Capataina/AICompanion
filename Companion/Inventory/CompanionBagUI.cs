#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.UI;

namespace AICompanion.Companion.Inventory;

/// <summary>
/// The cargo page fits a scrollable grid to the available width. Slots use native handling
/// in the bank context so drag, shift-click and stack splitting behave like a piggy bank,
/// through the single-item overloads: the array overload maps a bank slot to gamepad point
/// 400 + index, and the navigator's table stops at 40, so slots past it threw every frame
/// and never drew. Explicit sorting preserves the arrangement during manual transfers.
/// </summary>
public sealed class CompanionBagUI : UIState
{
    private readonly bool embedded;
    public CompanionBagUI(bool embedded = false) => this.embedded = embedded;
    private const int Columns = 10;
    private const int VisibleRows = 5;
    private const float SlotScale = 0.85f;
    private const float SlotSize = 52f * SlotScale;
    private const float Border = 4f;
    private const float Pad = 10f;
    private const float TitleHeight = 28f;
    private const float ScrollbarWidth = 20f;

    private UIScrollbar scrollbar = null!;
    private UIElement grid = null!;
    private UIText count = null!;
    private CompanionInventory bag = null!;
    private UIElement viewport = null!;
    private readonly System.Collections.Generic.List<BagSlot> slots = new();

    public override void OnInitialize()
    {
        bag = Main.LocalPlayer.GetModPlayer<PlayerIntegration.CompanionPlayer>().Bag;
        int rows = CompanionInventory.Slots / Columns;

        float gridWidth = Columns * SlotSize;
        float viewHeight = VisibleRows * SlotSize;
        float innerWidth = Pad + gridWidth + Pad + ScrollbarWidth + Pad;
        float innerHeight = Pad + TitleHeight + viewHeight + Pad;

        // Two panels make the thick border: the outer one is the frame colour, the inner one the
        // usual inventory blue inset by the border width.
        var frame = new UIPanel();
        frame.Width.Set(embedded ? 0f : innerWidth + 2f * Border, embedded ? 1f : 0f);
        frame.Height.Set(embedded ? 0f : innerHeight + 2f * Border, embedded ? 1f : 0f);
        frame.HAlign = 0.8f;
        frame.VAlign = 0.45f;
        frame.SetPadding(0f);
        frame.BackgroundColor = new Color(24, 30, 66);
        frame.BorderColor = new Color(10, 12, 30);
        Append(frame);

        var panel = new UIPanel();
        panel.Left.Set(Border, 0f);
        panel.Top.Set(Border, 0f);
        panel.Width.Set(-2f * Border, 1f);
        panel.Height.Set(-2f * Border, 1f);
        panel.SetPadding(Pad);
        panel.BackgroundColor = new Color(63, 82, 151) * 0.9f;
        panel.BorderColor = new Color(24, 30, 66);
        frame.Append(panel);

        var title = new UIText("Cargo", .85f);
        title.Top.Set(2f, 0f);
        panel.Append(title);

        count = new UIText("", 0.8f);
        count.Top.Set(6f, 0f);
        count.HAlign = 1f;
        panel.Append(count);
        var sort = new UITextPanel<string>("Sort", .75f, false);
        sort.Left.Set(86f, 0f); sort.Top.Set(-3f, 0f);
        sort.Width.Set(70f, 0f); sort.Height.Set(26f, 0f); sort.SetPadding(3f);
        sort.OnLeftClick += (_, _) => bag.Sort(); panel.Append(sort);

        viewport = new UIElement();
        viewport.Top.Set(TitleHeight, 0f);
        viewport.Width.Set(-24f, 1f);
        viewport.Height.Set(-TitleHeight, 1f);
        viewport.OverflowHidden = true;
        panel.Append(viewport);

        grid = new UIElement();
        grid.Width.Set(gridWidth, 0f);
        grid.Height.Set(rows * SlotSize, 0f);
        viewport.Append(grid);

        for (int i = 0; i < CompanionInventory.Slots; i++)
        {
            var slot = new BagSlot(this, i);
            slot.Left.Set((i % Columns) * SlotSize, 0f);
            slot.Top.Set((i / Columns) * SlotSize, 0f);
            grid.Append(slot);
            slots.Add(slot);
        }

        scrollbar = new UIScrollbar();
        scrollbar.Top.Set(TitleHeight, 0f);
        scrollbar.HAlign = 1f;
        scrollbar.Height.Set(-TitleHeight, 1f);
        scrollbar.SetView(viewHeight, rows * SlotSize);
        panel.Append(scrollbar);
    }

    public override void Recalculate()
    {
        base.Recalculate();
        if (viewport == null || scrollbar == null) return;
        int columns = System.Math.Clamp((int)(viewport.GetInnerDimensions().Width / SlotSize), 1, Columns);
        float height = ((CompanionInventory.Slots + columns - 1) / columns) * SlotSize;
        grid.Width.Set(columns * SlotSize, 0f); grid.Height.Set(height, 0f);
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].Left.Set((i % columns) * SlotSize, 0f);
            slots[i].Top.Set((i / columns) * SlotSize, 0f);
        }
        scrollbar.SetView(viewport.GetInnerDimensions().Height, height);
        grid.Recalculate();
    }

    public override void ScrollWheel(UIScrollWheelEvent evt)
    {
        base.ScrollWheel(evt);
        scrollbar.ViewPosition -= evt.ScrollWheelValue;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (IsMouseHovering)
            PlayerInput.LockVanillaMouseScroll("AICompanion/Bag");
        grid.Top.Set(-scrollbar.GetValue(), 0f);
        grid.Recalculate();
        count.SetText($"{bag.Count} / {CompanionInventory.Slots}");
    }

    private sealed class BagSlot : UIElement
    {
        private readonly CompanionBagUI owner;
        private readonly int index;

        public BagSlot(CompanionBagUI owner, int index)
        {
            this.owner = owner;
            this.index = index;
            Width.Set(SlotSize, 0f);
            Height.Set(SlotSize, 0f);
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            Item[] items = owner.bag.Items;
            float previous = Main.inventoryScale;
            try
            {
                Main.inventoryScale = SlotScale;
                Rectangle area = GetDimensions().ToRectangle();
                Vector2 mouse = Main.MouseScreen / Main.UIScale;
                if (ContainsPoint(mouse) && owner.viewport.ContainsPoint(mouse) && !PlayerInput.IgnoreMouseInterface)
                {
                    Main.LocalPlayer.mouseInterface = true;
                    ItemSlot.Handle(ref items[index], ItemSlot.Context.BankItem);
                }
                ItemSlot.Draw(spriteBatch, ref items[index], ItemSlot.Context.BankItem, area.TopLeft());
            }
            finally { Main.inventoryScale = previous; }
        }
    }
}
