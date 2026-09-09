# Map — how you find a companion that will not come to you

This folder exists because of one ruling: the companion never teleports. Everything else about it follows. A companion that always appears beside you needs no map presence at all; a companion that walks back from wherever it ended up needs to be findable, and watchable on the way, or being separated from it is indistinguishable from having lost it.

So the companion is drawn on the minimap and the full map as its own head, exactly the way a second player would be. That is the entire user-facing feature, and it turns "where did it go" from a lost-pet problem into information.

The second ruling shapes the other half. The companion reveals map as it travels, but **only what its torch actually lights** — never its whole screen — because a companion revealing a screen's worth of map would show the player what is behind walls, which is a bigger gift than the feature was meant to be. The reveal is therefore a light calculation, not a visibility rectangle, and that distinction is the whole design of `TorchMapReveal`.

```
Map/
├─ CLAUDE.md
├─ CompanionMapLayer.cs  the head on the minimap and the full map, with a name on hover
└─ TorchMapReveal.cs     what the torch lights becomes revealed map, falling off with distance
```

## How the reveal works, and why it is a flood rather than a lighting query

It floods outward from the torch's tile through non-solid tiles to the torch's reach, writing each air tile it reaches and the first solid face beyond it into the game's map, with light falling off by distance. Then it queues each changed tile into the game's own incremental map-update list, so only those tiles are redrawn.

Two properties come out of that shape rather than out of any rule written for them. Because it walks through open tiles only, a wall stops it, so nothing behind a wall is ever revealed — the privacy requirement is a consequence of the algorithm rather than a check. And because it reads *tiles* rather than asking the lighting engine, it works with the companion off screen, which matters constantly given the companion is frequently several screens away and that is exactly when the player wants to see where it has been.

## How it sits in the rest of the mod

```
Companion/CompanionBody  ──the head is drawn from──▶ the stand-in player's own look
Brain/WorldInteractions/Torch         ──only while lit──────────▶ the reveal runs at all
```

The reveal is gated on the torch genuinely being in the hand, not on darkness. That is the same rule the light itself follows, decided in `Companion/CompanionNPC.AI` after the brain has run: the torch is what the hand does when no action wanted it, so a companion mid-swing with a pickaxe is not lighting anything and therefore not revealing anything either. If the reveal were gated on ambient darkness instead, a companion would map a cave while holding a pickaxe, and the light on screen and the light on the map would disagree.

The head is drawn from the same stand-in player that `Companion/` maintains for rendering and enemy targeting, which is why this folder needs no sprite of its own and why its trap list mentions render targets.

## What it is not

- **Not a waypoint or ping system.** Nothing here lets the player mark a place or send the companion to one; the map is read-only information.
- **Not a second player marker.** The head is drawn by this mod's own map layer rather than by the game's player-head pass, because the stand-in is not an active player and the game's pass would not draw it.
- **Not fog-of-war for the companion.** The companion's own knowledge of the world is not modelled; the reveal writes into the *player's* map because that is the only map there is.
- **Not able to un-reveal.** The game's map lighting only ever raises a tile's light, which is also true of the player's own map, so a revealed tile stays revealed.

## Where it is going

If the companion ever takes orders about places — go here, wait there, meet me — this is the surface where a player would give them, and nothing here supports input of any kind today. The map layer draws and hovers; it does not click.

## Traps

- **The map overlay's position maths is `(tile - MapPosition) * MapScale + MapOffset`**, the same the vanilla map uses for player heads; `context.Draw` does it for a texture, the head renderer needs it done by hand.
- **`Main.Map.UpdateLighting` only ever raises a tile's light**, so the reveal cannot un-reveal; that is the game's own rule for the player's map too.
- **`Main.refreshMap` is the whole-map redraw, not the incremental one.** The game clears every map section and redraws them under a per-frame time budget; the first reveal set it on every pass and would have stalled the map for as long as the companion walked through unrevealed dark. The incremental path is MapHelper's `updateTileX/Y` queue, which `Main.DrawToMap` consumes on the next lighting export, and `refreshMap` is only its overflow fallback.
- **The map head renderer keeps one render target per player slot, sized to the player count, not the array.** A drawing-only player in the array's spare last slot throws on the first line of `DrawPlayerHead`; the body sits one slot lower. A renderer exception is logged once and latched, because a swallowed one made a never-working head look like an unverified one.
