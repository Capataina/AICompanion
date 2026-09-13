# Movement abilities — active controls and future capability shape

```
MovementAbilities/
├─ CLAUDE.md                       this guide
├─ ApplyMovementAbilities.cs       applies the capabilities active on a body
└─ DescribeMovementCapabilities.cs names the counters and constraints a route state carries
```

Only the ground jump is active today. The capability description exists so a future air jump, dash, swimming or flight changes the route state and traversal rules through one seam rather than adding special cases to search. It is a representation for planned work, not evidence that those abilities exist.

## No unbuilt method is admitted to any offer

`MovementCapabilities` carries `AirJumpCount`, `CanDash`, `CanSwim` and `CanFly`, and every live body is `Basic`: nothing assigns another value, the motor's capabilities are copied to the navigator each tick unchanged, and executed-route memory refuses any entry recorded under a non-`Basic` capability. `ApplyMovementAbilities` already decrements an air-jump counter when one exists and deliberately applies no dash. No route transition, local proof, interaction jump, activity offer or position request reads any of the four fields. So the expected-behaviour abilities (double and triple jump, dash, swimming, flight) are dependencies, and an acceptance row that needs one is incomplete rather than satisfied by what the ground jump manages. Distant-follow recovery flight is not one of them: it is a coordinator fallback outside route search, and it must never be offered as a traversal.

Player movement is not mirrored. Wings, jump bottles, dash accessories, flippers and movement speed change the player's body, and none of them is read onto the companion's. Whether a mastery unlock or the player's own kit should grant a method is the progression decision, separate from making an already-authorised method physically usable; the UI unlock, its cost and experience are not part of it.

## The sequence every missing method follows

Each method is one dependency and is done only when all five stages hold for it, in this order, because each stage consumes the one before.

1. **Resource transition, in this folder.** What starts the method, what it consumes and when that replenishes, as state on `MobilityState` applied in `ApplyMovementAbilities` for the simulator and the native adapter alike. Air jumps: a counter spent per mid-air jump and refilled on landing, with the triple jump as a larger count. Dash: a direction, a duration and a cooldown that ticks down whether or not the body moves. Swimming: liquid movement without the dry jump, and breath still charged by `ObserveCompanion`. Flight: a flight-time budget spent while ascending and refilled on the ground.
2. **Native motor effect, in `TerrariaIntegration/`.** The same controls applied to the live NPC with the game's own numbers for that method, and the native body prediction advancing identically, held by an `EngineReplay` matrix against Terraria's own collision the way the ground jump and liquids are. A planner-only move that the motor cannot perform is the failure this stage exists to stop.
3. **Local proof and route transitions, in `MovementExecution/` and `RoutePlanning/`.** The method becomes a `Traversal` whose candidate edges are proven by simulating it from the node's pose, with its resource in the route state so a spent air jump or a dash on cooldown cannot be planned again. Interaction jumps and hop approaches that simulate the ground jump today must either include the method or say they do not.
4. **Evidence invalidation on gain and loss.** Every retained fact derived under the old capability goes stale when it changes: the route edge cache (keyed today on node and lava policy only), the positioner's reachable regions, retained route and meeting floods, one-way verdicts, executed-route memory (which refuses non-`Basic` entries until its fingerprint carries the capability) and activity approach caches. The rule is the one tool reach now follows through `FindToolAccess.Reach`: whatever a retained fact was derived under belongs in its key.
5. **Tests from varied actual states.** Standalone (the method alone reaches a place the ground jump cannot), chained (the method inside a longer route with walks and ground jumps, its resource spent and replenished along the way) and return-dependent (a trip whose way back needs the method, judged by the round-trip query and then by the native body), each from several entry speeds and offsets, and each with the capability removed mid-route to show the evidence invalidation.

Only after all five may an offer, a reachability verdict or a position request rely on the method.
