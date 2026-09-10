# Shared movement system — one request surface, portable core, native adapter

This subsystem turns a desired feet position or immediate danger into controls. `CoordinateMovement` is the only public request surface for brain stages and `MovementQueries` is their read-only geometry and reachability surface. The companion motor applies the resulting control set; no behaviour, reflex, position selector or traversal writes the NPC directly.

```
SharedMovementSystem/
├─ CLAUDE.md                 this guide
├─ CoordinateMovement.cs     public movement request coordinator
├─ MovementQueries.cs        public geometry and reachability queries
├─ LimitPlanningWork.cs      shared soft deadline for planning consumers
├─ BodySimulation/           portable body state, physics and controls
├─ MovementAbilities/        active abilities and future capability description
├─ RoutePlanning/            terrain graph, A*, paths and reachability
├─ MovementExecution/        route ownership, local repair and traversal execution
├─ TerrainModel/             game-free tile interface and text scenario backend
└─ TerrariaIntegration/      native terrain, native body simulation and motor adapter
```

`CoordinateMovement` owns retained navigator state, active traversal and local repair. A position request becomes `MoveTo`; an immediate threat becomes `AvoidThreats`; a no-place request becomes `Hold`. An active traversal owns its pre-state, preparation and flight controls. `PlanLocalMovement` validates the full remaining traversal from the actual entry state and retains controls with expected states. A mismatch invalidates that proof; entry preparation and alternative profiles are evaluated before rejecting the route. Uncommitted movement uses the same body rules.

The portable core drives the same abstract body and terrain contract for planner, replay and local controller. Terraria integration adapts live tile shapes, native movement and controls into that contract. EngineReplay compares the live collision adapter with the native NPC wrapper across terrain and liquid transitions. It does not compare the portable approximation with the engine. Ordinary gameplay still requires recorded playtest evidence.

The active capability is the ordinary ground jump. `DescribeMovementCapabilities` carries the representation for future air jumps, dash, swimming and flight, and `ApplyMovementAbilities` applies active capabilities; those future capabilities are planned rather than available.

## Traps

`SeekState` accepts a safe-state predicate and a progress estimate for the same body-state controller used by ordinary clearance recovery. Survival supplies the reason to escape; movement owns candidate controls, predicted successors, retained prefixes and their validity. `LimitPlanningWork` bounds combined planning time, with a minimum generator operation per retained query to prevent starvation. It is a soft deadline, not a hard upper bound on the entire AI tick.

- A route proven in `TextTileWorld` proves only the portable model and captured terrain. It does not establish a live world route.
- `TraversalExecution` copies the traversal and retained state before a local trial. Reusing active state for a probe mutates the real route while evaluating an alternative.
- Terrain changes must invalidate cached route facts through the integration surface. Doors are a special case because Terraria’s door helper skips ordinary tile hooks.
