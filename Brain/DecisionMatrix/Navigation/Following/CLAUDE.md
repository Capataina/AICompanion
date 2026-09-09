# Following — performing the path the planner proved

The navigator takes a body state and a feet position each tick and returns the controls for that tick; it plans through `../Planning/` when the goal or the cadence says so and hands each step of the path to the traversal in `../Traversals/` that proved it. It names nothing of the game: the motor in `../../../../Companion/` builds the body state from the NPC and applies the controls to it, and the replay tool builds the state from a pose and applies the controls with `BodyMotion.Step`, so the navigator that runs in the game is the navigator the replay's `--follow` runs, and a failed plan reaches the telemetry through the `PlanFailed` seam that the brain wires and the tool leaves empty.

```
Following/
├─ CLAUDE.md
├─ Navigator.cs         MoveTo(BodyState, target) → Controls: plans when the goal moves past a slack of a couple of tiles, on a cadence (which waits while the current step's traversal says it is part way through a move a fresh plan would undo, a jump's back-off and run-in — and a moved goal waits on the same guard), when stuck or after a fault, from the ground or from a pinned body; advances past every step its traversal says is done; asks the current step's traversal to check and steer; a fault prices the step, counts a strike and replans on the next ground tick while the body asks for nothing; a stuck body (not moved for a while with a path) prices its step and counts a strike too; two strikes are what the brain reads as a spot to refuse; hands the tick to the reactive floor when there is no path; every step that finishes or faults is an EdgeReport (kind, from, to, ticks proven, ticks taken, outcome) the telemetry writes and the replay prints, and feeds the census
├─ ReactiveWalk.cs      the floor: a move toward a point from the current state with no plan — the fighter AI's own one-column feeler at four rows mapped to a jump height, its gap leap where there is no floor ahead or diagonally ahead, and a reversal when nothing has got closer for a while
└─ BehaviourCensus.cs   counts per move kind what the planner offered, what the follower began, completed and faulted, faults by reason, walks by vertical direction, and places asked for against places reached; game-free, so the replay's follow harness fills the same counters
```

The rule the follower lives by is that a step is performed the way it was proved, and the follower itself no longer knows how any step is performed: that knowledge is the traversal's, both halves of it. What stays here is the cadence, the strikes, the floor and the report of what each step cost against what it was proven to cost, which is the trace the game and the simulation are read against each other by.

## Two owners of one tick is the thing this folder is arranged to prevent

The floor and the traversals never both steer. `ReactiveWalk` runs only where there is no path or the path is finished; a live step belongs to its traversal, and a step that faults is priced, struck and replanned rather than handed to the floor. A floor that also fired on a fault would fight the replan and could misland into a fresh fault, so the boundary is a design decision and not an accident of where the `if` fell.

Why the floor exists at all is the architectural finding from reading vanilla's own AI in depth (2026-09-09). Vanilla has no route planner anywhere, and no vanilla walker ever gets permanently stuck at a step, because its reactive layer is *complete*: every tick, from whatever state it is in, the AI produces a move. A zombie cannot reach a rooftop and it is never frozen. This project had the opposite shape — a real planner over a floor that walked at the target and jumped only for a wall — so the planner was the only thing that could produce a climb, and an unroutable one-tile ledge stopped the body dead. The planner is for the routes a zombie cannot do: the long way round, the shaft, the player's platforms. The floor is what makes a failed plan degrade to a zombie instead of to a statue.

## Commitment is the other half of "the body executes what it planned"

A plan is worth nothing if it is replaced before a step of it completes. Measured over the 2026-09-09 session: 1,581 plan runs, mean run 17.8 ticks, mean deepest step reached 0.85, and 51.2% of plans never advanced a single step. The cause was not here — the positioner rescored on its own cadence and picked afresh with no memory of its last answer, and two standable tiles a few pixels apart score within noise of each other, so the tile it named wandered continuously under a request that had not changed. Four things now hold a decision still, and the shape of each matters more than its number:

- The goal tile carries a slack, so the scorer twitching is not the request changing. The path is walked to within the arrival slack of the *real* request rather than of this tile, so the slack costs nothing at the end.
- The strike count is not reset when the goal moves. It used to be, and with the goal changing three times a second that made the two-strike escalation — the thing the brain reads to ask for a different spot — unreachable by construction, so a body could stand at an impossible step for a whole session and never reach it. The count belongs to the attempt, not to the tile.
- A moved goal waits for a jump's run-up on the same guard the cadence uses, because a replan landing mid-run-up offers the mirror jump from the same take-off and the body circles between two jumps with nothing ever faulting.
- The positioner gives the spot it already holds a small bonus, which is what `../../Decision/` has done at the action layer since it was written. A bonus and not a hold, because a genuinely better spot must still win at once.

## A body that cannot move is not a body choosing to stand still

`BodyState.Pinned` is set by the motor from the engine's own displacement: a real velocity beside no movement, which the engine cannot do to a body it is integrating. Both the strike gate and the planning gate accept a pinned body as well as a grounded one, and they have to, because `OnGround` is `velocity.Y == 0f` — a held body with gravity accumulating under it reads as airborne, so through the whole 2026-09-09 shaft freeze every escape route in this file was gated shut.

`OnGround` was deliberately not redefined. Vanilla gates every jump on `velocity.Y == 0f` — the fighter's ladder, the town NPC's step block, all of it — so that test is the game's own reused path, and replacing it with an invention to paper over a symptom caused elsewhere is the wrong direction. The pin gets its own reading instead.

## A partial path that ends where it started is worse than no path, so it is discarded as one

A partial path is followed on purpose: walking to the nearest point a search reached beats standing where the goal went out of view. The degenerate member of that set is the exception, and it is worse than the failure it looks like. When the nearest node the search reached *is* the tile it was asked from, every step the path carries is already covered by the body, the follower reads them all as done on its first tick, and the path is finished with nothing moved.

The damage is not the wasted tick, it is that a path object exists. `LastPlanEmpty` is what the brain reads to count stranded ticks, and it clears that count on any plan that is not empty, so the roam that walks a sealed pocket can never start while these keep arriving. The cadence sees a finished path and replans every tick rather than honouring the failed-plan wait. And the census records a plan that was found. So the plan is discarded to null, which hands the body to the straight-line fallback exactly as no path does and arms every one of those mechanisms.

The general property, which is not about paths: **a result that is technically non-empty and semantically empty disables every guard written against emptiness**, and the guards are the ones that would have caught the situation. The emptiness test belongs on what the result achieved, never on whether an object came back.

## A plan has a wall-clock budget, and it is off by default

`Navigator.PlanMsBudget` is zero unless something sets it, and the mod sets it at world load. The replay tool runs this same navigator and must give one answer per scenario however busy the machine is, so a wall-clock limit in the core would make the committed corpus non-reproducible. An expansion budget cannot bound the cost on its own: every slow plan of the 2026-09-09 session sat at exactly the expansion cap with the reachability flood costing 1 to 15 ms beside it, so the time goes into the search's own edge proving, where one expansion on bare floor offers a few walks and one on a ledge over a shaft simulates every jump profile and descent line.
