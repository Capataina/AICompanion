# Chopping — trees and the axe

```
Chopping/
├─ CLAUDE.md
├─ TreeFinder.cs   which tile types are trees (IsATreeTrunk plus palm and cactus, like the vanilla axe code), the trunk bottom (GetTreeBottom returns the ground tile; TrunkBottom takes the row above), and the nearest tree with a standing spot
└─ TileChopper.cs  the companion's own HitTile and the vanilla axe formula (axe power × 1.2, × 3 on cactus); raises TileDamageWatcher.CompanionIsHitting around its KillTile so its own hits never read as the player's
```

The HitTile here is shared with `../Mining/TileMiner.cs`, so one cracks renderer draws both.
