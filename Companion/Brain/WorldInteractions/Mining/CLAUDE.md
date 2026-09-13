# Mining — ores and the pickaxe

```
Mining/
├─ CLAUDE.md
├─ OreFinder.cs   which tiles are ore (TileID.Sets.Ore), a bounded 8-connected vein, and ore discovery near a source body with an approach proved from the companion's actual feet; its result retains the exact nearest unresolved tile separately from a proven target
└─ TileMiner.cs   runs the game's own Player.PickTile on the companion's drawing-only player, so the damage formula, the power gates, a modded tile's power check, the crack table and the break are the game's; CanMine asks the game's private damage formula through a delegate bound to that player
```

## Traps

Remaining-work estimation uses the same private damage delegate as the native eligibility query, then includes the world modifier applied by PickTile after that formula. It reads the miner's existing hit table without allocating or damaging an entry. The estimate describes an ore completion while native gates and damage remain unchanged; modded transformations and later permission changes still require outcome revalidation. A removed, protected or undamageable ore has no completion estimate.

An unresolved approach belongs to a particular accepted tile. A nearby enclosed ore must not borrow the unknown status of a farther exposed ore, and a tool-ineligible diagnostic search cannot supply a playable unresolved candidate. SearchResult preserves the target of that evidence; callers do not rediscover it with different filters.

`TileMiner.Swing` reports whether a native call occurred. Its stamped `LastOutcome` separately reports partial damage, removal, material/frame change or no observed change. A native power or permission refusal can therefore accept the call while producing no work. Damage comes from this miner's HitTile table, not a shared tile-health value; removal does not prove which items were produced or collected.

- **Arrival tolerance is not tool reach.** The work action enters its swing only when the real `InReach` check succeeds; a navigator being near its destination does not prove a tile is mineable. Approach candidates reserve a small horizontal margin under that same reach predicate.

- **A useful actual pose needs no approach.** Approach first checks InReach at the supplied actual feet and returns that pose immediately when usable. Only an unusable current pose triggers standing-node enumeration and a walker query. The margin reserved for a future arrival does not shrink reach at an already occupied pose. This establishes current tool access, not a future safe landing or native permission to remove the tile.

- **`../FindToolAccess.cs` owns approach and actual reach for every tile-tool activity.** Ore discovery and execution use that same contract rather than keeping mining-specific copies. `Approach` reports the bounded walker query's yes/no/unknown result: only yes is selected, no can finish a reachable portion, and unknown must be retried.
- **`Point.ToWorldCoordinates()` already defaults to a tile centre, but a solid target is not a clear destination for any native tile walk.** `Player.PickTile` applies native damage and kill permission; it does not enforce tool reach at this call boundary. The companion checks range and adds occlusion by walking to an open tile beside an exposed ore face. Walking to the ore centre makes the native test reject the tile it is meant to mine.
- **The occlusion walk is `Collision.CanHit`, not `CanHitLine`.** `CanHitLine` refuses a solid tile on either side of every step, a beam three tiles wide, so it refused every line along a floor and any reach into a one-tile notch whose diagonal neighbour was solid. The player-sealed-then-reopened ore fixture found it on 2026-09-13: the companion stood beside an open notch for 900 ticks reporting no reachable ore. `CanHit` refuses a cell the walk enters and a two-sided squeeze, which still forbids reaching through a wall.
- **Ore above standing reach is worked by a hop.** Discovery asks `HopApproach` only when no standing pose works: a take-off pose counts when the shared body model proves a dry ground jump from rest there brings the tile into reach and lands beside it, and the walker can reach the take-off. The target then carries `Hop`; mining walks to the take-off, jumps, and swings while the rising body is in reach, re-proving the hop from the live pose before every jump. A take-off is re-derived only when terrain or the pick changes, because walking to it and the hop itself both move the feet by design.
- **The pick formula was a copy once and it drifted in eight places** (wrong multipliers on Chlorophyte, Meteorite, tombstones and the altar, a depth gate borrowed from the Hellforge, spikes and the modded-tile power check missing). The game's `PickTile` is public and only its multiplayer branches are gated, so it is called, not copied; the formula stays private and is reached by a delegate. If tModLoader renames `GetPickaxeDamage`, the miner's constructor throws at load and the log names it.
- **Discovery costs up to one walker search per distance-improving candidate**, stopping at that candidate's nearest reachable pose, plus hop proofs for candidates with no standing pose. That is why the search runs from the mine action on a trigger and a cooldown, never per tick; a pick change or a terrain change is such a trigger.
