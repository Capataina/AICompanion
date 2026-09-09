# Mining — ores and the pickaxe

```
Mining/
├─ CLAUDE.md
├─ OreFinder.cs   which tiles are ore (TileID.Sets.Ore), a bounded 8-connected vein, the nearest ore of a preferred type outside a vein then any ore, each with a standing spot inside the player's own reach box (Player.tileRangeX by tileRangeY) whose eye has a line to it and that the walker can reach from where the companion stands
└─ TileMiner.cs   runs the game's own Player.PickTile on the companion's drawing-only player, so the damage formula, the power gates, a modded tile's power check, the crack table and the break are the game's; CanMine asks the game's private damage formula through a delegate bound to that player
```

## Traps

- **`OreFinder.Approach` and `InReach` share one reach test** (the player's reach box from an eye above the feet, plus a sight line), so a target found is a target the swing reaches; changing one without the other makes the miner walk to a spot it cannot mine from. The mine action still re-checks reach on arrival and drops the tile rather than swing at what it cannot hit.
- **The pick formula was a copy once and it drifted in eight places** (wrong multipliers on Chlorophyte, Meteorite, tombstones and the altar, a depth gate borrowed from the Hellforge, spikes and the modded-tile power check missing). The game's `PickTile` is public and only its multiplayer branches are gated, so it is called, not copied; the formula stays private and is reached by a delegate. If tModLoader renames `GetPickaxeDamage`, the miner's constructor throws at load and the log names it.
- **`Approach` costs a walker search per distance-improving candidate**, which is why the search runs from the mine action on a trigger and a cooldown, never per tick.
