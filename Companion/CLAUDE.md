# Companion — the NPC and its body

```
Companion/
├─ CLAUDE.md
├─ CompanionNPC.cs    the ModNPC: mirrors the player's max life and defence, downed at zero and revived by 3 s beside it, owns Brain, Motor, Arsenal, Chopper, Miner (sharing the chopper's HitTile), Torch, held item and animation, picks up items on contact, draws through CompanionBody; the torch takes the hand after the brain when no action held anything; Find/Spawn/Instance
├─ CompanionBody.cs   a drawing-only Player (the player's own look with the gender swapped to female) synced each tick and drawn by Main.PlayerRenderer; Guide sprite fallback if the renderer ever throws
└─ CompanionMotor.cs  the only home of movement constants; MoveX, Stop, Face, Jump, JumpScaleForTiles, JumpOffsetAt, and ApplySteps (the game's StepUp/StepDown, run after the brain each tick)
```

The NPC exposes what actions call: `HoldItem`, `StartAnimation`, `SetAimRotation`, `Motor`, `Arsenal`, `Chopper`, `Bag`. It makes no decisions; `AI()` mirrors stats, handles downed, ticks the brain, collects touched items, syncs the body.

## Traps

- **`PreDraw` closes and reopens the sprite batch** around the player renderer, which draws to the device directly and expects a closed batch. Reopen with the NPC pass's own parameters (`Main.Transform`).
- **The body draws the held item from `lastVisualizedSelectedItem`**, set by hand in `CompanionBody.Sync`; without it the swing plays empty-handed.
- **`knockBackResist` is backwards from its name: 1 is full knockback, 0 is immunity.** The companion sits at 0.75; the first run had it at 0 and it never moved when hit.
- **`dontTakeDamage` toggles with downed.** `CheckDead` sets life to 1 first, or it fires every tick.
- **The bag lives on `CompanionPlayer`, not here**, so it saves with the character; `Bag` is a pass-through.
