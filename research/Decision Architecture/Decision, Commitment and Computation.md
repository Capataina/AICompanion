# Decision, commitment and computation in an opportunistic companion

## Questions this report deepens

The existing utility comparison establishes a fair starting position: utility, hierarchical execution, symbolic planning and learning are not whole-brain substitutes. This report deepens the open questions around activity continuity, concurrent hands and feet, future capability growth, bounded planning and deciding which candidate deserves more computation. It does not rank complete architectures or recommend a replacement.

The tempting answer is that a companion needs one stronger decision system so it can commit coherently. The evidence does not support that. Commitment is represented by BDI intentions, options, running BT nodes, HFSM states and planner execution, but those mechanisms answer different questions. A companion can use utility to compare currently worthwhile activities, an intention or option to retain the chosen activity's state, a planner only for alternatives with real consequences, and an explicit resource contract to let shooting coexist with travel. Combining those can be simpler than appointing one mechanism owner of every tick.

```text
What is worth doing now?          → preference / policy selection
What remains of this activity?    → job or intention lifecycle
Which coherent controls continue? → option, local state machine or plan execution
Which body/hand may write now?    → resource ownership and arbitration
What extra evidence is worth time?→ metareasoning / value of computation
```

The distinctions matter because the player may change direction, a nearby tree may be completed by someone else, an approach may become impossible, a new movement ability may appear, and a hostile may demand a temporary body-control handoff. “Resume the old work” has no correct answer until the policy compares *remaining benefit* with the current context and validates that the old route/tool conditions still hold.

## Claims, scope and counterclaims

| Mechanism | What its primary source actually defines or demonstrates | What it can add here | What it cannot decide by itself | Confidence |
|---|---|---|---|---|
| Utility / contextual scoring | Mark and Dill present response curves, utility combination and weighted selection as ways to avoid abrupt binary decisions.[^mark-dill] | Compare nearby, changing opportunities using explicit factors. | Whether an activity should persist, whether a route is physically valid, or which body writer owns a tick. | High for mechanism; no external controlled comparison here. |
| HFSM / behaviour hierarchy | Isla’s Halo 2 production account uses a behaviour DAG and both parent custom decisions and child relevancy; it explicitly retains custom decision routines when the general child competition is inadequate.[^halo] | Express stable phase/lifecycle and hard, inspectable policy. | Smooth trade-offs, concurrent resource arbitration or physical proof. | High as production account; transfer is an inference. |
| BDI | Rao and Georgeff distinguish beliefs, potentially conflicting desires and intentions; intention commits practical reasoning to a selected course while beliefs can be fallible.[^bdi] | Give a retained activity an identity, assumptions, means and reconsideration reason. | Make commitment desirable in every context; infer correct beliefs or protect a route from changing terrain. | High for theory; no Terraria evaluation. |
| Behaviour tree | BTs compose running, success and failure tasks; concurrency research shows parallel composition has interference/resource problems that require explicit treatment.[^bt-concurrency] | Render prioritised conditions and execution cancellation visibly. | Make two actions independent merely because two subtrees run. | High. |
| GOAP / HTN | Symbolic planning searches action alternatives; HTN decomposes through authored methods. The game HTN study used offline planning to avoid scarce runtime resources.[^htn-games] | Choose among truly alternative consequential workflows while retaining authored boundaries. | Turn uncertain physical predicates into truth, or justify planning every pickup. | High. |
| Options / SMDP | An option has an initiation set, internal policy and termination; options can be interrupted and learned from fragments of execution.[^options] | State an interruptible movement/tool manoeuvre without making it a whole-agent mode. | Supply an initiation/termination predicate, reward, or shared-control rules. | High. |
| RL / imitation | The Neverwinter Nights study learned companion action preferences around traps; interactive imitation research reports a human-in-loop method on small demonstrations.[^nwn][^interactive] | Learn one bounded value/preference or style decision if an evaluation contract supports it. | Generalise to new physical states, represent safety, or establish a reward from a vague desired feeling. | High for reported study scope; low for direct transfer. |
| Receding-horizon / metareasoning | Bounded reasoning treats a computation as an action whose value comes from its effect on external choice and elapsed time.[^metareasoning] | Schedule costly route/shot/candidate reasoning where it could change a decision. | Reliably estimate that value without data or make an uncertain computation free. | High for framing; empirical estimator is an open task. |

## The composition that the literature permits

The appropriate comparison unit is a **continuing, interruptible activity**: an identity; a target or target class; remaining work; entry assumptions; progress evidence; a body/hand request; and terminal reasons. This is not a new framework recommendation. It is a common vocabulary that lets otherwise different mechanisms be evaluated fairly.

```text
selection policy ── chooses / continues / abandons ──► activity record
                                                        │
activity record ── requests a coherent local option ───┼──► feet / tool / aim owners
                                                        │
planner or route query ── supplies alternatives ───────┘

reflex/survival ── may pre-empt an owner, preserving an explicit interruption reason
```

An option is particularly useful for the middle row. Sutton, Precup and Singh define it with an initiation set, policy and termination condition; primitive actions are simply one-step options.[^options] Thus an approach, run-up, jump, tool-swing period or local escape can be represented as an extended action without requiring selection to stop every tick. The selection layer may keep checking whether the activity is worthwhile, while an execution contract decides whether switching at this instant is safe.

The apparent tension between active work and independent shooting is therefore a resource issue rather than a behaviour-gate issue. A work activity can claim a tool hand only during an actual strike/coherent use period; approach can leave aiming available; held light can yield to a weapon; movement can belong to travel or a reflex. A parallel BT would need the same declarations, and a planner with two actions would need the same mutual-exclusion facts. “Parallel” changes syntax, not the underlying ownership problem. The BT concurrency study makes this concrete: uncoordinated parallel behaviours suffer the same resource/interference failures as concurrent programs.[^bt-concurrency]

## Utility remains a candidate, with a narrower responsibility

Mark and Dill’s GDC material argues that response curves and utility combination let an author express gradual preference where `if/then` thresholds produce abrupt switches.[^mark-dill] That supports utility for questions such as whether one remaining tree, an ore vein, a nearby drop or staying close to the player is currently worth the opportunity cost. It does not support treating a numeric score as an admission proof, a route result, or a control writer.

The Halo 2 countercase is useful because it is not “utility failed.” Isla describes a hierarchy with many behaviours where fixed priorities were sometimes inadequate, child relevancy was useful, and custom parent decision routines remained necessary; he uses explicitly listed impulses to accommodate context-sensitive priority changes.[^halo] The production lesson is authoring and debugging: stable hard cases can be legible as hierarchy/tag/impulse policy, while arbitrary floating preferences can become difficult to audit. It is not controlled evidence that a hierarchy improves an opportunistic Terraria companion.

The fair hybrid is therefore not a tree pasted over utility. It is a responsibility split:

| Question | Candidate owner | Concrete observable contract |
|---|---|---|
| Is optional work still worthwhile? | Utility or other contextual policy | Record factors, winner/continuation reason and the activity’s remaining benefit. |
| Is a protected edit permitted? | Hard external policy | One permission result with source/region evidence; no score can override it. |
| Is a manoeuvre already in flight safe to interrupt? | Local option/HFSM/execution owner | Current phase, next safe cancellation boundary and interruption reason. |
| May shooting run during approach? | Resource owner | Aim and tool requests with compatible/exclusive declaration and actual control source. |
| Is an ore route a viable plan? | Navigation/physical contract | `proven`, `unknown`, or `disproven-now`, separate from desirability. |

This arrangement lets a new mastery ability alter the right surface. It adds or changes the movement/action capabilities exposed to routing and local option generation; it changes opportunity costs and possibly the available activity set; it does not force a global hierarchy rewrite. A wider ability set is evidence for more deliberate action representation only when it creates meaningful alternative workflows that local activities cannot express.

## BDI explains commitment without demanding stubbornness

Rao and Georgeff’s BDI model separates beliefs about the world, desires that may conflict and intentions that commit an agent to practical reasoning.[^bdi] It is directly relevant to the distinction between “the companion remembers the ore” and “the companion must resume mining.” The ore job can be an intention-like record: it stores a target, a plan/means, assumptions and progress; it can be dropped when the context makes its remaining work no longer worth it.

BDI is not a single resumption rule. Its reconsideration question must be authored. In this project, that question should include at least:

```text
still valid target?             has the ore/tree/pot changed or gone?
still feasible?                 do route, tool reach and resources remain valid?
remaining benefit?              what is left, not what was already invested?
contextual opportunity cost?    player position, danger, active help and alternatives
safe cancellation boundary?     can current controls stop now without making the body worse?
```

This is where generic contextual remaining work matters. A nearly finished local job may be retained because little work remains and completing it frees the companion, whereas a partially prepared distant job may be abandoned because the player moved, the return proof changed or a more valuable local activity appeared. Neither decision should be justified by sunk preparation alone. Conversely, “do not resume” is also a poor rule: a brief reflex interruption does not erase an activity if its target, feasibility, remaining benefit and context still support it.

The BDI paper’s belief/desire distinction also warns against crisp planner facts. A belief can be inaccurate or invalidated; a desire can be incompatible with another desire. Therefore `reachable`, `safe`, `route-returnable`, `tool-ready` and `enemy-handled` need source, revision/fingerprint and result state. Reifying them as booleans inside an intention or GOAP state makes the same uncertainty invisible rather than resolved.

## BT/HFSMs buy visible execution policy, and inherit hand ownership

HFSMs and BTs are strongest where policy is genuinely structural: downed/revive lifecycle, a route execution phase, a mining strike phase, a recovery sequence or a hard prohibition. Their value is that a reader can trace condition → selected branch → running/failure/success state. Isla’s Halo account describes a behaviour DAG and design-time tagging to filter irrelevant behaviours before expensive relevancy checks.[^halo] This suggests an experiment, not a migration: measure whether the current hard decisions and cancellation boundaries fit a small explicit hierarchy more clearly than they fit score factors/incumbent modifiers.

Behaviour-tree form does not solve two hard cases. First, abort semantics: a reactive selector can re-evaluate a condition every tick, whereas a memoryful sequence can retain progress. The architecture must state which activities preserve progress and which are restarted. Second, parallel execution: movement + shooting, torch holding + aiming, and tool use + recovery can conflict even if they occupy different leaves. Colledanchise and Natale report that designers rarely use generic BT parallel composition where conflicts cannot be ruled out and introduce resource/progress mechanisms specifically to make it predictable.[^bt-concurrency]

An observable falsification test for a BT/HFSM addition is not “the tree is readable.” It is whether the activity trace can show, per tick, (1) the running node/phase, (2) the exact condition or resource request that selected it, (3) a halt reason, (4) what progress persisted, and (5) which feet/tool/aim writer actually won. If the same shared-controller ambiguity remains, a tree has relocated it rather than removed it.

## Plan only when a future consequence changes the present choice

GOAP and HTN are compelling when several legal ways can reach a goal and the choice depends on downstream consequences. HTN adds authored decomposition, which makes permitted methods visible but charges every method to authoring and maintenance. Kelly, Botea and Koenig’s game study chose offline HTN planning because runtime CPU/memory were scarce, then represented plans as scripts.[^htn-games] That result supports careful scoping: a mod need not run a general planner every tick merely because planning exists in the literature.

The discriminating question is not whether an activity has more than one step. Every pickup has `approach → contact`, and every ore job has `approach → swing`. A consequential plan exists only when the first step is locally worse or indifferent but unlocks different valuable later outcomes, or when failure of a prerequisite causes a choice among several methods. Examples that would justify a planning comparison must stay inside the closed ability kit: selecting a safe sequence to assist in a multi-opportunity area, choosing an ability-dependent route method, or coordinating a limited consumable/resource with a later need. “Walk to a nearby drop” does not meet that threshold.

HTN has a different possible role: preserve authored boundaries as future capabilities add. One task such as `help nearby` may decompose by legal methods determined by current ability, but each method must name preconditions, expected effects and invalidation. The hierarchy does not remove physical uncertainty; it delegates route/tool predicates to the same live contracts required by utility. If each high-level task has one obvious method, an HTN is an expensive spelling of a state machine.

| Evidence from matched scenarios | Interpretation |
|---|---|
| Reactive/current activity selection repeatedly makes a locally attractive move that prevents a later outcome the owner prefers, and the missed outcome needs an explicit enabling step. | Compare a small plan or HTN for that workflow. |
| Desired results arise from nearby opportunities becoming worthwhile after each ordinary completion, without preparation that must be represented. | Do not add a planner for the sequence alone. |
| New mastery abilities provide multiple legal ways to reach the same assistance result, with different resource/risk/return consequences. | Model alternative methods/capabilities before deciding between GOAP and HTN. |
| A plan only works by declaring uncertain `reachable`/`safe` predicates true. | Repair the information/physical contract first; planning comparison is premature. |

## Options make temporal abstraction explicit without learning it

The Options framework provides a precise middle ground. An option is defined by its initiation set, internal policy and termination condition; options can be selected alongside primitive actions, and the paper shows that execution may be interrupted and that learning can exploit fragments of an option trajectory.[^options] An option can therefore be fully authored: no RL is required.

For this companion, candidate options are short and physical, not broad personality modes:

| Candidate option | Initiation needs | Completion / cancellation boundary | Persistent state | Why it could help |
|---|---|---|---|---|
| `ExecuteValidatedPrefix` | Native entry contract and a retained route prefix. | Arrival, validation failure, explicit reflex pre-emption or safe stop. | Route identity, step index, entry assumptions. | Prevents a valid short control sequence being restarted every chooser tick. |
| `ApproachWorksite` | Target valid; route status proven or qualified safe prefix. | Tool reach, route failure, target invalidation or policy abandonment. | Target identity, route result, current approach phase. | Allows shooting during approach without calling approach “active mining.” |
| `PerformToolUse` | Range, swing geometry, permission and reserved tool hand. | Completion, target change, forced safety stop or declared interrupt point. | Remaining work and use phase. | Gives pickaxe/axe a coherent period without claiming the whole body. |
| `RecoverToStableState` | Reflex/survival evidence and an available local control proof. | Stable landing/clearance or failure classification. | Hazard cause, resource envelope, intended safe terminal region. | Prevents urgent local controls from becoming an unexplained activity change. |

This is not a proposal to add an options subsystem. It is a testable contract shape. If current retained actions already expose initiation, terminal result, progress and cancellation cleanly, a new name buys nothing. If recurring bugs come from selector ticks reinitialising a manoeuvre or from an activity label concealing a control handoff, this shape identifies a missing boundary.

## Learning is credible only with a bounded decision and a real evaluation contract

The direct companion study is narrowly encouraging. Sharifi, Zhao and Szafron trained action preferences for a Neverwinter Nights companion and evaluated behaviour around traps; the authors report rapid learning/adaptation.[^nwn] The primary source does **not** establish navigation, multi-resource concurrent control, terrain change, safety guarantees or style fidelity in Terraria. Its evidence is a use case for a bounded preference learner, not for handing the companion over to a general policy.

Interactive imitation learning offers a related but distinct proposition. Borovikov et al. train in the target environment with a human in the loop and report that interactivity improved on ordinary behaviour cloning with modest human effort, using small examples and an FPS outline.[^interactive] It matters only if the owner can label a well-defined decision from the companion’s observations—not merely replay player inputs. Player data can be invalid because the player has different abilities, knows terrain the companion has not observed, and may pursue objectives the companion should not imitate.

The least speculative learning targets are estimates that already have an observable terminal event:

| Learning target | Input and output | Ground truth | Held-out disqualifier |
|---|---|---|---|
| Local transition success estimate | Current native-meaningful state class and requested primitive → calibrated success estimate. | Native execution outcome, grouped by terrain/capability. | Better average score while admitting an unsafe/unexecutable high-cost class. |
| Preference residual | A compact candidate/activity representation → correction to an authored baseline score. | Owner-labelled pairwise preference in matched situations. | Learner changes a hard boundary or fails when an observation/capability is missing. |
| Computation priority | Query type/current uncertainty/cost → run/defer order. | Whether extra query work changed the eventual admissible choice or prevented a failure. | It spends work on candidates that cannot affect the selected action, or starves urgent physical checks. |

These remain research experiments. They require a fixed observation schema, train/held-out split by terrain/capability/scenario, baselines, and an explicit intervention policy when confidence is insufficient. A probability-like output without calibration evidence should be called a score, not a probability. A learner should never turn `unknown` route or return status into `proven` without the physical authority that establishes it.

## Receding horizon and value of computation choose how much to think, not what the product wants

Receding-horizon reasoning can compare a short future of controls or activities, execute a first action, then re-observe. It is attractive for local avoidance, positioning, route-prefix selection and a constrained sequence of meaningful opportunities. Its horizon is a modelling decision: without a terminal/invariant condition, it can prefer temporary progress that strands the companion after the horizon. It therefore combines with, rather than replaces, the navigation safe-prefix/return contracts.

Russell and Wefald give the relevant economic distinction: computation is an action whose net utility comes from whether it changes external action, against the time cost and uncertainty of the computation.[^metareasoning] Applied here, a costly route proof, shot solve, target candidate expansion or future-sequence simulation should be scheduled when its possible result could alter the admitted action. It is not a demand for a probabilistically exact global scheduler; that would itself consume computation and needs outcome statistics the project does not yet claim.

An authored bounded policy can approximate this principle transparently:

```text
Run expensive physical/candidate query when:
  1. a candidate is otherwise eligible and could beat the incumbent;
  2. the answer changes admission, resource ownership or a hard safety boundary; and
  3. its result has not already been invalidated by target, terrain, capability or body revision.

Defer when its result cannot change the current action.
Stop when budget expires, record `unknown` and its stop cause—never impossibility.
```

The separating instrument is a query-effect trace: query type, estimated/actual cost, candidate/decision it could affect, result, revision identity, and whether the eventual external choice differed. A metareasoning rule that makes no decisions different is overhead; one that spends less but removes a valid candidate is a regression. This evaluates computation policy without pretending that every opportunity needs a plan.

## Conditional experiments, not an architecture verdict

| Experiment | Alternatives fairly compared | Fixed evidence and pass criterion | Falsifies |
|---|---|---|---|
| Contextual continuation matrix | Stateless re-score, incumbent modifier, explicit intention/activity record. | Matched cases varying remaining work, player movement, target invalidation, route/return revision, and reflex interruption. Trace continuation/abandon reason and terminal outcome. Pass: no unconditional resume, no loss of a still-valuable safe job after a transient reflex. | An intention abstraction that cannot state a better reason than current retained job state. |
| Control-resource trace | Current ownership, BT parallel branch, options/local-HFSM requests. | Same approach/shoot/tool/light/survival cases; every tick records feet/tool/aim writer and compatible/exclusive resource claims. Pass: no conflicting writer and no label used as proof of writer. | “Parallel BT” or whole-behaviour gates solve hands/feet coexistence. |
| Consequential-workflow probe | Utility/activity continuation versus one bounded GOAP/HTN/sequence model. | A scenario where an enabling step has a measured later consequence; compare against matched cases where pickups are independent. Pass: planning improves only the consequential case without adding latency/complexity to ordinary pickup. | Planning is needed because any task has multiple steps. |
| Capability-growth regression | Existing activities plus capability query versus hierarchy/planner method additions. | Same scenarios under capability sets that alter movement/tool choices; check every affected feasibility, opportunity and route contract. Pass: a capability revision invalidates/re-evaluates the right assumptions and no stale action survives. | Future capability scaling alone proves a whole-brain planner/hierarchy is needed. |
| Bounded-learning trial | Authored baseline versus a learned residual/transition estimator. | Predeclared train/held-out terrain and capability partitions; report calibration, decision outcome, physical failures, reproducibility and author intervention. Pass: held-out improvement without weakening hard boundaries or native validation. | One successful training run proves learned control is generally better. |
| Query-value audit | Fixed eager calculation, fixed budget, result-sensitive scheduling. | Record cost, result, whether it changed admissibility/choice, and all candidates starved. Pass: less work or better outcome without hidden loss of viable high-value candidates. | More query work is automatically better reasoning. |

## Source ledger and gaps

Technical claims use primary papers, author presentations or released source/publication records. Accessed 12 September 2026. Quotations are avoided except for short source terminology; the report otherwise paraphrases. Existing utility-report sources are baseline context, not re-researched wholesale here.

[^mark-dill]: Dave Mark and Kevin Dill, “Improving AI Decision Modeling Through Utility Theory,” GDC AI Summit, 2010. [Session record](https://www.gdcvault.com/play/1012410/Improving-AI-Decision-Modeling-Through); [author slides](https://media.gdcvault.com/gdc10/slides/MarkDill_ImprovingAIUtilityTheory.pdf). The presentation explains utility response curves, distributions and weighted selection; it is production guidance, not a controlled comparative study.

[^halo]: Damian Isla, “Handling Complexity in the Halo 2 AI,” GDC proceeding, 11 March 2005. [Author proceeding](https://www.gamedeveloper.com/programming/gdc-2005-proceeding-handling-complexity-in-the-i-halo-2-i-ai). Relevant sections: behaviour DAG, custom/child decision routines, impulses and behaviour tagging. This is a production retrospective, not a controlled utility-versus-HFSM test.

[^bdi]: Anand S. Rao and Michael P. Georgeff, “BDI Agents: From Theory to Practice,” *Proceedings of the First International Conference on Multiagent Systems*, 1995. [Official AAAI PDF](https://cdn.aaai.org/ICMAS/1995/ICMAS95-042.pdf). The original retrieval used an accessible reproduction after a university endpoint timed out; independent review located the official copy. Used for the belief/desire/intention model, not a claim of game-performance superiority.

[^bt-concurrency]: Michele Colledanchise and Lorenzo Natale, “Handling Concurrency in Behavior Trees,” arXiv:2110.11813, submitted 22 October 2021; later IEEE *Transactions on Robotics* work. [Primary preprint record](https://arxiv.org/abs/2110.11813). The authors report simulation and real-robot validation of resource/progress-aware concurrency; neither is a companion-game study.

[^htn-games]: John-Paul Kelly, Adi Botea and Sven Koenig, “Offline Planning with Hierarchical Task Networks in Video Games,” AIIDE 2008. [Paper PDF](https://www.eecs.ucf.edu/~gitars/cap6671/Papers/AIIDE08-010.pdf). This paper studies offline script generation because runtime resources are scarce; it does not establish that online companion work should be planned offline.

[^options]: Richard S. Sutton, Doina Precup and Satinder Singh, “Between MDPs and Semi-MDPs: A Framework for Temporal Abstraction in Reinforcement Learning,” *Artificial Intelligence* 112(1–2), August 1999, pp. 181–211. [Author-hosted PDF](https://people.cs.umass.edu/~barto/courses/cs687/Sutton-Precup-Singh-AIJ99.pdf); [publisher record](https://www.sciencedirect.com/science/article/pii/S0004370299000521). Relevant definition and interruption/intra-option results; options do not imply learning or a particular game policy.

[^nwn]: AmirAli Sharifi, Richard Zhao and Duane Szafron, “Learning Companion Behaviors Using Reinforcement Learning in Games,” AIIDE 2010, pp. 69–75, published 10 October 2010. [Primary proceedings page](https://ojs.aaai.org/index.php/AIIDE/article/view/12392), DOI [10.1609/aiide.v6i1.12392](https://doi.org/10.1609/aiide.v6i1.12392). The PDF endpoint returned 403 on 12 September 2026, so claims are restricted to the primary page’s abstract: Neverwinter Nights, action preferences and trap-behaviour experiments.

[^interactive]: Igor Borovikov, Jesse Harder, Michael Sadovsky and Ahmad Beirami, “Towards Interactive Training of Non-Player Characters in Video Games,” arXiv:1906.00535, 3 June 2019. [Primary preprint record](https://arxiv.org/abs/1906.00535). It reports an interactive imitation method and small target-environment examples; it is not a demonstrated Terraria companion controller.

[^metareasoning]: Stuart Russell and Eric Wefald, “Principles of Metareasoning,” *Artificial Intelligence* 49(1–3), May 1991, pp. 361–395, DOI [10.1016/0004-3702(91)90015-C](https://doi.org/10.1016/0004-3702(91)90015-C). [Publisher abstract](https://www.sciencedirect.com/science/article/pii/000437029190015C). The supplied CMU PDF URL rendered as one page, so the source supports only the publisher abstract’s framework of computation actions, time cost and external-action effect.

### Gaps stated precisely

* No retrieved primary study compares utility, BDI, BT/HFSM, GOAP/HTN, options and learned values on an opportunistic 2D platformer companion with changing terrain, native collision, simultaneous tool/aiming, player-relative objectives and a closed ability kit. A global “most suitable” verdict would be fabricated.
* No community/practitioner corpus claim is used. This lane searched technical primary sources; maintainer/user-experience scores and dates are unavailable rather than inferred.
* The current code/telemetry and historical causal diagnosis belong to other research lanes. This report offers conditions and instruments for testing alternatives against those facts; it does not assert that any current defect is caused by the decision architecture.
* The cited learning work does not establish calibrated confidence, safe exploration, or reproducible behaviour under future abilities. Those require the predeclared evaluation contracts above.
