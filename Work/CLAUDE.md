# Work — tools for doing what the player does

```
Work/
├─ CLAUDE.md
├─ TreeFinder.cs         which tile types are trees (IsATreeTrunk plus palm and cactus, like the vanilla axe code), the trunk bottom (GetTreeBottom returns the ground tile; TrunkBottom takes the row above), and the nearest tree with a standing spot
├─ TileChopper.cs        the companion's own HitTile and the vanilla axe formula; a tool the chop action drives; raises TreeDamageWatcher.CompanionIsHitting around its KillTile
└─ TileCracksRenderer.cs draws the chopper's cracks with Main.DrawTileCracks from PostDrawTiles, cancelling the offScreenRange offset
```

Mining and planting go here when they arrive, as tools; the decision to use them is an action in `../Brain/Decision/Actions/`.
