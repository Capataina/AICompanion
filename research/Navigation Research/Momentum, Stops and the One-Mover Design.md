# Momentum, stops and the one-mover design

Research note, 2026-09-13, written against source `9ca5ae4` (0.22.46) and the captures `Telemetry/2026-09-13_18-56-52-801` and `Telemetry/2026-09-13_16-32-22-420`. It answers three owner reports from the 13 September playtests: the companion is slow over irregular ground, it hops on the surface and lands with no speed, and it stands below a ledge the player is on without climbing. The question under it is whether the movement system's representation can express fast, momentum-keeping travel at all, or only by adding a case per terrain shape. The history section is a ledger of what the log already tried, so a proposal here can be checked against it before it is built.

## What the record shows

The companion on a route moves at roughly half its walk speed, and the loss is stops rather than slow walking. Walk speed is 3.5 px/tick (`BodyPhysics.WalkSpeed`).

| Measure, on route (control source travel, navigator Executable or Partial) | 18:56 run | 16:32 run |
|---|---|---|
| ticks on a route | 5,856 | 17,859 |
| mean speed, px/tick | 1.70 | 1.51 |
| share of route ticks under 0.5 px/tick | 23% | 23% |
| share of route ticks with the walker asking for no movement | 20% | 16% |
| full stops (three or more consecutive ticks under 0.6 px/tick) | 119 | 476 |
| full stops per minute of travel | 67 | 79 |
| player mean speed on the same ticks when moving, companion mean | 2.65 vs 2.15 | 2.59 vs 1.74 |

The stops sit at edge boundaries. Drops began from rest (under 0.6 px/tick on their first tick) 59% and 53% of the time in the two runs, fall-throughs 79% and 77%, jump edges of every kind 43% and 52%. Every walk edge is one tile, so the median walk edge lasts 5 ticks and a cave route is a chain of one-tile walks with a stop before each descent.

Attributing the 119 stops of the 18:56 run (1,509 stopped ticks) by what the body was doing puts most of the lost time in the from-rest family and a smaller share in replans:

| What the stop was | Stops | Stopped ticks |
|---|---|---|
| already in the air with no sideways speed (a fall begun at rest, or a hop whose steering reversed) | 44 (37%) | 637 (42%) |
| braked on the ground, then a fall-through begun at rest | 10 (8%) | 330 (22%) |
| braked on the ground, then a jump begun at rest | 13 (11%) | 89 (6%) |
| braked on the ground, then a drop begun at rest | 10 (8%) | 98 (6%) |
| on the ground inside one walk step (steer tolerance or the walk's own start-tile brake) | 26 (22%) | 233 (15%) |
| on the ground while the path was replaced | 14 (12%) | 113 (7%) |
| on the ground across walk steps | 2 (2%) | 9 (1%) |

The first four rows are one mechanism seen at different moments: a move proven from rest is entered at rest and then performed with no sideways speed, so the fall after the brake is as slow as the brake. Replans are 7% of the stopped time, so a faster search would not move the number much; the walk-step row is the second lever and is the walker's own steering tolerance rather than the graph.

A stop costs about half a second of ground. Braking from 3.5 at `Slowdown` 0.2 takes 18 ticks; accelerating back at `Acceleration` (0.08 × 3.5 / 3 ≈ 0.093) takes 38 ticks and covers about four tiles where a cruising body covers eight. At 67 to 79 stops per minute that is roughly 30 seconds of every minute of travel spent braking and restarting, which accounts for the gap between the measured mean and the walk speed.

The surface hop is a second, smaller loss with the same signature. In the 18:56 run the walker raised a jump 19 times on a one-tile walk step (sideways one, up or down at most one), stayed airborne 17 to 75 ticks, and landed with horizontal speed between 0.00 and 1.57 px/tick; 13 of the 19 under 0.5. In the air the walker re-aims at the current one-tile step each tick, so once the body has overflown it the steering reverses and brakes the body to zero. `WalkTraversal.Steer` raises a jump on a walk step in two cases: `WallAhead` (a sideways collision this tick and two full solids ahead at the feet row and the row above), or the step's feet point sitting two or more rows above the live body. Sixteen of the 19 hops used the two-tile jump scale and launched from a slope, a half block or a footing the record reads as air, which fits the wall test; the mechanism by which a slope makes the ground ahead read as a two-tile wall is inferred from the code, not proven, and wants a fixture at tile 3372,368 of the 18:56 capture. The other three (ticks 6930, 7201, 7324) used the full jump scale from solid footing, which the walker only produces when the step is five or more rows above the body, so those are a body that had slipped below its own step jumping back up to it; that reading is inferred from the scale and not checked against the tiles.

The positioner's no-destination fallback is a third producer. When following has no accepted candidate, `CoordinateBrainTick` hands the body to a bounded state search toward the player; it jumped 11 times in the 18:56 run and 15 in the 11:06 run, and 12 of those 15 lost their horizontal motion in the air, several with the move control set against the player's direction.

## Why the system stops, read from source

A route node is a tile (`NavGrid`, `NavNode`). Velocity enters the planner at one place, the start pose handed to `Traversal.Candidates`, and is otherwise unknown, so every proof that depends on speed is pinned to rest. `WalkTraversal.Candidates` proves each one-tile walk twice, from rest and at walk speed, and marks the edge from rest when the two land on different tiles. `Traversal.StartsFromRest` makes every drop, fall-through and standing jump a from-rest move by definition. The walker honours that with `SettledFor`: the step before a from-rest move is not done until the body is under `RestSpeed` (0.6 px/tick), and `WalkTraversal.Steer` returns no controls while a from-rest walk still covers its own start tile above that speed.

Route cost cannot see time. `Traversal.MovementCost` prices a walk by tiles, a drop by depth and a jump by its tick count, so two routes that differ only in how many stops they force are priced the same; the planner cannot prefer the one that keeps momentum even if one existed in the graph.

The motor absorbs a one-tile rise without a decision (`BodyMotion.StepUp`, mirroring the game's `Collision.StepUp`) but has no counterpart for a one-tile descent: a step down is proven as a walk edge whose landing is a row lower, which is fine on a kerb and becomes a from-rest walk on a slope where the at-speed landing differs.

## What the log already tried, and what each attempt proved

The ledger is newest first; every row cites its commit, and the property column is the sentence a later design must not rebuild against.

| Date | Commit | Tried | Property it established |
|---|---|---|---|
| 2026-09-13 | `a9604d4` | walk and jump pace pegged to 110% and 105% of the player's current stats | a share-of-stats pace lets a nominally slower body overtake a buffed player; it does nothing about stops |
| 2026-09-13 | `fd66a33` | a plain walk records its at-speed duration rather than the from-rest duration it was proven with | pricing every walk tile as a stop-and-restart inflated every consumer that sums step durations several times over; route cost never read a walk's duration, so which route wins did not change |
| 2026-09-11 | `87fd960` | a running jump admitted by a runway formula, proven at nominal speed, performed at the run-up's speed | an admission test that approximates a performance cannot stand in for running the performance; 246 of 281 jumps in the exposing session faulted before their first tick |
| 2026-09-10 | `08c0b48` | early-frontier detour: publish a route's first frontier node before the search finishes | built and removed; 31 partial endpoints became 42 incomplete ones with no completion benefit |
| 2026-09-09 | `c4bc752` | latch the drop entry brake on a settled flag so a body brakes through its whole approach to a lip | measured on the corpus and torn out: 25 of 104 routes 4 to 11% faster, 4 new mislands and a run-up loop; "clamping to rest is what hides the pose the body actually started from"; the folder file records that removing the brake without validating the real entry pose remains a rejected approach |
| 2026-09-08 | `23318ef` | the walk's own self-brake, scoped to the step's start tile | an entry brake is legitimate only while scoped to the exact tile the proof assumed rest on |
| 2026-09-08 | `b64c139` | `ReactiveWalk`: fighter-style obstacle ladder, gap leap and stall reversal used whenever the planner has no path or the path has finished | before it, a failed step left the companion a statue; the fallback exists so the body always produces some move |
| 2026-09-08 | `985195e` | each jump and drop edge carries the exact parameters that realised it; the replan cadence waits while a jump backs off or runs in | a multi-phase move must be protected as one committed unit against the replan cadence, or the body cycles between two mirrored jumps forever with no fault recorded |
| 2026-09-08 | `bd2edb6` | every move proven by driving `BodyMotion.Step` from the node's pose | the follow harness went from 30 of 91 blocks walked to 66 of 91 purely from making proof and performance one simulation |
| 2026-09-08 | `20b080a` | one `Traversal` per move kind owning both the proof and the performance | the cause of every watched stuck companion to that point was the planner and the follower being two implementations of one move |
| 2026-09-08 | `807df2e` | tile shape replaces a boolean solid test in the grid | a boolean solid test cannot tell a slope from a wall |
| 2026-09-08 | `1e2d85e` | wall ahead read from two solid tiles rather than the engine's collision flag alone | the previous tick's collision flag is set before the game's step-up resolves a kerb, so jumping on the flag alone produced frantic jumping on ordinary kerbs; `WallAhead` today is this rule unchanged |

Standing rules in the folder memory that bind a redesign: `Steer` must use the same functions `Candidates` simulated with; a fallback's trigger has to be the fact it exists to answer, never the absence of the ordinary path's precondition; only a physical failure retires a remembered route; a stronger heuristic is not a free speed improvement (the weighted-heuristic experiment regressed passing routes); the platform lift is refused while descend is held and the two sides of that gate cost three failed fixes. Open in the log and not resolved here: the slope reluctance has three named candidates (`StepDown` firing only at zero vertical velocity, a shape-ordering fix not yet measured, descending walk edges never offered); 152 proven jumps the performer still refuses are counted and unexplained; the portable lift and the native step helper still differ in implementation.

The 2026-09-08 Slate Record ruling that killed a mode where the companion mirrors the player's own inputs does not touch any of this: a planner over body state with velocity solves movement on its own terms and never reads the player's controls.

## Outside prior art

Simulation-based search over control inputs is Robin Baumgarten's A* controller, winner of the 2009 and 2010 Mario AI competitions ([controller source](https://github.com/jumoel/mario-astar-robinbaumgarten), [competition report](http://julian.togelius.com/Togelius2010The.pdf)). Its node is the whole game state, its edges are button combinations forward-simulated with a copy of the physics, and it never corrects divergence because it throws the plan away and re-searches from the live state every two frames within a fixed millisecond budget. That account comes from a 2025 reconstruction (Schäfer, TU Berlin) that reports cross-checking against the recovered original source; the 2010 paper itself was not reachable when this was written, so the two-frame reset is claimed, not triangulated. Its documented weakness is dead ends that need backtracking, which a tile graph handles and a pure forward search does not. Constant re-search also conflicts with the committed-unit property from `985195e`; a design taking the replan-from-live-state idea has to keep a flight protected until it lands.

Jump-link navigation graphs (Godot `NavLink2D`, Unity off-mesh links, the Surfacer toolkit for Godot) put nodes on surfaces and one precomputed arc per reachable pair, executed by playback of the whole arc rather than re-aiming at intermediate points; velocity through an intermediate waypoint is solved once for the whole arc. None of the reachable sources describes a live correction for a body that leaves the arc, and the generic navmesh breaks at every edge that needs a jump unless a link is placed by hand. This is the same family as the mod's proven edges, and its lesson is that one edge should span the whole irregular stretch it can be proven across.

Terraria's own fighter and town NPC AI is the hold-a-direction, step up one tile, jump at a wall pattern. The game's patch notes record slope handling for the fighter AI fixed as late as 1.4.5.7, and players report town NPCs failing on stairs built with platforms, so the failure class the companion shows on slopes is the genre's, not unique to this mod.

Step offset is the standard character-controller answer to small bumps: the mover absorbs a rise or a descent of a tile or so inside collision, before any planner or jump decision exists (Unity's `CharacterController.stepOffset`; the Sonic engines' ground sensors that re-snap the body to the surface under it). Most platformer AIs therefore never plan a one-tile step at all. The mod has the rising half of this and not the descending half.

Pure pursuit, the carrot-on-a-stick steering used for ground vehicles, removes per-waypoint stopping by aiming at a point a fixed distance ahead on the path, and is documented to cut corners at large lookahead; no source applies it to a platformer by name, and on a platformer a cut corner is a walk off a ledge before the jump decision fires. It is recorded here as a rejected direction with its reason.

## The design this points at

The categorical answer is that the current representation cannot express momentum-keeping travel; it can only add cases. A tile-only graph has no place to hold "arriving fast" and so must prove everything from rest, and a walker that re-aims at one-tile steps must brake to hit them. The 2026-09-09 brake experiment is the evidence: taking the brake away without giving the proof the real entry pose produced mislands, because the proof was for a body the performer did not have.

The shape that fits every property in the ledger:

1. A route node is a tile and an entry-speed class (rest, walking, full run, each direction), and every edge is proven by simulation from its entry speed. A drop taken at full run is its own edge with its own landing; the walk before it is no longer from rest because nothing is. `Traversal.Candidates` already receives a pose; the change is that every node carries one. The pool flight from run 4 is the fixture that must still pass: an at-speed proof lands further, and the returnability question is asked of that landing.
2. Edge cost is ticks. `MovementCost` prices time so the planner prefers the route that keeps its speed, which today it cannot see.
3. One-tile descents belong to the motor, as one-tile rises already do: `BodyMotion` gains a step-down so a slope or a kerb down is walked at speed without an edge. This is the smallest change and is independent of the graph.
4. The reactive layer goes, with its job re-homed. `WallAhead` and the jump inside `WalkTraversal.Steer` are removed, because a proven walk step has no wall and a wall the planner did not see is a terrain change to replan on. `ReactiveWalk` is removed and its responsibility, that a failed plan does not leave a statue, moves to replanning from the live state with the retained search's proven prefix, which the navigator already keeps. The state-search fallback leaves following; a follow request with no accepted candidate resolves through the positioner offering the reachable tile that most reduces the gap, so there is always a proven destination or a proven stand. The property from `985195e` holds throughout: a flight is one committed unit and the replan cadence waits for it.
5. Airborne steering aims at the edge's proven landing, never at the next tile. `JumpTraversal` already does this, which is why proven jumps do not show the surface signature.

What loses: replanning every tick in Baumgarten's manner, because the graph has to hold retained searches and committed flights; pure pursuit, for the ledge reason; keeping the from-rest brake and speeding up elsewhere, because the measurement puts the loss in the stops and nowhere else.

The separating experiment before any of it ships: the corpus run's per-route times and its misland count against the current baseline, with the pass line declared first as no route slower, the four mislands from `c4bc752` absent, and the run-4 pool fixture still refusing the lip. The live number to re-measure afterward is stops per minute of travel, which is 67 to 79 today.
