#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.UI;

namespace AICompanion.Companion.Inventory;

/// <summary>
/// The bag panel: a bordered box with a title and a fill count, five rows of ten slots in
/// a viewport, and a scrollbar for the other five. Slots use the game's own slot handling
/// in the bank context so drag, shift-click and stack splitting behave like a piggy bank,
/// through the single-item overloads: the array overload maps a bank slot to gamepad point
/// 400 + index, and the navigator's table stops at 40, so slots past it threw every frame
/// and never drew. After any interaction that leaves the cursor empty the bag re-sorts.
/// </summary>
public sealed class CompanionBagUI : UIState
{
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
    private bool sortPending;

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
        frame.Width.Set(innerWidth + 2f * Border, 0f);
        frame.Height.Set(innerHeight + 2f * Border, 0f);
        frame.HAlign = 0.8f;
        frame.VAlign = 0.45f;
        frame.SetPadding(0f);
        frame.BackgroundColor = new Color(24, 30, 66);
        frame.BorderColor = new Color(10, 12, 30);
        Append(frame);

        var panel = new UIPanel();
        panel.Left.Set(Border, 0f);
        panel.Top.Set(Border, 0f);
        panel.Width.Set(innerWidth, 0f);
        panel.Height.Set(innerHeight, 0f);
        panel.SetPadding(Pad);
        panel.BackgroundColor = new Color(63, 82, 151) * 0.9f;
        panel.BorderColor = new Color(24, 30, 66);
        frame.Append(panel);

        var title = new UIText("Companion's bag", 1f);
        title.Top.Set(2f, 0f);
        panel.Append(title);

        count = new UIText("", 0.8f);
        count.Top.Set(6f, 0f);
        count.HAlign = 1f;
        panel.Append(count);

        var viewport = new UIElement();
        viewport.Top.Set(TitleHeight, 0f);
        viewport.Width.Set(gridWidth, 0f);
        viewport.Height.Set(viewHeight, 0f);
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
        }

        scrollbar = new UIScrollbar();
        scrollbar.Top.Set(TitleHeight, 0f);
        scrollbar.Left.Set(gridWidth + Pad, 0f);
        scrollbar.Height.Set(viewHeight, 0f);
        scrollbar.SetView(viewHeight, rows * SlotSize);
        panel.Append(scrollbar);
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
        if (sortPending && Main.mouseItem.IsAir)
        {
            sortPending = false;
            bag.Sort();
        }
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
            Main.inventoryScale = SlotScale;
            Rectangle area = GetDimensions().ToRectangle();
            if (ContainsPoint(Main.MouseScreen) && !PlayerInput.IgnoreMouseInterface)
            {
                Main.LocalPlayer.mouseInterface = true;
                int typeBefore = items[index].type, stackBefore = items[index].stack;
                ItemSlot.Handle(ref items[index], ItemSlot.Context.BankItem);
                if (items[index].type != typeBefore || items[index].stack != stackBefore)
                    owner.sortPending = true;
            }
            ItemSlot.Draw(spriteBatch, ref items[index], ItemSlot.Context.BankItem, area.TopLeft());
            Main.inventoryScale = previous;
        }
    }
}
