# Character body — the live NPC, the orb's drawing, and the stand-in hostiles aim at

```
CharacterBody/
├─ CLAUDE.md                          this guide
├─ CompanionNPC.cs                    NPC lifecycle, brain entry, the motor, health mirrored from the player, downing and revival, the liquid hurts, pickup, the presentation snapshot
├─ DrawTheOrb.cs                      the orb drawn as the scaled Destroyer probe rotated to its travel, and the Laser Drill beam during tool work
└─ PlaceStandInPlayerForHostiles.cs   the inactive player slot switched on only while a hostile's AI runs, so enemies can target the orb
```

`CompanionNPC` is the live body: a twenty-pixel orb with the engine's gravity and tile collision switched off, moved only by the motor through the mod's own circle contact (`../Brain/Infrastructure/Movement/`). The engine's box still exists at the orb's size and is what enemies and projectiles hit, so it is targeted, takes damage, is downed and dodges exactly as any NPC; the box and the contact circle must stay the same size, or the body is hit where it is not. It calls the brain, hands the motor the resolved controls, collects touched items, publishes the presentation the renderer reads, and records the tick. Nothing else writes its movement.

The companion's toughness is the player's: at the top of every AI tick its maximum life and defence are set from the local player's, a raise in maximum life carrying current life up by the same amount and a cut clamping it, so a life crystal or an armour change reaches the companion on the next tick and nothing is ever equipped on it. That is the owner's ruling of 14 September 2026 — two ways to get stronger: mirrored stats and the mastery tree — and `Tools/EngineReplay/Lifecycle/VerifyStatMirroring.cs` is its proof. Offence is not mirrored: the weapons are the gear the player hands it (`../Weapons/`, `../Inventory/`).

**Water and lava hurt on contact, and there is no breath.** The two hurts live here as constant pairs — this much damage every this many ticks of contact — because they are facts about the body and the motor is what applies them: it reads which liquid the contact circle touches every tick and strikes the NPC on the interval. Water is gentle enough that a few tiles of crossing is survivable on a starter life pool; lava is lethal within a few seconds whatever the pool. Two immunity flags on the body, defaulting off, are what the mastery tree flips, and the brain copies them into the terrain rules every tick so an immunity opens the flood and the route as well as stopping the hurt. Honey and shimmer only slow the body. A downed body takes no liquid damage, because the game's own strike refuses it and counting contact against it would deal the damage the moment it got up.

Player death does not suspend this lifecycle: the companion keeps observing, defending itself and leaving liquid. Only its own downed state bypasses the brain. Each tick clears the previous held-item claim and hides the previous torch before tools and weapons make current claims; a free, dry hand can then show the torch, reading the light field every consumer queries.

Downing cancels ordinary thought and asks the motor to cancel recovery flight. A clear flying body stops at once; a body caught inside terrain by an interrupted flight stays in the motor's bounded clearance mode until it reaches the last clear position, then returns to ordinary contact while downed and sinks to rest on the floor, where the player can reach it — a downed orb hovering five tiles up would be unrevivable. It gets up when the player has stood within reach for a few seconds or when a longer floor passes on its own, because a down that ended the companion's participation in the session cost a hundred seconds once. The downed path suspends the brain's current activity and enters its common control finaliser with an unavailable hand before recording the tick; turning recording off cannot leave the old activity executing.

The orb is drawn as the game's Destroyer probe, scaled so its drawn diameter overhangs the contact circle by a few pixels and rotated to face its travel, until its own art exists; the presentation surface the weapons and tools drive — a held item, an aim rotation, the drill beam during tool work in the look of the game's Laser Drill — is kept so the lane that hands the orb its gear draws through it. The stand-in player is a `Player` in the highest slot the map head renderer's per-player table accepts, inactive, switched active only for the span in which a hostile's AI runs, because every enemy picks its target by one loop over the player slots; its box is the orb's box, it draws nothing, and the mining interaction runs the game's own pick routine on it because that routine needs a player.

Pickup observation snapshots the touched item before inventory transfer, then records only the quantity actually removed from its world stack. A successful loot intent is not pickup evidence, and reading the item after `TurnToAir` loses its type and identity.

## Traps

- `CheckActive` returns false, so the companion is never culled for distance.
- The engine's `collideX`/`collideY` are always clear for this body and its `wet` flags are written by the motor, not the engine; a reader of those flags is reading the mod's own contact and liquid test.
- The stand-in player mirrors downed to `dead`, so a boss leaves when the player is dead and the companion is down; it is never the companion's owner of life or position.
