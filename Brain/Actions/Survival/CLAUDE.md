# Survival — the body's own rescue

One action, and the only one allowed to score above the top of the scale, because nothing the companion could be doing for the player is worth drowning for.

```
Survival/
├─ CLAUDE.md
└─ SurviveAction.cs   scores on the self sense's danger alone; asks for Exact at the nearest reachable refuge (standable, head row dry, no lava in the column); hand empty
```

`Score` is the self sense's `SelfDanger` scaled past one: nothing while the body is fine, a pull once breath is half gone or fire has caught, and above guard's ceiling near the end. It never keeps the companion out of water; crossing a pool is priced in `../../DecisionMatrix/Navigation/`, and this is the backstop when a crossing turns out longer than the breath or a lava price was paid and the fire is still burning. `Execute` finds a refuge by widening rings around the feet, checks the walker can reach it through `Reachability`, keeps it while it stays a refuge, and drops it the tick it stops being one. Forecast is zero: it never counts as time away from the player.

## Traps

- **The score must be able to beat guard at full danger**, which is why it is the one score scaled above one; a survive that tops out at one ties guard and loses to guard's commitment bonus while the companion drowns.
- **Reachability is walker reachability**, so a refuge across a jump the wet envelope cannot make is still offered and the navigator's replan finds another; the ring search returns the first refuge, not the best.
