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

Solid is a full block, a half block and every slope — the owner ruled that slopes are full tiles to this body — with a platform passable whatever shape hammering gave it, because the world's own pass-through flag is asked rather than the shape compared, and a closed door solid until the door interaction opens it. Outside the world is solid.

Liquid is not a wall to the contact, nor to the planner, nor a hurt to the motor: every liquid is air to this body. Making liquid a contact wall was considered and refused while water and lava still hurt: a body pushed out of a pool it was knocked into would be pushed toward whichever wet tile's normal won, which in a pool is up through the surface at best and into the pool's wall at worst.

## The four questions the folder answers

`Resolve` is the push-out, iterated a bounded number of passes because resolving one tile can move the circle into another; a body still overlapping after the cap is reported wedged, which recovery clearance reads. `Clearance` is the distance from the circle's edge to the nearest wall within a short reach, zero for a body overlapping terrain, capped beyond the reach because past it the answer is "far". `Touches` is whether the circle overlaps any tile a predicate accepts, counting a touch at the boundary — the liquid test, where a wet tile is a region and not a wall. `SweptClear` is whether the circle can travel a straight segment without meeting a wall, continuous along the segment rather than sampled at the endpoints, because the diagonal between two corners passes a one-tile step by a margin that lives at the segment's midpoint and a test that only looked under the endpoints would never see it.

## Traps

- The push-out normal is the closest-point normal, so a circle exactly at a tile's corner is pushed diagonally; the bounded pass count is what stops a body wedged between three tiles spinning for ever, and the wedged flag is what tells recovery it needs to eject rather than slide.
- `Clearance` reads the contact's own wall rule, so it is zero at a platform's edge only when the platform is a wall to the body, which it never is; a consumer wanting distance from any support reads the terrain model directly.
