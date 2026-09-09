# Survival — the body's own rescue

One action, and the top rung of the urgency ladder that scores above the ordinary band, because nothing the companion could be doing for the player — including guarding him, the rung below — is worth drowning for.

```
Survival/
├─ CLAUDE.md
└─ SurviveAction.cs   scores on the self sense's danger alone; asks for Exact at the nearest reachable refuge (standable, head row dry, no lava in the column); hand empty
```

`Score` is the self sense's `SelfDanger` scaled by `Weights.SurviveUrgency`, the top rung of the urgency ladder: nothing while the body is fine, a pull once breath is half gone or fire has caught, and past a *committed* guard near the end. It never keeps the companion out of water; crossing a pool is priced in `../../DecisionMatrix/Navigation/`, and this is the backstop when a crossing turns out longer than the breath or a lava price was paid and the fire is still burning. `Execute` finds a refuge by widening rings around the feet, checks the walker can reach it through `Reachability`, keeps it while it stays a refuge, and drops it the tick it stops being one. Forecast is zero: it never counts as time away from the player.

## Two rescues, and the second one is a floor rather than an alternative

Getting to a refuge is the plan; breaking the surface is the floor underneath it. Asking the motor to jump while the head is under water bobs the body off the floor whenever its feet touch down, and every break of the surface refills the breath, so the body survives anywhere the surface sits inside a wet jump's rise even with no route anywhere.

**The two are not alternatives, and writing them as alternatives is what made this folder's headline defect.** The bob used to run only where no refuge had been found at all, which reads as sensible and quietly excludes the case that actually drowns a companion: a refuge exists, the route to it stops short, and the body stands on the bottom of the pool holding a finished path. Whether a refuge was found is a fact about the map; whether the head is under water is the only fact drowning cares about, so the bob is asked for on the second and never gated on the first.

The shape of that mistake generalises past this folder: a last resort placed in the `else` of the ordinary path can only fire when the ordinary path was never attempted, which is the one situation it was not written for. A last resort belongs on the condition it exists to answer.

## Traps

- **The score must be able to beat a *committed* guard, not guard's raw ceiling.** The incumbent action keeps the commitment bonus, so beating guard means clearing guard's own urgency times that bonus; a survive that merely tops the band ties a running guard and loses to its bonus while the companion drowns. `Weights.GuardUrgency` and `Weights.SurviveUrgency` are one ladder for that reason and are changed together.
- **Reachability is walker reachability**, so a refuge across a jump the wet envelope cannot make is still offered and the navigator's replan finds another; the ring search returns the first refuge, not the best.
- **Self-rescue asks for a route the search proved, and it is the one caller that does.** `Reachability.WalkerReach` is three-valued, and this action reads its unknown as a no through `WalkerProvenReach`, because it *holds* the refuge it is given: an unknown accepted here is a commitment to walk somewhere the body may never arrive, with the breath running down the whole way. The threat sense's opposite rounding is right for the threat sense and fatal here.
- **A refuge stops being valid when the route to it does, not only when the tile does.** The tile is re-checked every tick and the route on a cadence, because a route can close behind the body — a one-way drop taken on the way, settling sand — while the tile stays a perfectly good refuge. Without the second check the body walks at a shore it can no longer reach until it drowns at the foot of it.
