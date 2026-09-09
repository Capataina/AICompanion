# World observation — one shared account of the world

This folder rebuilds facts from Terraria once per brain tick. It observes; it never decides, chooses a position, plans a route, or writes movement. `CoordinateBrainTick` passes its aggregate to reflex assessment and behaviour selection, so a new fact belongs here when more than one downstream system could otherwise derive it differently.

```
WorldObservation/
├─ CLAUDE.md                 this guide
├─ Senses.cs                 aggregate and update order
├─ ObserveCompanion.cs       breath, liquid, fire, life and recent damage; derives self danger
├─ ObservePlayer.cs          position, travel intent, fighting and sight
├─ ObservePlayerWork.cs      player tool hits and terrain-change notification
├─ ObserveThreats.cs         hostile records, reachability, danger and safety horizon
├─ ObserveProjectiles.cs     hostile projectile predictions for reflexes
├─ ObserveLoot.cs            collectible drops that fit a player stack or bag
├─ ObserveLight.cs           ambient light outside the companion’s own glow
├─ ThreatRecord.cs           a hostile and its predicted hitbox
└─ LineOfSight.cs            the named game line-of-sight query
```

`Senses.Update` runs player, threats, projectiles, loot, light and self observation before selection reads any value. Separate player and self danger are intentional: leaving a threatened player and walking into danger are different risks. Threats also derive a safety horizon, which selection uses to discount a behaviour that would keep the companion away too long.

Player tool hits are evidence, not animation guessed as intent. `ObservePlayerWork` records real axe and pick hits for work behaviours and tells shared movement whenever a relevant tile changes, invalidating cached movement facts. Doors report their own changes because the game’s door helper bypasses those tile hooks.

`Ambient` is the torch’s authority because its sampling excludes the companion’s glow. The held torch, its light and map reveal follow the interaction layer’s `Shown` answer; observation does not decide whether the hand is free.

## Traps

- Never calculate a world fact inside a behaviour: selection runs every behaviour every tick and duplicated readings drift.
- `PlayerDanger` and `SelfDanger` answer different questions. A formula of `1 - danger` must name whose danger it consumes.
- The player’s recorded trail can contain a double-jump route the companion cannot make; it is evidence for a scenario, not proof the planner is defective.
