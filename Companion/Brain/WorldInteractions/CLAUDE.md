# World interactions — game abilities chosen elsewhere

This folder performs reliable closed-set abilities using Terraria’s own mechanics where possible. It never selects a behaviour, chooses a place, plans movement or writes the NPC body; `Behaviours/Work` decides when it is useful and the brain gives it a chosen position.

```
WorldInteractions/
├─ CLAUDE.md             this guide
├─ RenderTileCracks.cs   companion-owned tile damage presentation
├─ Chopping/             tree detection and axe damage
├─ Mining/               ore discovery and the game pickaxe path
├─ Doors/                open or close a route obstruction
├─ Torch/                free-hand light and supplied permanent torch placement
└─ WorldProtection/      bed-room protection for autonomous edits
```

Tools read the player’s held-item numbers but do not invoke the player item-use pipeline. That boundary is why a companion ability works consistently while preserving player upgrades that are explicitly read. A terrain-changing interaction invalidates shared movement’s cached terrain facts.
