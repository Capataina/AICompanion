# WorldRun — the whole brain, in a real world, behind a player who moves

This is instrument 3 of the God's View plan. The other two ask whether one mechanism still does what it did on a shape somebody built for it; this one asks whether the companion, in a world the owner actually played in and behind the player track he actually walked, arrives — and whether it lies about why not.

**What it exists for is the player moving.** `EngineReplay --replay-water` rebuilds a captured window and replays one recorded pose, and its own guide names the limit: the player stands still at its recorded position throughout, so a decision that depended on the player travelling — a reunion, an overtaking walk, a follow that gave up — cannot be reproduced there at all. Here the player is placed at the position *and velocity* the recording holds for each tick, which is the whole difference. The velocity is not redundancy: following reads the player's predicted feet from velocity rather than from the gap between two observed positions, so a player moved by position alone reads as somebody standing still in a new place every tick, and every follow decision downstream is made against a player who never travels.

```
WorldRun/
├─ CLAUDE.md                    this guide
├─ WorldRun.csproj              the mod under the `live` alias, plus the ledger's row writer as a source file
├─ Program.cs                   library resolution and the save-path redirect, before anything touches Main
├─ WorldRunEntry.cs             the flag table, the skip paths, and what one run prints
├─ LoadTheSavedWorld.cs         a real .wld into Main.tile, without the loader machinery
├─ PrepareTheHeadlessEngine.cs  tile tables, loader hooks, the light engine, the companion, the reset
├─ ReadRecordedRoute.cs         a capture read as a player track and two declared kits
├─ RunTheWorld.cs               the tick loop, and what it records about every tick
├─ ScoreTheRun.cs               determinism, the recorded comparison, the checkpoint matrix
└─ ExploreWithoutTheTrack.cs    Go-Explore over body poses, with the reach sense as the progress score
```

## How a tick runs, and why in this order

The player is placed from the recording. The world clock advances, because the brain keys caches on it and a run that never moved it would let every tick-aged answer live forever. The light engine is advanced one phase. The brain runs and the motor applies its controls. Then the engine's own gravity and `UpdateCollision` finish the move. Afterwards the run records the body's feet, a trace line naming the decision as well as the position, and what the planner claimed about the tile the player was standing on.

The planner claim is asked *after* the tick rather than before, because the reach flood is advanced by the positioner's resolve rather than by the senses' own update, so asking first reads the previous tick's region under the previous tick's rules.

**The tick ends with gravity and `UpdateCollision`, not `NPC.UpdateNPC`.** That is a deliberate departure from the plan's wording, and the reason is `VerifyResponsiveFollowing.AdvanceNative`'s own: the production motor has already applied the movement abilities and the step helpers, so a full engine update applies the same controls a second time. What is used is the part this repository already treats as its independent oracle and checks its whole collision matrix against.

## The world source

The tiles come from the game's own `WorldFile.LoadWorldTiles` and nothing else, because tile *shape* is the thing no second parser should be deciding — a worldgen staircase of slopes drawn as full blocks is a false planner failure this repository has already paid for once.

Everything around that reader is written here, and the reason is a dead end worth not repeating: **`WorldGen.clearWorld` is a whole-game reset whose dependency set is the game's startup rather than the world file's contents.** `WorldFile.LoadWorld` and `LoadWorld_Version2` both reach it through `LoadHeader`. Walking it with the game's real initialisers got three subsystems deep — the tile renderer's cached draws, then `Wiring.Initialize`, then `Main.UpdateTimeRate` reaching `CreativePowerManager.Instance` and `SystemLoader` — and was still growing while getting further from anything about terrain. Resetting the creative powers sits on the path between reading a world's dimensions and reading its tiles, so no amount of shimming avoids content loading.

So the header is mirrored here in the game's own field order, as far as `rockLayer`, and the tile section is entered at the offset the file's own position table gives. The mirror is checked twice rather than trusted: after the tile read the stream must stand exactly on the next section's offset, which is the equality the game itself asserts; and the two layer depths are confirmed against the tiles, because sky is mostly air and the rock layer is mostly stone. That second check exists because `worldSurface` is the field able to be wrong most quietly — it decides what gravity a body is under, so a wrong one is not a crash, it is every jump in the run being the wrong height.

Not carried, so nobody looks for it: chests, signs, the town NPC roster, tile entities, the bestiary, and every world flag after `rockLayer` — hard mode, the downed-boss set, the invasion state. The `.twld` sidecar is not read either; this mod places vanilla torches and adds no tiles, so the terrain loses nothing.

## Two classes of headless absence

Game data the fixture suites never needed, because their scenes are one or two tile types and each states that its own type is solid. A real world is not dirt: without the vanilla tables every stone tile reads as air, the body falls through the world and every route is trivially clear. `Main.Initialize_TileAndNPCData1` and `2` are called by name and the result is read back — stone must be solid and a platform must have a top — so the tables silently failing to build is a refusal rather than a world walked as air.

Loader hook arrays, which mod loading builds and the loaders dereference unchecked. This was reached from three directions, so it is closed by construction rather than by name: the whole `Terraria.ModLoader` namespace is swept and every null `Hook*` array becomes an empty one, which is the honest state for a host with no mods. It refuses if it filled nothing, because a sweep that matched nothing looks exactly like one that worked.

## The light engine is a state machine, and this is the trap most likely to bite the next reader

`Lighting.LightTiles` reaches `LightingEngine.ProcessArea`, which **advances one of four phases per call**: minimap export, scene-metrics scan, tile scan, then blur and present. Only the fourth makes light readable. One call per tick is right, because that is what a running game does per frame — but **no brightness read means anything until four calls have gone by**, and a run that samples light on its first tick is reading an empty map rather than a dark world. Measured on a real world, brightness at a surface tile reads 0.000, 0.000, 0.000, then 0.686 while a tile deep underground stays at zero throughout.

Two of the four phases are not lighting, and both have to survive or the cycle never reaches the blur, which is why the minimap and the scene metrics are built here at all.

## Determinism needed three things and the third is invisible

Two passes of one route must produce one run, and the comparison is on the trace rather than on position alone: two runs can stand in the same place for a hundred ticks while disagreeing about what they are doing, and the disagreement is what makes every later row unrepeatable.

The planning allowances are lifted, because they are a wall clock and a run compared against another under them is comparing two afternoons. The process-wide memory is cleared between passes — route archive, terrain revision, edge cache — through the mod's own world-load path, because a second pass that inherited what the first learned would agree with it for reasons unconnected to determinism.

And **the light scanner is seeded per process.** `TileLightScanner._random` is `FastRandom.CreateWithRandomSeed()`, a fresh `Guid` each time, and every tile's light derives from it. Two runs in two processes light the same world slightly differently, so a lighting decision near its threshold flips for a reason nobody could trace. It is pinned by reflection. No fixture would ever have caught this, because the fixtures write their light maps by hand rather than letting the engine scan.

## A divergence figure is meaningless without the distance travelled beside it

The first travelling comparison reported a mean divergence of 0.708 px, which reads as a near-perfect reproduction by a build that has moved a long way since the recording. It was not. In that window the recorded companion sat at one position for all 120 ticks while the player climbed away, and the run stood still too — the two agreed about nothing happening.

So the rows carry how far the recorded body travelled and how far this run's body travelled, as their own rows rather than only as prose, and the message says outright when the recorded body barely moved. **A window is chosen for having motion in it, or the comparison cannot fail.**

These are measures and never pass lines, for two reasons. The source has moved since every capture here, so divergence is expected and a threshold would be asserting that this build should behave like an older one. And a saved world is the state at the end of the session, so terrain mined and torches placed after a capture's last row are present in the replay and were not present in the recording.

## Checkpoints, and the header both existing captures do not have

A checkpoint is a tile the player's own track stood on, taken at a cadence because consecutive ticks are the same tile. Each is scored on whether the body ever reached it — at any point in the run, not on the tick the player passed, because a companion following a player is behind them by design and scoring it at the moment of passing would count ordinary following as a failure.

The filter is the row's reason for existing: a place the player reached by wings is not evidence the companion failed to reach it. Both kits are read from the capture's `# capabilities=` header and **never inferred from the track**, which the plan refuses by name — a heuristic that watches where the player went and decides which ability took them there is a heuristic with its own false positives, running underneath the thing being measured.

Every capture written before that recorder line therefore **skips the verdict and still emits the counts**, because a count is not a verdict and a reader is better served by numbers plus an honest "this was not judged" than by nothing. The same holds for world identity: a capture with no `# world=` line cannot be matched to the world it is replayed in, and the run says "identity unverified" rather than asserting a match nobody checked. The first capture recorded on a build carrying both lines turns all of this on with no change here.

## Exploration finds a floor, and it stops finding things

The plan's Go-Explore loop, in the scripted form rather than the learned one: an archive of places reached, a return to the least-visited one, an excursion from there, and coverage read off the reach sense.

Two things about this brain shape it, and both are findings rather than conveniences. **There is no exploration behaviour to drive** — independent local exploration was deleted on purpose, and the companion left beside a stationary player keeps company and stays put — so what is scripted is where the *player* stands, and the companion's own following and route search carry it there. And **a restore is a fresh brain at a saved body pose**: the route archive, the flood and the edge cache cannot be put back, so every excursion starts from a body in the right place and a mind that has never been there, which makes the coverage number a floor rather than an estimate.

The observed behaviour to know before reading a number from it: **coverage saturates early.** Thirty excursions over nine thousand ticks produced new tiles on the first ten and nothing at all on the last twenty. That is Go-Explore's detachment failure mode arriving on schedule, and the cause is the frontier rule: the frontier is the furthest tile the reach sense has claimed, the flood only claims tiles near the body, so the frontier barely moves away from where the body already is. Anything improving this changes what a frontier is rather than how many ticks it gets.

One number in the same run is worth keeping for a different reason: the body stood on more tiles than the reach sense ever claimed. The reach region is narrower than where the body actually goes, because the flood is bounded per advance while the body keeps travelling.

## Running it

```
dotnet run --project Tools/WorldRun -- --route=Telemetry/<stamp>.tsv --world=<path>.wld
                                       [--from-tick=N] [--ticks=N] [--suite=<name>]
                                       [--print-trace] [--explore=<tick budget>]
```

Both inputs live outside the repository — `Telemetry/` is gitignored and a `.wld` is never committed — so both are named rather than discovered, and an absent one is a skipped row rather than a failure. `sh Tools/verify.sh` runs a slice with this machine's usual locations as defaults, overridable through `AIC_WORLD_RUN_ROUTE`, `AIC_WORLD_RUN_WORLD`, `AIC_WORLD_RUN_FROM` and `AIC_WORLD_RUN_TICKS`.

**`--from-tick` is how any question about a specific moment gets asked.** The source has moved since every capture here, so a run from tick one has diverged long before it reaches anything worth asking about; seeding both bodies at the recorded tick is the only way to ask what this build does at that place. A window chosen this way should be checked for having motion in it first, or the comparison rows measure two bodies standing still.

A whole capture is about five minutes once the determinism row has run it twice, at roughly two to nine milliseconds a tick depending on how much searching the window provokes. That is why verify plays a slice.

## Traps

- **A run that loads a world is heavy and says so.** The read itself is under a second, but the tilemap for a 6400x1800 world is a large allocation and the process holds it for the whole run.
- **Timings from here are comparable only to others taken the same way**, which is the suite's standing rule: the per-tick cost varies several-fold across windows of the same capture, because it is dominated by how much route searching the window provokes.
- **The census counts for the whole process**, so it is reset at the start of every pass. A report read after two passes without that reset is both passes summed.
- **No recorder is attached and none is started.** A world run reports through ledger rows; a recorder left running would drop a synthetic session into `Telemetry/` beside the real captures, and nothing in the file would tell a later reader that nobody ever played it.
- **`Tools/verify.sh --rerun-red` cannot rerun a world-run row.** Its dispatch table names the four older instruments, so a red here prints "which this script cannot rerun" rather than being graded. Rerunning by hand is the same command with `--suite` unchanged.
- **The companion-attach and native-advance helpers are a second copy of `EngineReplay`'s.** The two projects do not reference one another and the plan's kit is meant to own these once; until it does, a change to how a fixture builds a companion does not reach this folder.
