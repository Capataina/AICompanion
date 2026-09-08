# Work — tools for doing what the player does

```
Work/
├─ CLAUDE.md
├─ TreeFinder.cs         which tile types are trees (IsATreeTrunk plus palm and cactus, like the vanilla axe code), the trunk bottom (GetTreeBottom returns the ground tile; TrunkBottom takes the row above), and the nearest tree with a standing spot
├─ TileChopper.cs        the companion's own HitTile and the vanilla axe formula; a tool the chop action drives; raises TileDamageWatcher.CompanionIsHitting around its KillTile
├─ TileMiner.cs          the pickaxe: Player.GetPickaxeDamage copied (per-type multipliers, minimum-power gates, ModTile.MineResist), sharing the chopper's HitTile so both crack tables draw
├─ OreFinder.cs          which tiles are ore (TileID.Sets.Ore), a bounded 8-connected vein, the nearest ore of a preferred type outside a vein then any ore, each with a standing spot inside pickaxe reach (5 tiles) that has a line to it
├─ TorchBearer.cs        the torch ability: lit when the light sense's ambient reading falls under 0.22, out when it rises over 0.42, never switching within 3 s of the last switch; emits TorchID.Torch's colour at the hand and reveals the map every 10 ticks
└─ TileCracksRenderer.cs draws the shared cracks with Main.DrawTileCracks from PostDrawTiles, cancelling the offScreenRange offset
```

Tools here do the work; the decision to use them is an action in `../Brain/Decision/Actions/`. The torch is the one exception: it has no action, because it is what the hand does when no action wants it, decided in `CompanionNPC.AI` after the brain.

## Traps

- **Whether the torch shows is decided by the held item being None after the brain ticked**, so an action that wants empty hands in the dark must hold something else; none does today.
- **`OreFinder.Approach` and `InReach` use the same box, 5 tiles plus half a tile from an eye 30 px above the feet**, so a target found is a target the swing reaches; changing one without the other makes the miner walk to a spot it cannot mine from.
