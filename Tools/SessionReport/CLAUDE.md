# SessionReport — the reader for a playtest

Follow decisions require `brain_fresh`: when a downed companion stops executing its brain, the previous action and route fields remain sticky. Those rows end a follow-stall interval and cannot count as a newly selected follow response. Missing freshness is reduced coverage, not evidence of an active decision.

The record was never the problem; the reading was. A session is tens of thousands of rows across eighty-odd columns, every diagnosis this project has made from one was a hand-rolled column sum, and the columns move whenever the brain grows a new fact — which on 2026-09-09 produced a reported mean distance of three and a half thousand tiles from an index that had shifted by two while nobody noticed. This tool turns a session into a verdict: what is definitely wrong, what is probably wrong, and what merely looks odd.

```
SessionReport/
├─ CLAUDE.md
├─ SessionReport.csproj   a plain console project; it compiles nothing from the mod tree and references no game assembly, because the record is text
├─ Program.cs             the entry point: resolve the file, describe the session, run every check, print by severity, exit non-zero on a definitive finding
├─ Session.cs             the parsed file — columns addressed by name, each kept as raw text and as a number with NaN where the cell holds none
├─ Finding.cs             a finding, the three severities and what separates them, and the contract a check implements
├─ FindStretches.cs       the shared shape of nearly every check: "this condition held for long enough to matter", with a gap allowance
├─ DescribeSession.cs     the measurement block above the findings, and the reader for the sibling -plans.txt window file
├─ DescribeGodsEyeEvents.cs reads the occurrence sibling, checks completeness, joins projectile launches to contacts and spans the full session
├─ MultiRunReport.cs       preserves each selected run's independent causal and continuous-motion coverage
├─ WritePlaytestHtml.cs    writes a self-contained sampled actor timeline; it draws no terrain the record did not capture
├─ Chronicle.cs           the bounded event-and-interval account of player movement and companion response, with observation and inference kept distinct
├─ ChronicleTests.cs      deterministic synthetic-record tests run through the real parser and chronology reader
├─ CheckTheRecord.cs      is the instrument sane — ticks advancing, the returnable count inside the reach count, every numeric column parsing
├─ CheckTheBody.cs        did the body move when driven, did each proven move take its proven time, was being unable to reach him ever noticed
├─ CheckTheFight.cs       did it see the damage coming, did the hands work while threatened, was the fired weapon the higher-scoring one, did it go down
├─ CheckTheChoices.cs     did hunting stay on his screen, did the action board get used, was the torch in the hand during a fight, did the brain fit in a frame
├─ CheckTheInstrument.cs  was the body ever held in place with a velocity it never spent, do the offline motion rule and the engine still agree, and was every kind of move the plan offered ever actually made
└─ CheckFollowProgress.cs separates an unsatisfied follow objective with no navigator progress from a valid detour, and measures observed response time after departure
```

## The operating manual

The HTML viewer keeps source-run identity, a sample scrubber, exact tick entry, event-kind filtering, previous/next event navigation, every sampled TSV field, and full retained event payloads. It streams sparse events across the entire run into bounded per-kind reservoirs so cosmetic terrain contacts cannot erase a late injury or death. Every omitted sample/event, malformed row, missing sidecar and missing normal closure is disclosed. HTML is a bounded inspection view; the ordinary and multi-run reports still analyse the full source records and share definitive-finding exit semantics. The writer's caps own output size; the UI never implies that its sampled trail is a complete recording or draws invented terrain.

```
dotnet run --project Tools/SessionReport -- Telemetry/<stamp>.tsv
dotnet run --project Tools/SessionReport -- Telemetry          # the newest session in the folder
dotnet run --project Tools/SessionReport -- --timeline Telemetry/<stamp>.tsv  # complete chronological account
dotnet run --project Tools/SessionReport -- --multirun Telemetry/<stamp-a>.tsv Telemetry/<stamp-b>.tsv  # each run keeps its own coverage; exits 1 if any ordinary-report definitive finding exists
dotnet run --project Tools/SessionReport -- --html /tmp/aic-playtests.html Telemetry/<stamp-a>.tsv Telemetry/<stamp-b>.tsv  # headless, self-contained actor scrubber
dotnet run --project Tools/SessionReport -- --self-test        # deterministic chronology parser and narration tests
```

Exit 0 when nothing definitive was found and 1 when something was, so the run is a check and not only a report. The one-run folder form remains a newest-run convenience. `--multirun` and `--html` expand every `.tsv` in each supplied folder, preserving first and last captures rather than silently reducing a folder to one session; an unreadable selected TSV remains in the multi-run output as unavailable coverage and makes its verdict non-clean. The sibling `<stamp>-plans.txt` is read automatically when it sits beside the `.tsv`, and its window count and reasons are tallied in the header block with the replay command that opens them.

## The chronology says what the record saw, then labels the inference it earns

New records begin with a schema and UTC start metadata, then carry `wall_elapsed_ms` from the recorder's `Stopwatch` on every phase-labelled sample. `observed_*` is the companion body captured at AI entry, `predicted_*` is the motor result before helpers, and `npc_px` is an after-helper compatibility field; `Chronicle` uses the observed body for actual motion. Divergence readings carry `diverge_valid` plus `diverge_invalid_reason`, so a reader excludes a prediction invalidated by an external hit, a life change, terrain change, or missing prior prediction. It coalesces equal state into intervals and prints changed liquid, support, activity, control source and hit events in tick order beside actual elapsed wall time. It calls a slope ascent or descent only when a floor-slope support and matching vertical displacement occur together, and it names that as an inference; it never guesses pond, hill or stairs from coordinates. Player and companion hit events record final damage, direction and knockback; the player event names a source where Terraria exposes one, while the NPC hit callback has no attacker source and says so. A sustained active, non-reflex control request with one stable spot and no observed entry-body movement is reported as an **inferred lack of progress**, including its wall-time and tick interval; an intentional hold is not a failure. Follow diagnosis is separate: it needs an unsatisfied valid follow objective and no completed route step in the current window. Increasing direct distance is insufficient, because a valid C-turn can initially widen the gap while it completes waypoints. Remaining route time is displayed but never certifies progress, since it can fall while a frozen body spends an active step. Recovery-flight rows are excluded so stale `WithPlayer` state cannot become an ordinary follow failure. A distant moving player without later `walk-with` selection is an observed response-coverage oddity measured by `wall_elapsed_ms`, never frame time or automatic proof that work, guard, or recovery was wrong. Old files print one explicit unavailable line with every missing column, because partial chronology would otherwise read like an account of events the recorder never observed. The normal report is capped; `--timeline` prints every chronological event.

## Severity is decided by evidence, not by how bad it feels

The event reader consumes the sibling `-events.jsonl` automatically. It validates required schema fields, monotonic sequence numbers and explicit session start/end markers. Missing, malformed or interrupted capture is printed as reduced coverage. Default output groups meaningful events across the entire run; `--timeline` preserves each occurrence beside the TSV's continuous movement chronology. Projectile identity includes its spawn generation, so a reused slot cannot connect one shot to another shot's impact. Terrain contact before any recorded enemy contact is reported as an observed obstruction with launch intent and actual impact coordinates; a piercing projectile hitting terrain after an enemy is not labelled as that failure. The reader does not infer the cause from contact alone. Terrain is locally sampled, and unknown regions or sampling delays remain limits on reconstruction.

The categories are the whole point. A report that says "take a look around tick 3000" hands the reading back to whoever asked for it, so every finding lands in one of three places and the boundary between them is what would settle the question.

| | what it means | example |
|---|---|---|
| **definitive** | wrong by construction: the record contradicts itself, or the behaviour breaks a stated rule, and no situation makes it correct | damage taken in a tick scored as danger zero; a returnable count above the reach count; a body driven at 0.9 px/tick that never moves |
| **potential** | wrong in every situation anyone has thought of, and possibly right in one nobody has; the finding says what would settle it | a stretch under threat with nothing fired; a move that took four times its proven ticks; a row holding the lower-scoring weapon, which `TryFire`'s fallback can produce legitimately |
| **oddity** | a shape in the numbers with no rule behind it — for reading, not for fixing, and where the defects nobody has met yet surface first | one action taking most of the session; the torch out in a fight; the session's damage and cost baselines |

Every check is grown from a defect that actually happened, and none exists for a defect that has not. That is deliberate: a check written against an imagined failure fires on the shape its author imagined rather than on the failure, and the cost is paid in every report afterwards.

## Two properties keep it from lying, and both were bought with a defect

**Columns are addressed by name and never by index.** New facts land in the middle of the header whenever the brain grows one, so an index that was right last week reads the neighbouring column this week and reports a number that is wrong by a factor nobody can guess. The parser strips the byte-order mark the writer's UTF-8 stream puts in front of the first column name, without which `tick` is never found and every check reports itself unmeasurable.

**A check declares the columns it cannot work without, and is skipped loudly when one is absent.** Zero findings and zero coverage look identical in a report, so the coverage block prints before the findings and names the missing column for every skipped check. Running against a session from before a column existed is the normal case, not an error: an eight-column-narrower file from 2026-09-08 runs nine of the fourteen checks and says which five it could not.

The cells are not all plain numbers, and a float parse throws on a third of the file: life is `86/100`, breath carries a trailing `u` underwater, self danger a trailing `L` in lava, horizon is the word `inf` with no threat in the world, and the tiles and velocities are `x,y` pairs. `Session.ParseNumber` takes the leading number out of every one of those shapes and the suffix is read from the text where it matters; a cell that survives the stripping and still holds no number is counted, and a column with any such cell is reported, because that is the writer and the reader having quietly disagreed.

## What it found on the session it was written against

Run against `2026-09-09_12-29-48.tsv`, the file whose two defects had already been found by hand over a long afternoon, the first version of this tool raised both unprompted plus one nobody had looked for: 774 ticks of hunting at up to 84 tiles from the player, 8 of 10 hits landing while the danger column read zero, and three stretches totalling nearly seven hundred ticks where the body sat at one pixel while being driven at 0.9 px/tick — the platform fall-through freeze, in the same file, in a stretch nobody had opened. That is the pass line the tool was built against and it is worth restating whenever a check is added: a reader that cannot find the defects already known from the file they are in has no coverage, however many checks it lists.

It also found a defect in the instrument on its first run. The scenario capture's dodge detector compared `tick - lastDodgeTick` against a memory window with `lastDodgeTick` initialised to `long.MinValue`, so the subtraction overflowed a signed long and wrapped to a large negative number that passed the test: every hit in a session where no reflex had ever fired was dumped as a hit through a dodge, and the age printed in the window header was −9,223,372,036,854,773,868 ticks. The fix is in `../../Companion/Brain/BehaviourDiagnostics/CaptureMovementScenario.cs` and the tell was visible only because this tool prints the window reasons.

## A hit the world dealt is not a hit the companion failed to see

Lava, fire and a drained breath bar all take life with nothing alive in the room, so the threat sense is *right* to read zero on those ticks, and counting them as unseen hits would turn every cave session into a page of definitive findings. The damage check excludes them the way `ScenarioCapture` does — the `L` and `f` suffixes on `self_danger` — plus drowning, which the capture has no reason to exclude because a dodge cannot cause it. Being under water is not the test, and the session of 2026-09-08 is why: it holds two real hits at breath 0.85 and 0.98, so excluding on the `u` suffix alone would have suppressed two true findings. Drowning is the suffix *and* an empty bar.

Neither exclusion has fired on a real session, because no playtest has yet taken environmental damage. It was exercised on a probe instead: the same 2026-09-08 file with three of its real hits rewritten to carry the lava suffix, which moved the check from eleven hits to eight and named the three as excluded, while the header's own life-lost measurement stayed at eleven events because that number is what the body lost and not what it failed to see.

## Traps

- **A check that throws is caught and reported as a finding against the reader**, never swallowed, because a broken check returning nothing is indistinguishable from a clean run.
- **A gap allowance is wrong wherever the break in the run is the event being looked for.** `TheHandsWorkWhileThreatened` looks for ticks with nothing fired, so a tick that fired is the boundary of the stretch and not a flicker to be smoothed over; with an allowance of two, the reload ticks between two shots satisfy the condition and the single firing row between them falls inside the allowance, so a healthy two-minute exchange folds into one enormous "nothing fired" finding whose own tally lists the shots it fired. It was written with an allowance because every other check in the file wants one, and it survived a first run only because that session predates the `fire` column and the `shot` flag was sticky through a reload there — which is the shape of defect a check cannot be tested for on a file older than the column it reads.
- **The edge columns are sticky.** One finished move fills every row until the next finishes, so anything reading them deduplicates on `edge_n` changing or reports one overrun a thousand times. `plan_ms` and `flood_ms` are sticky the same way and overstate any per-tick share taken from them; the phase columns are the per-tick ones.
- **`npc_tile` uses `(int)(bottom / 16)` and the planner's feet rows use `(bottom - 1) / 16`.** They differ by one wherever the body's bottom sits exactly on a tile boundary, so tile columns from the two conventions are never compared for equality. Freezes are detected from `npc_px`, which has no convention problem.
- **Time on the floor is not a count of deaths.** 766 rows reading `downed` is one down lasting thirteen seconds, and reading the rows as the count produced "downed 613 times" in a report on 2026-09-09. Downs are counted as transitions into the state.
- **One check saying the same thing five times moves the reading cost rather than removing it**, which is the failure this tool exists to fix, so repeats past three fold into one line with their tick ranges.

## Where a new check goes

**A check is a detector, and a detector can only find a failure somebody imagined, so the report opens with something that is not one.** Every class here fires on a threshold a person chose, which means a behaviour nobody thought to threshold produces no finding at all — and zero findings is indistinguishable from zero coverage, which is the failure the coverage block was already built to name for *missing columns* and could not name for missing questions. On 2026-09-09 the follower completed 1,227 walks, eight drops, no jumps and no fall-throughs in nine minutes; every number was in columns 85 to 90, nothing asked, the session read clean. So the mod writes a census beside each session counting every category whether or not anything happened in it, and this tool prints it above every finding: `Jump: planned 47, begun 1, completed 0` is a row nobody reads past. A missing census is reported by name rather than passed over, because an absent one and an empty one mean opposite things.

When adding a check, prefer the form with no threshold in it where one exists. `EveryMoveOfferedGetsMade` asks whether a kind of move that was offered was ever once completed, which has no number to tune and therefore no failure it is blind to; it fires on the 2026-09-09 session retroactively, which is the only proof a check ever really has.

A file per family of question, a class per check, and the class name is the question: `TheBodyMovesWhenDriven`, `HuntingStaysOnHisScreen`. Add it to the array in `Program.cs`, declare every column it reads in `Needs` (including the ones it only reads for the finding's detail, or an old file crashes it instead of skipping it), and put the threshold in a named constant with the reason it is that number in its own comment. State the threshold inside the finding's text as well, because a finding that cannot be argued with is one that gets believed when it is wrong.
