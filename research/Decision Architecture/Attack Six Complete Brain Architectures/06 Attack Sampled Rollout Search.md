# Independent attack — F: sampled rollout / Monte Carlo tree search

## Unasked question

**What is the state transition model that a rollout is sampling, and why should the companion trust futures produced by it?**

That is prior to the tree policy. Terraria's relevant future state includes native projectile motion, enemy AI changes, the player's unannounced next move, terrain edits, inventory capacity, hand and feet occupancy, learned weapon uncertainty, tri-state reach/light knowledge, and irreversible world effects. MCTS can concentrate samples within a supplied model; it cannot recover an omitted state variable, invent a missing action, or distinguish model error from outcome variance. In this repository the existing combat simulator is already a concrete counterexample: it records knockback in `SimHit.PushX`, but `EnemyForecast.RolledCopy` explicitly carries life while leaving positions unchanged, so a first shot that shoves an Eye into a later pierce line cannot be discovered by adding more samples (`Companion/Brain/Infrastructure/WeaponKnowledge/Simulation/SimulateUse.cs:269-293`; `Companion/Brain/Infrastructure/Observation/ForecastEnemies.cs:59-87`; `Companion/Brain/Activities/Combat/Planning/SearchAttackPlans.cs:418-445`).

This question lands. F is only an architecture after it identifies a side-effect-free generative model, the state it carries, the actions it can generate, the observation/belief update, the reward/risk contract, and a bounded first answer. Without those, “sample rollouts” names a selection algorithm around the same unresolved causal model and policy choices.

## Requirements and source map

| Requirement | Source of requirement | Evidence surface used for this review |
|---|---|---|
| Behave as a competent, opportunistic friend across company, combat, collection, lighting, mining and chopping | `README.md:11-262` Expected Behaviour | Whole scenes, especially everything-at-once jungle, seven torches, mixed work, boss conduct, changing player intent |
| Compare complete consequences rather than raw task counts or hand-written priority branches | root `CLAUDE.md`, rulings; attack brief | Transition model, reward, terminal value and risk treatment |
| Keep a usable action while bounded planning refines | attack brief shared foundation; current planning history | `PlanningBudget.cs:5-14,45-77`; `SearchAttackPlans.cs:90-150,639-682`; commits `0a2a98e`, `145be5a` |
| Respect grants, resource occupancy, exact cargo, target/effect ownership and stable validity | attack brief shared foundation; Expected Behaviour | Root action definition and state hash/rebase contract |
| Treat unknown as unknown, not as stochastic permission | root/Brain guides; `README.md` unknown/disconnected reach scene | Belief-state and action-admission attack |
| Make continuous stand/aim/timing choices under 4 ms | combat planning source and weights | `BehaviourWeights.cs:367-395`; MCTS continuous-action literature |
| Learn from real effects without learning from imagined rollout effects | weapon-learning guide; shared effect receipts | Learn-versus-act attack |
| Prefer boss survival as a strict policy while ordinary combat remains opportunistic | `README.md` boss and ordinary-fight scenes | Rare-event/risk-neutral reward attack |
| Change course on genuine evidence, without tiny-noise thrash or sunk-cost completion | `README.md` stable course scenes; attack brief | Sample variance, tree reuse, rebase/invalidation attack |
| Establish feasibility from measurements at the actual 4 ms budget | attack brief | Current historical measurements plus proposed falsification experiments; no F prototype exists |

## Requirement matrix

| Criterion | Origin | Expected proof | Evidence gathered | Result | Caveat / smallest condition that changes the result |
|---|---|---|---|---|---|
| A first executable action exists before expensive expansion and remains available on interruption | Handed/shared foundation; derived from current failure | Trace showing time-to-first-usable action and action quality at 0/1/2/4 ms in crowded mixed scenes | Existing narrower combat search can exhaust 4 ms before one stand is priced; `SearchAttackPlans.cs:125-135` needs a separate fallback; `PlanningBudget.FromHereFallback()` is clock-free for up to 256 simulations (`PlanningBudget.cs:50-56`) | **Fail** | Becomes pass only when every decision starts from a valid executable incumbent whose production does not exceed the total budget, and interruption before any new sample returns it |
| The complete action space is represented without enumeration order deciding behavior | Handed; derived from seven-torch/mixed-action scenes | Declared action parameterisation and permutation-invariant coverage at 4 ms | Seven torch sites plus three drops already make ten discrete root jobs; stand, aim and timing are continuous. UCT normally tries each represented action once; large spaces yield shallow lookahead and progressive widening makes the proposal order/domain sampler decisive (Yee et al. 2016) | **Fail** | Requires measured root coverage or a completeness argument for the action proposer under the real budget, including the exact enabling action in knockback/pierce and tight-path scenes |
| Stochastic outcome estimates are stable enough to decide and carry calibrated risk | Handed | Seeded replicate distributions, confidence/risk trace, and rare-event detection under 4 ms | No F measurements. Learned weapon outcomes, enemy futures and player futures add variance. With a 1% catastrophe, 256 independent samples still miss it with probability `0.99^256 = 7.63%`; this arithmetic is illustrative, not a model of Terraria | **Fail** | Requires an explicit risk contract and empirical decision-error bounds for each safety-critical scene, not only expected return |
| Partial observability is modeled rather than conflated with tri-state unfinished knowledge | Handed; project rule | Belief/observation model that never authorizes work on unresolved reach/light and reacts to player observations | POMCP requires a correct belief state and a black-box generative simulator; its convergence result is conditional on the true belief and finite horizon. This project deliberately treats unfinished reach/light as `NotYet`, not a random hidden world | **Fail** | Requires a named separation: hard epistemic admission remains outside rollout sampling, while genuinely stochastic hidden state has an updateable belief model |
| Reward represents the player's desired whole behavior without exploitable proxies | Handed; Expected Behaviour | Metamorphic reward tests and effect-receipt accounting across all activities | F supplies no reward. Counts reward duplicate torches or split jobs; damage rewards trivial-foe farming over nearby ore; movement rewards busywork; negative time rewards idling; sunk-cost terms finish obsolete work. MCTS optimizes these defects more effectively | **Fail** | Requires outcome-based marginal effects and hard policy constraints that survive the listed metamorphic controls |
| Samples can be reused across ticks without either staleness or global restart starvation | Handed | Rebase/invalidation trace for irrelevant edits, player motion, target replacement, knowledge changes and resource changes | Existing combat cache demonstrates the burden: keys include target generation, coarse geometry, fire tick, knowledge, terrain and hashed enemy content (`CachePlannedSims.cs:40-67,89-119`). Current `EnemyForecast` is valid only for the tick it was built (`ForecastEnemies.cs:10-15`) | **Fail** | Requires dependency/provenance sufficient to preserve unaffected subtrees and invalidate/rebase every affected transition; a global revision or incomplete state hash does not qualify |
| Imagined events cannot mutate real learning, receipts or world state | Handed/shared foundation | Before/after state audit around thousands of rollouts, including cancellation | No rollout architecture specifies isolation. Torch placement, mining and chopping are irreversible native effects; weapon knowledge is updated from landed real hits | **Fail** | Requires a side-effect-free model whose simulated observations are unambiguously typed and cannot reach live learner/effect stores |
| Strict safety and feasibility are preserved under expected-value search | Derived from boss scene and grants contract | Boss test in which a rare hit has high damage and a high-DPS line has better mean reward | Vanilla MCTS backs up sample mean return. The boss requirement says survival first, which is a constraint/lexicographic policy, not necessarily the maximizing expected scalar reward | **Fail** | Requires safety/grant feasibility to gate actions independently of sampled reward, or an equivalently demonstrated risk objective that never selects the forbidden line |
| F can express delayed enabling sequences when the simulator is faithful | Handed knockback→pierce scene | Positive geometry probe and no-shove control | In principle, a rollout tree can value an enabling action through later return. Current source cannot express the transition because push geometry is not rolled forward | **Partial** | This is a real strength once the transition model propagates the causal geometry and the action generator exposes both shots |
| F fits the actual 4 ms wall-clock envelope in representative mixed scenes | Handed; repository constraint | p50/p90/p99 wall time, completed rollouts, root visits, first-answer time, allocations and choice correctness on production hardware | The only measurement is for a narrower beam search: 4 ms, 4,000 simulated-use cap, historical cuts before any stand, and a clock-free emergency search. No global-rollout measurement exists (`BehaviourWeights.cs:380-395`; commits below) | **Blocked** | Only a runnable F slice with the real simulator and scenes can resolve feasibility. Asymptotic convergence and results at 40 ms or seconds do not resolve a 4 ms budget |
| The shared execution foundation is sufficient for F | Handed | Clear boundary between planner output and causal ownership/validity/grants/effect receipts | The foundation supplies essential execution safety and can hold an incumbent while search refines. It does not supply the generative model, candidate distribution, reward, belief or risk semantics | **Partial** | The common foundation survives; it must not be counted as evidence unique to F |

## Concrete counterexamples

Each counterexample names the state, F's plausible decision, the failure, and the minimum condition that would have to become true. These are attacks, not implementation prescriptions.

### 1. Drop versus torch

- **State:** one ordinary drop is slightly nearer; one dark pocket has a valid torch site whose marginal light helps the player's next few seconds. Either action uses feet; torch also uses a hand and inventory.
- **Plausible decision:** short rollouts collect the drop because its receipt is immediate and certain; torch value arrives later through coverage and player path.
- **Failure:** horizon and reward timing decide the winner, not the player's actual benefit. Extending the horizon can reverse it while also increasing model error.
- **Minimum condition:** both consequences must be priced on the same time basis with exact grants/resources and a terminal value that preserves unresolved downstream value.

### 2. Seven overlapping torch sites plus three drops

- **State:** seven legal torch sites overlap in illumination; three drops lie along the route.
- **Plausible decision:** root expansion admits ten job actions, then stand/path/timing variants. A full ten-job ordering has `10! = 3,628,800` permutations; MCTS need not enumerate them, but under small samples it sees only a proposal-biased fraction. A blind depth-four sampler over ten actions hits one specified sequence with probability `10^-4`; 256 samples hit it at least once only about 2.53%. This is a stress illustration, not a prediction for informed UCT.
- **Failure:** duplicated coverage can make several torch placements look independently valuable; a rare good interleaving may never be generated; enumeration order can decide which roots get one visit before the clock.
- **Minimum condition:** action equivalence/marginal effects must collapse duplicate sites, and fixed-budget results must survive permutations of candidate enumeration while still exposing every materially different job.

### 3. The player takes step one of the plan

- **State:** rollouts assume the player continues right; after the companion starts, the player reverses or drops.
- **Plausible decision:** retain a deep, high-visit subtree because reuse is essential at 4 ms.
- **Failure:** reuse converts old confidence into confidently stale intent. Full restart discards nearly all work each tick and returns to first-action starvation.
- **Minimum condition:** the root must rebase on observed player intent with causal dependency tracking; measured reuse must preserve only futures whose premises remain true.

### 4. Irreversible action

- **State:** a rollout tests placing a torch, breaking ore, chopping a tree, or consuming a finite thrown stack.
- **Plausible decision:** invoke real interaction logic for fidelity, or approximate it in a copied state.
- **Failure:** the first mutates the live world during thought; the second creates a second semantics that can diverge from native effects. Undoing native world edits is itself an incomplete simulator.
- **Minimum condition:** an isolated transition model must predict the exact receipt/resource changes needed for choice, and execution must revalidate before committing the one real effect.

### 5. Trivial foe versus nearby ore

- **State:** a harmless slime is easy to hit while a nearby vein is useful and already on the player's route.
- **Plausible decision:** repeatedly choose combat because damage reward is dense, immediate and low-variance while ore value is delayed by travel and several mining ticks.
- **Failure:** reward density substitutes for importance; adding harmless hit points can perversely raise combat value.
- **Minimum condition:** a metamorphic control that adds a harmless damage sponge must not change the work choice unless it changes player risk or a genuine effect.

### 6. Enemy outside while the player descends

- **State:** a target remains outside the player's changing intent region as the player drops into a cave.
- **Plausible decision:** reused combat samples retain high estimated return because the target and weapon state remain similar.
- **Failure:** the companion leaves the player for a stale opportunity, or invalidates all combat samples and loses the 4 ms tick. Stable identity alone is insufficient; the purpose of the plan changed.
- **Minimum condition:** validity must include causal purpose relative to current player intent, while allowing equivalent-target rebinding only when that purpose and effect remain valid.

### 7. Boss versus ordinary fight

- **State:** a high-damage attack has a small predicted chance to hit the companion; an aggressive firing line has higher mean damage than a safe line.
- **Plausible decision:** choose the aggressive line because most small-budget samples contain no hit and mean return is higher.
- **Failure:** violates the explicit boss rule to avoid being hit before maximizing damage. In an ordinary fight the same risk may be acceptable, so one universal scalar is also suspect.
- **Minimum condition:** boss survival must be represented as an enforced policy/risk constraint whose violation cannot be washed out by sampled damage reward.

### 8. Moving enemy opportunity expires

- **State:** a moving Eye creates a pierce window that closes next tick or is replaced in the same NPC slot.
- **Plausible decision:** reuse the action's high visit count through a coarse state hash or stable slot.
- **Failure:** the root statistic belongs to a different geometry or generation. Restarting every tick loses the very depth used to see the pierce.
- **Minimum condition:** target generation, predicted geometry, fire time and opportunity expiry must participate in rebase validity, and the test must show stale samples disappear while equivalent ones survive.

### 9. Knockback enables later pierce

- **State:** shot one can hit an Eye; shot two cannot initially pierce Eye plus zombies. Only the first shot's shove creates the later line.
- **Plausible decision:** sample many shot sequences through the present simulator.
- **Failure:** every rollout retains the pre-shove enemy forecast. No visit count can discover the actual sequence. A hand-authored reward bonus for knockback would only hide the missing transition.
- **Minimum condition:** the transition model must advance affected body positions/collisions; the positive probe must choose the shove sequence and the no-shove control must lose both predicted and actual later pierce.

### 10. Repeated inserts starve old work

- **State:** while travelling to an older useful job, a stream of cheap drops appears just ahead.
- **Plausible decision:** each shallow rollout inserts the newest immediate receipt and postpones the older job again.
- **Failure:** receding-horizon starvation despite each individual choice having higher sampled short-term return. Adding an arbitrary commitment bonus would recreate hysteresis and sunk-cost behavior.
- **Minimum condition:** the state/return must represent continuing obligations, deadlines or foregone value so the old work wins only while its *remaining* consequence is still best.

### 11. Full cargo

- **State:** a drop is close but cargo has no compatible/free slot.
- **Plausible decision:** a simplified rollout rewards reaching or touching the drop.
- **Failure:** it repeatedly chooses an impossible collection effect or displaces a feasible job.
- **Minimum condition:** exact cargo transition and pickup receipt are part of the state; zero actual capacity means no simulated collection reward.

### 12. Unknown versus disconnected reach

- **State:** candidate A's reach flood is unfinished; candidate B is proven disconnected.
- **Plausible decision:** sample A as reachable with a prior probability and B as low probability, allowing A to win.
- **Failure:** turns `NotYet` into permission. Conversely, treating both as unreachable erases a future valid job.
- **Minimum condition:** unresolved admission remains unresolved outside stochastic rollout; only completed evidence may distinguish reachable from disconnected.

### 13. Real 4 ms pressure, including interruption before the first expensive candidate

- **State:** many enemies and mixed jobs make proposal generation itself expensive.
- **Plausible decision:** initialize the tree, enumerate/generate root actions, then start the first rollout.
- **Failure:** the clock expires with no visited root action. This already happened in the narrower combat system: Giant Worm pairing exhausted `Propose` before a stand was priced, with 699 of 1,156 worm ticks `Unresolved:budget-cut` (commit `4d44794`). A later 15-hostile window had 922 of 934 ticks unresolved before the clock-free from-here fallback (commit `145be5a`).
- **Minimum condition:** an executable incumbent exists before candidate generation and the total decision path, including fallback, remains inside the true wall-clock budget.

### 14. No plan

- **State:** no optional opportunity is currently admissible, or every search remains unresolved.
- **Plausible decision:** MCTS returns no root edge, zero-valued wait, or a random unvisited action.
- **Failure:** absence of a sampled plan becomes stillness or unsafe guesswork.
- **Minimum condition:** the shared companion action is a valid explicit incumbent with a causal purpose, not an accidental default encoded as zero reward.

### 15. Tiny fluctuations

- **State:** physical state is materially unchanged, but a few rollout outcomes or posterior weapon draws differ.
- **Plausible decision:** root sample means cross and the chosen job changes every tick.
- **Failure:** visible thrashing driven by Monte Carlo noise. A fixed percentage hold bonus is arbitrary hysteresis and can preserve a worse plan after real change.
- **Minimum condition:** switching evidence must reflect paired uncertainty/current remaining consequence, and a seeded unchanged-state run must hold the same causal course while a material counterfactual changes it.

## Costs and complexity

### First usable action cost

The phrase “anytime” applies after the algorithm has a state, legal-action set, initial node values, and at least one completed evaluation. In this domain each of those can be expensive. The existing attack planner spends time proposing stands, assessing tri-state reach, solving aims and simulating projectiles before pricing a plan. The production safeguard proves the point: when the 4 ms decision ends with an empty pool, it runs a separate count-capped, **clock-free** 256-simulation from-here price (`PlanningBudget.cs:50-56`; `SearchAttackPlans.cs:639-682`). F cannot cite that as a bounded first action. It is evidence that interruption before the first answer is a distinct design problem.

### Branching and discretisation

The action is not merely “combat, torch or collect.” It contains target/effect identity, destination/route, weapon, aim, fire time, tool use, concurrent hand/feet grants, wait/continue/drop, and possibly duration. Some parts are discrete and some continuous. Standard UCT's initial exploration requires trying represented actions; Yee, Lisy and Bowling state that UCT is not directly applicable to continuous spaces and that even a large finite action set produces shallow lookahead. Progressive widening limits width as a sublinear function of visits, but then the candidate sampler/order and widening constants determine what exists during a 4 ms decision. That policy is not free machinery; it is much of the brain.

The root must also distinguish physical concurrency from policy concurrency; otherwise it either misses valid joint actions or multiplies the branch factor with invalid pairs:

| Pair | Required interpretation in sampled state |
|---|---|
| Shoot + combat flight | Compatible: independent hand and feet grants |
| Projectile already in flight + later work | Physically compatible: the projectile persists after the hand action; later job still needs ordinary admission |
| Two active hand uses | Incompatible while their grants overlap |
| Evade + current job | Compatible: evade bends the flight while the job retains causal ownership |
| Shoot + mining approach | Current policy does not admit this combination even though feet and hand can be described separately; a global F proposal must name the policy rather than infer it from physics |
| Cooldown + other hand action | Undecided until cooldown semantics say whether the hand is occupied; treating cooldown as free or occupied by assumption changes the tree |

Representing each compatible joint action explicitly grows width combinatorially. Factoring the hand and feet policies reduces width but changes the process from a single flat MCTS into a factored/concurrent planner with its own coordination rule.

### Stochasticity and variance

There are at least three distinct uncertainties and they must not be averaged together:

1. **Aleatory execution uncertainty:** projectile spread or genuinely stochastic world outcomes.
2. **Epistemic model uncertainty:** learned weapon effect parameters.
3. **Unmodeled future agency:** enemy AI branch and the player's next action.

Resampling all three independently in each rollout can make root estimates too noisy for 4 ms. Holding one posterior draw fixed for the decision makes comparisons fairer but can optimize a single unlucky model sample. Resampling per root action gives different random worlds to competitors; shared random numbers reduce comparison variance but do not fix model bias. The architecture needs to say which is being estimated. “More rollouts” is not an answer at 4 ms.

### Partial observability

Silver and Veness's POMCP is relevant but raises, rather than removes, the burden. It assumes a black-box generative simulator for successor state, observation and reward and proves convergence conditional on a correct belief state for finite-horizon POMDPs. It constructs a history tree and particle beliefs; their reported systems could execute hundreds of thousands of simulations per second in their benchmark model. Nothing in that result establishes that a Terraria causal simulator, reach/light searches and native effect approximations can produce enough correct samples in 4 ms. Sunberg and Kochenderfer later show that a straightforward double-progressive-widening POMDP extension can collapse beliefs to one particle and converge to a suboptimal policy regardless of computation time. Partial observability is an architecture, not a boolean added to UCT.

### Reward exploits and policy necessity

The companion has several non-compensatory rules: boss survival before damage, no optional job on unanswered reach, no collection when cargo cannot accept it, no simultaneous conflicting grants. Encoding these as large reward penalties leaves a scale at which other reward compensates for them. The common execution foundation can reject illegal execution, but if the planner repeatedly chooses rejected high-reward actions it still starves useful work. Feasibility and policy need to shape admission/tree expansion, while reward compares legal consequences. This is a domain necessity, not an MCTS-specific benefit.

### Sample reuse and invalidation

Reuse is attractive because four milliseconds cannot rebuild depth each tick. It is also dangerous because almost every relevant premise changes. The present projectile cache includes weapon/modifiers, quantized muzzle and aim, absolute fire tick, weapon-knowledge revision, terrain revision and hashed enemy content. Its comments warn that any moving/changing simulator input omitted from the hash makes a stale flight price a fight that moved on (`CachePlannedSims.cs:89-119`). A global terrain revision throws away unaffected samples; a spatial dependency set preserves more but must include every cell/actor/effect read by every descendant. Tree reuse multiplies the cost of an incomplete state representation.

### Learn versus act safety

Rollouts must read a frozen knowledge snapshot and emit simulated observations into isolated storage. They cannot call the live learner, effect-receipt ledger, inventory or terrain mutation path. Otherwise imagined hits teach weapon models, imagined pickups consume capacity, or imagined torch placements alter later real senses. Conversely, if the rollout uses a simplified copy, its divergence must be measured at the decisions it changes. This boundary is stronger than ordinary cache isolation because the whole point of F is to execute many futures that must never have happened.

### Implementation and maintenance cost

F requires all of the common foundation plus:

- a copyable or persistent world abstraction for every causal variable used by each activity;
- a mixed discrete/continuous action proposer and progressive-widening policy;
- a rollout policy/terminal value for all leaves;
- a belief and observation model if player/enemy uncertainty is treated as partial observability;
- a risk objective or hard safety shield;
- transposition/tree-reuse keys with causal invalidation and rebase;
- deterministic seed/control support, confidence telemetry, allocation accounting and model-vs-world calibration.

The largest cost is semantic duplication. Every new real effect creates a corresponding simulated transition and invalidation dependency. The current knockback omission is the concrete maintenance warning, not a hypothetical one.

## What survives the attack

F has real strengths when its preconditions hold:

- It can compare stochastic multi-step consequences without enumerating every chance outcome.
- It can spend additional computation on promising branches and reuse a tree when the root transition is stable.
- It can represent an enabling action whose immediate reward is poor but later result is valuable, such as shove then pierce, **if** the model and action generator contain that causal sequence.
- It supports online refinement around a safe incumbent and can return increasingly informed estimates under a variable budget after initialization.
- It is a plausible local optimizer for a bounded tactical decision with a small generated action set, a cheap faithful simulator and material stochastic outcomes.

Those strengths do not establish it as the global brain. They identify the conditions under which it may earn a local role.

## Borrow, reject, and strongest competitor

**Borrow:** sampled comparison of bounded tactical alternatives; root confidence/visit telemetry; seeded common-random-number evaluation; offline high-budget rollout as a critic of cheaper online policies; local tree reuse where state identity is complete.

**Reject as the global primary architecture:** one MCTS spanning movement, combat, lighting, drops and work under the 4 ms production budget. The global action generator, simulator, reward and belief system would contain nearly all domain policy while MCTS adds sampling variance and weak first-answer guarantees.

The strongest competitor is **B, an event-repaired route-and-task schedule over concrete jobs with local combat as a job**. B wins when job effects, resource occupancy, durations and deadlines are mostly known: it has a finite concrete action set, can keep an executable prefix, repairs on observed events, exposes why a task is next, and does not spend samples rediscovering deterministic task order. F wins over B only when stochastic tactical outcomes materially change the action and a bounded, calibrated simulator produces enough samples before the decision deadline. B remains weaker for state-changing combat geometry unless the local combat job supplies a causal tactical model; that is the narrow place F could be borrowed.

## Asymptotic claims versus measured claims

| Claim | Type | What it does and does not prove here |
|---|---|---|
| UCT is consistent with finite-sample bounds in finite-horizon or discounted MDPs with a generative model | Asymptotic/theoretical (Kocsis & Szepesvári, 2006) | Supports the algorithm under model assumptions; does not prove a good root action before 4 ms, model fidelity, continuous-action coverage or safe rare-event choice |
| POMCP converges for finite-horizon POMDPs given the true belief state and black-box simulator | Asymptotic/theoretical (Silver & Veness, 2010) | Shows a path for partial observation; does not supply this project's belief, observation or simulator |
| Large/continuous action spaces need progressive widening or stronger domain-specific methods | Analytic plus measured in a different domain (Yee et al., 2016) | Directly identifies the width/depth trade-off; curling measurements do not transfer to Terraria or 4 ms |
| DPW alone can cause single-particle belief collapse and suboptimal convergence in continuous POMDPs | Theorem plus simulations (Sunberg & Kochenderfer, 2018) | Refutes “add progressive widening” as a sufficient POMDP answer; it does not prove their alternatives fit this project |
| Existing AICompanion combat search exhausted before first priced stand and later needed a fallback | Measured repository history (`4d44794`, `145be5a`) | Direct evidence that pre-rollout proposal cost and empty-pool interruption are live risks; it is not an F benchmark |
| 256 blind samples rarely hit one designated depth-four sequence among ten actions; rare catastrophes can be missed | Arithmetic probe in this review | Demonstrates sample scarcity sensitivity; informed UCT and domain proposals need not sample uniformly, so this is a falsifier design aid, not predicted production performance |

## Exact falsification experiments

No experiment below has been run; there is no F implementation. Each is phrased so a future prototype can falsify the proposal without accepting a favorable aggregate score.

1. **First answer and hard budget.** Run the everything-at-once scene at fixed 0, 1, 2 and 4 ms budgets, including a clock cut before candidate one. Record state-build, proposal, first completed rollout and final-selection times separately. Fail if any tick lacks a valid incumbent or if any fallback/cleanup exceeds the same total 4 ms budget.
2. **Candidate-order metamorphism.** In the seven-torch/three-drop scene, permute enumeration order across all fixed seeds. Log which materially distinct root actions received a visit. Fail if an equivalent world chooses a different course solely because candidates were listed earlier, or if the selected action was never compared with an action known to dominate it under the same model.
3. **Continuous action coverage.** Construct a narrow stand/aim window where only one small parameter region enables the effect. Compare the candidate generator's support against a dense offline sweep. Fail if the good region has zero online proposal probability or is never reached at 4 ms across the declared seed suite.
4. **Knockback causal geometry.** Second pierce is impossible initially and becomes possible only after the first shove. The no-shove control preserves initial geometry. Fail unless F predicts and chooses the enabling sequence in the positive case, predicts no later pierce in the control, and the executed collision record agrees.
5. **Variance and tiny fluctuations.** Hold the observed world byte-for-byte fixed; rerun many seeds/posterior samples. Then apply one material change. Record root return distributions and switch evidence. Fail if unchanged-state noise repeatedly flips the causal course or the material change fails to flip it.
6. **Boss rare loss.** Give the aggressive line a higher mean and a low-probability forbidden hit; safe line has lower damage. Vary the hit probability through values smaller than one expected observation per 4 ms decision. Fail if any declared boss-safety policy selects the aggressive line because no damaging sample happened to occur.
7. **Partial-observation control.** Present identical visible worlds where a reach query is unfinished in one and completed-disconnected in the other. Fail if F starts the optional job in the first or treats the two states as the same permanent refusal.
8. **Reuse/invalidation pairs.** After building a tree, make (a) a terrain edit outside every sampled dependency, (b) an edit on the sampled route, (c) same-slot target replacement, (d) equivalent-target replacement preserving effect purpose, and (e) player intent reversal. Fail if (a) discards the tree, if (b/c/e) retains affected values, or if (d) cannot rebind without losing valid work.
9. **Learn-versus-act isolation.** Hash live terrain, cargo, grants, effect receipts and weapon learner before and after thousands of canceled rollouts. Fail on any change. Then execute one real effect and fail if exactly the corresponding real state does not change.
10. **Reward exploit metamorphisms.** Add a harmless high-life dummy; split one ore effect into two bookkeeping tasks; duplicate an overlapping torch site; increase already-spent travel while holding remaining state fixed. Fail if these representation-only changes alter the chosen course.
11. **Repeated-insert starvation.** Stream low-value drops ahead of a route while an older job's remaining value/deadline stays fixed, then separately lower that remaining value. Fail if the first sequence postpones the old work indefinitely or the second preserves it because of spent effort.
12. **Full cargo and grants.** Repeat the same drop scene with one compatible slot and with no capacity; repeat mixed hand/feet scenes with every occupancy combination. Fail if simulated receipts or concurrent actions contradict the executor's actual acceptance.
13. **Decision regret under budget.** For each required scene, compare 4 ms selection to the same model at increasing offline budgets and to executed-world receipts. The point is not to canonize the long search; fail if additional budget systematically reverses the 4 ms root choice or if both agree against the world because the model omits the causal effect.

## Attack report

| Angle | What I tried | Outcome | Evidence |
|---|---|---|---|
| Alternative | Compared global F with an event-repaired concrete job schedule using the same observation, grants, validity and receipts; asked where sampling earns its cost | **Landed** | Most global jobs are finite and largely deterministic; F becomes preferable only for bounded stochastic tactical choices. Shared machinery cannot be credited uniquely to F |
| Coverage gap | Traced required scenes through state/action/transition/reward/observation, with special probes for shove→pierce, boss safety and unknown reach | **Landed** | Current transition drops shove geometry (`ForecastEnemies.cs:59-87`); no F belief, risk or reward contract exists |
| Real input | Used seven overlapping torch sites + three drops, full cargo, unknown/disconnected reach, moving/replaced enemy, repeated inserts and player reversal | **Landed** | These inputs force large/continuous branching, exact resources and selective invalidation; none is resolved by MCTS alone |
| Overhead | Accounted for state copy, action proposal, route/reach, projectile simulation, belief sampling, rollout, backpropagation and rebase inside 4 ms | **Landed** | Existing narrower planner already exhausts 4 ms before pricing (`4d44794`) and uses a clock-free fallback (`PlanningBudget.cs:50-56`) |
| Residue | Asked what duplicates the live causal system and what remains if MCTS is removed | **Landed** | The generative transition model, action proposer, rollout policy and reward contain the behavior; simulator/live semantic duplication already left knockback out of subsequent segments |
| Smell with a future | Tested the likely “MCTS handles uncertainty/continuous choice” claim against progressive widening, posterior sampling and tree reuse | **Landed** | Candidate sampling and widening constants become hidden policy; incomplete hashes preserve stale confidence; reward bugs are amplified by optimization |

## Evidence from research and practice

The desk evidence is conditional, not promotional:

- Kocsis & Szepesvári, **Bandit Based Monte-Carlo Planning** (2006), establish UCT consistency and finite-sample bounds for finite-horizon or discounted MDPs when a generative model is available: <https://aima.cs.berkeley.edu/~russell/classes/cs294/s11/readings/Kocsis%2BSzepesvari%3A2006.pdf>.
- Silver & Veness, **Monte-Carlo Planning in Large POMDPs** (NeurIPS 2010), require a black-box generative simulator and condition convergence on the correct belief state: <https://papers.nips.cc/paper_files/paper/2010/file/edfbe1afcf9246bb0d40eb4d8027d90f-Paper.pdf>.
- Yee, Lisy & Bowling, **Monte Carlo Tree Search in Continuous Action Spaces with Execution Uncertainty** (IJCAI 2016), state that ordinary UCT must try represented actions and that large finite spaces give shallow lookahead; progressive widening makes the considered action set grow with visits: <https://www.ijcai.org/Proceedings/16/Papers/104.pdf>.
- Sunberg & Kochenderfer, **Online Algorithms for POMDPs with Continuous State, Action, and Observation Spaces** (ICAPS 2018), prove DPW alone can collapse a belief to one particle and converge suboptimally: <https://doi.org/10.1609/icaps.v28i1.13882>.

The practical room is sparse and low-confidence, but it agrees with the desk research rather than contradicting it. In a March 2021 r/gamedev thread (post +52), one practitioner comment (+4, 22 March 2021) reports nearly a year implementing MCTS for a turn-based game, orders-of-magnitude more effort than rule AI, hard debugging/performance, and only slight improvement; the author agrees (+1) after days debugging: <https://www.reddit.com/r/gamedev/comments/m9vehw>. A later card-game thread (29 August 2024, low scores of +1/+2) calls MCTS anytime but says incomplete information needs opponent-card probabilities and good heuristics, with one practitioner using expert openings before tree search: <https://www.reddit.com/r/gamedev/comments/1f3skfj>. These voices support the implementation/modeling cautions but do not measure Terraria, real-time action spaces or 4 ms. **Research verdict: the room agrees with the desk research, weakly; the repository's own history is the stronger practical evidence.**

## History and implication

- `0a2a98e` (18 September 2026): the 0.30.2 play recorded **8,961** `Unresolved:budget-cut` ticks because a level-one cut returned no plan even when some stands had been priced. The change kept the best priced stand. **Implication:** interruptibility does not imply an executable answer unless partial work is deliberately surfaced.
- `4d44794` (18 September 2026): Giant Worm segments caused O(n²) pierce proposal work; **699 of 1,156** worm ticks cut before any stand was priced. The proposal generator, before plan evaluation, consumed the budget. **Implication:** root action generation is part of the hard real-time problem and domain representation controls cost.
- `145be5a` (18 September 2026): a 15-hostile interval had **922 of 934** unresolved budget-cut ticks; a from-here fallback was added with 256 simulations and no clock after the 4 ms search. **Implication:** the present system restored behavior by stepping outside the wall-clock bound. F must not inherit this while claiming a 4 ms guarantee.
- `PlanningBudget.cs:35-38` records an earlier unit-conversion defect that made a configured 4 ms run for 40 ms. **Implication:** measured wall time and explicit phase telemetry are mandatory; configuration text is not evidence.

No history reviewed shows a global sampled-rollout planner previously built and rejected. The nearby history is narrower combat planning, so it supplies counterexamples and cost bounds, not a direct verdict on F.

## Overall classification

**Fail as a complete global decision architecture; partial/pass-worthy only as a bounded local technique under demonstrated preconditions.**

The disqualifying gates are the absence of a faithful generative model for the required causal effects, no bounded first usable action inside the total 4 ms envelope, unresolved action-space coverage, no belief/risk contract, and no evidence that sample reuse is both selective and sound. The shared execution foundation remains compatible and useful, but every approach receives it. F is not entitled to count it as proof that stochastic search itself is safe, causal or timely.

The smallest state of affairs that would reopen the global verdict is a production-shaped slice that passes the exact experiments above while carrying all activities through one declared state/action/transition/observation/reward contract and never exceeding the total 4 ms decision budget. Until then, asymptotic convergence is evidence about the algorithm after its assumptions are met, not evidence that this companion has met them.

