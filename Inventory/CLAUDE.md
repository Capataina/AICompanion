# Inventory — the companion's bag

```
Inventory/
├─ CLAUDE.md
├─ CompanionInventory.cs  100 slots (twice the player's main inventory); Collect applies the quick-stack rule: a player stack with room first, then the bag; Sort runs the game's own chest sort; Save/Load through ItemIO
├─ CompanionBagUI.cs      the panel: a bordered box with title and fill count, five rows of ten ItemSlot-backed slots in a viewport, a scrollbar for the rest, placed right of centre beside the open inventory; re-sorts after any interaction that leaves the cursor empty
└─ CompanionBagSystem.cs  opens on right-click on the companion within 160 px (read in Players/CompanionPlayer.PreUpdate, so the click does not also use the held item), and opens the player's inventory with it; closes on a second click, when the inventory closes, or on walking 320 px away
```

The bag is state on `Players/CompanionPlayer.cs` so it saves with the character. Weapons never live in it.

## Traps

- **A container only works while `Main.playerInventory` is true.** With it false, `Player.dropItemCheck` throws whatever is on the cursor every tick, so an item lifted from a slot is dropped on the floor and nothing can be put in. Every vanilla chest opens the inventory for the same reason; the bag does too. Drag, shift-click and splitting are expected to work like a piggy bank on that basis, and are unverified until the first in-game run.
- **The array overloads of `ItemSlot.Handle`/`Draw` map a bank slot to gamepad point 400 + index, and the navigator's table ends at 40.** Slots 40 to 99 threw `KeyNotFoundException` every frame and never drew, which looked like a half-empty box on the first run. The single-item `ref Item` overloads are used instead; every slot is gamepad point 400, harmless with a mouse.
- **`ItemSorting.Sort(Item[], params int[])` is private.** The public entry points only sort the player's inventory or the open chest, so the bag binds the private one by reflection once; if a tModLoader update renames it, `SortMethod` is null and the bag simply stops sorting.
- **Sorting moves items under the cursor**, so `Sort` refuses while `Main.mouseItem` holds anything and the UI defers the sort until the cursor is empty.
- **Pickup routing copies `Player.ItemSpace`:** main slots for everything, the purse (50–53) for coins, ammo slots (54–57) for ammo; hearts and mana stars (`ItemID.Sets.IsAPickup`) are never taken, because the player consumes them on touch and the companion reaches drops first.
