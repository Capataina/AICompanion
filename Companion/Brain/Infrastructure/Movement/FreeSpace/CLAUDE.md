# Free space — what the orb may occupy, priced by how far the walls are, searched once

Everything that plans for the orb reads this folder's answer to one question, whether a place is free for the body, and walks one graph over the places that are. The reach sense in `../../Observation/` is a flood over this graph; the navigator's route is an A* over it; the positioner's candidates are its nodes. That is the reason the walker's five private searches are one search here: a question one flood has answered is not asked again per candidate.

```
FreeSpace/
├─ CLAUDE.md             this guide
├─ OrbTerrain.cs         free for the orb: not solid to the contact, whatever liquid is in it
├─ ClearanceField.cs     for every free tile, how far the nearest wall is, per chunk, kept while the world it describes stands
├─ ClearanceHeat.cs      that field plus live enemy boxes as one cost; parking, tool hovers and route edges all read it
├─ CornerGraph.cs        nodes are tile corners, eight-connected, usable when the four tiles around are free; the edge price
├─ FreeSpaceSearch.cs    one resumable best-first search: the flood with no goal, A* with one
├─ Reachability.cs       the three-valued answer every reach question passes around
└─ EstimateEnemyReach.cs estimates of what an enemy body can reach, for the threat sense; never proofs about the orb
```

## Free is one reading in one place

A tile is free for the orb when it is not solid under the contact's rules, and a wet tile is exactly as free as a dry one, because every liquid is air to this body. Until 15 September 2026 the rule also refused a liquid the body lacked immunity to, through a process-wide immunity setting the brain tick wrote every tick and a search could override for itself. With the immunity built in there is nothing to configure and no search carries rules of its own, so the flood, the route and the clearance field ask exactly what the contact collides with, and a flooded passage is a passage.

## Nodes are corners, and that is why a two-wide corridor is open

A twenty-pixel body centred on a sixteen-pixel tile in a two-tile corridor overhangs each wall by two pixels, so a grid of tile centres reports every two-wide corridor closed. The corner between the two tiles sits in the corridor's middle with room either side. A corner is usable when the four tiles around it are free, which is exactly the set of tiles a circle of the body's radius at that corner overlaps, and the graph is eight-connected. Every edge between two usable corners is clear for the body by construction — a cardinal edge's swept circle lies inside the six tiles the two footprints cover, and a diagonal edge reaches only the two step tiles beside it and passes each by more than the radius — so the search validates an edge by the usability of its far end alone; the swept test is spent where the theorem does not reach.

## The price is the corridor's middle

An edge costs its length times one plus a tunable over the clearance at its far corner, and the clearance is the field's: a distance transform over free tiles, per chunk, built on demand and kept while the world it describes stands. A chunk is rebuilt when the terrain revision has an edit inside the chunk's own reach or when the world object is replaced; asking about a tile far from every recent edit costs a dictionary lookup. The field's cap is eight tiles: beyond it the corridor-middle preference has nothing left to prefer, and a smaller cap makes every chunk cheaper to build by the square of the difference. Enemy bodies pay the same formula out to that cap, multiplied onto the edge, never as a wall: a two-tile crack stays legal. Parking and tool hovers read `ClearanceHeat.Combined` so the body stops in the same faint air the route prefers. The accompanying walk picks among its steps by that combined field. Combat HereAndCompany region samples call `PreferClearer(..., enemiesOnly: true)` so a perch is not an inflated hitbox; they do not walk off the floor, which broke P3. Every combat stand still crack-nudges one tile off terrain. The corridor fixture in `Tools/NavReplay` is the measurement that this price does what it says — a priced route down a corridor sits on its mid-line where the unpriced route hugs the wall it started beside — and its smoother row is the reason `../Steering/Route.Smooth` refuses a skip that would give the clearance back.

## One search, two shapes

`FreeSpaceSearch` is Dijkstra when it has no goal and A* with the straight-line heuristic when it has one; the heuristic is admissible because every edge costs at least its length. A third shape, a predicate goal with an ordering that leaned the flood toward the nearest dry cell, served only the environmental escape and went with it. It advances in slices bounded by an expansion budget and the shared deadline, keeps its frontier between slices, and finishes with a named stop: found, exhausted, the budget, the deadline, or the node limit, which is a backstop and not a bound anything relies on. Exhausted is the only stop that proves an absence; every other stop is the middle value of the three-valued answer. A goalless flood can be given a radius, a disc of straight-line distance around its start outside which nothing is queued, and then exhausted means every corner reachable inside the disc is closed. A corner inside the disc whose only route loops outside it reads as absent, so the guarantee is a detour bound and the reach sense states it where it consumes it: its known radius is half the disc, and a tile inside it that reads absent has no route shorter than about three times its straight line. A travel-cost ball would bound the detour at two, but only in unpriced units, and the reach flood keeps the clearance price on its edges so the estimates read off it stay the ones the brain was tuned against. A route search has a goal and no disc. A goalless flood can instead be given `Bounds`, a rectangle outside which nothing is queued, and exhausted then means every corner reachable without leaving the rectangle is closed. The intent sense uses it to ask which places inside the player's region join him without leaving the region, a question the body-rooted disc cannot answer, because a way round outside the region joins both sides of a wall inside it.

Every slice records the corners it read, and `Valid` asks the world's edit record whether anything landed inside that box since the search began, so a player mining a screen away does not restart a flood that never looked there. A search over a world that is not the process's live one names it in `WorldOverride`, which is how a headless tool runs the same class over a text world.

## Traps

- `Point.GetHashCode` is `X ^ Y`, which on a grid collapses thousands of corners onto a few hundred hashes and turns every set lookup into a chain walk; every dictionary and set here is keyed through `CornerKey`, and one that is not will make a flood that finishes in a slice spend the slice hashing.
- A flood's travel costs are upper bounds until it stops; a consumer pricing on an unfinished flood is pricing on bounds. The meeting-place chooser decides only on a finished flood for that reason.
- `EstimateEnemyReach` answers about enemy bodies for the threat sense and is deliberately not this graph: an enemy walker's reach is a floor question and the orb has no floor.
