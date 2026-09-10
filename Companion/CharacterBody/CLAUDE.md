# Character body — the live NPC and renderer boundary

```
CharacterBody/
├─ CLAUDE.md          this guide
├─ CompanionNPC.cs    NPC lifecycle, brain entry, pickup and engine state
├─ CompanionBody.cs   player-renderer adapter and held-item presentation
└─ CompanionBreath.cs drowning countdown and NPC damage
```

`CompanionNPC` is the live body. It calls the brain, accepts the resolved motor controls, updates breath, collects touched items, synchronises the renderer and records the tick. The movement adapter is the only path that changes live NPC movement; other systems describe intent or observations.

Player death does not suspend this lifecycle: the companion keeps observing, defending itself and seeking air. Only the companion's own downed state bypasses its brain. Each tick clears the previous held-item claim and hides the previous torch before tools and weapons make current claims; a free, dry hand can then show the torch. An earlier animation or item value is not authority to retain an occupied hand.

Downing cancels ordinary thought and asks the motor to cancel recovery flight. A clear flying body stops at once; a body made solid by interrupted flight stays in the motor's bounded clearance mode until it reaches the last clear position, then returns to normal collision while downed. The body layer never turns this into a rescue route or revives a body by moving it toward the owner.

The renderer borrows Terraria’s player drawing path, so it needs the selected-item visual state synchronised and a closed sprite batch around player drawing. Breath is NPC life-state logic, not player state: it drives NPC damage and downed behaviour while world observation reads the resulting facts.

Pickup observation snapshots the touched item before inventory transfer, then records only the quantity actually removed from its world stack. A successful loot intent is not pickup evidence, and reading the item after TurnToAir loses its type and identity. The diagnostic event names the combined player-stack/companion-bag destination because the collector may divide one transfer between them.
