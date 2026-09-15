# Measures — the numbers a play holds, produced by an instrument rather than by a reader

A check in `../Checks/` decides: it knows a rule the producer guarantees and reports a record that breaks it, so it can be Definitive with no threshold in it. A measure decides nothing. It says the companion trailed the travelling player by more than three tiles on 71.5% of rows, and whether that is bad is a question about the design which the verification plan answers and these files do not.

Keeping the two apart is the point rather than tidiness, because folding them together is how a number nobody declared becomes a pass line by accident. So every row here carries a value and never a verdict, and the plan's declared pass lines travel as tags on the row.

```
Measures/
├─ CLAUDE.md
├─ Measure.cs                     the contract, the row shapes, and the reading conventions all three share
├─ MeasureFollowingAndPlaces.cs   where the companion sat, what it knew it could reach, where its journeys ended
└─ MeasureCommitmentAndChoice.cs  where the body stopped for nothing the record names, why the chosen activity changed, what the hands did
```

`../Program.cs` runs them in the ordinary report and alone under `--measures <capture>`, which is what `../../backfill-capture.sh` calls.

## Why these exist at all, which is a defect rather than a design

Two numbers written into the research record about the 13:27 capture of 14 September came from a reader's own filter over the file rather than from any tool, and both were wrong. The first read said the companion was "ahead on none" of the moving rows; the correction that replaced it says 4.7% and is also wrong, because 4.7% is the eight-tile figure and the sentence it sits in says four tiles — the instrument measures 10.80%. The second says 23 stretches of arriving at a partial destination and holding; under the filter that same passage states, the capture holds exactly one.

After these files, the number in a report is the number the harness produced, and a disagreement between a report and a hand reading is a defect in one of them that somebody can go and settle.

## Conventions that were each a way to get the arithmetic wrong

- **A cell is parsed as a double straight from its text, never through the parsed float column.** A float carries about seven significant digits and a world coordinate here has five before the point, so `54334.46` comes back rounded and the median of eight thousand offsets moves by three pixels. Worse, a float widened back to a double is not the number written: `1.20` becomes 1.2000000476837158, which is greater than 1.2, so a "faster than 1.2 px/tick" test admitted 114 rows recorded at exactly the floor and changed the denominator of every share taken from them.
- **The two positions are not written from the same point, and a vertical comparison between them says so.** `npc_px` is the orb's centre, which is the only point it has; `player_px` stays the player's `Bottom`, whose X is the body's centre and not its left edge. Both X values are therefore centres and their horizontal difference means what it says, while their Y values differ by half the player's height. Reading either X as a left edge shifts every offset by half a body width, which produced a confident wrong diagnosis of the platform freeze once already.
- **A column can change meaning without changing name, and the coverage block cannot see it.** `npc_px` is the case in hand: under the walking body it was an after-helpers compatibility position anchored on the feet, and it is now the centre the navigator and the success region are both handed. A measure or a check whose arithmetic depends on which of those it is names a column only the orb's row carries — `touched_wall`, `clearance`, `desired_vel`, `route_remaining_px` — so an older capture skips instead of producing a confident number about a body the game no longer has.
- **"Ahead" is signed along the player's direction of travel, not along the x axis.** A raw x difference calls the companion ahead whenever the player walks left, which is how "ahead" stops meaning anything.
- **A sticky field is counted by its changes, never by its rows.** A sampled field stands until another sample replaces it, so consecutive identical reports are one event observed repeatedly rather than an event happening repeatedly; counting rows multiplies one defect by however often the sampler ran.
- **An offer is read on the flip tick and belongs to the outgoing activity.** Reading the previous row's offer measures the world one tick before the decision that used it, and it split the same 250 switches 203-to-47 instead of 121-to-129 — sending a reader after a chooser defect that was not there.
- **A raw score of zero is invalid whatever the eligibility word says**, because the evaluator multiplies its considerations and any zero vetoes the activity outright.
- **An activity's share is read from the decision occurrences, not from the rows**, wherever the column is retained between comparisons: a row count weights each decision by how long it happened to be held.
- **A measure that cannot run is skipped by name and never returns zero.** `terrain_revision` is the live case: no capture on disk carries it, and counting terrain-snapshot occurrences instead is what produced the wrong "426 terrain edits" reading, because those run on a fixed per-tick cadence and count captures rather than edits.

## Two measures were deleted rather than rewritten, and the reason is the producer

`cancelled-in-flight` counted moves released by their own owner while the body was off the ground, reading `last-edge=` and `preparation=` out of a `movement-state` sample. `arrived-with-follow-gap` counted stretches of arriving at a destination that did not satisfy the request, reading the positioner's `partial-progress-candidate` choice reason. The recorder writes neither key now: the navigator carries a route of free-space corners rather than a proved edge with a preparation in front of it, and a spot is a corner node the flood reached rather than a partial step toward one. Both quantities are gone with the body they described, and a measure left reading a name nobody writes returns zero for ever — which this harness treats as worse than a red, because a fixed zero reads as a defect that was fixed.

`airborne-no-sideways-speed-stops` survives as `unexplained-stops`, and the rename is the same point in the other direction. The producer attributes a stop in a strict precedence — `during-replan` for a route replaced under a still body, `against-wall` for a body pressed against a wall throughout, `other` for anything it cannot explain — so the row worth a pass line is the one named by exclusion. Keeping the old case name would have kept a row that could only ever read zero.

## The before-numbers are unpinned, and the file says so on every run

`../Tests/PlayMeasureTests.cs` used to assert twenty-four values against the 13:27 capture of 14 September 2026. Every one of them was read off a walking-body recording at schema 0.33.0, so none survives the change of body: four fed measures that no longer exist, and the rest were taken from columns whose meanings moved. The table is empty until the first orb playtest.

Two rules keep that emptiness honest, and both exist because the alternative looks like success. An empty table files a `skipped` row rather than a pass, because every assertion in that file is a loop over the table and a run with nothing in it satisfies all of them. And a capture below schema 0.34.0 files a `skipped` row naming the schema it found, because an old capture still carries every column name a surviving measure asks for — it would run to the end and produce a full set of confident numbers about a body the game does not have. That second case is the dangerous one precisely because it does not look damaged.

`Telemetry/` is gitignored, so an absent capture skips loudly too and names what would have been proved. None of the three paths ever reads as green.
