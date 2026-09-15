# Heads-up display — the companion's notch of three bars

```
HeadsUpDisplay/
├─ CLAUDE.md                 this guide
└─ CompanionHealthBar.cs     the notch: three stacked bars, docking, drag, right-click dock, and the click that opens the card
```

The notch is the one thing about the companion a player sees without opening anything, and since 15 September 2026 it carries three stacked bars and nothing else: health above mana above experience, in a body sized to hold exactly those bars with the same padding on every side. There is no name, no level, no numeric reading and no icon. That is the owner's ruling, and its reason is the rule to apply to anything proposed for this surface: information shown twice, or not needed at a glance, is clutter. The numbers are on the card, one click away, and what the companion is doing is on the card too. A player glancing at the HUD needs to know whether the companion is hurt, whether its pool is low and roughly how close it is to a level, and the three fills answer that.

The order and the colours are the card's identity strip's, so a player learns one column: health, mana in the game's own mana-star blue, experience in the card's gold. Health is the one bar that still changes colour, fading from green towards red as life drops, and while the companion is downed it turns grey and fills with revival progress instead of life, so the bar says both that it is down and how close it is to getting up. Nothing animates; a bar snaps.

## How it is built

`CompanionHealthBar` is a `ModSystem` that adds one interface layer after the game's own resource bars, drawn in raw screen pixels because Terraria's mouse coordinates are raw screen pixels, with every size multiplied by the UI scale by hand. `Bounds` is the notch's box: top-centre and flush with the screen edge while docked, the saved position clamped onto the screen while free. `Bars` derives the three tracks from that box rather than storing them, padding equal on all four sides and the two gaps equal, so the spacing stays even at any UI scale; the drawing and the headless fixture both read it, so the fixture's geometry is the drawing's own arithmetic.

Docked, the body has square top corners and rounded bottom ones, with two concave fillets outside the top corners so it reads as part of the screen edge, and a hairline border that follows the silhouette but not the top, because the edge it hangs from is the screen. Free, it is a rounded card with a border all round and no fillets. The rounded shapes are runtime-built alpha masks from `../ProfileCard/DrawCardPrimitives.cs`, shared with the card so there is one texture cache, and that cache is keyed by corner radius and fillet size, never by a fill's width: a bar's fill is drawn as flipped corner quadrants around solid bands, so the notch holds the same handful of textures whatever its three fractions read. The card system releases the cache at unload, and that release goes through `Main.QueueMainThreadAction` because FNA3D refuses to dispose a texture on the worker thread mod unload runs on.

A press is a click until the pointer travels past a small threshold: released before that, it opens or closes `../ProfileCard/`; past it, the notch follows the pointer, freed from the edge, and the position is saved per character through `../PlayerIntegration/CompanionPlayer`. Right-click docks it again. `CaptureInput` runs from the player's pre-update so a cursor entering and pressing on the notch in one tick is consumed before item use; setting `mouseInterface` only while drawing would miss that opening press.

## What the notch deliberately no longer does

Two icons used to flank the bars, the activity family on the left and the activity on the right, with tooltips naming them, dimmed while shared safety or recovery paused the work, and a "Stuck" label under the notch while movement made no progress. They went with the ruling above, along with the name, the readings and the hover text that explained mana and experience. Their information is not lost from the game: `../ProfileCard/DrawCompanionStatus.Describe` still composes the activity, its target, its reason, a stall and a downing from the brain's evidence, and the inspector draws the rest; the card simply stops showing it as a sentence, by a later ruling of the same day. Proposing any of them back onto the HUD is re-proposing something the owner removed.

## Verification

`../../Tools/EngineReplay/Lifecycle/VerifyCompanionHud.cs` draws three notches on one sheet at every render viewport (docked and healthy, free and healthy, free and downed) through the real layer method, and requires the base size at the UI scale, even padding and equal gaps from `Bars`, each bar's fill read back off the pixels against a pinned fraction, the mana blue and experience gold, every pixel inside the body and outside the bars to be body colour and the band under each notch to be untouched background, and no coloured health fill while downed. It then drives the input: a click opens the real card, a drag past the screen corner frees the notch on screen without opening the card, and right-click docks it.
