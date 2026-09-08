# Inventory — the companion's bag

```
Inventory/
├─ CLAUDE.md
├─ CompanionInventory.cs  100 slots (twice the player's main inventory); Collect applies the quick-stack rule: a player stack with room first, then the bag; Save/Load through ItemIO
├─ CompanionBagUI.cs      the panel: 10×10 ItemSlot-backed slots in the bank-item context, so drag, shift-click and splitting behave like a piggy bank
└─ CompanionBagSystem.cs  opens on right-click on the companion within 160 px (read in Players/CompanionPlayer.PostUpdate), closes on a second click, the inventory key, or walking 320 px away
```

The bag is state on `Players/CompanionPlayer.cs` so it saves with the character. Weapons never live in it.
