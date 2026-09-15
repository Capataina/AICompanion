extern alias live;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ModLoader;
using CardSystem = live::AICompanion.Companion.ProfileCard.CompanionProfileCardSystem;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;

/// <summary>
/// One press of Escape with the card open closes the card and does nothing else, replayed in the order the game runs a tick.
/// <c>Main.DoUpdate</c> updates the interface first (<c>UpdateUIStates</c>, Main.cs 16985), then samples the keyboard
/// (<c>DoUpdate_HandleInput</c>, 17135), then updates the player (<c>DoUpdateInWorld</c>, 17255), whose controls step calls
/// <c>PlayerLoader.SetControls</c> and then the inventory gate, Player.cs 23942-23954: on a fresh press of the Inventory
/// trigger, which is Escape unless rebound, it calls <c>ToggleInv</c>. The gate is copied here with <c>ToggleInv</c> counted
/// rather than run, because <c>ToggleInv</c> reaches capture and recipe state this shell never builds; the count is the
/// defect's signature, a toggle on the press that closed the card.
/// </summary>
internal static class VerifyEscapeClosesOnlyTheCard
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
        bool inventory = Main.playerInventory, control = player.controlInv, release = player.releaseInventory, dedicated = Main.dedServ;
        var time = new GameTime();
        int toggles = 0;

        void Tick(bool escapeHeld)
        {
            system.UpdateUI(time);
            Main.keyState = escapeHeld ? new KeyboardState(Keys.Escape) : new KeyboardState();
            player.controlInv = escapeHeld;
            PlayerLoader.SetControls(player);
            if (player.controlInv)
            {
                if (player.releaseInventory) toggles++;
                player.releaseInventory = false;
            }
            else player.releaseInventory = true;
        }

        using var hook = EnableModPlayerHooks.For("HookSetControls", save);
        try
        {
            Main.dedServ = true;
            var cases = new List<string>();
            foreach (bool inventoryWasOpen in new[] { false, true })
                foreach (bool onInventoryPage in new[] { false, true })
                {
                    if (CardSystem.IsOpen) CardSystem.CloseOpenCard();
                    Main.playerInventory = inventoryWasOpen;
                    Main.keyState = new KeyboardState();
                    player.controlInv = false;
                    player.releaseInventory = true;
                    toggles = 0;
                    CardSystem.Toggle();
                    if (onInventoryPage) CardSystem.OpenInventory();
                    string name = $"{(onInventoryPage ? "Inventory page" : "overview")} with the player's inventory {(inventoryWasOpen ? "open" : "closed")}";
                    Require(CardSystem.IsOpen, $"premise: the card must open for the {name} case");

                    Tick(escapeHeld: false);
                    Require(CardSystem.IsOpen && toggles == 0, $"{name}: premise: an idle tick leaves the card open and toggles nothing");
                    Tick(escapeHeld: true);
                    int onPress = toggles;
                    bool closedOnPress = !CardSystem.IsOpen;
                    Tick(escapeHeld: true);
                    Tick(escapeHeld: false);
                    Require(onPress == 0 && toggles == 0,
                        $"{name}: the press that closed the card also reached ToggleInv ({onPress} toggle(s) on the press, {toggles} by release)");
                    Require(closedOnPress, $"{name}: the card must close on the tick Escape is pressed, not a tick later");
                    Require(Main.playerInventory == inventoryWasOpen,
                        $"{name}: closing the card left the player's inventory {(Main.playerInventory ? "open" : "closed")}, where he had it {(inventoryWasOpen ? "open" : "closed")}");
                    Tick(escapeHeld: true);
                    Require(toggles == 1, $"{name}: premise: with the card closed, the next press must reach ToggleInv; it reached it {toggles} time(s)");
                    Tick(escapeHeld: false);
                    cases.Add(name);
                }
            Console.WriteLine($"escape: one press closes the card and reaches no ToggleInv, held or released, and the next press toggles the inventory, in {cases.Count} cases: {string.Join("; ", cases)}");
        }
        finally
        {
            if (CardSystem.IsOpen) CardSystem.CloseOpenCard();
            Main.keyState = keys;
            Main.playerInventory = inventory;
            player.controlInv = control;
            player.releaseInventory = release;
            Main.dedServ = dedicated;
        }
    }
}
