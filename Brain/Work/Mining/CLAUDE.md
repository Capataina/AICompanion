# Mining — ores and the pickaxe

```
Mining/
├─ CLAUDE.md
├─ OreFinder.cs   which tiles are ore (TileID.Sets.Ore), a bounded 8-connected vein (400 tiles), the nearest ore of a preferred type outside a vein then any ore, each with a standing spot inside pickaxe reach (5 tiles, Player.tileRangeX) whose eye has a line to it
└─ TileMiner.cs   Player.GetPickaxeDamage copied (per-type multipliers, minimum-power gates by tile and depth, ModTile.MineResist, the Get-Good-World doubling) over the chopper's HitTile
```

## Traps

- **`OreFinder.Approach` and `InReach` use the same box, 5 tiles plus half a tile from an eye 30 px above the feet**, so a target found is a target the swing reaches; changing one without the other makes the miner walk to a spot it cannot mine from.
- **The pick formula is a copy, not a call**, because `Player.GetPickaxeDamage` is private and bound to a Player. When tModLoader changes it, this file is where the drift shows; the numeric tile ids in the original are the `TileID` names here.
