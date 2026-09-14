# Heads-up display — the companion’s player-facing notch of three bars

```
HeadsUpDisplay/
├─ CLAUDE.md                 this guide
├─ CompanionHealthBar.cs     draggable notch of health, mana and experience, flanking icons and profile-card entry point
├─ DescribeCompanionHud.cs   stable family/activity symbols and paused/absent presentation
└─ DrawPurposeSymbol.cs     authored monochrome geometry independent of item art
```

The notch is a glanceable surface for the three numbers a player checks without opening anything, not a diagnostic overlay. Health is the hero, a full-width pill under the name and its reading; beneath it mana and experience share one row as two half-width pills, mana on the left with its reading flush left and experience on the right with its reading flush right, so the two readings frame the row the way the name and the health reading frame the row above. That order, health above mana above experience, is the same column the profile card's identity strip stacks, so a player learns it once. Mana is the game's own mana-star blue and experience the card's gold; the values live in the class. The mana reading is a plain fraction and its hover says what the pool does to a cast, because the pool never refuses a cast (`../Weapons/CompanionMana.cs`) and a bar that only drains would otherwise read as a resource the companion runs out of. The experience reading is the level beside the fraction toward the next, and its hover names the level the fraction leads to; the fraction comes from `../Progression/`. Nothing animates: a bar snaps. Downed keeps the grey health pill with the revive percentage and leaves the two small bars live.

Clicking the notch opens `../ProfileCard/`; that card owns the status and optional-work controls, and opens `../Inventory/`’s bag when requested. Its location persists with `../PlayerIntegration/CompanionPlayer`. Dense brain telemetry and score rendering remain under `../Brain/Infrastructure/Diagnostics/`.

The three pill tracks come from one public geometry function the drawing and the headless HUD fixture both read, so the fixture's assertions about where the bars sit are the drawing's own arithmetic rather than a second copy of it; the fixture pins the mana and experience fractions and counts the painted pixels along each small pill's centre row.

It uses raw screen pixels because Terraria’s mouse coordinates are raw screen pixels. Graphics disposal at mod unload must be queued to the main thread.

The family icon sits left of health and the activity icon right, enclosed in one notch. Both consume the brain's single completed-tick presentation snapshot. Family symbols are a basket for gathering, crossed blades for combat and a four-point spark for nearby assistance; the blades are a category symbol rather than the equipped weapon. Each of the seven activities has a stable authored monochrome symbol and a naming tooltip. Tool symbols do not change with ore type or equipment. Paused work keeps its activity symbol at reduced opacity during shared safety or recovery. When no ordinary activity is selected, or the companion is downed, both icons use neutral marks rather than inventing a survival behaviour.

The same bounds include both icon wings for drawing, screen-edge clamping and pre-update mouse capture. A cursor entering and pressing on the notch or either icon in one update must be consumed before item use; setting `mouseInterface` only while drawing can miss that opening press. A drag keeps capture until release. Unsuspended movement non-progress retains the Stuck label below health, independently of the held item. The label reports non-progress, not an unproven route cause.
