# Inventory — the companion's bag

```
Inventory/
├─ CLAUDE.md
├─ CompanionInventory.cs  100 slots (twice the player's main inventory); Collect applies the quick-stack rule: a player stack with room first, then the bag, and rolls a hundred coins up into the next coin on both sides the way Player.DoCoins does; Sort merges stacks and orders items through the game's own sorting layers, without the game's glow; Save/Load through ItemIO
├─ CompanionBagUI.cs      the panel: a bordered box with title and fill count, five rows of ten ItemSlot-backed slots in a viewport, a scrollbar for the rest, placed right of centre beside the open inventory; re-sorts after any interaction that leaves the cursor empty
└─ CompanionBagSystem.cs  opens on right-click on the companion within 160 px (read in Players/CompanionPlayer.PreUpdate, so the click does not also use the held item), and opens the player's inventory with it; the notch click opens it from anywhere; closes on a second click or when the inventory closes, never on distance, because the bag is the companion's and not a chest in the world
```

The bag is state on `Players/CompanionPlayer.cs` so it saves with the character. Weapons never live in it.

## Traps

- **A container only works while `Main.playerInventory` is true.** With it false, `Player.dropItemCheck` throws whatever is on the cursor every tick, so an item lifted from a slot is dropped on the floor and nothing can be put in. Every vanilla chest opens the inventory for the same reason; the bag does too. Drag, shift-click and splitting are expected to work like a piggy bank on that basis, and are unverified until the first in-game run.
- **The array overloads of `ItemSlot.Handle`/`Draw` map a bank slot to gamepad point 400 + index, and the navigator's table ends at 40.** Slots 40 to 99 threw `KeyNotFoundException` every frame and never drew, which looked like a half-empty box on the first run. The single-item `ref Item` overloads are used instead; every slot is gamepad point 400, harmless with a mouse.
- **The game's private `ItemSorting.Sort` glows the wrong inventory and cannot be called on the bag.** It ends by calling `ItemSlot.SetGlow` on every slot it filled, choosing between two 58-entry glow arrays by whether the player has a chest open; the bag opens none, so the bag's slot indices lit up the *player's* inventory (the sort colour wash on the wrong panel, seen 2026-09-08), and a bag holding more than 58 items would have indexed past the array. The bag now binds only `SetupSortingPriorities` and `_layerList` by reflection and runs the same merge-then-order passes itself with no glow; if a tModLoader update renames either, the bindings are null and the bag simply stops sorting.
- **Coins only roll up because the bag does it.** Stacking by type stops at a hundred copper and nothing in the game converts a stack it did not fill itself; the bag ports `Player.DoCoins` for its own array and calls the real one after topping up a purse slot.
- **Sorting moves items under the cursor**, so `Sort` refuses while `Main.mouseItem` holds anything and the UI defers the sort until the cursor is empty.
- **Pickup routing copies `Player.ItemSpace`:** main slots for everything, the purse (50–53) for coins, ammo slots (54–57) for ammo; hearts and mana stars (`ItemID.Sets.IsAPickup`) are never taken, because the player consumes them on touch and the companion reaches drops first.
