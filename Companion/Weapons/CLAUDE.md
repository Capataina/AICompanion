# Weapons — companion equipment selected by landed outcome

```
Weapons/
├─ CLAUDE.md                 this guide
├─ CompanionWeapon.cs        weapon facts and firing contract
├─ BowWeapon.cs               player-owned vanilla-arrow weapon
├─ ThrowingKnifeWeapon.cs     piercing thrown weapon
└─ Arsenal.cs                 target, weapon and cooldown selection
```

TargetEvidence records the considered entity generations, expected effective damage and urgency at TargetEvidenceTick. Held-target ticks retain that stamp rather than presenting an old ranking as a new evaluation. The observation recorder consumes it alongside the weapon and position alternatives.

Weapons are companion equipment, separate from the cargo bag and from brain behaviours. The brain’s hands step asks the arsenal to fire whenever no work tool occupies the arm. The arsenal selects a target and weapon from what a simulated trajectory would land over its evaluation window; each weapon supplies facts, never a private suitability score. Projectile aiming belongs to `../Brain/ProjectileAiming/` and fighting movement belongs to `../Brain/Behaviours/Combat/`.

Threat membership does not mean a target can be damaged. Target retention, engagement, expected-damage bodies and final firing revalidate Terraria's chase predicate; an invulnerable hostile stays in hazard observation but cannot attract a hunt or earn fictional weapon damage. Protection asks the arsenal for time to a useful shot against the most urgent player threat, including reload and projectile flight along a solved arc. No demonstrated shot means unknown intervention time, represented as infinity, so a wall cannot make the player appear safe. The estimate's cache is bound to the target's spawn generation; its initial timestamp sentinel is checked before subtraction to prevent overflow from making the first result permanent.

The choice trace and the final shot are deliberately separate. A weapon can score only on a solved swept arc, then its accuracy rotation is traced again before a projectile is spawned. A failed trace is cached only for its unchanged muzzle, target pose and terrain revision and only for a short freshness window; it is not a weapon cooldown, so an opened door or moving target can create a shot immediately. `CompanionWeapon.Fire` returns the spawned projectile slot so the arsenal can record one causal shot event with the final launch and expected impact.

Projectiles are player-owned and use the player’s ranged damage so kills, drops and eligible player effects stay attributable to the player. A new weapon implements its item, projectile, flight and fire facts; it does not decide when it is useful.
