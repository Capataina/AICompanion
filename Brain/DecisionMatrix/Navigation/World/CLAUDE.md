# World — every tile question goes through one interface

The planner, the body and the follower never read `Main.tile`; they ask `ITileWorld`, and there are two answers to it: the game, and a text window. That is what lets the replay tool run the planner with no game, and it is the boundary the motor-only rule in `../CLAUDE.md` is enforced against: a search for Terraria type names under `Navigation/` may hit only `GameTileWorld.cs`.

```
World/
├─ CLAUDE.md
├─ ITileWorld.cs     the interface every tile question goes through (in world, shape, water, lava) and TileShape, the collision shape of a tile: air, a full block, a half block, a platform, or one of four slopes named by the solid triangle, numbered as the game numbers them — one member per shape, so a tile is never two of them at once
├─ GameTileWorld.cs  the live world from Main.tile; the one file here allowed to name a Terraria type; an actuated block is air, a platform is a top surface whatever its style (the game collides with a tile that is tileSolid, or tileSolidTop at frame 0, and treats any colliding tileSolidTop tile as a top surface; vanilla platforms carry both flags with their style in frameY, so a frame test on every solid-top tile made every non-default platform style air, the P1 of the 2026-09-08 Codex review), a hammered or worldgen-smoothed block carries its slope or half-block bits
└─ TextTileWorld.cs  a world read from a plan dump or scenario file, one character per tile, with the alphabet in one place (Glyph) that the telemetry writer also uses — one glyph per TileShape, so the corpus inherits the same limit the enum has; entity and trail lines come back as extras; outside the window is wall to the sides and above and open air below, and AskedOutside records that a search read past the edge, which is how the replay tells a pocket the world seals from one the capture cut
```

## A stair loses its platform-ness here, and that is a known capability gap

Because `TileShape` has one member per shape and `Platform` is not one of the four slope ids, a hammered platform — a stair — resolves to its slope alone. That is the right reading for standing on one: the engine's `Collision.SlopeCollision` acts on any tile carrying a slope id, so a body is rested on the tread's diagonal and a grid that believed a flat tread would prove poses the body never holds. What it costs is the drop: no fall-through edge is ever offered on a stair, because nothing in this reader can say "sloped *and* passable while pressing".

The game does grant that drop, and the comment that used to say otherwise here was wrong on no citation. `SlopeCollision` computes `flag2 = fall && TileID.Sets.Platforms[type]`, and every branch that would rest the body on the diagonal is skipped while `flag2` is set, so pressing down on a staircase passes it exactly as a player experiences (read from the decompile, 2026-09-09). The gap is therefore conservative rather than free, and it reaches the corpus as well: since `TextTileWorld`'s glyph alphabet is one character per `TileShape`, no scenario file can hold a stair either, so this is not a case the replay tool can be pointed at.

Closing it is not a change to this folder — the simulated body is stopped on a stair by geometry rather than by the platform catch a press disables, so the fall-through intent has to reach `BodyPhysics.Fits`, which every pose, jump arc, walk proof and descent runs through. `../CLAUDE.md`'s traps own that condition; teaching only the grid about stairs would offer an edge the body cannot perform, which is the exact defect this whole folder split exists to prevent.

The alphabet is the contract between the mod and the tool: `Glyph` is the only place a shape becomes a character, so the telemetry writer in `../../../Debug/` and the parser here cannot disagree. A window's edge is a wall to the sides and above and open air below, because a search that reads below the window is one that fell out of it, and the `AskedOutside` flag is how the replay tells "sealed by the world" from "sealed by the capture".
