# Character body — the live NPC, the orb's drawing, and the stand-in hostiles aim at

```
CharacterBody/
├─ CLAUDE.md                          this guide
├─ CompanionNPC.cs                    NPC lifecycle, brain entry, the motor, health mirrored from the player, downing and revival, its immunity to every liquid, pickup, the presentation snapshot
├─ DrawTheOrb.cs                      the orb drawn as the scaled Destroyer probe rotated to its travel, and the Laser Drill beam during tool work
└─ PlaceStandInPlayerForHostiles.cs   the inactive player slot switched on only while a hostile's AI runs, so enemies can target the orb
```

`CompanionNPC` is the live body: a twenty-pixel orb with the engine's gravity and tile collision switched off, moved only by the motor through the mod's own circle contact (`../Brain/Infrastructure/Movement/`). The engine's box still exists at the orb's size and is what enemies and projectiles hit, so it is targeted, takes damage, is downed and dodges exactly as any NPC; the box and the contact circle must stay the same size, or the body is hit where it is not. It calls the brain, hands the motor the resolved controls, collects touched items, publishes the presentation the renderer reads, and records the tick. Nothing else writes its movement.

The companion's toughness is the player's: at the top of every AI tick its maximum life and defence are set from the local player's, a raise in maximum life carrying current life up by the same amount and a cut clamping it, so a life crystal or an armour change reaches the companion on the next tick and nothing is ever equipped on it. That is the owner's ruling of 14 September 2026 — two ways to get stronger: mirrored stats and the mastery tree — and `Tools/EngineReplay/Lifecycle/VerifyStatMirroring.cs` is its proof. Offence is not mirrored: the weapons are the gear the player hands it (`../Weapons/`, `../Inventory/`).

**Every liquid is air to the body, and there is no breath.** Water, honey, lava and shimmer neither hurt, slow nor transform the orb, by the owner's ruling of 15 September 2026 that the immunity is built in rather than a mastery unlock. Most of that is structural rather than a flag: the engine's wet slowdown, its lava strike and the shimmer buff that starts its transform all live in the NPC's collision step, which the engine skips for a body with tile collision switched off, and nothing in the mod reads liquid as anything but an observation. `SetDefaults` still declares the NPC immune to lava and to the shimmer buff, for any engine path outside that step. Nothing headless runs the engine's own NPC update, so what the engine does to a wet orb stands on the decompiled source rather than on a run. Until that ruling the water and lava hurts lived here as constant pairs and two immunity flags waited for the mastery tree; both went, and there is no liquid immunity to earn.

Player death does not suspend this lifecycle: the companion keeps observing and defending itself. Only its own downed state bypasses the brain. Each tick clears the previous held-item claim and hides the previous torch before tools and weapons make current claims; a free, dry hand can then show the torch, reading the light field every consumer queries.

Downing cancels ordinary thought and asks the motor to cancel recovery flight. A clear flying body stops at once; a body caught inside terrain by an interrupted flight stays in the motor's bounded clearance mode until it reaches the last clear position, then returns to ordinary contact while downed and sinks to rest on the floor, where the player can reach it — a downed orb hovering five tiles up would be unrevivable. It gets up when the player has stood within reach for a few seconds or when a longer floor passes on its own, because a down that ended the companion's participation in the session cost a hundred seconds once. The downed path suspends the brain's current activity and enters its common control finaliser with an unavailable hand before recording the tick; turning recording off cannot leave the old activity executing.

The orb is drawn as the game's Destroyer probe, scaled so its drawn diameter overhangs the contact circle by a few pixels and rotated to face its travel, until its own art exists; the presentation surface the weapons and tools drive — a held item, an aim rotation, the drill beam during tool work in the look of the game's Laser Drill — is kept so the lane that hands the orb its gear draws through it. The stand-in player is a `Player` in the highest slot the map head renderer's per-player table accepts, inactive, switched active only for the span in which a hostile's AI runs, because every enemy picks its target by one loop over the player slots; its box is the orb's box, it draws nothing, and the mining interaction runs the game's own pick routine on it because that routine needs a player.

Pickup observation snapshots the touched item before inventory transfer, then records only the quantity actually removed from its world stack. A successful loot intent is not pickup evidence, and reading the item after `TurnToAir` loses its type and identity.

## Traps

- `CheckActive` returns false, so the companion is never culled for distance.
- The engine's `collideX`/`collideY` are always clear for this body and its `wet` flags are written by the motor, not the engine; a reader of those flags is reading the mod's own contact and liquid test.
- The stand-in player mirrors downed to `dead`, so a boss leaves when the player is dead and the companion is down; it is never the companion's owner of life or position.
