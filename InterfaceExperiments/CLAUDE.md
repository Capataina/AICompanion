# Interface Experiments — archived visual references for the native companion menus

```
InterfaceExperiments/
├─ CLAUDE.md              this guide
├─ companion-card.html    the original companion profile card and its sub-views, self-contained
└─ mastery-map.html       selectable eight-branch mastery design with ranks and nested weapon upgrades
```

This folder holds no game code. Nothing here is compiled or loaded by the mod, and `build.txt` excludes it from packaging. These early HTML experiments preserve layout and progression ideas as visual references. New interface work belongs directly in the native Terraria menus, by the user's instruction; extending a web preview does not deliver an in-game feature. The native render harness checks the real panels offscreen without repeatedly launching the game.

## What the card is and what it replaces

`../Companion/HeadsUpDisplay/` draws a draggable health notch which opens `../Companion/ProfileCard/`. Its Inventory page embeds `../Companion/Inventory/`'s native bag slots. The notch therefore reaches behaviour preferences as well as storage; the bag's gameplay data remains independent of whichever interface presents it.

The native card is one panel whose body swaps between the profile overview, Inventory and Mastery; the two bottom status tiles are the way in, and there is no tab row. The overview presents live preferences; Inventory uses the game's item slots; Mastery offers a selectable eight-branch preview without gameplay effects, material payments or saved progression. Whitelists and loadout selection remain mock-only. The HTML layouts supply composition references, while the game owns its typography, panel rendering and interaction conventions.

## Mimic policies and voluntary activity toggles are different controls

Mimic means the player's corresponding action triggers work. Mining and chopping therefore have Off/Mimic/Auto choices. Voluntary hunting, pot breaking and supplied torch placement instead have Off/On choices. Disabling hunting does not disable guarding or self-preservation. Carrying a torch remains independent of placing one. Fishing is not implemented.

The native preferences in `../Companion/PlayerIntegration/ConfigureCompanionPreferences.cs` own these settings. The original HTML card predates the voluntary toggles and uses longer policy labels; its control inventory is not the current native capability list. A scored behaviour can be disabled by an eligibility policy without replacing utility scoring.

The native card explicitly identifies guarding and survival as automatic. The inspector and telemetry switches belong to tModLoader Mod Configuration rather than this card.

## The other decisions the mockup encodes

The original mock uses named distance stops. Native controls use Close/Standard/Free profiles, which scale following comfort, activity acquisition and recovery distance together. There is no remote mission mode.

**The current action line carries its reason.** The verb alone ("Mining") is a status; the reason ("copper vein 12 tiles below — opportunistic") is what lets a player tell a working companion from a stuck one, which is the single question this surface exists to answer. It names the policy that produced the choice, so the panel above and the line below read as one system.

**The weapon row is a loadout, not an inventory.** The companion carries two weapons at a time out of twenty-two, so the overview shows two named slots and the drill-down is where the pair is chosen. In that grid **a row is a tier and a column is a class**, which is the design matrix from `../WeaponExperiments/` rendered directly; laid out as one continuous run of slots the tier boundaries fell mid-row and the progression stopped being visible. A tier with no weapon in a class leaves that cell empty on purpose. A class filter was built and removed: with classes as columns it duplicated an axis already on screen.

Picking a third weapon replaces the one picked longest ago rather than refusing the click, because a refusal makes the player first decide which of the two to drop, which is two actions to do one thing.

**The two bottom regions are summary tiles, not buttons.** A button that says "Inventory" spends a sixth of the card to say a word the player already knows; the tile spends the same space carrying fill state and recent pickups, and opening is what happens when it is clicked.

**The whitelist grid hides what has never been mined.** An ore appears once it has been mined at least once and is choosable once a threshold is reached, with the partial count on the tile. The panel beside it previews the ore *as it appears in the wall*, because that is the form the player has to recognise while digging and the item icon never teaches it.

## Making it read as Terraria

The standalone mastery preview expands the original card's small tree sketch. It has eight connected radial branches: Fieldwork, Ranged, Movement, Magic, Survival, Scavenging, Thrown and Melee. These names and placements are a proposal. Circles carry multiple ranks; diamonds unlock abilities or weapons, and weapon diamonds open nested upgrades. First-rank purchases spend one preview point and make onward connections available; later ranks spend none. Stage selectors illustrate progression gates. Reset discards all preview selections, and nothing persists to game saves or changes stats, equipment, inventory or XP.

The intended progression separates three owners: companion levels award points, graph connections govern which node can be opened, and material/progression requirements govern purchases and upgrades. The balancing target is roughly half the full tree opened by Moon Lord, not a claim supported by this preview's point supply. Companion actions are intended to earn full XP and analogous player actions roughly one-third; sources, attribution, repetition limits, recipes and values remain design work. Shared tool upgrades cover mining, chopping and placement together. Unlocked movement must update both planner capability and real execution. Interface grouping may collect the profile, bag panel and mastery view, while bag storage and item transfer remain gameplay services.

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
