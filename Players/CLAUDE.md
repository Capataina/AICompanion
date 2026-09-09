# Players — what belongs to the character rather than to the world

The companion is not a thing you find in a world; it is a thing *your character* has. Summon it once and it is yours from then on, in that world and every other, with the same bag and the same HUD position where you left it. This folder is what makes that true, and it is the reason the mod has a `ModPlayer` at all.

The choice behind it is where each piece of state lives. Terraria offers two homes: the world, which is shared by every character who visits it, and the character, which travels. Anything about the companion that should survive changing worlds has to sit here, and anything that genuinely belongs to a place would sit in a `ModSystem` instead. Everything so far belongs to the character, which is a statement about what the companion is — a relationship rather than a world feature.

```
Players/
└─ CompanionPlayer.cs   the ModPlayer: what this character owns of the companion, and
                        where the player's input about the companion is read
```

## Two jobs in one file, and they are less unrelated than they look

**The state that persists**: whether this character has ever summoned a companion, which is what makes it respawn on every world enter without the command; the bag and everything in it; and where the HUD notch was dragged to.

**The input that concerns the companion**: the overlay keybind, and the right-click on the companion that opens its bag.

They share a file because they share a lifetime and an owner. The input handlers here are the ones that need to run *as the player*, in the player's own update order, rather than as the NPC — and the reason that matters is timing rather than tidiness. The right-click is read in `PreUpdate`, before the player's item use for the tick, because that is what stops the held item firing on the same click that opened the bag; the same handler in `PostUpdate` would let the swing happen first and the player would attack his own companion's bag open.

## How it sits in the rest of the mod

This folder is the persistence layer under three other folders, none of which knows it exists.

```
Inventory/CompanionInventory  ──stored on──▶ CompanionPlayer.Bag
UI/CompanionHealthBar         ──stored on──▶ the saved notch position
Brain/Debug (the overlay)     ──toggled by─▶ the keybind read here
Companion/CompanionNPC        ──spawned by─▶ the has-companion flag on world enter
```

The bag being here rather than on the NPC is the load-bearing one. The NPC is destroyed and recreated freely — on world exit, on the companion being downed and revived, on a mod reload — and a bag on the NPC would empty every time. `CompanionNPC.Bag` is a pass-through to this file, which is why picking up an item does not need the NPC to survive anything.

**Singleplayer only**, like the rest of the mod: the one instance that matters is `Main.LocalPlayer`'s, and nothing here iterates players or branches on who owns what.

## What it is not

- **Not the companion's own state.** The companion's health, position, current action and everything else transient live on the NPC and are deliberately not saved. A companion is re-created on world enter with full health; it does not remember being hurt yesterday.
- **Not a settings home.** There is no configuration here, and a genuine user setting would belong in tModLoader's own config rather than in character save data.
- **Not where the companion is spawned.** The flag lives here and the spawn happens on world enter, but `Commands/` and `Companion/` own the spawning itself.

## Where it is going

The mastery tree is the obvious next tenant and the reason to keep this file's shape clean: progression belongs to the character, so tree state — levels, unlocked nodes, whatever resources have been paid in — lands here rather than in the world. Orders are the same: a standing instruction ("stay", "gather while I build") is a property of your companion and travels with it.

Both are unbuilt. What is already decided is that neither becomes world state, because a companion that forgot its training when you visited a friend's world would be a different product.

## Traps

- **A keybind saved by an earlier version outranks the registered default permanently**, so moving a default moves nothing for anyone who has already played. The overlay was dead for a whole session against a saved binding of the key left of 1, which SDL reports as the grave or ISO-section scancode and FNA drops before it becomes a key at all. Every key the overlay should answer to is therefore listed raw here as well as registered as a default, and a log line names any Oem key the moment it is pressed so the right one can be read off `client.log` rather than guessed.
- **The right-click must be read before item use, not after.** Setting `mouseInterface` in `PreUpdate` is what suppresses the held item on that click.
