# Recorded episodes show several different failure chains

**Evidence date: 12 September 2026. Source baseline: `d60b92b`, with gameplay source unchanged from `4296f85`.** The four main recordings discussed in the README come from 11 September and identify mod versions 0.13.4, 0.14.0, 0.14.0 and 0.15.0. They are observations of those captured builds. The current source identifies version 0.15.1. A later source correction is not evidence that the corrected behaviour has succeeded in play.

The investigation read the raw tab-separated recordings, the latest event stream, the existing census and their source producers. It did not run a new game, rebuild a historical version or independently rerun the native/portable fixture suites. Historical test results belong to the [history investigation](<../Historical Evidence/Commit Chronology and Coverage Ledger.md>), with their original scope. Exact input identities and reproduction commands are in [Recording Inventory and Reproduction](<Recording Inventory and Reproduction.md>).

## The corpus supports a chronology, not a controlled version ranking

There are 30 local telemetry TSV files: 28 contain samples and two contain only a byte-order marker. Their schemas range from 60 to 168 columns. This research inventoried every file and deeply analysed selected episodes in the four recent substantive runs. It does not claim that every earlier sample received an episode-level diagnosis. The latest short 439-row recording is inventoried but is not one of the README's four main runs.

| Recording on 11 September | Sample rows | Contiguous runs of the recorded `action` label | Transitions between those runs | Mean samples per run |
|---|---:|---:|---:|---:|
| 12:45:43.810 | 13,844 | 154 | 153 | 89.90 |
| 16:53:41.137 | 31,722 | 500 | 499 | 63.44 |
| 17:22:50.710 | 26,716 | 481 | 480 | 55.54 |
| 18:30:04.871 | 38,475 | 1,727 | 1,726 | 22.28 |

These are label-run counts, not counts of chooser executions, distinct jobs, completed jobs or mistakes. The final run contains 6,101 samples whose control owner is `downed`; the last activity can remain recorded while ordinary selection is bypassed. Different run lengths, terrain, enemies, player activity, configuration and captured builds confound a comparison. A shorter mean run is evidence of more frequent recorded label changes per sample in that session; it is not by itself evidence that each intervening code change made behaviour worse.

The historical removal of the stalled-work bonus is stronger evidence about one mechanism: its arithmetic can raise an activity against an incumbent even when the activity has made no useful progress. The live association and the score expression justify a targeted counterfactual, but the four session totals do not isolate that mechanism's effect from the other differences.

## Episode A: a pot activity prevents ore work while its own approach remains unresolved

In recording `2026-09-11_17-22-50-710`, inclusive ticks **18,501–21,531** contain 3,031 samples. Every row labels the action `break-pots`; 2,979 rows label the control owner `travel` and 52 `hold`. The pot anchor remains `(2773, 500)`. Every row reports `fire=no-target`, with `no-clear-trajectory` in the bounded target evidence. Retained edge outcome is `None` on 2,909 rows and `Interrupted` on 122.

The same rows show `mine_status=mining` on **979** samples, `approach unknown` on 1,450, `no reachable ore` on 360, `relocating` on 100 and completion-related statuses on the remaining 142. This does **not** mean the NPC mined for 979 ticks while breaking pots. Mining's candidate evaluation mutates the work record before the chooser decides which activity executes. A candidate's local status can say that its mining conditions hold while another candidate wins. A mining status is therefore neither a swing event nor a productivity measurement.[^mine]

Three rival explanations remain relevant:

| Hypothesis | Evidence that supports considering it | Observation that would refute it as the first failure | Separating intervention |
|---|---|---|---|
| Optional pot work remains overvalued against available ore work. | A long retained pot activity coexists with a mining candidate reporting work readiness; score arithmetic historically permits an incumbent bonus to matter. | Ore was not actually executable from any admitted approach, or the pot activity was still making useful progress. | Replay the identical snapshot and candidate set with only continuation/score treatment changed; measure actual ore damage and pot progress. |
| The pot destination does not satisfy its interaction predicate. | Long travel/hold ownership with a stable pot anchor and no completion in the selected interval. | The body repeatedly reaches a position from which the native interaction succeeds. | Record the proposed destination, native reach/interaction result, route arrival and productive event with one activity identity. |
| Movement cannot execute a valid pot approach. | Travel owns most sampled ticks. | A fixed approach succeeds under the same full brain while pot selection still remains stuck. | Hold the selected valid approach and replay actual body entry and control ownership; distinguish planner, executor and interruption outcomes. |

The recording alone cannot choose among all three. In particular, `Interrupted` samples cannot be counted as 122 failed attempts without joining their attempt identities. Selection may keep offering an unproductive purpose while movement faithfully pursues a destination that never represented successful work. That upstream possibility is why replacing route search first would be an unjustified diagnosis.

## Episode B: the pool shows an ownership and danger-model question

In recording `2026-09-11_18-30-04-871`, inclusive ticks **21,400–21,476** contain 77 samples. The recorded action is `guard` throughout. The control owner is **`combat-reflex` on 63**, `travel` on 13 and `downed` on one. `decide_ms` is zero on 64 samples and nonzero on 13. The retained survival score is **0.35**, while the guard score is 0.79–0.80. The README interpretation before this research, that survival remained at zero, was factually wrong.

The sampled `danger` value describes danger to the player; it is not the companion's own damage risk. `self_danger` is a separate field and ranges from 0.23 to 0.52 here. The companion's life appears at 16/100, 17/100, 4/100 and then the downed value 1/100. Breath remains nonzero. Consequently, this episode does not establish that a drowning-only survival function ignored suffocation; it is a hostile-damage/positioning/control-ownership episode near water. Current source also includes recent damage in self danger.[^danger]

The event stream corroborates active combat and player harm: a bow shot occurs at tick 21,423, and player-damage events occur at 21,433 and 21,475. Enemy-damage events occur nearby. Those enemy-damage records do not identify the companion as their victim. The companion's life decline comes from its samples and companion-hit recording path, not from treating every `npc-damage` event as damage to our NPC.

`brain_fresh=1` on reflex-controlled ticks means the brain recorded a current tick. It does **not** mean the utility chooser ran. `CoordinateBrainTick` can return through reflex control before selection. The last guard label and old score can therefore survive while the body receives reflex controls. A claim that utility freshly chose guard on all 77 ticks would be unsupported.[^tick]

Similarly, the sampled `edge_outcome=Interrupted` spans different attempt identities and edge coordinates in this window. It is not proof that one identical jump failed repeatedly. The missing causal chain is the proposed travel control, the reflex counterfactual, the actual override, the native body result and the next safe terminal state. The first experiment should expose that chain before changing either the survival score or the route algorithm.

## Episode C: a hunt can arrive at a spot without obtaining a shot

In the same final recording, inclusive ticks **36,082–36,423** contain 342 samples. All label the action `hunt` and control owner `travel`; `nav_status` is `Arrived`. The NPC position is exactly `(60041, 6848)` throughout and the selected spot is `(3752, 428)`. The hunted target is a zombie, while the target anchor changes as the enemy moves.

Fresh bounded weapon evidence contains **bow `no-clear-trajectory`** and **throwing knife `outside-reach`** rejections. It does not say every weapon was outside reach. The earlier interval **11,352–13,869** shows the same mixed reason class over 2,518 no-target samples, despite changing activity labels.

This narrows the mechanism more than a generic “navigation failure.” The navigator considers its requested spot reached. The higher-level purpose—occupy a position yielding a useful attack—has not been achieved. Candidate generation, target admissibility, trajectory feasibility, target motion, positioning refresh and purpose-specific progress are the immediate alternatives. An A* replacement cannot by itself make an arrived spot useful.

Current source explains ways this can arise without proving which one caused this old run. Hunt admissibility samples only a subset of nearby candidate tiles with a straight visibility test; the expensive position solver checks a bounded shortlist; a poor position can still be the best evaluated result; and hunt progress can be renewed by independent weapon activity or movement rather than damage to the hunted enemy. Those are source-level mechanisms and proposed discriminators, not a reconstructed causal trace of every old tick.[^hunt]

## The report generator overstates the range explanation

`CheckTheFight.HuntingHadAWeaponThatCouldReach` treats the presence of the text `outside-reach` in bounded target evidence as a definitive finding that every candidate pair was outside range. A mixed set containing one blocked bow trajectory and one out-of-range knife satisfies that condition. The inference is invalid even without a gameplay reproduction: an existential match does not prove a universal statement.[^reader]

This is a confirmed source defect in the analyser, recorded for a future implementation change. The correct evidential contract needs structured per-pair results, their age, the complete candidate denominator and a distinction between untested candidates and rejected candidates. With bounded evidence, even “every recorded pair failed” says nothing about omitted candidates unless the capture declares completeness.

The defect is consequential because the analyser's confident prose fed an architectural explanation. A source-level selection issue was made to look like pure range mismatch. Research should therefore keep raw observations beside the diagnosis and permit a later reader to recompute the conclusion rather than inherit the adjective “definitive.”

## The stall classifier uses a control label the producer never emits

`Tools/SessionReport/Read/Chronicle.cs`, in `StallCandidate`, excludes `control_source == "reflex"` from ordinary lack-of-progress narration. `CoordinateBrainTick.cs` writes `combat-reflex` instead. The local corpus contains 2,121 rows with `combat-reflex` and no rows with the literal value `reflex`. A reflex-owned interval can therefore pass that exclusion and, if it also meets the classifier's stationary-duration and stable-spot conditions, be narrated as an ordinary task stall.

This is a second confirmed source defect in the analyser. It does not establish that a particular historic interval satisfied every condition or produced that narration; SessionReport was not run to demonstrate that outcome during this research. The durable failure class is disagreement between the recorder's categorical vocabulary and its consumers. E01 requires a stationary trace using the producer's exact token, plus an ordinary travel trace, so the observer must reject the first and recognise the second when all other stall conditions hold. It also checks missing or unfamiliar tokens explicitly instead of silently translating them. The analyser and its source-folder documentation remain unchanged within this research-only edit scope.

## Movement census columns have different denominators

The final census records 809 plan runs, 793 returned paths and 7,971 total planned steps. It separately records movement begun/completed/interrupted. A route may be replaced, reused or interrupted before all its listed steps execute; an execution attempt may not correspond one-to-one with a newly listed plan step.

| Movement | Planned steps | Begun attempts | Completed | Faulted | Interrupted |
|---|---:|---:|---:|---:|---:|
| Walk | 7,372 | 2,170 | 1,305 | 1 | 864 |
| Jump | 323 | 238 | 53 | 5 | 180 |
| Drop | 213 | 263 | 32 | 1 | 230 |
| Fall-through | 63 | 12 | 4 | 0 | 8 |

The drop row alone demonstrates why planned steps cannot be the denominator for physical attempt success: begun attempts exceed planned steps. The 53 completed jumps are out of 238 begun attempts in this census, with 180 interruptions and five faults. An interruption is a control/purpose event whose reason needs investigation; it is not automatically inability to execute the jump. Even these begun/completed counts need attempt-ID reconciliation before treating their ratios as an unbiased native movement reliability estimate.

## Timing fields need the computation they actually measure

At tick **31,001** in the final run, `brain_ms=42.99` and `decide_ms=41.82`; `position_ms=0.31`, `navigate_ms=0.02`, `reflex_ms=0.07` and `sense_ms=0.03`. This is a recorded expensive tick. The source puts candidate discovery, some route/feasibility queries and chosen-action execution inside the decision interval. It is therefore evidence of expensive decision-phase work, not a measurement that multiplying utility factors costs 41.82 ms.

Likewise, the reflex timing includes intervention estimation before the reflex test, while some work on an early-return avoidance branch is not attributed as ordinary navigation. A total duration remains useful; a component label must be read against its timer boundaries before it drives an optimisation. Compare p50, p95, p99 and worst tick by workload and build on future captures, and record recording-enabled versus recording-disabled overhead separately.

## God’s-eye observation is broad but remains bounded

The latest event stream contains 32,478 `navigation-state`, 4,393 `movement-state`, 2,368 `decision`, 1,111 terrain-snapshot, 126 shot, 149 pickup, 30 world-interaction and 14 player-damage events, plus native projectile/enemy/lifecycle records. Event counts measure emitted records, not the number of all underlying world events. For example, a decision event's emitter may record only a changed serialized decision; absence between two events is not absence of chooser execution.[^events]

`RecordTerrainChunks` samples around both actors, queues dirty chunks, caps work per tick and retains prior chunk contents to suppress unchanged duplicates. It records geometry, liquids, tile/wall types, tile state and frames. Its rolling capture is materially stronger than reconstructing a whole cave from one current screenshot, but it is not a simultaneous full-world snapshot. A chunk observed later cannot silently become evidence of earlier terrain; absence may mean out of range, queued, disabled or unobserved.[^terrain]

The visual inspector retains bounded samples from actual solvers rather than rerunning them merely to draw. This preserves the distinction between what was considered and what an observer could calculate afterwards. However, the display must show observation time, query identity and coverage. A retained trajectory without those labels can make a current body appear to own an old prediction.[^inspector]

The proposed observer should have two explicitly different views: **agent evidence**, showing exactly what the companion had available when deciding, and **retrospective reference**, showing captured world facts or a later validation. The latter can refute a decision model, but must never be reported as information the actor had then. Unrecorded enemy scripts or future terrain cannot be reconstructed by either view.

## What these episodes justify

The recordings establish that optional work, combat positioning and control handoffs can fail together. They do not establish that utility is fundamentally incapable of the desired behaviour, or that every failure is navigation. The most economical next evidence is a purpose-to-outcome chain: candidate → admissibility → selected activity → valid destination → route proof → control owner → native effect → productive progress. An experiment can then hold the downstream stages constant while testing an upstream choice, or hold the choice constant while testing physical execution.

That chain is the common prerequisite in all three proposals. Its value survives a switch to a hierarchy, planner or learned policy because each still needs to explain why its intended action did or did not occur.

## Source appendix

[^mine]: `Companion/Brain/Activities/MineAction.cs`, `Score`, `FindApproach` and `Execute`; `Companion/Brain/Infrastructure/Interactions/Mining/TileMiner.cs`, `Swing`. Baseline source was read directly. Paths identify symbols as well as files because line numbers drift.
[^danger]: `Companion/Brain/Infrastructure/Observation/ObserveCompanion.cs`, `ObserveThreats.cs`, `ThreatRecord.cs`; sample fields `danger`, `self_danger`, life and breath. Damage estimates in threat utility are not a calibrated effective-damage distribution.
[^tick]: `Companion/Brain/CoordinateBrainTick.cs`, early recovery/reflex branches and timer boundaries; `Companion/Brain/Infrastructure/Diagnostics/RecordBrainTelemetry.cs`, `brain_fresh`; `Companion/CharacterBody/CompanionNPC.cs`, downed bypass and recording order.
[^hunt]: [Decisions, Activities and Shared Controls](<../Implementation Evidence/Decisions, Activities and Shared Controls.md>), source references for HuntAction, firing evidence and Positioner.
[^reader]: `Tools/SessionReport/Checks/CheckTheFight.cs`, `HuntingHadAWeaponThatCouldReach`, baseline lines 292–325. The branch tests `Contains("outside-reach")` before constructing a definitive universal range explanation.
[^events]: `Companion/Brain/Infrastructure/Diagnostics/RecordGodsEyeEvents.cs` and `RecordBrainTelemetry.cs`; latest JSONL counted by its `kind` field with the read-only recipe in the inventory.
[^terrain]: `Companion/Brain/Infrastructure/Diagnostics/ObserveTerrainChanges.cs`, `RecordTerrainChunks.PostUpdateEverything` and dirtiness callbacks. Sampling and remembered-content bounds are implementation tunables, not a promise of complete terrain capture.
[^inspector]: `Companion/Brain/Infrastructure/Diagnostics/BrainInspectorSamples.cs`. Samples come from current solver instrumentation, bounded retained queues and opt-in capture.
