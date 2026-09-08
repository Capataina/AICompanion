#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.UI;

namespace AICompanion.Inventory;

/// <summary>
/// The bag panel: a grid of item slots the player can take from and put into, using
/// the game's own slot handling in the bank context so drag, shift-click and stack
/// splitting behave like a piggy bank.
/// </summary>
public sealed class CompanionBagUI : UIState
{
    private const int Columns = 10;
    private const float SlotScale = 0.75f;
    private const float SlotSize = 52f * SlotScale;

    public override void OnInitialize()
    {
        int rows = CompanionInventory.Slots / Columns;
        var panel = new UIPanel();
        panel.Width.Set(Columns * SlotSize + 24f, 0f);
        panel.Height.Set(rows * SlotSize + 48f, 0f);
        panel.HAlign = 0.5f;
        panel.VAlign = 0.5f;
        Append(panel);

        var title = new UIText("Companion's bag", 0.9f);
        title.Top.Set(0f, 0f);
        panel.Append(title);

        Item[] items = Main.LocalPlayer.GetModPlayer<Players.CompanionPlayer>().Bag.Items;
        for (int i = 0; i < CompanionInventory.Slots; i++)
        {
            var slot = new BagSlot(items, i, SlotScale);
            slot.Left.Set((i % Columns) * SlotSize, 0f);
            slot.Top.Set(24f + (i / Columns) * SlotSize, 0f);
            panel.Append(slot);
        }
    }

    private sealed class BagSlot : UIElement
    {
        private readonly Item[] items;
        private readonly int index;
        private readonly float scale;

        public BagSlot(Item[] items, int index, float scale)
        {
            this.items = items;
            this.index = index;
            this.scale = scale;
            Width.Set(52f * scale, 0f);
            Height.Set(52f * scale, 0f);
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            float previous = Main.inventoryScale;
            Main.inventoryScale = scale;
            Rectangle area = GetDimensions().ToRectangle();
            if (ContainsPoint(Main.MouseScreen) && !PlayerInput.IgnoreMouseInterface)
            {
                Main.LocalPlayer.mouseInterface = true;
                ItemSlot.Handle(items, ItemSlot.Context.BankItem, index);
            }
            ItemSlot.Draw(spriteBatch, items, ItemSlot.Context.BankItem, index, area.TopLeft());
            Main.inventoryScale = previous;
        }
    }
}
