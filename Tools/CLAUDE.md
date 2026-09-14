# Tools — headless evidence for companion behaviour

These console tools are excluded from the mod build and package. They consume telemetry and the portable movement core without launching Terraria, so a route or record can be inspected before a playtest result is interpreted.

```
Tools/
├─ CLAUDE.md                         this guide
├─ check-navigation-boundary.sh      verifies that portable movement code remains game-free
├─ verify.sh                         repository verification entry point
├─ backfill-capture.sh               turns a playtest recording into a ledger run under the revision that wrote it
├─ corpus.sh                         scenario corpus helper
├─ decompile.sh                      Terraria source lookup helper
├─ Ledger/                           one row per named case per run, committed, diffed against the nearest ancestor with a clean run
├─ EngineReplay/                     native collision and whole-brain fixtures, grouped by Combat, Gathering, Assistance, Movement, Observation and Lifecycle
├─ NavReplay/                        portable movement replay — three sources, stays flat
├─ SessionReport/                    telemetry reader, grouped by Read, Checks, Write and Tests
├─ WorldWindow/                      reshapes old plan windows from saved-world tile shapes
└─ Scenarios/                        committed movement cases
```

`check-navigation-boundary.sh` is the architecture check and it holds two boundaries. The portable core does not name Terraria outside the dedicated adapter. And no file under `Companion/Brain/Activities` or `Companion/Brain/Infrastructure/Interactions` runs a route search to ask whether a place is reachable: those folders read the reach sense, and the script refuses `WalkerReach`, `RoundTrip`, `AStar.Find` and their kin there. It deliberately does not refuse `Reachability.Reach`, which is the verdict every one of those folders passes around — a pattern that caught the type would fail the whole tree on the day it landed and would be switched off within the week. A green check establishes a source boundary only; it does not establish that portable movement matches a live NPC, nor that the reach sense answers correctly.

Run `dotnet run --project Tools/SessionReport -- Telemetry` after a playtest. It reads the newest session, says which checks its schema supports, and exits non-zero for definitive faults. `NavReplay/CLAUDE.md` owns planner replay flags and verdict meanings; `WorldWindow/CLAUDE.md` owns saved-world reshaping; `EngineReplay/CLAUDE.md` owns native collision verification.

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

- Replay success is evidence about the portable movement core. It never substitutes for a playtest or the native collision comparison.
- **A number quoted about a capture in prose is a claim, and twice now it has been wrong in a way no reader could catch.** Both misses were the same shape — a filter somebody wrote over the file once, reported as a measurement — and both were found by building the instrument that produces the number instead. Anything asserted about a recording is reproducible through `Tools/SessionReport --measures` or it is not yet evidence.
- **A check whose search tool is missing prints exactly what a held boundary prints.** `check-navigation-boundary.sh` pipes ripgrep into two greps and reports the boundary green when the pipeline is empty, so on a machine without `rg` it printed "movement boundary holds" while having searched nothing (2026-09-11). It falls back to POSIX grep now, and refuses with exit 2 only where neither tool exists. Two rules came out of that morning: a check states what it could not run rather than letting an absent tool read as a pass, and a check earns its keep by depending on nothing optional, because a permanent skip hides inside a green run just as well as a false pass does.
