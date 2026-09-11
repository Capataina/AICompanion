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
using Terraria.ModLoader;
using Terraria.UI;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.Inventory;
using AICompanion.Companion.PlayerIntegration;
using AICompanion.Companion.Brain.Behaviours.Work;

namespace AICompanion.Companion.ProfileCard;

/// <summary>
/// Owns the native profile, inventory and mastery pages. Inventory keeps Terraria's bank-slot
/// interaction semantics inside the same card rather than opening a competing window.
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
        Brain.BehaviourDiagnostics.BrainOverlay.Close();
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
        if (!IsOpen) return;
        if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape))
        {
            Close();
            return;
        }
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

    private sealed class CompanionProfileCard : UIState
    {
        private readonly CompanionProfileCardSystem owner;
        private readonly List<(UITextPanel<string> button, Func<bool> selected)> options = new();
        private readonly List<(UIElement element, string text)> hints = new();
        private UIElement content = null!;
        private UIPanel frame = null!;
        private UIElement titleBar = null!, identity = null!, footer = null!;
        private UITextPanel<string> back = null!;
        private readonly PreviewMasteryTree mastery = new();
        private string page = "Companion";
        private bool dragging, minimised;
        private Vector2 dragOffset;
        private Point screenSize;
        public bool InventoryVisible { get; private set; }
        public bool CoversPointer => frame.ContainsPoint(Main.MouseScreen);

        public CompanionProfileCard(CompanionProfileCardSystem owner) => this.owner = owner;

        public override void OnInitialize()
        {
            frame = new UIPanel { BackgroundColor = DrawCardPrimitives.Panel, BorderColor = DrawCardPrimitives.Edge };
            frame.SetPadding(10f);
            Append(frame);
            titleBar = new TitleBar(this);
            titleBar.Width.Set(0, 1); titleBar.Height.Set(34, 0); frame.Append(titleBar);
            titleBar.OnLeftMouseDown += (evt, _) =>
            {
                if (evt.Target is UITextPanel<string> && !(page == "Companion" && evt.Target == back)) return;
                dragging = true; dragOffset = Mouse - frame.GetDimensions().Position();
            };
            titleBar.OnLeftMouseUp += (_, _) => dragging = false;
            back = Button("+", 30, () => { if (page != "Companion") ShowOverview(); });
            titleBar.Append(back);
            var minimise = Button("_", 30, () => { minimised = !minimised; Layout(); });
            minimise.HAlign = 1; minimise.Left.Set(-36, 0); titleBar.Append(minimise);
            var close = Button("X", 30, owner.Close);
            close.HAlign = 1; titleBar.Append(close);
            identity = new DrawCompanionStatus();
            identity.Width.Set(0, 1); identity.Top.Set(38, 0); frame.Append(identity);
            content = new UIElement();
            content.Width.Set(0, 1);
            frame.Append(content);
            footer = new UIElement(); footer.Width.Set(0, 1); frame.Append(footer);
            var inventoryTile = new StatusTile(this, false);
            inventoryTile.Width.Set(-5, .5f); inventoryTile.Height.Set(0, 1);
            inventoryTile.OnLeftClick += (_, _) => ShowInventory(); footer.Append(inventoryTile);
            var masteryTile = new StatusTile(this, true);
            masteryTile.Left.Set(5, .5f); masteryTile.Width.Set(-5, .5f); masteryTile.Height.Set(0, 1);
            masteryTile.OnLeftClick += (_, _) => ShowMastery(); footer.Append(masteryTile);
            ShowOverview();
        }

        // UpdateUI and UI-scaled interface layers already receive SetZoom_UI input.
        private static Vector2 Mouse => Main.MouseScreen;
        private static Vector2 ViewportSize => PlayerInput.OriginalScreenSize / Main.UIScale;

        private void Layout()
        {
            float w = Math.Min(780, ViewportSize.X - 24);
            float h = Math.Min(650, ViewportSize.Y - 24);
            frame.Width.Set(w, 0); frame.Height.Set(minimised ? 54 : h, 0);
            Vector2 p = owner.position ?? new Vector2((ViewportSize.X - w) / 2, (ViewportSize.Y - h) / 2);
            p.X = Math.Clamp(p.X, 0, Math.Max(0, ViewportSize.X - w));
            p.Y = Math.Clamp(p.Y, 0, Math.Max(0, ViewportSize.Y - (minimised ? 54 : h)));
            frame.Left.Set(p.X, 0); frame.Top.Set(p.Y, 0);
            float identityHeight = h < 550 ? 92 : 112;
            float footerHeight = page == "Companion" ? (h < 550 ? 78 : 100) : 56;
            identity.Height.Set(identityHeight, 0);
            content.Top.Set(44 + identityHeight, 0);
            content.Height.Set(h - 20 - 44 - identityHeight - footerHeight - 8, 0);
            footer.Top.Set(h - 20 - footerHeight, 0); footer.Height.Set(footerHeight, 0);
            foreach (UIElement child in new[] { identity, content, footer })
            {
                if (minimised) child.Remove();
                else if (child.Parent == null) frame.Append(child);
            }
            if (page == "Companion")
            {
                float available = content.Height.Pixels - 66;
                float rowHeight = Math.Min(46, Math.Max(30, (available - 20) / 6));
                foreach (var hint in hints)
                {
                    hint.element.Height.Set(rowHeight, 0);
                    foreach (UIElement cell in hint.element.Children) cell.Top.Set((rowHeight - 30) / 2, 0);
                }
            }
            Recalculate();
        }

        private void ClearPage()
        {
            content.RemoveAllChildren(); options.Clear(); hints.Clear(); InventoryVisible = false;
        }

        public void ShowInventory()
        {
            ClearPage(); InventoryVisible = true;
            page = "Inventory"; back.SetText("<");
            // Native cursor stacks require item-management mode, even inside this card.
            if (!Main.playerInventory) owner.openedPlayerInventory = true;
            Main.playerInventory = true;
            var bag = new CompanionBagUI();
            bag.Width.Set(0f, 1f); bag.Height.Set(0f, 1f); bag.Activate(); content.Append(bag);
            Layout();
        }

        private void ShowMastery()
        {
            ClearPage();
            page = "Mastery"; back.SetText("<");
            mastery.Width.Set(0f, 1f); mastery.Height.Set(0f, 1f);
            content.Append(mastery);
            Layout();
        }

        private void ShowOverview()
        {
            ClearPage();
            page = "Companion"; back.SetText("+");
            var heading = new CardLabel("Behaviour", .85f);
            heading.Width.Set(0, 1); heading.Height.Set(26, 0); content.Append(heading);
            var list = new UIList { ListPadding = 4f };
            list.Top.Set(28, 0); list.Width.Set(-24f, 1f); list.Height.Set(-66f, 1f);
            content.Append(list);
            var scrollbar = new UIScrollbar();
            scrollbar.Top.Set(28, 0); scrollbar.HAlign = 1f; scrollbar.Height.Set(-66f, 1f);
            content.Append(scrollbar); list.SetScrollbar(scrollbar);
            AddPolicy(list, "Mining", p => p.Mining, (p, v) => p.Mining = v);
            AddPolicy(list, "Chopping", p => p.Chopping, (p, v) => p.Chopping = v);
            AddToggle(list, "Hunting", "Seek enemies when protection and useful work leave time.", p => p.Hunting, (p, v) => p.Hunting = v);
            AddToggle(list, "Break pots", "Break nearby reachable pots and collect their drops.", p => p.PotBreaking, (p, v) => p.PotBreaking = v);
            AddToggle(list, "Place torches", "Uses the companion's torches first, then yours. Protects homes.", p => p.TorchPlacement, (p, v) => p.TorchPlacement = v);
            AddOptions(list, "Distance", "Useful jobs can travel farther than ordinary following.",
                new[] { "Close", "Standard", "Free" }, i => (int)CompanionPreferences.Current.DistanceMode == i,
                i => CompanionPreferences.Current.DistanceMode = (CompanionDistanceMode)i);
            var note = new CardLabel("Fighting, dodging and pickups are automatic. The action above explains the choice.", .66f);
            note.Width.Set(0, 1); note.Height.Set(27, 0); note.Top.Set(-27, 1); content.Append(note);
            Layout();
        }

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
            CompanionNPC? companion = CompanionNPC.Find()?.ModNPC as CompanionNPC;
            if (companion == null) { owner.Close(); return; }
            foreach (var option in options)
            {
                bool selected = option.selected();
                option.button.TextColor = selected ? Color.Gold : Color.White;
                option.button.BackgroundColor = selected ? DrawCardPrimitives.Selected : DrawCardPrimitives.Panel;
                option.button.BorderColor = selected || option.button.IsMouseHovering ? Color.Gold : DrawCardPrimitives.Edge;
            }
            foreach (var hint in hints)
                if (hint.element.IsMouseHovering) Main.instance?.MouseText(hint.text);
        }

        private static UITextPanel<string> Button(string text, float width, Action onClick)
            => DrawCardPrimitives.Button(text, width, onClick);

        private void AddPolicy(UIList list, string label, Func<CompanionPreferences, WorkPolicy> get, Action<CompanionPreferences, WorkPolicy> set)
        {
            AddOptions(list, label, "Mimic: work when you do. Auto: find useful work nearby.", new[] { "Off", "Mimic", "Auto" },
                i => (int)get(CompanionPreferences.Current) == i, i => set(CompanionPreferences.Current, (WorkPolicy)i));
        }
        private void AddToggle(UIList list, string label, string hint, Func<CompanionPreferences, bool> get, Action<CompanionPreferences, bool> set)
        {
            AddOptions(list, label, hint, new[] { "On", "Off" }, i => get(CompanionPreferences.Current) == (i == 0),
                i => set(CompanionPreferences.Current, i == 0));
        }
        private void AddOptions(UIList list, string label, string hint, string[] values, Func<int, bool> selected, Action<int> select)
        {
            var row = new UIElement();
            row.Width.Set(0f, 1f); row.Height.Set(34f, 0f);
            var caption = new CardLabel(label, .8f);
            caption.Top.Set(5, 0); caption.Width.Set(130, 0); caption.Height.Set(25, 0); row.Append(caption);
            var controls = new UIElement(); controls.Left.Set(136, 0); controls.Width.Set(-136, 1); controls.Height.Set(30, 0); row.Append(controls);
            for (int i = 0; i < values.Length; i++)
            {
                int index = i;
                var button = Button(values[i], 0f, () => select(index));
                button.Width.Set(-4f, 1f / 3);
                button.Left.Set(0f, i / 3f);
                controls.Append(button); options.Add((button, () => selected(index)));
            }
            hints.Add((row, hint));
            list.Add(row);
        }

        private sealed class TitleBar(CompanionProfileCard card) : UIElement
        {
            protected override void DrawSelf(SpriteBatch sb)
            {
                var r = GetDimensions().ToRectangle();
                DrawCardPrimitives.Text(sb, card.page, new Vector2(r.X + 42, r.Y + 3), Color.White, 1f);
                DrawCardPrimitives.Fill(sb, new Rectangle(r.X, r.Bottom, r.Width, 1), DrawCardPrimitives.Edge);
            }
        }

        private sealed class StatusTile(CompanionProfileCard card, bool isMastery) : UIPanel
        {
            protected override void DrawSelf(SpriteBatch sb)
            {
                BackgroundColor = DrawCardPrimitives.Panel;
                bool active = isMastery ? card.page == "Mastery" : card.InventoryVisible;
                BorderColor = active || IsMouseHovering ? Color.Gold : DrawCardPrimitives.Edge;
                base.DrawSelf(sb);
                Rectangle r = GetDimensions().ToRectangle();
                var bag = Main.LocalPlayer.GetModPlayer<CompanionPlayer>().Bag;
                int value = isMastery ? card.mastery.OpenedCount : bag.Count;
                int total = isMastery ? PreviewMasteryTree.Nodes.Length : CompanionInventory.Slots;
                var bar = new Rectangle(r.X + 10, r.Y + 10, r.Width - 80, 6);
                DrawCardPrimitives.Fill(sb, bar, new Color(20, 24, 75));
                bar.Width = bar.Width * value / total;
                DrawCardPrimitives.Fill(sb, bar, isMastery ? Color.Gold : Color.LightGreen);
                DrawCardPrimitives.Text(sb, $"{value}/{total}", new Vector2(r.Right - 63, r.Y + 4), DrawCardPrimitives.Muted, .65f);
                DrawCardPrimitives.Text(sb, (active ? "> " : "") + (isMastery ? "Mastery" : "Inventory"), new Vector2(r.X + 10, r.Y + 23), Color.White, .85f);
                if (r.Height < 70) return;
                string detail = isMastery ? "Explore the preview" : $"{value} of {total} slots occupied";
                DrawCardPrimitives.Text(sb, detail, new Vector2(r.X + 10, r.Y + 47), DrawCardPrimitives.Muted, .68f);
                if (r.Height >= 90)
                    DrawCardPrimitives.Text(sb, isMastery ? "No points or materials spent" : bag.LastPickup ?? "No recent pickup", new Vector2(r.X + 10, r.Y + 69), DrawCardPrimitives.Muted, .64f);
            }
        }
    }

    private sealed class CardLabel(string text, float scale) : UIElement
    {
        protected override void DrawSelf(SpriteBatch sb)
            => DrawCardPrimitives.WrappedText(sb, text, GetDimensions().ToRectangle(), DrawCardPrimitives.Muted, scale);
    }
}
