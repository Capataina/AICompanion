# Character body — the live NPC and renderer boundary

```
CharacterBody/
├─ CLAUDE.md          this guide
├─ CompanionNPC.cs    NPC lifecycle, brain entry, pickup and engine state
├─ CompanionBody.cs   player-renderer adapter and held-item presentation
└─ CompanionBreath.cs drowning countdown and NPC damage
```

`CompanionNPC` is the live body. It calls the brain, accepts the resolved motor controls, updates breath, collects touched items, synchronises the renderer and records the tick. The movement adapter is the only path that changes live NPC movement; other systems describe intent or observations.

The renderer borrows Terraria’s player drawing path, so it needs the selected-item visual state synchronised and a closed sprite batch around player drawing. Breath is NPC life-state logic, not player state: it drives NPC damage and downed behaviour while world observation reads the resulting facts.
