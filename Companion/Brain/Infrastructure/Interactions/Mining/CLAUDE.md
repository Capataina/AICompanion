# Mining — ores and the pickaxe

```
Mining/
├─ CLAUDE.md
├─ OreFinder.cs   which tiles are ore (TileID.Sets.Ore), a bounded 8-connected vein, and ore discovery near a source body with an approach proved from the companion's own centre; its result retains the exact nearest unresolved tile separately from a proven target
└─ TileMiner.cs   runs the game's own Player.PickTile on the companion's drawing-only player, so the damage formula, the power gates, a modded tile's power check, the crack table and the break are the game's; CanMine asks the game's private damage formula through a delegate bound to that player
```

## Invariants

- **Ore work never excavates ordinary terrain.** `OreFinder.IsOre()` uses `TileID.Sets.Ore` to classify tiles; only ore tiles are added to the discovered vein or the search result. A wall, dirt, sand or any other tile sits beside the ore and is never targeted or damaged, even by swings whose visual spread reaches beyond the intended tile.

## Traps

Remaining-work estimation uses the same private damage delegate as the native eligibility query, then includes the world modifier applied by PickTile after that formula. It reads the miner's existing hit table without allocating or damaging an entry. The estimate describes an ore completion while native gates and damage remain unchanged; modded transformations and later permission changes still require outcome revalidation. A removed, protected or undamageable ore has no completion estimate.

An unresolved approach belongs to a particular accepted tile. A nearby enclosed ore must not borrow the unknown status of a farther exposed ore, and a tool-ineligible diagnostic search cannot supply a playable unresolved candidate. SearchResult preserves the target of that evidence; callers do not rediscover it with different filters.

`TileMiner.Swing` reports whether a native call occurred. Its stamped `LastOutcome` separately reports partial damage, removal, material/frame change or no observed change. A native power or permission refusal can therefore accept the call while producing no work. Damage comes from this miner's HitTile table, not a shared tile-health value; removal does not prove which items were produced or collected.

- **Arrival tolerance is not tool reach.** The work action enters its swing only when the real `InReach` check succeeds; a navigator being near its destination does not prove a tile is mineable. Approach candidates reserve a small horizontal margin under that same reach predicate.

- **A useful actual position needs no approach.** Approach first checks InReach at the body's own centre and returns it immediately when usable. Only an unusable current position triggers the hover-cell scan and the reach sense. This establishes current tool access, not native permission to remove the tile.

- **`../FindToolAccess.cs` owns approach and actual reach for every tile-tool activity.** Ore discovery and execution use that same contract rather than keeping mining-specific copies. `Approach` reports the reach sense's yes/no/unknown about a hover: only yes is selected, no can finish a reachable portion, and unknown is not an offer. A later preparation, once the flood has grown or the body has moved, may prove it.
- **The approach is the reach sense's flood over free space, and the flood is what decides what a vein behind water means.** Water and lava are walls to the flood until an immunity opens them, so a vein across a pool the body is not immune to is refused rather than approached, and no clock reaches any of this: a row producing an undecided verdict by starving a deadline is testing nothing, and two such rows were once found doing exactly that.
- **`Point.ToWorldCoordinates()` already defaults to a tile centre, but a solid target is not a clear destination for any native tile walk.** `Player.PickTile` applies native damage and kill permission; it does not enforce tool reach at this call boundary. The companion checks range and adds occlusion by hovering in a free cell beside an exposed ore face. Aiming at the ore centre makes the native test reject the tile it is meant to mine.
- **The occlusion walk is `Collision.CanHit`, not `CanHitLine`.** `CanHitLine` refuses a step when the cell or either neighbour across the step is solid, a beam three tiles wide, so it refused any walk whose band one row either side touched a floor tile, and any reach into a one-tile notch whose diagonal neighbour was solid. The player-sealed-then-reopened ore fixture found it on 2026-09-13: the companion stood beside an open notch for 900 ticks reporting no reachable ore. `CanHit` refuses a cell the walk enters and a two-sided squeeze, which still forbids reaching through a wall.
- **Ceiling ore is ordinary work.** The hover scan ranks every free cell in reach of the tile whether it is above or below, so ore in a ceiling is reached the way ore in a floor is, and the walker's hop — a take-off proof, a jump held in flight, a lost-take-off deferral and a narrow-top limitation asserted by `VerifyMiningHops` — went with the walker along with that fixture. Ore with no exposed face inside any free cell is known-unusable; nothing simulates a way to it.
- **The pick formula was a copy once and it drifted in eight places** (wrong multipliers on Chlorophyte, Meteorite, tombstones and the altar, a depth gate borrowed from the Hellforge, spikes and the modded-tile power check missing). The game's `PickTile` is public and only its multiplayer branches are gated, so it is called, not copied; the formula stays private and is reached by a delegate. If tModLoader renames `GetPickaxeDamage`, the miner's constructor throws at load and the log names it.
- **Discovery costs a cell scan per distance-improving candidate and no route search at all**, stopping at that candidate's nearest reachable hover. The scan is still not free — it walks every hoverable cell in reach of the target and asks the engine for a line to an exposed face — so the search still runs from the mine action on a trigger and a cooldown rather than per tick; a pick change or a terrain change is such a trigger.
