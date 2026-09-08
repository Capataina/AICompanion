# AICompanion — "Multi... Player?"

A tModLoader mod that adds an AI companion to Terraria: an NPC that follows you, fights beside you, does what you are doing, will take orders, and grows through its own mastery tree. The internal mod name is `AICompanion`; the display name on the mod browser is `Multi... Player?`. This is the first step of a long-standing aspiration recorded in the LifeOS vault at `Profile/Personal/AI-Populated Games.md`, and its plan lives in the Slate project `ai-companion` (prefix AIC).

**This mod is singleplayer only, by Caner's ruling on 2026-09-07, and every line of code assumes it.** The player is always `Main.LocalPlayer`; there is no netcode, no `netUpdate`, no server/client branching, no iteration over `Main.player`. A change that adds any of those is wrong even if it works, because it spends effort on a case the mod refuses to support and makes every later feature carry the same cost.

The companion is an NPC, deliberately, not a second `Player` slot. Abilities are the mod's own closed set: the companion never runs a real item through the game's item-use code, because that routing is the class of bug (bows that will not fire, potions that cannot be used) that keeps the existing companion mod, TerraGuardians, feeling like an NPC that does some stuff. Reading an item's *numbers* is fine and expected: the chopper takes axe power and use time from the player's held axe, the weapons take speed, damage and cooldown from `ContentSamples`, and nothing goes near `Player.ItemCheck`. The one place the game only understands players is enemy targeting, and there the companion's stand-in player (the one the renderer draws) sits in a player slot and is made visible to a hostile's AI for exactly the span that AI runs, so enemies hunt it and a boss stays for it; `Companion/CLAUDE.md` has the mechanism and its limits. A downed companion reads as dead there, so a boss leaves when the player is dead and the companion is down, by Caner's ruling on 2026-09-08.

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
│  │  ├─ Survival/           survive: the body's own rescue from drowning, lava and fire
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
├─ Tools/                    console tools that are not mod code: excluded from the mod's compile and from the .tmod
│  ├─ NavReplay/             runs the real planner on a plan dump or scenario file with no game running, and draws the answer
│  ├─ WorldWindow/           rewrites a plan dump's tiles with the slope and half-block shapes from the saved world file
│  └─ Scenarios/             the committed database of places the companion must be able to reach, one block per case
└─ Telemetry/                written by the mod at run time, one .tsv per world session plus a -plans.txt of tile windows for failed plans; ignored by git and the packager, read by an agent after a playtest
```

## Operating manual

Build from the shell (what a session does to verify a change compiles):

```
cd "~/Library/Application Support/Terraria/tModLoader/ModSources/AICompanion"
dotnet build -nologo -v q
```

Zero `error CS` lines and a fresh `bin/Debug/net8.0/AICompanion.dll` is the pass. With the game closed the same command also packages the `.tmod`; while the game is open it fails at that step with `TML003: Please close tModLoader or disable the mod in-game`, which is the packaging step, not the compile.

Build for the game (what Caner does to play it): with the game closed, the shell build above packages `Mods/AICompanion.tmod`; launch tModLoader and the mod is loaded. In a world, `/companion` once; from then on it spawns with you on every world enter. F6 (rebindable under Controls → Mod Controls; on a MacBook the F-keys need Fn unless set to standard) toggles the brain overlay. Right-click the companion within reach, or click its health notch, to open its bag.

Replay a run's failed plans without the game (what a session does after a playtest, before touching the planner):

```
dotnet run --project Tools/NavReplay -- Telemetry/<stamp>-plans.txt
dotnet run --project Tools/NavReplay -- Tools/Scenarios
dotnet run --project Tools/NavReplay -- --trace-jump <scenario.txt>   # the simulated jump S→G, tick by tick
```

Every block is one scenario; the tool prints PASS or FAIL for the recorded start-to-goal plan, a second line saying whether the player's feet were reachable when they are in the window, a third saying whether the goal and the player lie inside the region the positioner's own flood reaches from the start (a goal "out" of a "complete" region can never be reached, which is the positioner's finding rather than the grid's), and the map with the path (`w j d f`) or, on a failure, every tile the search closed (`c`) so "no path" reads as "it got this far". Exit 0 only when everything passed and nothing was skipped. A dump written before the mod knew about slopes draws them as walls; rewrite it from the saved world first, which needs the `lihzahrd` parser in a venv (`python3 -m venv /tmp/wldenv && /tmp/wldenv/bin/pip install lihzahrd`):

```
/tmp/wldenv/bin/python Tools/WorldWindow/reshape.py Telemetry/<stamp>-plans.txt "~/Library/Application Support/Terraria/tModLoader/Worlds/<world>.wld"
```

The output lands in `Tools/Scenarios/` and is committed, because the scenarios are the growing database of places the companion must be able to go.

## Traps

- **The shell build says "Build succeeded" in under two seconds.** That is real; check the DLL timestamp before trusting it.
- **Mod unload runs on a worker thread, and FNA3D refuses graphics calls there.** Disposing a texture in `Unload` throws `ThreadStateException: most FNA3D audio/graphics functions must be called on the main thread`, and tModLoader then reports the mod unable to unload and demands a restart (seen on 2026-09-08 after a reload, `client.log` 13:0x, from `CompanionHealthBar.Unload`). Anything graphics-side that must be released at unload goes through `Main.QueueMainThreadAction`.
- **In-game Build + Reload has died three times and survived twice on 2026-09-08, and the cause is open.** Every death ends the log at `Unloading: ModLoader` with every hook of ours having logged its unload; nothing of ours runs after that line, and the mod holds no hooks and no game-event subscriptions (searched). The notch's texture disposal was suspected and is refuted: the third death, at 13:41, ran with that fix in. Two signatures, from `terrariasteamclient.log`'s "connection closed" line against the last client line: the two morning deaths closed 0.6 s after it (an exit), the 13:41 one 65 s after it (a hang, then most likely a force-quit), and neither wrote a macOS crash report. The survivors (11:41, 13:23) each logged "AICompanion mod class still using memory", so the assembly context leaks on every unload. Diagnose a hang, do not kill it: `~/.dotnet/tools/dotnet-stack report -p $(pgrep -f 'tModLoader.dll' | head -1)` prints every thread's stack while it hangs. Until then, the shell build with the game closed and a fresh launch is the reliable route; the in-game build itself works (the `.tmod` is packaged before the unload starts).
- **The key left of 1 can never reach the mod on a Mac ISO keyboard.** FNA logs `KEY/SCANCODE MISSING FROM SDL2->XNA DICTIONARY: SDL_SCANCODE_GRAVE` and drops the press before it becomes a key, so no keybind and no raw-key fallback sees it; a whole playtest on 2026-09-08 produced zero key lines. The overlay lives on F6.
- **`WorldGen.GetTreeBottom` returns the ground tile under the trunk, not the lowest trunk tile.** Use `TreeFinder.TrunkBottom`.
- **The player renderer draws the held item from `lastVisualizedSelectedItem`**, which only `Player.Update` sets; `CompanionBody.Sync` assigns it by hand.
- **The player renderer expects a closed sprite batch**; `CompanionNPC.PreDraw` closes and reopens the NPC batch around it.
- **`Main.DrawTileCracks` adds `offScreenRange`** unless `drawToScreen`; `TileCracksRenderer` cancels it.
- **`CheckActive` returns false**, so the companion is never culled for distance.
- **The health bar draws in raw screen pixels** because `Main.mouseX/Y` are screen pixels.
- **A tile that "has a solid tile" is not a wall.** Worldgen smooths cave corners into slopes and half blocks, the game's collision skips a slope from its open side and rests the body on its diagonal, and the fourth run of 2026-09-08 parked the companion for six thousand ticks above a staircase of five such slopes that the grid drew as `#`. Every tile question goes through `ITileWorld.Shape`, never `tileSolid` alone.
- **The brain has been watched for four short runs as of 2026-09-08, all on the surface and the first cave.** Jump edges are simulated with the motor's own arithmetic and not yet confirmed in play; the replay tool under `Tools/` is where a navigation claim is checked before a playtest, and `--trace-jump` prints an arc tick by tick.

## Planned work

See the Slate project `ai-companion`, milestone AIC-19 for the brain (jump-shot candidates, projectile reflexes, telemetry AIC-51 next) and AIC-8 for boss persistence before any tree work.
