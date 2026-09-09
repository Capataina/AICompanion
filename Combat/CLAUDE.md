# Combat — the weapons

```
Combat/
├─ CLAUDE.md
└─ Weapons/
   ├─ CompanionWeapon.cs     the base, facts only: item drawn, projectile, flight profile, base damage and use time, reach, pierce, the fire-rate and damage factors, Fire
   ├─ BowWeapon.cs           wooden bow, player-owned friendly arrow; one body a shot, harder per second against it
   ├─ ThrowingKnifeWeapon.cs thrown knife, its projectile passes through two bodies; softer per throw, twice as fast
   └─ Arsenal.cs             the two equipped weapons; chooses by expected damage over a window, solves through the brain's aimer, adds aim noise, fires on cooldown
```

Weapons are equipment, which is why they sit outside the brain: how the companion aims is `../Brain/Aiming/`, *when* it fires is `Brain.Engage` — every tick, whatever the feet were told, unless a tool is in the arm — where it walks to fight is `../Brain/Actions/Combat/`, and this folder is what it fires. Every projectile is spawned with `owner = Main.myPlayer` and damage from the player's ranged stat, by decision: kills, drops and on-hit accessories are the player's. Fire rate is `BaseUseTime × FireRateFactor` (2 at launch), damage is `BaseDamage × DamageFactor`, and every shot is rotated by up to `AimNoise` (4°); the first two are the mastery tree's earliest upgrades and the third is what the tree's accuracy nodes lift.

A new weapon is a subclass that states its numbers and nothing else — it implements no scoring, because the arsenal decides by working out what each weapon would actually land in the next few seconds and a weapon carrying its own view of when it suits a situation would be overruling that arithmetic with a guess. `Weapons/CLAUDE.md` has the quantity, the three properties that make it work and why the first design's per-weapon `Suitability` was replaced.
