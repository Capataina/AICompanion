# Work — the tools an action drives

A tool does one kind of work with the game's own formulas and never decides when; the decision is an action in `../Actions/Work/`. One subfolder per kind of work, so a new kind (planting, fishing, building) is a new folder here and a new action there.

```
Work/
├─ CLAUDE.md
├─ Chopping/             trees: which tiles are trees, the trunk bottom, the nearest tree with a standing spot, and the axe formula over the companion's own HitTile
├─ Mining/               ores: which tiles are ore, a bounded vein, the nearest ore outside a vein with a standing spot in pickaxe reach, and the pickaxe formula over the shared HitTile
├─ Torch/                the torch ability: lit from the light sense with hysteresis, emits light, reveals the map
└─ TileCracksRenderer.cs draws the shared cracks with Main.DrawTileCracks from PostDrawTiles, cancelling the offScreenRange offset
```

The torch is the one tool without an action: it is what the hand does when no action wants it, decided in `CompanionNPC.AI` after the brain, so an action that wants empty hands in the dark must hold something else (none does today).
