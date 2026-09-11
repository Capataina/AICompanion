# Inventory — cargo that persists with the player

```
Inventory/
├─ CLAUDE.md               this guide
├─ CompanionInventory.cs   storage and acceptance rules
├─ CompanionBagSystem.cs   pickup routing and state bridge
└─ CompanionBagUI.cs       filtered fixed-size grid, item details and native transfers
```

The bag stores collected cargo, not companion equipment. It persists through `../PlayerIntegration/CompanionPlayer`, so it survives world changes and a downed NPC. The NPC exposes the same bag as a pass-through for touched-item pickup; gathering scores a full bag as unable to accept more.

Weapons are capability equipment under `../Weapons/` and never consume bag slots. The UI embeds into the profile card's Inventory page without creating, resizing or relocating the outer panel. Its slots keep a fixed size while the column count follows available width. The viewport initially fits complete rows; a native scrollbar exposes every slot. All/Ore/Wood/Loot filters preserve the original backing indices and never rearrange items. Ore uses the native ore tile classification; Wood uses Terraria's Any Wood recipe group, including additions made by other mods. Hovering an occupied slot selects its detail view, which uses the item's rarity colour and the native ore tile texture for the in-wall example. The sample tile is illustrative, not a captured world frame.

Native bank handling owns cursor transfers and stack splitting. Hand everything over uses `Player.GetItem` and leaves any remainder in its original bag slot; it refuses while a cursor stack is held. This uses the game's stacking and item hooks rather than a second transfer algorithm. The native icon renderer preserves item drawing hooks, but the blue slot background is selected explicitly: bank context uses brown art, and player-inventory context stamps a hotbar shortcut on the single-item overload. Both looked wrong in populated offscreen renders. Inventory scale is restored in a finally block so a drawing failure cannot affect the rest of Terraria's inventory.

Transfer feedback takes the ore thumbnail's space when the detail panel is short, so a held cursor stack or full player inventory still produces a visible explanation at the smallest supported viewport.

The transient recent-pickup summary captures the item name before clearing a world item and reports the accepted quantity. It is reset when storage loads and never written to character saves. The profile tile distinguishes occupied slots from item counts. Collection's existing sort and save semantics remain the storage owner's responsibility.

The native UI renderer checks a full, partly available and empty player inventory through the real hand-over button, preserving total item counts. It also renders populated slots and verifies filtering and column changes. Keyboard/gamepad navigation through this custom grid is not covered by those mouse-event checks.
