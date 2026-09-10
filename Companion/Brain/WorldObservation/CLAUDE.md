# World observation — one shared account of the world

This folder rebuilds facts from Terraria once per brain tick. It observes; it never decides, chooses a position, plans a route, or writes movement. `CoordinateBrainTick` passes its aggregate to reflex assessment and behaviour selection, so a new fact belongs here when more than one downstream system could otherwise derive it differently.

```
WorldObservation/
├─ CLAUDE.md                 this guide
├─ Senses.cs                 aggregate and update order
├─ ObserveCompanion.cs       breath, liquid, fire, life and recent damage; derives self danger
├─ ObservePlayer.cs          position, travel intent, fighting and sight
├─ ObservePlayerWork.cs      player tool hits and terrain-change notification
├─ ObserveThreats.cs         hostile records, reachability, danger and safety horizon
├─ ObserveHostileAttackSources.cs native hostile projectile ownership and generation reset
├─ ObserveProjectiles.cs     hostile projectile predictions for reflexes
├─ PredictObservedMotion.cs  shared terrain-constrained enemy forecast used by aiming and reflexes
├─ CalculateRegroupUrgency.cs pure return-pressure calculation from distance and travel cost
├─ ObserveLoot.cs            collectible drops that fit a player stack or bag
├─ ObserveLight.cs           ambient light outside the companion’s own glow
├─ ThreatRecord.cs           a hostile and its predicted hitbox
└─ LineOfSight.cs            the named game line-of-sight query
```

`Senses.Update` runs player, threats, projectiles, loot, light and self observation before selection reads any value. Separate player and self danger are intentional: leaving a threatened player and walking into danger are different risks. Threats also derive a safety horizon, which selection uses to discount a behaviour that would keep the companion away too long.

Player tool hits are evidence, not animation guessed as intent. `ObservePlayerWork` records real axe and pick hits for work behaviours and tells shared movement whenever a relevant tile changes, invalidating cached movement facts. Doors report their own changes because the game’s door helper bypasses those tile hooks.

`Ambient` is the torch’s authority because its sampling excludes the companion’s glow. The held torch, its light and map reveal follow the interaction layer’s `Shown` answer; observation does not decide whether the hand is free.

## Traps

- A harmful hostile and an attackable target are different observations. Threat collection keeps harmful NPCs even when Terraria declines `CanBeChasedBy`; the arsenal applies that targetability filter when it considers a shot. Player death clears player protection pressure, but does not erase danger to the companion or its retreat anchor.
- Motion continuation carries measured one-tick forecast error and a decaying confidence. Low confidence shortens protection time conservatively; it does not invent type-specific future AI.

Reachability is destination-specific: `CanReachPlayer` gates player urgency and the safety horizon, while `CanReachCompanion` gates personal urgency and retreat eligibility. The two results use the same bounded searches and staggered refresh, with a shared answer only when their destination tiles coincide. A cached negative becomes unknown/potential danger when the enemy, destination, movement class or terrain changes; that invalidation does not add unbudgeted searches. Neither endpoint's reachability determines whether an enemy occupies a proposed future position or route.

Enemy predictions share one observed-motion history across aiming and reflexes. Consecutive observations supply acceleration; collision impulses and abrupt jumps are not repeated as acceleration. The forecast snapshots each NPC's current `gravity`, `maxFallSpeed`, liquid flags and movement-speed fields, then uses native tile and slope collision with the same wet slowdown before restoring the game's shared collision scratch state. Forecasts are cached per observed entity state and tick, and cleared on spawn, death and world closure. They predict current motion over a bounded horizon; they cannot know an arbitrary modded enemy's next scripted decision or reproduce a future NPC-specific step/slope policy. Hostile projectile velocity is converted from sub-update units to game-tick units before reflex projection.

The predictor primes itself from an observation, then measures its next observed continuation against the forecast. Confidence is accompanied by its sample count; zero samples is unmeasured, never proof of stable enemy motion. Native projectile ownership is attributed at spawn with generation-aware identity, so a reused projectile slot cannot inherit an earlier hostile's attack source.

- Never calculate a world fact inside a behaviour: selection runs every behaviour every tick and duplicated readings drift.
- `PlayerDanger` and `SelfDanger` answer different questions. A formula of `1 - danger` must name whose danger it consumes.
- The player’s recorded trail can contain a double-jump route the companion cannot make; it is evidence for a scenario, not proof the planner is defective.
