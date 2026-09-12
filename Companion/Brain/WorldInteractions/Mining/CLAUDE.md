# Mining — ores and the pickaxe

```
Mining/
├─ CLAUDE.md
├─ OreFinder.cs   which tiles are ore (TileID.Sets.Ore), a bounded 8-connected vein, and ore discovery near a source body with an approach proved from the companion's actual feet; its result retains the exact nearest unresolved tile separately from a proven target
└─ TileMiner.cs   runs the game's own Player.PickTile on the companion's drawing-only player, so the damage formula, the power gates, a modded tile's power check, the crack table and the break are the game's; CanMine asks the game's private damage formula through a delegate bound to that player
```

## Traps

An unresolved approach belongs to a particular accepted tile. A nearby enclosed ore must not borrow the unknown status of a farther exposed ore, and a tool-ineligible diagnostic search cannot supply a playable unresolved candidate. SearchResult preserves the target of that evidence; callers do not rediscover it with different filters.

`TileMiner.Swing` reports whether a native call occurred. Its stamped `LastOutcome` separately reports partial damage, removal, material/frame change or no observed change. A native power or permission refusal can therefore accept the call while producing no work. Damage comes from this miner's HitTile table, not a shared tile-health value; removal does not prove which items were produced or collected.

- **Arrival tolerance is not tool reach.** The work action enters its swing only when the real `InReach` check succeeds; a navigator being near its destination does not prove a tile is mineable. Approach candidates reserve a small horizontal margin under that same reach predicate.

- **A useful actual pose needs no approach.** Approach first checks InReach at the supplied actual feet and returns that pose immediately when usable. Only an unusable current pose triggers standing-node enumeration and a walker query. The margin reserved for a future arrival does not shrink reach at an already occupied pose. This establishes current tool access, not a future safe landing or native permission to remove the tile.

- **`OreFinder.Approach` and `InReach` share one reach test** (the player's reach box from an eye above the feet, plus a sight line), so a target found is a target the swing reaches; changing one without the other makes the miner walk to a spot it cannot mine from. `Approach` additionally reports the bounded walker query's yes/no/unknown result: only yes is selected, no can finish a reachable portion, and unknown must be retried.
- **`Point.ToWorldCoordinates()` already defaults to a tile centre, but a solid target is not a clear `CanHitLine` destination.** `Player.PickTile` applies native damage and kill permission; it does not enforce tool reach at this call boundary. The companion checks range and adds occlusion by tracing to an open tile beside an exposed ore face. Tracing to the ore centre makes native `CanHitLine` reject the tile it is meant to mine.
- **The pick formula was a copy once and it drifted in eight places** (wrong multipliers on Chlorophyte, Meteorite, tombstones and the altar, a depth gate borrowed from the Hellforge, spikes and the modded-tile power check missing). The game's `PickTile` is public and only its multiplayer branches are gated, so it is called, not copied; the formula stays private and is reached by a delegate. If tModLoader renames `GetPickaxeDamage`, the miner's constructor throws at load and the log names it.
- **`Approach` costs a walker search per distance-improving candidate**, which is why the search runs from the mine action on a trigger and a cooldown, never per tick.
