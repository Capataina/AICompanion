# Tools — headless evidence for companion behaviour

These console tools are excluded from the mod build and package. They consume telemetry and the portable movement core without launching Terraria, so a route or record can be inspected before a playtest result is interpreted.

```
Tools/
├─ CLAUDE.md                         this guide
├─ check-navigation-boundary.sh      verifies that portable movement code remains game-free
├─ verify.sh                         repository verification entry point
├─ build.sh                          builds the mod or any tool project and prints only what failed
├─ run-case.sh                       runs one named case standalone, outside any run file
├─ measure-flake.sh                  one case many times at one commit, with the interval that bounds it
├─ backfill-capture.sh               turns a playtest recording into a ledger run under the revision that wrote it
├─ corpus.sh                         scenario corpus helper
├─ decompile.sh                      Terraria source lookup helper
├─ Ledger/                           one row per named case per run, committed, diffed against the nearest ancestor with a clean run
├─ EngineReplay/                     native collision and whole-brain fixtures, grouped by Combat, Gathering, Assistance, Movement, Observation and Lifecycle
├─ CombatAudit/                      re-decides every recorded combat snapshot through five passes, the only instrument grading a decision rather than a behaviour
├─ NavReplay/                        the game-free movement core's own contract rows, and the extractor that cuts a scenario out of a recording
├─ SessionReport/                    telemetry reader, grouped by Read, Checks, Measures, Write and Tests
├─ WorldRun/                         the whole brain and the orb in a real saved world: behind a recorded player track, in a committed scenario window, inside the recording's own staged scene of hostiles, drops and settings, or — the soak — behind a seeded bot player for as long as it takes a slow climb to show
├─ WorldWindow/                      reshapes old plan windows from saved-world tile shapes
└─ Scenarios/                        committed terrain windows from play, two of them the orb's checkpoints
```

`check-navigation-boundary.sh` is the architecture check and it holds three boundaries. The game-free core does not name Terraria outside the dedicated adapter. And no file under `Companion/Brain/Activities` or `Companion/Brain/Infrastructure/Interactions` runs a search to ask whether a place is reachable for any tile: those folders read the reach sense, and the script refuses `FreeSpaceSearch`, `CornerGraph`, `ClearanceField`, `Navigator.MoveTo`, `SteerAlongRoute`, `Route.Smooth` and their kin there. It deliberately does not refuse `Reachability.Reach`, which is the verdict every one of those folders passes around — a pattern that caught the type would fail the whole tree on the day it landed and would be switched off within the week. And the third, the verdict boundary: no file under `Tools/` outside `Tools/Ledger/` prints its own `PASS` or `FAIL`. A fixture that decides its verdict in a print looks in a terminal exactly like one that reported, while the ledger never saw it — so it cannot be compared against a baseline, selected by `--case`, rerun by `--rerun-red`, or noticed when it stops running, and every one of those is a silence that reads as health. A fixture returns a failure count, throws, or calls `EmitLedgerRows.Detail` for a line a person should see; `Detail` prints exactly what the old line printed **and** folds it into the row, so a red says why in the run file rather than only in a console nobody kept. The rule is enforced rather than remembered for a measured reason: a hand audit of this tree found eighteen such prints and the check then found twenty-eight more, all of them interpolated strings the hand pattern had missed.

A green check establishes a source boundary only; it does not establish that portable movement matches a live NPC, nor that the reach sense answers correctly.

**No wall-clock allowance decides a verdict in the default suite.** `EmitLedgerRows.Case` calls a reset before every case, and each instrument registers its own: the engine tier in `EngineReplay/ResetProcessState.cs`, the portable tier inline in `NavReplay/Program.cs`. The reset lifts the millisecond allowances, leaving each query's work-count limits as the only bound, and returns the statics a case can reach — the planning allowance, the shared clearance field, the search's world override, the terrain record and the census — to what a fresh process holds. Both copies of every static are written, this project's and the mod assembly's, because EngineReplay compiles the movement core a second time beside the `live` alias and a reset naming one of them leaves the other holding the previous case's world.

A case whose subject *is* a deadline keeps the clock, and there are two ways to say so. A whole case tags itself `EmitLedgerRows.ProductionAllowancesTag`. A single row inside an otherwise lifted case saves the regime, turns the lift off, and restores what it found — never a literal, because a literal is the current default written down twice. The row's own mode is stamped by the emitter rather than passed by the caller, so a row always records the regime the process was actually in.

The walker's three deadline rows went with its planner, and no orb row is about a deadline yet; the mechanism stays because the next one will be. Each of the three had announced itself by going red with its own premise assertion rather than by passing quietly, which is what a good premise sounds like — "the starved run finished its search in one tick, so it proves nothing about deadlines" — and an orb deadline row is held to the same bar.

Run `dotnet run --project Tools/SessionReport -- Telemetry` after a playtest. It reads the newest session, says which checks its schema supports, and exits non-zero for definitive faults. `NavReplay/CLAUDE.md` owns the core's contract rows and the scenario extractor; `WorldWindow/CLAUDE.md` owns saved-world reshaping; `EngineReplay/CLAUDE.md` owns the native fixtures; `WorldRun/CLAUDE.md` owns the recorded-route run, the scenario checkpoints, the play-measures run that stages a recording's own scene and grades it, and the soak, which is the only instrument here that runs the brain for longer than a recording — its short form is two minutes of seeded play inside `verify.sh` and its hour-long form is a command run on purpose before a package; `sh Tools/verify.sh` runs the play measures against the 22 September 2026 capture and that suite is red until the brain fix it measures is in, which is its purpose rather than a defect in it.

**An instrument's exit code is acted on, not merely collected.** Rows are the verdict, but an instrument that fails *without* writing a red row — a crash before its first case, a project that will not build, a failing path that files none — contributes exactly the silence a healthy instrument contributes. So every non-zero exit goes to `ledger error`, which files an error row only where that instrument's own rows do not already account for it. Before that, a red SessionReport self-test exited 1 and the run still scored clean at exit 0.

`sh Tools/verify.sh` runs the build, the boundary check and every instrument, **and it no longer stops at the first failure**. That was not a style preference: assertions in these fixtures throw, EngineReplay summed thirty-eight of them in one expression, and the script exited on the first instrument that returned non-zero — so one throwing fixture took the rest of its chain with it and the run reported a single exit code that could not tell an unrun fixture from a passing one. The known intermittent fixture sits thirteenth of thirty-eight, so on the runs where it fired, twenty-two later fixtures reported nothing at all. Now every instrument runs to the end, every case writes its own row, and the ledger's scoreboard is the verdict. Exit 2 still means a check could not be asked rather than failed.

```
sh Tools/verify.sh                      everything, scored against the baseline
sh Tools/verify.sh --case "ore work"    only cases whose name contains the fragment
sh Tools/verify.sh --rerun-red 5        rerun each red case five times and grade it
sh Tools/measure-flake.sh 30 "ore work" one case many times at one commit, with its interval
sh Tools/backfill-capture.sh <capture>  a recording as a ledger run under the revision that wrote it
```

`Ledger/` is what makes any of the above a trend rather than a verdict. Every instrument reports one row per named case through its emitter and through nothing else, so "every case reports" is a property rather than a habit, and no instrument parses another's printed output — a print is for a person, and the moment a second program reads it every print becomes an undeclared contract. A run stores under the commit its rows describe, which for a playtest recording is the revision the recorder stamped rather than the checkout, so a capture made before the ledger existed still becomes a run at its own commit. `Ledger/CLAUDE.md` owns the row schema, the six verdicts and the two statistical rules; the one thing worth carrying here is that a measure is never graded against a threshold, because a number nobody declared becoming a pass line is what collapses "ever green" into "green now".

## Traps

- A green NavReplay row is evidence about the game-free movement core, which is the same contact and search the mod runs; it never substitutes for a playtest, or for the native suite, where that contact reads Terraria's own tiles.
- The two scenario checkpoints and the recorded-route slice need the saved world, which is not committed; on a machine without it they file skips, and a run whose "stopped reporting" block names them is a machine without the world rather than a regression.
- **A number quoted about a capture in prose is a claim, and twice now it has been wrong in a way no reader could catch.** Both misses were the same shape — a filter somebody wrote over the file once, reported as a measurement — and both were found by building the instrument that produces the number instead. Anything asserted about a recording is reproducible through `Tools/SessionReport --measures` or it is not yet evidence.
- **A check whose search tool is missing prints exactly what a held boundary prints.** `check-navigation-boundary.sh` pipes ripgrep into two greps and reports the boundary green when the pipeline is empty, so on a machine without `rg` it printed "movement boundary holds" while having searched nothing (2026-09-11). It falls back to POSIX grep now, and refuses with exit 2 only where neither tool exists. Two rules came out of that morning: a check states what it could not run rather than letting an absent tool read as a pass, and a check earns its keep by depending on nothing optional, because a permanent skip hides inside a green run just as well as a false pass does.
