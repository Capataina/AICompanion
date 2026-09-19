# Experiments must identify the first broken contract

**Proposed work, 12 September 2026. No experiment in this document was implemented or run during this research.** The purpose is to compare changes against the owner's expected behaviour, not to make one architecture win by choosing its preferred metric. These experiments are shared by the three roadmaps in `research/proposal/`; their identifiers stay stable when code moves.

The source of the design is the combination of [recorded episodes](<Recorded Episodes and Measurement Limits.md>), [implementation evidence](<../Implementation Evidence/Decisions, Activities and Shared Controls.md>), [platformer research](<../Navigation Research/Dynamic Platformer Navigation.md>) and [decision research](<../Decision Architecture/Decision, Commitment and Computation.md>). Each experiment below states rival explanations, an intervention and the observation that changes the next step. Thresholds that depend on feel or hardware must be declared before the experiment, not selected after seeing its result.

## Compare the outcome the player sees

The [Proposal 1 failure analysis](<Proposal 1 Failure Cases and Diagnostic Contracts.md>) supplies 64 prospective cases and maps them to E01–E16. Use its paired raised-lip mining geometry, separate movement/firing targets, family-offer checks and coherent HUD snapshot requirements as additional acceptance conditions. The matrix does not replace an original-signature reproduction or establish that every listed failure currently occurs.

A companion may execute many commands while achieving little. The primary outcome measures therefore describe assistance, disruption and physical consequences. An aggregate score can help summarise a batch, but the individual dimensions remain visible because a gain in gathered ore cannot buy permission to damage a home or repeatedly abandon the player.

| Outcome | Measurement and desirable direction | What it must not be confused with |
|---|---|---|
| Useful work | Actual blocks removed, relevant drops transferred, illumination coverage improved and threats prevented per eligible opportunity; higher is useful within policy. | Time in `mine`, swings issued, walking distance or number of plans. |
| Companion disruption | Player waiting, unwanted separation, repeated recovery flight, obstruction and unexplained pursuit; lower is desirable. | All autonomous movement; a short independent excursion can be wanted. |
| Context-sensitive protection | Damage to the player and companion, intervention before predicted harm, and excessive defensive interruption; assess together. | Maximising kill count or avoiding every one-damage hit. |
| Coherent actions | Tool/weapon conflicts, interrupted uses without a reason, repeated reinitialisation and visible aim/body contradictions; lower is desirable. | Fewer activity switches regardless of danger or opportunity. |
| Physical reliability | Completed native traversals divided by admitted, begun, identifiable attempts under a stated contract, stratified by terrain/ability; higher is desirable. | Completed steps divided by every step printed by every plan. |
| Feasibility calibration | False admissions, false rejections and unresolved queries, separately; reduce wrong answers without hiding uncertainty. | Treating fewer `unknown` results as automatically better. |
| Responsiveness and cost | End-to-end action latency, per-stage p50/p95/p99/max time, allocations, query count and observer overhead; lower cost at equal behaviour. | Pure utility arithmetic inferred from the broad `decide_ms` timer. |
| Comprehensibility | A reviewer can explain why an excursion, interruption or abandonment occurred from captured evidence and can challenge it. | A convincing generated narrative unsupported by a complete event chain. |

Use an opportunity denominator: a mining failure is assessed only when a permitted, detectable, tool-eligible opportunity existed; a guard failure only when an intervention was physically possible within the declared model. The denied and unresolved populations must remain reported. Otherwise a policy can appear excellent by refusing every difficult job.

## E01: verify the observer before ranking architectures

**Rivals:** apparent failure is a real policy/execution failure; it is stale or incorrectly aggregated evidence; or the behaviour happened outside capture coverage. Construct small known traces containing one candidate evaluation without selection, one reflex bypass, two attempts sharing an edge, a downed interval, a mixed weapon rejection set and one actual pickup split between player and bag.

The proposed analyser must distinguish all of them. It should reject a universal statement about weapon range when only one pair is outside range, count no fresh chooser execution on a bypass tick, retain separate world/brain clocks, count each movement attempt once, and report missing terrain as missing. Add a stationary interval long enough to satisfy the ordinary stall classifier, using the producer's exact `control_source=combat-reflex`, and an otherwise identical `travel` interval: the reflex interval must be excluded from ordinary task-stall narration while the travel interval is recognised. Current `Chronicle.StallCandidate` tests the nonexistent token `reflex`; this paired case catches that actual defect. Missing or unfamiliar categorical values must remain explicit rather than silently treated as ordinary travel. A mutation of any relevant event identity or coverage flag should change the corresponding assertion or make it unknown. The pass line is exact classification of these known traces. This is a future analyser test, not a reason to modify production AI first.

If the result fails, stop architectural performance comparisons and repair the measuring rule. If the result passes while recorded behaviour remains bad, proceed to E02. If the old recording lacks the necessary field, preserve the old conclusion as unresolved and obtain a new capture after instrumenting; do not fill the field from today's code.

## E02: isolate choice, destination and body execution

Start from the pot/ore stall, an ore-adjacent non-mining scene and the arrived-without-shot hunt. Save terrain, body pose/velocity, active enemies/items, player trace, capabilities and relevant retained activity state. Run three controlled variants in an appropriate headless native harness, followed by an authorised live observation once meaningful differences appear.

1. **Keep the whole brain:** reproduce the original signature, including outcome/status class and lack of productive progress.
2. **Hold a chosen purpose and target:** allow normal position, route and body control. This asks whether selection is the first failure.
3. **Hold a native-valid destination or local primitive:** retain native physics and relevant interruptions. This separates destination validity from execution.

| Result | Next interpretation | Next experiment |
|---|---|---|
| Only the full-brain case stalls. | Selection, continuity or control handoff is implicated. | E03, E04 and E05; inspect the first differing event. |
| Both full brain and fixed purpose stall, but a validated destination succeeds. | Candidate generation or destination success predicate is implicated. | E07 and E08. |
| A validated destination still cannot be executed. | Physical model, primitive, actual entry state or control ownership is implicated. | E05, E09 and E10. |
| A fixed purpose fails only when hostile motion differs from capture. | Replay lacks the relevant dynamics, or the policy needs online updating. | Improve the fixture boundary; do not use an uncontrolled replay to choose an architecture. |
| The original signature cannot be reproduced. | Evidence is insufficient for causal repair. | Retain the hypothesis, add the missing recorder fields and obtain a matching capture. |

Do not force an unsafe target through ordinary policy just to create success. The intervention must declare which decision was replaced and retain hard edit, body and product boundaries. A fixed-target success is an explanatory counterfactual, not a new product policy.

## E03: compare continuation by remaining value

Use paired scenes varying one fact at a time: remaining ore/tree/enemy health, distance to completion, player movement, effective incoming damage, time to harm, target disappearance and route/return validity. Include two histories ending in the same current state, one where the companion did the earlier work and one where the player did. Include a cheap almost-complete job that has become irrelevant, so a mechanism cannot pass merely by always finishing.

Compare the existing incumbent modifier, a stateless current-value baseline, and an explicit activity record whose continuation reflects remaining cost, switching cost and uncertainty. The desired result is context dependence: equal current opportunities should not become more valuable solely because the companion previously spent effort; changed remaining work may make finishing more attractive; a genuine danger or invalidation may still end it. Record the alternative foregone, not only the winning score.

If the simple baseline succeeds across the paired cases, keep it. If failures arise from lost target/progress identity, add or repair that record. If failures remain because a future enabling consequence changes the present choice, move to E13. If reviewers disagree on taking a small hit, preserve that case as a preference boundary for owner judgement; a paper cannot decide how this companion ought to feel.

## E04: compare flat and grouped utility without changing their candidates

Grouping mining, chopping and pots under harvesting can improve authoring while changing which opportunities compete. First make candidate evaluation observational: a score query should not silently advance a job, invalidate a cache or discover only the first item. Then compute the same leaf opportunities for both selectors and record them before grouping.

Compare flat selection with hierarchical selection using maximum eligible child value, sum/aggregate family value and a family policy with explicit child cost. These are different hypotheses. A sum can reward a family just because it has more children; a maximum can conceal expensive alternatives; independent parent/child hysteresis can trap work or amplify switching costs.

Metamorphic checks should include adding an irrelevant zero-value child, duplicating an identical opportunity and renaming/reordering a family. Where those changes are semantically irrelevant, they must not change the chosen meaningful action. Cross-family urgency must still interrupt work. Measure switch cause, target continuity, computation cost and useful outcome with identical observation/physics. If grouping only shortens code without improving authoring or outcomes, retain organisational folders and keep flat competition. If failures cluster at parent/child boundaries, flatten that boundary rather than tune two layers indefinitely.

The three-family follow-up places this comparison inside Path 1. Its initial parent offer is exactly the best eligible child's value; sum/aggregate policies remain alternatives rather than part of the starting design. Preserve deterministic tie-breaking in the flat/grouped reference test. Identical winners establish equivalence for the tested inputs, not improved behaviour or reduced query cost.

Add offer-to-execution cases for an empty combat family, a useful child omitted by discovery, a child left unknown because its query was deferred, a target disappearing after submission, and an unchanged child refused by execution. Capture offered child/target IDs, input and dependency revisions, eligibility, deferred coverage, parent selection and execution admission. Empty families cannot win; unknown opportunities cannot be silently labelled impossible; changed dependencies must invalidate stale offers; unchanged input must not let the parent purchase an activity the child never offered. The [Path 1 diagnostic table](<../Historical Evidence/Archived Brain Proposals/01 Preserve Utility and Repair Activity Contracts.md#a-chosen-family-must-have-a-concrete-activity-to-deliver>) owns the attribution and failure branches.

For Path 2, hold the three-family offers and physical model constant and compare lightweight activity ownership with a common executor. Measure duplicate phase/abort responsibilities, outcome attribution, interrupted work, controller handoffs and cost across several purposes. This is a different experiment from establishing hierarchy equivalence. If the executor adds no benefit, retain Path 1 even if grouping itself remains useful.

## E05: assign hands and feet by action phase

The test matrix includes travel-to-ore plus shooting; active pickaxe use across cooldown gaps; active axe use; held torch yielding to a shot; supplied torch placement; airborne travel; immediate dodge; sustained escape; recovery flight; downing; and resumed ordinary travel. Log every requested output channel and the final grant, not merely the action's name.

The invariant is one authoritative writer per incompatible output for a tick, with a retained coherent-use period where required. Shooting during approach is legal. Active pick/axe work cannot weave weapon use between normal tool strikes. Held light can yield and resume. A necessary urgent interruption may release the tool, but it needs a named reason and a safe/understood cancellation boundary. Movement is shared by all purposes through the existing motor boundary.

If the only conflict occurs on a reflex early return, repair that shared handoff. If conflicts occur across many activities because ownership is implicit, compare a small resource arbiter with the current coordinator. A full behaviour-tree migration earns no credit unless it makes the same invariant easier to enforce and diagnose. If exclusive hands prevent a legal shot while walking, the grant was scoped to the entire behaviour rather than its current action phase.

## E06: model danger as consequences with uncertainty

Create matched threat scenes differing in armour/effective damage, current health, regeneration, enemy attack cadence, time to contact, projectile visibility, escape options and danger to the player. Include an invulnerable/unattackable enemy that remains harmful, a low-damage nuisance, a high-damage worm and a threat that cannot physically reach either actor. Do not require the companion to know an unobserved modded attack script.

Compare proximity/raw-damage heuristics with bounded projected-harm estimates derived from observed attacks and native damage rules. Record predicted time-to-harm, damage interval, source of the estimate, confidence, actual harm and whether intervention could have changed it. Distinguish selecting an evasive position, interrupting work and attacking the threat; these have different effects.

If raw proximity drives needless abandonment, improve the estimate before changing selection architecture. If a good estimate is ignored, inspect utility composition and control ownership. If both agree but late intervention persists, inspect sensing/decision cadence and travel time. If uncertainty dominates unfamiliar attacks, use a declared conservative fallback and measure its opportunity cost rather than presenting a made-up DPS as knowledge. Owner preference remains necessary at real trade-offs; empirical calibration can narrow the disagreement but cannot select values for them.

## E07: measure destination validity before path quality

For mining, enumerate candidate standing/body poses and ask the actual tool predicate whether each can hit a permitted ore tile; for a shot, run the actual weapon trajectory/range predicate at the candidate pose; for pickup, ask contact/capacity rules; for torch placement, ask the actual placement rule. Keep both rejected and unevaluated candidates visible.

Compare cheap shortlist sizes, spatial sampling patterns, full local enumeration where affordable and a shared authoritative success predicate. Include odd/even ledges to expose stride sampling, arc shots blocked by straight-line visibility, moving targets, reach changes and the tolerance gap between navigation arrival and work reach. A reachable point is a useful destination only if the purpose can succeed there under its declared assumptions.

If a usable candidate exists but was never generated, fix candidate coverage. If generated but not solved because of budget, compare E12 query allocation. If solved and good but not selected, inspect scoring. If selected and reached but no longer useful, inspect freshness/invalidation. If there is no useful local position, correctly refuse or investigate a wider safe region; do not let “best available” imply “good enough.”

## E08: preserve unknown, failed and proven as separate results

Exercise deadline exhaustion, node-cap exhaustion, omitted candidate generation, changed target generation, stale capability, known unreachable graph region, physical transition refusal and successful native proof. Require a reason and scope on every result. Route-to-goal and return-from-goal are separate dimensions.

The specific current counterexample is a large downward edge whose reverse search runs out of work. It is currently allowed by the one-way filter. A new contract must not call that return-proven. It may permit a separately certified reversible prefix, wait, or use a separately justified player-following one-way permission. The experiment's pass line is correct classification and action under each outcome; it is not that every unknown eventually becomes reachable.

If too many optional jobs defer, inspect search budget, evidence reuse and safe-prefix affordances. If fewer defer only because unknown was relabelled safe, reject the change. If player permission is needed, E14 must provide actual player traversal history instead of inferring history from current geometry.

## E09: distinguish graph omissions from search work

Use native-confirmed examples of run-up jumps, slopes, half blocks, platforms, doors, narrow passages, liquid exits, directed drops and future capability sets. For each, record whether the graph contains a transition with compatible entry conditions, whether search finds it and whether the live body executes it. Compare fresh A*, retained A*, weighted/anytime search and incremental repair only on an identical graph and cost definition.

Increase work budgets on the same input. Discovery at a larger budget supports a search-work explanation. A missing edge despite native feasibility supports representation/transition generation. A graph path with native failure supports model/entry/execution diagnosis. A repeated queue restart supports reuse/invalidation diagnosis. None of these outcomes is captured by the single sentence “A* failed.”

D* Lite/LPA* comparisons additionally require stable node identities, correct directed predecessors and an edge-change journal. Include fixed work goals, moving origins, moving goal regions and capability changes. Report first valid route latency, repaired expansions, total cost, queue/memory overhead and native completion. Reject an apparently fast algorithm if its correctness assumptions differ from the baseline or it silently uses a more permissive graph.

## E10: test actual entry state and safe termination

Sweep native body pose, horizontal/vertical velocity, support, wetness, jump resources and collision flags around the same macro edge. Compare the coarse planner, portable simulator, native predictor and actual NPC update. Record the first divergent state and which component supplied the control. A fixture starting from an ideal take-off cannot prove a running approach will reach that state.

For avoidance and escape, measure sustained safe landing/head clearance, life and remaining breath, not merely initial upward displacement. For a partial route, require a terminal state that supports a declared continuation or return under current information. A safe-looking finite horizon that ends above a fatal pit is a failed policy. If a full phase-space global search becomes necessary, its evidence is repeated irreducible state aliasing that local entry contracts cannot economically resolve, not the mere existence of velocity.

## E11: qualify and invalidate route experience

Replay an executed directed transition unchanged, then vary terrain shape, platform state, liquid, body entry, target region and capability/resource profile. The retained experience may save work only when its relevant preconditions still hold or a fresh validator confirms them. A failed physical attempt must update the relevant evidence; a voluntary interruption must not poison an otherwise valid edge.

Measure reuse hit rate, native success, stale admissions, invalidation breadth and storage cost. If reuse saves little because keys are too specific, compare a stronger equivalence relation with the same false-admission test. If reuse fails because keys omit a relevant state, refine the key. If world changes make experience mostly harmful, keep a bounded short-lived cache instead of a general archive. Novelty/exploration memory is evaluated separately from successful-route memory.

## E12: spend computation where its result can change the action

Record every expensive route, reach, return, firing-position and trajectory query: candidate/activity identity, input revisions, start/stop clock, work count, result, cache status and decision affected. Compare eager evaluation, fixed round-robin budgets and result-sensitive scheduling. This operationalises the value-of-computation question without requiring a learned metareasoner.

A candidate that cannot beat the incumbent under any admissible bound can defer expensive refinement. A query that determines a hard constraint may need priority even when the candidate's estimated value is low. Deferred queries remain unknown. Tests must report starvation and omitted opportunities, because lower average CPU bought by never examining difficult work is not an improvement.

If cost is concentrated in repeated identical queries, repair invalidation/reuse. If it is genuine unique exploration, compare budgeting and abstraction. If a cheap bound incorrectly discards the best candidate, refine the bound. Only consider learned query scheduling after there is a labelled outcome stream indicating when computation changed a decision.

## E13: distinguish opportunism from consequential planning

Begin with the owner's ore–bat–drops example. If defeating the bat leaves ore valuable, current-value selection can return to ore without a forced resume rule. If nearby drops are cheap to collect, incidental collection can win; distant drops can wait. That sequence alone does not require GOAP or HTN.

Then add a genuinely consequential case inside the closed ability kit: an optional detour positions the companion for two future opportunities while preserving a return path, or different unlocked movement methods consume different resources and change what can be done afterwards. Hold observations and physical predicates constant. Compare reactive utility plus retained activities with a bounded short-horizon plan and, separately, authored HTN methods.

If planning improves only the consequential cases, keep it scoped there. If a planner merely sequences deterministic `approach → use`, use a local executor. If it wins by assuming unknown predicates are true, reject the comparison. If the observation/model cannot predict enough of the future, shorten the horizon and measure the lost benefit. If flat/grouped utility matches the plan with less computation and equally readable behaviour, prefer the simpler measured result.

## E14: measure player-relative autonomy and exploration

Use the same cave geometry with different player traces: sustained fast traversal; stationary mining; moving within a local area while placing torches; retreat under threat; dropping into a previously inaccessible region; death and respawn. Record player motion/activity history and the inferred local engagement, but label inferred intent as uncertain.

The companion should not require a matching player tool to do useful permitted work nearby. It should reduce optional excursions during sustained travel and adapt when the player remains in an area. Compare distance-only, short motion/activity context and an explicit local-area model. Ground covered needs a spatial memory with freshness and a declared purpose; a short trail is not a complete exploration map, and route experience is not novelty memory.

For one-way following, record the actual player crossing and region generation; current relative position is insufficient historical evidence. For incidental loot, measure marginal detour/time/risk against the ongoing useful activity, not distance from the companion alone. If these estimates explain the owner's accepted examples, retain them without inventing mission logic. If a persistent multi-step area plan is required, E13 decides its scope.

The shared-companionship follow-up adds an empty-world case: with no ore, enemies, useful darkness or drops, player motion must still cause an explicit keep-company destination. Compare a short worthwhile job during travel with a sequence of individually cheap detours; the latter must account for separation already accumulated rather than postponing reunion indefinitely. Pair near slow work behind the companion with quick work along the player's heading to distinguish straight-line distance from actual time-apart and reunion cost. Invalidate the estimate when pace, terrain or capabilities change. Interpret intent as a fallible estimate, not an observed destination.

## E15: capability, compatibility and persistence stress

Run the same obligations under increasing and decreasing movement/tool/reach/defence capability profiles, including mid-job changes, world reload and target-slot reuse. Include unknown modded NPCs/projectiles, shaped/actuated tiles, inventory capacity, protected home regions, disallowed edits and closed-kit weapons. This is a matrix of representative interfaces and failure modes, not a promise to enumerate all mods.

The capability profile must have one authority and a revision; every route, reach, damage, opportunity and archived-experience consumer must either revalidate or explicitly state why the change is irrelevant. A stronger capability should not keep an old impossibility verdict merely because a cache survives. A weaker capability must not inherit an old proof. Downing and recovery cancellation must preserve the single body owner and no-teleport boundary.

A compatible mod should consume supported native predicates and observable attack facts where possible. If an external mod bypasses those contracts, expose reduced knowledge and a conservative declared behaviour rather than guessing arbitrary script semantics. Saving must retain only durable facts whose identity can survive a load; transient control ownership and stale entity slots need fresh reconstruction.

## E16: run held-out play and keep uncertainty visible

The [Behavioural Acceptance Matrix](<Behavioural Acceptance Matrix.md>) specifies positive cases, changed circumstances, evidence and failure branches for all twenty-seven README responsibilities. Its pairs are the minimum behavioural coverage, not a claim that one run per pair is sufficient.

After the earlier discriminators pass, use a fixed acceptance set drawn from the README plus held-out worlds, geometries and capability combinations. Include easy cases, previously failing captures and combinations not used for tuning. Pair runs where possible and record seeds, configuration, player trace and engine version. Report denominators and uncertainty intervals; repeated ticks within one encounter are correlated observations, not thousands of independent trials.

Live judgement remains necessary for whether autonomy feels helpful and readable. It is requested at a concrete checkpoint, with a prepared build and specific scenes, rather than used as a substitute for diagnosis. Passing a finite set closes its documented contract; it never proves perfect movement across every Terraria world or future mod. A new failure is classified by the first broken contract and added to the held-out regression set after its diagnosis, rather than met by another unmeasured global weight adjustment.

## Recorder and God’s-eye upgrades required by these tests

The existing recorder already has body samples, broad decision/movement evidence, projectile events and rolling terrain. Extend its identity and semantics rather than create parallel recorders.

| Proposed record | Fields that make it useful | Existing ambiguity it resolves |
|---|---|---|
| Capture manifest | Exact source/package/configuration/mod/capability hashes; schema; clock definitions; enabled streams; sampling and loss policy. | A version number does not identify the tested code or observation coverage. |
| Fresh decision event | Chooser execution ID; candidate set/coverage; activity and target generations; raw/composed/continuation factors; winner; rejected/unknown reasons. | Last activity label and candidate status look like freshly executed choices. |
| Activity lifecycle | Begin/continue/suspend/resume/abandon/complete; reason; remaining work; expected benefit; measured productive progress. | Time, movement and unrelated shots can masquerade as job progress. |
| Resource grants | Requested and granted feet/tool/aim/light channels; owner; coherent-use phase; cancellation reason; actual final controls. | Reflex/recovery early returns conceal which system acted. |
| Feasibility query | Query identity; scope; input revisions; candidate coverage; stop cause; route and return status separately. | Timeout, sampling miss and proven impossibility collapse together. |
| Position success predicate | Purpose; candidate pose; native interaction/shot/contact predicate; valid-until assumptions; arrival predicate. | A navigator can arrive without enabling the action. |
| Movement attempt | Attempt ID; parent route/activity; actual entry; predicted terminal; native terminal; completion/fault/interruption; interrupting owner. | Retained edge status overcounts failures and mixes denominators. |
| Outcome attribution | Tool hit attempted; native damage/block change; shot target vs pursuit target; predicted/actual harm; actual pickup transfer. | Selection or animation is counted as useful work. |
| Query cost | Inclusive/exclusive stage durations; cache hit; work units; allocations where affordable; chosen/deferred work. | `decide_ms` is mistaken for the cost of utility arithmetic. |
| Retrospective reference | Captured terrain time/coverage, optional off-line validation, missing data mask and explicit counterfactual flag. | God’s-eye information is attributed to the companion at decision time. |

Prefer structured fields for analysis; reserve formatted strings for display. Emit normal events on meaningful changes and bounded detailed candidate samples on selected diagnostic captures. Measure backpressure, dropped records and enabled/disabled overhead. The inspector should display the same event identities, evidence ages and uncertainty states the offline reader uses, with optional full detail on selection rather than filling every frame with text.

These upgrades are common to all three proposals, but their additional traces differ: the grouped path needs parent/child competition and abort propagation; the planning path needs considered plans, preconditions/effects, horizon, predicted outcomes and replan causes. Add those only with the mechanism they explain.
