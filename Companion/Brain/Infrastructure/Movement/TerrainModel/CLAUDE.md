# Terrain model — shape and pass-through are independent facts

The movement core asks ITileWorld about geometry, liquids and revision; it never reads Main.tile. A hammered platform retains both its slope and its downward pass-through rule, because changing its surface does not make it a solid block.

```
TerrainModel/
├─ CLAUDE.md             the terrain contract
├─ ITileWorld.cs         shape, pass-through, liquids, revision, where the recent revisions happened, and an optional native simulation backend
├─ RecordTerrainEdits.cs `TerrainEditLog`, the revision counter beside a bounded ring of the tiles behind it, and `TerrainEditVerdict`, the three-valued answer it gives about a region — neither type is named for the file, so search for the types
└─ TextTileWorld.cs      captured terrain, marker parsing, glyphs and bounded scenario mutation
```

**A world says where it changed as well as how often, and each world keeps its own record.** `ITileWorld.ChangedSince` takes a predicate over tiles rather than a rectangle, so a consumer asks its own question — the region it explored, inflated by however far that region's geometry reads — and the record walks its recent edits against it rather than the consumer walking its region against the record. Its default implementation is the conservative one, any difference in the counter being a change, so a world with no record behaves exactly as everything did before one existed and an immutable fixture at revision zero answers unchanged for ever. The live adapter delegates to `TerrainChanges`, and a text world keeps an instance of its own: feeding two worlds' bumps into one ring would let a query rooted in one be answered against edits made in the other, and the world-identity check that guards against this is upstream of the counters rather than inside them.

**The answer has three values and a caller acts on two of them.** `Unchanged` is the only one that keeps retained work; `Changed` names an edit the predicate accepted, or a wholesale reset; `TooOld` says the asked-about revision is older than the ring reaches back, so nothing is known about the gap. `TooOld` must be treated exactly as `Changed` and is named apart only so a diagnostic can tell a real edit from a lost window. The safe direction is always changed — a wrongly clean answer serves a consumer terrain from before a dig it could not see, while a wrongly changed one costs a restart.

The window is the record's one real limit, and it is sized by what a consumer can miss between two checks rather than by how long a consumer lives, because a clean check moves the consumer's own revision forward. Where the window sits is a constant in `TerrainEditLog`, not a figure to quote elsewhere.

**A reset is not a rewind: the revision is monotonic across one.** `Reset` — a world loading or unloading, or a fixture rebuilding its scene — bumps the counter and raises a floor at the new value, so every earlier revision is unanswerable rather than comparing equal to something a consumer has already seen. A consumer that stamps work with a revision and compares that stamp later is therefore safe across a reset, and a consumer that discards its memory on a reset is belt and braces rather than the guard. Position's refusal memory was bitten by the opposite assumption while this guarantee was absent.

**`ChangedSince` is opt-in, and a consumer that only compares `Revision` still restarts on any edit anywhere.** The counter did not go away and most of the movement stack still reads it that way — retained control sequences, the local macro and preparation proofs, the navigator's failure caches and its executed-traversal record, the motor's divergence reason, the threat sense's reach memory and the positioner. The retained route search in `../RoutePlanning/` is today the only consumer that asks *where*. Reading "invalidation is spatial now" as a property of this folder is the misreading to avoid: this folder offers the spatial question and each consumer chooses whether to ask it.

TerrariaIntegration supplies the live implementation. TextTileWorld supplies deterministic captured geometry. Its alphabet round-trips flat platforms, sloped platforms and pass-through half blocks as distinct glyphs; the recorder calls the same encoder. Marker glyphs represent air and must not replace a terrain feature in a fixture.

The optional body backend supplies both one-tick simulation and current gravity. A consumer can propose an appropriately sized jump without importing game types or duplicating the native altitude formula. Current gravity is an estimate for that proposal, not a promise that acceleration stays constant along the route.

Outside a captured window, sides and ceiling are walls while the bottom is open. AskedOutside records whether an algorithm consulted missing terrain. A closed graph inside that window proves a result about this movement model; it does not prove physical impossibility in Terraria. Missing edges and unsupported abilities can also close a graph.

Tile revision changes invalidate retained execution proofs, and a consumer that knows which tiles it read invalidates only for an edit that landed on one of them. `TextTileWorld.Set` records the tile it wrote; the live adapter records the tile each engine mutation event named.

**Nothing here can defend against a change the engine never announces, because an unannounced change has no tile to record and does not move the counter either.** A liquid settling is the standing case. What catches that is not this record but the swept-area fingerprint over tile shape, platform state and liquid kind and amount that executed-route memory recomputes before reusing a connection, described in `../RoutePlanning/`.

## Traps

Liquid kind and quantity are independent fingerprint inputs for executed-route memory. The live adapter provides the engine's exact values; older text captures can only supply the type and full-cell amount their glyph encodes. That reduced fidelity must not be described as a complete liquid reconstruction.

- Flattening a stair to its slope loses pass-through; flattening it to a platform loses its diagonal contact geometry. Both properties must reach fitting, support and descent queries.
- A platform’s style can change its frame without changing collision. Read the live adapter’s tile flags, rather than treating every non-zero frame as air.
- A captured tile records one collision shape and pass-through property. The old glyph format cannot recover an absent property; reshape old captures from a saved world when needed.
