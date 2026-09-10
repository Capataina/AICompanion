# Interface Experiments — in-game UI mocked in HTML before it is drawn in Terraria

```
InterfaceExperiments/
├─ CLAUDE.md              this guide
└─ companion-card.html    the companion profile card and its sub-views, self-contained
```

This folder holds no game code. Nothing here is compiled or loaded by the mod, and `build.txt` excludes it from packaging. It exists because Terraria's UI is drawn in `Terraria.UI` code with hand-placed pixel offsets and no live reload, so trying a layout in the game costs a build, a launch and a world load, while trying it here costs an edit and a refresh. The rule this folder embodies: **the look is agreed in HTML, then built once in the real surface.**

## What the card is and what it replaces

`../Companion/HeadsUpDisplay/` draws a draggable health notch, and clicking it currently opens `../Companion/Inventory/`'s bag panel directly. The card is the notch's new target: the bag becomes one view inside it rather than the whole of what a click reaches. That reframing is the point — the notch is the only always-visible companion surface, so whatever it opens is the entire surface area the player has for controlling the companion, and a bag alone spends it on cargo.

The card holds four views behind one back arrow: the overview, the cargo bag, a per-activity detail, and the mastery tree. Sub-views are drill-downs inside the same card rather than second floating panels, because a second panel needs its own position, its own drag state and its own close affordance, and the player then has two things to dismiss.

## The decisions the mockup encodes, and why

**Three behaviour modes are three sources of instruction, not a scale of intensity.** Never means nobody tells the companion to do this; Mimic means the player's own action is the instruction; Free means the companion's own judgement is. That is why they render as a segmented control with equal-weight segments and not as a slider — a slider would claim Mimic is halfway between the other two, and it is a different kind of thing.

**Follow distance is a value, so it does not wear a mode's clothes.** It sits in its own row with named stops rather than a third three-state segment, because a player scanning the panel reads matching controls as matching kinds of setting.

**The current action line lives at the foot of the portrait**, with the reason under the verb. The verb alone ("Mining") is a status; the reason ("copper vein 12 tiles below — opportunistic") is what lets a player tell a working companion from a stuck one, which is the single question this surface exists to answer. It names the mode that produced the choice, so the behaviour panel above it and the action below it are visibly the same system.

**The two bottom regions are summary tiles, not buttons.** A button that says "Inventory" spends a sixth of the card to say a word the player already knows; the tile spends the same space carrying fill state and recent pickups, and opening is what happens when it is clicked. Same for mastery, which shows branch progress rather than the word "mastery".

**Presets exist because a ten-row toggle panel reads as a wall.** Stay put / Follow my lead / Free rein set every row at once, so the granular controls become the thing a player edits rather than the thing they must first assemble.

**The whitelist/blacklist grid hides what has never been mined.** An ore appears once it has been mined at least once and is choosable once a threshold is reached, with the partial count on the tile and the remainder in the tooltip. The tooltip also previews the ore *as it appears in the wall*, because that is the form the player has to recognise while digging and the item icon never teaches it.

## Traps

- **No CSS transitions or keyframes anywhere, deliberately.** Terraria's interface does not animate, so an animated mockup would agree to a look the real surface cannot deliver, and every hover and selection state here changes instantly on purpose.
- **The palette is Terraria's own blue-violet panel language, not the neutral ground of `../WeaponExperiments/`.** That page is a design document to be read; this one is a picture of something that will sit over a game world, and judging it against a neutral page palette judges the wrong thing.
- **Mock state is mock, and the demo strip under the card is not part of the game UI.** Anything added here that reads as a real value — a bag count, a level, a branch percentage — is invented for the picture and must not be quoted back as a fact about the mod.
