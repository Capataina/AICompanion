# Senses — the world model

Rebuilt every tick from the game, read by every other part, and never a decision maker. `Senses.cs` is the aggregate; each file below owns one kind of fact.

```
Senses/
├─ CLAUDE.md
├─ Senses.cs             aggregate; Update(npc, player, breath) runs the parts in order and stamps the tick
├─ CompanionSense.cs     the companion's own body: breath fraction and head-under-water from the NPC's breath, lava-wet, on fire, life fraction, damage over the last second; derives SelfDanger
├─ PlayerSense.cs        position, smoothed travel intent (outlives a pause), fighting, real chopping, sight to companion
├─ ThreatSense.cs        one ThreatRecord per hostile; reachability (cached, staggered), observed speed, shooters; derives PlayerDanger and Horizon
├─ ThreatRecord.cs       the per-hostile record and its predicted hitbox
├─ LootSense.cs          items on the ground within reach, nearest first, with a value
├─ TileDamageWatcher.cs  GlobalTile.KillTile hook: the player really hit a tree or an ore (fail hits included); companion hits excluded by a flag
├─ LightSense.cs         ambient brightness on a coarse grid over a screen-sized window centred on the companion with a disc around the companion cut out, plus the light at the player and at the companion; refreshed on a short cadence
└─ LineOfSight.cs        names over Collision.CanHitLine
```

## The light numbers

`Ambient` is what the torch decision reads, because it is the one number the companion's own torch cannot raise: the cut-out disc is wider than a torch's glow. The window it averages follows the companion, not the camera: the first version sampled the screen, which is the camera on the player, so a companion sent into a cave while the player stood in daylight read the player's light and never lit up. `AtPlayer` and `AtCompanion` are for the overlay and later factors. The lighting engine only holds values for the visible screen and reads 0 outside it, so the window is clipped to the screen before averaging; without the clip a companion near the screen edge counted the unlit outside as black and lit a torch in a place that was dim, not dark. With no sample left the companion is off screen, ambient reads 0, and a companion far away lights its torch wherever it is.

## The companion's own danger

`SelfDanger` is the largest of three readings and is kept apart from `PlayerDanger` on purpose, so "the player is safe" never hides "I am drowning": drowning, which is zero until breath is half gone, climbs to one as it runs out and is one outright once it is nearly gone with the head still under; burning, which is one in lava and a fraction while on fire; and bleeding, the share of max life lost in the last second scaled by how little life is left, so a big hit at full health is a note and the same hit near death is an alarm. The breath itself lives on `Companion/CompanionBreath.cs`, because it changes the body (drowning damage); this sense only reads it.

## The two derived numbers

`PlayerDanger` is the most urgent reachable threat's urgency: weight(damage share, boss) × closeness(time-to-player, 0 at six seconds out) × sight factor, with a shooter that has a sight line treated as already there. `Horizon` is the smallest (time-to-player − the companion's return time) over reachable threats, clamped at zero, `float.MaxValue` with none. The chooser charges any action whose forecast outlasts it.

## Traps

- **`WorldGen.KillTile` fires the hook for the companion's own swings too.** `TileChopper` and `TileMiner` raise `CompanionIsHitting` around their calls; without it the companion's chopping reads as the player's and it never stops.
- **Reachability is refreshed once a second per NPC on a stagger**, so a threat is up to a second stale. Raising the refresh rate multiplies A* calls per tick.
- **Observed speed decays slowly and starts at 1 px/tick.** A dashing enemy is underestimated until its first dash is seen.
