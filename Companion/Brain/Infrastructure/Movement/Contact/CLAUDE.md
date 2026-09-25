# Contact — the orb's one collision with terrain

The body is a circle, and this folder is the whole of how it meets a wall: the circle of the body's radius against the rectangle of every solid tile its bounding box overlaps, pushed out along the closest-point normal, with the component of velocity into each wall killed and the component along it kept. It runs in the motor after the tick's velocity is decided, and identically in every headless tool, so there is one body and no parity to measure.

```
Contact/
├─ CLAUDE.md          this guide
└─ CircleContact.cs   the radius and diameter, what is solid to the body, push-out and slide, clearance, the touch test and the swept-segment test
```

## Why a circle, and what the size rule is

A twenty-pixel box centred on a tile corner cannot pass a one-tile diagonal step, because its corner catches on the step's corner; a twenty-pixel circle passes the same step with a few pixels to spare, and only a box of sixteen pixels or less would, which is exactly the one-tile gap the body must not fit. The acceptance test for the body is therefore the size rule the circle satisfies: it fits every two-by-two gap in every direction and no one-by-one gap in any, and `Tools/EngineReplay`'s orb-contact fixture drives the real motor through both.

The engine's box (`NPC.width` and `height`) must stay equal to the diameter, because the box is what enemies and projectiles hit, and a box a different size from the circle is a body hit where it is not and missed where it is.

## What is a wall here, and what deliberately is not

Solid is a full block, a half block and every slope — the owner ruled that slopes are full tiles to this body — with a platform passable, and a closed door solid until the door interaction opens it. Outside the world is solid.

**A platform is passable on two readings, and it needs both.** The shape settles the ordinary platform, because the ruling is that platforms are passable to this body whatever a world says about fall-through; the world's own pass-through flag settles a tile hammering gave a slope or half shape while it kept its fall-through, which no shape compare can see. Asking the flag alone was the whole of the test until 22 September 2026, and the pair it left open — a world reporting `Platform` without the flag — made a platform a wall to the body. No world in this tree produces that pair: the two that read terrain of their own cannot (`GameTileWorld.Shape` returns `Platform` only where `PassThrough` is true; `TextTileWorld` maps `=` to `Platform` with the flag set), and the third, `CaptureCourseTravel.ReadTravelTerrain`, forwards both answers to whatever it decorates, so it inherits the property rather than holding one. So the shape clause changed no behaviour when it landed; it is there because the combination is representable through `ITileWorld`, the recorder already names it (`RecordBrainTelemetry` writes `solid-platform` for exactly it), and the ruling must not be reversible by a world nobody is looking at.

**One predicate, and every reader of tile solidity for the body reaches it.** `OrbTerrain.Solid` is `CircleContact.Solid` by delegation, and the corner graph's usability test, the clearance field's build loop, the route search, the reach flood and the positioner's candidates all go through `OrbTerrain.Free`. So the contact's clearance and the field the park prices against cannot disagree about a tile: they are the same answer twice. The 2026-09-22 capture was read as the clearance column being blind to platforms while the contact treated one as a wall; the column *is* `CircleContact.Clearance`, and the reconstruction that matched it to a median 0.10 px over 1,196 ticks is therefore evidence that the code already agrees with itself, not against it. `Tools/EngineReplay/Movement/VerifyOrbContact.PlatformsAreAir` holds all of it, and removing the shape clause reddens it.

Liquid is not a wall to the contact, nor to the planner, nor a hurt to the motor: every liquid is air to this body. Making liquid a contact wall was considered and refused while water and lava still hurt: a body pushed out of a pool it was knocked into would be pushed toward whichever wet tile's normal won, which in a pool is up through the surface at best and into the pool's wall at worst.

## The four questions the folder answers

`Resolve` is the push-out, iterated a bounded number of passes because resolving one tile can move the circle into another; a body still overlapping after the cap is reported wedged, which recovery clearance reads. `Clearance` is the distance from the circle's edge to the nearest wall within a short reach, zero for a body overlapping terrain, capped beyond the reach because past it the answer is "far". `Touches` is whether the circle overlaps any tile a predicate accepts, counting a touch at the boundary — the liquid test, where a wet tile is a region and not a wall. `SweptClear` is whether the circle can travel a straight segment without meeting a wall, continuous along the segment rather than sampled at the endpoints, because the diagonal between two corners passes a one-tile step by a margin that lives at the segment's midpoint and a test that only looked under the endpoints would never see it.

**`SweptClear` reads only the tiles of its box that could be within the radius of the segment**, passing the rest by on a centre-distance bound (the radius plus the tile's half-diagonal, rounded up to twelve pixels) before any tile read, and the exact segment-to-rectangle distance still decides every tile the bound admits, in the same order, so the answer and the first wall found are unchanged. A long diagonal chord's box is mostly far tiles: the smoother tests chords up to 48 corners long, and reading every tile of those boxes was 0.9 ms a route late in the 25 September 2026 capture's replay, more than the route search itself. The bound is a geometric claim, and `Tools/NavReplay`'s swept-test row checks it against the exact distance over four thousand seeded segments, degenerate ones included; a margin of four pixels, below the half-diagonal, reddens it with sixty-five thousand tiles wrongly skipped.

## Traps

- The push-out normal is the closest-point normal, so a circle exactly at a tile's corner is pushed diagonally; the bounded pass count is what stops a body wedged between three tiles spinning for ever, and the wedged flag is what tells recovery it needs to eject rather than slide.
- `Clearance` reads the contact's own wall rule, so it is zero at a platform's edge only when the platform is a wall to the body, which it never is; a consumer wanting distance from any support reads the terrain model directly. The consequence a reader of a capture has to hold: a body sitting at or through a platform reads its clearance against whatever *else* is near, so "clearance 7 px" beside a platform is the distance to the wall and says nothing about the platform the body is inside. To a player that body is sinking into the floor, and no column in the record calls it a touch. `MovementQueries.IsSupport` is the other question — `Shape != Air`, so a platform counts — and it is deliberately not this one.
