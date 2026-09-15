extern alias live;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using CardSystem = live::AICompanion.Companion.ProfileCard.CompanionProfileCardSystem;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;
using BrainOverlay = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay;

/// <summary>
/// One press of Escape closes the top-most companion panel — the card, or the brain inspector's chooser when no card is open —
/// and does nothing else, replayed in the order the game runs a tick. The inspector reads its input in <c>PreUpdate</c>, which
/// the replayed tick calls before <c>SetControls</c>, so a raw-key close put back there is seen closing the chooser a step
/// before the gate and leaving the press to toggle the inventory.
/// <c>Main.DoUpdate</c> updates the interface first (<c>UpdateUIStates</c>, Main.cs 16985), then samples the keyboard
/// (<c>DoUpdate_HandleInput</c>, 17135), then updates the player (<c>DoUpdateInWorld</c>, 17255), whose controls step calls
/// <c>PlayerLoader.SetControls</c> and then the inventory gate, Player.cs 23942-23954: on a fresh press of the Inventory
/// trigger, which is Escape unless rebound, it calls <c>ToggleInv</c>. The gate is copied here with <c>ToggleInv</c> counted
/// rather than run, because <c>ToggleInv</c> reaches capture and recipe state this shell never builds; the count is the
/// defect's signature, a toggle on the press that closed the card.
/// </summary>
internal static class VerifyEscapeClosesOneCompanionPanel
{
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    /// <summary>The card system the game's static entry points reach, registered and loaded once if no fixture has yet.</summary>
    internal static CardSystem RegisteredSystem()
    {
        var system = ModContent.GetInstance<CardSystem>();
        if (system != null) return system;
        system = new CardSystem();
        ContentInstance.Register(system);
        system.Load();
        return system;
    }

    public static void Run()
    {
        var system = RegisteredSystem();
        Player player = Main.LocalPlayer;
        var save = player.GetModPlayer<CompanionPlayer>();
        KeyboardState keys = Main.keyState;
        bool inventory = Main.playerInventory, control = player.controlInv, release = player.releaseInventory, dedicated = Main.dedServ, wasDead = player.dead, chooserWasOpen = BrainOverlay.Enabled;
        var triggers = PlayerInput.Triggers.Current.KeyStatus;
        bool hadTrigger = triggers.TryGetValue("Inventory", out bool trigger);
        var time = new GameTime();
        int toggles = 0;

        // A living player's tick copies the triggers into controlInv and then calls SetControls before the gate. A dead
        // player's returns into UpdateDead, which calls the mod's UpdateDead before copying the triggers and then runs the
        // same gate shape to open the in-game options (TryOpeningInGameOptionsBasedOnInput, 16011); the count stands for that.
        void Tick(bool escapeHeld)
        {
            system.UpdateUI(time);
            Main.keyState = escapeHeld ? new KeyboardState(Keys.Escape) : new KeyboardState();
            PlayerInput.Triggers.Current.Inventory = escapeHeld;
            BrainOverlay.CaptureInput();
            if (player.dead)
            {
                PlayerLoader.UpdateDead(player);
                player.controlInv = escapeHeld;
            }
            else
            {
                player.controlInv = escapeHeld;
                PlayerLoader.SetControls(player);
            }
            if (player.controlInv)
            {
                if (player.releaseInventory) toggles++;
                player.releaseInventory = false;
            }
            else player.releaseInventory = true;
        }

        using var hook = EnableModPlayerHooks.For("HookSetControls", save);
        using var deadHook = EnableModPlayerHooks.For("HookUpdateDead", save);
        try
        {
            Main.dedServ = true;
            var cases = new List<string>();
            foreach (bool dead in new[] { false, true })
            foreach (bool inventoryWasOpen in new[] { false, true })
                foreach (bool onInventoryPage in new[] { false, true })
                {
                    player.dead = dead;
                    string gate = dead ? "the in-game options" : "ToggleInv";
                    if (CardSystem.IsOpen) CardSystem.CloseOpenCard();
                    Main.playerInventory = inventoryWasOpen;
                    Main.keyState = new KeyboardState();
                    player.controlInv = false;
                    player.releaseInventory = true;
                    toggles = 0;
                    CardSystem.Toggle();
                    if (onInventoryPage) CardSystem.OpenInventory();
                    string name = $"{(dead ? "a dead player's" : "a living player's")} {(onInventoryPage ? "Inventory page" : "overview")} with the inventory {(inventoryWasOpen ? "open" : "closed")}";
                    Require(CardSystem.IsOpen, $"premise: the card must open for the {name} case");

                    Tick(escapeHeld: false);
                    Require(CardSystem.IsOpen && toggles == 0, $"{name}: premise: an idle tick leaves the card open and toggles nothing");
                    Tick(escapeHeld: true);
                    int onPress = toggles;
                    bool closedOnPress = !CardSystem.IsOpen;
                    Tick(escapeHeld: true);
                    Tick(escapeHeld: false);
                    Require(onPress == 0 && toggles == 0,
                        $"{name}: the press that closed the card also reached {gate} ({onPress} toggle(s) on the press, {toggles} by release)");
                    Require(closedOnPress, $"{name}: the card must close on the tick Escape is pressed, not a tick later");
                    Require(Main.playerInventory == inventoryWasOpen,
                        $"{name}: closing the card left the player's inventory {(Main.playerInventory ? "open" : "closed")}, where he had it {(inventoryWasOpen ? "open" : "closed")}");
                    Tick(escapeHeld: true);
                    Require(toggles == 1, $"{name}: premise: with the card closed, the next press must reach {gate}; it reached it {toggles} time(s)");
                    Tick(escapeHeld: false);
                    cases.Add(name);
                }

            // The inspector's chooser, alone and under the card. Alone, one press closes it and spends the press; under the card,
            // the card is on top, so the first press closes only the card and the second only the chooser.
            BrainOverlay.Close();
            foreach (bool dead in new[] { false, true })
            {
                player.dead = dead;
                string gate = dead ? "the in-game options" : "ToggleInv";
                string who = dead ? "a dead player's" : "a living player's";
                if (CardSystem.IsOpen) CardSystem.CloseOpenCard();
                Main.playerInventory = false;
                Main.keyState = new KeyboardState();
                player.controlInv = false;
                player.releaseInventory = true;
                toggles = 0;

                BrainOverlay.Enabled = true;
                Tick(escapeHeld: false);
                Require(BrainOverlay.Enabled && toggles == 0, $"{who} inspector chooser: premise: an idle tick leaves the chooser open and toggles nothing");
                Tick(escapeHeld: true);
                int onPress = toggles;
                bool closedOnPress = !BrainOverlay.Enabled;
                Tick(escapeHeld: true);
                Tick(escapeHeld: false);
                Require(closedOnPress, $"{who} inspector chooser: the chooser must close on the tick Escape is pressed");
                Require(onPress == 0 && toggles == 0,
                    $"{who} inspector chooser: the press that closed the chooser also reached {gate} ({onPress} toggle(s) on the press, {toggles} by release)");
                Tick(escapeHeld: true);
                Require(toggles == 1, $"{who} inspector chooser: premise: with nothing open the next press must reach {gate}; it reached it {toggles} time(s)");
                Tick(escapeHeld: false);
                cases.Add($"{who} inspector chooser");

                // Opening the card closes the chooser (CompanionProfileCardSystem.Open), so both are open only when the inspector
                // key is pressed with the card already up; the case opens them in that order.
                toggles = 0;
                CardSystem.Toggle();
                BrainOverlay.ToggleMenu();
                Require(CardSystem.IsOpen && BrainOverlay.Enabled, $"{who} card over the chooser: premise: both must be open");
                Tick(escapeHeld: false);
                Tick(escapeHeld: true);
                Require(!CardSystem.IsOpen && BrainOverlay.Enabled && toggles == 0,
                    $"{who} card over the chooser: the first press must close only the card (card open {CardSystem.IsOpen}, chooser open {BrainOverlay.Enabled}, {toggles} toggle(s))");
                Tick(escapeHeld: false);
                Tick(escapeHeld: true);
                Require(!BrainOverlay.Enabled && toggles == 0,
                    $"{who} card over the chooser: the second press must close only the chooser (chooser open {BrainOverlay.Enabled}, {toggles} toggle(s))");
                Tick(escapeHeld: false);
                cases.Add($"{who} card over the chooser");
            }
            Console.WriteLine($"escape: one press closes the top companion panel and reaches neither ToggleInv nor the dead player's options menu, held or released, and the next press does, in {cases.Count} cases: {string.Join("; ", cases)}");
        }
        finally
        {
            if (CardSystem.IsOpen) CardSystem.CloseOpenCard();
            Main.keyState = keys;
            Main.playerInventory = inventory;
            player.controlInv = control;
            player.releaseInventory = release;
            player.dead = wasDead;
            BrainOverlay.Enabled = chooserWasOpen;
            if (hadTrigger) triggers["Inventory"] = trigger; else triggers.Remove("Inventory");
            Main.dedServ = dedicated;
        }
    }
}
