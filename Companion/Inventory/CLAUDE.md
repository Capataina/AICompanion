# Inventory — cargo that persists with the player

```
Inventory/
├─ CLAUDE.md               this guide
├─ CompanionInventory.cs   storage and acceptance rules
├─ CompanionBagSystem.cs   pickup routing and state bridge
└─ CompanionBagUI.cs       bag panel and item-slot interaction
```

The bag stores collected cargo, not companion equipment. It persists through `../PlayerIntegration/CompanionPlayer`, so it survives world changes and a downed NPC. The NPC exposes the same bag as a pass-through for touched-item pickup; gathering scores a full bag as unable to accept more.

Weapons are capability equipment under `../Weapons/` and never consume bag slots. The bag UI embeds into the profile card's Cargo page and uses Terraria's bank-slot interaction path. Its column count adapts to available width, with a clipped viewport and scrollbar exposing every slot. Manual transfers retain their arrangement; the explicit Sort button changes it. Inventory scale is restored in a finally block so an item renderer failure cannot change the rest of Terraria's inventory drawing.
