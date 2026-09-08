# Weapons — what the companion fires

```
Weapons/
├─ CLAUDE.md
├─ CompanionWeapon.cs     the base: the item drawn in hand, the flight profile handed to the aimer, base use time, reach, Suitability(situation), Fire
├─ BowWeapon.cs           wooden bow, a vanilla WoodenArrowFriendly owned by the player; suits distance, single targets, bosses
├─ ThrowingKnifeWeapon.cs a vanilla throwing knife; suits close range and crowds
└─ Arsenal.cs             the two equipped weapons; CanEngage cached 20 ticks per NPC, a failed solve rests 15; picks by Suitability, solves through ../../Brain/Aiming/, adds AimNoise, fires on cooldown
```

See `../CLAUDE.md` for the ownership rule (every projectile is the player's) and the two launch handicaps the mastery tree lifts. A weapon reads its numbers from `ContentSamples` and never runs an item through the player's item code.
