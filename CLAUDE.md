# AICompanion — “Multi... Player?”

This singleplayer tModLoader mod makes an NPC companion that behaves as a second presence in Terraria: it keeps roughly with the player, fights, collects nearby drops, helps with work already underway, and lights dark places. It is deliberately neither a second player nor a pet. The companion’s abilities are a closed set rather than calls into real item use, so each ability is reliable and must be written explicitly.

**What the companion is supposed to do lives in `README.md`, and reading it is the first move on any behaviour work.** That file carries four things in the order they have to be read: Expected Behaviour, a half-hour of play written as a story with no reference to any system; Current Behaviour, what it actually does, sourced only from named telemetry sessions; The System In Place, the machinery read from source; and a table of named responsibilities carrying all three per row. Each section opens with its own rules for maintaining it. This guide describes how the code is arranged; that file describes what it is for, and the two disagree only when one of them is stale.

**A play behaviour we want to see, said in a session, is not a note to remember later.** If it is not already in Expected Behaviour, it goes into `README.md` in the same sitting: a row in Behaviour By Behaviour first, then a scene in the story, in the words a player would use. Nothing in that write-up may name a score, a family, a search or a mode. Prefer thickening a scene that already holds the collision over adding a timestamp. A wish that stays in chat is how the companion we described fails to be the one we build.

## The companion is an opportunistic companion, not an orders system

Behaviours compete by score each tick for companionship, combat, gathering and nearby work. Shared safety can suspend ordinary activity to escape environmental exposure or avoid a collision. Shooting is independent hands work, so it runs while the feet follow or dodge; the torch fills an otherwise free hand. Missions and player-directed sending were abandoned.

The unbuilt mastery progression is stats, weapon unlocks and movement unlocks, and **nothing on it changes what the brain decides**. That is a ruling rather than a description of what is built: a behaviour sitting behind an upgrade multiplies the numbers to tune and reads to a player as a weird gate, so an upgrade may change what a move costs or what the world does to the companion, never whether an activity is available to choose. Where a mastery node and the brain do interact, the interaction is expected to emerge through the senses rather than be scripted — a torch hat brightens the companion's surroundings, so the light sense reports a lit neighbourhood and the torch decision sees no reason to hold one, with nothing in the torch code aware that a hat exists. A selectable interface preview is not a gameplay ability.

## The architecture has one movement boundary

```
Terraria world ─► Brain/Infrastructure/Observation
                     │   player · terrain · threats · light · reach
                     │
                     ├─► SharedBehaviours/Safety ─► Infrastructure/Movement ─► motor ─► NPC
                     └─► Activities ─► Infrastructure/Selection ─► Infrastructure/Position ─┘
                                  │
                                  └─► Infrastructure/Interactions and Companion/Weapons
```

**A fact the whole brain needs is a sense, not a private answer.** Light and reach are the two that were extracted, and they are the pattern for the next one. Reachability used to be the positioner's own: everyone else either asked the positioner for the one shape it happened to expose or ran a route search of their own, so five callers each paid a fresh bounded search for a question one flood had already answered. Both now sit in Observation beside the threat sense, and the positioner is a consumer like everybody else — it keeps its whole public surface as one-line delegations, deliberately, because renaming it would have been a sweep through files for no behaviour.

Both senses answer in three values rather than two, and the middle one is the reason they are worth having. A tile missing from a flood that has not finished is *not yet known*, not *absent*; a place the engine has not lit is *unread*, not *bright*. An activity that refuses the middle answer is declining to start on an unanswered search rather than asserting the world is empty, which is a distinction each caller used to have to reconstruct and now cannot get wrong. The two senses keep different clocks and this bites: the reach flood's unit of time is the rescore, not the game tick, because it is bounded per advance and grows across successive resolves, so ageing it on the tick starves it.

`Companion/Brain/CoordinateBrainTick.cs` owns the tick order. `Companion/Brain/Infrastructure/Movement/CoordinateMovement.cs` is the single movement request interface; its query surface is how other brain systems ask movement questions. The motor is the only component permitted to apply resolved controls to the live NPC. The portable simulation, route planner and offline replay share the core; Terraria integration is an adapter outside that core. EngineReplay compares the native collision adapter with Terraria’s own NPC collision routine, including liquid transitions. The portable text-world simulation is a separate approximation. The live parity recorder checks each requested tick during gameplay, which still needs playtest evidence.

## Rulings that constrain every change

- This is singleplayer only: use `Main.LocalPlayer`; do not add netcode, server branches, `netUpdate`, or player iteration.
- The companion is an NPC. Its temporary stand-in player exists only while hostile AI runs so enemies can target it; life, death and movement belong to the NPC.
- It never teleports. Ordinary following can start continuous recovery flight when far from the live player; combat, work and downing cannot start it. A nearby sealed companion still uses ordinary movement and sealed-pocket handling. Recovery is outside route memory and separate from mastery flight.
- Decisions use multiplicative utility scoring, never priority branches or scenario-specific rules. This ruling describes what is built and is deliberately open for re-argument as of 2026-09-11: whether utility scoring is the right brain at all, and whether A* over a tile graph is the right route search, are both questions the owner has opened rather than settled ones. Treat it as the current design to work against, not as a boundary on what may be proposed.
- Reuse a decompiled Terraria path when it is not gated on the local player. The mod reads game item numbers but never runs companion abilities through `Player.ItemCheck`.

## Map

```
AICompanion/
├─ Companion/                the complete companion gameplay subsystem
│  ├─ CharacterBody/         NPC lifecycle, rendering and breath
│  ├─ EnemyIntegration/      hostile targeting bridge and spawn-rate adjustment
│  ├─ Brain/                 observe, choose, request movement
│  │  ├─ Activities/         the seven jobs and their shared contract
│  │  │  ├─ Combat/         guarding and hunting
│  │  │  ├─ Gathering/      mining and chopping
│  │  │  └─ NearbyAssistance/ lighting, collection and keeping company
│  │  ├─ SharedBehaviours/   can take the body without winning a family
│  │  │  ├─ Safety/         escape, dodge, combat space
│  │  │  └─ Recovery/       distant flight home
│  │  └─ Infrastructure/     how those get done
│  │     ├─ Observation/    player, terrain, threat and activity facts, and the light and reach senses
│  │     ├─ Selection/      utility scoring and the behaviour tunables
│  │     ├─ Position/       position requests and candidate scoring
│  │     ├─ Movement/       travel, avoidance and the motor
│  │     │  ├─ RoutePlanning/      the tile graph and its A* search
│  │     │  ├─ MovementExecution/  the navigator, traversals and guards
│  │     │  ├─ MovementAbilities/  which moves the body is allowed to offer
│  │     │  ├─ BodySimulation/     the portable body and its physics
│  │     │  ├─ TerrainModel/       the tile world every search reads
│  │     │  └─ TerrariaIntegration/ the engine adapter outside the portable core
│  │     ├─ Interactions/   chop, mine, torch, doors, homes
│  │     │  ├─ Chopping/          trees and their trunks
│  │     │  ├─ Mining/            ore tiles through the game's own PickTile
│  │     │  ├─ Torch/             carrying and placing light
│  │     │  ├─ Doors/             opening what the route treats as a wall
│  │     │  └─ WorldProtection/   what autonomous edits may not touch
│  │     ├─ Aiming/         projectile trajectory solver
│  │     ├─ Grants/         one packet for feet and hand
│  │     └─ Diagnostics/    overlay layers, cost strip, telemetry, scenario capture
│  ├─ Weapons/               companion equipment and arsenal choice
│  ├─ Inventory/             persistent cargo bag and panel
│  ├─ PlayerIntegration/     persistence, input, player events and /companion
│  ├─ ProfileCard/           native behaviour controls, inventory and mastery pages
│  ├─ DiagnosticsConfiguration/ native inspector and recording switches
│  ├─ MapIntegration/        map head and torch-limited reveal
│  └─ HeadsUpDisplay/        player-facing health notch
├─ Localization/             display strings
├─ Tools/                    headless replay, report, reshape and verification tools
│  ├─ NavReplay/             portable route replay and movement contract tests
│  ├─ EngineReplay/          native fixtures, in folders by what they ask
│  │  ├─ Combat/             guarding, hunting, firing and encounter context
│  │  ├─ Gathering/          ore and tree work, and work accounting
│  │  ├─ Assistance/         lighting, collection, keeping company, the light and reach senses
│  │  ├─ Movement/           engine motion, following, mislandings, projectiles
│  │  ├─ Observation/        the senses' lifecycle, family offers, evidence scenes
│  │  └─ Lifecycle/          spawn, death, preferences, the HUD and the native card
│  ├─ SessionReport/         chronological reader
│  │  ├─ Read/               the chronicle and the god's-eye events
│  │  ├─ Checks/             the findings graded against the record
│  │  ├─ Write/              the rendered report
│  │  └─ Tests/              the reader's own self-test
│  ├─ WorldWindow/           saved-world restoration of capture geometry
│  ├─ Scenarios/             committed terrain fixtures from play
│  └─ Decompiled/            gitignored; game source written on demand by Tools/decompile.sh
├─ InterfaceExperiments/     selectable visual prototypes outside game code
├─ WeaponExperiments/        closed-kit weapon design prototypes
├─ research/                architectural questions, evidence and trade-offs for discussion
│  ├─ Decision Architecture/       what chooses, when it commits, what it costs
│  ├─ Navigation Research/         how platformer bodies are routed elsewhere
│  ├─ Evaluation and Observability/ what to measure and what the recorder must carry
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

It builds without packaging, proves the DLL is newer than every source file that feeds it rather than trusting "Build succeeded", runs the navigation boundary, then the NavReplay and SessionReport self-tests, then the native engine suite. Exit 0 requires all of them; exit 2 means the boundary alone could not be asked because ripgrep is absent, which is neither a pass nor a violation. **Read the last line, not only the exit code, and see the trap below about what exit 0 does and does not currently mean.**

Read a playtest with `dotnet run --project Tools/SessionReport -- Telemetry`; replay and world-window procedures live in their respective tool folders. `Telemetry/` is gitignored, so a fresh clone has nothing to read until a playtest writes one.

## Traps

- **The shell build says "Build succeeded" in under two seconds.** That is real; check the DLL timestamp before trusting it. `Tools/verify.sh` already checks this for you — it refuses a DLL older than any source file feeding it — so the trap bites hardest when building by hand.
- **`sh Tools/verify.sh` is not reliably green, and a single green run is not evidence that it is.** Measured 2026-09-14 on this machine at effd5e9, five consecutive runs: three exited 0, two exited 1. Both failures were the same fixture and the same assertion, `VerifyOreWork.RaisedLipsAtBothGravitiesProduceWork` at `Tools/EngineReplay/Gathering/VerifyOreWork.cs:428`, "raised lip must produce a native ore break", and both were the `mirrored=True, mode=HeldActivity` case — but at different gravities on the two runs, floor 90 on one and floor 60 on the other, so the failing case is not fixed either. Nothing in the tree changed between the runs. The practical consequence is the one that matters when you are reading a commit body or writing one: a commit that cites "verify.sh exits 0" has about a three-in-five chance of saying that after one run whether or not its change is sound, so a green here confirms much less than the citation implies, and a red on this fixture alone is probably not yours. Anything depending on this suite needs a batch of runs sized to how rarely the flake fires, not one.
- **Mod unload runs on a worker thread, and FNA3D refuses graphics calls there.** Disposing a texture in `Unload` throws `ThreadStateException: most FNA3D audio/graphics functions must be called on the main thread`, and tModLoader then reports the mod unable to unload and demands a restart (seen on 2026-09-08 after a reload, `client.log` 13:0x, from `CompanionHealthBar.Unload`). Anything graphics-side that must be released at unload goes through `Main.QueueMainThreadAction`.
- **In-game Build + Reload has died three times and survived twice on 2026-09-08, and the cause is open.** Every death ends the log at `Unloading: ModLoader` with every hook of ours having logged its unload; nothing of ours runs after that line, and the mod holds no hooks and no game-event subscriptions (searched). The notch's texture disposal was suspected and is refuted: the third death, at 13:41, ran with that fix in. Two signatures, from `terrariasteamclient.log`'s "connection closed" line against the last client line: the two morning deaths closed 0.6 s after it (an exit), the 13:41 one 65 s after it (a hang, then most likely a force-quit), and neither wrote a macOS crash report. The survivors (11:41, 13:23) each logged "AICompanion mod class still using memory", so the assembly context leaks on every unload. The fourth hang (17:37, the same last line) was sampled alive with macOS `sample <pid> 5 -file out.txt` (`dotnet-stack` hangs too, because the runtime's diagnostic thread is suspended with everything else): a thread-pool worker is inside the loader's own `GC.Collect` (the unload's memory check, `ModLoader.cs` WarnModsStillLoaded) with the collector's mark phase spinning in a handful of instructions for every sample of five seconds, and every other thread parked in the runtime's suspension wait. A marking loop that never ends is a corrupted object graph or a runtime fault, under Rosetta (the process is x86-64 translated); the mod has no unsafe code, only two bounds-checked texture uploads, so what our side contributes, if anything, is the leaked assembly context the survivors log. Until the cause is known, the shell build with the game closed and a fresh launch is the reliable route; the in-game build itself works (the `.tmod` is packaged before the unload starts), so a hang costs a force-quit and nothing else.
- **The key left of 1 can never reach the mod on a Mac ISO keyboard.** FNA logs `KEY/SCANCODE MISSING FROM SDL2->XNA DICTIONARY: SDL_SCANCODE_GRAVE` and drops the press before it becomes a key, so no keybind and no raw-key fallback sees it; a whole playtest on 2026-09-08 produced zero key lines. The overlay default is the left square bracket.
- **A saved keybind outranks its registered default.** The inspector honours the user's Mod Controls binding and has no hidden raw-key overrides. If an older saved binding names a key FNA drops, rebind it in Controls; changing the registration default cannot repair that saved value. Input for the inspector and the opening health-notch press is consumed before item use.
- **`WorldGen.GetTreeBottom` returns the ground tile under the trunk, not the lowest trunk tile.** Use `TreeFinder.TrunkBottom`.
- **The player renderer draws the held item from `lastVisualizedSelectedItem`**, which only `Player.Update` sets; `CompanionBody.Sync` assigns it by hand.
- **The player renderer expects a closed sprite batch**; `CompanionNPC.PreDraw` closes and reopens the NPC batch around it.
- **`Main.DrawTileCracks` adds `offScreenRange`** unless `drawToScreen`; `TileCracksRenderer` cancels it.
- **`CheckActive` returns false**, so the companion is never culled for distance.
- **The health bar draws in raw screen pixels** because `Main.mouseX/Y` are screen pixels.
- **The engine-only `moved` column cannot see our AI-phase position writes.** The separate AI-entry observation captures the resulting position on the next tick. `oldPosition = position` is assigned inside the engine's `Collision_MoveWhileDry` immediately before `position += velocity`, so the telemetry's `moved` column spans the engine's own move and nothing our brain, motor or a `Collision.*` helper did beforehand. `Collision.StepUp` writes position by reference and never touches `velocity.Y`, and calling it with `holdsMatching: true` on every tick of a descent made the companion climb the platform it was falling through for 265 ticks while reading as a body with a large velocity, no collision and no movement. The general rule: a reused game helper that takes a "the player is holding this" flag needs that flag computed per tick from the same intent a vanilla NPC computes it from, never hard-coded — the town NPC recomputes it from whether it is above its home, the fighter from whether its target is below. The `pinned` column exists to catch the whole class without knowing which writer it is.
- **A tile that "has a solid tile" is not a wall.** Worldgen smooths cave corners into slopes and half blocks, the game's collision skips a slope from its open side and rests the body on its diagonal, and the fourth run of 2026-09-08 parked the companion for six thousand ticks above a staircase of five such slopes that the grid drew as `#`. Every tile question goes through `ITileWorld.Shape`, never `tileSolid` alone.

## What a reader will get wrong here

- **The two bodies are the point of failure, not a curiosity.** The real NPC and the live prediction use Terraria collision, while portable replay uses a shape approximation. Shared controls alone do not prove the two backends agree. Reading either one as "the" body is how a fix gets built for the half that was not broken; a divergence measurement runs every tick precisely because this keeps happening.
- **The stand-in player in `Main.player` is not the companion.** It is a drawing-and-targeting device that is only active inside a hostile's AI call. Damage, life and death all belong to the NPC.
- **The mod's version in `build.txt` moves with the work and is not a release signal.** Nothing here has shipped to anyone.
- **A number in any of these folder files is either a fact of the world or a dated measurement.** The behaviour tunables are the `Weights` class in `Companion/Brain/Infrastructure/Selection/BehaviourWeights.cs` — the class and the file are not named the same thing, which is worth knowing before searching for `Weights.cs`, because there is no such file. A threshold quoted in prose anywhere else is a documentation defect rather than the current value.


## Current state — 2026-09-14

The week since `9ca5ae4` did four things, and they are worth separating because only one of them is a feature:

```
a fact became shared      light and reach left their owners and became senses
a job became a job        lighting works a dark region instead of visiting a site
a rule was deleted        the walk stopped raising its own jump; the reactive floor is gone
a design was refused      the entry-speed graph was built, measured in full, and not shipped
```

Proposal 1 is in source. Three purpose families — Gathering, Combat, NearbyAssistance — each offer their best of seven behaviours; the parent compares those three offers with the same scoring machinery. Survival and independent local-exploration children are gone; shared safety owns environmental escape, collision avoidance and combat space, and keeping company is an ordinary offer rather than a leftover after everything else vanishes. Empty `Behaviours/{Combat,Companionship,Gathering,Survival}` husks are deleted. The README System In Place is a tick trace, family table and shared-system list verified against source — but verified at `9ca5ae4`, and deliberately left untouched since, because the senses and the movement changes were being built while it was written. It is accurate for 0.22.46 as packaged and stale against source until it is rewritten. EngineReplay and SessionReport sit in folders by what they ask.

`sh Tools/verify.sh` covers the native NPC collision match, the family-offer fixtures (flat match, empty family, deferred child, absent offers) and the rest of the default native suite, and **it is currently intermittent rather than green** — three of five runs measured on 2026-09-14 at `effd5e9`, with both failures on one ore-work fixture; the trap above carries the signature and what it means for reading a commit body's evidence. G1 (two-tile pillar-top hop) is asserted as a known limitation. Schema 0.31.0 joins decision, activity and attempt identities and carries light and reachability as two senses every consumer reads — `dark_near`, `dark_ahead`, `torch_reason`, `light_region`, `reach_any`, `reach_two_way`, `reach_complete` in place of the retired ambient scalar; a repeated failed method with no credited effect is a SessionReport finding, and a hunt already refused as no reachable firing position is not reported as the hands refusing to shoot.

Utility scoring and the independent shooting chain remain the architecture. Hunting still scores below proven work, an enemy with no reachable firing position is not hunted, jumps are proven from the take-off the body actually reaches, and ore work never excavates ordinary terrain. CombatReflexes still only supplies a collision predicate; they never write the NPC.

Navigation lost two things this week and gained no replacement for either, which is the point. The walk no longer raises its own jump: that jump never fired for the reason it was written, because a walk edge cannot contain a wall or a two-tile rise by construction, and what actually triggered it was measuring the rise from the live body's bottom rather than the step's proven feet — so every floor slope in a worldgen cave read as a two-tile rise. It was a stop dressed as a shortcut, landing slower than the walk it replaced. The reactive floor is deleted too, and the honest reason is that its output was computed on exactly the ticks it was not consumed: its two guard conditions were exact complements fourteen lines apart, so every control it ever produced was discarded. The property to keep from it, because a reactive layer will be proposed again: **a fallback whose trigger is the absence of the ordinary path's precondition fires hardest while the planner is still thinking, which is the moment it can do the most damage.** Anything reviving one answers what fact it fires on, and whether that fact is "the planner failed" or merely "the planner has not finished yet".

The entry-speed graph is the refusal worth reading before anyone proposes it again. Putting the body's arrival speed in the node key does plan better — a model-sealed block became a proven route — and it costs 3.9x the planning time, but the cost is not why it died. It walks much worse: nineteen more routes never arrive and mislandings go up five times, because **a class stands for one speed while the body arriving has a real speed somewhere inside that class's band, and a steered move carries that difference through every tick it runs.** Rounding the other way trades overshoot for undershoot and mislands just as readily, so the constraint is arithmetic rather than a matter of picking better boundaries: soundness needs a finer quantisation than performance can afford. That is a property of discretising speed in the node key, not of this implementation. The question to answer before trying again is how a speed-sensitive move gets a landing tile it can keep. `Movement/RoutePlanning/CLAUDE.md` and the research notes carry the measurements.

The last packaged playtest is `0.22.46`, and its verdict is negative: the 18:56 capture of 2026-09-13 is the newest recording, and it is bad on lighting, following and speed — the torch never lowers, lighting nominates no site on the overwhelming majority of ticks and places no torch, and the body stops about seventy times a minute of travel.

**`build.txt` reads `0.23.0`, packaged on 14 September 2026 and not yet played**, so the version now separates the two halves of the tree that 0.22.46 could not: everything the 18:56 capture condemned was in 0.22.46, and everything that answered it is in 0.23.0 and evidenced by fixtures alone until the next capture. The split:

```
in 0.22.46, the played package, condemned by 18:56
├─ far reunion pull never sitting at 1, so a proven job further on screen can still win
├─ the bounded lighting search
└─ hunt existence as the arsenal's ForecastAttack

in 0.23.0, packaged and unplayed
├─ light as a field with every carried light discounted, and reach as a sense
├─ lighting working a dark region and chaining its sites
├─ the walk no longer raising its own jump
├─ the reactive floor's deletion
├─ the four overlay layers and the cost strip
└─ whole journeys and every stop in the record, with the player's own time beside them
```

Everything in the second group is evidenced by fixtures alone. Taking the first group's list in full: far reunion pull never sits at 1 so a proven job further on screen can still win (hard leash at fly-home is not that cap); lighting walks to the nearest dark region the light field found from the companion's own feet, bounded to the work radius around the player, and keeps the job after each torch so a region is worked rather than visited; the torch reads that same field around the body and at the player's predicted feet, so it is up for dark air ahead and out in a lit room; hunt existence is the arsenal's `ForecastAttack`, and a solvable stand the flood has not claimed is already a hunt. Optional work does not start on an unanswered search — mining and hunting publish Unknown at value zero rather than walking at it; companionship and safety may still walk toward the player or a refuge. A collect pose is on the drop's floor inside pickup reach minus arrival slack. Follow heading box lerps and grows, fly-home sits at recovery distance, reunion slopes from the comfort box toward that distance, local hunt is not an outing, live walk and jump track the player's stats, and debug layers default on. The brain is Activities, SharedBehaviours (Safety, Recovery) and Infrastructure. Weapons stay at Companion/Weapons.

Four 13 September recordings back README's Current Behaviour, newest first: 18:56 on packaged 0.22.46, 16:32 on 0.22.45 as its cross-check, and 10:57 and 11:06 on 0.22.41, where chopping is Mimic by the in-game setting. `Telemetry/` is gitignored, so which of them a given clone actually holds varies, and README is the durable account of what they showed.

The inspector grew four overlay layers saying what the companion means to do, plus a cost strip reading what the thinking cost, one column per tick; no appended layer can inherit another layer's toggle, and the meeting marker holds its size at every zoom. Planner time is now reported per route rather than as a run total, because a total cannot say whether a change spread its cost or built one expensive search. One measurement rule came out of that work and generalises past it: **any timing from this suite is comparable only to another taken the same way** — a fixture run alone through its own flag pays JIT compilation for the whole route-search path on its first use, where the same fixture inside the default suite does not, and a twelvefold difference was once credited to a change that had in fact moved the worst tick by nothing. Expected Behaviour now includes overtaking, a comfortable space around the player whose pull grows with distance, danger that depends on how a creature actually arrives, choosing a place by how much work sits there, and — when two things will both land — preferring the hit it would rather take, including a tap-then-switch. Time to kill stays in the thought process for that sequencing and is not in danger or targeting. Slope walking (AIC-212) is still open. Not built, and named as such: mastery movement abilities, chaining several jobs (Proposal 3), combat sequencing of more than one body, a projectile dodge that jumps. Route search still treats a closed door as a wall. The portable historical corpus remains non-green. These checks establish the recorded fixtures, not reliable movement through every live cave or compatibility with every mod.
