# Terrain model — shape and pass-through are independent facts, and a world says where it changed

The movement core asks `ITileWorld` about geometry, liquids and revision; it never reads `Main.tile`. A hammered platform retains both its slope and its downward pass-through rule, because changing its surface does not make it a solid block, and that is the fact the contact's wall rule depends on: a platform is passable whatever shape it has.

```
TerrainModel/
├─ CLAUDE.md             the terrain contract
├─ ITileWorld.cs         shape, pass-through, liquids, revision, and where the recent revisions happened
├─ RecordTerrainEdits.cs `TerrainEditLog`, the revision counter beside a bounded ring of the tiles behind it, and `TerrainEditVerdict`, the three-valued answer it gives about a region — neither type is named for the file, so search for the types
└─ TextTileWorld.cs      captured terrain, marker parsing, glyphs and bounded scenario mutation
```

**A world says where it changed as well as how often, and each world keeps its own record.** `ITileWorld.ChangedSince` takes a predicate over tiles rather than a rectangle, so a consumer asks its own question — the box it explored, inflated by however far its geometry reads — and the record walks its recent edits against it rather than the consumer walking its region against the record. Its default implementation is the conservative one, any difference in the counter being a change, so an immutable fixture at revision zero answers unchanged for ever. The live adapter delegates to `TerrainChanges`, and a text world keeps an instance of its own: feeding two worlds' bumps into one ring would let a query rooted in one be answered against edits made in the other.

**The answer has three values and a caller acts on two of them.** `Unchanged` is the only one that keeps retained work; `Changed` names an edit the predicate accepted, or a wholesale reset; `TooOld` says the asked-about revision is older than the ring reaches back, so nothing is known about the gap. `TooOld` must be treated exactly as `Changed` and is named apart only so a diagnostic can tell a real edit from a lost window. The safe direction is always changed — a wrongly clean answer serves a consumer terrain from before a dig it could not see, while a wrongly changed one costs a restart. The window is sized by what a consumer can miss between two checks rather than by how long it lives, because a clean check moves its own revision forward; where the window sits is a constant in `TerrainEditLog`.

**A reset is not a rewind: the revision is monotonic across one.** A world loading or unloading, or a fixture rebuilding its scene, bumps the counter and raises a floor at the new value, so every earlier revision is unanswerable rather than comparing equal to something a consumer has already seen.

**Every retained consumer of the orb asks where.** The free-space search records the box it explored, the route keeps the box it was planned over, the clearance field rebuilds a chunk only for an edit inside the chunk's reach, and the reach sense stands on the search's own validity. There is no consumer left that restarts on any edit anywhere, which was the walker's state for most of its stack.

`TerrariaIntegration/` supplies the live implementation. `TextTileWorld` supplies deterministic captured geometry. Its alphabet round-trips flat platforms, sloped platforms and pass-through half blocks as distinct glyphs; the recorder's terrain snapshots and plan dumps call the same encoder, which is what lets a scenario be cut out of a capture. A liquid glyph is water or lava with a full cell; the text world cannot express a partial amount or honey and shimmer, which is reduced fidelity rather than a complete liquid reconstruction. Outside a captured window every side is a wall, and a search that reached a window's edge has met the recording's limit rather than the world's.

**Nothing here can defend against a change the engine never announces, because an unannounced change has no tile to record and does not move the counter either.** A liquid settling is the standing case, and it costs the orb's routes nothing, because every liquid is air to the orb and no search reads liquid; it matters to the consumers that still read it, such as where a torch may go or where a drop will land, and each of those rereads the tile when it acts.

## Traps

- Marker glyphs are air. A fixture that writes an actor marker over a slope or half block erases the terrain feature under it; move actors through the header, never by writing a marker on the grid.
- `TextTileWorld.Set` records the tile it wrote, so a scenario mutated in a fixture invalidates the same way a mined world does; a fixture that rebuilds the whole world calls `Reset` so nothing planned over the old one survives.
