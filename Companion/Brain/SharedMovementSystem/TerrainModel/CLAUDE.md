# Terrain model — shape and pass-through are independent facts

The movement core asks ITileWorld about geometry, liquids and revision; it never reads Main.tile. A hammered platform retains both its slope and its downward pass-through rule, because changing its surface does not make it a solid block.

```
TerrainModel/
├─ CLAUDE.md          the terrain contract
├─ ITileWorld.cs      shape, pass-through, liquids, revision and an optional native simulation backend
└─ TextTileWorld.cs   captured terrain, marker parsing, glyphs and bounded scenario mutation
```

TerrariaIntegration supplies the live implementation. TextTileWorld supplies deterministic captured geometry. Its alphabet round-trips flat platforms, sloped platforms and pass-through half blocks as distinct glyphs; the recorder calls the same encoder. Marker glyphs represent air and must not replace a terrain feature in a fixture.

Outside a captured window, sides and ceiling are walls while the bottom is open. AskedOutside records whether an algorithm consulted missing terrain. A closed graph inside that window proves a result about this movement model; it does not prove physical impossibility in Terraria. Missing edges and unsupported abilities can also close a graph.

Tile revision changes invalidate retained execution proofs. Text-world Set increments it; the live adapter receives engine mutation events. Cached route proposals also have bounded expiry for terrain changes the engine does not announce.

## Traps

- Flattening a stair to its slope loses pass-through; flattening it to a platform loses its diagonal contact geometry. Both properties must reach fitting, support and descent queries.
- A platform’s style can change its frame without changing collision. Read the live adapter’s tile flags, rather than treating every non-zero frame as air.
- A captured tile records one collision shape and pass-through property. The old glyph format cannot recover an absent property; reshape old captures from a saved world when needed.
