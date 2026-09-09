# Weapons — what the companion fires

```
Weapons/
├─ CLAUDE.md
├─ CompanionWeapon.cs     the base, and facts only: the item drawn in hand, the projectile it fires, the flight profile handed to the aimer, base damage and use time, reach, pierce read off the projectile, projectiles per shot, the fire-rate and damage factors the tree lifts, Fire
├─ BowWeapon.cs           wooden bow, a vanilla WoodenArrowFriendly owned by the player; one body a shot, the harder hitter per second against that body
├─ ThrowingKnifeWeapon.cs a vanilla throwing knife, whose projectile passes through two bodies; softer per throw, twice as fast
└─ Arsenal.cs             the two equipped weapons and what to shoot with them; picks the target by expected damage weighted by urgency and the weapon by expected damage over a window, both cached per target; solves through ../../Brain/ProjectileAiming/, adds AimNoise, fires on cooldown, and records why it did not
```

## The weapon holds no opinion about when to use it

A weapon states numbers and the arsenal does all the judging. That is the opposite of the first design, where each weapon implemented `Suitability(situation)` and returned a product of hand-set multipliers — a range curve, a crowd bonus, a boss discount — which is a guess about the outcome standing where the outcome belongs. Those multipliers could not be right: the knife's `crowdAround >= 2 ? 1f : 0.7f` fired on enemies clustered near the target whether or not the arc crossed any of them, and no value of it could have known that a wall sat between.

What replaced it is one quantity, computed per weapon per target: **the damage this weapon would actually land in the next few seconds, fired from where the companion is standing right now.** The larger number wins. Three properties of that quantity carry the whole design, and each of them exists because the naive reading fails:

- **It is counted over a window, not per shot.** The window is why a slower weapon that clears a line can beat a faster one that kills singly — the thing the game's own DPS readout cannot express, since output per second is blind to what the output lands on.
- **Every hostile contributes only the life it still has.** A shot worth three hundred against a slime worth twenty is worth twenty. Without that clamp the rule prefers the heaviest weapon in the bag for every critter, and the heaviest weapon then kills a line of them one at a time while a weapon that clears the line scores lower.
- **The bodies a shot hits are the ones its simulated arc crosses**, in order, capped by the projectile's own penetration. This is asked of the flight rather than of a radius, so a wall, a gap, a bad angle or a target on the far side of a hill removes them, and nothing anywhere names walls, gaps, angles or hills.

Read forward, that is what lets a roster of ninety weapons need no per-weapon behaviour. A weapon that pierces, sprays, crits, hits hard, fires fast or reaches further is compared on the same scale as every other, and a weapon whose shot has no solution scores zero — which is also the fix for a companion standing still holding a weapon it cannot fire, a state it died in on 2026-09-09 with thirteen hostiles on it and a bow in its hand.

The one thing scoring cannot do is rescue a weapon that is strictly worse, and both of the launch pair were: vanilla gives the bow 4 + 5 damage every 30 ticks and the knife 12 every 15, so before any scaling the knife dealt 2.67 times the bow's damage a second *and* pierced twice, and a correct chooser would simply never pick the bow. `DamageFactor` is the knob that fixes the roster rather than the rule, and it is set so that one enemy on the line favours the bow and two favour the knife, because a crossing point somewhere inside the situations that actually occur is what makes both slots get used. A roster whose weapons are never all chosen is a roster with dead slots, which is the same defect as a chooser that always picks one thing.

## The arsenal also picks what to shoot at, because the hands are no longer the feet's passenger

Firing left the actions on 2026-09-09 and became a step of its own that runs every tick from `Brain.Engage`, which meant the target could no longer be "whatever the running action was walking toward" — a companion following the player has no combat target at all, and that used to be the whole reason it could not shoot while following. `BestTarget` supplies one: among the nearest few reachable hostiles, the one with the largest expected damage weighted by the greater of its urgency to the player and its urgency to the companion. So it shoots what it can most hurt, tilted toward whatever is most about to hurt someone.

Three things keep that affordable, since it is asked every tick while the brain is also deciding: only the nearest handful of candidates are considered, the winner is held for a short while so the aim does not flick between two equal enemies, and the per-weapon expected-damage arithmetic behind it is itself cached per target. The damage per hit subtracts half the target's defence, the way the game does, and counts no critical hits at all — a projectile spawned from an NPC source carries none of the player's crit chance however the player's ranged damage multiplier is applied, so counting one would score every weapon above what it lands.

`LastFireOutcome` records why no projectile left the hands — `fired`, `cooldown`, `no-arc`, `no-target`, `hands-busy` — and it exists because a record saying only whether a shot happened cannot separate a reload from a body standing where nothing can be hit, and those two want opposite fixes. It is written to the telemetry's `fire` column and is what the session reader keys "it was under attack and did nothing" on.

## Traps

- **A weapon scored on one damage number and fired with another is chosen for a reason that never happens.** `DamagePerHit` is the single method both the scorer and `Fire` read, deliberately; the first version computed damage inline inside each `Fire`.
- **`Pierce` comes from the projectile, not from the weapon.** It is read from `ContentSamples.ProjectilesByType[...].penetrate`, so a weapon that later fires something else pierces whatever that fires. Unlimited penetration reports the width of the arsenal's pierce buffer, because nothing can hit more bodies than the arc is walked for.
- **The pierce walk and the aim solve step through the same flight function.** Two loops with their own copy of the projectile physics is the instrument-versus-code split that has produced four defects elsewhere in this project; `Advance` in `../../Brain/ProjectileAiming/` is the one implementation.
- **The choice is cached for a dozen ticks and the world moves inside that.** So `TryFire` re-solves the arc on the tick it fires and falls back to the other weapon when the cached winner's arc has since gone, rather than resting on a stale choice.
- **Simulating both weapons' arcs is dear**, which is what the per-target choice cache is for. The pierce walk runs over the threat sense's own hostile list rather than every NPC slot, because it is asked while the brain is deciding.

See `../CLAUDE.md` for the ownership rule (every projectile is the player's) and the launch handicaps the mastery tree lifts. A weapon reads its numbers from `ContentSamples` and never runs an item through the player's item code.
