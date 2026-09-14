# Shared movement system — one request surface, portable core, native adapter

This subsystem turns a desired feet position or immediate danger into controls. `CoordinateMovement` is the only public request surface for brain stages and `MovementQueries` is their read-only geometry and reachability surface. The companion motor applies the resulting control set; no behaviour, reflex, position selector or traversal writes the NPC directly.

```
Movement/
├─ CLAUDE.md                 this guide
├─ CoordinateMovement.cs     public movement request coordinator
├─ MovementQueries.cs        public geometry and reachability queries
├─ LimitPlanningWork.cs      shared soft deadline for planning consumers
├─ BodySimulation/           portable body state, physics and controls
├─ MovementAbilities/        active abilities and future capability description
├─ RoutePlanning/            terrain graph, A*, paths and reachability
├─ MovementExecution/        route ownership, local repair and traversal execution
├─ TerrainModel/             game-free tile interface, text scenario backend, and the record of where the world was recently edited
└─ TerrariaIntegration/      native terrain, native body simulation and motor adapter
```

`CoordinateMovement` owns retained navigator state, active traversal and local repair. A position request becomes `MoveTo`; an immediate threat becomes `AvoidThreats`; a satisfied or deliberately stationary request becomes `Hold`. A `Hold` or a method change that arrives while the body is committed to a move (in flight, or inside a jump's run-up) does not take effect until the move lands: the navigator holds the release, the coordinator returns the step's own controls through `ContinueCommitted` rather than none, and the brain tick names that source `travel-committed` so the record and the travel episodes read it as the journey still being walked; `MovementExecution/CLAUDE.md` owns the rule. Each of those that ends a retained route says who ended it: `Hold` is a voluntary cancellation unless its caller names the owner that pre-empted the body (downing, recovery flight), `SeekState` and `AvoidThreats` pre-empt, and `SeekDestination` is the same purpose changing method. The navigator scores those endings apart from physical failure and names why a held goal is not being delivered; `MovementExecution/CLAUDE.md` owns that classification. An unresolved follow objective retains its state search when position selection has no standing destination. An active traversal owns its pre-state, preparation and flight controls. `PlanLocalMovement` validates the full remaining traversal from the actual entry state and retains controls with expected states. A mismatch invalidates that proof; entry preparation and alternative profiles are evaluated before rejecting the route. Uncommitted movement uses the same body rules.

The portable core drives the same abstract body and terrain contract for planner, replay and local controller. Terraria integration adapts live tile shapes, native movement and controls into that contract. EngineReplay compares the live collision adapter with the native NPC wrapper across terrain and liquid transitions. It does not compare the portable approximation with the engine. Ordinary gameplay still requires recorded playtest evidence.

The active capability is the ordinary ground jump. `DescribeMovementCapabilities` carries the representation for future air jumps, dash, swimming and flight, and `ApplyMovementAbilities` applies active capabilities; those future capabilities are planned rather than available.

## Traps

An unresolved follow objective without a selected standing tile is still a travel request. `SeekDestination` retains that objective through body-state search, invalidating it when the anchor changes materially or the objective is satisfied. Turning a missing destination into Hold strands a companion whose geometric candidate search was incomplete rather than whose intention ended.

`SeekState` accepts a safe-state predicate and a progress estimate for the same body-state controller used by ordinary clearance recovery. SharedSafety supplies the reason to escape; movement owns candidate controls, predicted successors, retained prefixes and their validity. The current unsafe-body forecast also constrains this search, so seeking air does not bypass known collision hazards. `LimitPlanningWork` bounds combined planning time, with a minimum generator operation per retained query to prevent starvation. It is a soft deadline, not a hard upper bound on the entire AI tick. Callers read it through `Deadline` (route and reach searches), `Expired` (route memory), `Spent` (position aiming), and narrow it with `Narrow` where a particular query wants a tighter limit. Its `Unbounded` switch is the one place an offline harness removes machine load from a result while each query keeps its work-count limit. The brain tick always begins a deadline, so without that switch two identical full-brain runs are not guaranteed to agree; that is expected in play and a defect only in a determinism check.

- A route proven in `TextTileWorld` proves only the portable model and captured terrain. It does not establish a live world route.
- `TraversalExecution` copies the traversal and retained state before a local trial. Reusing active state for a probe mutates the real route while evaluating an alternative.
- Terrain changes must invalidate cached route facts through the integration surface, and an announcement carries the tile it happened at, because a retained search asks whether an edit landed where it looked rather than whether one happened at all. Doors are a special case because Terraria’s door helpers skip ordinary tile hooks, so detours on those helpers announce every toggle whoever made it, and the opener announces its own tiles too.
