# Interface Experiments — archived visual references for the native companion menus

```
InterfaceExperiments/
├─ CLAUDE.md              this guide
└─ companion-card.html    the agreed next revision of the native profile card, at the pixel sizes the C# will be built to, with its four pages and controls live, the mastery wheel included; its header lists every departure from the 2026-09-13 render
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

**The pair it carries is a loadout, not an inventory, and it lives under the action line.** The companion carries two weapons at a time out of the twenty, so the overview shows the pair as two chips under the action line, each the weapon's name in its rarity colour, and clicking a chip opens the Weapons page where the pair is chosen. Chips rather than item slots because slots do not fit under the action line without growing the overview the owner asked to keep short, and a chip carries the name an icon never teaches. On the page **a row is a tier and a column is a class**, which is the design matrix from `../WeaponExperiments/` rendered directly; laid out as one continuous run of slots the tier boundaries fell mid-row and the progression stopped being visible. Each cell is the slot with the name under it, so a long name has the cell's full width and stays on one line, and what the weapon does is the hover hint rather than a second line in the cell. A locked weapon is dimmed and never hidden, because the wheel already shows every weapon and filtering here dims rather than removes, the way it does everywhere in this product. A tier with no weapon in a class leaves that cell empty on purpose. A class filter was built and removed: with classes as columns it duplicated an axis already on screen. The Weapons page exists so the twenty weapons can be tested by equipping them from the card rather than one build at a time, which is why the card precedes the weapons on the roadmap.

Picking a third weapon replaces the one picked longest ago rather than refusing the click, because a refusal makes the player first decide which of the two to drop, which is two actions to do one thing.

**The three bottom regions are summary tiles, not buttons, and each takes over the whole card.** A button that says "Inventory" spends a sixth of the card to say a word the player already knows; the tile spends the same space carrying fill state and recent pickups, and opening is what happens when it is clicked. The overview itself is short — title bar, identity strip with the three surviving controls on its right, three tiles — and Inventory, Mastery and the Mining list each replace it with a full-height page whose title bar holds only back and close, so a page never shares the card with the strip it does not need. Hunting and pot breaking are not toggles in the first version and following distance never changes, so the overview draws none of them.

**The ore list hides what has never been picked up, and ores are the only thing with a list.** An ore appears once the player has held one in their inventory at least once, recorded as a saved set on the character so that selling the last of it does not remove it, and never before, so the list does not spoil what a world or a mod holds. A "mine N first" threshold was the earlier design and is dropped because it can be gamed. One mode says what a mark on a tile means — skip the marked ores, or mine only the marked ores — and whatever the mode, an ore the companion will mine is drawn bright and one it will leave is drawn dim, so the picture answers "what will it dig" without reading the mode. The preview under the grid shows the hovered ore *as it appears in the wall*, because that is the form the player has to recognise while digging and the item icon never teaches it. No other behaviour has a list: the rest are Off/Mimic/Auto or Off/On.

**The inventory page is the grid and Terraria's own chest buttons, nothing else.** Loot All, Deposit All, Quick Stack and Restock are the game's names and the game's semantics, so a player already knows them and the native build can reuse the chest routines; the bag sorts itself after every transfer, so there is no Sort button, no filters and no detail panel. An earlier version had type filters that dimmed what they excluded and a selected-slot panel on the right, and both went because they spent the space the grid should have.

**The card's mastery wheel has eight lanes with settled meanings, and it is symmetric on purpose.** Four lanes are the weapon classes — Melee, Ranged, Magic, Throwing — and five diamonds on each is the twenty-weapon kit, with a weapon opening into its own damage, rate, tier and special lines. Movement holds speed, jumps, dash and flight; Survival holds life, armour, regen, resistances and respawn; Gathering holds all tool use (mining and chopping speed and reach, the torch hat and its light, the bag and pickup); Guarding holds the companion's protection duties (reach, threat awareness, revival, cleansing effects on the player). Tools and Support, which the native node table still carries, are gone: Tools duplicated Gathering and Support's ideas live under Guarding. Every spoke is one template rotated by a multiple of 45 degrees, the diamonds alternate sides rank by rank with a circle-circle rank between each, and the page computes both the nearest pair of nodes and the largest deviation of any spoke rotated onto the first, so a layout change that breaks either is caught by a number rather than by eye. An earlier "irregular branching" version varied each spoke's stretch and twist and was rejected because the spokes then read as unequal.

## Making it read as Terraria

A standalone mastery map (`mastery-map.html`, with Fieldwork, Scavenging and Thrown among its lane names and a point-and-stage purchase preview) was deleted on 2026-09-14 because the card's own mastery page is the design now and two maps invited confusion; its lane ideas survive as the card wheel's eight lanes above. Circles carry multiple ranks; diamonds unlock abilities or weapons, and weapon diamonds open nested upgrades. Nothing in the mockup persists to game saves or changes stats, equipment, inventory or XP.

**The mastery tree never gates a behaviour.** Stats, weapon unlocks and movement abilities are the only learnings. A companion behaviour is not gated by whether the player has reached a node; the behaviour exists or it does not, and mastery progression only adds new capabilities, not gates to existing ones.

The intended progression separates three owners: companion levels award points, graph connections govern which node can be opened, and material/progression requirements govern purchases and upgrades. The balancing target is roughly half the full tree opened by Moon Lord, not a claim supported by this preview's point supply. Only actions the companion completes earn experience — its kills, ore, torches and wood — and the player's own actions earn it nothing, by the owner's ruling of 2026-09-14, which superseded an earlier note giving player actions a third; the ledger is meant to consume the attempt-outcome record the brain already emits with attribution, so a new activity earns experience without new hooks. Repetition limits, recipes and values remain design work. Shared tool upgrades cover mining, chopping and placement together. Unlocked movement must update both planner capability and real execution. Interface grouping may collect the profile, bag panel and mastery view, while bag storage and item transfer remain gameplay services.

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
