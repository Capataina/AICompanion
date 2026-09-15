# Free space — what the orb may occupy, priced by how far the walls are, searched once

Everything that plans for the orb reads this folder's answer to one question, whether a place is free for the body, and walks one graph over the places that are. The reach sense in `../../Observation/` is a flood over this graph; the navigator's route is an A* over it; the positioner's candidates are its nodes. That is the reason the walker's five private searches are one search here: a question one flood has answered is not asked again per candidate.

```
FreeSpace/
├─ CLAUDE.md             this guide
├─ OrbTerrain.cs         free for the orb: not solid to the contact, not wet with a liquid the body is not immune to; the shared immunities
├─ ClearanceField.cs     for every free tile, how far the nearest wall is, per chunk, kept while the world it describes stands
├─ CornerGraph.cs        nodes are tile corners, eight-connected, usable when the four tiles around are free; the edge price
├─ FreeSpaceSearch.cs    one resumable best-first search: the flood with no goal, A* with one, a predicate goal for the nearest safe place
├─ Reachability.cs       the three-valued answer every reach question passes around
└─ EstimateEnemyReach.cs estimates of what an enemy body can reach, for the threat sense; never proofs about the orb
```

## Free is one reading in one place

A tile is free for the orb when it is not solid under the contact's rules and not wet with a liquid the body lacks immunity to. The immunities are process-wide (`OrbTerrain.Immunity`), written by the brain tick from the body's flags and by a headless tool for its scene, so a mastery immunity opening water changes the flood, the route and the clearance field for every consumer at once, and nothing has to be told. A search may run under its own immunities instead — the escape does, because a body already in water needs a flood that admits the corner it stands on — and says so, so its validity check does not refuse it for disagreeing with the process rule.

## Nodes are corners, and that is why a two-wide corridor is open

A twenty-pixel body centred on a sixteen-pixel tile in a two-tile corridor overhangs each wall by two pixels, so a grid of tile centres reports every two-wide corridor closed. The corner between the two tiles sits in the corridor's middle with room either side. A corner is usable when the four tiles around it are free, which is exactly the set of tiles a circle of the body's radius at that corner overlaps, and the graph is eight-connected. Every edge between two usable corners is clear for the body by construction — a cardinal edge's swept circle lies inside the six tiles the two footprints cover, and a diagonal edge reaches only the two step tiles beside it and passes each by more than the radius — so the search validates an edge by the usability of its far end alone; the swept test is spent where the theorem does not reach.

## The price is the corridor's middle

An edge costs its length times one plus a tunable over the clearance at its far corner, and the clearance is the field's: a distance transform over free tiles, per chunk, built on demand and kept while the world it describes stands. A chunk is rebuilt when the terrain revision has an edit inside the chunk's own reach, when the immunities change what counts as a wall, or when the world object is replaced; asking about a tile far from every recent edit costs a dictionary lookup. The field's cap is a structural constant rather than a tunable: beyond it the corridor-middle preference has nothing left to prefer, and a smaller cap makes every chunk cheaper to build by the square of the difference. The corridor fixture in `Tools/NavReplay` is the measurement that this price does what it says — a priced route down a corridor sits on its mid-line where the unpriced route hugs the wall it started beside — and its smoother row is the reason `../Steering/Route.Smooth` refuses a skip that would give the clearance back.

## One search, three shapes

`FreeSpaceSearch` is Dijkstra when it has no goal and A* with the straight-line heuristic when it has one; the heuristic is admissible because every edge costs at least its length. A predicate goal makes it the nearest-safe-place search, with an optional ordering that leans the flood toward what the caller prefers. It advances in slices bounded by an expansion budget and the shared deadline, keeps its frontier between slices, and finishes with a named stop: found, exhausted, the budget, the deadline, or the node limit that bounds a flood over a whole loaded world. Exhausted is the only stop that proves an absence; every other stop is the middle value of the three-valued answer.

Every slice records the corners it read, and `Valid` asks the world's edit record whether anything landed inside that box since the search began, so a player mining a screen away does not restart a flood that never looked there. A search over a world that is not the process's live one names it in `WorldOverride`, which is how a headless tool runs the same class over a text world.

## Traps

- `Point.GetHashCode` is `X ^ Y`, which on a grid collapses thousands of corners onto a few hundred hashes and turns every set lookup into a chain walk; every dictionary and set here is keyed through `CornerKey`, and one that is not will make a flood that finishes in a slice spend the slice hashing.
- A flood's travel costs are upper bounds until it stops; a consumer pricing on an unfinished flood is pricing on bounds. The meeting-place chooser decides only on a finished flood for that reason.
- `EstimateEnemyReach` answers about enemy bodies for the threat sense and is deliberately not this graph: an enemy walker's reach is a floor question and the orb has no floor.
