# Map — the companion on the world map

Two rulings from 2026-09-08 shape this folder. The companion never teleports, so the player must be able to find it; and it reveals the map only with what its torch actually lights, because revealing its whole screen would expose what is behind walls.

```
Map/
├─ CLAUDE.md
├─ CompanionMapLayer.cs  a ModMapLayer: draws the body's head through Main.MapPlayerRenderer on the minimap and the full map, Guide head as the fallback, "Companion" on hover
└─ TorchMapReveal.cs     breadth-first flood from the torch's tile through non-solid tiles to its reach, writing each air tile and the first solid face to Main.Map with light falling off by distance; reads tiles, not the lighting engine, so it works off screen
```

## Traps

- **The map overlay's position maths is `(tile - MapPosition) * MapScale + MapOffset`**, the same the vanilla map uses for player heads; `context.Draw` does it for a texture, the head renderer needs it done by hand.
- **`Main.Map.UpdateLighting` only ever raises a tile's light**, so the reveal cannot un-reveal; that is the game's own rule for the player's map too.
