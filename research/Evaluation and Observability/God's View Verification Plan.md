# God's View — one verification harness, three instruments, one kit

Written 14 September 2026, before any of it is built. This is the plan the owner asked for in one piece: what the harness should be for it to tell us something in the long run, with the research that supports or refuses each part. It supersedes nothing in the tree; it says what the tree should become. The owner's bar, stated in his words and binding on every part below: a wrong test is more harmful than no test, and no test is worse than him having to go in-game to find out.

**Read on 15 September 2026, after the body changed.** The walking body this plan grades was retired on the night of 14 September for a flying orb, so three of its rules describe a body that no longer exists: R4 (a move in flight is atomic, with landings and pre-emptions) has no jumps to hold, R6 (a partial-progress stand from the body's actual feet) names a fallback the orb's positioner deleted in favour of aiming at the request's own anchor, and every row that counts jumps, drops, cancellations in flight or mislandings is a walker row. R2 (spatial invalidation), R3 (one motion forecaster) and R5 (three-valued, time-aware offers) transfer unchanged and are built. The instruments, the kit and the ledger are as described; the recorded-route checkpoint matrix is the orb's first measure against real play and reads sixteen of twenty on the water-pocket capture at `c94c797`. This paragraph is the only edit; the plan below is left as it was written so its reasoning can still be read.

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

## The 0.24.0 play of 14 September, read as classes, and how each is proven by this harness

The 13:27 capture (22,473 rows, schema 0.32.0, source 3d8b75e) is the first play of the two root fixes. The owner's report names six symptoms and three things that now work. Three independent traces were run on the record and the source without seeing each other or this section (the evidence pack carried the report verbatim, the tools and the paths, and no hypotheses), and the section below is their convergence plus the main thread's own measurements, with every number re-derived from the TSV and the events rather than taken from a return. Two numbers in the first version of this section were wrong and are corrected here: the companion was not "ahead on none" of the moving rows, and the 426 events counted as terrain edits are terrain-snapshot captures.

```
symptom                                   what the record shows                                          class
never overtakes, stops at the box edge    on the 7,901 rows where the player moves faster than          R1  a zero-gradient follow box: satisfaction is a
                                          1.2 px/tick the companion is behind by more than three            symmetric 240 x 96 px predicate on the player's
                                          tiles on 71.5%, ahead by more than four on 10.8% and by more        current feet with no velocity term, and inside it
                                          than eight on 4.7% (the ledger's ahead-share row), median
                                          181 px behind; on 33% of sustained travel it is resting or          the reunion pull is exactly zero, so keep-company
                                          strolling; the priced lead fired on 3 rows because a place          scores its wander floor and any rival wins; the
                                          is decided only on a finished flood and the flood is                one lead mechanism exists and is starved by R2
                                          "meeting-undecided" on 2,835 of the moving rows
the jump onto the platform fails,         149 jumps begun, 72 completed, 70 interrupted (68 cancelled,   R4  no commitment window between chooser and motor:
stops mid-air and falls                   2 pre-empted), 5 mislands, 4 refusals; 36 cancellations were       a voluntary Hold or a method change reaches
                                          in the air with the navigator left Idle, and 27 of those were       Navigator.Interrupt with no ground or mid-move
                                          keep-company's own request going WithPlayer to Hold on a             gate, the path and step are nulled and the motor
                                          tick the box read as satisfied while both bodies were               gets no controls mid-arc; the airborne stops are
                                          airborne (ticks 8127 to 8128 are the whole mechanism);               the record's 30 "airborne-no-sideways-speed"
                                          15 were grounded route replacements; drops lose 39 of 72;            stops; the cadence replan's mid-move guard covers
                                          both refused edges land offline from their recorded states           one of the two ways a plan starts, and that path
                                                                                                               is ground-gated, so it is the minority
the same jump, over and over              the objective is unsatisfied again the moment the cut body     R1 + R4 as one loop: satisfied in the air, held,
                                          lands outside the box, reunion wins, the same edge is                fallen, unsatisfied, replanned; the proof was
                                          replanned; NavReplay lands the edge 10 of 10 from its own              never wrong
                                          poses; that edge needs 120 ticks of uninterrupted ownership
                                          (77 of them run-up) against a median activity run of 18 ticks
does not know it can reach some places    reach flood unfinished on 9,812 of 22,473 rows (44%);          R2  one world-global clock: ObserveReach refloods
                                          the two-way region averages 177 tiles and is within 1.5% of        from nothing and ContinueRouteSearch invalidates
                                          the raw region, so the return requirement is not what binds;        on any change to TerrainChanges.Revision, which
                                          the edit rate itself is not in the record (the revision              any tile anywhere bumps; the flood advances only
                                          counter is not a column; the 426 are snapshot captures)              on a resolve, 400 tiles per advance
slow to go for a torch site or a drop     lighting is Usable on 255 rows (1.1%) and "not yet known       R2, R3  a drop is read at rest: the contact pose needs
until it lands                            reachable" on 75% of the rest; collect refuses 431 rows as            a standable floor within 16 px of the item's
                                          drop-has-no-contact-pose and holds every tick a proven drop           current bottom, and execution holds while the
                                          has moved more than 16 px, which a falling drop always has            item has moved a tile since its proof
a slime arrives and it stops; hunts       250 switches; on the flip tick the incumbent's own offer      R5  two mechanisms, one pair: hunt loses because its
ceiling slimes it cannot reach            was already invalid on 129 (hunt 64, guard 53, collect 9,           bounded stand search (8 solves or 2 ms) ran out
                                          torches 3) and outscored on 121 (keep-company 101, hunt            and the cut is reported as KnownUnusable, the
                                          20); keep-company and hunt swap on 120 of the 250; hunt is        label for a proven impossibility; keep-company
                                          KnownUnusable on 303 of 624 decisions, 50 of its 84 losses         loses because inside the box it scores the floor
                                          are "no usable destination established"; the median winner        (R1); the incumbent bonus is 1.15 against a
                                          to incumbent ratio on outscored flips is about 3.85                 median 3.85, so no margin would hold it
(not named) the gun is silent             fired on 72 ticks; no-target on 20,220; by activity, keep-     R7  the feet are never sent: with hunt chosen the
                                          company reads no-target on 98% of 16,600 rows while hunt            hands fire or cool on 43% of ticks, with keep-
                                          fires or cools on 43% of 3,385; the positioner and the               company they have a target on 2%; the hands are
                                          arsenal share one trajectory solver, and 50 of 82 arrows             not the defect, R5 is; the solver accepting arcs
                                          met terrain                                                          the world blocks is a separate open question
(not named) arrives at its own tile and   partial-progress-candidate on 328 rows at one spot with       R6  a destination with no exit condition: the fallback
holds with the player twenty tiles up     nav Arrived and follow-vertical-gap, flood complete, no             compares tile-quantised positions while arrival is
                                          state search; one trace claimed 22 siblings and the ledger's         12 px, so it can name the tile the body is already
                                          arrived-with-follow-gap row finds exactly one under the
                                          state definition (the 23 the report raised were I1's
                                          stale rejection field); the native replay
                                          reproduces the hold for 200 ticks with no rejection                  arrived at, and it declares no success region
                                                                                                               (the quantisation arithmetic is inferred, untested)
(not named) the chosen stand churns       the success-region revision changes 1,017 times, once every  R8  no commitment at the destination layer either:
under an unchanged request                ~22 ticks; WithPlayer journeys asked 222, reached 13,               a lattice re-sampled around a moving anchor with
                                          abandoned 209; 287 walks cancelled, about 120 of them                24 px of slack, and every method change is a
                                          keep-company's own method flipping WithPlayer/Exact/Hold             cancellation of the move in hand (R4's class)
(not named) stops inside walk steps       316 stops, 215 of them inside-walk-step (1,396 ticks) on a    R9  per-step entry costs on a chain of steps: a from-
                                          grounded body with the step unchanged; 76 a minute                  rest step makes the step before coast to a stop;
                                                                                                               the underground slowness he felt; open, not this
                                                                                                               round's
(not named) jumps regressed from 0.23.0   0.23.0: 69 begun, 39 completed (56.5%); 0.24.0: 149 begun,    the movement lane closed refusals (684 to 4), so
                                          72 completed (48.3%), cancellations 36% to 46% of begun            twice the jumps are offered and R4 cancels them
(not named) no gathering evidence         chop is PolicyForbidden (mimic-awaiting-player-tree-contact)   the in-game Mimic setting, not a defect; mining is
                                          on every row; gathering produced zero attempts                     R2-starved; "spends most of its time following"
                                                                                                             is partly configuration
```

The owner's two hypotheses come out differently. The platform jump was not the planner reading the world wrong: NavReplay lands the edge from every captured pose and the native replay of the held tick shows no refusal, so the plan is right and the flight is cut by keep-company's own Hold. The hunt flicker was, as he said, hunting being invalid at the moment of the flip, and the mechanism is the bounded stand search's budget cut being consumed as a proven absence; the chooser-level commitment margin proposed before that measurement is withdrawn, because it would hold an offer that cannot be executed, which is the ceiling-slime case exactly, and Path 2's own note warns that activity and local retention combine into stubbornness.

Where the three traces converged and where they did not, because the divergence is the finding: all three, independently, landed on the ungated Interrupt with no commitment window, on the budget cut reported as KnownUnusable, on the zero-gradient box, on the world-global revision and on the drop read at rest. One trace put the jump death on the cadence replan's stale branch skipping the mid-move guard; that mechanism is real at source and ground-gated, so it accounts for the 15 grounded replacements and not the 36 airborne stops. One trace split the flips 203 chooser to 47 validity; the same flips measured on the incumbent's own offer at the flip tick split 121 to 129, matching the third trace, so the first split read the score a tick early. One trace proposed the hands asking position selection for a stand with a shot; refused, because it makes the arsenal a second owner of "where to stand" and the cross-tab says the gun works whenever the feet are at a stand. One trace read the meeting place's "player-not-travelling" as a broken travel test; 4,540 of its 5,459 rows are on still rows, and the moving rows are dominated by "meeting-undecided", which is R2.

Each class closes by construction, and each has its harness row:

- **R1** The follow box becomes the player's intent region, a sense every consumer reads: its centre is the player's feet plus a lead of velocity times a lead time, low-pass filtered and clamped at the screen edge; it grows with the lead, vertically too; the pull is continuous from the centre outward and never exactly zero while the player travels; satisfaction is judged from the ground, never on an airborne tick; reunion, the work radius, the light search centre and the collect radius all measure to the box. Row: a route with an authored straight walk, signed offset along the player's travel direction on moving rows, ahead-rows greater than behind-rows; the 13:27 track replayed, the same statistic, with 10.8% ahead-by-four-tiles (64 px) as the baseline; the 4.7% first written here was the eight-tile figure, and the ledger's ahead-share row is the number from now on.
- **R2** Knowledge is invalidated where it happened: a terrain revision carries its tile, a retained search or flood is restarted only if its explored region contains that tile, and the flood advances on the tick rather than only on a resolve. Row: the 13:27 track replayed, reach-complete share above 90 percent; a fixture that edits a tile outside a flood's region and asserts the flood is untouched; the revision counter recorded per row so the edit rate is a number.
- **R3** One motion forecaster for every moving thing (the player, hostiles and drops through the same observed-motion track), and collection proves its pose at the forecast landing. Row: a drop released mid-air, the walk begins before it lands; drop-has-no-contact-pose at zero on the replayed track.
- **R4** A move in flight is atomic: a voluntary release (Hold, a method change, a new goal) lands the body first and takes effect at the landing; only a pre-empting owner (safety, downing, recovery) takes an airborne body, through the same navigator. Row: the census's cancelled-in-flight count at zero on every route; airborne-no-sideways-speed stops at zero; jump completion above the 0.23.0 rate; the corpus follow pass unchanged.
- **R5** Offer validity is three-valued and time-aware: a stand search that ran out of solves or time returns Unresolved and the incumbent keeps its destination; only a search that solved every candidate returns KnownUnusable; the shot is solved against the target's forecast over a short window, and no stand is offered whose shot window is shorter than the trip. Row: validity-driven switches from the recorded tracks with a declared ceiling; hunt's KnownUnusable share; the decision row carries winner, runner-up and margin.
- **R6** A stand that does not satisfy the request is not an arrival: the partial-progress fallback must beat the navigator's arrival radius from the body's actual feet and must declare its region, or return nothing and hand the request to the state search. Row: follow-vertical-gap with status Arrived never co-occurs.
- **R7** Folded into R5: a firing position exists whenever the arsenal's solver would accept it, and the feet are sent there. Row: fired ticks over ticks with a reachable hostile in range, with a floor; hands-by-activity cross-tab.
- **R8** A chosen destination holds until reached, invalidated or released, and a method change inside one purpose is not a cancellation of the step in hand. Row: journeys reached over asked, per request kind, with a floor.
- **R9** Open and named: the walk chain's entry costs. Row: inside-walk-step stops per minute, recorded now, ceiling declared when the fix is designed.

Instrument findings from the same read, which the harness plan absorbs:

- **I1** `CheckPersistentRejections` reads the retained last-rejection beside a still body and cannot tell a refusal issued this sample from one issued minutes ago; the census counted 4 refusals where the check raised 23 stretches. A refusal event must carry its own tick, and the check reads that.
- **I2** `--replay-water` had no tile hooks and no movement trace (fixed at 6379e27) and has no player track: a follow decision cannot be reproduced without the recorded player, which is the world run's recorded-track mode and not optional.
- **I3** The decision rows already hold every activity's score per comparison; the flip check should compute the margin from them rather than asking a reader to, and the 624 decision events are coalesced records, not a cadence: the chooser runs every tick (choice_id changes 22,240 times).
- **I4** `TerrainChanges.Revision` is not recorded, so the one number R2's whole argument rests on, the edit rate, cannot be read from a capture. The recorder gains it per row.
- **I5** `--compare-jump 3409,627,3411,622` proves no edge over any captured window while the live navigator held a step for that pair with a 27-tick flight; either the windows postdate the live terrain or the generator and the macro proof disagree at that pose. Open; the native replay at tick 17174 is the first check.
- **I6** The first version of this section's "ahead on none" came from a reader's own filter, not from an instrument. The recorded-track row defines "ahead" as the signed offset along the player's travel direction on rows where the player moves, so the number is produced by the harness and not by whoever last opened the file.

## Sources this plan rests on

Luo, Hariri, Eloussi, Marinov, "An Empirical Analysis of Flaky Tests", FSE 2014. Lam, Oei, Shi, Marinov, Xie, "iDFlakies", ICST 2019. Shi, Lam, Oei, Xie, Marinov, "iFixFlakies", ESEC/FSE 2019. Micco, "Flaky Tests at Google and How We Mitigate Them", Google Testing Blog, 2016. Petrović and Ivanković, "State of Mutation Testing at Google", ICSE-SEIP 2018. Stryker.NET documentation, 2026. Zeller and Hildebrandt, "Simplifying and Isolating Failure-Inducing Input", TSE 2002. Regehr et al., "Test-Case Reduction for C Compiler Bugs", PLDI 2012. MacIver and Donaldson, "Test-Case Reduction via Test-Case Generation", ECOOP 2020 (abstract only). MET-MAPF, TOSEM 2024 (abstract only). Pettersson, "Execution monitoring in robotics: A survey", RAS 2005 (abstract only). Mawhorter and Smith, "Automated Testing in Super Metroid with Abstraction-Guided Exploration", FDG 2023. Ecoffet et al., "First return, then explore", Nature 2021. Zhan, Aytemiz, Smith, "Taking the Scenic Route", KEG 2019. Georges, Buytaert, Eeckhout, "Statistically Rigorous Java Performance Evaluation", OOPSLA 2007. Cleland-Huang et al., "Software Traceability: Trends and Future Directions", FOSE 2014. OpenFastTrace. The Ubisoft voxel-versus-navmesh validation paper (arXiv 2605.21397) and the La Forge Go-Explore reachability paper (arXiv 2209.00570) from the earlier industry survey. Not retrieved and cited bibliographically only: Jia and Harman (TSE 2011), McKeeman (DTJ 1998), Chen et al. (CSUR 2018), Chen, Cheung and Yiu (HKUST 1998), Kalibera and Jones (ISMM 2013). The most costly miss is Shi et al., "Mitigating the effects of flaky tests on mutation testing", ISSTA 2019, which is the exact intersection of this suite's two problems and was not reachable.
