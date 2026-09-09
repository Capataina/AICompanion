# Inventory — cargo that persists with the player

```
Inventory/
├─ CLAUDE.md               this guide
├─ CompanionInventory.cs   storage and acceptance rules
├─ CompanionBagSystem.cs   pickup routing and state bridge
└─ CompanionBagUI.cs       bag panel and item-slot interaction
```

The bag stores collected cargo, not companion equipment. It persists through `../PlayerIntegration/CompanionPlayer`, so it survives world changes and a downed NPC. The NPC exposes the same bag as a pass-through for touched-item pickup; gathering scores a full bag as unable to accept more.

Weapons are capability equipment under `../Weapons/` and never consume bag slots. The bag UI uses Terraria’s item-slot interaction path and is opened from player-facing integration or the HUD notch.
