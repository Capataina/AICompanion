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

Projectiles are player-owned and use the player’s ranged damage so kills, drops and eligible player effects stay attributable to the player. A new weapon implements its item, projectile, flight and fire facts; it does not decide when it is useful.
