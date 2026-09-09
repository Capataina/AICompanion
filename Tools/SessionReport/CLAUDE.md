# SessionReport — the reader for a playtest

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
├─ CheckTheRecord.cs      is the instrument sane — ticks advancing, the returnable count inside the reach count, every numeric column parsing
├─ CheckTheBody.cs        did the body move when driven, did each proven move take its proven time, was being unable to reach him ever noticed
├─ CheckTheFight.cs       did it see the damage coming, did the hands work while threatened, was the fired weapon the higher-scoring one, did it go down
└─ CheckTheChoices.cs     did hunting stay on his screen, did the action board get used, was the torch in the hand during a fight, did the brain fit in a frame
```

## The operating manual

```
dotnet run --project Tools/SessionReport -- Telemetry/<stamp>.tsv
dotnet run --project Tools/SessionReport -- Telemetry          # the newest session in the folder
```

Exit 0 when nothing definitive was found and 1 when something was, so the run is a check and not only a report. The sibling `<stamp>-plans.txt` is read automatically when it sits beside the `.tsv`, and its window count and reasons are tallied in the header block with the replay command that opens them.

## Severity is decided by evidence, not by how bad it feels

The categories are the whole point. A report that says "take a look around tick 3000" hands the reading back to whoever asked for it, so every finding lands in one of three places and the boundary between them is what would settle the question.

| | what it means | example |
|---|---|---|
| **definitive** | wrong by construction: the record contradicts itself, or the behaviour breaks a stated rule, and no situation makes it correct | damage taken in a tick scored as danger zero; a returnable count above the reach count; a body driven at 0.9 px/tick that never moves |
| **potential** | wrong in every situation anyone has thought of, and possibly right in one nobody has; the finding says what would settle it | a stretch under threat with nothing fired; a move that took four times its proven ticks |
| **oddity** | a shape in the numbers with no rule behind it — for reading, not for fixing, and where the defects nobody has met yet surface first | one action taking most of the session; the torch out in a fight; the session's damage and cost baselines |

Every check is grown from a defect that actually happened, and none exists for a defect that has not. That is deliberate: a check written against an imagined failure fires on the shape its author imagined rather than on the failure, and the cost is paid in every report afterwards.

## Two properties keep it from lying, and both were bought with a defect

**Columns are addressed by name and never by index.** New facts land in the middle of the header whenever the brain grows one, so an index that was right last week reads the neighbouring column this week and reports a number that is wrong by a factor nobody can guess. The parser strips the byte-order mark the writer's UTF-8 stream puts in front of the first column name, without which `tick` is never found and every check reports itself unmeasurable.

**A check declares the columns it cannot work without, and is skipped loudly when one is absent.** Zero findings and zero coverage look identical in a report, so the coverage block prints before the findings and names the missing column for every skipped check. Running against a session from before a column existed is the normal case, not an error: an eight-column-narrower file from 2026-09-08 runs nine of the fourteen checks and says which five it could not.

The cells are not all plain numbers, and a float parse throws on a third of the file: life is `86/100`, breath carries a trailing `u` underwater, self danger a trailing `L` in lava, horizon is the word `inf` with no threat in the world, and the tiles and velocities are `x,y` pairs. `Session.ParseNumber` takes the leading number out of every one of those shapes and the suffix is read from the text where it matters; a cell that survives the stripping and still holds no number is counted, and a column with any such cell is reported, because that is the writer and the reader having quietly disagreed.

## What it found on the session it was written against

Run against `2026-09-09_12-29-48.tsv`, the file whose two defects had already been found by hand over a long afternoon, the first version of this tool raised both unprompted plus one nobody had looked for: 774 ticks of hunting at up to 84 tiles from the player, 8 of 10 hits landing while the danger column read zero, and three stretches totalling nearly seven hundred ticks where the body sat at one pixel while being driven at 0.9 px/tick — the platform fall-through freeze, in the same file, in a stretch nobody had opened. That is the pass line the tool was built against and it is worth restating whenever a check is added: a reader that cannot find the defects already known from the file they are in has no coverage, however many checks it lists.

It also found a defect in the instrument on its first run. The scenario capture's dodge detector compared `tick - lastDodgeTick` against a memory window with `lastDodgeTick` initialised to `long.MinValue`, so the subtraction overflowed a signed long and wrapped to a large negative number that passed the test: every hit in a session where no reflex had ever fired was dumped as a hit through a dodge, and the age printed in the window header was −9,223,372,036,854,773,868 ticks. The fix is in `../../Brain/Debug/ScenarioCapture.cs` and the tell was visible only because this tool prints the window reasons.

## Traps

- **A check that throws is caught and reported as a finding against the reader**, never swallowed, because a broken check returning nothing is indistinguishable from a clean run.
- **The edge columns are sticky.** One finished move fills every row until the next finishes, so anything reading them deduplicates on `edge_n` changing or reports one overrun a thousand times. `plan_ms` and `flood_ms` are sticky the same way and overstate any per-tick share taken from them; the phase columns are the per-tick ones.
- **`npc_tile` uses `(int)(bottom / 16)` and the planner's feet rows use `(bottom - 1) / 16`.** They differ by one wherever the body's bottom sits exactly on a tile boundary, so tile columns from the two conventions are never compared for equality. Freezes are detected from `npc_px`, which has no convention problem.
- **Time on the floor is not a count of deaths.** 766 rows reading `downed` is one down lasting thirteen seconds, and reading the rows as the count produced "downed 613 times" in a report on 2026-09-09. Downs are counted as transitions into the state.
- **One check saying the same thing five times moves the reading cost rather than removing it**, which is the failure this tool exists to fix, so repeats past three fold into one line with their tick ranges.

## Where a new check goes

A file per family of question, a class per check, and the class name is the question: `TheBodyMovesWhenDriven`, `HuntingStaysOnHisScreen`. Add it to the array in `Program.cs`, declare every column it reads in `Needs` (including the ones it only reads for the finding's detail, or an old file crashes it instead of skipping it), and put the threshold in a named constant with the reason it is that number in its own comment. State the threshold inside the finding's text as well, because a finding that cannot be argued with is one that gets believed when it is wrong.
