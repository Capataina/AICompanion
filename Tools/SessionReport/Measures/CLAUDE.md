# Measures — the numbers a play holds, produced by an instrument rather than by a reader

A check in `../Checks/` decides: it knows a rule the producer guarantees and reports a record that breaks it, so it can be Definitive with no threshold in it. A measure decides nothing. It says the companion trailed the travelling player by more than three tiles on 71.5% of rows, and whether that is bad is a question about the design which the verification plan answers and these files do not.

Keeping the two apart is the point rather than tidiness, because folding them together is how a number nobody declared becomes a pass line by accident. So every row here carries a value and never a verdict, and the plan's declared pass lines travel as tags on the row.

```
Measures/
├─ CLAUDE.md
├─ Measure.cs                     the contract, the row shapes, and the reading conventions all three share
├─ MeasureFollowingAndPlaces.cs   where the companion sat, what it knew it could reach, where its journeys ended
└─ MeasureCommitmentAndChoice.cs  what was cancelled mid-move, why the chosen activity changed, what the hands did
```

`../Program.cs` runs them in the ordinary report and alone under `--measures <capture>`, which is what `../../backfill-capture.sh` calls.

## Why these exist at all, which is a defect rather than a design

Two numbers written into the research record about the 13:27 capture of 14 September came from a reader's own filter over the file rather than from any tool, and both were wrong. The first read said the companion was "ahead on none" of the moving rows; the correction that replaced it says 4.7% and is also wrong, because 4.7% is the eight-tile figure and the sentence it sits in says four tiles — the instrument measures 10.80%. The second says 23 stretches of arriving at a partial destination and holding; under the filter that same passage states, the capture holds exactly one.

After these files, the number in a report is the number the harness produced, and a disagreement between a report and a hand reading is a defect in one of them that somebody can go and settle.

## Conventions that were each a way to get the arithmetic wrong

- **A cell is parsed as a double straight from its text, never through the parsed float column.** A float carries about seven significant digits and a world coordinate here has five before the point, so `54334.46` comes back rounded and the median of eight thousand offsets moves by three pixels. Worse, a float widened back to a double is not the number written: `1.20` becomes 1.2000000476837158, which is greater than 1.2, so a "faster than 1.2 px/tick" test admitted 114 rows recorded at exactly the floor and changed the denominator of every share taken from them.
- **`npc_px` and `player_px` are both `Bottom`, whose X is the body's centre and not its left edge.** Taking both from the same convention is what makes their difference mean anything; reading either as a left edge shifts every offset by half a body width, which produced a confident wrong diagnosis of the platform freeze once already.
- **"Ahead" is signed along the player's direction of travel, not along the x axis.** A raw x difference calls the companion ahead whenever the player walks left, which is how "ahead" stops meaning anything.
- **A sticky field is counted by its changes, never by its rows.** The navigator's last edge stands until another replaces it, so consecutive identical reports are one cancellation observed repeatedly rather than a cancellation happening repeatedly; counting rows multiplies one defect by however often the sampler ran.
- **An offer is read on the flip tick and belongs to the outgoing activity.** Reading the previous row's offer measures the world one tick before the decision that used it, and it split the same 250 switches 203-to-47 instead of 121-to-129 — sending a reader after a chooser defect that was not there.
- **A raw score of zero is invalid whatever the eligibility word says**, because the evaluator multiplies its considerations and any zero vetoes the activity outright.
- **An activity's share is read from the decision occurrences, not from the rows**, wherever the column is retained between comparisons: a row count weights each decision by how long it happened to be held.
- **A measure that cannot run is skipped by name and never returns zero.** `terrain_revision` is the live case: no capture on disk carries it, and counting terrain-snapshot occurrences instead is what produced the wrong "426 terrain edits" reading, because those run on a fixed per-tick cadence and count captures rather than edits.

## The before-numbers are pinned against a real recording

`../Tests/PlayMeasureTests.cs` asserts twenty-four values and one named skip against the 13:27 capture. It is a real recording rather than a synthetic fixture because real play is sparse and clustered — one activity owns three quarters of that session, the player stands still for half of it, the reach flood is unfinished on 44% of rows — and a measure tuned on generated rows returns a perfect number against a shape that never occurs.

`Telemetry/` is gitignored, so the test skips loudly when the capture is absent and names what would have been proved. It never reads as green.
