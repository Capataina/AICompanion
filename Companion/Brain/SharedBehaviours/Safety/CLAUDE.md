# Shared safety — the prediction movement bends against

Safety runs after current danger observation and before ordinary activity selection, and it takes nothing. It hands movement one fact, the predicted collision, and movement's evade layer bends whatever the job is doing away from it. It submits no request to Grants, never applies the motor and never chooses a weapon.

```
Safety/
├─ CLAUDE.md
└─ AssessImmediateThreats.cs   the predicted-collision predicate: where incoming hostiles and projectiles will be, against the orb's coasting body
```

## Avoiding a hit is a bend in the job's own flight, and safety only supplies the prediction

The reflex predicts each incoming hostile and projectile against the orb's own coasting body — the contact circle integrated forward through the same contact the motor runs, so a body drifting toward a wall is predicted where the wall will leave it — and hands that predicate to movement. Movement's evade layer (`../../Infrastructure/Movement/Steering/EvadeWhileMoving.cs`) flies the job forward against it after navigation — this tick's controls, then what the job's own steering would ask from each simulated state — and bends the controls only when that flight would meet a hit. The activity is not suspended, its attempt stays open, its hands stay granted, and the grant names the tick `evade`. A body that flies dodges up and down as readily as back, and through a pool as readily as through air.

## Every liquid is air to the body, so there is nothing to escape

Until 15 September 2026 safety had one response that took the body, environmental escape. Water and lava hurt the orb on contact and were walls to every search until a mastery immunity opened them, so touching either was the exposure, and a retained flood through the liquid to the nearest dry cell was the response. The owner ruled that day that the companion's immunity to water, honey, lava and shimmer is built in rather than earned, because a capability an upgrade switches on has to be tested with and without it after every later change to the brain. With nothing that hurts, there is no exposure to leave, and the escape went with its request identity, its retained state search, its telemetry columns and its overlay layer. A proposal that brings back a response to liquid is answering a hazard the body no longer has.

## Three responses were deleted, and the property that deleted the two about enemies outranks any trigger that brings them back

Before the escape went, safety had two more responses, and both took the body. Collision avoidance suspended the activity for as long as a collision was predicted and ran its own dodge. Combat spacing started from the companion's threat assessment and geometric exposure, searched the shared state search for a cell under an exposure threshold, suspended ordinary work, and held the cell until the threat stopped connecting. The first orb play showed what both cost: the orb dodged well and did nothing else, and it sat beside a zombie for about thirteen seconds while the player walked away, because nothing spacing read ended when he left — while the threat sense was also calling walkers under the hovering orb dangerous at all, which is fixed in `../../Infrastructure/Observation/ObserveThreats.cs` rather than here.

The owner's ruling is the property: **safety is applied on top of whatever the body is doing, never instead of it.** A response that suspends the job in order to stay safe is the shape that ruling refuses, so a proposal that restores a spacing search or a dodge that owns the feet is refused by the property whatever fact it fires on. Exposure to enemies survives only as a cost where positions are scored (`Positioner.PredictedExposureAt`), so a destination beside an enemy is worth less without anything taking the body to leave it.

`Tools/EngineReplay/Combat/VerifySafetyIsALayerOnTheJob.cs` holds the evidence for the layer. An enemy beside the orb while the player walks forty tiles away suspends nothing, never stills the body and still arrives; a companion beside a zombie keeps firing with its job never suspended; a shot at a guarding orb bends the guard for a handful of evade ticks with the hand available, the same guard identity and no hit. Enemy AI does not run headless, so a zombie that walks after the orb is not exercised there, and the freeze's thirteen-second duration was never reproduced headless — only its signature. `Tools/EngineReplay/Movement/VerifyLiquidsAreAir.cs` holds the evidence that nothing responds to liquid.

## Traps

- A takeover that hands the body back must not hand it to a hover anchored before the takeover began. The escape found this: the motor learns which liquid the body touches only inside its own application, so a hold anchored on the first tick in water floated the body straight back into the pool the moment the escape let go, looping until it was downed. Downing and recovery flight are the takeovers left, and the same applies to them.
- A reactive floor — a fallback whose trigger is the absence of the ordinary path's precondition — fires hardest while the planner is still thinking. One was deleted from the walker for producing controls on exactly the ticks nothing consumed them; anything reviving one answers what fact it fires on, and whether that fact is "the planner failed" or only "the planner has not finished yet".
- Old captures and the session reader still carry `combat-spacing`, `combat-reflex`, `collision-avoidance`, `environmental-escape` and `survival-escape` as owner names. They are readings of recordings made before the deletions, not live responses.
