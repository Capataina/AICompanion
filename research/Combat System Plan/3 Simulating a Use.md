# 3 Simulating a Use — one use flown through learned knowledge against predicted enemies

The simulator is the single function the planner calls to learn what an attack would do. It replaces three things that answer parts of that question today and disagree at the edges: `TrajectoryAimer`'s swept trace, `ItemWeapon.Hits`, and `Arsenal.Forecast`'s walk of the bodies a trace crosses.

## The call

```csharp
public static SimulatedUse SimulateUse(in CombatWorld world, WeaponId weapon, Vector2 muzzle, Vector2 aimPoint,
    int fireTick, in EnemyForecast enemies, ref PlanningBudget budget);

public readonly record struct SimulatedUse(WeaponId Weapon, Vector2 Muzzle, Vector2 AimPoint, int FireTick,
    int UseTicks, float ManaCost, SimHit[] Hits, SimEvent[] Events, int LastEffectTick, bool Truncated, float Predictability);

public readonly record struct SimHit(int NpcSlot, int NpcGeneration, int Tick, float Damage, float DamageIfDebuffed,
    float PushX, bool FromArea, short SpawnIndex);

public readonly record struct SimEvent(SimEventKind Kind, int Tick, Vector2 Position, short SpawnIndex);  // Bounce, Child, Explode, Death
```

`CombatWorld` is the decision's frozen view: the companion's gear and knowledge revision, its mana, terrain access through the mod's tile world, and the modifier state. `EnemyForecast` is built once per decision (below). `Predictability` is the product of the laws' confidence along the use, which the planner uses to discount uncertain value rather than trust it.

## What one call does, in order

1. **Expand the volley.** Read the item's `VolleyShape`. Spread slots are expanded at fixed quantiles of their learned distributions (for a slot with count c, the angles at the (i + ½)/c quantiles), never random draws, so a decision is reproducible and a fixture's answer is stable. Origins are placed from their anchor (the muzzle, above the aim point, at the aim point). Types are substituted from the companion's ammo where the slot names none.
2. **Apply companion modifiers** through `ApplyCompanionModifiers`: extra spawns with their spacing and damage, pierce added to each instance's declared penetrate where it is finite.
3. **Fly each spawn** from its delay tick, update by update (`UpdatesPerTick` sub-steps a tick), under its law. At each sub-step:
   - test the swept box against terrain with the engine's own `Collision.TileCollision` for the new velocity and `Collision.SolidCollision` for occupancy, so a reflection uses the game's answer about which axis was blocked; apply the learned wall response, record a `Bounce` or end the spawn;
   - test the box against every enemy's predicted box at that tick from a broad-phase grid, respecting per-body hit cooldowns and remaining pierce; record each `SimHit` with damage from the item's composed damage × the slot's damage share × the k-th-hit ratio × the weapon-effects table's damage ratio for that NPC type after defence; record push from the weapon-effects table;
   - fire learned children whose trigger just occurred, and learned timers; children recurse to the observed chain depth;
   - apply learned area on its trigger event to every predicted box within the radius.
4. **Stop** a spawn at its lifetime, its spent pierce, a dying wall contact, or when the budget is exhausted, in which case `Truncated` is set and the planner reports the decision as cut rather than empty.

Damage is not reserved inside one call: two pellets on a body with ten life each record their hit. The evaluator (file 4) caps damage by remaining life across a whole plan, which is where overkill is removed once for everything.

## Predicting the enemies

`ForecastEnemies.cs` builds `EnemyForecast` once per decision from the threat sense:

- every hostile `ThreatSense` lists, and every segment of a worm individually, since each segment is its own NPC;
- a predicted hitbox per tick over the planning horizon from `PredictObservedMotion.Predict`, sampled every few ticks and interpolated between, with its `Confidence`;
- current life, defence, knockback resistance, generation, and `Urgency` and `UrgencyToCompanion` from the `ThreatRecord`;
- a coarse spatial grid per sampled tick for the broad phase.

Predicted positions after a plan's own pushes and kills are applied by the evaluator, not here: the simulator answers what one use does to the enemies as forecast when it fires.

## Aims

`SolveAims.cs` produces the aim points the planner tries for a weapon from a muzzle at a target set:

- the intercept for a primary target, from the law flown forward against the target's predicted box, replacing today's angle sweep;
- offsets around it at a step equal to the angle the target's box plus the projectile's box subtends at that distance, over the volley's learned spread, so a shotgun is centred on a group and a sniper on a body;
- for a law with a reflecting wall response, when no direct aim reaches the target, a coarse sweep of launch angles flown with bounces, refined around any angle that reaches it — the bank shot;
- the aim through the longest line of predicted bodies for a law with pierce above one.

## Caching and cost

A `SimulatedUse` is cached for the decision by (weapon, knowledge revision, muzzle snapped to half a tile, aim bucket, fire tick bucket, terrain revision of the chunks its trace crossed). The cache is dropped at the next decision because the enemy forecast changes. A law flies in a handful of arithmetic operations per sub-step; the cost is dominated by the broad phase and the number of spawns, so `C1` in file 8 measures forty hostiles, four weapons and a forty-pellet volley before phase C is accepted, and the planner's budget, not a fixed candidate count, bounds how many uses a decision simulates.

## What today's code becomes

| Today | Becomes |
|---|---|
| `TrajectoryAimer.Solve`/`TryClear` angle sweep (`SolveProjectileTrajectory.cs`) | `SolveAims.cs` intercept and aim sets |
| `TrajectoryAimer.PathHits` and `ItemWeapon.Hits` | `SimulateUse` hits |
| `ProjectileFlight.Advance` with `LearnedMotion` | `FlightLaw` one update |
| `Arsenal.MaxPierceCounted` buffer | the enemy forecast's list; pierce read from the instance |
| `Arsenal.Forecast` damage walk | `SimulateUse` plus the evaluator's life cap |
| `FlightModel` passed to the positioner | gone: the positioner no longer asks whether a shot exists |
