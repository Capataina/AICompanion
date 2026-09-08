# AICompanion — "Multi... Player?"

A tModLoader mod that adds an AI companion to Terraria: an NPC that follows you, fights beside you, does what you are doing, will take orders, and grows through its own mastery tree. The internal mod name is `AICompanion`; the display name on the mod browser is `Multi... Player?`. This is the first step of a long-standing aspiration recorded in the LifeOS vault at `Profile/Personal/AI-Populated Games.md`, and its plan lives in the Slate project `ai-companion` (prefix AIC).

**This mod is singleplayer only, by Caner's ruling on 2026-09-07, and every line of code assumes it.** The player is always `Main.LocalPlayer`; there is no netcode, no `netUpdate`, no server/client branching, no iteration over `Main.player`. A change that adds any of those is wrong even if it works, because it spends effort on a case the mod refuses to support and makes every later feature carry the same cost.

The companion is an NPC, deliberately, not a second `Player` slot. Abilities are the mod's own closed set: the companion never runs a real item through the game's item-use code, because that routing is the class of bug (bows that will not fire, potions that cannot be used) that keeps the existing companion mod, TerraGuardians, feeling like an NPC that does some stuff. Reading an item's *numbers* is fine and expected: the chopper takes axe power and use time from the player's held axe, the weapons take speed, damage and cooldown from `ContentSamples`, and nothing goes near `Player.ItemCheck`. The one requirement the NPC shape makes harder is keeping a boss fight alive after the human dies; AIC-8 on the board carries the two routes and the check.

**The companion never teleports, by ruling on 2026-09-08.** Knocked off a boss platform it climbs back; sent away it walks back, watchable on the map, where it is drawn as its own head. A companion trapped behind a sand fall stays trapped until the player digs it out; rescue behaviours are later work (AIC-65). It reveals the map only with what its torch actually lights, never its whole screen, because that would show what is behind walls.

**The companion decides by scoring, not by a priority chain.** Every tick the brain reads the world into senses, lets a reflex take the body if something is about to hit, scores every possible action from the same facts and runs the best, asks where to stand, and walks there over a real path. The design is in `Brain/CLAUDE.md`; the Slate architecture field carries the durable version. The behaviour is as smart as it can be from the start; the mastery tree upgrades stats and weapons, and by the 2026-09-08 revision may also gate *sending* behaviours (go gather, go farm) while doing-with-you behaviours (mine beside the player) come early or free. Its shape is undecided: a mix of levels and resource costs paid from the player's inventory at the tree, not skill points alone.

**Before implementing any mechanic, read the decompiled game for the path that already does it, and reuse it unless it is gated on the local player.** Caner's standing instruction on 2026-09-07. Chopping reuses `HitTile` plus the vanilla axe formula and `Main.DrawTileCracks`; the body reuses the player renderer; arrows are vanilla projectiles owned by the player so the player's on-hit accessories and ranged stats apply; the bag uses the game's own `ItemSlot`. Decompile with `ilspycmd -t Terraria.<Type> "<Steam>/tModLoader/tModLoader.dll"` (installed under `~/.dotnet/tools`) and grep the result; the reflection scratch tool at `/tmp/tmlreflect` lists member signatures.

## The map

Every folder has its own `CLAUDE.md`; this is the whole tree, folders only, with one line each.

```
AICompanion/
├─ CLAUDE.md                 this file
├─ README.md                 outward-facing summary
├─ build.txt                 tModLoader manifest: display name, author, version, buildIgnore
├─ description.txt           mod browser description
├─ AICompanion.csproj        imports ../tModLoader.targets → tMLMod.targets from the Steam install
├─ AICompanion.cs            the Mod subclass; logs on load, nothing else
├─ Companion/                the NPC, its drawn body, its motor
├─ Brain/                    how the companion behaves: deciding, and doing what it decided
│  ├─ DecisionMatrix/        how a choice is made, independent of what the choices are
│  │  ├─ Senses/             the world model and the derived danger, horizon and light
│  │  ├─ Decision/           the utility chooser, considerations, weights, position requests
│  │  ├─ Positioning/        where to stand, scored over candidate tiles
│  │  ├─ Navigation/         grid, A*, path following, reachability
│  │  └─ Reflexes/           simulated dodges, the path that skips scoring
│  ├─ Actions/               what can be chosen, one file each, by family
│  │  ├─ Companionship/      walk-with, guard, wander
│  │  ├─ Combat/             hunt, kite
│  │  ├─ Gathering/          loot
│  │  └─ Work/               chop, mine
│  ├─ Work/                  the tools a chosen action drives
│  │  ├─ Chopping/           trees and the axe
│  │  ├─ Mining/             ores and the pickaxe
│  │  └─ Torch/              the torch in the dark
│  ├─ Aiming/                the arc solver every ranged weapon and the positioner share
│  └─ Debug/                 the brain overlay (F6) and the per-tick telemetry writer
├─ Combat/
│  └─ Weapons/               the two equipped weapons and the arsenal that picks between them
├─ Map/                      the companion on the world map, and what its torch reveals there
├─ Inventory/                the bag, its panel, and the right-click that opens it
├─ UI/                       the HUD health notch
├─ Players/                  the ModPlayer: persistence and input
├─ Commands/                 /companion
├─ Localization/             en-US strings (display name, keybind)
└─ Telemetry/                written by the mod at run time, one .tsv per world session; ignored by git and the packager, read by an agent after a playtest
```

## Operating manual

Build from the shell (what a session does to verify a change compiles):

```
cd "~/Library/Application Support/Terraria/tModLoader/ModSources/AICompanion"
dotnet build -nologo -v q
```

Zero `error CS` lines and a fresh `bin/Debug/net8.0/AICompanion.dll` is the pass. With the game closed the same command also packages the `.tmod`; while the game is open it fails at that step with `TML003: Please close tModLoader or disable the mod in-game`, which is the packaging step, not the compile.

Build for the game (what Caner does to play it): with the game closed, the shell build above packages `Mods/AICompanion.tmod`; launch tModLoader and the mod is loaded. In a world, `/companion` once; from then on it spawns with you on every world enter. F6 (rebindable under Controls → Mod Controls; on a MacBook the F-keys need Fn unless set to standard) toggles the brain overlay. Right-click the companion within reach, or click its health notch, to open its bag.

## Traps

- **The shell build says "Build succeeded" in under two seconds.** That is real; check the DLL timestamp before trusting it.
- **In-game Build + Reload kills the game, silently, on this build.** Twice on 2026-09-08 `client.log` ended at `Unloading: ModLoader` with no exception and no macOS crash report; a fresh launch loaded the same `.tmod` cleanly. Decompiled `ModLoader.Mods_Unload`, that line is followed by `ModContent.Unload` and `AssemblyManager.Unload` (the assembly-load-context unload plus ten forced collections), where nothing of ours runs, so our hooks log their unload to prove it. The reloads on 0.1 and 0.2 survived with "AssemblyLoadContext still using memory" warnings, so the leak was already there. Build from the shell and relaunch instead; the cause is open.
- **The key left of 1 can never reach the mod on a Mac ISO keyboard.** FNA logs `KEY/SCANCODE MISSING FROM SDL2->XNA DICTIONARY: SDL_SCANCODE_GRAVE` and drops the press before it becomes a key, so no keybind and no raw-key fallback sees it; a whole playtest on 2026-09-08 produced zero key lines. The overlay lives on F6.
- **`WorldGen.GetTreeBottom` returns the ground tile under the trunk, not the lowest trunk tile.** Use `TreeFinder.TrunkBottom`.
- **The player renderer draws the held item from `lastVisualizedSelectedItem`**, which only `Player.Update` sets; `CompanionBody.Sync` assigns it by hand.
- **The player renderer expects a closed sprite batch**; `CompanionNPC.PreDraw` closes and reopens the NPC batch around it.
- **`Main.DrawTileCracks` adds `offScreenRange`** unless `drawToScreen`; `TileCracksRenderer` cancels it.
- **`CheckActive` returns false**, so the companion is never culled for distance.
- **The health bar draws in raw screen pixels** because `Main.mouseX/Y` are screen pixels.
- **Nothing in the brain has been watched running as of 2026-09-08.** The first batch (body, chop, bow, bar, persistence) was playtested twice on 2026-09-07; the brain, the navigator, the bag and the overlay compile and await the first run. Jump reach in `NavGrid` is derived, not measured.

## Planned work

See the Slate project `ai-companion`, milestone AIC-19 for the brain (jump-shot candidates, projectile reflexes, telemetry AIC-51 next) and AIC-8 for boss persistence before any tree work.
