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
using Terraria.ModLoader;
using Terraria.UI;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.Inventory;
using AICompanion.Companion.PlayerIntegration;

namespace AICompanion.Companion.ProfileCard;

/// <summary>Which body the card is showing.</summary>
public enum CardPage { Overview, Inventory, Mastery, MiningList }

/// <summary>
/// Owns the companion card: one panel whose body is either the short overview (title bar, identity strip with the
/// work controls, three tiles) or one full-height page (Inventory, Mastery, Mining list), swapped in place so the
/// title bar never moves. A page's own actions sit in the title bar, right-aligned before the close button. Back
/// exists only on a page; the overview's title bar holds the title and close, because a back button with nothing to
/// go back to did nothing.
/// </summary>
public sealed class CompanionProfileCardSystem : ModSystem
{
    private UserInterface? ui;
    private CompanionProfileCard? state;
    private GameTime? lastTime;
    private Vector2? position;
    private bool openedPlayerInventory;

    public static bool IsOpen { get; private set; }
    public static bool InventoryOpen => IsOpen && ModContent.GetInstance<CompanionProfileCardSystem>().state?.InventoryVisible == true;
    public static void OpenInventory()
    {
        var system = ModContent.GetInstance<CompanionProfileCardSystem>();
        if (!IsOpen) system.Open();
        system.state?.ShowInventory();
    }

    public override void Load() => ui = new UserInterface();

    public override void Unload()
    {
        Close();
        ui = null;
        position = null;
        lastTime = null;
        DrawCardPrimitives.ReleaseMasks();
    }

    public override void OnWorldUnload() => Close();

    public static void Toggle()
    {
        var system = ModContent.GetInstance<CompanionProfileCardSystem>();
        if (IsOpen) system.Close(); else system.Open();
    }

    public static void CloseOpenCard() => ModContent.GetInstance<CompanionProfileCardSystem>().Close();

    private void Open()
    {
        if (CompanionNPC.Find()?.ModNPC is not CompanionNPC)
            return;
        Brain.Infrastructure.Diagnostics.BrainOverlay.Close();
        state = new CompanionProfileCard(this);
        state.Activate();
        ui?.SetState(state);
        IsOpen = true;
    }

    private void Close()
    {
        if (openedPlayerInventory) Main.playerInventory = false;
        openedPlayerInventory = false;
        ui?.SetState(null);
        state = null;
        IsOpen = false;
    }

    public override void UpdateUI(GameTime gameTime)
    {
        lastTime = gameTime;
        // Escape is not read here: this runs before the tick's keyboard is sampled, so a close here came a tick after the
        // same press had already toggled the inventory. The card closes on the Inventory trigger in CompanionPlayer.SetControls.
        if (!IsOpen) return;
        ui?.Update(gameTime);
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int inventory = layers.FindIndex(layer => layer.Name == "Vanilla: Inventory");
        if (inventory >= 0 && IsOpen)
            layers[inventory] = new BlockCoveredInventoryInput(layers[inventory], () => state?.CoversPointer == true);
        int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
        if (index < 0) index = layers.Count;
        layers.Insert(index, new LegacyGameInterfaceLayer("AICompanion: Companion Profile", () =>
        {
            if (IsOpen && ui != null && lastTime != null)
                ui.Draw(Main.spriteBatch, lastTime);
            return true;
        }, InterfaceScaleType.UI));
    }

    /// <summary>The docked notch's bottom edge in UI units, or null while the notch is free of the screen edge.</summary>
    private static float? DockedNotchBottom()
    {
        var save = Main.LocalPlayer.GetModPlayer<CompanionPlayer>();
        return save.HealthBarPosition == null ? HeadsUpDisplay.CompanionHealthBar.Bounds(save).Bottom / Main.UIScale : null;
    }

    private sealed class CompanionProfileCard : UIState
    {
        private readonly CompanionProfileCardSystem owner;
        private UIPanel frame = null!;
        private UIElement titleBar = null!, footer = null!, content = null!;
        private DrawCompanionStatus identity = null!;
        private UITextPanel<string> back = null!, close = null!;
        private readonly List<(UITextPanel<string> Button, CardAction Action)> actions = new();
        private readonly PreviewMasteryTree mastery = new();
        private UIElement? pageElement;
        private CardPage page = CardPage.Overview;
        private bool dragging;
        private Vector2 dragOffset;
        private Point screenSize;
        public bool InventoryVisible => page == CardPage.Inventory;
        public bool CoversPointer => frame.ContainsPoint(Main.MouseScreen);

        public CompanionProfileCard(CompanionProfileCardSystem owner) => this.owner = owner;

        public override void OnInitialize()
        {
            frame = new UIPanel { BackgroundColor = DrawCardPrimitives.Panel, BorderColor = DrawCardPrimitives.Edge };
            frame.SetPadding(CardRegions.Padding);
            // An element's size is capped at its parent's by default, and the parent here is the screen. The card is a fixed
            // logical size by the mock, and on a screen shorter than a page the page's foot runs past the bottom edge; with
            // the default caps a 540-tall screen squashed every page to 540 and pulled its content up under the title bar.
            frame.MaxWidth.Set(float.MaxValue, 0);
            frame.MaxHeight.Set(float.MaxValue, 0);
            Append(frame);
            titleBar = new TitleBar(this);
            titleBar.Width.Set(0, 1); titleBar.Height.Set(CardRegions.TitleHeight, 0); frame.Append(titleBar);
            // Only a press on the bar itself drags. A press on back, close or any page action is that button's, and
            // must not move the card under the pointer.
            titleBar.OnLeftMouseDown += (evt, _) =>
            {
                if (evt.Target != titleBar) return;
                dragging = true; dragOffset = Mouse - frame.GetDimensions().Position();
            };
            titleBar.OnLeftMouseUp += (_, _) => dragging = false;
            back = DrawCardPrimitives.Button("<", CardRegions.RoundButton, ShowOverview);
            close = DrawCardPrimitives.Button("X", CardRegions.RoundButton, owner.Close);
            close.HAlign = 1; titleBar.Append(close);

            identity = new DrawCompanionStatus();
            identity.Top.Set(CardRegions.BodyTop, 0); identity.Width.Set(0, 1); identity.Height.Set(CardRegions.IdentityHeight, 0);
            footer = new UIElement();
            footer.Top.Set(CardRegions.TilesTop, 0); footer.Width.Set(0, 1); footer.Height.Set(CardRegions.TileHeight, 0);
            var targets = new[] { CardPage.Inventory, CardPage.Mastery, CardPage.MiningList };
            for (int i = 0; i < targets.Length; i++)
            {
                // Three equal tiles with a fixed gap: each is a third of the width less two thirds of a gap, and starts a
                // third of a gap further right than a plain third, so the last one ends exactly on the strip's edge.
                var tile = new StatusTile(this, targets[i]);
                tile.Left.Set(i * CardRegions.TileGap / 3f, i / 3f);
                tile.Width.Set(-2 * CardRegions.TileGap / 3f, 1 / 3f);
                tile.Height.Set(0, 1);
                CardPage target = targets[i];
                tile.OnLeftClick += (_, _) => Show(target);
                footer.Append(tile);
            }
            content = new UIElement();
            content.Top.Set(CardRegions.BodyTop, 0); content.Width.Set(0, 1); content.Height.Set(CardRegions.ContentHeight, 0);
            // The page's height is decided with its position, in Layout, which ShowOverview calls below.
            ShowOverview();
        }

        // UpdateUI and UI-scaled interface layers already receive SetZoom_UI input.
        private static Vector2 Mouse => Main.MouseScreen;
        private static Vector2 ViewportSize => PlayerInput.OriginalScreenSize / Main.UIScale;

        /// <summary>
        /// Where the card sits and how tall a page is. A card never dragged opens centred, a gap below a docked notch; a
        /// docked notch owns the top of the screen, so the title bar never rises closer to it than the clearance. The
        /// vertical clamp uses the overview's height, not the current page's, so opening a page grows the card downward and
        /// never moves its title bar. A page is the mock's height wherever that fits between the title bar and the bottom
        /// edge; where it does not, the page ends at the bottom edge and its content is shorter, because every page lays
        /// itself out from its content's height and scrolls what no longer fits, so nothing it offers is left off screen.
        /// A page is never shorter than the overview.
        /// </summary>
        private void Layout()
        {
            Vector2 view = ViewportSize;
            float? notch = DockedNotchBottom();
            float top = notch is float n ? n + CardRegions.NotchClearance : 0;
            Vector2 p = owner.position ?? new Vector2((view.X - CardRegions.Width) / 2, (notch ?? 0) + CardRegions.BelowNotch);
            p.X = Math.Clamp(p.X, 0, Math.Max(0, view.X - CardRegions.Width));
            p.Y = Math.Max(top, Math.Min(p.Y, Math.Max(0, view.Y - CardRegions.OverviewHeight)));
            owner.position = p;
            float pageHeight = Math.Min(CardRegions.PageHeight, Math.Max(CardRegions.OverviewHeight, view.Y - p.Y));
            content.Height.Set(pageHeight - CardRegions.PageOverhead, 0);
            frame.Width.Set(CardRegions.Width, 0); frame.Height.Set(page == CardPage.Overview ? CardRegions.OverviewHeight : pageHeight, 0);
            frame.Left.Set(p.X, 0); frame.Top.Set(p.Y, 0);
            Recalculate();
        }

        private void Show(CardPage next)
        {
            switch (next)
            {
                case CardPage.Inventory: ShowInventory(); break;
                case CardPage.Mastery: ShowPage(CardPage.Mastery, mastery); break;
                case CardPage.MiningList: ShowPage(CardPage.MiningList, new ShowMiningList()); break;
                default: ShowOverview(); break;
            }
        }

        public void ShowInventory()
        {
            // Native cursor stacks require item-management mode, even inside this card.
            if (!Main.playerInventory) owner.openedPlayerInventory = true;
            Main.playerInventory = true;
            var bag = new CompanionBagUI();
            bag.Activate();
            ShowPage(CardPage.Inventory, bag);
        }

        private void ShowOverview()
        {
            content.RemoveAllChildren();
            content.Remove();
            pageElement = null;
            page = CardPage.Overview;
            if (identity.Parent == null) frame.Append(identity);
            if (footer.Parent == null) frame.Append(footer);
            back.Remove();
            RebuildActions();
            Layout();
        }

        private void ShowPage(CardPage next, UIElement element)
        {
            identity.Remove();
            footer.Remove();
            content.RemoveAllChildren();
            if (content.Parent == null) frame.Append(content);
            element.Width.Set(0, 1f); element.Height.Set(0, 1f);
            content.Append(element);
            pageElement = element;
            page = next;
            if (back.Parent == null) titleBar.Append(back);
            RebuildActions();
            Layout();
        }

        /// <summary>Put the page's actions in the title bar, right-aligned so the last ends one gap before the close button.</summary>
        private void RebuildActions()
        {
            foreach (var (button, _) in actions) button.Remove();
            actions.Clear();
            if (pageElement is not ICardPage cardPage) return;
            float offset = -(CardRegions.RoundButton + CardRegions.ActionEndGap);
            foreach (CardAction action in cardPage.Actions.Reverse())
            {
                float width = action.Round ? CardRegions.RoundButton
                    : MathF.Ceiling(FontAssets.MouseText.Value.MeasureString(action.Label).X * .8f) + 28;
                var button = DrawCardPrimitives.Button(action.Label, width, action.Click);
                button.HAlign = 1; button.Left.Set(offset, 0);
                titleBar.Append(button);
                actions.Add((button, action));
                offset -= width + CardRegions.ActionGap;
            }
        }

        private string Title => pageElement is ICardPage cardPage ? cardPage.Title : "Companion";

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            Point size = ViewportSize.ToPoint();
            if (size != screenSize) { screenSize = size; Layout(); }
            if (dragging)
            {
                Main.LocalPlayer.mouseInterface = true;
                if (Main.mouseLeft) { owner.position = Mouse - dragOffset; Layout(); }
                else dragging = false;
            }
            if (frame.ContainsPoint(Mouse)) Main.LocalPlayer.mouseInterface = true;
            if (InventoryVisible) Main.playerInventory = true;
            if (CompanionNPC.Find()?.ModNPC is not CompanionNPC) { owner.Close(); return; }
            foreach (var (button, action) in actions)
            {
                if (action.Selected == null) continue;
                bool selected = action.Selected();
                button.TextColor = selected ? Color.Gold : Color.White;
                button.BackgroundColor = selected ? DrawCardPrimitives.Selected : DrawCardPrimitives.Panel;
                button.BorderColor = selected || button.IsMouseHovering ? Color.Gold : DrawCardPrimitives.Edge;
            }
        }

        private sealed class TitleBar(CompanionProfileCard card) : UIElement
        {
            protected override void DrawSelf(SpriteBatch sb)
            {
                var r = GetDimensions().ToRectangle();
                DrawCardPrimitives.Text(sb, card.Title, new Vector2(r.X + 42, r.Y + 3), Color.White, 1f);
                DrawCardPrimitives.Fill(sb, new Rectangle(r.X, r.Bottom, r.Width, 1), DrawCardPrimitives.Edge);
            }
        }

        /// <summary>A tile is a fill bar, a count and the page's name, and a click opens that page.</summary>
        private sealed class StatusTile(CompanionProfileCard card, CardPage target) : UIPanel
        {
            private static readonly Color Track = new(20, 24, 75);
            private static readonly Color Green = new(140, 240, 140);

            protected override void DrawSelf(SpriteBatch sb)
            {
                BackgroundColor = DrawCardPrimitives.Panel;
                BorderColor = IsMouseHovering ? Color.Gold : DrawCardPrimitives.Edge;
                base.DrawSelf(sb);
                Rectangle r = GetDimensions().ToRectangle();
                var (fill, count, colour, name) = Reading(card, target);
                var bar = new Rectangle(r.X + 10, r.Y + 10, r.Width - 74, 6);
                DrawCardPrimitives.Fill(sb, bar, Track);
                DrawCardPrimitives.Fill(sb, bar with { Width = (int)(bar.Width * Math.Clamp(fill, 0, 1)) }, colour);
                Vector2 countSize = FontAssets.MouseText.Value.MeasureString(count) * .65f;
                DrawCardPrimitives.Text(sb, count, new Vector2(r.Right - 10 - countSize.X, r.Y + 4), DrawCardPrimitives.Muted, .65f);
                DrawCardPrimitives.Text(sb, name, new Vector2(r.X + 10, r.Y + 23), Color.White, .85f);
            }

            private static (float Fill, string Count, Color Colour, string Name) Reading(CompanionProfileCard card, CardPage target)
            {
                switch (target)
                {
                    case CardPage.Inventory:
                        var bag = Main.LocalPlayer.GetModPlayer<CompanionPlayer>().Bag;
                        return (bag.Count / (float)CompanionInventory.Slots, $"{bag.Count}/{CompanionInventory.Slots}", Green, "Inventory");
                    case CardPage.Mastery:
                        return (card.mastery.LearnedCount / (float)DefineMasteryGraph.Nodes.Length, $"{card.mastery.Spent} pts", Color.Gold, "Mastery");
                    default:
                        var list = CompanionPreferences.Current.MiningList;
                        int known = list.Known.Count, mined = list.Known.Count(list.Allows);
                        return (known == 0 ? 0 : mined / (float)known, $"{mined}/{known}", Color.Gold, "Mining list");
                }
            }
        }
    }
}
