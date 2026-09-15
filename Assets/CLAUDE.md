# Assets

Concept art for the companion, kept in the repository and left out of the packaged mod (`build.txt`'s `buildIgnore` names `Assets\*`). Nothing in this folder is loaded by the game. The sheets are the reference the in-game art is drawn from, not the in-game art.

```
Assets/
├─ Companion Drone Concept 01 - Full Specification Sheet.png     the drone's full sheet: views, ring motion, eye states, utility modes, close-ups
├─ Companion Drone Concept 01 - Compact Specification Sheet.png  the same design, fewer panels
└─ CLAUDE.md                                                     this file: how the sheets become in-game art, and what is still being decided
```

## The sheets cannot be drawn in the game at their own detail

Terraria's art is drawn in two-by-two screen-pixel blocks, so a sprite's detail is half its drawn size in each direction. The sheet's own in-game scale puts the drone at about 24 px, which is 12 art pixels across; the lens on the sheet is about a fifth of the body, which leaves it two or three art pixels. That is a glowing dot and a blink, never an expression, a gaze direction a player can read, or a shield or sword glyph. The same arithmetic makes the rings, crystals, plating and seams on the sheet mostly unreadable in play. The orb's contact circle is the body the brain and the motor use and is independent of how large it is drawn, so drawing it larger costs nothing in movement; it does cost screen presence beside a player sprite, which is a judgement made by eye.

## Direction under discussion, 15 September 2026 — not yet decided

This section records a conversation that is still open. When it settles, the decision goes into Slate's Design field (design.structure.surfaces currently says how a sheet becomes in-game drawing has not been worked out) and the play behaviours into README's Expected Behaviour, and this section is rewritten as the settled rule.

The direction the owner is leaning toward:

- **One sprite, simple on purpose.** A rough, low-resolution, fully symmetrical disc inspired by the sheet, with one gentle looping idle animation made in Aseprite. The same loop plays whatever the companion is doing. No expressions, no gaze, no eye shapes, no per-mode sprite animation. The reason is legibility and comfort: a busy animated sprite at this size reads as noise and tires the eye.
- **Everything else is drawn by code, not by sprites.** Particles, the work beam, dodge streaks, hit bursts, tints. The companion's mode is visible through these effects rather than through the sprite.
- **Downed is a tint and a fall.** While downed the purple is removed programmatically so the orb reads dark, and it falls to the floor and rolls like a ragdoll until it revives.
- **Reviving by standing beside it goes.** The companion revives on its own after a delay the owner put at 20 seconds, which the mastery tree's Survival lane (already holding respawn) can bring down to 5 seconds. Today `CompanionNPC.UpdateDowned` revives either after a player stands within reach of it for a while or after a longer self-revive timer; the stand-beside path is the one removed.

Points raised in the discussion that bear on how this is built, not yet agreed:

- A looping rotation should be drawn into the Aseprite frames rather than produced by rotating the sprite in code. Rotating pixel art by an angle that is not a multiple of 90 degrees resamples it, and the pixels crawl and shimmer from frame to frame, which is exactly the eye strain the simple sprite is meant to avoid. Rotations by 90 degrees are lossless.
- A fully symmetrical disc turning in place is invisible unless something on it breaks the symmetry as it turns (the four crystals, a seam). The loop needs one such feature to read as motion.

## Considered and dropped: a rig of drawn parts assembled by code

Before the resolution arithmetic above was set against the sheets, the proposal was a rig: parts drawn in Aseprite (lens glyphs, crystals, ring joints, plating, beam strip) placed each frame by a small 3D model of the orb. The eye was a point on a sphere that turned to look at whatever the hands worked on; each ring was a tilted circle projected to an ellipse and split front and back for depth; ring tilt followed smooth layered noise so the motion never visibly repeated, with momentum on turns and a wobble on hits; the lens swapped expression per mode (focused when hunting, a shield shape when guarding, scanning while mining, sleepy when idle) through a blink; and the look was to be tuned in a browser playground before being ported. It was dropped on 15 September 2026 because the lens it depends on is two or three art pixels at the sheet's own scale and still only four or five at 40 px, so the expressions and gaze it existed for cannot be read in play, and because the motion it adds is the kind the owner wants to keep off a small sprite. It is worth reopening only if the companion is ever drawn at a size or a draw resolution where the lens has room for a shape.

One fact found while designing it still holds for any light effect: a light the companion emits through `Lighting.AddLight` (a flashlight cone, a glow) does not make the torch decision think a dark area is lit, because `Brain/Infrastructure/Observation/ReadTransientLights.cs` reads the engine's per-frame list of every such light and `ObserveLight.cs` drops those samples from the darkness reading, the same way it ignores the player's held torch.

## Open questions in the discussion

- Which effect carries each mode, how sparse the always-on layer is, and which of the game's own dusts are used, as against particles drawn by the mod.
- Whether the flashlight from the earlier discussion (a cone of light aimed at the dark area the companion is going to light, before the torch goes in) survives as a code effect.
- How the downed roll handles liquid and long falls, and whether hostiles still target a downed orb.
- The drawn size of the sprite beside a player.
