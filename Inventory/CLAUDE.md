# Inventory — the companion's bag

The companion picks things up so the player does not have to stop what he is doing. That is the whole reason this folder exists: a second body in the world is only worth having if it does the errands, and the most constant errand in this game is walking back over ground you already cleared to collect what died on it. So the companion loots on contact, holds it, and hands it over whenever the player opens the bag.

Its size is a design statement rather than a number someone picked: twice the player's own main inventory, because the companion is meant to be a second pair of hands and not a mule you have to manage. A bag that filled quickly would turn every trip into an inventory-management chore, which is the chore this exists to remove. It is deliberately generous, and the intent is that a player forgets it is there until he wants something out of it.

```
Inventory/
├─ CLAUDE.md
├─ CompanionInventory.cs  the bag itself: what it will accept, taking a world item on contact, sorting, and saving
├─ CompanionBagUI.cs      the panel the player rummages in, built on the game's own slot handling so it behaves like a piggy bank
└─ CompanionBagSystem.cs  when the panel is open: the right-click on the companion, the click on the HUD notch, and what closes it
```

## What it is not

**It is not the companion's equipment.** Weapons live in `../Combat/Weapons/` and are chosen by the arsenal; nothing the companion fights with is ever in the bag, and putting a sword in it arms nobody. The separation is deliberate — equipment is a capability the mastery tree grows, and carried loot is cargo — and it is why the bag has no equip slots and no concept of a held item.

**It is not a chest in the world.** Distance never closes it, because it belongs to the companion rather than to a place; the player can rummage in it from across the map while the companion is off doing something else.

**It is not a second player inventory.** New kinds of thing are the companion's to carry, so a pickup only ever tops up a stack the player already holds and otherwise goes to the bag — it never fills one of the player's empty slots, because deciding what the player's own free space is for is the player's business.

## How it sits in the rest of the mod

The bag is a leaf that four other systems reach into, and three of the four are not obvious from inside this folder.

```
Players/CompanionPlayer ──owns──▶ the bag        state lives there, so it saves with the character
Brain/Actions/Gathering ──asks──▶ CanAccept      a full bag scores looting at zero
Companion/CompanionNPC  ──calls─▶ Collect        on contact, every tick
UI + Players input      ──open──▶ the panel      the notch click and the right-click
```

The one worth knowing before touching anything here is the second. `LootAction` asks the bag whether a pickup would fit *while it is scoring*, so capacity is an input to the companion's decisions and not merely a limit on its storage: a bag that refuses something makes the companion stop walking to that thing at all, and a change to what the bag accepts is a change to how the companion behaves. Anything added here that can say no is a behaviour change, and the place it shows up is a companion that walks past loot for no visible reason.

The bag's own state lives on `../Players/CompanionPlayer.cs` rather than on the NPC, so it persists with the character across worlds and survives the companion being downed. The NPC's `Bag` is a pass-through to it.

## Where the game's own code does the work, and where it does not

Every question with a right answer already in the game is asked of the game rather than reimplemented. A coin goes through the player's own pickup path, so the purse fills first, a hundred rolls into the next denomination and the popup and sound are the ones the player already knows. The panel's slots are the game's slot handling in its bank context, so dragging, shift-clicking and splitting stacks work the way a piggy bank works without this folder implementing any of it. Sorting borrows the game's own ordering layers.

The one place that failed is worth carrying: the first version merged coins by hand with its own copy of the convert-at-a-hundred rule, it read correctly and did the wrong thing in play, and it is gone rather than debugged. The general form is the project's standing instruction — read the decompiled game for the path that already does it — and the specific lesson is that a copied rule which looks right is more expensive than no rule, because it fails only in play and only sometimes.

## Where it is going

Nothing here is upgradeable yet, and it should be. The bag is the natural home for the mastery tree's carrying nodes (more capacity, then automatic hand-over, then picking up from further away), and for orders about what to keep — a filter is the obvious next feature and there is no place for one today, because `CanAccept` answers "does this fit" and nothing asks "do we want it". Adding that means the filter has to sit where `LootAction` can see it, or the companion will walk to things it intends to refuse.

Depositing into a chest the player has open, and handing the bag's contents over on a command, are both unbuilt and both want the same thing first: a transfer path that is not the pickup path.

## What a reader will get wrong here

- **The bag is not where the companion's things live; it is where the *player's* things live temporarily.** Every projectile the companion fires is owned by the player, every kill and drop is the player's, and the bag is a holding area on the way to him. Reasoning about it as the companion's property produces features nobody wants, like the companion consuming a potion out of it.
- **`CanAccept` is on the scoring path and runs every tick**, so anything expensive added to it is paid for by the brain, not by the pickup.
- **Hearts and mana stars are never taken**, and that is not an oversight: the player consumes those on touch and the companion reaches drops first, so a companion that picked them up would be stealing healing.

## Traps

- **A container only works while `Main.playerInventory` is true.** With it false, `Player.dropItemCheck` throws whatever is on the cursor every tick, so an item lifted from a slot is dropped on the floor and nothing can be put in. Every vanilla chest opens the inventory for the same reason; the bag does too. Drag, shift-click and splitting are expected to work like a piggy bank on that basis, and are unverified until the first in-game run.
- **The array overloads of `ItemSlot.Handle`/`Draw` map a bank slot to a gamepad point offset by its index, and the navigator's table stops well short of the bag's slot count.** The later slots threw `KeyNotFoundException` every frame and never drew, which looked like a half-empty box on the first run. The single-item `ref Item` overloads are used instead; every slot maps to one point, harmless with a mouse.
- **The game's private `ItemSorting.Sort` glows the wrong inventory and cannot be called on the bag.** It ends by calling `ItemSlot.SetGlow` on every slot it filled, choosing between two fixed-size glow arrays by whether the player has a chest open; the bag opens none, so the bag's slot indices lit up the *player's* inventory (the sort colour wash on the wrong panel, seen 2026-09-08), and a bag holding more items than the array is long would have indexed past it. The bag now binds only `SetupSortingPriorities` and `_layerList` by reflection and runs the same merge-then-order passes itself with no glow; if a tModLoader update renames either, the bindings are null and the bag simply stops sorting.
- **A coin is the player's and takes the player's own pickup path.** The bag's first version merged coins into the purse by hand and converted at a hundred by its own copy of the rule, and run 5 (2026-09-08) still had copper sitting in the bag as stacks that never became silver; the copy read right and did wrong in play. The bag still ports `Player.DoCoins` for its own array, for what the game hands back when the player's inventory is full. Every coin pickup writes one line to the mod's log, so the next playtest measures where a coin went instead of the report guessing.
- **Sorting moves items under the cursor**, so `Sort` refuses while `Main.mouseItem` holds anything and the UI defers the sort until the cursor is empty.
- **Pickup routing for everything but coins copies the stack half of `Player.ItemSpace`:** an existing stack in the main slots, or in the ammo slots for ammo, and never an empty player slot.
