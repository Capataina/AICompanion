# AICompanion — “Multi... Player?”

This singleplayer tModLoader mod makes an NPC companion that behaves as a second presence in Terraria: it keeps roughly with the player, fights, collects nearby drops, helps with work already underway, and lights dark places. It is deliberately neither a second player nor a pet. The companion’s abilities are a closed set rather than calls into real item use, so each ability is reliable and must be written explicitly.

**What the companion is supposed to do lives in `README.md`, and reading it is the first move on any behaviour work.** That file carries four things in the order they have to be read: Expected Behaviour, a half-hour of play written as a story with no reference to any system; Current Behaviour, what it actually does, sourced only from named telemetry sessions; The System In Place, the machinery read from source; and a table of named responsibilities carrying all three per row. Each section opens with its own rules for maintaining it. This guide describes how the code is arranged; that file describes what it is for, and the two disagree only when one of them is stale.

## The companion is an opportunistic companion, not an orders system

Behaviours compete by score each tick for companionship, combat, gathering and nearby work. Shared safety can suspend ordinary activity to escape environmental exposure or avoid a collision. Shooting is independent hands work, so it runs while the feet follow or dodge; the torch fills an otherwise free hand. Missions and player-directed sending were abandoned. The unbuilt mastery progression may later unlock movement abilities including air jumps, dash, swimming and flight; a selectable interface preview is not a gameplay ability.

## The architecture has one movement boundary

```
Terraria world ─► Brain/Infrastructure/Observation
                     │
                     ├─► SharedBehaviours/Safety ─► Infrastructure/Movement ─► motor ─► NPC
                     └─► Activities ─► Infrastructure/Selection ─► Infrastructure/Position ─┘
                                  │
                                  └─► Infrastructure/Interactions and Companion/Weapons
```

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
│  │     ├─ Observation/    player, terrain, threat and activity facts
│  │     ├─ Selection/      utility scoring and tunables
│  │     ├─ Position/       position requests and candidate scoring
│  │     ├─ Movement/       travel, avoidance and the motor
│  │     ├─ Interactions/   chop, mine, torch, doors, homes
│  │     ├─ Aiming/         projectile trajectory solver
│  │     ├─ Grants/         one packet for feet and hand
│  │     └─ Diagnostics/    overlay, telemetry, scenario capture
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
│  ├─ EngineReplay/          native fixtures by Combat, Gathering, Assistance, Movement, Observation, Lifecycle
│  ├─ SessionReport/         chronological reader (Read, Checks, Write, Tests)
│  ├─ WorldWindow/           saved-world restoration of capture geometry
│  └─ Scenarios/             committed terrain fixtures from play
├─ InterfaceExperiments/     selectable visual prototypes outside game code
├─ WeaponExperiments/        closed-kit weapon design prototypes
├─ research/                architectural questions, evidence and trade-offs for discussion
└─ Telemetry/                ignored runtime session records
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

Read a playtest with `dotnet run --project Tools/SessionReport -- Telemetry`; replay and world-window procedures live in their respective tool folders.

## Traps

- **The shell build says "Build succeeded" in under two seconds.** That is real; check the DLL timestamp before trusting it.
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
- **A number in any of these folder files is either a fact of the world or a dated measurement.** Tunables live in `Weights.cs`, and a threshold quoted in prose anywhere else is a documentation defect rather than the current value.


## Current state — 2026-09-13

Proposal 1 is in source. Three purpose families — Gathering, Combat, NearbyAssistance — each offer their best of seven behaviours; the parent compares those three offers with the same scoring machinery. Survival and independent local-exploration children are gone; shared safety owns environmental escape, collision avoidance and combat space, and keeping company is an ordinary offer rather than a leftover after everything else vanishes. Empty `Behaviours/{Combat,Companionship,Gathering,Survival}` husks are deleted. The README System In Place is a source-verified tick trace, family table and shared-system list. EngineReplay and SessionReport sit in folders by what they ask.

`sh Tools/verify.sh` exits 0: 2536/2536 native NPC collision matches, family-offer fixtures (flat match, empty family, deferred child, absent offers), and the rest of the default native suite. G1 (two-tile pillar-top hop) is asserted as a known limitation. Schema 0.30.0 joins decision, activity and attempt identities; a repeated failed method with no credited effect is a SessionReport finding, and a hunt already refused as no reachable firing position is not reported as the hands refusing to shoot.

Utility scoring and the independent shooting chain remain the architecture. Hunting still scores below proven work, an enemy with no reachable firing position is not hunted, jumps are proven from the take-off the body actually reaches, and ore work never excavates ordinary terrain. CombatReflexes still only supplies a collision predicate; they never write the NPC. The packaged playable build is `0.22.41`. The brain is Activities, SharedBehaviours (Safety, Recovery) and Infrastructure. Weapons stay at Companion/Weapons.

What still needs a person: a recorded playtest of this packaged build (Current Behaviour is still the 11 September caves), owner look at the family/activity notch, and slope walking (AIC-212). Not built, and named as such: mastery movement abilities, chaining several jobs (Proposal 3), a projectile dodge that jumps. Route search still treats a closed door as a wall. The portable historical corpus remains non-green. These checks establish the recorded fixtures, not reliable movement through every live cave or compatibility with every mod.
