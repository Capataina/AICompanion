# Profile Card — the companion's truthful player-facing control surface

```
ProfileCard/
├─ CLAUDE.md                     this guide
└─ CompanionProfileCardSystem.cs  native Terraria UI state and its lifetime
```

The profile card is the HUD notch's primary click target. It presents the companion's rendered head, health, current action, cargo entry point and choices the implementation honours: mining and chopping work policy, voluntary hunting, pot breaking, torch placement and following distance. Survival and guarding are automatic and deliberately have no control. Explicit segmented buttons show Off/Mimic/Auto, Off/On and Close/Standard/Free; selecting the current value does not cycle unexpectedly. Preferences belong to `PlayerIntegration/`, where native per-character persistence owns them.

The card uses one `UserInterface` and one `UIState`. Its close button, Escape and world unload end the open state. A scrollable settings list keeps the footer available in smaller viewports; the blue/gold treatment follows the repository's profile mock. Input is consumed in the player's pre-update phase, before item use, including the notch's opening press. It owns no graphics resources, so unload drops UI references. Cargo stays in `../Inventory/`, whose bank-slot UI is opened through the card's bag button. Actual in-game appearance remains a playtest acceptance surface; headless compilation does not prove typography or portrait rendering.
