# Heads-up display — the companion’s player-facing health notch

```
HeadsUpDisplay/
├─ CLAUDE.md                 this guide
├─ CompanionHealthBar.cs     draggable health notch, flanking icons and profile-card entry point
├─ DescribeCompanionHud.cs   stable family/activity symbols and paused/absent presentation
└─ DrawPurposeSymbol.cs     authored monochrome geometry independent of item art
```

The notch is a glanceable health surface for the companion, not a diagnostic overlay. Clicking it opens `../ProfileCard/`; that card owns the status and optional-work controls, and opens `../Inventory/`’s bag when requested. Its location persists with `../PlayerIntegration/CompanionPlayer`. Dense brain telemetry and score rendering remain under `../Brain/BehaviourDiagnostics/`.

It uses raw screen pixels because Terraria’s mouse coordinates are raw screen pixels. Graphics disposal at mod unload must be queued to the main thread.

The family icon sits left of health and the activity icon right, enclosed in one notch. Both consume the brain's single completed-tick presentation snapshot. Family symbols are a basket for gathering, crossed blades for combat and a four-point spark for nearby assistance; the blades are a category symbol rather than the equipped weapon. Each of the seven activities has a stable authored monochrome symbol and a naming tooltip. Tool symbols do not change with ore type or equipment. Paused work keeps its activity symbol at reduced opacity during shared safety or recovery. When no ordinary activity is selected, or the companion is downed, both icons use neutral marks rather than inventing a survival behaviour.

The same bounds include both icon wings for drawing, screen-edge clamping and pre-update mouse capture. A cursor entering and pressing on the notch or either icon in one update must be consumed before item use; setting `mouseInterface` only while drawing can miss that opening press. A drag keeps capture until release. Unsuspended movement non-progress retains the Stuck label below health, independently of the held item. The label reports non-progress, not an unproven route cause.
