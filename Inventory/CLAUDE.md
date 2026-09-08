# Inventory — the companion's bag

```
Inventory/
├─ CLAUDE.md
├─ CompanionInventory.cs  100 slots (twice the player's main inventory); Collect applies the quick-stack rule: a player stack with room first, then the bag; Save/Load through ItemIO
├─ CompanionBagUI.cs      the panel: 10×10 ItemSlot-backed slots in the bank-item context, placed right of centre beside the open inventory
└─ CompanionBagSystem.cs  opens on right-click on the companion within 160 px (read in Players/CompanionPlayer.PreUpdate, so the click does not also use the held item), and opens the player's inventory with it; closes on a second click, when the inventory closes, or on walking 320 px away
```

The bag is state on `Players/CompanionPlayer.cs` so it saves with the character. Weapons never live in it.

## Traps

- **A container only works while `Main.playerInventory` is true.** With it false, `Player.dropItemCheck` throws whatever is on the cursor every tick, so an item lifted from a slot is dropped on the floor and nothing can be put in. Every vanilla chest opens the inventory for the same reason; the bag does too. Drag, shift-click and splitting are expected to work like a piggy bank on that basis, and are unverified until the first in-game run.
- **Pickup routing copies `Player.ItemSpace`:** main slots for everything, the purse (50–53) for coins, ammo slots (54–57) for ammo; hearts and mana stars (`ItemID.Sets.IsAPickup`) are never taken, because the player consumes them on touch and the companion reaches drops first.
