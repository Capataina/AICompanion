# Weapons — companion equipment selected by landed outcome

```
Weapons/
├─ CLAUDE.md                 this guide
├─ CompanionWeapon.cs        weapon facts and firing contract
├─ BowWeapon.cs               player-owned vanilla-arrow weapon
├─ ThrowingKnifeWeapon.cs     piercing thrown weapon
└─ Arsenal.cs                 target, weapon and cooldown selection
```

Weapons are companion equipment, separate from the cargo bag and from brain behaviours. The brain’s hands step asks the arsenal to fire whenever no work tool occupies the arm. The arsenal selects a target and weapon from what a simulated trajectory would land over its evaluation window; each weapon supplies facts, never a private suitability score. Projectile aiming belongs to `../Brain/ProjectileAiming/` and fighting movement belongs to `../Brain/Behaviours/Combat/`.

The choice trace and the final shot are deliberately separate. A weapon can score only on a solved swept arc, then its accuracy rotation is traced again before a projectile is spawned. A failed trace is cached only for its unchanged muzzle, target pose and terrain revision and only for a short freshness window; it is not a weapon cooldown, so an opened door or moving target can create a shot immediately. `CompanionWeapon.Fire` returns the spawned projectile slot so the arsenal can record one causal shot event with the final launch and expected impact.

Projectiles are player-owned and use the player’s ranged damage so kills, drops and eligible player effects stay attributable to the player. A new weapon implements its item, projectile, flight and fire facts; it does not decide when it is useful.
