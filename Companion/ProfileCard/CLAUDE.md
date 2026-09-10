# Profile Card — the companion's truthful player-facing control surface

```
ProfileCard/
├─ CLAUDE.md                     this guide
├─ CompanionProfileCardSystem.cs  native identity rail, controls and page lifetime
└─ PreviewMasteryTree.cs          tiered preview nodes, ability subtrees and pan/zoom canvas
```

The profile card is the HUD notch's primary click target. It presents the companion's rendered head, health, current action, cargo entry point and choices the implementation honours: mining and chopping work policy, voluntary hunting, pot breaking, torch placement and following distance. Survival and guarding are automatic and deliberately have no control. Explicit segmented buttons show Off/Mimic/Auto, Off/On and Close/Standard/Free; selecting the current value does not cycle unexpectedly. Preferences belong to `PlayerIntegration/`, where native per-character persistence owns them.

The card uses one `UserInterface` and one `UIState` with profile, cargo and mastery pages. The identity rail is separate from the scrollable controls; normal Terraria text and panel assets carry the appearance. Its close button, Escape and world unload end the open state. Input is consumed before item use, including the notch's opening press. Cargo embeds `../Inventory/`'s bank-slot grid and opens the player's inventory for transfers. Unload drops UI references; the card owns no graphics resources.

Mastery is explicitly a preview: eight connected branches contain tiered circular stat nodes and diamond abilities. Weapon abilities open selectable sub-nodes. Selection changes only the preview, never stats, inventory, experience or saves. The canvas fits the available area on layout changes, clips to its viewport and supports dragging, zooming and reset. This boundary keeps an unfinished progression economy from charging real materials.

`Tools/EngineReplay --render-ui` renders the production pages with installed Terraria fonts and textures into PNGs through a hidden graphics surface. Live portrait animation and game input remain playtest acceptance surfaces.
