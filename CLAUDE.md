# AICompanion — "Multi... Player?"

A tModLoader mod that adds an AI companion to Terraria: an NPC that follows you, fights beside you, does what you are doing, will take orders, and grows through its own mastery tree. The internal mod name is `AICompanion`; the display name on the mod browser is `Multi... Player?`. This is the first step of a long-standing aspiration recorded in the LifeOS vault at `Profile/Personal/AI-Populated Games.md`, and its plan lives in the Slate project `ai-companion` (prefix AIC).

## What this is trying to be, and why that is hard

Terraria is a better game with someone else in the world, and most people do not have someone else. The companion exists to be that second presence — not a pet, not a summon, not a turret that follows you. The test it is built against is whether a player forgets to manage it: it should walk with you at a comfortable distance, shoot what is shooting at you, pick up the drops you walked past, mine the vein you started on, hold a torch when it gets dark, and be somewhere sensible when you turn around. Nothing about that list is impressive individually; the difficulty is that all of it has to happen without the player ever issuing an instruction, which is why the whole thing is a scoring problem rather than a feature list.

**The existing companion mod, TerraGuardians, is the reference for what to avoid.** It routes companion actions through the game's real item-use code, which means a companion holding a bow that will not fire, or a potion it cannot drink, and the result reads as an NPC doing an impression of a player. This mod takes the opposite trade: abilities are its own closed set, so they always work, and the cost is that the companion can only do what has been written for it. Reading an item's *numbers* is fine and expected — the chopper takes axe power and use time from the player's held axe, the weapons take speed, damage and cooldown from `ContentSamples` — and nothing goes near `Player.ItemCheck`.

The second hard thing is that the companion has to be *good at the game*, and the game's own AI is no help. Vanilla walkers have no route planner anywhere, no double jump, no dash and no dodging; they are complete reactive layers, which is a genuine lesson and not a model to copy. What this mod is attempting — a body that plans a route over jumps and drops, dodges a projectile it can see coming, and chooses a weapon by what it would actually land — has no precedent in the game to lift from, so almost every navigation and combat mechanism here is built rather than borrowed, and that is where the defects live.

## What the companion actually does

Eight behaviours compete every tick, and none of them is a mode the player sets. They are grouped by family under `Brain/Actions/`, and the families exist so that adding a kind of behaviour is a folder rather than a longer switch statement.

```
staying with you   walk with the player, guard him when he is in danger, wander when he is settled
fighting           hunt a target worth shooting, kite away from something too close
working            chop a tree, mine an ore vein — beside you, on the job you are already doing
gathering          walk to a drop and pick it up
surviving          its own rescue from drowning, lava and fire
```

Two things run outside that competition, deliberately. **Shooting is the hands, not the feet**: the weapon fires every tick whenever there is a live target worth shooting, whichever behaviour won the body, because a player does not choose between travelling and attacking and neither should the companion. And **the torch is what the hand does when nothing else wants it** — no behaviour owns it, it fills an empty hand in the dark, and its light and its map reveal exist only while it is genuinely in the hand.

## How the whole thing fits together

Every tick runs the same chain, and each stage hands one small answer to the next. This is the level at which the parts fit; `Brain/CLAUDE.md` carries how the deciding actually works, and each folder's own file carries the inside of its stage.

```
the world  ──▶  Senses        one pass over tiles, hostiles, items, light and the player,
                              turning them into derived facts: how dangerous this is, how
                              long the player can safely be left, how dark it is here
                  │
                  ▼
                Reflexes      a threat already on its way gets a dodge, simulated against
                              every nearby hostile, and takes the body outright — nothing
                              below this line runs on that tick, because a reflex that
                              waits for a score arrives late
                  │
                  ▼
                Chooser       every behaviour scores itself from the same facts and the
                              best one wins; the winner asks for a *kind of place to be*
                              rather than a destination
                  │
                  ▼
                Positioner    that request becomes an actual tile, scored over candidates
                              around an anchor — can I get there and back, can I see him,
                              can I shoot from there, is anything about to walk through it
                  │
                  ▼
                Navigator     a route to that tile, planned over moves the body has been
                              proven able to make, then walked step by step
                  │
                  ▼
                the Motor     the only thing that touches the NPC's velocity and position
                  │
                  ▼
                the hands     the weapon fires and the torch fills a free hand, both
                              independent of whatever the feet were told
```

**Four foundations are shared across that whole chain, and they are the things to know before changing anything.**

The body's arithmetic lives in one place. `Brain/DecisionMatrix/Navigation/Body/` holds the movement numbers and the one-tick rules, and the real motor, the planner's simulated body, the dodge simulation and the offline replay tool all move a body by *that same code*. That is what makes a planned move trustworthy: the route was proven with the same arithmetic that will execute it. It is also the most dangerous seam in the project, because when the simulated body and the real one disagree by even one condition, the planner proves routes the body cannot walk — which has cost this project multiple sessions.

The navigation core does not know the game exists. Everything under `Navigation/` talks to tiles through an interface, with exactly one implementation that names Terraria types; a shell script enforces the boundary. That is what lets the planner run headless in a console tool against a text file, which is where every navigation claim gets checked before a playtest.

Every tunable number is in one file. `Brain/DecisionMatrix/Decision/Weights.cs` holds them, so a playtest note like "it hugs too close" is one edit in one place. A number that appears anywhere else is a bug or a fact of the world.

Every tick is recorded. `Brain/Debug/` writes a row per tick to a file, plus a census of what the planner offered against what the body actually did, a drawing of the whole session, and windows around anything that looked wrong. `Tools/SessionReport` reads that back and prints what is definitely wrong, probably wrong and merely odd. This exists because the alternative is diagnosing behaviour from memory of watching it, which produced several confident wrong fixes before the recorder existed.

## The rulings that shape every line

**This mod is singleplayer only, by Caner's ruling on 2026-09-07, and every line of code assumes it.** The player is always `Main.LocalPlayer`; there is no netcode, no `netUpdate`, no server/client branching, no iteration over `Main.player`. A change that adds any of those is wrong even if it works, because it spends effort on a case the mod refuses to support and makes every later feature carry the same cost.

**The companion is an NPC, deliberately, not a second `Player` slot.** The one place the game only understands players is enemy targeting, and there the companion's stand-in player — the one the renderer draws — sits in a player slot and is made visible to a hostile's AI for exactly the span that AI runs, so enemies hunt it and a boss stays for it; `Companion/CLAUDE.md` has the mechanism and its limits. A downed companion reads as dead there, so a boss leaves when the player is dead and the companion is down, by Caner's ruling on 2026-09-08.

**The companion never teleports, by ruling on 2026-09-08.** Knocked off a boss platform it climbs back; sent away it walks back, watchable on the map, where it is drawn as its own head. A companion trapped behind a sand fall stays trapped until the player digs it out; rescue behaviours are later work (AIC-65). It reveals the map only with what its torch actually lights, never its whole screen, because that would show what is behind walls.

**The companion decides by scoring, not by a priority chain.** A priority chain answers "what is the most important thing" and a score answers "what is worth doing given everything at once", and only the second can weigh a nearly-dead zombie against a tree three screens away. Scores are products rather than sums, which is the single most important property of the design: any one consideration returning zero vetoes the behaviour outright and no other term can buy it back, so "close, reachable and in sight" is expressible and "very close, therefore ignore that it is unreachable" is not.

**No hard-coded scenario behaviour.** Cases like "fire into the pit from the rim rather than jumping in" are outcomes a generic system should produce, by Caner's ruling on 2026-09-08, not branches to write. Likewise nothing hard-codes a biome list, and every in-game failure becomes a replayable scenario file rather than a patch aimed at the instance.

**Before implementing any mechanic, read the decompiled game for the path that already does it, and reuse it unless it is gated on the local player.** Caner's standing instruction on 2026-09-07. Chopping reuses `HitTile` plus the vanilla axe formula and `Main.DrawTileCracks`; mining runs the game's own `PickTile`; the body reuses the player renderer; doors ask `WorldGen.OpenDoor` whether they can swing rather than measuring clearance; arrows are vanilla projectiles owned by the player so the player's on-hit accessories and ranged stats apply; the bag uses the game's own `ItemSlot` and the player's own coin pickup. Decompile with `ilspycmd -t Terraria.<Type> "<Steam>/tModLoader/tModLoader.dll"` (installed under `~/.dotnet/tools`) and grep the result; the reflection scratch tool at `/tmp/tmlreflect` lists member signatures.

**The player plays with a double jump**, by his own note on 2026-09-08, which matters whenever a recorded player trail is read as evidence: a trail tile the companion's body could never reach is his accessory and not a planner defect.

## What this is not

- **Not a second player.** No inventory management burden, no orders required to function, no controlling it directly. Orders are a planned addition on top of a companion that already behaves well without them.
- **Not multiplayer, and not multiplayer-ready.** See the ruling above; this is the most common well-intentioned change that would be rejected.
- **Not a combat pet.** Fighting is one of several families of behaviour and loses to staying with the player at range, on purpose.
- **Not a mod that runs real items through the game's item code**, which is the specific trade that keeps its abilities reliable.
- **Not tuned by scenario.** A behaviour that is right only because someone wrote a branch for that situation is a defect here even when it looks correct in play.

## Where it stands, 2026-09-09

Ninety-four commits over three days. The whole chain above is built and compiles: senses, reflexes, the eight behaviours, the positioner, the A* navigator over proven moves, both weapons and the arsenal that picks between them, chopping, mining, the torch, doors, the bag, the HUD notch, the map head and its torch reveal, per-character persistence, the per-tick recorder and the session reader, and the headless replay tool with a committed scenario database.

What is genuinely verified in play is much smaller than what is built, and the distinction matters: the body draws and swings, trees get chopped at the right trunk, arrows fly, the health bar and persistence work, and the brain runs. Navigation has been watched over a handful of short surface-and-first-cave runs, and the most recent fix — the motor climbing a platform it was meant to be falling through, which froze the body for hundreds of ticks — is packaged and awaiting a playtest at the time of writing.

The mastery tree is designed and unbuilt. Its shape is deliberately undecided: a mix of levels and resource costs paid from the player's inventory at the tree rather than skill points alone, and by the 2026-09-08 revision it may gate *sending* behaviours (go gather, go farm) while doing-with-you behaviours come early or free. Weapon direction is decided and partly unbuilt: short sword and bow at launch, throwing knives as a piercing upgrade, an overheat mechanic.

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
│  │  ├─ Navigation/         how to get there, split by owner
│  │  │  ├─ World/           the tile interface, the game world, the text world of dumps and scenarios
│  │  │  ├─ Body/            the body's numbers and shape tests, shared by the motor, the planner and the reflexes
│  │  │  ├─ Traversals/      one class per move kind (walk, jump, drop, fall-through), each proving the move for the grid and performing it for the follower from one shared steering rule
│  │  │  ├─ Planning/        the grid, A*, the path, reachability
│  │  │  └─ Following/       the navigator that performs the path through the motor
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
│  │  ├─ Torch/              the torch in the dark
│  │  └─ Doors/              the door in the way, opened the way a villager opens it
│  ├─ Aiming/                the arc solver every ranged weapon and the positioner share
│  └─ Debug/                 the brain overlay (left square bracket), the per-tick telemetry writer, the nine scenario detectors, and the whole-session map
├─ Combat/
│  └─ Weapons/               the two equipped weapons and the arsenal that picks between them
├─ Map/                      the companion on the world map, and what its torch reveals there
├─ Inventory/                the bag, its panel, and the right-click that opens it
├─ UI/                       the HUD health notch
├─ Players/                  the ModPlayer: persistence and input
├─ Commands/                 /companion
├─ Localization/             en-US strings (display name, keybind)
├─ Tools/                    console tools that are not mod code: excluded from the mod's compile and from the .tmod; check-navigation-boundary.sh keeps the navigation core free of game types
│  ├─ NavReplay/             runs the real planner on a plan dump or scenario file with no game running, walks the plan with the real navigator, and draws the answer
│  ├─ SessionReport/         reads a playtest's .tsv back and prints what is definitely wrong, probably wrong and merely odd, with a coverage block for the checks the file is too old to run
│  ├─ WorldWindow/           rewrites a plan dump's tiles with the slope and half-block shapes from the saved world file
│  └─ Scenarios/             the committed database of places the companion must be able to reach, one block per case
└─ Telemetry/                written by the mod at run time, one .tsv per world session plus three siblings under the same stamp: a -plans.txt of tile windows for failed plans and detected scenarios (a follow failure, a stuck run, a hit through a dodge, a missed mode, one spot wanted and never approached, a pinned body) each with the player's trail, a -census.txt counting every move the planner offered against every one the body made, and a -map.txt drawing the whole session; ignored by git and the packager, read by an agent after a playtest
```

## Operating manual

Build from the shell (what a session does to verify a change compiles):

```
cd "~/Library/Application Support/Terraria/tModLoader/ModSources/AICompanion"
dotnet build -nologo -v q -p:BuildMod=false
```

Zero `error CS` lines and a fresh `bin/Debug/net8.0/AICompanion.dll` is the pass. `BuildMod=false` skips packaging, which is the right default: it works whether or not the game is open, and it cannot rewrite a `.tmod` underneath a playtest in progress.

Build for the game (what Caner does to play it): with the game closed, drop `-p:BuildMod=false` and the same command packages `Mods/AICompanion.tmod`; launch tModLoader and the mod is loaded. With the game open that step fails with `TML003: Please close tModLoader or disable the mod in-game`, which is the packaging step and not the compile. In a world, `/companion` once; from then on it spawns with you on every world enter. The left square bracket toggles the brain overlay, and so do F6, the key left of 1 and the ISO-section key, because every one of them is also read raw: a binding saved by an earlier version overrides the registered default permanently, so the raw list rather than the default is what makes the overlay reachable. Rebindable under Controls → Mod Controls. Right-click the companion within reach, or click its health notch, to open its bag.

Read a playtest back (the first command after he stops playing, before opening any column by hand):

```
dotnet run --project Tools/SessionReport -- Telemetry
```

It takes the newest `.tsv` in the folder, prints the session's shape and the census, then every finding sorted into definitive issues, potential issues and oddities, and exits non-zero when anything is definitively wrong. A check whose columns the file predates is skipped by name in a coverage block, so an old session reads as reduced coverage rather than as a clean run. `Tools/SessionReport/CLAUDE.md` has what separates the three categories and how to add a check.

Replay a run's failed plans without the game (what a session does after a playtest, before touching the planner):

```
dotnet run --project Tools/NavReplay -- Telemetry/<stamp>-plans.txt
dotnet run --project Tools/NavReplay -- Tools/Scenarios
dotnet run --project Tools/NavReplay -- --trace-jump <scenario.txt>   # the simulated jump S→G, tick by tick
dotnet run --project Tools/NavReplay -- --no-cache Tools/Scenarios      # every edge simulated afresh; must read the same verdicts as with the cache
dotnet run --project Tools/NavReplay -- --churn Tools/Scenarios         # breaks every tile under a found path in turn; the warm cache's plan must equal a cold one's (0 stale plans)
dotnet run --project Tools/NavReplay -- --follow Tools/Scenarios        # the real navigator walks each found plan over the simulated body; every edge and its outcome, the state tick by tick at the first fault
dotnet run --project Tools/NavReplay -- --edges X,Y <scenario.txt>      # every edge each traversal offers from that tile, with the profile, line and ticks each carries
dotnet run --project Tools/NavReplay -- --trace-walk X,Y,DIR <scenario.txt>   # the walk proof from that tile that way, tick by tick
dotnet run --project Tools/NavReplay -- --follow-ticks A,B <scenario.txt>     # --follow with every tick from A to B printed, for a stall that never faults
sh Tools/check-navigation-boundary.sh                                    # no code line under Navigation/ names the game outside World/GameTileWorld.cs; exit 0 is the pass
```

Every block is one scenario. The tool prints PASS, FAIL or SEALED for the recorded start-to-goal plan, a second line saying whether the player's feet were reachable when they are in the window, a third saying whether the goal and the player lie inside the region the positioner's own flood reaches from the start (a goal "out" of a "complete" region is then classed by whether the flood ever read past the window's edge: SEALED START is a pocket the world closes, which is a rescue's job and not the planner's; SEALED GOAL is a spot the positioner must not offer; both clipped is undecidable as cut and wants `--pad`; a sealed block is its own count and does not fail the run), a fourth naming the first tile of the player's trail the grid refuses when the block carries one, and the map with the path (`w j d f`) or, on a failure, every tile the search closed (`c`) so "no path" reads as "it got this far". Exit 0 only when everything passed and nothing was skipped; a recorded goal the window does not hold is skipped as untestable, never replayed to the player's tile instead.

A dump written before the mod knew about slopes draws them as walls; rewrite it from the saved world first, which needs the `lihzahrd` parser in a venv (`python3 -m venv /tmp/wldenv && /tmp/wldenv/bin/pip install lihzahrd`):

```
/tmp/wldenv/bin/python Tools/WorldWindow/reshape.py Telemetry/<stamp>-plans.txt "~/Library/Application Support/Terraria/tModLoader/Worlds/<world>.wld" --pad 48
```

The output lands in `Tools/Scenarios/` and is committed, because the scenarios are the growing database of places the companion must be able to go. `--pad N` grows every window by N tiles from the saved world, because the edge is a wall to the tool and a window cut close fails for the edge rather than the planner; the world is the end of the session, so a padded tile can differ from what the run saw. Widen until no block prints "undecidable as cut": the fourth run needed 48, the fifth 96, and the header says which. A dump's window is never a screenshot: it is the smallest box holding the plan's start, its goal and the player's tile, padded by the constant in `BrainTelemetry`, so a plan whose three points sit close is a thin block and one whose player is far is a wide one. From 0.4.13 the pad is wider than half a screen on every side, by Caner's ruling that the wide picture is the default and not the rescue, so a fresh dump rarely needs `--pad` and older dumps still do.

## Traps

- **The shell build says "Build succeeded" in under two seconds.** That is real; check the DLL timestamp before trusting it.
- **Mod unload runs on a worker thread, and FNA3D refuses graphics calls there.** Disposing a texture in `Unload` throws `ThreadStateException: most FNA3D audio/graphics functions must be called on the main thread`, and tModLoader then reports the mod unable to unload and demands a restart (seen on 2026-09-08 after a reload, `client.log` 13:0x, from `CompanionHealthBar.Unload`). Anything graphics-side that must be released at unload goes through `Main.QueueMainThreadAction`.
- **In-game Build + Reload has died three times and survived twice on 2026-09-08, and the cause is open.** Every death ends the log at `Unloading: ModLoader` with every hook of ours having logged its unload; nothing of ours runs after that line, and the mod holds no hooks and no game-event subscriptions (searched). The notch's texture disposal was suspected and is refuted: the third death, at 13:41, ran with that fix in. Two signatures, from `terrariasteamclient.log`'s "connection closed" line against the last client line: the two morning deaths closed 0.6 s after it (an exit), the 13:41 one 65 s after it (a hang, then most likely a force-quit), and neither wrote a macOS crash report. The survivors (11:41, 13:23) each logged "AICompanion mod class still using memory", so the assembly context leaks on every unload. The fourth hang (17:37, the same last line) was sampled alive with macOS `sample <pid> 5 -file out.txt` (`dotnet-stack` hangs too, because the runtime's diagnostic thread is suspended with everything else): a thread-pool worker is inside the loader's own `GC.Collect` (the unload's memory check, `ModLoader.cs` WarnModsStillLoaded) with the collector's mark phase spinning in a handful of instructions for every sample of five seconds, and every other thread parked in the runtime's suspension wait. A marking loop that never ends is a corrupted object graph or a runtime fault, under Rosetta (the process is x86-64 translated); the mod has no unsafe code, only two bounds-checked texture uploads, so what our side contributes, if anything, is the leaked assembly context the survivors log. Until the cause is known, the shell build with the game closed and a fresh launch is the reliable route; the in-game build itself works (the `.tmod` is packaged before the unload starts), so a hang costs a force-quit and nothing else.
- **The key left of 1 can never reach the mod on a Mac ISO keyboard.** FNA logs `KEY/SCANCODE MISSING FROM SDL2->XNA DICTIONARY: SDL_SCANCODE_GRAVE` and drops the press before it becomes a key, so no keybind and no raw-key fallback sees it; a whole playtest on 2026-09-08 produced zero key lines. The overlay default is the left square bracket.
- **A keybind saved by an earlier version outranks the registered default for ever, so moving a default moves nothing for anyone who has already played.** The overlay was dead through the whole 2026-09-09 session against a saved binding of the key FNA drops, while the default in code was F6 and the raw fallback would have answered F6 immediately — the Mod Controls screen showing the dead key was the only visible symptom and it pointed at the wrong thing. Anything the mod wants reachable regardless of saved state is listed in the raw key check in `Players/CompanionPlayer.cs`, not just registered as a default.
- **`WorldGen.GetTreeBottom` returns the ground tile under the trunk, not the lowest trunk tile.** Use `TreeFinder.TrunkBottom`.
- **The player renderer draws the held item from `lastVisualizedSelectedItem`**, which only `Player.Update` sets; `CompanionBody.Sync` assigns it by hand.
- **The player renderer expects a closed sprite batch**; `CompanionNPC.PreDraw` closes and reopens the NPC batch around it.
- **`Main.DrawTileCracks` adds `offScreenRange`** unless `drawToScreen`; `TileCracksRenderer` cancels it.
- **`CheckActive` returns false**, so the companion is never culled for distance.
- **The health bar draws in raw screen pixels** because `Main.mouseX/Y` are screen pixels.
- **Anything of ours that writes `npc.position` during the AI phase is invisible to the record, and one such writer cost three failed fixes.** `oldPosition = position` is assigned inside the engine's `Collision_MoveWhileDry` immediately before `position += velocity`, so the telemetry's `moved` column spans the engine's own move and nothing our brain, motor or a `Collision.*` helper did beforehand. `Collision.StepUp` writes position by reference and never touches `velocity.Y`, and calling it with `holdsMatching: true` on every tick of a descent made the companion climb the platform it was falling through for 265 ticks while reading as a body with a large velocity, no collision and no movement. The general rule: a reused game helper that takes a "the player is holding this" flag needs that flag computed per tick from the same intent a vanilla NPC computes it from, never hard-coded — the town NPC recomputes it from whether it is above its home, the fighter from whether its target is below. The `pinned` column exists to catch the whole class without knowing which writer it is.
- **A tile that "has a solid tile" is not a wall.** Worldgen smooths cave corners into slopes and half blocks, the game's collision skips a slope from its open side and rests the body on its diagonal, and the fourth run of 2026-09-08 parked the companion for six thousand ticks above a staircase of five such slopes that the grid drew as `#`. Every tile question goes through `ITileWorld.Shape`, never `tileSolid` alone.

## What a reader will get wrong here

- **The two bodies are the point of failure, not a curiosity.** There is a real NPC the engine moves and a simulated body the planner proves moves against, and they run the same arithmetic on purpose. Reading either one as "the" body is how a fix gets built for the half that was not broken; a divergence measurement runs every tick precisely because this keeps happening.
- **The stand-in player in `Main.player` is not the companion.** It is a drawing-and-targeting device that is only active inside a hostile's AI call. Damage, life and death all belong to the NPC.
- **The mod's version in `build.txt` moves with the work and is not a release signal.** Nothing here has shipped to anyone.
- **A number in any of these folder files is either a fact of the world or a dated measurement.** Tunables live in `Weights.cs`, and a threshold quoted in prose anywhere else is a documentation defect rather than the current value.

## Planned work

See the Slate project `ai-companion`, milestone AIC-19 for the brain and AIC-8 for boss persistence before any tree work.
