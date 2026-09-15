# Shared safety — leaving what hurts, and the prediction movement bends against

Safety runs after current danger observation and before ordinary activity selection. It has one response that takes the body, environmental escape, and one fact it hands to movement without taking anything, the predicted collision. It submits at most one request to Grants; it never applies the motor itself or chooses a weapon.

```
Safety/
├─ CLAUDE.md
├─ AssessImmediateThreats.cs   the predicted-collision predicate: where incoming hostiles and projectiles will be, against the orb's coasting body
├─ ChooseSafetyResponse.cs     the escape's identity, its movement request and its cancellation
└─ ReachEnvironmentalSafety.cs leaving water or lava: a retained flood, through the liquid, to the nearest dry cell
```

## Environmental escape is leaving the liquid, and nothing else

The orb has no breath: water and lava hurt it on contact, so touching either is the exposure and leaving it is the whole response. The escape is a retained flood over the corner graph that is allowed to cross the liquid the body is already in — a flood that refused wet corners would refuse the one the body stands on — toward the nearest free, dry cell, and the target is kept stable while the search runs so it finishes instead of restarting at every nearer pocket. Dry means the contact circle touches no tile that is a wet wall under the body's immunities, so an immunity the mastery tree grants ends the exposure without any change here. A body sealed inside a pool with no dry cell in reach reports that failure and keeps its purpose; nothing here grants immunity or manufactures a successful escape. Downing and recovery explicitly cancel safety ownership, and a cancelled escape clears the shared body's retained state search, because its frontier was proved for the escape's terminal condition.

The escape also drops movement's hold anchor when it starts, and that is not tidiness. The motor learns which liquid the body touches only inside its own application, so on a body's first tick in water the brain has not yet been told, holds, and anchors its hover in the pool; an escape that left that anchor in place carried the body out and the hover floated it straight back in the moment the escape let go, looping until the body was downed. `Tools/EngineReplay/Movement/VerifyCapturedEscape.cs`'s empty-offers pool is the scene that found it.

## Avoiding a hit is a bend in the job's own flight, and safety only supplies the prediction

The reflex predicts each incoming hostile and projectile against the orb's own coasting body — the contact circle integrated forward through the same contact the motor runs, so a body drifting toward a wall is predicted where the wall will leave it — and hands that predicate to movement. Movement's evade layer (`../../Infrastructure/Movement/Steering/EvadeWhileMoving.cs`) tests the tick's controls against it after navigation and bends them only when they would meet a hit. The activity is not suspended, its attempt stays open, its hands stay granted, and the grant names the tick `evade`. A body that flies dodges up and down as readily as back.

## Two responses were deleted, and the property that deleted them outranks any trigger that brings them back

Until 15 September 2026 safety had two more responses, and both took the body. Collision avoidance suspended the activity for as long as a collision was predicted and ran its own dodge. Combat spacing started from the companion's threat assessment and geometric exposure, searched the shared state search for a cell under an exposure threshold, suspended ordinary work, and held the cell until the threat stopped connecting. The first orb play showed what both cost: the orb dodged well and did nothing else, and it sat beside a zombie for about thirteen seconds while the player walked away, because nothing spacing read ended when he left — while the threat sense was also calling walkers under the hovering orb dangerous at all, which is fixed in `../../Infrastructure/Observation/ObserveThreats.cs` rather than here.

The owner's ruling is the property: **safety is applied on top of whatever the body is doing, never instead of it.** A response that suspends the job in order to stay safe is the shape that ruling refuses, so a proposal that restores a spacing search or a dodge that owns the feet is refused by the property whatever fact it fires on. Exposure to enemies survives only as a cost where positions are scored (`Positioner.PredictedExposureAt`), so a destination beside an enemy is worth less without anything taking the body to leave it. Environmental escape is the one takeover kept, deliberately: a body in lava has no job worth keeping.

`Tools/EngineReplay/Combat/VerifySafetyAftermath.cs` holds the evidence for the layer. An enemy beside the orb while the player walks forty tiles away suspends nothing, never stills the body and still arrives; a companion beside a zombie keeps firing with safety owning no tick; a shot at a guarding orb bends the guard for a handful of evade ticks with the hand available, the same guard identity and no hit. Enemy AI does not run headless, so a zombie that walks after the orb is not exercised there, and the freeze's thirteen-second duration was never reproduced headless — only its signature.

The escape does not price liquid damage against a hostile shot: every escape state is vetoed where a predicted projectile box meets the body, so a shot hanging over the only exit is impassable rather than a cost weighed against the hurt. A priced trade between hit damage, liquid contact and the aftermath is open, and it needs the movement system's veto to become a cost rather than a predicate.

## Traps

- `ReachEnvironmentalSafety` is a movement escape and is not the reach sense in `../../Infrastructure/Observation/`, which every consumer reads once per tick; the escape's flood runs through liquid and the sense's never does.
- A reactive floor — a fallback whose trigger is the absence of the ordinary path's precondition — fires hardest while the planner is still thinking. One was deleted from the walker for producing controls on exactly the ticks nothing consumed them; anything reviving one answers what fact it fires on, and whether that fact is "the planner failed" or only "the planner has not finished yet".
- Old captures and the session reader still carry `combat-spacing`, `combat-reflex` and `collision-avoidance` as owner names. They are readings of recordings made before the deletion, not live responses.
