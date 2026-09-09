# Enemy integration — make hostiles recognise the companion

```
EnemyIntegration/
├─ CLAUDE.md                 this guide
├─ CompanionAggro.cs         temporary player-slot stand-in during hostile AI
└─ CompanionSpawnRate.cs     spawn-rate adjustment while a living companion exists
```

Terraria hostile AI targets players, so `CompanionAggro` makes a drawing-and-targeting stand-in active only for the span of hostile AI, then removes it. The real companion remains the NPC; its life, downed state and movement never move into the player slot. The cleanup backstop hides the stand-in after each NPC pass and empties it on world unload.

The spawn adjustment treats a living companion as a second fighter for balance. It composes with earlier mods by multiplying the game’s resulting spawn interval and cap, rather than replacing their values.
