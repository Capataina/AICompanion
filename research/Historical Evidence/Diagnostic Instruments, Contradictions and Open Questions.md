# Diagnostic Instruments, Contradictions and Open Questions

Source revision: `d60b92b`, 2026-09-12. This report answers historical Questions 9–16 from commits, recovered conversations and read-only Slate records. It does not decide the open architecture questions. “Demonstrated” below means the named evidence actually exercised the claim; it does not mean the result generalises to every Terraria cave or mod interaction.

## How the diagnostic system acquired its present shape

| Stage | Trigger in the conversation | What landed | What the evidence established | What remained outside it |
|---|---|---|---|---|
| Per-tick file telemetry, `b0c042d` | Dictated symptoms could not identify which decision, route, control or body state caused them; the overlay key did not work. The owner prioritised automatic recording and rejected dependence on a manual “wrong” marker. | Scores, selected action, movement request and route state, body state, weapon/fire state, light state and player proxies were written each tick. | A later reader could align narration with ticks and inspect the whole run. | The first commit had compiled only; no game had written the new schema yet. |
| Scenario windows and player trail, `100711e` | The owner's pass condition was “if I can get through, it can”; a failed plan alone missed followers that stalled with a valid route or dodges whose prediction was wrong. | Triggered terrain windows for follow lag, stuck movement, post-dodge damage and missed work, each carrying the recent player trail. | Historical scenarios remained replayable and the trail parser had a synthetic negative case. | Triggers only represented failure classes already anticipated. |
| Categorised session reader, `0281c2b` | Manual column inspection produced wrong indices and lists of places to inspect rather than conclusions. The owner asked the tool to name definitive issues, potential issues and oddities itself. | Named-column parsing, explicit coverage, evidence categories and grouped repeated findings. | It independently rediscovered known damage/hunting defects and an unexamined platform freeze. It also exposed overflow that had made the recorder invent dodge scenarios. | Checks absent from an older schema were skipped; a skipped check and no defect are different outcomes. |
| Actual-entry/native movement evidence, `2fc66e` | Planner and follower shared constants but not a body backend, and route changes obscured whether a move completed, failed or was merely interrupted. | Native collision comparison, actual-entry proof, movement identity and terminal outcome accounting. | Controlled Terraria comparisons and native route fixtures could distinguish planner/body disagreement. | Native checks were fixtures, not a live-play parity result; the portable corpus remained mixed. |
| Retained work and directed route memory, `0f9d7f` | Repeated searching discarded usable work; the owner proposed connecting a new route to a previously successful ledge-to-hill traversal. | Search frontier retained across ticks, directed completed-traversal memory, terrain fingerprints and failure invalidation. | One experience fixture fell from 48 to 15 expansions; the corpus recovered five completed cases. | Aggregate planner time did not improve, and no live acceptance had occurred. |
| Interactive “God's eye”, `a72846a` | The owner noted that zero completed jumps were present in the log but had not been questioned until narration contradicted them, and asked for a postgame graph that judged competence as well as consistency. | Timeline joining samples, decisions, alternatives, movement outcomes, projectiles, damage, pickups and terrain snapshots. | Two real recordings could be rendered with explicit sample coverage and omissions. | Sampling was partial; uncaptured terrain, player intent and unseen enemy futures remained unrecoverable. |

The instrument history has a repeated pattern: every new reader found defects in the instrument below it. That is evidence for layered observability and counterfactual checks, and against treating telemetry as ground truth merely because it is detailed.

## Contradictions that the architecture comparison must preserve

| Attractive statement | Historical counterevidence | Consequence for the next comparison |
|---|---|---|
| “The chooser selected follow, so following failed.” | A completed navigator destination could still lie outside the follow comfort region; an Exact survival request could be a refuge whose route was never proved. | Trace admission, destination, route, control owner and body outcome before assigning the failure to selection. |
| “A planned route is executable.” | Representative start poses, preparation controls and portable collision differed from the live body. The platform freeze was a motor-side `StepUp` write, not a missing A* edge. | Search and control must be compared on common physical semantics, including the actual entry state. |
| “Unknown should conservatively mean reachable” or “unknown should mean unreachable.” | Threat sensing, exploratory ore approach and breath-critical refuge commitment have opposite error costs. | Preserve uncertainty as data and make each caller state how it consumes it. |
| “More commitment or progress awareness reduces churn.” | A shared `MovementStalled` modifier coincided with mean recorded action runs falling from 55.5 to 22.3 rows; the historical analysis reported 1,436 immediate reversals. Different sessions do not isolate causality. | Progress evidence needs a task identity, freshness and ownership; body displacement alone cannot price task success. |
| “The route-memory work implemented experience graphs.” | The implementation stores directed completed traversals and guides connection search, but does not reproduce the full cited E-Graph algorithm. | Compare the actual route archive and search bias, not the borrowed name. |
| “The corpus improved, therefore live movement improved.” | The corpus recovered five complete cases, but planner time did not improve and the commit expressly deferred live acceptance. | Keep corpus, native fixtures and live play as separate evidence classes. |
| “No shot fired means aiming failed.” | A tick may have no target, be in cooldown, have hands busy, have no arc, or fire. A later README analysis found cooldown accounted for most selected-target non-fire ticks. | Record eligibility and every rejection reason before using a final event rate to diagnose an upstream stage. |
| “Hunting beat mining.” | In the 11 September trace, mining often had no candidate or a permanently unknown approach and therefore did not enter the comparison. | Diagnose admission and information flow before changing utility weights. |
| “A completed jump landed successfully.” | The owner observed sliding after jumps counted complete; later measurement checked the body's position after the completion tick and found a smaller but real post-landing loss. | Movement success needs a stable terminal condition suitable for the next move, not first contact alone. |

## Alternatives that were rejected, and what would reopen them

| Alternative | Why it lost in the recorded discussion | Evidence that would reopen it |
|---|---|---|
| Whole-brain finite-state machine | The owner's examples required follow, danger, loot, work and combat to trade off at once rather than run one fixed priority sequence. | A bounded execution problem whose states are about completing one chosen action, rather than replacing opportunity comparison. |
| Behaviour tree as the top-level chooser | The proposed selector form preserved the priority-chain problem. | A tree used for action execution or explicit hard constraints while a separate mechanism compares opportunities. |
| Reinforcement learning | The owner rejected it for lack of data, compute and a way to tell insufficient training from bad architecture. | A faithful simulator, representative demonstrations, a held-out evaluation set and an affordable iteration loop. |
| LLM-controlled companion | It shared the cost and evaluation objections and did not fit deterministic local control. | A sharply bounded advisory role with latency, determinism and failure behaviour measured in the game loop. |
| D* Lite immediately replacing A* | Incremental search can reuse work but cannot generate an omitted movement transition or fix incompatible body semantics. | Profiling that isolates repeated search repair as the dominant remaining cost after transition coverage is established. |
| One global boolean reachability answer | The same inconclusive search needs opposite treatment in threat caution and self-rescue. | No reopening condition is evident while caller error costs remain different. |
| Pricing voluntary one-way drops | A small score error could strand the companion for the rest of a session. | A proven, affordable return or recovery route that changes the consequence of taking the drop. |
| Priority interrupt for guard | The owner wanted danger to trade against remaining work and effective threat, and the arithmetic defect could be repaired within the scoring model. | A product rule that identifies a truly non-negotiable safety condition rather than a very large preference. |
| Progress-conditioned commitment from body stall | The signal was shared across behaviours and persisted after ownership changed, producing a flip-flop. | Task-owned progress with identity, timestamps, remaining work and explicit terminal outcomes. |
| Manual “that was wrong” capture as the primary recorder | The owner expected to forget the key while dictating and wanted the entire playtest available. | It remains useful as an optional annotation layered on automatic capture. |

## Direct answers to Questions 9–16

### Q9. Which evidence corresponds to each pivotal episode?

The companion report now maps seven episodes rather than only commit ranges:

1. 7–8 September Claude conversation → priority-state-machine critique, utility/A* proposal and implementation authorisation → `85961e`.
2. 8–9 September Claude conversation → physical jump/run-up requirements, narrated platform and slope failures, global-cause request and `StepUp` diagnosis → `985195e`, then the shared-boundary work in `2fc66e`.
3. 8–9 September Claude conversation plus Slate Record → leaveable-place discussion, activity-specific one-way permission and later caller-specific uncertainty → `544a0e9`.
4. 9–10 September Codex conversation → owner-drawn ledge-to-hill reuse, broader research, accepted route-memory plan → `0f9d7f`.
5. 11 September Claude conversations → 55.5-tick baseline, progress-sensitive commitment, 22.3-tick regression, owner stop and revert → `9b402cb`.
6. 11 September Claude conversation → inaccessible hunts, missing repositioning, endless-enemy concern and missing ore admission → `b821afa`, `ac0c23c`, `0f9394e`.
7. 8–10 September Claude/Codex conversations → automatic telemetry, scenario windows, autonomous classification and postgame “God's eye” → `b0c042d`, `100711e`, `0281c2b`, `a72846a`.

The [coverage ledger](<Commit Chronology and Coverage Ledger.md>) accounts for all 176 gameplay commits plus the two research commits. Conversation recovery adds the pre-commit requests and rejected interpretations that the ledger could not supply.

### Q10. What do the wider windows reveal?

They reveal five distinctions hidden by the commit bodies:

* Utility, experience-route reuse and the competence-oriented recorder were responses to owner-supplied examples; they were not mechanisms independently derived and then presented as settled architecture.
* Several accepted implementations followed a discussion-only turn and explicit authorisation. The 11 September commitment edit is the exception: implementation started during a requested discussion, the owner stopped it, and the assistant acknowledged the overreach.
* The platform freeze had multiple rejected causal stories before the local motor call was found. A clean final commit story would otherwise conceal how fragile the inference had been.
* “Experience graph” was a research analogy attached to the owner's idea. The implementation was a narrower directed traversal archive.
* The owner consistently asked for general failure-class removal and challenged narrow fixes, while the assistants sometimes returned to per-symptom tuning before source or telemetry contradicted them.

### Q11. Why was the original priority state machine replaced?

The triggering expectations were concurrent: keep moving with the player, approach closer under danger, restore line of sight, reposition for a shot, choose between threats, collect compatible loot and help with current work. A fixed state priority could express only one ordering at a time. Utility scoring was selected to compare those opportunities on common factors, with reflexes for immediate threats and small state machines inside actions. A behaviour-tree selector and reinforcement learning were discussed and rejected for the reasons above. The first utility commit had no live-play validation, so the rationale is historically established while the claimed behavioural improvement at that point is not.

### Q12. How did A* and the terrain graph evolve?

The first A* arrived with utility selection and treated navigation primarily as standable tiles and traversal kinds. Subsequent work added partial results, run-up and fall-through edges, return search and failure memory. Playtests then exposed that the graph's representative pose and the follower's actual pose, momentum and collision order could disagree. `2fc66e` moved the design toward one movement boundary, actual-entry proof, retained control execution and native Terraria collision comparison. Later commits retained the search frontier across ticks, added directed traversal memory and revalidated execution from current feet. The current arrangement is therefore layered: behaviour requests a position; positioning reasons over candidates and reachability; route planning supplies validated prefixes; execution owns actual-state continuation; the motor alone writes live controls. Historical failures concern every boundary in that chain, not A* alone.

### Q13. Why was route memory introduced, what did it implement, and what improved?

It was introduced because the owner wanted a new search from C to connect to a previously successful A→B route, both to find detours and to avoid rediscovering known movement. `0f9d7f` stored successful ordinary traversals as directed, world-specific memory with terrain fingerprints and failure invalidation, then allowed search to use remembered entrances. A fixture reduced expansions from 48 to 15, and five historical cases moved from incomplete to complete. Planner time did not improve and no live cave acceptance had yet run. Calling the change “experience-graph-like reuse” is accurate; claiming the full algorithm or a demonstrated live speed-up is not.

### Q14. How did reachability, returnability and unknown change?

Reachability began as a permissive boolean designed for threat caution. Returnability added a reverse-route question and activity-specific permission for one-way edges: necessary companionship and survival may cross boundaries that voluntary work should refuse. A drowning trace then showed the boolean's hidden uncertainty being consumed as a life-critical promise. `544a0e9` preserved `Unknown` and made callers choose their policy. Later pursuit and ore work reused the same epistemic distinction: an incomplete search is neither a firing position nor proof that none exists. One-way memory remains incomplete at the product level because the rule “only after the player has taken it” still lacks a general record of ground the player has covered.

### Q15. What led to the commitment experiment, and why did its outcome differ from its rationale?

The rationale was real: a flat incumbent multiplier was suspected of keeping an unreachable pot ahead of ore, while rapid behaviour changes could prevent multi-second work from finishing. The owner wanted persistence to be probabilistic and evidence-sensitive rather than a minimum lock time. The implementation used the body's shared stalled flag as a proxy for the incumbent's progress. Because that flag persisted across behaviour changes, it could penalise the newcomer for the previous task, creating a plausible reversal loop. The raw recordings reproduce mean action-run lengths of 55.5 and 22.3 rows, about 60% shorter, but not a controlled causal effect: their durations and circumstances differ. The 3.59-fold increase in total run count must not be reported as a switching-rate multiplier. The source mechanism argues against that proxy; it does not refute the need to represent task progress.

### Q16. Which repairs were progress, repeated assumptions or useful intermediate work?

| Classification | Examples | Why |
|---|---|---|
| Reported progress within its evidence class | `544a0e9` preserves unknown; `0f9d7f` reports fewer expansions in one fixture and five recovered corpus cases; `b821afa` reports current-shot/reposition/sealed-target counterfactuals. `9b402cb` restores the earlier commitment formula, without a post-revert live measurement in the compared captures. | Read the named fixture or source change at its own scope. A revert establishes the formula, not restored gameplay performance. |
| Revisited failed assumption | Treating search unknown as success; treating a planned tile edge as executable; treating body stall as task failure; treating zero fire as an aiming failure; treating an internally consistent recorder as accurate. | Later evidence invalidated the interpretation rather than merely changing a threshold. |
| Useful intermediate work with the original acceptance still open | Portable route fixtures, native collision parity, first scenario windows, directed route memory, ore unknown-approach movement, high-ore hopping. | Each improved a component or made failure observable, but the relevant commit explicitly lacked live acceptance, representative coverage or an after-playtest measure. |

## Historical additions that should constrain the README review

The parent README review should preserve these distinctions when describing current and expected behaviour:

* Utility scoring is current machinery whose architectural status has been reopened. Its original choice answered competing owner examples, but its first implementation was not live-validated independently of the navigator that landed beside it.
* Route memory is directed memory of completed ordinary traversals with invalidation. It is not a general memory of explored ground and does not implement the still-missing rule that one-way terrain becomes acceptable after the player demonstrates it.
* Reachability has three outcomes. “Unknown” must not be paraphrased as reachable or unreachable without naming the consuming policy.
* Native parity means the checked collision cases matched. It does not mean portable replay and the live NPC are one body, or that live cave movement is reliable.
* Commitment currently uses a flat incumbent bonus because the tested body-stall proxy regressed badly. That is a historical rollback, not evidence that remaining effort or task-owned progress should be absent from a future brain.
* Hunting admission now distinguishes a shot from here from a reachable firing position. The historical fixture is static and does not establish moving-enemy pursuit quality.
* The “God's eye” and session reader expose captured alternatives and outcomes with explicit sampling. They cannot reconstruct facts that were never captured.

## Sources

1. [Pivotal Decisions and Conversation Evidence](<Pivotal Decisions and Conversation Evidence.md>), recovered conversation windows and episode analysis.
2. [Commit Chronology and Coverage Ledger](<Commit Chronology and Coverage Ledger.md>), all commit titles and bodies through `d60b92b`.
3. Read-only Slate Architecture, Identity, Design, State, Record and chronological roadmap for `ai-companion`, read 2026-09-12.
4. Commits `b0c042d`, `100711e`, `0281c2b`, `2fc66e`, `0f9d7f`, `a72846a`, `544a0e9`, `b821afa`, `ac0c23c`, `0f9394e` and `9b402cb`, cited for their own measurements and stated limits.
