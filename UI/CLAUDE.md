# UI — the one thing on screen that says how the companion is doing

The companion is an NPC in the world rather than a portrait in a party frame, so without a HUD element the player has two questions he cannot answer while playing: how hurt is it, and what does it think it is doing. This folder answers both in the smallest space that can carry them, and nothing else.

That scope is deliberate. The debug overlay in `../Brain/BehaviourDiagnostics/` already shows every score, the chosen spot and the path, and it is a development instrument — dense, keyboard-toggled, and useless to a player. What a player needs is glanceable: a health reading he does not have to look for, and enough of the companion's intent that "why is it wandering off" has a visible answer. Everything past that belongs in the bag panel or the overlay, which is why this folder holds one file.

```
UI/
├─ CLAUDE.md
└─ CompanionHealthBar.cs   the notch: the health reading, the mode icon beside it, and the
                           drag-and-dock behaviour that also makes it the bag's button
```

## Why it is a notch, and what that buys

It reads as part of the screen edge rather than a window laid on top: docked at top-centre it hangs from the edge with square top corners and rounded bottom ones, with concave fillets outside the top corners so the silhouette meets the edge instead of floating near it. Dragged anywhere else it becomes a free rounded card and the fillets go, because a shape that implies attachment while attached to nothing looks broken. Right-click docks it again.

The reason to care is that Terraria's HUD has no rounded primitives at all, so every one of those shapes is an alpha mask this file builds on the graphics device and caches by pixel size. That is the folder's whole technical weight, and it is why a file this small has a trap list.

The mode icon to the left is the part that does real work for a player. It is one of the game's own item sprites for whatever the companion is currently doing — the tool for a job, the weapon for a fight, a shield for guarding, boots for kiting, a coin for looting, a compass for following, a sunflower for wandering, a breathing reed while it is saving itself, a feather while a reflex has the body, a torch while one is up, a tombstone while it is down. Hovering names the action. Reusing the game's sprites rather than drawing icons is the same instinct as the rest of the mod: the vocabulary is already in the player's head.

## How it sits in the rest of the mod

Three edges, and the second is the one that is invisible from inside this folder.

```
Brain (the chooser's current action) ──read──▶ the mode icon
the notch, clicked                   ──open──▶ ../Inventory/'s bag panel
Players/CompanionPlayer              ──holds─▶ where the notch was dragged to
```

The mode icon is a **read-only window onto the brain's decision**, which means this file is a consumer of the action set and has to know every action's name. Adding a behaviour to `../Brain/Behaviours/` without giving it an icon here leaves a player with a blank space where the answer should be — so the icon table is part of an action's cost, not part of this folder's.

The click behaviour is the piece worth understanding before editing anything: a press that is released before it has travelled a few pixels is a *click* and toggles the bag, and a press held past that distance becomes a *drag*. One control is therefore both the bag's button and the notch's handle, which is why there is no separate button anywhere. The position itself lives on `../Players/CompanionPlayer.cs`, so it persists per character rather than per world.

## What it is not

- **Not a companion panel.** There is no stats page, no equipment view and no order buttons here; the bag has its own window in `../Inventory/` and the brain has its overlay.
- **Not scaled by the game's UI system.** It draws in raw screen pixels and multiplies its own sizes by the UI scale by hand, because the mouse coordinates it has to hit-test against are screen pixels.
- **Not a health bar over the companion's head.** The reading is on the HUD on purpose, so it is legible when the companion is off screen — which, given it never teleports and can be several screens away, is often.

## Where it is going

The mastery tree has no entry point on the HUD, because the tree is unbuilt; when it exists, this is the surface that has to reach it, and the notch is the obvious anchor since it is already the one thing on screen that belongs to the companion. Orders are the same shape of problem: a companion that takes instructions needs somewhere to receive them, and nothing here is built for that yet.

Neither is designed. What is settled is that this folder stays the *glanceable* surface — anything that needs reading rather than glancing gets a window, not more notch.

## Traps

- **There are no rounded primitives on the HUD.** The shapes are alpha masks built once per pixel size (`RoundedMask`, `FilletMask`) on the graphics device and cached. Building or disposing a texture must happen on the main thread: the draw layer is, `Unload` is not, so `Unload` queues the disposals through `Main.QueueMainThreadAction`; disposing them inline threw `ThreadStateException` and made the mod unable to unload (2026-09-08).
- **Text is `DrawBorderStringFourWay` in the body colour**, so the label has no dark halo on the dark notch; `DrawBorderString` would.
- **The docked border deliberately has no line along the top**, because the edge it hangs from is the screen; without the border on the other three sides the dark body vanished against a night sky.
