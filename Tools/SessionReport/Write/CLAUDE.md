# Write — reports for playtest inspection

These classes render a session's evidence as human-readable output. Both draw only what the record captured, making no inference beyond what the read and check layers provide.

```
Write/
├─ MultiRunReport.cs       compares runs by schema, source revision and configuration; prints shared and differing facts
├─ WriteCourseTimeline.cs  one row per decision: what the census admitted, what the course ordered and bound, what it cost
└─ WritePlaytestHtml.cs    self-contained HTML with scrollable event timeline, tick navigation and per-kind filtering
```

**`WriteCourseTimeline` and `../Read/DescribeCourseDecisions` narrate the same session and answer different questions, and the split is worth stating because two pages describing one record is how documentation drifts.** The narration reads the `course-decision` occurrences and coalesces them on *what was decided* — reason, activity, bound purpose — which answers "what was it doing at tick 8127". The timeline is keyed on the decision *identity* the rows carry in `choice_id` since schema 0.43.0, and joins to each the four things the occurrence payload does not hold: the per-domain census admission from the `decision` occurrence, the published order and its runner-up from the row, and what the decision cost. Those are what every reading of the 22 September 2026 capture had to assemble by script.

Three properties of that table were each a way to get it wrong. **A decision's payload is not on its own `choice_tick`** — the identity advances when a decision is reached and the payload is written when an outcome is traced, so 2,340 payloads landed on 1,364 ticks of that capture and a lookup keyed on the decision tick printed dashes on about half the rows; the payloads inside a decision's own tick span are its own. **A decision that traced no payload is not a difference**, so it folds into the run it sits in and the count of undescribed decisions is stated once in the header, because treating the absence as a value split the table into a hundred runs a third of which were one blank decision each. And **the abridged view keeps both ends**, because a session's diagnosis is usually at its end: the first thirty runs of that capture stop at tick 788 and hide the row the whole report is read for, which is 258 decisions from tick 1,821 binding nothing while the census admitted three combat targets and four drops.

The HTML scrubber plots one companion position per sample, `npc_px`, which is the orb's centre in whole pixels. It no longer reconstructs a centre from a left edge and a width, because the record carries neither and there is no second body to plot beside the first. A sample whose `npc_px` is absent or unparseable leaves the companion trail empty for that tick rather than drawing a body at the origin.

The HTML viewer is a bounded inspection surface: it holds enough context to diagnose one session, preserves event ordering and tick identity, and discloses every loss (cut rows, malformed entries, missing sidecars, interrupted capture). The multi-run report establishes whether runs can be compared and what differed between them; both kinds exit 0 when no definitive finding exists and 1 when one does, so they are checks and not only reports.
