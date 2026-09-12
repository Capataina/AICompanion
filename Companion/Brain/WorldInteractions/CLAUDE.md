# World interactions — game abilities chosen elsewhere

This folder performs reliable closed-set abilities using Terraria’s own mechanics where possible. It never selects a behaviour, chooses a place, plans movement or writes the NPC body; `Behaviours/Work` decides when it is useful and the brain gives it a chosen position.

```
WorldInteractions/
├─ CLAUDE.md             this guide
├─ RenderTileCracks.cs   companion-owned tile damage presentation
├─ ObserveTileToolEffect.cs native tool-call snapshots and observed damage/removal outcomes
├─ Chopping/             tree detection and axe damage
├─ Mining/               ore discovery and the game pickaxe path
├─ Doors/                open or close a route obstruction
├─ Torch/                free-hand light and supplied permanent torch placement
└─ WorldProtection/      bed-room protection for autonomous edits
```

Tools read the player’s held-item numbers but do not invoke the player item-use pipeline. That boundary is why a companion ability works consistently while preserving player upgrades that are explicitly read. A terrain-changing interaction invalidates shared movement’s cached terrain facts.

Tool execution preserves immutable before/after observations around the native call. A requested swing, increased damage in that tool's hit table, tile removal and changed material/frame are separate outcomes. The hit table belongs to the hitter, so its damage is not shared world health; removal alone does not establish item yield. Tool instances retain their latest observation with its tick and monotonically increasing attempt number, and cooldown rejection creates no observation. Consumers join attempt identity with actor, tool and session rather than treating a retained result as fresh each tick.
