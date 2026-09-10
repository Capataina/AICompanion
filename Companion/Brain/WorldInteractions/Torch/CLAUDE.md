# Torches — held light and permanent lighting

```
Torch/
├─ CLAUDE.md
├─ TorchBearer.cs             decides lit and shown, then drives held item, light and map reveal
├─ RecommendTorchPlacement.cs Smart Cursor's native torch-only recommendation with isolated scratch state
└─ PlaceSuppliedTorches.cs    native placement and successful-placement-only inventory consumption
```

`Lit` comes from world observation’s ambient light outside the companion’s own glow, with hysteresis and a minimum hold. `Shown` additionally requires a free hand and a dry body, following vanilla's ordinary-torch wet restriction. The NPC clears last tick's hand claim and calls `Hide` before resolving this tick, so a previous torch cannot veto its own fallback. Light, map reveal and held item follow that one answer; downed state never shows the torch.

Permanent placement is optional work selected in `Behaviours/Work`. It favours elevated positions to either side, asks the shared movement simulator whether a jump can reach an interaction and land safely, and places only from actual tool reach. The recommendation reuses `SmartCursorHelper.Step_Torch`, including the game's existing-torch spacing and attachment rules. Its private context is supplied directly because the public `SmartCursorLookup` reads and overwrites the local player's cursor. The wrapper restores the shared target list in `finally`; a changed private engine contract fails explicitly rather than silently inventing recommendations.

Usable torch items come from the companion bag first, then the player's inventory. Native `WorldGen.PlaceTile` remains the placement authority and a successful resulting tile is required before consuming one item. No supply or refused placement consumes nothing. The companion adds stricter limits to Smart Cursor: no replacing occupied terrain, no liquid placement, and no edits inside a protected bed room. These restrictions intentionally exclude water torches until underwater placement is part of the designed work kit.
