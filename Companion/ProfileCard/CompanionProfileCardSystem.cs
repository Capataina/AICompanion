#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.Inventory;
using AICompanion.Companion.PlayerIntegration;
using AICompanion.Companion.Brain.Behaviours.Work;

namespace AICompanion.Companion.ProfileCard;

/// <summary>
/// Owns the native profile, cargo and mastery pages. Cargo keeps Terraria's bank-slot
/// interaction semantics inside the same card rather than opening a competing window.
/// </summary>
public sealed class CompanionProfileCardSystem : ModSystem
{
    private UserInterface? ui;
    private CompanionProfileCard? state;
    private GameTime? lastTime;

    public static bool IsOpen { get; private set; }
    public static bool CargoOpen => IsOpen && ModContent.GetInstance<CompanionProfileCardSystem>().state?.CargoVisible == true;
    public static void OpenCargo()
    {
        var system = ModContent.GetInstance<CompanionProfileCardSystem>();
        if (!IsOpen) system.Open();
        system.state?.ShowCargo();
    }

    public override void Load() => ui = new UserInterface();

    public override void Unload()
    {
        ui?.SetState(null);
        ui = null;
        state = null;
        lastTime = null;
        IsOpen = false;
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
        private UIText action = null!;
        private UIText health = null!;
        private readonly List<(UITextPanel<string> button, Func<bool> selected)> options = new();
        private readonly List<(UIElement element, string text)> hints = new();
        private UIElement content = null!;
        private UIPanel frame = null!;
        private readonly PreviewMasteryTree mastery = new();
        public bool CargoVisible { get; private set; }

        public CompanionProfileCard(CompanionProfileCardSystem owner) => this.owner = owner;

        public override void OnInitialize()
        {
            frame = new UIPanel();
            frame.Width.Set(-32f, 1f);
            frame.MaxWidth.Set(760f, 0f);
            frame.Height.Set(-48f, 1f);
            frame.MaxHeight.Set(570f, 0f);
            frame.HAlign = .5f;
            frame.VAlign = .5f;
            frame.SetPadding(12f);
            frame.BackgroundColor = new Color(33, 43, 79) * .97f;
            frame.BorderColor = new Color(104, 130, 187);
            Append(frame);

            var close = Button("X", 34f, () => owner.Close());
            close.HAlign = 1f;
            close.Left.Set(-4f, 0f);
            close.Top.Set(4f, 0f);
            frame.Append(close);

            var title = new UIText("Companion", .72f, true) { TextColor = Color.Gold };
            title.Left.Set(8f, 0f); title.Top.Set(4f, 0f); frame.Append(title);
            var back = Button("Profile", 82f, ShowOverview);
            back.Left.Set(0f, 0f); back.Top.Set(42f, 0f); frame.Append(back);
            var cargo = Button("Cargo", 82f, ShowCargo);
            cargo.Left.Set(88f, 0f); cargo.Top.Set(42f, 0f); frame.Append(cargo);
            var tree = Button("Mastery", 90f, ShowMastery);
            tree.Left.Set(176f, 0f); tree.Top.Set(42f, 0f); frame.Append(tree);
            content = new UIElement();
            content.Top.Set(84f, 0f); content.Width.Set(0f, 1f); content.Height.Set(-84f, 1f);
            frame.Append(content);
            ShowOverview();
        }

        private void ClearPage()
        {
            content.RemoveAllChildren(); options.Clear(); hints.Clear(); CargoVisible = false;
            frame.HAlign = .5f; frame.VAlign = .5f;
            frame.MaxHeight.Set(570f, 0f);
        }

        public void ShowCargo()
        {
            ClearPage(); CargoVisible = true;
            frame.MaxHeight.Set(350f, 0f); frame.VAlign = 1f;
            // Native cursor stacks require item-management mode, even inside this card.
            Main.playerInventory = true;
            var bag = new CompanionBagUI(embedded: true);
            bag.Width.Set(0f, 1f); bag.Height.Set(0f, 1f); bag.Activate(); content.Append(bag);
        }

        private void ShowMastery()
        {
            ClearPage();
            mastery.Width.Set(0f, 1f); mastery.Height.Set(0f, 1f);
            content.Append(mastery);
        }

        private void ShowOverview()
        {
            ClearPage();
            var identity = new UIPanel { BackgroundColor = new Color(39, 51, 92), BorderColor = new Color(77, 99, 154) };
            identity.Width.Set(162f, 0f); identity.Height.Set(-78f, 1f); identity.SetPadding(10f); content.Append(identity);
            var portrait = new CompanionPortrait();
            portrait.HAlign = .5f; portrait.Top.Set(10f, 0f);
            portrait.Width.Set(100f, 0f); portrait.Height.Set(90f, 0f); identity.Append(portrait);

            health = new UIText("", .7f) { TextColor = Color.LightGreen };
            health.HAlign = .5f; health.Top.Set(104f, 0f); identity.Append(health);
            action = new UIText("", .65f);
            var now = new UIText("Right now", .75f) { TextColor = Color.Gold };
            now.Top.Set(142f, 0f); identity.Append(now);
            action.Top.Set(169f, 0f);
            action.Width.Set(0f, 1f);
            action.IsWrapped = true;
            identity.Append(action);

            var bag = Button("Cargo bag", 180f, ShowCargo);
            bag.Left.Set(0f, 0f);
            bag.Top.Set(-66f, 1f); content.Append(bag);
            var tree = Button("Mastery tree", 180f, ShowMastery);
            tree.Left.Set(190f, 0f); tree.Top.Set(-66f, 1f); content.Append(tree);
            var note = new UIText("Protection and survival are always active.", .65f) { TextColor = Color.LightSteelBlue };
            note.Top.Set(-22f, 1f); content.Append(note);

            var list = new UIList { ListPadding = 6f };
            list.Left.Set(172f, 0f);
            list.Width.Set(-196f, 1f);
            list.Height.Set(-78f, 1f);
            content.Append(list);
            var scrollbar = new UIScrollbar();
            scrollbar.HAlign = 1f; scrollbar.Height.Set(-78f, 1f);
            content.Append(scrollbar); list.SetScrollbar(scrollbar);

            AddOptions(list, "Following distance", "Useful jobs can travel farther than ordinary following.",
                new[] { "Close", "Standard", "Free" }, i => (int)CompanionPreferences.Current.DistanceMode == i,
                i => CompanionPreferences.Current.DistanceMode = (CompanionDistanceMode)i);
            AddPolicy(list, "Mining", p => p.Mining, (p, v) => p.Mining = v);
            AddPolicy(list, "Wood chopping", p => p.Chopping, (p, v) => p.Chopping = v);
            AddToggle(list, "Hunting", "Seek enemies when protection and useful work leave time.", p => p.Hunting, (p, v) => p.Hunting = v);
            AddToggle(list, "Break pots", "Break nearby reachable pots and collect their drops.", p => p.PotBreaking, (p, v) => p.PotBreaking = v);
            AddToggle(list, "Place torches", "Uses the companion's torches first, then yours. Protects homes.", p => p.TorchPlacement, (p, v) => p.TorchPlacement = v);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (frame.ContainsPoint(Main.MouseScreen / Main.UIScale)) Main.LocalPlayer.mouseInterface = true;
            if (CargoVisible) Main.playerInventory = true;
            CompanionNPC? companion = CompanionNPC.Find()?.ModNPC as CompanionNPC;
            if (companion == null) { owner.Close(); return; }
            string current = companion.IsDowned ? "Downed: stay nearby to revive" : companion.Brain.Reflexes.Active ?? companion.Brain.ActivityStatus;
            action.SetText(current);
            health.SetText($"Life {companion.NPC.life} / {companion.NPC.lifeMax}");
            foreach (var option in options)
            {
                bool selected = option.selected();
                option.button.TextColor = selected ? Color.Gold : Color.LightSteelBlue;
                option.button.BackgroundColor = selected ? new Color(66, 88, 151) : new Color(43, 59, 105);
                option.button.BorderColor = selected || option.button.IsMouseHovering ? Color.Gold : new Color(104, 130, 187);
            }
            foreach (var hint in hints)
                if (hint.element.IsMouseHovering) Main.instance.MouseText(hint.text);
        }

        private static UITextPanel<string> Button(string text, float width, Action onClick)
        {
            var button = new UITextPanel<string>(text, .8f, false);
            button.Width.Set(width, 0f);
            button.Height.Set(30f, 0f);
            button.SetPadding(4f);
            button.BackgroundColor = new Color(63, 82, 151) * .95f;
            button.BorderColor = new Color(104, 130, 187);
            button.OnMouseOver += (_, _) => button.BorderColor = Color.Gold;
            button.OnMouseOut += (_, _) => button.BorderColor = new Color(104, 130, 187);
            button.OnLeftClick += (_, _) => onClick();
            return button;
        }

        private void AddPolicy(UIList list, string label, Func<CompanionPreferences, WorkPolicy> get, Action<CompanionPreferences, WorkPolicy> set)
        {
            AddOptions(list, label, "Mimic: work when you do. Auto: find useful work nearby.", new[] { "Off", "Mimic", "Auto" },
                i => (int)get(CompanionPreferences.Current) == i, i => set(CompanionPreferences.Current, (WorkPolicy)i));
        }
        private void AddToggle(UIList list, string label, string hint, Func<CompanionPreferences, bool> get, Action<CompanionPreferences, bool> set)
        {
            AddOptions(list, label, hint, new[] { "Off", "On" }, i => get(CompanionPreferences.Current) == (i == 1),
                i => set(CompanionPreferences.Current, i == 1));
        }
        private void AddOptions(UIList list, string label, string hint, string[] values, Func<int, bool> selected, Action<int> select)
        {
            var row = new UIPanel { BackgroundColor = new Color(39, 51, 92), BorderColor = new Color(77, 99, 154) };
            row.Width.Set(0f, 1f); row.Height.Set(92f, 0f); row.SetPadding(8f);
            row.Append(new UIText(label, .8f) { TextColor = Color.White });
            for (int i = 0; i < values.Length; i++)
            {
                int index = i;
                var button = Button(values[i], 0f, () => select(index));
                button.Width.Set(-4f, 1f / values.Length);
                button.Left.Set(0f, i * 1f / values.Length);
                button.Top.Set(24f, 0f);
                row.Append(button); options.Add((button, () => selected(index)));
            }
            hints.Add((row, hint));
            var help = new UIText(label == "Following distance" ? "How freely your companion travels" : values.Length == 3 ? "Mimic follows your work. Auto finds its own." : "Optional companion activity", .6f) { TextColor = Color.LightSteelBlue };
            help.Top.Set(60f, 0f); row.Append(help);
            list.Add(row);
        }
    }

    private sealed class CompanionPortrait : UIElement
    {
        protected override void DrawSelf(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            if (CompanionNPC.Instance is not { } companion) return;
            var bounds = GetDimensions();
            // The map renderer already draws this stand-in player's appearance without changing
            // the NPC's world pose; the card must not borrow the Guide's entire sprite sheet.
            Main.MapPlayerRenderer.DrawPlayerHead(Main.Camera, companion.Body.Player,
                bounds.Position() + new Vector2(bounds.Width / 2, bounds.Height / 2), 1f, 1.6f, Color.White);
        }
    }
}
