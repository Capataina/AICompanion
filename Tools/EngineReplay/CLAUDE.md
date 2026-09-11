# EngineReplay — native NPC collision is the independent oracle

This console tool loads the installed tModLoader assembly without opening a game window. It compares SimulateTerrariaBody with Terraria’s own NPC collision wrapper; it does not compare two copies of the portable text-world simulator.

The occurrence fixture also instantiates the real player observer through ModPlayer.NewInstance and invokes OnHurt with surviving and fatal damage. It asserts the callback's pre-subtraction life and explicitly expected successor health. Player is a read-only view of the attached entity; assigning that property through reflection is not the native attachment lifecycle.

The personal-danger fixture runs the real threat observer against sealed native-tile chambers. It checks both player/companion arrangements for a walker, a tile-colliding flyer, a wall-crossing phaser, and entry into an enemy's chamber before a cached reachability refresh. These isolate destination coupling; they do not measure live combat judgement or a modded hostile's own pathfinding skill.

```
EngineReplay/
├─ CLAUDE.md                 setup, scope and evidence limits
├─ EngineReplay.csproj       compiles the movement core and native adapter against the installed game
├─ Program.cs               resolves the installed game’s library dependencies
├─ RenderNativeInterface.cs  hidden native graphics render, production controls and debug-line pixel regression
├─ ReplayRecordedWater.cs    event-terrain reconstruction with native full-brain water replay and coverage limits
├─ VerifyAttackOutcomes.cs   threat removal, overkill, multi-hit and follow-up attack valuation
├─ VerifyHuntProgress.cs     ineffective engagements defer without cancelling productive travel, and an alternating pick still defers
├─ VerifyFiringPosition.cs   the solve shortlist carries line of sight, so the chosen spot has an arc
├─ VerifyEngineMotion.cs     terrain/liquid matrix, scratch-state assertions and native route checks
├─ VerifyObservedMotion.cs   native-terrain hostile forecast and pure regroup-urgency contracts
├─ VerifyProjectileMotion.cs native projectile-AI and swept-shot contracts
├─ VerifyPersonalDanger.cs   separated chambers verify actor-specific hostile reachability
├─ VerifyCompanionLifecycle.cs actual mod NPC attachment, player-death decisions and hand/downed lifecycle
├─ VerifyRoutePersistence.cs full route-cache TagIO round trips and fail-open load cases
├─ VerifyThreatAnticipation.cs harmful unattackable actors, projectile attribution and measured forecast confidence
├─ VerifyCapturedEscape.cs   captured pool and mirrored awnings exercised against native collision
├─ VerifyResponsiveFollowing.cs generic follow arrival, vertical separation and C-turn route progress
├─ VerifyFollowRecoveryAndProtection.cs visible recovery flight, cancellation clearance and retained guard protection
├─ VerifyOreWork.cs           ore-only retained mining jobs and the Disabled/Mimic/Opportunistic work policies
├─ VerifyCompanionPreferences.cs per-character defaults, malformed payloads and compressed native save round trips
├─ VerifyCompanionActivities.cs activity identities, range boundaries, progress windows, protected rooms and native torch placement
├─ VerifyObservationLifecycle.cs real recorder reservation, zero-tick metadata and callback-scoped lifecycle evidence
├─ GodsEyeTestStubs.cs       unrelated mod and TSV seams; the real player hurt observer remains compiled
└─ VerifyGodsEyeEvents.cs    real sparse-event writer, native-hook, generation and terrain-capture contracts
```

From the repository root run `dotnet run --project Tools/EngineReplay`. Exit zero requires every matrix entry to match and all native route fixtures to arrive. `sh Tools/verify.sh` includes this command. The game location can be supplied as the executable’s first argument; MSBuild’s TModLoaderRoot controls the reference location when compiling on another installation.

`--render-ui` uses a hidden SDL surface to render the actual profile, cargo, mastery and inspector pages with installed game assets. PNGs go under the process temporary directory's `aic-native-ui` folder. On macOS, provide `DYLD_LIBRARY_PATH` pointing to the installed loader's `Libraries/Native/OSX`. It exercises policy-button event handlers and bounds the painted pixels of a real debug line; no visible game is launched. Native UI setup must supply cached original screen dimensions, the UI matrix, the world view matrix and inventory gamepad link points. Missing those services produces blank/clipped pages or inventory exceptions that are harness failures rather than UI findings.

`--replay-water=Telemetry/<stamp>.tsv --tick=<tick>` reconstructs local terrain from event snapshots preceding that sample's wall-clock time, restores the observed body and breath, and runs the full brain with native collision. The wall clock joins the files because their game-tick origins can differ across world entry. Exact tile flags, frames and liquid amounts are restored when present; older glyph snapshots normalise material and disclose that loss. Uncaptured terrain is closed, unknown mod content is rejected with context, and moving liquids and other entities are not reconstructed. This is a bounded static replay, not a saved-world loader or a complete reproduction of enemy interactions.

Full-brain water tests require sustained native head clearance while alive. A one-cell head check can report air while the native breathing rectangle remains submerged. The captured older pool starts with its recorded remaining breath; full liquid cells are an explicit reconstruction assumption. Cosmetic combat-text slots are occupied in headless fixtures so native drowning strikes apply damage without requiring graphics fonts; the damage path itself remains active.

The setup assigns Terraria.Program.SavePath before Main’s static constructor, constructs a small Tilemap through its non-public constructor and marks the process dedicated-server for headless operation. Each collision comparison creates an ordinary NPC, runs its private gravity setup, applies shared controls and the step helpers, adds the engine’s gravity, then invokes private UpdateCollision. It fixes wetCount to suppress liquid entry/exit audiovisual effects. That collision matrix invokes no game AI, enemy spawning, save loading or full NPC update. The separate lifecycle fixture invokes the actual companion's AI entry point; the captured escape fixture invokes its production survival action and senses over native collision.

The matrix covers floor and ceiling slope orientations, flat/sloped platforms, dry movement, water, honey, shimmer, liquid transitions, offsets, jump and fall-through controls, and both space-scaled and ordinary gravity. Position, velocity, wet state, liquid priority and stair state must agree; prediction must restore all seven Collision scratch fields. Native route cases cover flat travel, a two-tile ledge, staircase ascent and staircase descent. Their output reports recovery faults even when the destination is reached.

The same executable also verifies every projectile in the current companion kit against native `Projectile.VanillaAI` for a bounded free-flight run. It compares phase, gravity, drag, terminal velocity and default hitbox, then checks that a swept trace rejects a thin blocking tile, reopens when that tile is removed, and rejects an accuracy-rotated launch that no longer reaches its target. This establishes the solver's supported projectile profiles and collision sampling; it does not prove every possible modded projectile or a live combat playtest.

`VerifyObservedMotion` runs the shared target forecast against the same initialized Terraria tile map. It checks a stationary grounded hostile remains supported; a tile-colliding flyer stops at a wall while a phaser crosses it; observed acceleration changes the short forecast; a jump is not extrapolated as a repeated impulse; `Forget` removes a reused NPC slot's old track; and a position correction during the same engine tick replaces an already-built forecast. It compares dry custom gravity/fall cap and wet custom movement slowdown against native `UpdateCollision` exactly, then bounds the remaining slope/step-policy difference while requiring non-zero current gravity to travel through `SlopeCollision`. It also keeps the pure regroup pressure monotonic as separation, return time, player movement away and stalled travel grow, while a nearby stationary companion remains calm. The test snapshots Terraria's collision scratch flags around each forecast, because a target forecast that changes shared collision state can corrupt the movement prediction it is supposed to inform.

`VerifyGodsEyeEvents` compiles the actual sparse event writer and its native NPC, projectile and terrain hooks with only test-local telemetry and mod stubs. It writes and parses a temporary JSONL session, then deletes it. The fixture proves sequence/schema/timestamp validity, normal session closure, snapshot-at-occurrence behaviour, reused NPC/projectile slot generations, shot-to-terrain correlation through the first projectile generation, and tile dirtiness becoming a changed local terrain snapshot only after post-update sees the engine edit. Its terrain fixture includes a slope and water, checks the rolling initial capture, and requires the recorded local chunk to retain glyph, liquid and material fields.

These are repeatable collision and route fixtures. They do not establish general world navigation, threat prediction quality or comfortable companionship. The historical text-world corpus and a recorded playtest answer those different questions. Private engine method names are intentional verification dependencies: if a game update removes them, the test must fail visibly rather than silently substitute another simulator.

Loader templates must be registered once per content type. Registering a fresh template for every fixture makes `ContentInstance<T>.Instance` null when more than one instance exists, so isolated tests pass while the combined process fails. Attach each fresh ModPlayer through its inherited `Entity` property and preserve the one template registration. Native tile placement also expects every player slot to contain an object, including inactive slots; a null slot is a fixture defect before it is evidence about placement.

The firing-position fixture resolves the positioner repeatedly rather than once, because its reachable region is flooded incrementally across rescores and a single call sees a region a few tiles wide — a one-call check measures flood budget rather than candidate scoring. Its shaft geometry is asserted to be discriminating before the result is read: the near floor must be blind and the lip sighted, or a green result would mean nothing. Terraria's `CanHitLine` tests three tile rows at each step, so an opening exactly as wide as the shaft leaves every solid-floored lip blocked and the fixture would be asserting an impossible shot.

Arrival assertions measure the actual body against the follow objective and local sight. The positioner's follow-status flag belongs to its current request and is cleared when selection yields to idle; using that flag alone can label a completed route failed precisely because following ended correctly.

Activity tests exercise the native Smart Cursor torch selector and `WorldGen.PlaceTile`, including nearby-light rejection, unsupported/occupied sites, companion-first resource consumption and shared cursor scratch preservation. Room fixtures include enclosed bedrooms and door boundaries beyond the conservative open-room fallback. Observation checks cover independent configuration switches, prediction state surviving recording opt-out, inspector bounds and the HUD's opening-click consumption before drawing. These contract tests complement rather than replace in-game UI inspection.

The project references the actual mod under the `live` assembly alias for lifecycle and behaviour checks. Those tests attach a real CompanionNPC through ModNPC.Entity, disable rendering/first-tick logging that require unavailable graphics/loader services, and keep the production brain, senses and movement components. `--lifecycle` checks owner death; `--escape` checks captured water escapes; `--route-persistence` round-trips a full-capacity archive through compressed native TagIO; `--follow` checks vertical intent and a live C-turn; `--protection-recovery` checks guard retention and continuous recovery; `--ore-work` checks work selection and resumption; `--observation` checks the actual recorder's retry and lifecycle evidence. The default run includes all of them. This does not launch a game, run enemy AI or test save-file loading through the full world loader.
