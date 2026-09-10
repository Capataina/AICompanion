# Weapon Experiments — weapon design before any weapon is written

```
WeaponExperiments/
├─ CLAUDE.md      this guide
└─ index.html     the twenty-weapon design matrix, self-contained
```

This folder is theorycrafting and holds no game code. Nothing here is compiled, referenced or loaded by the mod, and `build.txt` excludes the folder from packaging so a page of design sketches never ships inside the `.tmod`. It exists so weapon ideas can be argued about, rejected and rewritten at the cost of an HTML edit rather than the cost of a `ModItem`, a `ModProjectile` and a sprite sheet.

## What the page is for and how it is built

The page is a matrix: five rows for the stages the mastery tree unlocks against, four columns for melee, ranger, rogue and mage. The design premise it encodes is twenty core weapons rather than one weapon per class per boss — the in-between eras spend their budget on upgrade branches to weapons you already own, which is why every card carries an `Upgrade branches` line and no card carries a damage number. Companion weapons scale off the player's damage and are gated on resources and companion level rather than on boss kills, so nothing on the page states a stat.

Each card animates its own behaviour, because a weapon whose properties cannot be read off its loop has not been designed yet — the same standard the Terraria wiki's dummy-arena gifs set. Every demo draws into one shared 260×140 tile world at one scale, so two weapons in different columns are genuinely comparable side by side; that is the reason there is a single `Arena` engine rather than per-card animation code. A weapon is added by appending one object to the `TIERS` array with a `demo(ctx, p, A)` function, where `p` runs 0 to 1 across the loop.

Tier colours are Terraria's own rarity ladder — blue, lime, yellow, cyan, red, in the game's real ordering — so the row rails read as progression to anyone who has played it, rather than as an arbitrary palette.

## Traps

- **`A.gy(x)` starts its scan below the ceiling band on purpose.** It returns the ground under a column, and an enclosed terrain such as `box` has solid tiles at row 0; a scan from the very top reports the ceiling as the floor and stands every body off the top edge, which renders as a blank canvas with no error. The general rule for anything added here: a terrain query that assumes open sky above is wrong the moment a demo needs a roof.
- **A canvas that draws nothing looks identical to one that draws correctly in a page screenshot.** Verify by sampling non-background pixels from `getImageData` across several values of `p`, not by eye — blank at one instant is normal dead time between beats, blank at every instant is a broken demo.
- **Only visible canvases animate**, via an `IntersectionObserver`; twenty always-running loops is real CPU for a page nobody reads end to end in one sitting. Anything that measures a canvas must force `on` first or it reads an empty buffer.
