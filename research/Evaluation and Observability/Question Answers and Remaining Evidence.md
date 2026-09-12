# Question answers distinguish evidence from a verdict

**Evidence date: 12 September 2026. Research baseline: `d60b92b`; gameplay source is unchanged from `4296f85`.** This is a coverage ledger, not an architecture decision. Each answer is deliberately a disposition: an accepted requirement records the owner's contract, an **observed** answer records a capture or direct source reading, a **historical claim** records an earlier commit/test report without rerunning it, an **inference** connects evidence without claiming a reproduced cause, and **unresolved** identifies the smallest relevant experiment. Links name the report section that carries the supporting detail. E01–E16 refer to [Experiments and Recorder Requirements](<Experiments and Recorder Requirements.md>).

The ledger answers all 140 questions once. “Unresolved” is still an answer: it says what cannot be inferred from desk research, old recordings, or source shape, and prevents an attractive proposal from receiving borrowed certainty. No listed experiment has been implemented or run.

The later 12 September [purpose-family discussion](<../Decision Architecture/Purpose Families and Shared Companionship.md>) selects three families within Path 1 and narrows Path 2 to stronger shared execution. This ledger preserves the initial research dispositions; references below that locate grouping only in Path 2 are superseded for proposal direction by that discussion and the current [proposal synthesis](../proposal/CLAUDE.md). Source findings and historical measurements retain their original scope. E04 and E14 now include explicit offer-validity and shared-companionship comparisons.

## 1–8: establish the behavioural contract

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 1 | **Accepted requirement.** README behaviour statements are the observable contract; utility, A*, fixed sequences and broad behaviour names are reopened mechanisms. | [Behaviour, Opportunities and Comparison Criteria: accepted requirements](<../Decision Architecture/Behaviour, Opportunities and Comparison Criteria.md#accepted-requirements-and-unresolved-choices-stay-distinct>) |
| 2 | **Accepted requirement.** Closed abilities, no teleporting and autonomous nearby collaboration are firm; danger, effort, benefit and capability are contextual comparisons. | Same; E06, E15 |
| 3 | **Accepted requirement.** Remaining effort matters only through future cost/value; who already made progress does not create ownership or sunk-cost entitlement. | [Progress changes future cost](<../Decision Architecture/Behaviour, Opportunities and Comparison Criteria.md#progress-changes-future-cost-effort-already-spent-is-not-a-reward>); E03 |
| 4 | **Accepted behaviour; inferred mechanism.** Sharing an ore vein is allowed and a separate tree is preferred. Target-specific opportunity value and marginal cost are a proposed generalisation, not proof that two local rules are always unnecessary. | [An opportunity is more concrete](<../Decision Architecture/Behaviour, Opportunities and Comparison Criteria.md#an-opportunity-is-more-concrete-than-a-behaviour-name>); E03/E14 |
| 5 | **Accepted requirement.** Sustained player travel should reduce optional excursions; local engagement permits independent nearby work. Existing recordings lack player-intent evidence. | [E14](<Experiments and Recorder Requirements.md#e14-measure-player-relative-autonomy-and-exploration>) |
| 6 | **Unresolved.** Excursion tolerance needs observed player trace, return proof and capability/resource envelope; distance alone cannot settle duration or safety. | E14, E15 |
| 7 | **Inference.** Decisions need timestamped, scoped facts with revision/coverage/unknown state; old terrain or future enemy behaviour is not knowable. | [God’s-eye observation](<Recorded Episodes and Measurement Limits.md#gods-eye-observation-is-broad-but-remains-bounded>); E01 |
| 8 | **Accepted requirement.** Capability changes must revise feasibility, reach, danger and opportunity value while closed-kit/no-teleport/product boundaries remain invariant. | E15 |

## 9–16: reconstruct decisions and regressions

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 9 | **Historical claim.** The commit/transcript reconstruction identifies FSM→utility, physical A*, returnability, route memory, commitment reversal, admission failures and recorder growth. Slate/transcript access is represented only through paraphrased historical evidence. | [Pivotal Decisions](<../Historical Evidence/Pivotal Decisions and Conversation Evidence.md>); [Q9](<../Historical Evidence/Diagnostic Instruments, Contradictions and Open Questions.md#q9-which-evidence-corresponds-to-each-pivotal-episode>) |
| 10 | **Historical claim.** Wider decision windows recover owner corrections and rejected interpretations that commit diffs omit; they do not establish runtime outcomes. | [Q10](<../Historical Evidence/Diagnostic Instruments, Contradictions and Open Questions.md#q10-what-do-the-wider-windows-reveal>) |
| 11 | **Historical claim.** Priority branches lost because overlapping assistance needed contextual comparison; this does not prove utility sufficient. | [Q11](<../Historical Evidence/Diagnostic Instruments, Contradictions and Open Questions.md#q11-why-was-the-original-priority-state-machine-replaced>) |
| 12 | **Historical claim.** A* evolved into terrain transitions, position selection and physical execution; its present limits are distributed rather than one algorithmic defect. | [Q12](<../Historical Evidence/Diagnostic Instruments, Contradictions and Open Questions.md#q12-how-did-a-and-the-terrain-graph-evolve>) |
| 13 | **Historical claim.** Directed executed-route evidence was introduced to reuse validated ordinary traversal; reported improvement is historical scope, not a new live measurement. | [Q13](<../Historical Evidence/Diagnostic Instruments, Contradictions and Open Questions.md#q13-why-was-route-memory-introduced-what-did-it-implement-and-what-improved>); E11 |
| 14 | **Historical claim.** Unknown, directed returnability and player-taken one-way permission became distinct, but timeout still needs a sound action contract. | [Q14](<../Historical Evidence/Diagnostic Instruments, Contradictions and Open Questions.md#q14-how-did-reachability-returnability-and-unknown-change>); E08 |
| 15 | **Historical claim.** Flat commitment could retain unproductive work; its shared-stall replacement could penalise the new activity and cause oscillation. Neither failure is evidence against all continuation. | [Q15](<../Historical Evidence/Diagnostic Instruments, Contradictions and Open Questions.md#q15-what-led-to-the-commitment-experiment-and-why-did-its-outcome-differ-from-its-rationale>); E03 |
| 16 | **Inference.** Several repairs exposed useful boundaries, but only source defects and captured episode facts are measured; causal repair requires replay or fresh controlled capture. | [Q16](<../Historical Evidence/Diagnostic Instruments, Contradictions and Open Questions.md#q16-which-repairs-were-progress-repeated-assumptions-or-useful-intermediate-work>); E02 |

## 17–24: attribute observed failures before replacing systems

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 17 | **Observed.** Episodes A–C are identified by named recording/tick windows and their capture limits; exact old source/package/configuration is not fully recoverable. | [Recorded episodes](<Recorded Episodes and Measurement Limits.md#the-corpus-supports-a-chronology-not-a-controlled-version-ranking>); E01 |
| 18 | **Observed.** Labels/statuses are sampled or retained, candidate evaluation can mutate state, and event absence is not chooser absence. | [Measurement limits](<Recorded Episodes and Measurement Limits.md#gods-eye-observation-is-broad-but-remains-bounded>); E01 |
| 19 | **Unresolved.** Ore-side standing can be candidate state, not mining. Choice, invalid destination and execution remain rival first failures. | [Episode A](<Recorded Episodes and Measurement Limits.md#episode-a-a-pot-activity-prevents-ore-work-while-its-own-approach-remains-unresolved>); E02/E07 |
| 20 | **Inference.** Inaccessible/far hunting can enter through target admission, bounded position generation or stale purpose progress; old capture cannot isolate the admitting stage. | [Episode C](<Recorded Episodes and Measurement Limits.md#episode-c-a-hunt-can-arrive-at-a-spot-without-obtaining-a-shot>); E02/E07 |
| 21 | **Observed limit.** `Interrupted` joins multiple attempts and owners; it is neither necessarily-needed interruption nor a repeated jump failure. | [Episode B](<Recorded Episodes and Measurement Limits.md#episode-b-the-pool-shows-an-ownership-and-danger-model-question>); E05/E10 |
| 22 | **Inference.** Arrival without shot leaves candidate coverage, trajectory, readiness and firing as alternatives; mixed pair evidence rules out a universal range claim. | [Episode C](<Recorded Episodes and Measurement Limits.md#episode-c-a-hunt-can-arrive-at-a-spot-without-obtaining-a-shot>); E02/E07 |
| 23 | **Observed.** One tick spent 41.82ms in the broad decision interval, not demonstrably in utility arithmetic; inclusive stage traces are missing. | [Timing fields](<Recorded Episodes and Measurement Limits.md#timing-fields-need-the-computation-they-actually-measure>); E12 |
| 24 | **Inference.** Confirmed analyser overclaim is a recorder-contract defect; episode alternatives may be tuning, interfaces or representation, with replacement premature. | [Range overstatement](<Recorded Episodes and Measurement Limits.md#the-report-generator-overstates-the-range-explanation>); E01/E02 |

## 25–32: make opportunities and uncertainty explicit

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 25 | **Inference.** An opportunity needs identity, benefit, remaining work, approach/success predicate, risk, uncertainty, relevance and revisions. | [Opportunity model](<../Decision Architecture/Behaviour, Opportunities and Comparison Criteria.md#an-opportunity-is-more-concrete-than-a-behaviour-name>); E01/E07 |
| 26 | **Inference.** Share observation facts but retain attackability, pursuit feasibility, harm and intervention as separate predicates; proximity is insufficient. | [Shared controls](<../Implementation Evidence/Decisions, Activities and Shared Controls.md#danger-is-an-observation-derived-preference-not-an-estimate-of-expected-lost-health>); E06 |
| 27 | **Unresolved.** Current danger is not calibrated expected harm; effective damage, rate, regeneration and actor exposure require matched threats. | E06 |
| 28 | **Accepted requirement.** Modded enemy futures remain uncertain; use observed/native facts plus conservative declared fallback, never a guessed script catalogue. | E06/E15 |
| 29 | **Inference.** Motion/activity context can explain local autonomy better than distance, but player intent must remain uncertain. | E14 |
| 30 | **Inference.** Route experience proves directed transitions; opportunity memory tracks candidates; trail/coverage record player/world exploration. None substitutes for the others. | E11/E14 |
| 31 | **Unresolved.** Lighting value needs player benefit, access, supplies and edit permissions; current records do not jointly expose them. | E07/E14/E15 |
| 32 | **Observed risk.** Bounded sampling/caches and score-side effects can hide candidates; coverage and query-stop cause are not yet sufficient. | [Decision evidence](<../Implementation Evidence/Decisions, Activities and Shared Controls.md#what-this-evidence-supports-changing-first>); E01/E12 |

## 33–40: test grouping and boundaries

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 33 | **Observed.** Present behaviours blend candidate discovery, target/position selection, lifecycle and execution; ownership is not uniformly separated. | [Decisions and activities](<../Implementation Evidence/Decisions, Activities and Shared Controls.md>); E02/E05 |
| 34 | **Inference.** Purpose grouping, physical phase and resource ownership cut across each other; no grouping dominates before matched comparison. | [Three grouping alternatives](<../Decision Architecture/Behaviour, Opportunities and Comparison Criteria.md#three-grouping-alternatives-deserve-a-fair-comparison>); E04/E05 |
| 35 | **Inference.** Mining/chopping/pots share approach/use/completion shape but retain ore permission, tree ownership and breakable-specific effects. | Same; E04/E07 |
| 36 | **Inference.** Hunt/guard/kite can share target/threat facts while retaining intervention purpose, movement objective and cancellation boundaries. | [Combat evidence](<../Implementation Evidence/Decisions, Activities and Shared Controls.md#pursuit-and-firing-are-distinct-but-their-evidence-must-agree-on-what-each-promises>); E04/E06 |
| 37 | **Unresolved.** Held light is often a compatible resource state; placement creates a spatial/edit objective. Test rather than force one group. | E05/E07 |
| 38 | **Inference.** Target-specific opportunity is the useful comparison unit; activity records provide continuity, while groups organise authoring. | [Opportunity model](<../Decision Architecture/Behaviour, Opportunities and Comparison Criteria.md#an-opportunity-is-more-concrete-than-a-behaviour-name>); E03/E04 |
| 39 | **Unresolved.** Parent aggregation can reward child count or hide valuable children; compare maximum, sum and explicit cost on identical leaves. | E04 |
| 40 | **Unresolved.** Hierarchy helps only if ownership/abort traces improve; otherwise it relocates exceptions into transitions. | E04/E05 |

## 41–48: compare decision architectures without a winner

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 41 | **Inference.** Utility can express contextual trade-offs, but hard boundaries, uncertainty and concurrent ownership are awkward when encoded as scores. | [Utility remains a candidate](<../Decision Architecture/Decision, Commitment and Computation.md#utility-remains-a-candidate-with-a-narrower-responsibility>); E03/E04 |
| 42 | **Observed source risk.** Zero vetoes, correlated factors and incomparable ranges can distort current composition; no measured aggregate ranking yet exists. | Same; E03/E04 |
| 43 | **Inference.** Explicit constraints suit firm product permissions and physical legality. Ordinary danger remains a contextual trade-off; the owner has not authorised converting every safety preference into a veto. | [Contract above utility](<../Decision Architecture/Behaviour, Opportunities and Comparison Criteria.md#a-contract-above-utility-exists-even-if-it-is-not-another-ai>); E03 |
| 44 | **Inference.** Shared, revisioned capability facts must feed every score/predicate; scattered scaling is inconsistent by construction. | [Capability scaling](<../Implementation Evidence/Decisions, Activities and Shared Controls.md#capability-scaling-exists-in-pieces-and-must-become-a-shared-contract>); E15 |
| 45 | **Unresolved.** Flat, hierarchical and opportunity utility need same candidate/physics/outcome comparison. | E04 |
| 46 | **Inference.** BT/HFSM suits visible execution/hard lifecycle, not generic selection or shared-control resolution. | [BT/HFSM](<../Decision Architecture/Decision, Commitment and Computation.md#bthfsms-buy-visible-execution-policy-and-inherit-hand-ownership>); E05 |
| 47 | **Inference.** GOAP/HTN earns a bounded comparison only when enabling consequences change the present move; ordinary approach→use does not. | [Plan only when consequences change](<../Decision Architecture/Decision, Commitment and Computation.md#plan-only-when-a-future-consequence-changes-the-present-choice>); E13 |
| 48 | **Inference.** Learning can estimate local transition, preference residual or query value only after labelled outcomes and held-out safety checks; it cannot learn permissions. | [Learning limits](<../Decision Architecture/Decision, Commitment and Computation.md#learning-is-credible-only-with-a-bounded-decision-and-a-real-evaluation-contract>); E12/E16 |

## 49–56: continuation without sunk-cost rules

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 49 | **Inference.** Progress is purpose-specific productive effect: travel reaches a viable state; work changes target; protection prevents harm; standing still can be work. | [Outcome measures](<Experiments and Recorder Requirements.md#compare-the-outcome-the-player-sees>); E03 |
| 50 | **Accepted requirement.** Lower remaining effort and higher future completion value may retain work; elapsed effort alone may not. | [Progress model](<../Decision Architecture/Behaviour, Opportunities and Comparison Criteria.md#progress-changes-future-cost-effort-already-spent-is-not-a-reward>); E03 |
| 51 | **Inference.** Retention needs target identity, circumstance revisions and no-progress evidence; changed targets cannot erase failures forever. | E03/E11 |
| 52 | **Unresolved.** Threat windows require preparation, interruption, response and uncertainty traces, rather than a fixed timer. | E06 |
| 53 | **Accepted requirement.** Mining and collection are separately valued; returnability, abilities and player movement may change drop value later. | E03/E14 |
| 54 | **Inference.** Value incidental work by marginal extra travel/action/risk, not distance or compulsory pickup. | [Incidental actions](<../Decision Architecture/Behaviour, Opportunities and Comparison Criteria.md#incidental-actions-compete-by-marginal-cost>); E03/E14 |
| 55 | **Inference.** Re-evaluation suffices for ordinary sequential opportunity; explicit planning is reserved for measured consequential enabling steps. | E13 |
| 56 | **Inference.** Hysteresis must retain a still-valid opportunity through brief reflexes while exposing target/reason/progress, rather than a bonus that rewards stall. | E03/E05 |

## 57–64: coordinate hands, feet and interruptions

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 57 | **Observed.** Coordinator/reflex/recovery and behaviours can impose objectives/control; present evidence does not expose one final per-channel grant. | [Coordinator evidence](<../Implementation Evidence/Decisions, Activities and Shared Controls.md#the-coordinator-decides-which-decisions-are-allowed-to-run>); E05 |
| 58 | **Accepted requirement.** Active pick/axe use spans normal hit pauses; shooting during approach is legal, tool/weapon weaving during use is not. | E05 |
| 59 | **Accepted requirement.** Held light may yield then resume around shooting; placement consumes supplies and needs a purpose-specific predicate. | E05/E07 |
| 60 | **Inference.** Compatibility is per output channel/phase, not broad behaviour label; feet, aim, tool, light and presentation must each declare claims. | E05 |
| 61 | **Inference.** Avoidance may preserve destination/traversal when it records an interruption and restores a valid state; unsafe/invalid cases must abandon. | E05/E10 |
| 62 | **Unresolved.** Local avoidance must be compared against harm, escape terminal state and player protection in matched scenes. | E06/E10 |
| 63 | **Accepted requirement.** Ordinary travel, recovery, survival and downing have separate authority/boundaries; recovery is not ordinary route mobility. | E05/E15 |
| 64 | **Inference.** Handoff needs momentum, body/capability revisions, resources, progress and interruption reason; labels alone are insufficient. | E05/E10 |

## 65–72: separate graph, search and experience

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 65 | **Observed.** Route, region, reverse-return and synchronous reach questions have distinct budgets/result meanings. | [Several searches](<../Implementation Evidence/Routes, Returnability and Physical Execution.md#there-are-several-searches-with-different-meanings-and-budgets>); E08/E09 |
| 66 | **Observed.** Tile plus counters aliases velocity, support, liquid and resources; exact-state need is conditional on local refinement failure. | [Motion state](<../Implementation Evidence/Routes, Returnability and Physical Execution.md#a-tile-plus-counters-is-not-a-complete-motion-state>); E10 |
| 67 | **Unresolved.** Existing graph has known shaped/entry/capability risks; native-confirmed transition matrix separates omission from search. | E09 |
| 68 | **Observed.** Current cost/heuristic does not justify shortest-path claims; it can still provide bounded feasible-prefix results when labelled accordingly. | [Heuristic limit](<../Implementation Evidence/Routes, Returnability and Physical Execution.md#the-current-heuristic-does-not-support-a-shortest-path-guarantee>); E09 |
| 69 | **Inference.** Retained ordinary search may suffice for moving origins; D*/LPA* needs stable directed predecessors and edge-change journal, not a substitution. | [D* requirements](<../Navigation Research/Dynamic Platformer Navigation.md#what-d-lite-would-require-here-beyond-replacing-a-call-to-a>); E09 |
| 70 | **Observed.** Experience stores executed directed evidence with fingerprints/invalidation; its broad live reuse benefit and false-admission rate are unmeasured, despite reported bounded fixture gains. | [Experience memory](<../Implementation Evidence/Routes, Returnability and Physical Execution.md#experience-memory-is-executed-directed-evidence-with-a-deliberately-limited-capability-domain>); E11 |
| 71 | **Unresolved.** Safe prefix requires viable continuation/return and safe terminal, not geometric progress alone. | E08/E10 |
| 72 | **Inference.** Retained frontier can support away-first detours only if selected partial endpoint advances toward a declared safe continuation and does not restart. | [Retention weakness](<../Implementation Evidence/Routes, Returnability and Physical Execution.md#retention-already-supports-acting-while-planning-but-prefix-choice-has-a-strategic-weakness>); E09/E10 |

## 73–80: establish physical contracts

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 73 | **Observed.** Nominal macro proofs can diverge from real run-up entry state; pose, velocity/support/wetness/resources are causal candidates. | E10 |
| 74 | **Historical claim.** Native parity checks reportedly cover thousands of comparisons, but this research did not rerun them; coverage is not a full physical guarantee. | [Physical execution](<../Implementation Evidence/Routes, Returnability and Physical Execution.md#physical-execution-has-made-progress-without-proving-the-whole-route-model-complete>); E09/E10 |
| 75 | **Inference.** Run-up through settling must use actual capability/stat profile and native terminal validation, not one nominal speed. | E10/E15 |
| 76 | **Unresolved.** Breath, damage and limited abilities need outward-and-return resource envelope and native survival outcomes. | E06/E10/E15 |
| 77 | **Accepted requirement.** Returnable differs from unknown and player permission; voluntary one-way travel needs actual player traversal history, not current geometry. | E08/E14 |
| 78 | **Inference.** Terrain/threat/capability/body revisions invalidate dependent proofs; unrelated evidence can continue when scoped revisions hold. | E11/E15 |
| 79 | **Unresolved.** Usable ore approach requires native range/swing/obstruction/permission success, not navigator arrival. | E07 |
| 80 | **Inference.** Store failure class and relevant revision; physical fault informs edge evidence, voluntary interruption does not poison it permanently. | E10/E11 |

## 81–90: bound what case studies transfer

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 81 | **Historical/source claim.** RimWorld baseline separates jobs, reservations and interruption; transfer is organisational, not proof for platformer movement. | [RimWorld baseline](<../Game and Mod Case Studies/RimWorld Work Scheduling.md#baseline-what-the-sources-can-and-cannot-establish>) |
| 82 | **Source claim.** Pick Up And Haul batches carrying with reservations/recovery; it demonstrates costs of carry-state scheduling, not Terraria collection policy. | [Pick Up And Haul](<../Game and Mod Case Studies/RimWorld Work Scheduling.md#pick-up-and-haul-carry-state-batching-with-reservations-and-recovery>) |
| 83 | **Source claim.** While You’re Up is a detour policy; its public implementation/version evidence is limited. | [WYAN countercase](<../Game and Mod Case Studies/RimWorld Work Scheduling.md#while-youre-up-lineage-and-modern-compatibility-countercase>) |
| 84 | **Source claim.** While You Are Nearby transforms priority by locality rather than proving route opportunism. | [Nearby](<../Game and Mod Case Studies/RimWorld Work Scheduling.md#while-you-are-nearby-priority-transformation-not-pathfinding>) |
| 85 | **Source claim.** Common Sense makes preparation/order policy explicit, with domain rules that do not transfer as universal companion logic. | [Common Sense](<../Game and Mod Case Studies/RimWorld Work Scheduling.md#common-sense-a-real-detour-test-and-explicit-queue-ownership>) |
| 86 | **Source claim.** Free Will/YouDoYou identity and scalar-score claims were corrected/scoped; no evidence establishes a general utility solution. | [Free Will](<../Game and Mod Case Studies/RimWorld Work Scheduling.md#free-will--youdoyou-identity-corrected-and-scalar-scoring-scoped>) |
| 87 | **Source claim.** Compatibility reports show competing pawn schedulers collide through ownership/order; prevalence and causal proof remain sparse. | [RimWorld synthesis](<../Game and Mod Case Studies/RimWorld Work Scheduling.md#what-combines-what-substitutes-and-what-would-decide-a-transfer>) |
| 88 | **Source claim.** Starsector separates hull positioning, target/weapon control and flux cancellation; no direct outcome comparison proves transfer. | [Starsector mechanism](<../Game and Mod Case Studies/Starsector Ship and Weapon AI.md#mechanism-walkthrough-three-targets-not-one>) |
| 89 | **Observed/source claim.** Terraria NPC boundary and TerraGuardians show local NPC ownership/reusable native predicates; other-mod compatibility remains conditional. | [Terraria boundaries](<../Game and Mod Case Studies/Terraria NPC and Companion Boundaries.md>) |
| 90 | **Research inference.** Platformer literature distinguishes stateful graph/entry/repair concerns; reported success does not prove dynamic Terraria reliability. | [Dynamic platformer claims](<../Navigation Research/Dynamic Platformer Navigation.md#the-claims-and-their-limits>); E09/E10 |

## 91–100: set fair evaluation and change criteria

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 91 | **Proposed mechanism.** One capability authority/revision should make the accepted requirement for coherent scaling reach movement, tools, combat, risk, value and returnability. | E15 |
| 92 | **Inference.** Replace vanilla-specific assumptions with native predicates/observable facts; unsupported external behaviour remains reduced knowledge. | E15 |
| 93 | **Inference.** Capability changes must invalidate dependent proofs, while physical differences such as entry momentum may legitimately persist. | E10/E15 |
| 94 | **Unresolved.** README-derived plus held-out terrain, threats, player traces and progression profiles is the representative matrix; it has not run. | E16 |
| 95 | **Inference.** Use paired saved states where possible, fixed seeds/traces and outcome denominators; acknowledge altered-world counterfactual limits. | E02/E16 |
| 96 | **Proposed measurement contract.** Useful work, disruption, protection, coherence, physical reliability, calibration, latency/cost and explainability jointly measure experience. | [Outcome table](<Experiments and Recorder Requirements.md#compare-the-outcome-the-player-sees>) |
| 97 | **Inference.** Require matched fixture, capture manifest, held-out cases and first-broken-contract attribution before calling improvement. | E01/E16 |
| 98 | **Proposed research plan.** E01–E16 are minimal discriminators staged before whole-brain rewrites. | [Experiment catalogue](<Experiments and Recorder Requirements.md>) |
| 99 | **Inference.** Keep when outcome/contracts hold; tune only measured parameters; repair contract for first broken interface; change representation for repeated aliasing/omission; replace only comparative evidence. | E02/E04/E09/E16 |
| 100 | **Accepted reporting contract.** Preserve counterevidence, scope, uncertainty and reversal condition with each conclusion. | [Evaluation CLAUDE](<CLAUDE.md>) |

## 101–110: verify observer provenance

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 101 | **Observed.** World/brain/event clocks differ; reflex/downed bypass breaks naive joins and wall time has another meaning. | [Episode B](<Recorded Episodes and Measurement Limits.md#episode-b-the-pool-shows-an-ownership-and-danger-model-question>); E01 |
| 102 | **Observed.** Decision events/labels may be retained or changed snapshots, not fresh chooser executions. | [God’s-eye limits](<Recorded Episodes and Measurement Limits.md#gods-eye-observation-is-broad-but-remains-bounded>); E01 |
| 103 | **Observed.** Work statuses can mean candidate evaluation; they do not prove selection, tool action or productive effect. | [Episode A](<Recorded Episodes and Measurement Limits.md#episode-a-a-pot-activity-prevents-ore-work-while-its-own-approach-remains-unresolved>); E01 |
| 104 | **Observed.** Planned edges, begun attempts, retained outcomes and stable arrivals have different denominators. | [Movement census](<Recorded Episodes and Measurement Limits.md#movement-census-columns-have-different-denominators>); E01 |
| 105 | **Observed.** Mixed weapon rejection evidence caused a confirmed universal-range analyser defect. | [Range overstatement](<Recorded Episodes and Measurement Limits.md#the-report-generator-overstates-the-range-explanation>); E01 |
| 106 | **Observed gap.** Old captures identify date/version incompletely, not exact source/package/config/mod/capability hash. | [Recording inventory](<Recording Inventory and Reproduction.md>); E01 |
| 107 | **Observed.** Rolling terrain chunks record bounded timed geometry; earlier full world and future scripts remain unknowable. | [Terrain limits](<Recorded Episodes and Measurement Limits.md#gods-eye-observation-is-broad-but-remains-bounded>); E01 |
| 108 | **Unresolved.** Recorder/inspector/analyser overhead lacks enabled-v-disabled stage/allocation measurement. | E01/E12 |
| 109 | **Accepted requirement.** Agent evidence and retrospective reference must be explicitly separated by time, coverage and counterfactual flag. | [God’s-eye limits](<Recorded Episodes and Measurement Limits.md#gods-eye-observation-is-broad-but-remains-bounded>); E01 |
| 110 | **Proposed test design.** Known positive/negative, missing and malformed traces make observer assertions falsifiable. | E01 |

## 111–120: test hidden information-to-action contracts

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 111 | **Observed risk.** Reverse timeout is unknown, not return proof; directed-region reuse answers another question. | E08 |
| 112 | **Observed risk.** Bounded firing-position sampling cannot turn omission into no-position proof. | E07 |
| 113 | **Unresolved.** Arrival tolerance can disagree with native work predicate; record both in fixed-pose test. | E07 |
| 114 | **Proposed measurement contract.** Tool invocation is not progress; native hit/block change/completion is the productive event. | E01/E07 |
| 115 | **Observed source risk.** Independent firing/displacement may renew hunt progress without harm to its target; target-specific progress trace is required. | [Episode C](<Recorded Episodes and Measurement Limits.md#episode-c-a-hunt-can-arrive-at-a-spot-without-obtaining-a-shot>); E03 |
| 116 | **Inference.** Target motion and capability revisions invalidate every dependent cache; scoped dependencies prevent indiscriminate loss. | E11/E15 |
| 117 | **Observed.** Current heuristic/costs lack shortest-path proof; feasible-prefix/attempt outcomes remain separately meaningful. | [Heuristic limit](<../Implementation Evidence/Routes, Returnability and Physical Execution.md#the-current-heuristic-does-not-support-a-shortest-path-guarantee>); E09 |
| 118 | **Unresolved.** Retained frontier needs endpoint-advance and safe-continuation measurements on away-first detours. | E09/E10 |
| 119 | **Observed risk.** Family gating can alter discovery if current score evaluation has side effects; make candidate evaluation observational before grouping. | E04 |
| 120 | **Observed risk.** Reflex/recovery early returns can bypass ordinary hand rules; final channel grants expose it. | E05 |

## 121–130: compare computation and changing capability policy

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 121 | **Inference.** Proven value bounds can prune candidates unable to change the winner. Budget-limited deferral of other candidates must remain labelled unknown, with starvation and urgent physical checks exposed. | E12 |
| 122 | **Inference.** Urgent physical checks get explicit budget/priority while deferred work remains unknown and starvation is reported. | E12 |
| 123 | **Inference.** Incremental repair is justified only with persistent goal, sparse edge changes and required graph journal/predecessors. | [D* requirements](<../Navigation Research/Dynamic Platformer Navigation.md#what-d-lite-would-require-here-beyond-replacing-a-call-to-a>); E09 |
| 124 | **Inference.** Local entry-state refinement is first choice; repeated irreducible native divergence justifies richer global state. | E10 |
| 125 | **Inference.** Finite horizon needs return/resource terminal condition; otherwise temporary progress can strand the body. | E10/E13 |
| 126 | **Inference.** Native transition terminal outcome is a measurable bounded learning label; it cannot train permissions. | E12/E16 |
| 127 | **Proposed mechanism.** Persist stable identity-backed experience/opportunities only when revisions survive reload; rebuild transient control/entity state. | E11/E15 |
| 128 | **Proposed test design.** Irrelevant addition, duplicate, reordering/grouping and other-actor progress should preserve semantically equivalent choices. | E03/E04 |
| 129 | **Accepted requirement.** Resource accounting covers outward+return and treats recovery flight outside ordinary mobility. | E10/E15 |
| 130 | **Accepted requirement.** Player intent is inferred, recorded as uncertain and falsifiable; no silent order semantics. | E14 |

## 131–140: keep the proposals reversible and evidence-led

| Q | Disposition and answer | Evidence / next discriminator |
|---|---|---|
| 131 | **Accepted source rule.** Check project identity, version, public source/baseline and date before transfer; similarly named mods/forks are not evidence. | [Case-study scope](<../Game and Mod Case Studies/CLAUDE.md>) |
| 132 | **Observed gap.** Maintainer/community accounts can add compatibility failures but current case studies do not establish prevalence or causality; RimWorld closed-source baseline and WYAN sourcing remain limits. | [RimWorld gaps](<../Game and Mod Case Studies/RimWorld Work Scheduling.md#source-ledger-confidence-and-gaps>) |
| 133 | **Accepted classification.** Capture/commit facts are dated; contracts/principles endure; performance/behaviour claims must rerun after affected change. | [Evaluation discipline](<CLAUDE.md>) |
| 134 | **Proposed common preparation.** Purpose-to-outcome identity chain, capture manifest, fresh decision/lifecycle/resource/feasibility/movement/outcome/cost records aid every architecture. | [Recorder upgrades](<Experiments and Recorder Requirements.md#recorder-and-gods-eye-upgrades-required-by-these-tests>) |
| 135 | **Unresolved.** A lower-ranked proposal rises only by predeclared matched outcome/complexity evidence that falsifies the higher-ranked mechanism, not by additive features. | E02/E04/E13/E16 |
| 136 | **Proposed rollback rule.** Stop a branch when matched evidence shows no accepted-behaviour gain, worsened physical/uncertainty contract, or complexity without explanatory power. | E01/E16 |
| 137 | **Unresolved.** Immediate ore/hunt discriminators are necessary, not full-contract proof; each roadmap needs held-out cooperation, danger, exploration and progression acceptance. | E16 |
| 138 | **Proposed audit.** Exact 1–140 ledger coverage, linked reports, independent observer validation, source/history/artefact cross-check and held-out tests distinguish coverage from confidence. | This ledger; E01/E16 |
| 139 | **Observed limit.** Desk research/source/old captures cannot reveal uncaptured world state, enemy futures, player intent, old exact configuration, causal performance, or live feel/reliability. | [Measurement limits](<Recorded Episodes and Measurement Limits.md#gods-eye-observation-is-broad-but-remains-bounded>); E16 |
| 140 | **Proposed first experiment.** Run E01 first: validate the observer on known traces before ranking architectures; expected result is correctly classified provenance, while failure means repair measurement and fresh capture before E02. | E01 |

## Evidence gaps that constrain every proposal

The four largest gaps are cross-cutting. First, the captured corpus establishes episodes but not a controlled build comparison: old recordings lack an exact source/package/configuration/mod/capability manifest. Second, the observer cannot reconstruct uncaptured terrain, enemy scripts or future world state; retrospective facts must not be attributed to the companion. Third, one expensive decision tick establishes neither a causal hotspot nor observer overhead, so performance claims require inclusive/exclusive timing and recording-enabled baselines. Fourth, source inspection and fixtures cannot establish live helpfulness, broad cave reliability or mod compatibility; only an authorised, recorded held-out play acceptance can do that.

The case-study floor is also explicit. RimWorld's base game is closed source, public mod/version evidence has limits around While You’re Up, and maintainer reports are not prevalence measurements. Those cases can generate mechanisms and failure questions; they cannot decide the AICompanion architecture. The three proposals remain drafts until their distinguishing experiments produce evidence at the outcome and contract level described above.
