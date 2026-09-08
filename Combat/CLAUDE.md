# Combat — weapons and how they fly

```
Combat/
├─ CLAUDE.md
├─ Weapons/
│  ├─ CompanionWeapon.cs     the base: item drawn, flight profile, base use time, reach, Suitability(situation), Fire
│  ├─ BowWeapon.cs           wooden bow, player-owned friendly arrow; suits distance, single targets, bosses
│  ├─ ThrowingKnifeWeapon.cs thrown knife; suits close range and crowds
│  └─ Arsenal.cs             the two equipped weapons; picks by Suitability, solves through the aimer, adds aim noise, fires on cooldown
└─ Ballistics/
   └─ TrajectoryAimer.cs     WeaponProfile (speed, straight ticks, gravity, cap) and the arc simulation against solid tiles with target lead
```

Every projectile is spawned with `owner = Main.myPlayer` and damage from the player's ranged stat, by decision: kills, drops and on-hit accessories are the player's. Fire rate is `BaseUseTime × FireRateFactor` (2 at launch) and every shot is rotated by up to `AimNoise` (4°); both are the mastery tree's first two upgrades. A new weapon is a subclass with its own `Suitability`; the arsenal and the actions need no change.
