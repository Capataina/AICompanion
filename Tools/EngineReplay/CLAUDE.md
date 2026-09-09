# EngineReplay — native NPC collision is the independent oracle

This console tool loads the installed tModLoader assembly without opening a game window. It compares SimulateTerrariaBody with Terraria’s own NPC collision wrapper; it does not compare two copies of the portable text-world simulator.

The occurrence fixture also instantiates the real player observer through ModPlayer.NewInstance and invokes OnHurt with surviving and fatal damage. It asserts the callback's pre-subtraction life and explicitly expected successor health. Player is a read-only view of the attached entity; assigning that property through reflection is not the native attachment lifecycle.

The personal-danger fixture runs the real threat observer against sealed native-tile chambers. It checks both player/companion arrangements for a walker, a tile-colliding flyer, a wall-crossing phaser, and entry into an enemy's chamber before a cached reachability refresh. These isolate destination coupling; they do not measure live combat judgement or a modded hostile's own pathfinding skill.

```
EngineReplay/
├─ CLAUDE.md                 setup, scope and evidence limits
├─ EngineReplay.csproj       compiles the movement core and native adapter against the installed game
├─ Program.cs               resolves the installed game’s library dependencies
├─ VerifyEngineMotion.cs     terrain/liquid matrix, scratch-state assertions and native route checks
├─ VerifyObservedMotion.cs   native-terrain hostile forecast and pure regroup-urgency contracts
├─ VerifyProjectileMotion.cs native projectile-AI and swept-shot contracts
├─ VerifyPersonalDanger.cs   separated chambers verify actor-specific hostile reachability
├─ GodsEyeTestStubs.cs       unrelated mod and TSV seams; the real player hurt observer remains compiled
└─ VerifyGodsEyeEvents.cs    real sparse-event writer, native-hook, generation and terrain-capture contracts
```

From the repository root run `dotnet run --project Tools/EngineReplay`. Exit zero requires every matrix entry to match and all native route fixtures to arrive. `sh Tools/verify.sh` includes this command. The game location can be supplied as the executable’s first argument; MSBuild’s TModLoaderRoot controls the reference location when compiling on another installation.

The setup assigns Terraria.Program.SavePath before Main’s static constructor, constructs a small Tilemap through its non-public constructor and marks the process dedicated-server for headless operation. Each comparison creates an ordinary NPC, runs its private gravity setup, applies shared controls and the step helpers, adds the engine’s gravity, then invokes private UpdateCollision. It fixes wetCount to suppress liquid entry/exit audiovisual effects. No game AI, enemy spawning, save loading or full NPC update is invoked.

The matrix covers floor and ceiling slope orientations, flat/sloped platforms, dry movement, water, honey, shimmer, liquid transitions, offsets, jump and fall-through controls, and both space-scaled and ordinary gravity. Position, velocity, wet state, liquid priority and stair state must agree; prediction must restore all seven Collision scratch fields. Native route cases cover flat travel, a two-tile ledge, staircase ascent and staircase descent. Their output reports recovery faults even when the destination is reached.

The same executable also verifies every projectile in the current companion kit against native `Projectile.VanillaAI` for a bounded free-flight run. It compares phase, gravity, drag, terminal velocity and default hitbox, then checks that a swept trace rejects a thin blocking tile, reopens when that tile is removed, and rejects an accuracy-rotated launch that no longer reaches its target. This establishes the solver's supported projectile profiles and collision sampling; it does not prove every possible modded projectile or a live combat playtest.

`VerifyObservedMotion` runs the shared target forecast against the same initialized Terraria tile map. It checks a stationary grounded hostile remains supported; a tile-colliding flyer stops at a wall while a phaser crosses it; observed acceleration changes the short forecast; a jump is not extrapolated as a repeated impulse; `Forget` removes a reused NPC slot's old track; and a position correction during the same engine tick replaces an already-built forecast. It compares dry custom gravity/fall cap and wet custom movement slowdown against native `UpdateCollision` exactly, then bounds the remaining slope/step-policy difference while requiring non-zero current gravity to travel through `SlopeCollision`. It also keeps the pure regroup pressure monotonic as separation, return time, player movement away and stalled travel grow, while a nearby stationary companion remains calm. The test snapshots Terraria's collision scratch flags around each forecast, because a target forecast that changes shared collision state can corrupt the movement prediction it is supposed to inform.

`VerifyGodsEyeEvents` compiles the actual sparse event writer and its native NPC, projectile and terrain hooks with only test-local telemetry and mod stubs. It writes and parses a temporary JSONL session, then deletes it. The fixture proves sequence/schema/timestamp validity, normal session closure, snapshot-at-occurrence behaviour, reused NPC/projectile slot generations, shot-to-terrain correlation through the first projectile generation, and tile dirtiness becoming a changed local terrain snapshot only after post-update sees the engine edit. Its terrain fixture includes a slope and water, checks the rolling initial capture, and requires the recorded local chunk to retain glyph, liquid and material fields.

These are repeatable collision and route fixtures. They do not establish general world navigation, threat prediction quality or comfortable companionship. The historical text-world corpus and a recorded playtest answer those different questions. Private engine method names are intentional verification dependencies: if a game update removes them, the test must fail visibly rather than silently substitute another simulator.
