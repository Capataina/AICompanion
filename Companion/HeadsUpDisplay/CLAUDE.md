# Heads-up display — the companion’s player-facing health notch

```
HeadsUpDisplay/
├─ CLAUDE.md                 this guide
└─ CompanionHealthBar.cs     draggable health notch and bag entry point
```

The notch is a glanceable health surface for the companion, not a diagnostic overlay. Clicking it opens `../Inventory/`’s bag panel; its location persists with `../PlayerIntegration/CompanionPlayer`. Dense brain telemetry and score rendering remain under `../Brain/BehaviourDiagnostics/`.

It uses raw screen pixels because Terraria’s mouse coordinates are raw screen pixels. Graphics disposal at mod unload must be queued to the main thread.
