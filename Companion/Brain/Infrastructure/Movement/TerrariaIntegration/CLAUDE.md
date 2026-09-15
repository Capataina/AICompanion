# Terraria integration — live terrain, the edit announcements, and the motor

This is the only shared-movement folder allowed to name Terraria types (`Tools/check-navigation-boundary.sh` refuses the name anywhere else in the core). It adapts the live tile map to the core's terrain contract, announces the world's edits into the core's record, and holds the motor, which is the only component that writes the NPC's velocity.

```
TerrariaIntegration/
├─ CLAUDE.md                     this boundary
├─ ReadGameTerrain.cs            GameTileWorld: shape and pass-through read separately from Main.tile, liquids, and the edit record
├─ ApplyControlsToCompanion.cs   CompanionMotor: momentum toward the requested velocity, the contact, the liquid reading and its hurt, the pace, recovery flight and the downed sink
└─ TrackTerrainChanges.cs        `TerrainChanges`, the one announcement point and the owner of the door detours; `TrackTerrainChanges`, the GlobalTile hooks that announce placement, breaking, hammering and wiring; `ResetTerrainChanges`, the ModSystem that resets the record on world load and unload and installs the door detours at mod load
```

## The motor

Each tick the motor moves its own momentum toward the velocity the brain asked for under `../Steering/OrbPace.Step` — the change across the heading capped by the turn authority and the change along it by the slower speed change, or by the turn authority on both when the controls burst or the body is downed — caps the speed, runs the circle contact on where that velocity would put the body, reads which liquid the resolved position touches, and writes the resolved displacement as the NPC's velocity. The engine, with tile collision switched off for this body, adds it and does nothing else. The momentum is the motor's and not the NPC's velocity: the displacement written after a push-out is what contact left, and reading it back as the next tick's velocity would turn a push-out into a bounce. What changed the NPC's velocity from outside between two ticks — a knockback, a hit, a fixture writing it by hand — is folded in as the difference between what was applied and what is read back.

The pace is the player's own: the cap a multiple of his maximum run speed after accessories, and the turn authority and the speed change each a multiple of his run acceleration, all times a multiplier on the body that the mastery tree drives, so a companion at the cap overtakes a running player and nothing here lags him. All three are published to `../Steering/OrbPace` every tick before anything plans, so the game-free steering eases and bends against the numbers this tick will apply.

Liquid is read here because the engine reads it inside the collision it no longer runs for this body, and it is written to the NPC's own wet flags because everything from hit effects to the senses reads them. Water and lava hurt on contact, a fixed amount every fixed interval of contact, with the two pairs living on the body; honey and shimmer only scale the tick's displacement. A downed body takes no liquid damage and sinks at a fixed pace until the contact rests it on the floor, where the player can reach it to revive it.

`Track`, at the top of the tick, reads what the engine did with the last application: a body holding a velocity that did not move is pinned, which a body the engine only integrates cannot do on its own, and the count of such ticks is the record's `pinned` column and the state search's "cannot act" flag.

Recovery flight is the one mode outside the contact: a continuous phasing flight toward the owner while the coordinator keeps it active. Its cancellation for a downed companion or a dead owner does not leave a body inside terrain — while the phasing body overlaps rock the motor moves it continuously back toward the last clear position it observed, then hands back to ordinary contact — and that clearance is ejection, never a second owner-seeking path.

## Edits enter the core here, and the announcement is a closed list

`TerrainChanges.Changed(x, y)` records the tile in the world's edit log; every retained consumer — the search, the route, the clearance field's chunks — asks that record whether an edit landed inside what it read. What announces: the `GlobalTile` hooks for wiring, sloping, placement and a kill that actually succeeded (a pickaxe hit that only cracks a tile is not a change); detours on the game's own door helpers, which write tiles through none of those hooks, running the original first and announcing only on a true return because the swing decision is the game's own; the door adapter in `../../Interactions/Doors/`, by hand; and the fixtures. An edit made by any other route is invisible downstream, which is the failure to look for first when retained work survives an edit it should not have.

The detour and the adapter both fire for a companion toggle, so those rows are announced twice; that is a known duplicate costing a few ring entries per door toggle, and the fix is deleting the adapter's own loops, which belongs to that folder.

**Who installs the detours is split, and both halves are real.** `ResetTerrainChanges.Load` and `Unload` own them in the game. Headlessly `ModSystem.Load` never runs, so a fixture that needs them installs and removes them itself, in a finally, because a detour left standing changes the revision counts of every door fixture after it. The generated `On_` types live in `Libraries/TerrariaHooks/`, which the mod build finds through the whole `Libraries` tree and which `Tools/EngineReplay` names explicitly.

## Traps

- `Main.tileSolid` alone is not a wall: worldgen smooths cave corners into slopes and half blocks, and every tile question goes through `GameTileWorld.Shape`, which reads the slope and half-block fields. Slopes are solid to the orb, so the distinction matters less than it did for the walker, but a platform hammered into a slope is still passable and only the shape reader knows.
- `Tile` is a struct view returned by value from the tile map's indexer: a fixture that writes a field through the indexer expression writes to a copy.
- The motor is the only writer. A behaviour, reflex or Terraria callback that writes position or velocity directly bypasses the contact and the record's `moved` and `pinned` columns, which is exactly the class those columns exist to catch.
