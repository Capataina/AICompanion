# Heads-up display — the companion’s player-facing health notch

```
HeadsUpDisplay/
├─ CLAUDE.md                 this guide
└─ CompanionHealthBar.cs     draggable health notch and profile-card entry point
```

The notch is a glanceable health surface for the companion, not a diagnostic overlay. Clicking it opens `../ProfileCard/`; that card owns the status and optional-work controls, and opens `../Inventory/`’s bag when requested. Its location persists with `../PlayerIntegration/CompanionPlayer`. Dense brain telemetry and score rendering remain under `../Brain/BehaviourDiagnostics/`.

It uses raw screen pixels because Terraria’s mouse coordinates are raw screen pixels. Graphics disposal at mod unload must be queued to the main thread.

The same bounds supply drawing and pre-update mouse capture. A cursor entering and pressing on the notch in one update must be consumed before item use; setting `mouseInterface` only while drawing can miss that opening press. A drag keeps capture until release. Downing, reflexes and recovery precede the movement-stalled status; a stalled objective gets a visible label and cannot be hidden by a torch held in the free hand. The label reports non-progress, not an unproven route cause.
