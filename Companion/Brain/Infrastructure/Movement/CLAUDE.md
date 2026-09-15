# Shared movement system — one request surface, a game-free core, one motor

This subsystem turns a place the brain wants the orb to be into a velocity the motor applies. `CoordinateMovement` is the only request surface a brain stage may use and `MovementQueries` is their read-only geometry surface; the motor in `TerrariaIntegration/` is the only component that writes the NPC. Everything between the two — the contact, the free-space graph, the search, the route, the steering law — is game-free and compiles into `Tools/NavReplay` without Terraria, so the search the mod runs and the search a headless tool runs are one search rather than a copy.

```
Movement/
├─ CLAUDE.md                 this guide
├─ CoordinateMovement.cs     the request surface: MoveTo, Hold, SeekDestination, SeekState, AvoidThreats, Configure
├─ MovementQueries.cs        the read-only geometry surface: the world, tile and corner questions, hover points
├─ LimitPlanningWork.cs      the shared millisecond allowance every search reads, and the switch that lifts it offline
├─ Contact/                  the circle-against-tiles contact, the one body the orb has
├─ FreeSpace/                what is free for the orb, the clearance field, the corner graph and the one resumable search
├─ Steering/                 the body's state and controls, the route, the steering law, the navigator, the pace and the census
├─ TerrainModel/             the game-free tile interface, the text world, and the record of where the world was edited
└─ TerrariaIntegration/      the live tile reader, the edit announcements and door detours, and the motor
```

## The body is an orb, and that decides everything below it

The companion is a twenty-pixel circle that flies: no gravity, no ground, no jump, no breath. The engine's tile collision is switched off for it, so the engine's whole contribution to its motion is adding the velocity to the position; the contact that keeps it out of walls is the mod's own (`Contact/`), and it runs in the motor after the tick's velocity has been chosen. There is therefore one body, and a headless tool that integrates a pose through the same contact is integrating the body the game has, which is the property the walker's two bodies never had and the reason its divergence had to be measured every tick.

What the body treats as a wall is one rule in two places. The contact pushes out of solid tiles only: full blocks, half blocks and every slope, because slopes are full tiles to this body, with platforms passable and a closed door solid until the door interaction opens it. The planner's wall (`FreeSpace/OrbTerrain`) is the contact's wall plus any liquid the body is not immune to: water and lava are walls to every search until a mastery immunity opens them, and they hurt on touch rather than blocking, so a route is what keeps the body out of the water and the motor is what charges it when something else puts it in. Honey and shimmer are never walls; they slow the body.

## How a request becomes a velocity

The brain tick sets the tick's terrain rules first (`Configure`, which writes the body's immunities into the shared `OrbTerrain.Immunity`), because every search and every clearance chunk reads them. A request then takes one of four shapes:

- **A destination** (`MoveTo`) hands the navigator a goal point. The navigator owns one route at a time: it plans an A* over the corner graph from the corner under the body to the corner under the goal, smooths the corners by line of sight without losing the clearance the search paid for, and steers the body along the result every tick, re-planning when the goal moves materially, when the world is edited under the route, when the segment ahead is no longer clear, or when the body has stopped making progress. A search too large for one tick keeps its frontier; while it is unfinished the body follows the route it had, steers straight at the goal when the line is clear, and otherwise holds. A search that exhausts the free space without finding the goal is a proven absence, reported apart from unfinished — and every "not yet is not no" rule upstream stands on that distinction.
- **A travel intention with no chosen place** (`SeekDestination`) aims at the request's anchor itself until the caller's own arrival test says the body is there. It exists because a follow request that admits no candidate is still a request to be with the player, and the walker's substitute-tile fallback for that case was a destination that could be arrived at without achieving anything.
- **A state** (`SeekState`) floods outward from the body for the nearest corner a predicate accepts and steers there, optionally through liquid the body may not normally enter — the escape's case, where the body is already in the water and a flood that refused wet corners would refuse the one it stands on. The flood is kept across ticks while unfinished.
- **A dodge** (`AvoidThreats`) runs a handful of headings and a stop forward through the contact for a few ticks against a predicted-collision predicate and takes the one that stays safe longest, ties broken by progress toward the goal.

`Hold` releases whatever is held and asks the motor for nothing, which the motor reads as a brake. Each of these that ends a retained route says who ended it, so the navigator scores the attempt as completed, cancelled, pre-empted or failed rather than folding them together.

The motor (`TerrariaIntegration/`) then accelerates its own momentum toward the requested velocity by at most the acceleration, caps the speed, runs the contact on where that velocity would put the body — push-out along the closest-point normal, the velocity into the wall killed, the slide along it kept — reads which liquid the resolved position touches, and writes the resolved displacement as the NPC's velocity. Momentum is the motor's, not the NPC's: the displacement written after a push-out is not read back as the next tick's velocity, or a push-out would become a bounce. The pace the motor applies is the player's own maximum run speed and run acceleration, each times a multiple the body carries, published every tick to `Steering/OrbPace` so the steering brakes and bends against the numbers that will be applied.

## Two things the shape guarantees, and one it does not

Any route with clearance is followable: the body is holonomic, every edge between two usable corners is swept-clear by construction, and the steering's bend cap is what makes that true in practice, so there is no proof step between planning and performing and no class of "the planner proved it and the body could not". The acceleration is also the body's turn authority — the velocity change per tick is capped in every direction, so the turning radius at speed is the square of the speed over the acceleration — which is why the acceleration multiple in `Selection/BehaviourWeights.cs` was raised until the corridor fixture's steered body kept clearance through a bend; a body that cannot turn inside a corridor's width scrapes its far wall however good the route was.

What it does not guarantee is arrival on an unfinished search. A body between a route and a replan steers straight at the goal only where the straight line is clear, and a request whose flood runs out of budget every tick over a very large free space reports pending indefinitely; the search's node limit bounds that rather than resolving it. The reach sense in `../Observation/` does not have that problem, because its flood is a disc around the body that exhausts; a route search has a goal and no disc.

## Traps

- A retained search is thrown away only where the world changed under what it read: every search records the box it explored and asks the world's edit record whether an edit landed inside it. A caller that only compares `Revision` restarts on any edit anywhere. `TerrainModel/CLAUDE.md` owns the record.
- `LimitPlanningWork` is a soft deadline shared by every nested search in a tick; its `Unbounded` switch is the one place an offline harness removes machine load from a result, and the brain tick always begins a deadline, so two identical live ticks are not guaranteed to agree. Every headless instrument lifts it, and a fixture whose subject is the deadline says so in its recorded mode.
- The corner graph's cardinal and diagonal edges are clear by usability alone, and the swept-circle test is kept for the segments that theorem does not cover: the long segments of smoothing, and the joins from the body to its first corner and from the last corner to the goal. Running the swept test on every edge is what made a screen-sized flood cost eighteen milliseconds a slice.
- `MovementQueries.World` is plugged in by the mod at load and by every headless tool for its scene; asking before it is set throws by design rather than answering from an empty world.
