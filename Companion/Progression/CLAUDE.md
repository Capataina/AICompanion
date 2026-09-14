# Progression — what the companion has earned

```
Progression/
├─ CLAUDE.md                this guide
└─ CompanionExperience.cs   the experience total, the level curve, and the fraction the notch draws
```

The companion earns experience only from what it completes itself beside the player, never from the player's own actions; that is the owner's ruling of 14 September 2026 and the reason the award is fed from the attempt-outcome record the activity owner already emits with attribution rather than from any game event. The total is character state, saved with `../PlayerIntegration/`, and a level is read off it on the curve in this folder. Nothing in `../Brain/` reads a level: a level changes what the companion can do and survive through the mastery tree and never what it decides, which is the ruling in the root guide.

`../HeadsUpDisplay/` draws the level and the fraction toward the next; `../ProfileCard/`'s mastery page will spend the points a level grants once the tree's contents are numbered. The per-family worth of one productive effect and the curve are tuning numbers and live here, in the class, where the next person balancing levels will look.
