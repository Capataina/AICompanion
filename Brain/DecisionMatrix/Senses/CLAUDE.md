# Senses — the world model

Rebuilt every tick from the game, read by every other part, and never a decision maker. `Senses.cs` is the aggregate; each file below owns one kind of fact.

```
Senses/
├─ CLAUDE.md
├─ Senses.cs             aggregate; Update(npc, player) runs the parts in order and stamps the tick
├─ PlayerSense.cs        position, smoothed travel intent (outlives a pause), fighting, real chopping, sight to companion
├─ ThreatSense.cs        one ThreatRecord per hostile; reachability (cached, staggered), observed speed, shooters; derives PlayerDanger and Horizon
├─ ThreatRecord.cs       the per-hostile record and its predicted hitbox
├─ LootSense.cs          items on the ground within reach, nearest first, with a value
├─ TileDamageWatcher.cs  GlobalTile.KillTile hook: the player really hit a tree or an ore (fail hits included); companion hits excluded by a flag
├─ LightSense.cs         ambient brightness on a 4-tile grid over the screen with a 10-tile disc around the companion cut out, plus the light at the player and at the companion; refreshed every 10 ticks
└─ LineOfSight.cs        names over Collision.CanHitLine
```

## The light numbers

`Ambient` is what the torch decision reads, because it is the one number the companion's own torch cannot raise: a torch's glow reaches about eight tiles and the cut-out is ten. `AtPlayer` and `AtCompanion` are for the overlay and later factors. Off screen every reading is 0, since the lighting engine holds nothing there, so a companion sent away lights its torch wherever it is.

## The two derived numbers

`PlayerDanger` is the most urgent reachable threat's urgency: weight(damage share, boss) × closeness(time-to-player, 0 at six seconds out) × sight factor, with a shooter that has a sight line treated as already there. `Horizon` is the smallest (time-to-player − the companion's return time) over reachable threats, clamped at zero, `float.MaxValue` with none. The chooser charges any action whose forecast outlasts it.

## Traps

- **`WorldGen.KillTile` fires the hook for the companion's own swings too.** `TileChopper` and `TileMiner` raise `CompanionIsHitting` around their calls; without it the companion's chopping reads as the player's and it never stops.
- **Reachability is refreshed once a second per NPC on a stagger**, so a threat is up to a second stale. Raising the refresh rate multiplies A* calls per tick.
- **Observed speed decays slowly and starts at 1 px/tick.** A dashing enemy is underestimated until its first dash is seen.
