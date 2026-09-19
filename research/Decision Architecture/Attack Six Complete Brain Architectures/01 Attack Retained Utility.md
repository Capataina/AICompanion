# Architecture attack A — retained reactive utility with concrete continuation and local ordering

**Reviewed surface:** approach A only: retain reactive utility, bind the incumbent to a concrete continuation with causal ownership and real progress, compare local orders, and do not add a general long-consequence search. The checkout was `b3a4ad94f1f9ea503fca829e45a1ce63c12eb852` (build 0.30.6) when inspected. `research/Historical Evidence/Archived Brain Proposals/04 Plan a Course of Action and Follow It.md` was already modified and ten ledger files were untracked; none was changed or treated as evidence for this review. No production file, Slate record, memory, build, game process or visible UI was touched.

## The unasked question

**What does “finish useful work” mean when useful work keeps arriving?** There are two materially different contracts:

1. **Throughput:** the companion keeps producing useful effects even if an older job can starve.
2. **Conditional liveness:** once a concrete job has begun and remains valid, safe and worthwhile, it eventually completes unless a named world change makes abandoning it better.

Approach A can provide throughput under repeated arrivals. It cannot guarantee conditional liveness from real progress plus relative future value alone. A constructive two-job counterexample below keeps completing new one-tick jobs forever while a partly completed two-tick job never advances. A completion guarantee requires an additional assumption (arrivals eventually stop or are bounded), or an additional mechanism (fairness/age, non-pre-emptible semantic membership, a deadline, or a retained schedule). Each mechanism changes the policy; none follows from observing progress.

That distinction matters because README Expected Behaviour asks for both adaptation and conditional liveness: an almost-finished valid job should finish (`README.md:123-139`), equal jobs should not swap (`README.md:125`), but an irrelevant surface fight should be dropped when the player descends (`README.md:65`) and boss conditions should interrupt ordinary work immediately (`README.md:223-237`).

## Discovery map: requirements, evidence, history and data flow

### Requirements derived from the product rather than the current chooser

The narrative establishes the scenes, but `README.md:647-665` says the **Behaviour By Behaviour / Expected** column is the specification. The controlling rows for this attack are therefore: **Boss and event behaviour** (`README.md:674`), **Reading where the player is going** (`README.md:675`), **Enemy selection** (`README.md:676`), **Chaining several jobs into one trip** (`README.md:677`), **Playing several steps ahead** (`README.md:678`), **Knowing what it cannot do** (`README.md:680`), **Committing to a decision** (`README.md:681`), **Opportunistic mining** (`README.md:690`), **Looting** (`README.md:694`), **Weapon selection** (`README.md:695`), and **Using handed gear** (`README.md:703`). The story examples below are corroborating examples, not a substitute specification.

| Requirement | Product source | Disqualifier |
|---|---|---|
| Choose concrete enemies, drops, torch sites, veins and weapon uses, rather than category labels | `README.md:43-49`, `119-129`, `151-165` | One activity-level candidate hides several distinct sites or bodies. |
| Finish valid useful work without flip-flopping, while abandoning it when its remaining outcome no longer warrants completion | `README.md:101-115`, `123-139`, `149`, `183`, `255-259` | A valid incumbent can starve under repeated slightly better arrivals, or a stale incumbent cannot be dropped. |
| Order mixed work as a stretch, including incidental work on an already-paid route | `README.md:119-137`, `157-165`, `211` | The architecture sees only isolated jobs from the current pose or recomputes and discards the tail. |
| Credit enabling actions whose payoff appears in a later state | `README.md:43-49`, `157-161`, `243-247` | An action that is locally worse but creates a superior later shot is never generated or valued. |
| Follow changing player intent and drop obsolete outside work | `README.md:57-69`, `65`, `129`, `163`, `203-207` | Retention is unconditional or player intent reaches value but not continuation validity. |
| Act while refinement is incomplete and under bounded computation | `README.md:47`, `115`, `153-165`, `199`; attack brief | A cut before the first expensive candidate leaves no valid executable action, or planning routinely exceeds the tick budget. |
| Keep 2x2 passages legal while preferring clear air | `README.md:83`; shared movement contract | Choice logic invents a body feasibility rule or turns preference into prohibition. |
| Preserve truthful uncertainty, resource capacity, target identity and causal effects | `README.md:99-115`, `213`; common foundation | Unknown is read as no, full cargo is still offered, replacement inherits old identity, or movement counts as work. |
| Avoid arbitrary category priority and unexplained incumbent percentages | attack brief; `README.md:129`, `161-165` | Combat/lighting/collection order is fixed by category, or a percentage decides the hard scenes. |

### Artefacts expected to prove those requirements

- Product: `README.md:11-259`, read in full through the boundary before Current Behaviour.
- Candidate contract: `Companion/Brain/Activities/CompanionAction.cs:22-28,31-39,41-75,77-103,120-163`.
- Preparation and choice: `Companion/Brain/Infrastructure/Selection/ChooseBehaviour.cs:24-45,75-140,149-211`.
- Shared arithmetic: `Companion/Brain/Infrastructure/Selection/EvaluatePreparedActivities.cs:34-106`.
- Local ordering: `Companion/Brain/Infrastructure/Selection/OrderNearbyTasks.cs:11-33,43-129`.
- Ownership and effect receipts: `Companion/Brain/Infrastructure/Selection/OwnCurrentActivity.cs:42-120`; `ChooseBehaviour.cs:66-73`.
- Query scheduling: `Companion/Brain/Infrastructure/Selection/ScheduleOpportunityQueries.cs:11-18,27-72`.
- Concrete current activity limitations: one action object per activity in `ChooseBehaviour.cs:26-36`; one target per nearby-world-work activity in `PerformNearbyWorldWork.cs:18-20,63-78,242-285`; collection publishes one selected drop-or-pot method in `CollectNearbyItems.cs:55-87`; lighting searches nearest-first and stops on the first proven site in `PerformNearbyWorldWork.cs:338-440`.
- Measured negative: `python3 "research/Evaluation and Observability/Probes/ReproduceDecisionCaptureCounts.py" --root "$PWD"` over capture `2026-09-18_18-57-09-481`.
- History: full bodies of `9b402cb`, `573d9d4`, `6892189`, `7a6b473`, `16a39de`, `457b168`, `145be5a`, `b89abee`, `a0be32f`, `e0c80b8`, and `b3a4ad9`.

### Data chain actually present

```text
Senses / world facts
  -> each CompanionAction.Prepare captures one value, target, duration and eligibility
  -> EvaluatePreparedActivities applies protection, incumbent, horizon, separation, time and fit
  -> OrderNearbyTasks enumerates permutations of close activity-level tasks
  -> each family nominates one child; the parent selects one nomination
  -> target and optional position method are validated
  -> OwnCurrentActivity selects one purpose and opens one attempt
  -> action Execute requests a position / hand use
  -> position + movement + native interaction perform the tick
  -> RecordWork and activity-specific attempt conclusion attribute actual effects
```

This chain is already a strong foundation for A. Its decisive limitation is representation: the prepared board has six entries, not the seven torch sites, three drops, several enemies and weapon moves the product asks it to order. The local order is used only to choose the next action and is discarded immediately (`OrderNearbyTasks.cs:25-31`).

## 1. Requirement matrix

| Criterion | Origin | Expected proof | Evidence gathered | Result | Caveat |
|---|---|---|---|---|---|
| Concrete, valid candidate binding | Handed common foundation + derived | Every selected item/NPC/tile retains generation or immutable binding through activation | `ValidatePreparedActivity.cs:13-38`; `CompanionAction.cs:31-39`; `OwnCurrentActivity.cs:42-80` | **Pass** | Non-entity identity still uses activity equality; same-material replacement without a lifecycle signal is outside the current proof. |
| Causal work and interruption ownership | Handed common foundation | Productive effects only from native receipts during an open attempt; suspension cannot consume work failure budget | `OwnCurrentActivity.cs:86-120`; `ChooseBehaviour.cs:66-73`; commit `a0be32f`; commit `be22f13` | **Pass** | This establishes attribution, not that the right job is chosen. |
| Truthful unknown / disconnected reach | Handed common foundation | Unknown remains unresolved; finished refusal is distinct; optional work has a valid action while waiting | `CompanionAction.cs:77-103`; `PerformNearbyWorldWork.cs:256-279,398-431`; `ScheduleOpportunityQueries.cs:11-18` | **Partial** | The uncertainty distinction is sound. If no incumbent or cheap fallback is valid and the first expensive candidate is cut, A still has no architecture-level useful action guarantee. |
| Player intent invalidates obsolete work | Handed + derived | Same target kept while still useful; descent/turn can zero or invalidate outside work immediately | `ChooseBehaviour.cs:229-267`; `EvaluatePreparedActivities.cs:70-88`; `README.md:65,129,163` | **Pass at mechanism level** | Depends on forecast and intent accuracy; no approach can repair a wrong observation by selection alone. |
| Equal drop vs torch is resolved without category priority | Handed challenge | Compare concrete outcomes and route cost; equality is stable and explainable | Current registration is combat, collect, chop, mine, lighting, company (`ChooseBehaviour.cs:26-36`); exact equal final values break by lower registration index (`NominateFamilyActivities.cs:38-41`) | **Partial** | A semantic tie is harmless if either outcome is genuinely equal, but the current tie key silently prefers collect over lighting. Local order can resolve route difference only if both concrete sites are exposed. |
| Seven torch sites plus three drops can be ordered | Handed challenge + derived | Candidate set includes all ten sites, produces a bounded useful prefix, and eventually revisits every still-valid site | Current board exposes one collection method and one lighting site. Exact `OrderNearbyTasks.Visit` is factorial (`OrderNearbyTasks.cs:86-115`); 10! = 3,628,800 | **Fail** | Keeping the current five-task cap preserves cost by omitting required sites; removing it violates the bounded-compute premise. A heuristic could make this partial, but then gives up any guarantee that its local order is best. |
| Real progress + relative future comparison guarantees completion without a tuning margin | Explicit handed test | Proof under the stated arrival model, or a counterexample-free deterministic harness | Constructive probe: a partly completed two-tick job C loses every rescore to a newly arriving one-tick J by 2.456643 vs 2.443590 and never advances through eight arrivals | **Fail** | Completion follows only under extra assumptions such as a finite stable candidate set and monotonic exact costs. Progress alone does not create fairness. |
| Repeated arrivals cannot starve older work | Handed challenge | Adversarial insertion test completes the old valid job or names a semantic reason to abandon it | Same constructive probe; `ScheduleOpportunityQueries` guarantees preparation rotation, not execution fairness (`ScheduleOpportunityQueries.cs:11-18`) | **Fail** | System throughput remains high: eight J jobs complete. That is not per-job liveness. |
| Tiny fluctuations cannot switch unchanged physical opportunities | Handed challenge | Same identities and physical state with epsilon perturbations produce no owner transition | Concrete identity can freeze validity; raw reactive comparison still selects strict `>` and stable-index equality (`NominateFamilyActivities.cs:38-41`). History `9b402cb` shows a shared lagging stall signal tripled churn | **Partial** | Exact switching cost derived from the retained course can stabilise fixed alternatives. Any recomputed noisy quantity not frozen with identity can still cross without a margin. |
| Player takes step one / irreversible action starts | Handed challenge | Revalidate after effect receipt; never plan as though the effect can roll back | Native effects are recorded causally; prepared targets are revalidated; one interaction concludes complete only after an observed effect (`PerformNearbyWorldWork.cs:287-300,461-500`) | **Pass** | The next order is regenerated; no retained multi-step tail exists to repair. |
| Trivial foe vs nearly finished ore | Handed challenge | Remaining work and actual future harm decide; no combat-first rule | Time term uses remaining `TaskTicks` (`EvaluatePreparedActivities.cs:79-88`); mining/chopping expose remaining native work; README `139,161` | **Pass in expressive fit** | Accurate comparison still depends on combat harm forecast and ore remaining-time calibration. |
| Enemy outside while player descends | Handed challenge | Outside fight loses as future separation grows; threat near player remains | Separation and player fit apply to all jobs (`EvaluatePreparedActivities.cs:70-88`); history `573d9d4`; README `65` | **Pass** | This is one of A's strongest scenes because it is an immediate changed-state comparison. |
| Boss vs ordinary fight, including player death | Handed challenge + Expected table | World encounter suppresses optional work; ordinary dummy does not become urgent by category; a boss fight continues after the player dies | Encounter intensity is applied in shared evaluation (`EvaluatePreparedActivities.cs:47-61`), but `FightEnemies.cs:136-139` refuses all combat as `player-dead`; Expected explicitly requires boss/event continuation (`README.md:674`, story detail `223-237`) | **Partial** | A can express encounter-specific continuation, but the current unconditional player-dead guard violates the boss row. Ordinary combat and boss combat have different requirements; this is a common preparation-contract defect, not evidence against reactive comparison itself. |
| Moving enemy makes opportunity expire | Handed challenge | Identity/generation and plan membership invalidate; incumbent falls back without pretending success | `ValidatePreparedActivity.cs:24-35`; combat commitment validates bodies and urgency (`CommitAttackPlan.cs:150-172,250-285`) | **Pass** | A can react faster than longer planners here. |
| Knockback creates later piercing geometry | Explicit handed test + derived | Current action is credited for state transition that enables later multi-target action | Toy probe: one-step utility chooses direct reward 5; two-step totals are direct 10 vs knock 21, so the enabling knock is invisible locally | **Fail for general A** | Current combat has a bounded specialised multi-segment planner, which can cover this one domain. Repeating that per domain turns A into several planners without a shared consequence contract. |
| Full cargo suppresses collection without hiding other work | Handed challenge | Capacity refusal is candidate-local and another action remains eligible | `CollectNearbyItems.cs:114-145`; classification at `55-87` | **Pass** | Pot-content uncertainty remains conservative by design. |
| Act usefully when no valid plan exists yet or search is cut before candidate one | Handed common foundation/challenge | A cheap valid action or prior incumbent executes while refinement persists | Query scheduler retains incumbent preparation and tri-state cuts; combat has a from-here fallback in current history (`145be5a`) | **Partial** | No general foundation guarantees every family has a cheap baseline. Defaulting to company can be useful but can also abandon a pending consequential action. |
| Preserve 2x2 legal passages while preferring clear air | Handed | Shared geometry remains authority; preference changes cost, not feasibility | The approach does not require changing movement; root/Brain contracts keep one body and one movement boundary | **Pass** | This is inherited, not a benefit of reactive utility. |
| Respect real planning pressure | Handed challenge | Decision work is bounded and an executable action survives a cut; measured on final implementation | Current source declares total 12 ms, family 3 ms, combat 4 ms (`BehaviourWeights.cs:138-146,382`); exact local order is O(m!). Yet held-plan re-evaluation simulates cache misses under `PlanningBudget.Unbounded()` (`ReevaluateAttackPlan.cs:60-64`), and the empty-pool fallback is count-capped at 256 but clock-free (`PlanningBudget.cs:50-56`; `SearchAttackPlans.cs:639-682`) | **Partial** | The unbounded held-plan and fallback paths are common-foundation budget violations that every approach inheriting current combat must close. Separately, hypothetical site-grain A is unmeasured; current 5-task exact order is 120 permutations and ten sites are 3.63 million. |
| Preserve handed gear and tool ownership | Expected table + common foundation | The chosen course uses only the two handed weapons and handed pick/axe, respects the active hand grant, and visibly refuses unsupported gear | Expected `README.md:695,703`; grants and one-purpose ownership flow through `OwnCurrentActivity.cs:42-120`; current combat owns weapon/target selection locally | **Pass as an inherited boundary** | A must consume the arsenal/tool mechanisms as authoritative jobs; a cross-family selector that independently chooses weapon or tool would violate this row. |
| Avoid arbitrary category priorities and incumbent percentages | Handed | No family order, percentage band or registration tie decides a meaningful scene | Current A baseline has `Commitment=1.15`, `TaskOrderShare=.4`, `TaskOrderMaximum=5` (`BehaviourWeights.cs:254,266-285`) | **Fail as currently shaped; achievable only partly** | Semantic validity retention can replace a flat incumbent percentage. Candidate pruning and tie-breaking still need a declared policy; pure local value cannot make all ties meaningful. |

## 2. Attack report

| Angle | What I tried | Outcome | Evidence |
|---|---|---|---|
| Alternative | Compared A against (1) the smallest collect/torch identity retention and (2) event-repaired route/task schedule B | **Landed.** The tiny identity fix is cheaper for the observed swap but does not reach the full product. B is stronger once the candidate set contains several concrete sites with dependencies because it retains and repairs their mixed order instead of recomputing only the first step. | Capture reproduction below; one-target-per-activity source; README `119-137,151-165`. |
| Coverage gap | Traced every claimed capability to the prepared-board representation, then asked where a knockback action's later pierce payoff enters; also inspected whether the current combat exception preserves every successor state | **Landed.** A has no general place for state-changing enabling effects. The current specialised combat planner is an exception and is itself incomplete evidence: `TopBeam` removes a prefix solely from its current `CombatOutcome`, although nodes with the same outcome can carry different remaining life, debuffs, positions and future extensions. | Toy probe `one-step=direct`, `two-step=knock`; `CompanionAction.cs:22-28`; `SearchAttackPlans.cs:194-224` (`BeamNode` contains successor state but `TopBeam` filters only `plan.Outcome`). |
| Real input | Exercised repeated arrivals, ten simultaneous specific sites, full cargo, unknown reach, moving targets, player descent and first-candidate budget cut | **Landed.** Full cargo, moving targets, descent and unknown are handled by the foundation. Repeated arrivals defeat completion; ten sites defeat the current representation/budget; no-plan-yet remains conditional on a fallback. | Probe outputs; source and matrix rows above. |
| Overhead | Counted exact local-order permutations at the site grain and compared them with source budgets | **Landed.** Exact order grows factorially: 5!=120, 7!=5,040, 10!=3,628,800, 12!=479,001,600. The current cap is a runtime guard that becomes a coverage hole at the required grain. | `OrderNearbyTasks.cs:86-115`; factorial command; `BehaviourWeights.cs:138-146,266-272`. |
| Residue | Searched for state or output made obsolete by retaining concrete continuation | **Survived.** I found no dead component forced by A. The full order tail is not executable state, but it contributes to choosing the leader and its text feeds diagnostics; it is recomputation cost, not dead code. | `OrderNearbyTasks.cs:86-129`; `ChooseBehaviour.cs:159-165,224-227`. |
| Smell with a future | Followed the likely way A would cover missed enabling choices: add local sequence logic to combat, lighting, collection and gathering independently | **Landed.** The concrete trigger is the first cross-domain effect: knockback changes a later pierce, a kill produces a drop on a torch route, or a pot produces unknown loot. Separate local planners must duplicate effect, resource and invalidation semantics or disagree. | Current combat commitment already owns a local sequence (`CommitAttackPlan.cs`); other activities expose one target. |

## 3. Overall classification

**PARTIAL — viable as the low-cost reactive foundation and as a bounded policy for mostly independent opportunities; insufficient as the complete decision architecture promised by Expected Behaviour.**

Approach A survives the scenes whose answer is “re-evaluate the current world accurately”: player descent, moving targets, full cargo, unknown reach, nearly finished work and irreversible effects. It can express boss context, but the current unconditional `player-dead` combat refusal contradicts the distinct boss requirement. It fails two gates that are intrinsic to “no general consequence search”: it cannot value an enabling action whose benefit exists only in a successor state, and it cannot guarantee completion under unbounded repeated arrivals merely from progress and relative future comparison. Its exact local ordering also cannot scale from six activity nominations to the required specific-site grain within the stated budget.

The classification would rise to **Pass for a narrower contract** if the product accepted all three of these bounds: candidates are finite and eventually stop arriving; each candidate's effects are independent except inside activity-specific planners; and only a bounded heuristic prefix, not the best mixed order, is required. README's jungle, on-route work and repeated-arrival scenes intentionally exceed those bounds.

## 4. Smallest condition that closes each failed gate

| Failed gate | Single smallest condition that must become true |
|---|---|
| Completion under repeated arrivals | The architecture states and enforces a liveness condition under which a still-valid incumbent must eventually receive execution time; the condition may be an arrival bound, semantic non-pre-emption, fairness/age, or a retained repaired schedule, but it must be observable and falsifiable. |
| Enabling actions | At least the domains that can change later opportunity geometry expose state transitions and value successor outcomes far enough to distinguish the direct-5 / knock-then-pierce-21 counterexample. |
| Ten specific sites under budget | The selector exposes site-grain candidates and produces a useful bounded prefix without factorial enumeration or an arbitrary truncation that can permanently hide a still-valid site. |
| Arbitrary constants | Continuation and pruning are derived from concrete remaining effects, switching consequences and declared resource limits, with no percentage or registration-order decision controlling a materially different outcome. |
| Useful action before first expensive candidate | Every refinement process has a cheap, valid executable incumbent or baseline whose semantics are part of the activity, not a default category chosen because search returned nothing. |
| Bounded shared execution | Every preparation and fallback path inherited by A respects the enclosing wall-clock allowance or returns a previously proved executable action without new unbounded work. |
| Boss continuation after player death | Player death invalidates ordinary player-protection assumptions but does not invalidate combat while a recognised boss or event remains active. |

## 5. History found and what it implies

The relevant history is unusually strong because this surface has failed repeatedly:

- `9b402cb` records the sharpest warning against generic progress proxies. Conditioning commitment on body-level `MovementStalled` tripled churn: 55.5 to 22.3 ticks held per decision; 1,197 of 1,727 switches followed the stalled flag although it appeared on 4.5% of ticks. The implication for A is exact: continuation evidence must be owned by the concrete task and causal to its outcome.
- `573d9d4` added the current time term and exhaustive local ordering after the orb flew past a slime to a torch. It explicitly recomputes and discards the order. Its fixed two-job fixture passed, but the later live capture reproduced a class outside that fixture.
- `6892189`, `7a6b473` and `16a39de` each fixed continuity below the chooser—retained destination membership, committed movement landing, and company-method arrival. They show that semantic membership works better than a score sticker when the object being retained has a clear validity contract.
- `b89abee` and `a0be32f` established one-purpose ownership, suspension and causal attempt outcomes. Those are foundation strengths A should keep.
- `457b168` and `145be5a` gave combat a retained plan, a real 4 ms budget and a cheap from-here fallback after search cut. This proves targeted bounded consequence planning can coexist with reactive utility, but also proves current A is already relying on a domain planner for one of its hardest scenes.
- `e0c80b8` records the 0.30.6 live negative and `b3a4ad9` demotes the resulting Proposal 04 discussion from plan to handoff. The independent raw reader reproduced the negative: 68 collect↔torch transitions in ticks 7773–11125, 700 consecutive pure-window ticks split 359 collect / 341 torch, 20 collection attempts replaced with the drop still present, 23 torch attempts replaced before interaction, 53 attempted torch trips in the swap window, and zero companion torch experience credits.

No relevant revert branch was found in `git branch -a` or the focused log search. The abandoned approaches are preserved in commit bodies rather than branches: stall-conditioned commitment, chooser-level commitment margins, and several follow-method locks. The implication is that a new “progress factor” or wider incumbent percentage would repeat a measured failure class.

## Concrete counterexamples

### A. Progress plus relative future comparison does not imply completion

State:

- C is a valid incumbent with real prior progress and **two ticks remaining**, reward 2.
- At each rescore one fresh job J arrives, takes one tick and yields reward 1.01.
- The local order objective is the current code's shape: reward delivered sooner using `W/(W+finish)`, with `W=10`.
- The comparison includes both jobs and their future completion; this is stronger than comparing immediate values.

Decision:

```text
C then J = 2*10/(10+2) + 1.01*10/(10+3) = 2.443590
J then C = 1.01*10/(10+1) + 2*10/(10+3) = 2.456643
```

J is correctly chosen first. It completes. On the next tick an identical new J appears, so the same comparison repeats. After eight ticks, eight useful new jobs completed and C still has two ticks remaining.

Failure: the system has excellent throughput and no completion guarantee. C's real progress matters only if C executes again; while pre-empted its remaining-work term stays fixed. No arbitrary margin was used in the counterexample.

Minimal condition to avoid it: bound arrivals, age C, reserve execution share, make C semantically non-pre-emptible until a named invalidation, or retain a schedule in which insertion cannot repeatedly move C behind the frontier. Each is additional policy.

### B. Locally sufficient actions miss enabling actions

State:

- Direct shot yields 5 now and leaves targets separate.
- Knock shot yields 1 now and moves the flying target into a line.
- From the lined state, a piercing shot yields 20.

Decision: one-step reactive utility chooses direct (5 > 1). A two-step evaluator chooses knock because its totals are direct=10 and knock=21.

Failure: neither real progress nor concrete current-job identity can reveal value that is absent from the current action and appears only after the action changes the next state's opportunity set.

Minimal condition to avoid it: a bounded successor model in the domain where actions have enabling effects. Current combat already supplies such a local model; A fails only if it claims the reactive layer itself is sufficient or if cross-domain effects remain outside every local model.

### C. Site-grain local order crosses the runtime boundary

State: seven placeable torch sites and three fitting drops, all valid and all close enough to consider.

Decision surface today: one `CollectNearbyItems` entry and one `LightUsefulArea` entry. Raising the prepared board to ten concrete sites gives exact permutation counts:

```text
2! = 2
3! = 6
5! = 120
7! = 5,040
10! = 3,628,800
12! = 479,001,600
```

Failure: the existing exact search either keeps the cap and hides five required sites, or removes it and spends millions of leaves before candidate preparation, geometry or combat simulation. No Terraria timing is claimed from this arithmetic; it only refutes “exact exhaustive local order remains cheap at the required grain.”

Minimal condition to avoid it: use a bounded incremental insertion/repair or other heuristic that returns a valid prefix early and preserves the unpriced remainder as unknown. That is already much closer to approach B's retained schedule than to a stateless local re-ranking.

## Challenge-scene ledger

| Scene | A's likely decision | Failure or survival | Minimal condition |
|---|---|---|---|
| Equal worthwhile drop vs torch | Stable tie or shortest marginal route | **Survives only with site candidates.** Current activity tie silently favours collect by registration. | Expose both concrete sites; tie on a declared stable neutral key after consequences are equal. |
| Seven torch sites and three drops | Exact local permutation or heuristic prefix | **Fails current representation; factorial if exact.** | Incremental bounded prefix plus retained unknown remainder. |
| Player takes step one | Reobserve and re-rank | **Survives.** Reactivity is an advantage. | Effects and target validity update before the next comparison. |
| Irreversible action starts | Receipt changes world; next comparison starts from new state | **Survives.** | Never simulate rollback; native effect receipt is authoritative. |
| Trivial foe vs nearly finished ore | Finish ore if remaining work ends before meaningful harm | **Survives if forecasts are calibrated.** | Compare real remaining work with predicted harm arrival. |
| Enemy outside while player descends | Separation/fit zero the stale fight | **Survives.** | Intent reaches continuation validity and not only raw acquisition. |
| Boss vs ordinary fight | Encounter suppresses optional work; dummy remains ordinary | **Partially survives.** Encounter value exists, but current combat refuses both contexts when the player dies. | Ordinary combat may stop on player death; recognised boss/event combat must continue (`README.md:674`; `FightEnemies.cs:136-139`). |
| Moving enemy expires | Generation/plan validity releases it | **Survives.** | Cheap executable fallback while replanning. |
| Knockback creates piercing geometry | Direct locally stronger shot | **Fails without local successor search.** | Combat-specific bounded state-transition model. |
| Repeated arrivals/inserts | Fresh small job repeatedly leads | **Fails conditional liveness.** | Explicit liveness assumption or policy. |
| Full cargo | Collection candidate refused, other work continues | **Survives.** | Capacity remains candidate-local. |
| Disconnected/unknown reach | Unknown waits; finished absence refuses | **Survives foundation.** | Do not default unknown to impossible or free. |
| Real 4 ms planning pressure | Reuse incumbent; prepare families incrementally | **A is comparatively strong, but expanded ordering is unmeasured.** | Prefix available before deadline and every cut reported unresolved. |
| No valid plan yet | Company or cheap from-here action | **Partial.** Company can be semantically wrong during a fight; a generic fallback is not enough. | Per-domain cheap valid baseline. |
| Search cut before first expensive candidate | Keep incumbent/fallback | **Partial.** No incumbent means no architecture-level guarantee. | Price baseline before expensive candidates or retain last still-valid result. |
| Tiny fluctuations, same physical opportunity | Identity retention can hold; raw score can cross | **Partial.** | Freeze task-owned continuation evidence and include real switching consequence; semantic invalidation, not epsilon score, releases it. |

## Implementation, runtime and gameplay costs

### Implementation cost

The narrow implementation is modest: give collection and lighting semantic continuation identities, freeze task-owned remaining work and switching consequences, and replace the flat 1.15 factor. That reaches the observed 0.30.6 swap.

The full Expected Behaviour implementation is large despite retaining the chooser:

- activities must publish multiple concrete candidates rather than one winner;
- every candidate needs target identity, validity, remaining effects, duration, resource grants, and causal receipts;
- the chooser needs a bounded order-prefix interface rather than `PreparedActivity[6]`;
- incidental actions need to be valued against the route already being executed without becoming category exceptions;
- local enabling planners need a shared effect vocabulary or they will disagree across combat, loot, lighting and work;
- diagnostics must record retained continuation, insertion/replacement reason, unpriced remainder and which action created each later opportunity.

That is less model work than a whole-brain simulator, but not a small extension to the present six-row board.

### Runtime cost

Current reactive scoring is O(k) after bounded preparation, where k is the six activity entries. Family scheduling bounds optional preparation and rotates deferred siblings. This is A's strongest engineering property.

Exact local order is O(m! * m) time and O(m) recursion/state. Current `m<=5` is 120 orders. Required site grain reaches m=10 before enemies, veins or weapon moves, which is 3.63 million orders. A greedy insertion or pairwise comparison can reduce this to roughly O(m^2), but can miss a globally better route and does not value enabling effects without additional modelling.

Measured timings must remain separate from asymptotics. Current source budgets total planning at 12 ms, optional family preparation at 3 ms and combat planning at 4 ms. Historical documentation says an incumbent mining recomputation once consumed 12 ms and nearest-first brought that scene to about 4 ms (`BehaviourWeights.cs:138-146`). No timing was measured for a hypothetical site-grain A, so no performance pass is claimed.

The telemetry schema can repeat a family's last value: the writer calls these columns “retained from the last completed comparison” (`RecordBrainTelemetry.cs:1037-1044`). That possibility does **not** invalidate this capture's timing distribution. In `2026-09-18_18-57-09-481`, all 12,062 rows have `choice_fresh=1` and all 12,062 have distinct `choice_id` values. Therefore `combat_prepare_ms max=40.781 ms` and floor-index p99 `5.495 ms` are fresh per-row family-preparation measurements for this run. The same read gives `brain_ms max=53.09 ms`, p99 `7.46 ms`; `plan_ms` is explicitly current-tick navigation planning and is a different quantity. Future capture analysis must still filter on freshness or deduplicate by choice id rather than infer freshness from the column name.

Two real current paths also escape the nominal clock. A cache miss while re-evaluating a held combat plan creates an unbounded simulation budget before the normal new-plan search (`ReevaluateAttackPlan.cs:60-64`). If the 4 ms search cut with no priced plan, `FromHereFallback` permits 256 simulations with no clock (`PlanningBudget.cs:50-56`; `SearchAttackPlans.cs:639-682`). Both are common-foundation defects: A does not cause them, but it cannot claim a bounded executable prefix while inheriting them.

### Gameplay cost and future failures

- Old valid work can starve while the companion remains visibly busy, which violates “finish the worse C rather than get neither” even though throughput looks healthy.
- Cross-domain enabling moves are either missed or implemented as isolated authored planners. The latter gradually turns “reactive utility” into a collection of incompatible small planning systems.
- On-route work remains hard because its value is conditional on a future path. Treating it as a standalone job overprices the detour; treating lighting specially violates the product.
- A wait-for-perfect-shot failure persists unless combat always supplies a valid from-here prefix. This is solved locally today, but the same shape returns in every expensive family search.
- Tiny forecast changes can still cause semantic churn if continuation evidence is recomputed instead of frozen with identity.
- Fairness additions can make the companion finish obsolete chores; strict membership can make it stubborn. A must state the release contract rather than call either “progress.”

## Strengths that survived the attack

1. **Adaptation is direct.** Player turns, target movement, cargo changes and world edits immediately change the next comparison; no stale long plan has to be repaired first.
2. **The common foundation is already unusually truthful.** Usability, uncertainty, target binding, ownership, suspension and native productive effects are distinct.
3. **It is the cheapest architecture when opportunities are independent and the world is volatile.** A model of long futures would add error where immediate state already answers the question.
4. **It is diagnosable.** Every multiplier can be recorded, and causal attempt outcomes keep “selected,” “executed” and “worked” separate.
5. **It composes with targeted planners.** Current combat demonstrates that a bounded local consequence planner can submit one reactive offer without replacing the whole chooser.

## The value-choice boundary A cannot remove

Geometry and time can rank two courses only after their achieved effects are valued. A shorter route to one drop is comparable with a longer route to the same drop; it does not explain whether one drop is worth one torch, one prevented hit, or two copper tiles. Removing scalar weights does not remove this choice. It moves it into category priority, pruning order, a hidden tie-break, or incumbency.

The least arbitrary boundary available to A is an explicit **outcome vector and partial order**:

1. Hard validity and product constraints remove impossible courses: target identity, reach, grants, cargo, world-edit permission, player intent and boss/event rules.
2. A new course replaces the incumbent whenever it Pareto-dominates it on declared achieved outcomes after including remaining work and switching cost, or when a named context rule from Expected Behaviour applies (for example, recognised boss/event work excludes optional work).
3. When neither course dominates and no Expected rule supplies an exchange, they are honestly incomparable. Stable incumbency may break that tie, but only as a tie policy; it cannot be described as proof that the incumbent is better.

This does not make incumbency absolute: any dominating course, invalidation, or named context transition replaces it. It also does not solve every choice. Ore versus torch can remain incomparable until the product supplies an exchange rate or accepts a neutral tie rule. Any architecture that promises a unique answer there without an explicit preference is hiding its value judgement. A may retain context-sensitive utility for those declared exchanges, but “keep utility” alone is not a recommendation; each cross-effect weight must state which Expected behaviour it represents and which counter-scene would falsify it.

Current combat's vector illustrates both the promise and the limit. `KeepOnlyUndominated` can retain a Pareto front, then `WeighCombatObjectives` chooses within it. Yet `TopBeam` prunes prefixes using only current outcome, even though `BeamNode` also carries remaining life, debuffs, positions and future landings (`SearchAttackPlans.cs:194-224`). Equal present outputs can have unequal continuation sets. For enabling actions, dominance must be evaluated over a sufficient successor-state key or the search can delete the only prefix that creates the later payoff.

## What to borrow and what to reject

Borrow:

- concrete continuation identity and membership validity from mining, combat and retained destinations;
- real remaining work and effect receipts, frozen with the identity rather than inferred from body movement;
- tri-state incremental search and a cheap executable incumbent while refinement continues;
- reactive global re-evaluation for player intent, encounter state, resource capacity and invalidation;
- targeted local successor models only where an action truly changes later opportunity geometry.

Reject:

- any claim that progress alone guarantees finish;
- a flat incumbent bonus, arbitrary percentage band or “new task always wins/never wins” rule;
- one candidate per category as the input to mixed ordering;
- exhaustive permutations at site grain;
- distributed per-activity consequence models without a shared state/effect contract;
- “company wins when search has no answer” as a universal useful prefix.

## Strongest competitor and the condition under which it wins

**Approach B, an event-repaired route and task schedule, is the strongest competitor for the stated product.** It wins when the dominant hard cases are several explicit, mostly deterministic pieces of work with travel, duration, dependencies and resource conflicts: seven torch sites, three drops, pot-before-vein, two copper on the way home, and repeated insertions. In that regime a retained schedule can insert or repair locally and preserve liveness/order evidence without enumerating all permutations on every rescore.

A wins instead when the candidate set is small, consequences are local, arrivals or target motion invalidate plans frequently, and accurate future effects cost more than re-evaluation saves. Combat's knockback/pierce case still needs its local planner under either architecture; B can accept that planner's jobs rather than simulate projectiles itself.

The cheaper alternative for the single observed collect/torch swap is narrower than either architecture: retain those two concrete identities by membership. It should be judged as a defect fix, not presented as reaching the README's mixed-course behaviour.

## External comparison and its limit

The nearest documented industry convention does not prove Terraria behaviour, but it does expose a world-fit expectation: execution systems retain an active task and make completion/abort explicit. Epic's official Behavior Tree task reference describes tasks as running work, supports `In Progress`, and exposes “Ignore Restart Self” so a search can discard a restart of the task already running; its runtime API separately models active tasks and pending/latent aborts. The official overview also describes sequences and condition-driven aborts rather than reselecting the same leaf from scratch every frame: [Epic task reference](https://dev.epicgames.com/documentation/unreal-engine/behavior-tree-node-reference-tasks?application_version=4.27), [Epic Behavior Tree overview](https://dev.epicgames.com/documentation/unreal-engine/behavior-tree-in-unreal-engine---overview), [Epic task abort API](https://dev.epicgames.com/documentation/unreal-engine/API/Runtime/AIModule/BehaviorTree/UBTTaskNode/AbortTask?application_version=5.5).

This supports concrete continuation and explicit invalidation, not Behavior Trees as the answer. It provides no evidence that Unreal's task model produces good Terraria companion choices, no completion theorem under unbounded arrivals, and no comparison of utility A against schedule B in this project.

## First experiments that could falsify approach A

1. **Adversarial arrival liveness.** In a deterministic EngineReplay scene, begin an ore action with exactly two productive ticks remaining. At every rescore inject a new one-tick valid drop or torch whose complete local-order value is epsilon above ore-first. Hold player, danger, reach and geometry constant. Pass A's completion claim only if the ore receives execution and completes within a predeclared bound, or if the architecture explicitly scopes completion to finite arrivals. Record each inserted identity, selected continuation, remaining work and productive effect.
2. **Cross-effect enabling action.** Build a combat audit scene where direct damage is locally higher, but one knockback places a flyer on a two-body pierce line and total prevented harm/damage over two uses is higher. Mutate the successor-effect term off; the row must turn red. This establishes the minimum local planner A needs and distinguishes “reactive chooser” from “combat planner underneath it.”
3. **Mixed site grain under the real deadline.** Seven legal torch sites and three fitting drops, with the nearest choice intentionally producing the longest remaining route. Measure candidate coverage, prefix latency and completed effects under a 4 ms planner allowance, not just total 12 ms brain time. Plant a cap of five; the row must identify the five unpriced candidates as unknown and eventually include them rather than silently pass.
4. **First expensive candidate cut.** Make the first candidate consume the whole budget before producing a price, with no incumbent. Require a named cheap action on that tick and require the expensive candidate to resume rather than restart on the next. A company fallback passes only if company is actually the best safe prefix for that scene.
5. **Epsilon stability.** Hold identities, geometry and physical outcomes fixed while alternating one observation by plus/minus one representable unit across the equality boundary. Require zero ownership changes unless that observation changes a declared continuation validity or switching consequence.
6. **Player-step repair.** Execute the first irreversible step of a mixed course, then move the player so the tail becomes obsolete. Require the effect to stay credited, the old tail to disappear, and the new course to begin from the changed world without replaying step one.

## Commands run and outcomes

All commands ran from the repository root unless stated otherwise.

```text
git rev-parse HEAD
  b3a4ad94f1f9ea503fca829e45a1ce63c12eb852

git status --short
  existing modified proposal 04 and ten existing untracked ledger JSONL files; untouched

python3 "research/Evaluation and Observability/Probes/ReproduceDecisionCaptureCounts.py" --root "$PWD"
  exit 0
  0.30.6: 12,062 rows; combat 5,886 ticks; median combat run 13; no-use-worth-firing 1,304
  actions: company 3,590; combat 5,886; collect 1,347; place-torches 1,239
  swap window: collect->torch 36; torch->collect 32
  pure window 9401..10100: 359 collect, 341 torch
  20 collect attempts replaced with drop still in world; 23 torch attempts replaced before interaction
  53 attempted torch trips in swap window; companion torch credits 0

python3 -c '<completion counterexample>'
  exit 0
  C->J=2.443590
  J->C=2.456643
  lead=J; delta=0.013054
  eight arrivals: C remaining stays 2.0; eight J jobs complete

python3 -c '<enabling-action counterexample>'
  exit 0
  one-step=direct; immediate=5
  two-step_totals={'direct': 10, 'knock': 21}
  two-step=knock

python3 -c '<factorial counts>'
  exit 0
  5!=120; 7!=5,040; 10!=3,628,800; 12!=479,001,600

python3 -c '<TSV timing freshness and quantiles>'
  exit 0
  rows=12,062; choice_fresh=12,062; unique choice_id=12,062
  combat_prepare_ms max=40.781, p99 floor-index=5.495
  brain_ms max=53.09, p99 floor-index=7.46
  plan_ms max=6.55, p99 floor-index=2.09, positive rows=585

git branch -a --no-color
git log --all --oneline --reflog --grep='revert|abandon|torn out|withdrawn' -i -30
  main and experiment/entry-speed-graph found; no relevant abandoned utility-order branch or revert
```

No production mutation test was attempted because the attack brief explicitly prohibited production edits. The two toy probes are refutations of claims, not Terraria performance or gameplay proof.

