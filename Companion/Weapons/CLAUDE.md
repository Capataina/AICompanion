# Weapons — companion equipment selected by landed outcome

```
Weapons/
├─ CLAUDE.md                 this guide
├─ CompanionWeapon.cs        weapon facts and firing contract
├─ BowWeapon.cs               player-owned vanilla-arrow weapon
├─ ThrowingKnifeWeapon.cs     piercing thrown weapon
├─ Arsenal.cs                 target, weapon and cooldown selection
└─ EvaluateAttackOutcomes.cs  bounded joint attack and follow-up valuation
```

TargetEvidence records considered entity generations, weapon, health-capped damage, projected kills, prevented harm and combined value at TargetEvidenceTick. Held-target ticks retain that stamp rather than presenting an old ranking as a new evaluation. The recorder consumes it alongside position alternatives and links the selected sequence estimate to the shot's projectile generation.

The joint evaluator compares legal first attacks with a bounded greedy continuation using the same candidate attacks. Damage reserves projected health, so overkill and duplicate pellets cannot repeatedly earn kill credit. Time-discounted effective damage, threat removal and a small finishing value share one policy in BehaviourWeights. This is an estimate over current geometry, not a simulation of future enemy decisions or a globally optimal attack schedule. New weapons supply physical hit predictions; no per-weapon suitability policy belongs here. Synthetic multi-hit tests establish the evaluator contract, not an implemented shotgun weapon.

Weapons are companion equipment, separate from the cargo bag and from brain behaviours. The brain’s hands step asks the arsenal to fire whenever no work tool occupies the arm. The arsenal selects a target and weapon from what a simulated trajectory would land over its evaluation window; each weapon supplies facts, never a private suitability score. Projectile aiming belongs to `../Brain/ProjectileAiming/` and fighting movement belongs to `../Brain/Behaviours/Combat/`.

Threat membership does not mean a target can be damaged. Target retention, engagement, expected-damage bodies and final firing revalidate Terraria's chase predicate; an invulnerable hostile stays in hazard observation but cannot attract a hunt or earn fictional weapon damage. Protection asks the arsenal for an optimistic time to remove the most urgent player threat, including reload, projectile flight and repeat hits. No demonstrated shot means unknown intervention time, represented as infinity, so a wall cannot make the player appear safe. Choice, engagement and intervention caches expire when the muzzle, target pose, life, generation or terrain changes. Their time limits bound unchanged states; they must not hide a newly opened firing window. An urgent player threat can interrupt target retention, and the bounded candidate shortlist orders urgency before proximity.

The choice trace and the final shot are deliberately separate. A weapon can score only on a solved swept arc, then its accuracy rotation is traced again before a projectile is spawned. A failed trace is cached only for its unchanged muzzle, target pose and terrain revision and only for a short freshness window; it is not a weapon cooldown, so an opened door or moving target can create a shot immediately. `CompanionWeapon.Fire` returns the spawned projectile slot so the arsenal can record one causal shot event with the final launch and expected impact.

Projectiles are player-owned and use the player’s ranged damage so kills, drops and eligible player effects stay attributable to the player. A new weapon implements its item, projectile, flight and fire facts; it does not decide when it is useful.
