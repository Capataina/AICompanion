# God's View — one verification harness, three instruments, one kit

Written 14 September 2026, before any of it is built. This is the plan the owner asked for in one piece: what the harness should be for it to tell us something in the long run, with the research that supports or refuses each part. It supersedes nothing in the tree; it says what the tree should become. The owner's bar, stated in his words and binding on every part below: a wrong test is more harmful than no test, and no test is worse than him having to go in-game to find out.

## Vocabulary

**God's View** is the whole harness family. **God's Eye** keeps its existing meaning: the event timeline the recorder writes and SessionReport reads, and every instrument writes through it. An **instrument** is one composition of the kit that asserts one kind of thing. The **kit** is the set of shared parts every instrument is built from. The **ledger** is the memory: one row per named case per run, committed, diffed against the nearest ancestor with a run. A **row** is the unit of everything: a case's verdict, a measure's value, a checkpoint's outcome.

## What the three instruments are, and the one thing each must never do

```
fixtures      Tools/EngineReplay + Tools/NavReplay --self-test
              question   does this one mechanism still do what it did, on this exact shape
              never      depend on the wall clock, on another case's leftovers, or report any way but a row

corpus        Tools/Scenarios replayed by NavReplay, audited by --audit-jumps
              question   does the planner still agree with the executor on every shape play produced
              never      be altered to make a proposed move succeed; fold its outcomes into one number

world run     new: a real .wld, a scripted player, the engine's own tick
              question   in a real world behind a real player track, did the whole brain arrive, and did it lie about why not
              never      count a place the player reached by an ability the companion lacks as evidence
```

The three differ in exactly three choices: where the world comes from, where the player comes from, and what is asserted. Everything else is the kit.

## The kit: one owner per part, two tiers

The parts already exist as helpers scattered across five fixture files (`VerifyCompanionLifecycle.Create`, `TickWithOneControlGrant`, `AdvanceBrain`, `AdvanceNative`, `WriteMeasuredLight`, `ForceRefresh`, per-fixture world builders). The kit is those, owned once. The boundary that splits it into two tiers already exists: the navigation boundary keeps the portable movement core game-free, and NavReplay compiles without Terraria.

```
portable tier (all three instruments)
├─ scene text format         the corpus's own glyph world, read and written by one pair
├─ results emitter           the row schema and writer, owned by Tools/Ledger
├─ checkpoint scorer         planner claim × body outcome × player trail, filtered by the envelope
└─ envelope                  MovementCapabilities, read from the mod, never copied

engine tier (fixtures and the world run)
├─ world source              hand-built tiles · a captured window rebuilt as native tiles · a real .wld through the engine's loader
├─ actors                    attach the real companion · place hostiles · a scripted player from a track or waypoints
├─ clock                     one tick through the engine's own NPC and player update; allowances lifted or held on purpose;
│                            the shared random generator seeded; the engine thread pool pinned
├─ presentation              light computed by the engine's own area processor over a window, or written by hand where a row says so
├─ record                    the recorder and God's Eye, always on
└─ reset                     one helper that returns every process-wide static a case can reach to a known state
```

The one thing the kit never owns is the assertion. Each instrument says what it claims; the kit builds scenes and advances time. A shared "did it work" helper is how a suite starts passing for the wrong reason.

## The ledger, which four techniques need before they can exist

The research finding that orders everything: order randomisation, mutation attribution, pass-rate intervals and test-case reduction all require that a named case have an outcome, and today the suite aborts on the first failure and returns one exit code. The repository had already derived the consequence for itself ("a mutation run against a row that sits late in an abort-on-first-failure list proves nothing about that row", `Tools/EngineReplay/Gathering/CLAUDE.md`). So the ledger is first, and it is one core with emitters and consumers:

```
Tools/Ledger                 owns the row schema, the store, baseline resolution, the diff, the scoreboard, the compare command
├─ emitters                  EngineReplay, NavReplay, SessionReport, WorldRun — each writes rows through the library and nothing else
├─ orchestrator              verify.sh runs every instrument, never stops early, calls the ledger last, exits on its verdict
└─ consumers                 the scoreboard printed at the end of every verify; `ledger compare <A> <B>` for any two commits
```

A row: instrument, suite, case (the human sentence the fixture already prints), verdict (pass, fail, sealed, skipped, error, measure), value and direction for measures, mode (alone or in-suite; unbounded or production allowances — the suite's own rule that a timing is comparable only to one taken the same way), tags (known-limitation, requires-idle, behaviour id), message, duration. A run header: commit, dirty flag, timestamp, machine, load average at start, concurrent dotnet processes. Rows live at `Tools/Ledger/runs/<hash>-<timestamp>.jsonl`, committed, one file per run so concurrent lanes never conflict. Baseline for a clean run is the nearest ancestor with a clean run; a dirty run compares against HEAD's last clean run. Because a run stores under whatever hash it ran at, history can be backfilled, which is how a change that landed before the ledger is still benchmarked and how calibration on a known-bad build uses the same mechanism as everything else.

Two rules from the research land in the scoreboard rather than in prose. A pass rate is printed with its interval, never as a point: five green runs bound a failure rate only below about 43 percent (Wilson, 95 percent), thirty below 10, a hundred below 3, and "three of five" is consistent with anything from 12 to 77 percent, so the batch size the flake trap asks for is now arithmetic. And a measure's delta is judged against a band computed from repeats on one commit (Georges et al., OOPSLA 2007: overlapping intervals mean no conclusion), with fewer than three repeats printing no band rather than a fake one.

Verdict semantics that stop a wrong test from reading as a right one: `error` is the check breaking, not the thing under test failing; `skipped` carries its reason and is never a pass; `sealed` is the corpus's model-closed answer, never proof of impossibility; a `known-limitation` row flipping green is an upgrade candidate, not noise; a "new red" is a case green at the baseline and red now, and on one run it prints as unconfirmed with the rerun command beside it.

Enforcement: the navigation boundary script gains a rule that a fixture printing `PASS` or `FAIL` outside the emitter fails the boundary, so "every case reports" is a property rather than a habit.

## Instrument 1, fixtures: what changes and why

**Every assertion becomes a row.** 21 of about 1,180 assertion sites are wrapped as named cases today; the rest are bare throws. This is a migration across 39 files, and it is the price of everything below. Each case name is the sentence the fixture already prints.

**No wall clock in the default suite, enforced.** The raised-lip flake is Luo et al.'s "Time" category (FSE 2014): the planner's budget is a wall-clock allowance, so machine load changes how much search fits, and the repository's own measurement shows it (`--brain-cost` diverged at tick 40 under production allowances and matched for 600 ticks with `LimitPlanningWork.Unbounded`). The fix class, not the instance: the default suite forbids production allowances except in the four kinds that test the deadline itself (isolated escape searches, the deadline rows, cost measurements, `--replay-water`), enforced by a check that lists them and fails any other fixture that does not lift the allowances. The pass line is declared now: thirty consecutive idle runs green on the raised-lip fixture bounds its failure rate under 10 percent; if it still fires, the load hypothesis is dead and the ledger's load column says what is left.

**Isolation before reordering.** The Courtesy stillness "depending on what ran before" and the recorder's seeded travel test are Shi et al.'s victim-and-polluter pair (FSE 2019); the observation suite restoring the mining policy is a hand-written cleaner. The kit's reset helper is the cleaner written once, and a boundary rule refuses a fixture that touches a process-wide static without it. Only after the suite is green in its original order does randomised order (Lam et al., ICST 2019) become a mode: iDFlakies could not analyse a third of the modules it tried because they were not green in original order, which is where this suite sits today.

**Per-case selection and rerun-red.** EngineReplay gains `--case <name>` beside its group flags; `verify.sh --rerun-red N` reruns only the reds and prints their interval.

**Mutation as a recorded property of every row, not a tool.** The suite has found rows that tested nothing twice, both times by hand-mutating the production rule (the encounter gate, the light sense), and the research says why a tool would not have: five of the seven hand mutations were semantically plausible rival rules, not syntactic operators, and Stryker.NET drives a VSTest-shaped test project, which neither console harness is. So automated mutation is refused for now, with its revival condition: a test-adapter surface over the per-case protocol. What is built instead is smaller and closes the class: a row may carry `killed_by`, the mutation that fails it, and a row added without one is reported by the scoreboard as unkilled, so the practice the suite already follows by hand becomes visible when it is skipped.

**Generated shapes.** The 63-entry ledge sweep in `VerifyMovementFailures` is property-based testing by hand. A generator over rise, gap, runway, ceiling and entry speed (CsCheck, current as of this month) sweeps the physics envelope in NavReplay, where the oracle is deterministic, and proof-versus-execution agreement is the property.

**Traceability by tag.** Every row carries the README behaviour row it evidences, in the style OpenFastTrace uses (an id in ordinary text, checked by grep); the scoreboard lists behaviours with zero rows. The research's warning (Cleland-Huang et al., FOSE 2014) is that heavier traceability fails on cost, which is why this is a tag and a grep and nothing more.

## Instrument 2, corpus: what changes and why

**Mirroring, only where the system is deterministic.** Mirroring every window horizontally and requiring the same verdict is the environment-class metamorphic relation the multi-agent pathfinding literature uses, and it doubles the corpus for free. It runs on the NavReplay corpus now, because that backend is deterministic. It does not run on EngineReplay until the wall-clock rule above has landed, because the one mirror relation already implemented there (`mirrored=True` in the raised-lip fixture) is the flaky case, and a relation checked against a flaky oracle tests the oracle. Whether that red is a real left-right asymmetry is the first question the deterministic suite answers.

**Reduction with tile-shaped moves.** `ledger shrink <scenario>` cuts a failing window to the smallest sub-window that still fails, using Zeller's ddmin (TSE 2002) with domain-specific transforms as Regehr et al. showed matters (PLDI 2012): shrink the window bounds toward the start, replace a region with air, delete a column or row, collapse identical rows. The oracle is `--audit-jumps` or `--follow` on that one file, which is scriptable and deterministic. The validity trap is the corpus's own rule: actor markers carry the support under them, and a fixture is never altered to make a move succeed. Generated windows shrink by regeneration instead (MacIver and Donaldson, ECOOP 2020), which is why generation lives in the same tool.

**The residue becomes rows.** `--audit-jumps` is red on a counted, uninvestigated residue. Each outcome class is a ledger row with a count, so the red is a number that moves and a triage that is visible.

**Extraction from captures.** `ledger extract-scenario <capture> <tick>` cuts a window around a SessionReport finding, so a play defect becomes a corpus file with one command and a row that tracks its fix.

## Instrument 3, the world run: what it is and what it must prove first

**Inputs.** A route file, committed: world name and hash, the player's track per tick (position, velocity, and the ability flags copied from the engine's player object: mount, grapple, wings, dash, extra jumps, rockets), and where it came from. Recorded routes are extracted from captures; authored routes are waypoints at walking speed with velocity derived from consecutive positions, because follow reads predicted feet from velocity. The `.wld` stays uncommitted, matched by hash; a mismatch is a skipped row with its reason.

**Tick.** The engine's own loader with the mod's systems registered; the engine's own NPC update for the companion and hostiles, not the fixtures' reflection copy; the light engine's area processor driven over the window around the player each tick (its code has no graphics dependency, and nothing in the suite has ever driven it, so this is a spike); the recorder on. Spawns off, hostiles placed from the capture, the shared random generator seeded, planning unbounded.

**Determinism is a row in every run.** Two passes, equal telemetry hashes. If red, every other row of that run is unconfirmed.

**Calibration before trust.** The 09:28 route through the 0.23.0 source must reproduce what play showed: budget spent on most ticks, the three refused edges, the sealed pocket, the low arrival share. Each defect it fails to reproduce is a measured false negative of the instrument, recorded in this folder. Only then does a green on a newer build mean anything.

**Checkpoints.** Planner claim, body outcome and player trail joined per checkpoint, filtered through `MovementCapabilities`:

```
planner reachable,   body arrived                          pass
planner reachable,   body never arrived                    fail: the executor class
planner unreachable, trail went there on foot              fail: a missing edge
planner unreachable, trail went there by an ability the    skipped: outside the envelope
                     companion does not have
planner unreachable, trail never went                      no row
```

When an ability lands in the companion's definition, the filter narrows on its own.

**Route choice by coverage, without learning.** Mawhorter and Smith (FDG 2023) run Go-Explore's archive with progress scores read off an imperfect tile abstraction of the game, no reinforcement learning, and the pieces are already here: the reach sense is the progress score, the tile graph is the abstraction, the census is the coverage counter. Go-Explore's two failure modes name the world run's two risks: derailment, where returning to a checkpoint is unreliable, which is why the run needs state snapshot and restore rather than replay-from-start; and detachment, forgetting frontier states, which the archive exists to prevent. Coverage is reported as reachable tiles reached over reachable tiles, as a curve over ticks (Zhan et al., 2018), with the reach flood supplying the denominator.

## The recorder

Two schema additions: the player's ability flags per tick, and the world's name and hash in the session header. Without the second, replaying an old capture against a world he has since mined through is a false positive nobody would catch.

## Refused, with the condition that reopens each

```
├─ ✗ automated mutation through Stryker.NET      needs a VSTest-drivable case surface; the harnesses are console executables
│                                                  reopens: a test adapter over the per-case protocol
├─ ✗ a learned exploration agent                  the field's own survey names developer distrust; scripted Go-Explore covers the need
├─ ✗ mirroring on EngineReplay now                the one mirror relation there is the flaky case; reopens: the wall-clock rule landed
├─ ✗ thresholds per instrument                    collapse "ever green" into "green now"; cannot separate a flake from a regression
├─ ✗ parsing stdout into the ledger               every print becomes an undeclared contract
├─ ✗ a gitignored or single-file ledger           unreadable from a worktree; conflicts on every concurrent lane
├─ ✗ inferring abilities from the track           a heuristic with its own false positives under the thing measured
└─ ✗ a dashboard page                             read in a terminal after a build
```

## Build order, and what each step makes measurable

1. Ledger core, emitters for the three existing instruments, verify.sh inverted to run everything, the per-case migration and the boundary rule. Measurable at once: corpus PASS count and audit-jumps proven-versus-flown per file, at any two commits, including backfilled ones.
2. The wall-clock rule and the reset helper, both enforced; per-case selection and rerun-red. Pass line: thirty idle runs green on the raised-lip fixture.
3. Recorder: ability flags and world hash.
4. Backfill the last dozen commits idle, so the scoreboard has a trend and the flake has its first interval.
5. Corpus: mirroring, the residue as rows, shrink, extract-scenario.
6. World run: the two spikes (loader with mod hooks, light engine headless), the determinism row, recorded routes, the calibration run, authored routes, coverage-guided route choice.

No duration is attached, because the log holds no comparable unit to price a tooling project of this shape from.

## Sources this plan rests on

Luo, Hariri, Eloussi, Marinov, "An Empirical Analysis of Flaky Tests", FSE 2014. Lam, Oei, Shi, Marinov, Xie, "iDFlakies", ICST 2019. Shi, Lam, Oei, Xie, Marinov, "iFixFlakies", ESEC/FSE 2019. Micco, "Flaky Tests at Google and How We Mitigate Them", Google Testing Blog, 2016. Petrović and Ivanković, "State of Mutation Testing at Google", ICSE-SEIP 2018. Stryker.NET documentation, 2026. Zeller and Hildebrandt, "Simplifying and Isolating Failure-Inducing Input", TSE 2002. Regehr et al., "Test-Case Reduction for C Compiler Bugs", PLDI 2012. MacIver and Donaldson, "Test-Case Reduction via Test-Case Generation", ECOOP 2020 (abstract only). MET-MAPF, TOSEM 2024 (abstract only). Pettersson, "Execution monitoring in robotics: A survey", RAS 2005 (abstract only). Mawhorter and Smith, "Automated Testing in Super Metroid with Abstraction-Guided Exploration", FDG 2023. Ecoffet et al., "First return, then explore", Nature 2021. Zhan, Aytemiz, Smith, "Taking the Scenic Route", KEG 2019. Georges, Buytaert, Eeckhout, "Statistically Rigorous Java Performance Evaluation", OOPSLA 2007. Cleland-Huang et al., "Software Traceability: Trends and Future Directions", FOSE 2014. OpenFastTrace. The Ubisoft voxel-versus-navmesh validation paper (arXiv 2605.21397) and the La Forge Go-Explore reachability paper (arXiv 2209.00570) from the earlier industry survey. Not retrieved and cited bibliographically only: Jia and Harman (TSE 2011), McKeeman (DTJ 1998), Chen et al. (CSUR 2018), Chen, Cheung and Yiu (HKUST 1998), Kalibera and Jones (ISMM 2013). The most costly miss is Shi et al., "Mitigating the effects of flaky tests on mutation testing", ISSTA 2019, which is the exact intersection of this suite's two problems and was not reachable.
