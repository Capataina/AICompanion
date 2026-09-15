# Assets

Concept art for the companion, kept in the repository and left out of the packaged mod (`build.txt`'s `buildIgnore` names `Assets\*`). Nothing in this folder is loaded by the game. The sheets are the reference the in-game art is drawn from, not the in-game art, and this file holds how that translation is meant to work while it is still being designed.

```
Assets/
├─ Companion Drone Concept 01 - Full Specification Sheet.png     the drone's full sheet: views, ring motion, eye states, utility modes, close-ups
├─ Companion Drone Concept 01 - Compact Specification Sheet.png  the same design, fewer panels
└─ CLAUDE.md                                                     this file: how the sheets become in-game art, and what is still being decided
```

## Status — 15 September 2026: agreed in discussion, not yet recorded as a decision

Everything below the rig section was worked out with the owner on 15 September 2026 and he agreed to it, but he asked to keep discussing before it is recorded as a decision. When it settles: the art direction goes into Slate's Design field (`design.structure.surfaces` still says how a sheet becomes in-game drawing has not been worked out), the play behaviours (flashlight, downed fall, self-revival, the effects setting) go into README's Expected Behaviour and its Behaviour-By-Behaviour rows, and this section is rewritten as the settled rule rather than a record of a conversation.

## The sheets cannot be drawn in the game at their own detail

Terraria's art is drawn in two-by-two screen-pixel blocks, so a sprite's detail is half its drawn size in each direction. The sheet's own in-game scale puts the drone at about 24 px, which is 12 art pixels across; the lens on the sheet is about a fifth of the body, which leaves it two or three art pixels. That is a glowing dot and a blink, never an expression, a gaze direction a player can read, or a shield or sword glyph. The same arithmetic makes the rings, crystals, plating and seams on the sheet mostly unreadable in play. So the sheet is a reference for silhouette, palette and feel, and the in-game sprite is a much rougher, lower-resolution drawing of it.

## One sprite, simple on purpose, the same in every mode

- A rough, low-resolution, fully symmetrical disc drawn from the sheet, with one small, gentle looping idle animation made in Aseprite. The same loop plays whatever the companion is doing: no expressions, no gaze, no eye shapes, no per-mode sprite animation. The reason is comfort and legibility — a busy animated sprite at this size reads as noise and tires the eye, and a small gentle loop is enough that it never looks completely still.
- **The loop's rotation is drawn into the frames, not produced by rotating the sprite in code.** Rotating pixel art by an angle that is not a multiple of 90 degrees resamples it, so the pixels crawl and shimmer from frame to frame, which is the eye strain the simple sprite exists to avoid. Rotations by exact multiples of 90 degrees are lossless.
- **A fully symmetrical disc turning in place is invisible**, because every frame is the same picture; the loop needs one feature that breaks the symmetry as it turns (one of the four crystals, or a seam) so it reads as motion at all.
- It is the only sprite. Every other visual is drawn by code.

## The drawn body is sized from the Eye of Cthulhu's own ratio

The owner's rule: a sprite that visibly overlaps terrain or an enemy it is not touching looks wrong, so the drawn body must match the size of the box the game actually collides with, the way the Eye of Cthulhu's does — a hit on the Eye feels like it lands on the circle even though its hitbox is a rectangle.

Measured on 15 September 2026, not recalled:

- **The Eye's hitbox is 100 × 110 px** (`NPC.SetDefaults`, type 4, in the decompiled `Terraria.NPC.cs`).
- **Its sprite frame is 110 × 166 px**, six frames in a 110 × 996 texture (`Content/Images/NPC_4.xnb`, decompressed with FNA's own `LzxDecoder`, and `Main.npcFrameCount[4]` is 6). Scanning opaque pixels row by row, the eyeball is one solid run from roughly row 72 to row 165 of the frame, at most 110 px wide; everything above that is several separate runs, which are the tendrils.
- So the Eye's round body is about as wide as its hitbox plus a tenth (110 against 100) and about as tall as it (roughly 95 to 110 against 110), and only thin decoration — the tendrils — extends outside the box. How the engine aligns the frame over the hitbox when it draws was not checked.

Applied to the orb, whose collision is a circle rather than a box (`CircleContact`, and `NPC.width`/`NPC.height` are set to the same diameter): **the drawn disc should be about the contact diameter, up to about a tenth larger, with only thin details such as crystal tips reaching further out.** A 32 px disc on today's contact would overhang terrain by several pixels on every side, which is exactly what the owner wants to avoid. The owner's instinct was about 32 px; the two ways to get there are a disc near the current contact size with thin details reaching toward 32 px, or a larger contact. A larger contact is not free: the orb's route search needs a corridor wider than the body, and a body near 32 px no longer fits through gaps two tiles (32 px) wide, which today's body flies through. That trade is still open.

## The companion's mode is shown by effects drawn in code, not by the sprite

One principle holds the set together: **only the thruster runs all the time; every other effect is triggered by something happening and is short.** Several constant particle signatures around a sprite a dozen art pixels wide would be the noise and eye strain the simple sprite exists to avoid. Mode is still readable at a glance because almost every mode already produces its own effect while it works, so the effect is the evidence of the work rather than decoration added on top. The owner agreed this table on 15 September 2026.

| When | Effect | Why it reads |
|---|---|---|
| Always, while moving | a few purple wisps in a cone behind the direction of travel, more the faster it moves, almost none while it hovers slowly | a jet; it also shows speed and heading for free |
| Keeping company | the thruster alone | the fallback mode is the plainest look |
| Guarding | three or four dim motes orbiting slowly | the only mode with no work effect of its own, so it gets the one quiet always-on exception |
| Hunting | a small flash on each shot, and the thruster tightening into sharper, brighter streaks | the shots say fighting; the streaks add urgency |
| Dodging | a thin slash streak along the dodge and two fading afterimages of the sprite | fired on the tick a dodge begins, never on every dodging tick, or it strobes |
| Mining or chopping | a beam from the orb to the exact block with a slow pulse along it, and the block's own break dust at the impact | the dust takes the material's colour, so stone reads as stone and wood as wood; it replaces today's borrowed Laser Drill beam |
| Lighting | the flashlight, below, then a small spark burst where the torch goes in | a visible payoff from across the screen |
| Collecting loot | a thin stream of motes from the item to the orb and a pop on pickup | the grab without a drawn tractor beam |
| Getting hit | a short purple burst thrown away from the hit, plus a pale flash for a frame or two | damage without a damaged sprite |
| Downed | the purple removed by tint, a fall, a bounce and a roll, a few grey smoke puffs | below |
| Reviving | the purple spreading back out from the centre with one ring burst | a clear "it's back" moment |

**The flashlight is how lighting looks.** While the companion is heading to light somewhere, it shines a soft cone of real light toward the dark places in the direction it is travelling and at the spot it is going to put a torch, and the torch goes in when it arrives. The beam is brightest at the companion and dims along its length to nothing at its reach, as a real torch beam does, so the Gathering lane's Flashlight reach lengthens the fade rather than moving a hard edge. The owner confirmed on 15 September 2026 that this is exactly how it should work. The light is added through `Lighting.AddLight`, so the player really sees into the dark, and it cannot stop the torch from being placed: `Brain/Infrastructure/Observation/ReadTransientLights.cs` reads the engine's per-frame list of every such light and `ObserveLight.cs` drops those samples from the darkness reading, the same way it ignores the player's held torch.

How the effects are meant to be built:

- Particles use the game's own dust from code rather than a particle renderer of the mod's own, because dust is already drawn at Terraria's pixel scale and matches the world; which of the game's purple dusts are used is picked by eye. Tile break dust comes from the game.
- The dodge trigger is the evade layer's per-tick verdict (the lane that rebuilt evade on 15 September 2026 added it), read for the tick a bend begins.
- The beam, the cone, the afterimages, the tints and the flashes are drawn by code; none of them is a sprite.

**An effects setting in the mod config** — full, subtle, off — was agreed on 15 September 2026, for three reasons the owner gave: players sensitive to motion, performance, and late-game boss fights and events where the screen is already too crowded.

## Downed and reviving

The owner's direction on 15 September 2026:

- **Reviving by standing beside the companion goes.** It revives on its own after a delay he put at 20 seconds, which the mastery tree's Survival lane (it already holds respawn) can bring down to 5 seconds. That fits the project's ruling that an upgrade may change what something costs or how long it takes, never whether the companion can do it.
- **While downed the purple is removed programmatically**, so the orb reads dark, and it falls to the floor and rolls like a ragdoll until it revives.

How it works today, read from `Companion/CharacterBody/CompanionNPC.cs` on 15 September 2026: it revives once a player has stayed within reach of it for a short hold, or on its own after a longer self-revive timer (the constants `ReviveDistance`, `ReviveTicks` and `SelfReviveTicks` in that file); while downed it takes no damage, and the motor sinks it slowly to the floor, where the comment in `ApplyControlsToCompanion.cs` says it exists so the player can reach it to revive it. Removing revive-by-standing removes the only reason the body sinks, so the fall becomes purely a look. The orb's contact with terrain is already a circle against tiles, so a fall with gravity, a bounce and a roll follows from the contact it already has. The roll is the one place a code rotation of the sprite is acceptable, because it is brief and the sprite is dark grey by then.

## Considered and dropped: a rig of drawn parts assembled by code

Before the resolution arithmetic above was set against the sheets, the proposal was a rig: parts drawn in Aseprite (lens glyphs, crystals, ring joints, plating, a beam strip) placed each frame by a small 3D model of the orb. The eye was a point on a sphere that turned to look at whatever the hands worked on; each ring was a tilted circle projected to an ellipse and split front and back for depth; ring tilt followed smooth layered noise so the motion never visibly repeated, with momentum on turns and a wobble on hits; the lens swapped expression per mode (focused when hunting, a shield shape when guarding, scanning while mining, sleepy when idle) through a blink; and the look was to be tuned in a browser playground before being ported. It was dropped on 15 September 2026 because the lens it depends on is two or three art pixels at the sheet's own scale, so the expressions and gaze it existed for cannot be read in play, and because the motion it adds is the kind the owner wants to keep off a small sprite. It is worth reopening only if the companion is ever drawn at a size or draw resolution where the lens has room for a shape.

## Still open

- The drawn size against the contact size: a disc near today's contact with thin details reaching out, or a larger contact at the cost of two-tile gaps.
- How the downed fall handles water and lava (it takes no damage, but rolling into lava may still look alarming, and floating on the surface may read better) and long falls (after reviving it flies home with the recovery flight it already has).
- Whether hostiles still target a downed orb.
- Which of the game's purple dusts carry the thruster, the motes and the bursts.
