# Weapon Experiments — weapon design before any weapon is written

```
WeaponExperiments/
├─ CLAUDE.md      this guide
└─ index.html     the twenty-weapon design matrix, self-contained
```

This folder is theorycrafting and holds no game code. Nothing here is compiled, referenced or loaded by the mod, and `build.txt` excludes the folder from packaging so a page of design sketches never ships inside the `.tmod`. It exists so weapon ideas can be argued about, rejected and rewritten at the cost of an HTML edit rather than the cost of a `ModItem`, a `ModProjectile` and a sprite sheet.

## What the page is for and how it is built

The page is a matrix: five rows for the stages the mastery tree unlocks against, four columns for melee, ranger, rogue and mage. The design premise it encodes is twenty core weapons rather than one weapon per class per boss — the in-between eras spend their budget on upgrade branches to weapons you already own, which is why every card carries an `Upgrade branches` line and no card carries a damage number. Companion weapons scale off the player's damage and are gated on resources and companion level rather than on boss kills, so nothing on the page states a stat.

Each card animates its own behaviour, because a weapon whose properties cannot be read off its loop has not been designed yet — the same standard the Terraria wiki's dummy-arena gifs set. Every demo draws into one shared 260×140 tile world at one scale, so two weapons in different columns are genuinely comparable side by side; that is the reason there is a single `Arena` engine rather than per-card animation code. A weapon is added by appending one object to the `TIERS` array with a `demo(ctx, p, A, level)` function, where `p` runs 0 to 1 across the loop and `level` is 1, 2 or 3 — the upgrades are additive, so a level is a branch inside one demo rather than three separate animations.

**An upgrade adds a mechanic; it never adds a number.** "Two extra leaps", "a third pass", "five instead of three", "a second one" and "hits harder while shielded" are counters wearing an upgrade's clothes: they read as progression in a description and change nothing about how the weapon is used. Every level on this page names something the weapon could not previously do — the return trip follows the companion's walk, the volley picks its own targets per shard, the shield's overflow routes to the companion, the second round detonates instead of splitting. The one exception in the roster is Millstone's second weight, which is a count and is kept because it was asked for by name.

**A weapon has to look like it belongs to its tier.** The tell that caught this was a post-Moon-Lord melee weapon that never released a projectile, which is the wrong shape for an era where melee throws things; the fix was making it throw, not moving it down the ladder.

**No upgrade may cost the player anything.** A weapon that consumed the player's own projectiles to power itself read as a nerf however large the resulting shot was, and was cut for it.

**An arena has to prove the property, not merely show the weapon.** A homing shot needs a target it was not aimed at, so the seeking is the only explanation for the path; a piercing shot needs bodies standing in a line, and the projectile drawn at body height rather than above it; a weapon that cannot reach flyers needs a flyer on screen that it visibly does not reach; a weapon that goes over cover needs the target placed behind the cover. An arena where the weapon simply travels from left to right and something flashes proves nothing that the sentence beside it did not already say.

Tier colours are Terraria's own rarity ladder — blue, lime, yellow, cyan, red, in the game's real ordering — so the row rails read as progression to anyone who has played it. Everything else on the page is neutral on purpose: those five are the only thing colour carries information about, and an earlier navy ground competed with all five at once.

## Traps

- **`A.gy(x)` starts its scan below the ceiling band on purpose.** It returns the ground under a column, and an enclosed terrain such as `box` has solid tiles at row 0; a scan from the very top reports the ceiling as the floor and stands every body off the top edge, which renders as a blank canvas with no error. The general rule for anything added here: a terrain query that assumes open sky above is wrong the moment a demo needs a roof.
- **A canvas that draws nothing looks identical to one that draws correctly in a page screenshot.** Verify by sampling non-background pixels from `getImageData` across several values of `p`, not by eye — blank at one instant is normal dead time between beats, blank at every instant is a broken demo. The check asks `PAL_RGB` what the background is rather than naming colours itself, because a restyle that changed three hard-coded constants would leave the check passing while measuring nothing.
- **An upgrade that renders identically to the level below it passes every blank check.** The second instrument diffs level 1 against 2 and 2 against 3 frame by frame; anything under a few hundred changed pixels across a whole loop is an upgrade nobody can see, whatever its description claims. Freeblade's mirrored swing shipped at 105 changed pixels before this was measured.
- **A projectile drawn above the bodies it claims to hit reads as correct in every screenshot.** Slime bodies span `gy-13` to `gy-4`; a shot flying at `gy-22` misses everything by nine pixels while looking entirely plausible. Ruptureshot shipped that way.
- **Only visible canvases animate**, via an `IntersectionObserver`; twenty always-running loops is real CPU for a page nobody reads end to end in one sitting. Anything that measures a canvas must force `on` first or it reads an empty buffer.
