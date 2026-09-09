# Doors — invalidate movement facts when the game opens a passage

```
Doors/
├─ CLAUDE.md
└─ DoorOpener.cs   asks Terraria’s door helper to open or close the obstruction
```

The game door helper bypasses the player-work tile hooks, so a successful open or close invalidates shared movement facts for all door rows. Otherwise the planner retains a closed-door edge cache after the motor has made an opening.
