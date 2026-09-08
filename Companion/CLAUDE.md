# Companion — the NPC and its body

```
Companion/
├─ CLAUDE.md
├─ CompanionNPC.cs    the ModNPC: mirrors the player's max life and defence, downed at zero and revived by 3 s beside it, owns Brain, Motor, Arsenal, Chopper, held item and animation, picks up items on contact, draws through CompanionBody; Find/Spawn/Instance
├─ CompanionBody.cs   a drawing-only Player (the player's own look with the gender swapped to female) synced each tick and drawn by Main.PlayerRenderer; Guide sprite fallback if the renderer ever throws
└─ CompanionMotor.cs  the only home of movement constants; MoveX, Stop, Face, Jump
```

The NPC exposes what actions call: `HoldItem`, `StartAnimation`, `SetAimRotation`, `Motor`, `Arsenal`, `Chopper`, `Bag`. It makes no decisions; `AI()` mirrors stats, handles downed, ticks the brain, collects touched items, syncs the body.

## Traps

- **`PreDraw` closes and reopens the sprite batch** around the player renderer, which draws to the device directly and expects a closed batch. Reopen with the NPC pass's own parameters (`Main.Transform`).
- **The body draws the held item from `lastVisualizedSelectedItem`**, set by hand in `CompanionBody.Sync`; without it the swing plays empty-handed.
- **`dontTakeDamage` toggles with downed.** `CheckDead` sets life to 1 first, or it fires every tick.
- **The bag lives on `CompanionPlayer`, not here**, so it saves with the character; `Bag` is a pass-through.
