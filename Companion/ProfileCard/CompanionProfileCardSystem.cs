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
/// Owns the companion card's one native UI state.  It is separate from the bag window so the
/// bag can continue using Terraria's bank-slot context while the card remains a small status and
/// preference surface.
/// </summary>
public sealed class CompanionProfileCardSystem : ModSystem
{
    private UserInterface? ui;
    private CompanionProfileCard? state;
    private GameTime? lastTime;

    public static bool IsOpen { get; private set; }

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
        if (CompanionBagSystem.IsOpen)
            CompanionBagSystem.Toggle();
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

        public CompanionProfileCard(CompanionProfileCardSystem owner) => this.owner = owner;

        public override void OnInitialize()
        {
            var frame = new UIPanel();
            frame.Width.Set(-32f, 1f);
            frame.MaxWidth.Set(620f, 0f);
            frame.Height.Set(-48f, 1f);
            frame.MaxHeight.Set(490f, 0f);
            frame.HAlign = .5f;
            frame.VAlign = .5f;
            frame.SetPadding(12f);
            frame.BackgroundColor = new Color(43, 48, 153) * .94f;
            frame.BorderColor = new Color(143, 147, 240);
            Append(frame);

            var close = Button("X", 34f, () => owner.Close());
            close.HAlign = 1f;
            close.Left.Set(-4f, 0f);
            close.Top.Set(4f, 0f);
            frame.Append(close);

            var portrait = new CompanionPortrait();
            portrait.Left.Set(4f, 0f);
            portrait.Top.Set(5f, 0f);
            portrait.Width.Set(54f, 0f);
            portrait.Height.Set(68f, 0f);
            frame.Append(portrait);

            var title = new UIText("Companion", .9f, true) { TextColor = Color.Gold };
            title.Left.Set(70f, 0f);
            title.Top.Set(4f, 0f);
            frame.Append(title);

            health = new UIText("", .7f) { TextColor = Color.LightGreen };
            health.Left.Set(70f, 0f);
            health.Top.Set(33f, 0f);
            frame.Append(health);
            action = new UIText("", .65f);
            action.Left.Set(70f, 0f);
            action.Top.Set(57f, 0f);
            action.Width.Set(-92f, 1f);
            action.IsWrapped = true;
            frame.Append(action);

            var bag = Button("Open cargo bag", 132f, () =>
            {
                owner.Close();
                if (!CompanionBagSystem.IsOpen) CompanionBagSystem.Toggle();
            });
            bag.Left.Set(0f, 0f);
            bag.Top.Set(-30f, 1f);
            frame.Append(bag);
            var note = new UIText("Guarding and survival stay automatic.", .52f) { TextColor = Color.LightSteelBlue };
            note.Left.Set(145f, 0f); note.Top.Set(-22f, 1f); frame.Append(note);

            var list = new UIList { ListPadding = 6f };
            list.Top.Set(94f, 0f);
            list.Width.Set(-24f, 1f);
            list.Height.Set(-138f, 1f);
            frame.Append(list);
            var scrollbar = new UIScrollbar();
            scrollbar.HAlign = 1f; scrollbar.Top.Set(94f, 0f); scrollbar.Height.Set(-138f, 1f);
            frame.Append(scrollbar); list.SetScrollbar(scrollbar);

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
            Main.LocalPlayer.mouseInterface = true; // card occupies input until its close/back path dismisses it
            CompanionNPC? companion = CompanionNPC.Find()?.ModNPC as CompanionNPC;
            if (companion == null) { owner.Close(); return; }
            string current = companion.IsDowned ? "Downed: stay nearby to revive" : companion.Brain.Reflexes.Active ?? companion.Brain.ActivityStatus;
            action.SetText(current);
            health.SetText($"Life {companion.NPC.life} / {companion.NPC.lifeMax}");
            foreach (var option in options)
            {
                bool selected = option.selected();
                option.button.TextColor = selected ? Color.Gold : Color.LightSteelBlue;
                option.button.BackgroundColor = selected ? new Color(59, 65, 189) : new Color(74, 78, 180);
                option.button.BorderColor = selected ? Color.Gold : new Color(95, 100, 200);
            }
        }

        private static UITextPanel<string> Button(string text, float width, Action onClick)
        {
            var button = new UITextPanel<string>(text, .72f, true);
            button.Width.Set(width, 0f);
            button.Height.Set(30f, 0f);
            button.SetPadding(4f);
            button.BackgroundColor = new Color(69, 90, 166) * .95f;
            button.BorderColor = new Color(180, 198, 255);
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
            var row = new UIPanel { BackgroundColor = new Color(26, 29, 105) * .9f, BorderColor = new Color(95, 100, 200) };
            row.Width.Set(0f, 1f); row.Height.Set(72f, 0f); row.SetPadding(8f);
            row.Append(new UIText(label, .72f) { TextColor = Color.White });
            for (int i = 0; i < values.Length; i++)
            {
                int index = i;
                var button = Button(values[i], 0f, () => select(index));
                button.Width.Set(-3f, .5f / values.Length);
                button.Left.Set(3f, .5f + i * .5f / values.Length);
                button.Top.Set(-2f, 0f);
                row.Append(button); options.Add((button, () => selected(index)));
            }
            var help = new UIText(hint, .49f) { TextColor = Color.LightSteelBlue, IsWrapped = true };
            help.Top.Set(34f, 0f); help.Width.Set(0f, 1f); row.Append(help);
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
