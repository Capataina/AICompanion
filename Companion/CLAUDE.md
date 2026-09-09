# Companion — the NPC body and engine boundary

This folder owns the Terraria NPC, its player-derived rendering, temporary targeting stand-in, life state and the sole live motor. The brain requests controls through shared movement; this folder applies them to the engine body.

```
Companion/
├─ CLAUDE.md                this guide
├─ CompanionNPC.cs          NPC lifecycle, brain tick, item pickup and engine-facing state
├─ CompanionBody.cs         player renderer adapter and held-item presentation
├─ CompanionAggro.cs        temporary stand-in player while hostile AI runs
├─ CompanionBreath.cs       drowning countdown and damage
└─ CompanionSpawnRate.cs    spawn-rate adjustment while a living companion exists
```

`CompanionNPC.AI` updates breath, lets the brain produce a movement intent, applies door work, synchronises the renderer, and records telemetry. `SharedMovementSystem/TerrariaIntegration/ApplyControlsToCompanion.cs` is the motor implementation used here: it is the only authority allowed to change NPC movement state. External hits are reported to that motor so engine knockback becomes part of its next body state instead of a competing write.

The targeting stand-in is not the companion. `CompanionAggro` makes it visible only around a hostile’s AI call, then hides it again; damage, life, death and movement remain on the NPC. A downed companion reads as absent to hostile targeting.

## Traps

- The renderer expects `lastVisualizedSelectedItem` to be synchronised and a closed sprite batch around player drawing.
- The motor is an engine adapter, while planner and replay use portable movement. Do not document those as one proven physics implementation; native parity requires playtest evidence.
- Never move the NPC directly from another brain subsystem. That bypasses motor ownership and makes body diagnostics incomplete.
