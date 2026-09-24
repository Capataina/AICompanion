# Read — session parsing and chronology

The parser, stretch finder, God's Eye sidecar, attempt-identity join, chronicle and session description. Checks import these; they do not decide findings. These modules resolve raw telemetry into a queryable session structure and establish what the recording captured and in what order it arrived.

```
Read/
├─ Chronicle.cs             coalesces identical state into intervals and names the inference it earns
├─ DescribeCombatAudit.cs   the report's Combat decisions section, read back out of CombatAudit's sidecar
├─ DescribeGodsEyeEvents.cs reads the `-events.jsonl` sidecar for projectile and terrain contact records
├─ DescribeSession.cs       session metadata, schema witness, closure status, recording cost, loss distribution, each activity's candidate funnel by stage — or, from schema 0.46.0, one line saying that funnel was retired with the family chooser, because a section that silently disappears is this header failing at its own job — and whether a machine rather than a person produced the capture
├─ FindStretches.cs         runs of consecutive rows satisfying a predicate, with an optional gap allowance
├─ JoinAttemptEvidence.cs   reads attempt outcomes, grants and strikes by their process-wide identity counters
├─ DescribeCourseDecisions.cs the course as a story: decisions coalesced into runs by reason, activity and purpose
├─ ReadCourseChronicle.cs   validates typed course snapshots and distinguishes complete input from missing evidence
├─ ReadCourseDecisions.cs   the typed `course-decision` payload as a record, with unreadable payloads counted
├─ ReadGodsEyeEvents.cs     keeps the last file read while path, write time and length are unchanged; streams events by line
├─ ReconstructTheSceneAtATick.cs where the bodies, the region, the steering target, the goal, the hostiles and the drops stood at one tick, each with its source and age
├─ ReconstructTerrainWindow.cs (linked from ../../NavReplay) the terrain around a recorded moment rebuilt from the capture's snapshots
└─ DescribeWhereTheTimeGoes.cs the report's "where the time goes" block: section shares, allocation, collections, and the spike ticks with their sidecar trees, from schema 0.48.0
```

**`ReconstructTheSceneAtATick` places a hostile or a drop only where an occurrence said where it stood, and the newest captures hold the fewest such occurrences.** Up to schema 0.45.0 the `candidate-funnel` occurrence named every hostile the combat census held, by slot and tile, whenever the list changed, and every drop the collection census held; 0.46.0 retired it with the family chooser. After that a hostile is placed only at its `npc-spawn`, `npc-damage` and `npc-death` and inside a `combat-snapshot`, and a drop at `drop-sighted` or in a funnel, retired by a `pickup` of the same type within three tiles. `npc-damage` carries no slot, so the slot is learned from the spawn that minted the damage record's `subject`. **A placed NPC is called hostile only if the combat census or a combat snapshot held its slot after its current occupant spawned**, because `npc-spawn` names squirrels, fireflies and bunnies as readily as zombies and the record has no other witness of hostility; the rest are `OtherNpcs`, and on the 22 September capture they are a squirrel and three fireflies. A sighting older than 300 ticks is counted and not placed, and each placed one carries its source and age, because a funnel entry is a tile centre and five seconds is long enough for a zombie to have left. **A world run at 0.48.0 writes no spawn, damage or death at all** — its staged actors never pass through the spawn hook — so on the world run of the 22 September capture the only hostile positions are the one `combat-snapshot` at tick 680 (measured 24 September 2026). **No schema records a route's corners**: `route_points` is a count, so the scene carries the steering target (`lookahead`), the destination tile (`spot`) and the navigator's goal tile from `movement-state`, and nothing is drawn between them that the record did not hold.

**`DescribeWhereTheTimeGoes` reads the same `Profile` as `../Measures/MeasureWhereTheTimeGoes.cs`**, so the block and the ledger rows cannot disagree, and adds the one thing only the sidecar holds: each `cost-spike` occurrence's whole section tree, whose largest self times name what the worst ticks were spent on. A capture below 0.48.0 prints one `unrecorded` line naming the schema it declares.

**Three files read a course and they answer different questions in a fixed order, which is the whole reason there are three.** `ReadCourseChronicle` is the gate and asks whether the evidence can be trusted at all — schema, envelope, snapshot manifests, terminal closure — returning coverage and never a story. `ReadCourseDecisions` parses the `course-decision` payload into a record and counts what it could not read rather than dropping it. `DescribeCourseDecisions` narrates those records, coalescing a run of ticks holding one decision into one line. The gate prints above the story in `Program`, deliberately, so a story is never read without the coverage statement saying how much of it is there. `Checks/CheckCourseDecisions.cs` and `Measures/MeasureCourseWork.cs` are the other two consumers of the same parse.

The coalescing key is the reason, the activity and the bound purpose, and it is not the whole record: order counts and refusal tallies move every tick in a live fight, so keying on them would produce one line per tick and call it a story. A published tick and a retained tick on one course are deliberately *not* one run, because "it decided this" and "it kept deciding this" are the two things a reader of a capture is trying to tell apart.

**Nothing on disk has ever exercised this.** The newest capture is schema 0.40.0 and typed course evidence begins at 0.41.0, so every course reader here is skipped by name on every recording that exists as of 21 September 2026, and the first play of the wired course brain is the first real input any of them will see. The synthetic groups in `Tests/ChronicleTests.cs` prove each rule by mutation; they do not establish that a real capture parses.

`ReadCourseChronicle.cs` is the retained-course gate. It treats a schema before 0.41.0 as historical-unavailable and a missing, malformed, truncated or envelope-incomplete sidecar as explanatory-partial. It never turns absent course records into an empty course timeline. Exact-input-complete additionally deserialises every immutable decision snapshot and runs the producer's manifest validator: actual values hash to their declared digests, every expected read has one matching captured value, and the aggregate digest covers context, model, scheduler, random state and manifests. Unknown payload versions are not replay inputs. The last terminal writer status must certify complete delivery and its row count must match the capture. A digest-only or empty manifest remains explanatory-partial. A snapshot occurrence with a null, missing or non-object snapshot is malformed, even beside another complete snapshot. Source tick, native phase, observation ordinal and receipt watermark must match the enclosing event. This validates recorded inputs; it does not execute a decision replay or certify that an uninstrumented producer recorded every dependency it should have read.

**`DescribeSession.Synthetic` is the one reader of the `# synthetic=` marker**, and both the summary and the play-measure pins consume it, so a capture one of them refuses as play cannot be play to the other. `../CLAUDE.md` carries what the marker means and why the note's own semicolons broke the first parser; the shape of the rule here is that the producer is the value's first segment and nothing later can become it.

## There is one body, and the chronicle reads one position for it

The companion's state comes from `npc_px`, the orb's centre in whole pixels, and `npc_vel` beside it. That is the whole of it: no second predicted body, no divergence reading to qualify, no support to change, and no left edge and width to reconstruct a centre from. A capture that predates this reads as unavailable rather than being narrated from the columns that happen to share a name, which is what `liquid` in the chronicle's required list is doing — it is the witness that this capture recorded the orb.

The player is still a walking body and keeps everything that implies. The slope ascent and descent inference is the player's alone, and it survives for that reason: he rests on a support the recorder names, and the inference is labelled as an inference rather than asserted. Nothing equivalent exists for the companion.

Two cell formats are worth naming because a reader built on the old ones stays silent rather than failing:

- **`control` reads `desired=x,y`.** A zero desire is a brake and not a neutral, so any non-zero component is an active movement request. A predicate looking for `move=`, `jump=1` or `fall=1` is false on every row of every capture this reader will see again.
- **`spot` is a destination tile and the body is a centre**, so a distance between them converts the tile to its own centre on both axes. The walker's conversion put the target on the tile's floor because its position was a pair of feet; mixing the two leaves this reader and the arrival check measuring from points eight pixels apart.

## What the sidecar no longer carries

A `movement-state` sample carries status, search stop, goal, route points, segment index, remaining steps, progress reason, stuck ticks, stuck strikes and attempt ending. It does not carry `last-rejection`, and the narration that unpacked one is deleted with the check that read it: a refusal was the walking body's macro proof turning a step down before its first tick, and the orb's search either returns a corridor of free space or does not.

A `stop` occurrence carries `against-wall-throughout`, `same-segment-throughout`, `replanned-during` and `fastest-px-per-tick`. The walker's `grounded-throughout`, `airborne-throughout`, `same-step-throughout`, `next-step-from-rest` and `fastest-sideways-px-per-tick` are all gone.

A control grant still embeds whole control strings, so `Field` takes the first occurrence of a key on purpose. The embedded string is `requested-controls=desired=1.00,-2.00` now rather than a semicolon-separated control list, which makes the shadowing hazard smaller rather than absent; the rule holds whatever shape the motor's controls take next.
