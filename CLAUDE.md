# AICompanion — “Multi... Player?”

This singleplayer tModLoader mod makes an NPC companion that behaves as a second presence in Terraria: it keeps roughly with the player, fights, collects nearby drops, helps with work already underway, and lights dark places. It is deliberately neither a second player nor a pet. The companion’s abilities are a closed set rather than calls into real item use, so each ability is reliable and must be written explicitly.

## The companion is an opportunistic companion, not an orders system

Behaviours compete by score each tick: loose companionship, combat, gathering, survival and nearby work. Shooting is independent hands work, so it runs while the feet follow or dodge; the torch fills an otherwise free hand. Missions and player-directed sending were abandoned. The unbuilt mastery progression may later unlock movement abilities including air jumps, dash, swimming and flight; a selectable interface preview is not a gameplay ability.

## The architecture has one movement boundary

```
Terraria world ─► Brain/WorldObservation
                     │
                     ├─► CombatReflexes ─► SharedMovementSystem ─► Companion motor ─► NPC body
                     │
                     └─► BehaviourSelection ─► PositionSelection ─┘
                                  │
                                  └─► WorldInteractions and the hands
```

`Companion/Brain/CoordinateBrainTick.cs` owns the tick order. `Companion/Brain/SharedMovementSystem/CoordinateMovement.cs` is the single movement request interface; its query surface is how other brain systems ask movement questions. The motor is the only component permitted to apply resolved controls to the live NPC. The portable simulation, route planner and offline replay share the core; Terraria integration is an adapter outside that core. EngineReplay compares the native collision adapter with Terraria’s own NPC collision routine, including liquid transitions. The portable text-world simulation is a separate approximation. The live parity recorder checks each requested tick during gameplay, which still needs playtest evidence.

## Rulings that constrain every change

- This is singleplayer only: use `Main.LocalPlayer`; do not add netcode, server branches, `netUpdate`, or player iteration.
- The companion is an NPC. Its temporary stand-in player exists only while hostile AI runs so enemies can target it; life, death and movement belong to the NPC.
- It never teleports. Ordinary following can start continuous recovery flight when far from the live player; combat, work and downing cannot start it. A nearby sealed companion still uses ordinary movement and sealed-pocket handling. Recovery is outside route memory and separate from mastery flight.
- Decisions use multiplicative utility scoring, never priority branches or scenario-specific rules.
- Reuse a decompiled Terraria path when it is not gated on the local player. The mod reads game item numbers but never runs companion abilities through `Player.ItemCheck`.

## Map

```
AICompanion/
├─ Companion/                the complete companion gameplay subsystem
│  ├─ CharacterBody/         NPC lifecycle, rendering and breath
│  ├─ EnemyIntegration/      hostile targeting bridge and spawn-rate adjustment
│  ├─ Brain/                 observation, behaviour, movement and interactions
│  │  ├─ WorldObservation/      player, terrain, threat and activity facts
│  │  ├─ BehaviourSelection/    utility scoring and considerations
│  │  ├─ Behaviours/            choices grouped by purpose
│  │  │  ├─ Companionship/      follow, guard and wander
│  │  │  ├─ Combat/             hunt and kite
│  │  │  ├─ Gathering/          collect nearby drops
│  │  │  ├─ Survival/           seek safety for the companion
│  │  │  └─ Work/               help with trees and ore
│  │  ├─ PositionSelection/     position requests and candidate scoring
│  │  ├─ CombatReflexes/        immediate threat assessment
│  │  ├─ SharedMovementSystem/  shared travel, avoidance and control contracts
│  │  │  ├─ TerrainModel/       tile geometry and pass-through properties
│  │  │  ├─ BodySimulation/     body state and portable motion
│  │  │  ├─ MovementAbilities/  movement capabilities and resource transitions
│  │  │  ├─ RoutePlanning/      coarse routes, reachability and cache
│  │  │  ├─ MovementExecution/  actual-state validation and retained movement
│  │  │  └─ TerrariaIntegration/ native collision, terrain and live control adapter
│  │  ├─ WorldInteractions/     tools and environment interactions
│  │  │  ├─ Chopping/           tree discovery and axe use
│  │  │  ├─ Mining/             ore discovery and pickaxe use
│  │  │  ├─ Torch/              held light and supplied Smart Cursor placement
│  │  │  ├─ WorldProtection/    bed-anchored autonomous-edit boundaries
│  │  │  └─ Doors/              open a door in the route
│  │  ├─ ProjectileAiming/      shared projectile trajectory solver
│  │  └─ BehaviourDiagnostics/  overlay, timeline recording and scenario capture
│  ├─ Weapons/               companion equipment and arsenal choice
│  ├─ Inventory/             persistent cargo bag and panel
│  ├─ PlayerIntegration/     persistence, input, player events and /companion
│  ├─ ProfileCard/           native behaviour controls and cargo entry
│  ├─ DiagnosticsConfiguration/ native inspector and recording switches
│  ├─ MapIntegration/        map head and torch-limited reveal
│  └─ HeadsUpDisplay/        player-facing health notch
├─ Localization/             display strings
├─ Tools/                    headless replay, report, reshape and verification tools
│  ├─ NavReplay/             portable route replay and movement contract tests
│  ├─ EngineReplay/          native NPC collision and route acceptance tests
│  ├─ SessionReport/         evidence-based chronological session reader
│  ├─ WorldWindow/           saved-world restoration of capture geometry
│  └─ Scenarios/             committed terrain fixtures from play
├─ InterfaceExperiments/     selectable visual prototypes outside game code
├─ WeaponExperiments/        closed-kit weapon design prototypes
└─ Telemetry/                ignored runtime session records
```

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


## Current state — 2026-09-10

The companion has one repository home, including its brain, body, equipment, cargo and engine integrations. Navigation retains its search frontier across ticks while executing validated prefixes. Reachability can be reused when directed paths prove the body's new position belongs to the same returnable region; travel time remains relative to the actual search origin. Successfully executed ordinary traversals become directed world memory, with terrain fingerprints and physical-failure invalidation. Short control-sequence searches handle local clearance and escape towards a stable air target; route clearance proves its endpoint, while survival can retain intermediate progress through to a safe landing. Protection accounts for time to intervene, harmful enemies remain sensed even when unattackable, and the companion continues acting after the player dies. Hands clear each tick and ordinary torches hide while wet. Utility scoring and the independent shooting chain remain the architecture; the arsenal evaluates legal weapon-target pairs by useful damage, finishing and projected harm removed over bounded follow-up attacks.

Following uses horizontal and vertical comfort limits and distinguishes a usable destination from arrival at a route waypoint. Guard commitment tracks a relevant hostile generation through brief pressure dips. Distant-follow flight finishes only with a clear companion at the vertically settled owner's level or above; cancellation inside terrain continuously clears the body before normal collision resumes. The profile card exposes mining and chopping policies, voluntary hunting, pot breaking, supplied torch placement and distance profiles. Active jobs have a wider retention envelope than new jobs, and a completed worksite can retain its nearby loot. An ore job revalidates its approach from current feet, yields on unknown reach, and never excavates non-ore terrain. Bed-connected home regions veto automatic alterations, with a conservative vicinity fallback for unbounded spaces. Route archives use bounded byte arrays because native TagIO strings cannot safely hold a grown archive.

Optional observation combines continuous body/player samples with timestamped decision, movement, native projectile, damage and pickup events plus rolling local terrain snapshots. Search and execution identities, intervention estimates, local control ownership and bounded position/target alternatives explain the decision chain. SessionReport analyses multiple runs and produces an interactive HTML timeline with raw sample fields, event payloads and explicit capture/sampling coverage. The visual brain inspector displays selectable retained evidence from the actual solvers; tModLoader configuration independently controls the inspector and local recording. Neither can reconstruct uncaptured terrain or predict an enemy's unobserved future script.

Verification on 2026-09-10 matched all 2,032 native collision comparisons. Full-brain survival checks require sustained native head clearance while preserving life, including the captured pool and both awning orientations. They can incur drowning damage before escape and therefore do not establish breath-feasible routing. Older isolated escape timings did not cover the full brain's control handoffs or continued safe landing. Native following checks cover vertical-only travel intent, stopping tolerance at the follow comfort boundary, a closed-door player-side destination and a C-turn whose first movement goes away from the player. Recovery checks cover continuous flight, independent targeting during flight, downing inside a thick wall and no route-memory learning. The portable historical corpus remains non-green, with partial routes, skipped inputs and model-closed regions that do not prove physical impossibility. These checks establish the recorded fixtures, not reliable movement through every live cave or compatibility with every mod.

The actual native Profile, Cargo, Mastery and Inspector pages are rendered offscreen at several viewport sizes by `Tools/EngineReplay --render-ui`, with real policy-button events and a pixel-level debug-line check. Cargo uses native item slots inside the profile; mastery remains a selectable preview without stat effects or costs. Live portrait animation, item transfers and the feel of exploration still need a recorded playtest. Symptom narration is welcome, and the remaining navigation cases stay open on the roadmap.
