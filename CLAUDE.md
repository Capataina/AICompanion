# AICompanion — “Multi... Player?”

This singleplayer tModLoader mod makes an NPC companion that behaves as a second presence in Terraria: it keeps roughly with the player, fights, collects nearby drops, helps with work already underway, and lights dark places. It is deliberately neither a second player nor a pet. **Since the night of 14 September 2026 its body is a flying orb**, twenty pixels across, that plans over free space and is handed two weapons, a pickaxe and an axe by the player; the player-shaped walking body, its tile-graph route search and its authored weapon kit were retired that night after six negative captures whose every defect was a route the planner proved and the body then did not perform (the Slate Record carries the decision and what lost). The companion's *mechanisms* are a closed set rather than calls into real item use — it fires a handed weapon's projectile itself, swings a handed sword in its own arc, drills with a handed pickaxe's power — so each is reliable and written explicitly; the *items* those mechanisms read are open, any weapon or tool whose use is aiming and releasing.

The lanes that built that body landed on 15 September 2026 (`806a998` the body and its navigation, `c94c797` the gear, the notch and the mastery wheel on main between them), and every folder guide describes the orb. Its description at the level of the product is the Slate Architecture field and README's Expected Behaviour and System In Place; at the depth of a walkthrough it is `Companion/Brain/Infrastructure/Movement/CLAUDE.md` for the body and its navigation, `Companion/Inventory/CLAUDE.md` for the four slots and `Companion/Weapons/CLAUDE.md` for what the hands do with them.

**What the companion is supposed to do lives in `README.md`, and reading it is the first move on any behaviour work.** That file carries four things in the order they have to be read: Expected Behaviour, a half-hour of play written as a story with no reference to any system; Current Behaviour, what it actually does, sourced only from named telemetry sessions; The System In Place, the machinery read from source; and a table of named responsibilities carrying all three per row. Each section opens with its own rules for maintaining it. This guide describes how the code is arranged; that file describes what it is for, and the two disagree only when one of them is stale.

**A play behaviour we want to see, said in a session, is not a note to remember later.** If it is not already in Expected Behaviour, it goes into `README.md` in the same sitting: a row in Behaviour By Behaviour first, then a scene in the story, in the words a player would use. Nothing in that write-up may name a score, a family, a search or a mode. Prefer thickening a scene that already holds the collision over adding a timestamp. A wish that stays in chat is how the companion we described fails to be the one we build.

## The companion is an opportunistic companion, not an orders system

Behaviours compete by score each tick for companionship, combat, gathering and nearby work. Nothing suspends ordinary activity to keep the body safe: avoiding a hit is a bend in whatever the body is already doing, so the job keeps the body while it dodges, and every liquid is air to the orb, so there is nothing to escape. Only downing and recovery flight take the body. Shooting is independent hands work, so it runs while the feet follow or dodge; the torch fills an otherwise free hand. Missions and player-directed sending were abandoned.

The unbuilt mastery progression is stats, weapon unlocks and movement unlocks, and **nothing on it changes what the brain decides**. That is a ruling rather than a description of what is built: a behaviour sitting behind an upgrade multiplies the numbers to tune and reads to a player as a weird gate, so an upgrade may change what a move costs or what the world does to the companion, never whether an activity is available to choose. Where a mastery node and the brain do interact, the interaction is expected to emerge through the senses rather than be scripted — a torch hat brightens the companion's surroundings, so the light sense reports a lit neighbourhood and the torch decision sees no reason to hold one, with nothing in the torch code aware that a hat exists. A selectable interface preview is not a gameplay ability.

## The architecture has one movement boundary

```
Terraria world ─► Brain/Infrastructure/Observation
                     │   player · terrain · threats · light · reach · intent
                     │
                     ├─► SharedBehaviours/Safety ─► Infrastructure/Movement ─► motor ─► NPC
                     └─► Activities ─► Infrastructure/Selection ─► Infrastructure/Position ─┘
                                  │
                                  └─► Infrastructure/Interactions and Companion/Weapons
```

**A fact the whole brain needs is a sense, not a private answer.** Light, reach and the player's intent region are the three that were extracted, and they are the pattern for the next one. The region is the newest: "how far from the player" used to be answered in five places with five radii (the reunion pull, the work radius, the meeting place's anchor, the lighting search's centre, the collect radius), and it is now one box carried ahead of the player's feet by his own observed pace that every one of them measures to. Reachability used to be the positioner's own: everyone else either asked the positioner for the one shape it happened to expose or ran a route search of their own, so five callers each paid a fresh bounded search for a question one flood had already answered. Both now sit in Observation beside the threat sense, and the positioner is a consumer like everybody else — it keeps its whole public surface as one-line delegations, deliberately, because renaming it would have been a sweep through files for no behaviour.

Both senses answer in three values rather than two, and the middle one is the reason they are worth having. A tile missing from a flood that has not finished is *not yet known*, not *absent*; a place the engine has not lit is *unread*, not *bright*. An activity that refuses the middle answer is declining to start on an unanswered search rather than asserting the world is empty, which is a distinction each caller used to have to reconstruct and now cannot get wrong. The two senses keep different clocks and this bites: the reach flood grows every brain tick, bounded per slice, but where it is rooted and when a replacement flood takes over is decided on the positioner's rescore, because the region a candidate is scored against has to hold still for a rescore; ageing that decision on the tick multiplies the cadences and refloods the region every few seconds.

`Companion/Brain/CoordinateBrainTick.cs` owns the tick order. `Companion/Brain/Infrastructure/Movement/CoordinateMovement.cs` is the single movement request interface; its query surface is how other brain systems ask movement questions. The motor is the only component permitted to write the live NPC, and it is also where the body's contact with terrain runs: the engine's box collision is switched off for the orb, so the mod's circle-against-tiles test is the one body, compiled into the game-free core and run identically by the mod, by NavReplay, by EngineReplay and by the world run over Terraria's own tiles. There is no second body to keep in parity with, which is why the walker's parity recorder and its collision matrix are gone rather than rewritten.

## Rulings that constrain every change

- This is singleplayer only: use `Main.LocalPlayer`; do not add netcode, server branches, `netUpdate`, or player iteration.
- The companion is an NPC. Its temporary stand-in player exists only while hostile AI runs so enemies can target it; life, death and movement belong to the NPC.
- It never teleports. Ordinary following can start continuous recovery flight when far from the live player; combat, work and downing cannot start it. Recovery ignores terrain and is the one exception to ordinary contact.
- The body is an orb and nothing plans as if it stood. A route is a corridor of free space wider than the body; reach is a flood over free cells; the ceiling above the player's feet is a positioning rule, never a wall the body meets. Every liquid — water, honey, lava and shimmer — is air to it: no search treats one as a wall, the body is never hurt, slowed or transformed by one, and nothing takes the body out of one. The owner ruled on 15 September 2026 that this immunity is built in rather than a mastery unlock, because a capability an upgrade switches on would have to be tested with and without it after every later change to the brain. A read of liquid that is about something other than the body — where a torch may go, where a drop will land, how an enemy or a projectile moves — stays. Slopes are full tiles to it and platforms are passable. There is one body: the orb's contact with terrain is the mod's own circle-against-tiles test, run identically in the mod and in every headless tool, with the engine's box collision switched off for it.
- Decisions use multiplicative utility scoring, never priority branches or scenario-specific rules. Whether utility scoring is the right brain at all is a question the owner has opened rather than settled; the route-search question closed on 2026-09-14 with the orb, whose search is A* over free cells with a clearance cost.
- Reuse a decompiled Terraria path when it is not gated on the local player. The mod reads game item numbers but never runs companion abilities through `Player.ItemCheck`; a handed weapon or tool is read for its facts and used by the companion's own mechanisms, and an item those mechanisms cannot express is refused by its slot.
- A decision evaluates every option it has and learns from what its own actions did, never a hand-written proxy for the question. The owner ruled this on 15 September 2026 about weapon and target choice and said it holds for every system from here on: a firing spot is priced by the best attack every handed weapon could make from there, not by the weapon chosen last; how a weapon flies, shoves and hurts each enemy type is learned from its own hits; and interactions between weapons, such as one applying a debuff the other exploits, are to be learned the same way rather than written as rules. A factor that stands in for a quantity the brain could compute is a defect waiting for the case where the two disagree.
- Four slots and no more: two weapons, a pickaxe and an axe. No armour, no accessories, no ammo slot; ammo is free, mana mirrors the player's and tires magic rather than stopping it.

## Map

```
AICompanion/
├─ Companion/                the complete companion gameplay subsystem
│  ├─ CharacterBody/         the orb NPC's lifecycle, its drawing, its immunity to every liquid and the stand-in hostiles aim at
│  ├─ EnemyIntegration/      hostile targeting bridge and spawn-rate adjustment
│  ├─ Brain/                 observe, choose, request movement
│  │  ├─ Activities/         the seven jobs and their shared contract
│  │  │  ├─ Combat/         guarding and hunting
│  │  │  ├─ Gathering/      mining and chopping
│  │  │  └─ NearbyAssistance/ lighting, collection and keeping company
│  │  ├─ SharedBehaviours/   can take the body without winning a family
│  │  │  ├─ Safety/         the hit prediction the evade layer reads
│  │  │  └─ Recovery/       distant flight home
│  │  └─ Infrastructure/     how those get done
│  │     ├─ Observation/    player, terrain, threat and activity facts, and the light, reach and intent-region senses
│  │     ├─ Selection/      utility scoring and the behaviour tunables
│  │     ├─ Position/       position requests and candidate scoring
│  │     ├─ Movement/       travel, avoidance and the motor
│  │     │  ├─ Contact/            the circle-against-tiles contact, the one body the orb has
│  │     │  ├─ FreeSpace/          what is free for the orb, the clearance field, the corner graph and the resumable search
│  │     │  ├─ Steering/           the body's state and controls, the route, the steering law, the navigator and the census
│  │     │  ├─ TerrainModel/       the tile world every search reads, and the record of where it was edited
│  │     │  └─ TerrariaIntegration/ the live tile reader, the edit announcements and the motor, outside the game-free core
│  │     ├─ Interactions/   chop, mine, torch, doors, homes
│  │     │  ├─ Chopping/          trees and their trunks
│  │     │  ├─ Mining/            ore tiles through the game's own PickTile
│  │     │  ├─ Torch/             carrying and placing light
│  │     │  ├─ Doors/             opening what the route treats as a wall
│  │     │  └─ WorldProtection/   what autonomous edits may not touch
│  │     ├─ Aiming/         projectile trajectory solver, and the arc it learns per projectile type from the companion's own shots
│  │     ├─ Grants/         one packet for feet and hand
│  │     └─ Diagnostics/    overlay layers, cost strip, telemetry, scenario capture
│  ├─ Weapons/               the arsenal's target, weapon and aim choice valued by what its shots achieved, the item-backed weapon, and the mana pool
│  ├─ Progression/           the experience total and level curve the notch draws and the tree will spend
│  ├─ Inventory/             persistent cargo bag, the four gear slots, and their panel
│  ├─ PlayerIntegration/     persistence, input, player events and /companion
│  ├─ ProfileCard/           native behaviour controls, inventory and mastery pages
│  ├─ DiagnosticsConfiguration/ native inspector and recording switches
│  ├─ MapIntegration/        map head and torch-limited reveal
│  └─ HeadsUpDisplay/        player-facing health notch
├─ Localization/             display strings
├─ Tools/                    headless replay, report, reshape and verification tools
│  ├─ NavReplay/             the game-free movement core's contract rows, and the scenario extractor
│  ├─ EngineReplay/          native fixtures, in folders by what they ask
│  │  ├─ Combat/             guarding, hunting, firing, handed gear, the item weapon, learned arcs and encounter context
│  │  ├─ Gathering/          ore and tree work, and work accounting
│  │  ├─ Assistance/         lighting, collection, keeping company, the light and reach senses
│  │  ├─ Movement/           engine motion, following, mislandings
│  │  ├─ Observation/        the senses' lifecycle, family offers, evidence scenes
│  │  └─ Lifecycle/          spawn, death, preferences, the HUD and the native card
│  ├─ SessionReport/         chronological reader
│  │  ├─ Read/               the chronicle and the god's-eye events
│  │  ├─ Checks/             the findings graded against the record
│  │  ├─ Measures/           the play measures the self-test pins against a real capture
│  │  ├─ Write/              the rendered report
│  │  └─ Tests/              the reader's own self-test
│  ├─ Ledger/                the committed run record every instrument files rows into, and the scoreboard against the last clean ancestor
│  ├─ WorldRun/              the whole brain and the native body in a real saved world, behind a recording's player track
│  ├─ WorldWindow/           saved-world restoration of capture geometry
│  ├─ Scenarios/             committed terrain fixtures from play
│  └─ Decompiled/            gitignored; game source written on demand by Tools/decompile.sh
├─ InterfaceExperiments/     selectable visual prototypes outside game code
├─ research/                architectural questions, evidence and trade-offs for discussion
│  ├─ Decision Architecture/       what chooses, when it commits, what it costs
│  ├─ Navigation Research/         how platformer bodies are routed elsewhere
│  ├─ Evaluation and Observability/ what to measure, what the recorder must carry, and the plan for the harness that grades it
│  ├─ Implementation Evidence/     what this codebase was measured doing
│  ├─ Historical Evidence/         the commit chronology and coverage ledger
│  ├─ Game and Mod Case Studies/   how other games solved the same problems
│  └─ proposal/                    the ranked architecture proposals
├─ runs/                     gitignored; agent-harness session logs, tens of megabytes each
└─ Telemetry/                gitignored; runtime session records written by the mod
```

Architectural research and the trade-offs under discussion live in [research/Architecture and Behaviour Map.md](<research/Architecture and Behaviour Map.md>). Its hypotheses are separate from accepted gameplay decisions; utility selection and route search remain open questions.

## Operating manual

Compile without packaging while the game may be open:

```
cd "$HOME/Library/Application Support/Terraria/tModLoader/ModSources/AICompanion"
dotnet build -nologo -v q -p:BuildMod=false
```

The pass is zero `error CS` lines and a fresh `bin/Debug/net8.0/AICompanion.dll`. Build with packaging only when the game is closed. Run the navigation-boundary check after movement-core changes:

```
sh Tools/check-navigation-boundary.sh
```

The whole check, and the one nearly every commit body in this repository cites as its evidence, is:

```
sh Tools/verify.sh
```

It builds without packaging, proves the DLL is newer than every source file that feeds it rather than trusting "Build succeeded", runs `check-navigation-boundary.sh`'s three boundaries, then the ledger, NavReplay and SessionReport self-tests, then the native engine suite, and finally the ledger's scoreboard, whose exit code is the script's. Every instrument runs to the end whatever the one before it did.

**Exit 0 means no row went red, and that is less than "everything passed".** A case that stopped reporting — skipped, or filing no row at all — is not a red row, so the scoreboard names it in its own block and carries the count on the closing line, and the closing line is therefore the thing to read rather than the exit code. That is deliberate rather than a gap: a fresh clone has no capture for the play measures, because `Telemetry/` is gitignored, and failing the run for that would punish the clone for the store's shape. Exit 2 still means a check could not be asked at all, which is the absent-ripgrep case and is neither a pass nor a violation.

```
sh Tools/verify.sh --case "ore work"        only cases whose name contains the fragment
sh Tools/verify.sh --rerun-red 5            rerun each red five times and print its interval
sh Tools/measure-flake.sh 30 "ore work"     one case many times at one commit, with its interval
sh Tools/backfill-capture.sh <capture>      a recording as a ledger run under the revision that wrote it
```

A red is a stop whatever the reruns show: a case that fails once and then passes four times prints `FLAKY` with its interval and the run still exits 1, because the fail row is a row.

Read a playtest with `dotnet run --project Tools/SessionReport -- Telemetry`; replay and world-window procedures live in their respective tool folders. `Telemetry/` is gitignored, so a fresh clone has nothing to read until a playtest writes one.

## Traps

- **The shell build says "Build succeeded" in under two seconds.** That is real; check the DLL timestamp before trusting it. `Tools/verify.sh` already checks this for you — it refuses a DLL older than any source file feeding it — so the trap bites hardest when building by hand.
- **`sh Tools/verify.sh` is not reliably green under load, and a single green run is not evidence that it is.** Measured 2026-09-14 on this machine at effd5e9, five consecutive runs with a droid fleet building in the same checkout: three exited 0, two exited 1, both on `VerifyOreWork.RaisedLipsAtBothGravitiesProduceWork` at `Tools/EngineReplay/Gathering/VerifyOreWork.cs`, "raised lip must produce a native ore break", the `mirrored=True, mode=HeldActivity` case at floor 90 on one run and floor 60 on the other. Five runs at 1554e0b with the machine idle were all green. Later the same day, with a second android building the same suite concurrently, the reach-sense lane saw 13 of 15 runs red on the same fixture, every failure identical (`floor=60, mirrored=True, mode=HeldActivity, miningTicks=15, action=<empty>`), and checking out the commit before its own first change reproduced the failure identically, which is the cheap control: **before reading a red on this fixture as yours, run the suite at the parent of your first commit.** The load hypothesis (wall-clock planning deadlines expiring) is AIC-249's; it is not settled. The practical consequence when reading or writing a commit body: a citation of "verify.sh exits 0" after one run under load confirms much less than it implies, and a red on this fixture alone is probably not yours. Anything depending on this suite needs a batch of runs sized to how rarely the flake fires, taken idle. **The row that flaked no longer exists**: `RaisedLipsAtBothGravitiesProduceWork` was one of the walker's gravity rows and went with the body on 2026-09-15, so the signature above cannot recur on it and the load hypothesis is untested rather than settled; the per-case reset that rebuilds the tile map (`Tools/EngineReplay/ResetProcessState.cs`) removed the one order dependence the rebuild did find, which was terrain rather than time.

  **The suite no longer lets the wall clock decide any of this, and the flake did not fire either side of that change.** Every case now runs with the millisecond allowances lifted, so the planning deadline cannot depend on how busy the machine was; the rows that are about a deadline hold the clock deliberately and say so in their recorded mode. Paired batches of ten through `Tools/measure-flake.sh`, on 2026-09-14 at `1a64ef0` with two other lanes building throughout: **10 of 10 green before the lift at load 2.74, and 10 of 10 green after it at load 3.58**, the second batch's rows recording `mode: in-suite; unbounded-allowances`. That settles nothing about the load hypothesis in either direction, and saying so is the point — ten green runs bound the failure rate only below 27.8 percent, which cannot tell a fixture that fails one run in four from one that never fails. The plan's pass line, thirty runs on an idle machine, was taken later that evening at `ca69344` with one dotnet process running: **30 of 30 green, failure bounded below 11.4 percent** (`Tools/Ledger/runs/ca69344-20260914-193027.jsonl`). That is a bound and not a cause. **Do not read "the allowances are lifted now" as "the flake is fixed": it was never observed firing under the lifted regime because it was never observed firing at all this session, so the batch cannot tell a flake the lift removed from one that was always rarer than a tenth.**

  What has changed is attribution rather than the rate. `sh Tools/verify.sh --rerun-red 5 --case "<name>"` now reruns a red and prints its interval, and the loop is exercised rather than assumed — `AIC_LEDGER_FORCE_RED=always` and `=flaky` drive it on demand through the ledger's own self-test. A red still stops the run whatever the reruns show, because the original fail row is a row.
- **Mod unload runs on a worker thread, and FNA3D refuses graphics calls there.** Disposing a texture in `Unload` throws `ThreadStateException: most FNA3D audio/graphics functions must be called on the main thread`, and tModLoader then reports the mod unable to unload and demands a restart (seen on 2026-09-08 after a reload, `client.log` 13:0x, from `CompanionHealthBar.Unload`). Anything graphics-side that must be released at unload goes through `Main.QueueMainThreadAction`.
- **In-game Build + Reload has died three times and survived twice on 2026-09-08, and the cause is open.** Every death ends the log at `Unloading: ModLoader` with every hook of ours having logged its unload; nothing of ours runs after that line, and the mod holds no hooks and no game-event subscriptions (searched). The notch's texture disposal was suspected and is refuted: the third death, at 13:41, ran with that fix in. Two signatures, from `terrariasteamclient.log`'s "connection closed" line against the last client line: the two morning deaths closed 0.6 s after it (an exit), the 13:41 one 65 s after it (a hang, then most likely a force-quit), and neither wrote a macOS crash report. The survivors (11:41, 13:23) each logged "AICompanion mod class still using memory", so the assembly context leaks on every unload. The fourth hang (17:37, the same last line) was sampled alive with macOS `sample <pid> 5 -file out.txt` (`dotnet-stack` hangs too, because the runtime's diagnostic thread is suspended with everything else): a thread-pool worker is inside the loader's own `GC.Collect` (the unload's memory check, `ModLoader.cs` WarnModsStillLoaded) with the collector's mark phase spinning in a handful of instructions for every sample of five seconds, and every other thread parked in the runtime's suspension wait. A marking loop that never ends is a corrupted object graph or a runtime fault, under Rosetta (the process is x86-64 translated); the mod has no unsafe code, only two bounds-checked texture uploads, so what our side contributes, if anything, is the leaked assembly context the survivors log. Until the cause is known, the shell build with the game closed and a fresh launch is the reliable route; the in-game build itself works (the `.tmod` is packaged before the unload starts), so a hang costs a force-quit and nothing else.
- **The key left of 1 can never reach the mod on a Mac ISO keyboard.** FNA logs `KEY/SCANCODE MISSING FROM SDL2->XNA DICTIONARY: SDL_SCANCODE_GRAVE` and drops the press before it becomes a key, so no keybind and no raw-key fallback sees it; a whole playtest on 2026-09-08 produced zero key lines. The overlay default is the left square bracket.
- **A saved keybind outranks its registered default.** The inspector honours the user's Mod Controls binding and has no hidden raw-key overrides. If an older saved binding names a key FNA drops, rebind it in Controls; changing the registration default cannot repair that saved value. Input for the inspector and the opening health-notch press is consumed before item use.
- **`WorldGen.GetTreeBottom` returns the ground tile under the trunk, not the lowest trunk tile.** Use `TreeFinder.TrunkBottom`.
- **`Main.DrawTileCracks` adds `offScreenRange`** unless `drawToScreen`; `TileCracksRenderer` cancels it.
- **`CheckActive` returns false**, so the companion is never culled for distance.
- **The health bar draws in raw screen pixels** because `Main.mouseX/Y` are screen pixels.
- **The engine-only `moved` column cannot see our AI-phase position writes.** `oldPosition = position` is assigned inside the engine immediately before `position += velocity`, so the telemetry's `moved` column spans the engine's own move and nothing our brain or motor wrote to `npc.position` beforehand — the walker's step-up helper once lifted the body back onto a platform it was descending for 265 ticks while reading as a body with a large velocity, no collision and no movement. The general rule: a reused game helper that takes a "the player is holding this" flag needs that flag computed per tick from the same intent a vanilla NPC computes it from, never hard-coded. The `pinned` column exists to catch the whole class without knowing which writer it is.
- **A tile that "has a solid tile" is not a wall.** Worldgen smooths cave corners into slopes and half blocks, the game's collision skips a slope from its open side and rests the body on its diagonal, and the fourth run of 2026-09-08 parked the companion for six thousand ticks above a staircase of five such slopes that the grid drew as `#`. Every tile question goes through `ITileWorld.Shape`, never `tileSolid` alone.

## What a reader will get wrong here

- **There is one body now, and a reader who remembers two will look for a divergence that cannot exist.** The walker had a real NPC under Terraria's collision beside a portable prediction under a shape approximation, and every second defect lived in the gap between them. The orb's contact is the mod's own circle-against-tiles test, run by the motor in the game and by every headless tool from the same source, with the engine's collision switched off for it; what a headless row proves is a property of the body the game moves, and the native suite adds only that the contact reads Terraria's tiles correctly.
- **The stand-in player in `Main.player` is not the companion.** It is a drawing-and-targeting device that is only active inside a hostile's AI call. Damage, life and death all belong to the NPC.
- **The mod's version in `build.txt` moves with the work and is not a release signal.** Nothing here has shipped to anyone.
- **A number in any of these folder files is either a fact of the world or a dated measurement.** The behaviour tunables are the `Weights` class in `Companion/Brain/Infrastructure/Selection/BehaviourWeights.cs` — the class and the file are not named the same thing, which is worth knowing before searching for `Weights.cs`, because there is no such file. A threshold quoted in prose anywhere else is a documentation defect rather than the current value.


## Current state — 2026-09-15

**The companion is a flying orb, and this is the first tree in which every part of it is one thing.** The night of 14 September retired the walking body: the second play of 0.25.0 showed the statue ledge still unreached and the companion looping in a water pocket, the diagnosis found that the run-up jump existed for the body and was never proposed because the candidate generator scanned four tiles across, and that was the seventh fix to one class, so the owner called the class rather than the fix. Three lanes built the replacement off `ced9923` and landed on the 15th: the body and its navigation as `806a998`, the gear and the item-backed weapon as `c94c797`, and the notch's three bars with the generated mastery wheel committed on main between them. 0.26.0, the version that carried all of it with the reach flood as a disc and the four weapon fixes the sentinels landed, was packaged at 09:19 on 15 September 2026 from `6a2e59e` and played once that morning (capture `2026-09-15_08-30-31-684`), and that play is why `build.txt` reads `0.27.0`. The orb stayed far back, moved in stop-start bursts, dodged well and did nothing else, and sat beside a zombie while the player walked away. The owner then ruled what the body is — every job unchanged, safety on top of the job rather than instead of it, never strictly still, eased and curving motion at the unchanged speed — and `52c0576` built that: a hover wherever the body is asked to be, a motor with separate turn and speed-change caps, an evade layer in place of combat spacing and the reflex takeover, and a jump envelope in the threat sense. 0.27.0 was packaged at 11:17 on 15 September 2026 with the game closed, from `efd5cca`, and its first play was recording from 11:27 that morning (capture `2026-09-15_10-27-40-531`, named in UTC). A sentinel's review of `52c0576` then found a stale wait anchor, a dodge that could fly into lava and an under-read walker jump in that package; `ed733ec` fixes all three on the tree, and `build.txt` reads `0.27.1` for them: the whole suite was green at `99c2631` with the game closed (102 rows, nothing red, committed as `65d1e9c`) and 0.27.1 was packaged at 12:14 that day from `1a23501`, not yet played.

```
the body        a twenty-pixel circle, no gravity, no breath, the engine's collision off, the mod's own
                circle contact in the motor; every liquid is air to it and to every search
navigation      corner nodes where the circle fits, a clearance field, one resumable flood that is also
                the reach sense, A* priced toward the corridor's middle, line-of-sight smoothing,
                momentum steering with a speed cap, a turn cap and a slower speed-change cap that eases
                into arrival, a hover wherever the body is asked to be, and an evade layer that bends any
                tick's flight away from a predicted hit
the hands       four slots (two weapons, a pickaxe, an axe) holding real items read for their numbers;
                one item-backed weapon that spawns or swings; free default ammo; mana mirroring the
                player's on a gradient that never refuses; arcs learned per projectile type from its shots
the notch       health, mana and experience with the level; the mastery wheel a generated four-lane preview
the record      schema 0.36.0, the orb's columns with a candidate funnel per searching activity and the player's own
                cursor as a reference; experience credited from attempt outcomes by attribution
```

Two properties of that shape are the ones a stranger has to know. **Any route with clearance is followable by construction**: the body is holonomic and every edge between two usable corners is swept clear, so there is no proof step between planning and moving and no class of "the planner proved it and the body could not", which is the property every one of the walker's negative captures lacked. And **the orb has one body**: the contact in the motor is the contact in every headless tool, from the same source, so a headless integration is integrating the body the game has and the walker's two-body parity measurement has no subject.

**What the suite says, on 15 September at `c94c797`**: `sh Tools/verify.sh` prints "96 rows: 59 pass, 1 red, 1 skipped, 35 measure, 0 sealed" and exits 1. The red was the recorded-route row: the world run replays the player's track from the water-pocket capture of 14 September through the saved world, and at `c94c797` the orb reached sixteen of the twenty places the player stood. The merge body, and the first version of this paragraph, read the instrument's "flood unfinished" row as the cause of the four misses; a sentinel then showed both halves of that to be the instrument's own artefact. The four "misses" were a body hovering within twenty pixels of being over the player and a hundred-odd above his feet, inside the box the brain itself admits, scored against a circle on his feet; and the "flood unfinished" row asked the reach sense about the solid tile under his feet, which no flood of free space can ever hold, so it printed the same answer for every checkpoint. Underneath that artefact sat a real defect: the reach flood ran to the search's node limit in an open world and finished without exhausting, so it could never complete and nothing could ever be proven unreachable. The flood is a disc around the body now (`Observation/CLAUDE.md`), the instrument measures to the player's body and asks about the tile his body occupied, and on the same route the orb reaches all twenty with its mean distance from the recorded track down from 146.6 px to 119.1 px; the gain is the flood completing and nothing else, because pricing the flood by clearance or not gave the same run to the pixel. No checkpoint is excused for the liquid it lies in, because every liquid is air to the orb. The skip is the session reader's before-numbers, whose capture declares the walker's schema. One behaviour change rode in with the body and every sight consumer inherited it: line of sight is the game's single-tile walk rather than its three-row beam, because a body one row above a floor never has the beam along that floor.

**The orb has been played twice, and the rewrite answered the stillness and not the distance.** The first orb capture, 08:30 on 15 September on 0.26.0, is pinned by the session reader's play measures (`Tools/SessionReport/Measures/CLAUDE.md`): the body sat still on 60.12% of the ticks the player moved, trailed at a median 164 px while he moved, and combat spacing owned 61.44% of its living ticks. The second, 11:27 the same day on 0.27.0, read still on 0.55% of player-moving ticks with no still run at all, a median offset of 10 px behind rather than 103, a speed-change 90th percentile of 0.16 rather than 0.48 and hunting that fired, but a median distance of 237 px while he moved, 196 px while only keeping company; README's Current Behaviour carries the split. That capture is unpinned. The walker's 13:27 capture of 14 September stays the track the orb is measured against headlessly. The acceptance play is unchanged — go right, drop all the way down, come back to the statue, and the orb should follow throughout and float up to light the dark area — with two things to watch that the first play made the point of it: whether the body ever reads as parked, and whether an enemy beside it still costs the player his companion.

**Every liquid is air to the orb, by the owner's ruling of 15 September 2026, and 0.28.1 is the first build that carries it.** Water, honey, lava and shimmer neither hurt, slow, transform nor block the body, and nothing takes it out of one: the planner's liquid wall, the motor's hurts and its honey and shimmer slowdowns, the evade layer's liquid mask and the environmental escape all went, with the escape's telemetry columns, its overlay layer and its fixtures, and the record moved to schema 0.36.0 for the removals. The engine contributed nothing to undo: its wet slowdown, lava strike and shimmer buff live in the NPC collision step it already skips for a body with tile collision off, which rests on the decompiled source because no headless tool runs the engine's NPC update. What liquid still means to the mod is about things other than the body — where a torch may go, where a drop lands, how an enemy or a shot moves — and those reads stay. `Tools/EngineReplay/Movement/VerifyLiquidsAreAir.cs` is the acceptance: every tick inside each liquid moves the air cruising step to within a hundredth of a pixel with life untouched, and a passage flooded with each liquid is where the route runs and the body flies; each of a liquid wall, a liquid strike and the engine's wet slowdown put back turns it red.

### What the walker established that still binds

The walker's folder guides were replaced rather than kept, so the properties they learned live here as properties rather than as the patches that expressed them; a design that comes back wearing new mechanics is refused by the property, not by a diff.

- **A fallback whose trigger is the absence of the ordinary path's precondition fires hardest while the planner is still thinking**, which is the moment it can do the most damage. Anything reviving a reactive layer answers which fact it fires on: that the planner failed, or merely that it has not finished yet.
- **A bound that ran out is a third value, not a refusal.** Optional work does not start on an unanswered search; only a completed sweep with nothing found is a proven absence. This is why every sense answers in three values, and why the reach flood's clock is the resolve rather than the tick.
- **Invalidation is spatial.** A retained search records what it read and is discarded only by an edit inside that; a caller that restarts on any edit anywhere starves under a digging player.
- **A destination is kept by membership, not by a bonus.** A held spot stays while it still belongs to the region it was admitted against, and no rival is asked to outscore it.
- **A live interference footprint must not suppress the intent region's lead.** A region pulled back onto a walking player's feet asks the companion to stand exactly where he is going; courtesy is answered where the body is chosen, never by blinding the sense that says where he is heading.
- **A speed discretised into a search node cannot carry the real speed accurately enough to perform what it proved**; soundness needs a finer quantisation than performance affords. The orb's search carries no speed because momentum steering follows any clear route, and the property returns the day a move depends on arrival speed.
- **Two timings are comparable only when taken the same way**: a fixture run alone pays JIT for the whole search path and the same fixture inside the suite does not, and a twelvefold difference was once credited to a change that had moved the worst tick by nothing.
- **A number quoted about a capture is a claim until an instrument reproduces it.** Twice a filter written over a file once was reported as a measurement and was wrong; anything asserted about a recording is reproducible through the session reader's measures or it is not yet evidence.

Not built, and named as such: the mastery wheel's effects (speed, acceleration and the dash; there is no liquid immunity to earn, because every liquid is air to the body from the start), chaining several jobs into one trip, boss and event conduct beyond stopping optional work, a fought retreat home, and the gear's refusal of a modded item with its own firing code, which no headless run can exercise. Also not built: any cue that leads a player to the four gear slots (the first orb play's gear was empty all session), what an unarmed companion treats as its job in a fight, and a world run with a hostile in it, which is why the freeze was found in play and not headlessly. Route search still treats a closed door as a wall. These checks establish the recorded fixtures, not reliable flight through every live cave or compatibility with every mod.
