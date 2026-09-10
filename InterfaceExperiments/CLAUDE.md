# Interface Experiments — in-game UI mocked in HTML before it is drawn in Terraria

```
InterfaceExperiments/
├─ CLAUDE.md              this guide
└─ companion-card.html    the companion profile card and its sub-views, self-contained
```

This folder holds no game code. Nothing here is compiled or loaded by the mod, and `build.txt` excludes it from packaging. It exists because Terraria's UI is drawn in `Terraria.UI` code with hand-placed pixel offsets and no live reload, so trying a layout in the game costs a build, a launch and a world load, while trying it here costs an edit and a refresh. The rule this folder embodies: **the look is agreed in HTML, then built once in the real surface.**

## What the card is and what it replaces

`../Companion/HeadsUpDisplay/` draws a draggable health notch, and clicking it currently opens `../Companion/Inventory/`'s bag panel directly. The card is the notch's new target: the bag becomes one view inside it rather than the whole of what a click reaches. That reframing is the point — the notch is the only always-visible companion surface, so whatever it opens is the entire surface area the player has for controlling the companion, and a bag alone spends it on cargo.

The card holds five views behind one back arrow: the overview, the cargo bag, a per-activity whitelist, the weapon loadout, and the mastery tree. Sub-views are drill-downs inside the same card rather than second floating panels, because a second panel needs its own position, its own drag state and its own close affordance, and the player then has two things to dismiss.

## Only work is configurable, and that is a structural fact rather than a design choice

**A three-state policy can only exist for a behaviour the player themselves performs.** Mimic means "your own action is the trigger", so it is meaningful for chopping, mining and fishing and meaningless for anything else. Fighting, kiting, carrying a torch, opening a door and picking up drops have no player action to mirror — they are behaviours the utility scorer weighs against each other every tick, and offering them as settings would promise control the architecture does not have.

The code agrees and is the authority: `WorkPolicy` in `../Companion/Brain/Behaviours/Work/WorkPolicies.cs` is exactly `Disabled / Mimic / Opportunistic`, and only Mining and Chopping hold one. The panel uses those three words verbatim so the label and the field cannot drift apart. An earlier draft of this page carried Combat and Support groups with the same three-state control; they were cut for exactly this reason, and re-adding a toggle for a scored behaviour is the mistake to avoid rather than an omission to fix.

The panel says this out loud in a footnote, because a player hunting for a "should it fight" toggle needs to be told it does not exist, and told where to look instead — the action line under the portrait, which shows what the scorer actually chose.

## The other decisions the mockup encodes

**Follow distance is a value, so it does not wear a mode's clothes.** It sits in its own row with named stops — Heel, Close, Loose — rather than a matching three-segment control, because a player scanning the panel reads matching controls as matching kinds of setting. There is no Roam stop: roaming was removed from the design.

**The current action line carries its reason.** The verb alone ("Mining") is a status; the reason ("copper vein 12 tiles below — opportunistic") is what lets a player tell a working companion from a stuck one, which is the single question this surface exists to answer. It names the policy that produced the choice, so the panel above and the line below read as one system.

**The weapon row is a loadout, not an inventory.** The companion carries two weapons at a time out of twenty-two, so the overview shows two named slots and the drill-down is where the pair is chosen. In that grid **a row is a tier and a column is a class**, which is the design matrix from `../WeaponExperiments/` rendered directly; laid out as one continuous run of slots the tier boundaries fell mid-row and the progression stopped being visible. A tier with no weapon in a class leaves that cell empty on purpose. A class filter was built and removed: with classes as columns it duplicated an axis already on screen.

Picking a third weapon replaces the one picked longest ago rather than refusing the click, because a refusal makes the player first decide which of the two to drop, which is two actions to do one thing.

**The two bottom regions are summary tiles, not buttons.** A button that says "Inventory" spends a sixth of the card to say a word the player already knows; the tile spends the same space carrying fill state and recent pickups, and opening is what happens when it is clicked.

**The whitelist grid hides what has never been mined.** An ore appears once it has been mined at least once and is choosable once a threshold is reached, with the partial count on the tile. The panel beside it previews the ore *as it appears in the wall*, because that is the form the player has to recognise while digging and the item icon never teaches it.

## Making it read as Terraria

Four things carry nearly all of the resemblance, and a mockup that misses them reads as "a game menu" rather than as Terraria:

1. **Every string has a hard black drop shadow**, offset down-right with no blur. It is set once on `body` so everything inherits it, which is also how the game does it, and it is the single most recognisable trait.
2. **Panels are a saturated blue-violet at about 86% over the world**, so the world shows through and the panel never reads as a page.
3. **Item slots are a lighter blue with a bright thin border**, square, at a fixed 48px.
4. **Names are coloured by rarity rather than by role**, on Terraria's own ladder — white, blue, lime, yellow, cyan, red — so the weapon grid reads as progression to anyone who has played it.

The page draws a tiled world behind the card for the third of these to mean anything: without something to be translucent against, an 86% panel is just a solid colour. The backdrop is generated from a fixed seed so two screenshots are comparable.

## Traps

- **No CSS transitions or keyframes anywhere, deliberately.** Terraria's interface does not animate, so an animated mockup would agree to a look the real surface cannot deliver, and every hover and selection state here changes instantly on purpose.
- **A slot sized as a fraction of its panel is wrong at every panel width.** Ten columns of `1fr` across the card produced 82px slots, roughly four times Terraria's own, and stopped reading as an inventory. Slots are a fixed pixel size and the column count follows from the width.
- **A selector scoped to a container will silently miss the copy of that element outside it.** `.row .lbl em` did not match the distance stepper's hint, which rendered at full size and full brightness — and a too-loud label still looks like a label in a screenshot, so nothing about it reads as broken.
- **Chrome floated over a pannable surface will land on its content.** The mastery legend sat on a node label as soon as the graph used its corners; it is a footer bar outside the surface now.
- **Mock state is mock.** Any value here that reads as real — a bag count, a level, a branch percentage, an ore threshold — is invented for the picture and must not be quoted back as a fact about the mod. The one exception is the `WorkPolicy` names, which are copied from the enum on purpose.
