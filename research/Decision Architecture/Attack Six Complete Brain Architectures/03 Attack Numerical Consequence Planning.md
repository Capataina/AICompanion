# C: numerical receding-horizon consequence planning

Evaluation by the main agent, assigned exclusively to C for the six-way attack. Unlike the worker returns, this is not a fresh-context review. Source checked at b3a4ad94f, 0.30.6. No production changes, build, game or native replay. The later combined design needs a separate adversarial review.

## The unasked question is whether the prediction model can represent a useful next state before a better optimiser is added

Receding-horizon planning means predicting a limited future, executing the first part, and updating from observation. This is compatible with a retained executor and with scheduling. It does not automatically provide either. A complete C contender receives the common execution contracts in this folder's CLAUDE.md.

## Requirements, history and flow

README Expected Behaviour was read in the preceding investigation in full; relevant scenes were refreshed this turn at lines 95–184. They require coherent drilling, finite useful diversions, following a leaving player, remaining productive while positioning, multi-weapon causal sequences, and reconsidering without oscillation. The entire Companion→Brain→Activities→Combat→Planning CLAUDE chain was read with the full-text loader, seven chunks ending CODEX_INSTRUCTIONS_COMPLETE.

The current path is FightEnemies.Prepare → held-plan Validate/Reevaluate or SearchAttackPlans.Search → propose stands → assess all stands → price candidates → prune prefixes/expand beam → choose weighted Pareto-front member → activity selection → movement and FireDueUse. The current planner's bound is depth three, beam width four and a three-second forecast; these are current source values, not proposed design constants. BehaviourWeights.cs:371–395 and CompanionCombat.cs:29 are their owners.

Relevant full recent commit bodies were read from an orientation extract, reproducible with `git log -50 --format=fuller b3a4ad94f`. 457b168 records a unit conversion making 4ms become 40ms and urgency creep invalidating held plans. 0a2a98e retains already priced candidates at a cut; 145be5a adds a from-here fallback because no candidate had yet been priced. c5a1374 extends clearance to actual hover consumers. 03986f0 records specialised generator restrictions and fixture changes in the combat rewrite. These histories explain choices; they do not prove a new architecture solves play.

## Requirement matrix

| Criterion | Evidence | Result | Limit |
|---|---|---|---|
| Useful action while refinement is incomplete | SearchAttackPlans.cs:649–692 falls back after the main budget; PlanningBudget.cs:56 is count-capped and clock-free | Partial | Current repair preserves an answer at extra unbounded wall-clock cost. C must obtain a feasible seed before discretionary search and retain it. |
| One total computation allowance | FightEnemies.cs:182–211 reevaluates before main budget; ReevaluateAttackPlan.cs:62 creates Unbounded; Positioner.AssessStands runs before pricing | Fail as a current property; required in C | Four milliseconds is not an end-to-end bound. Hard wall-clock guarantees also require bounding individual work units and accounting for allocations/diagnostics. |
| A weak first action can enable a strong second action | TopBeam at SearchAttackPlans.cs:199–224 discards dominated prefix outcomes; numerical toy in ChallengeDecisionRules.py | Partial | Pruning by immediate outcome is unsound across different successor states. A low-damage shove can be the unique opener of the best sequence. |
| Knockback alters subsequent aiming | SimulateUse.cs:269–293 and SearchAttackPlans.cs:418–443, directly verified in prior investigation | Fail in current model | Life and debuffs change; later trajectories do not inherit pushed position/velocity. More search cannot repair this. |
| Every legal weapon/stand alternative can compete | SearchAttackPlans.cs:748–783 binds specialised generators; area weapons excluded except AboveArea/AuditGrid | Fail as universal enumeration | Candidate generation embeds policy before the outcome comparison; specialised generation must be a search heuristic, not a false legality rule. |
| Long useful work survives a short tactical horizon | Three-second combat horizon; completion-only counterexample script | Partial | A bounded horizon with no defensible tail value can endlessly choose small immediate rewards. Coarser durative actions and valid retained tails help but do not abolish the valuation problem. |
| Stable completion without hiding meaningful changes | Static route probe completes 500/500 jobs with no mid-job reversal | Partial | Only exact static finite 1D worlds and fixed output sets tested. Model noise, different outcomes and changing players invalidate that evidence. |
| Relative equations resolve cross-domain preferences | README scenes change desired winner with threat, darkness and departure | Fail as parameter-free claim | Geometry/time cannot compare unlike benefits without product policy. Pareto filtering leaves incomparable alternatives. |

## Concrete failure traces

1. A direct hit produces immediate value 5; a shove produces 0 now and permits a later piercing result worth 20. Prefix filtering keeps 5 and deletes 0, even though final outcomes favour the shove. Only prune when states are equivalent for all relevant continuations or an actual bound proves inferiority. Otherwise state-diverse alternatives must survive subject to explicit incomplete-search status.

2. A four-tick indivisible job pays 10; a one-tick repeatable job pays 1; horizon is three ticks. A completion-only planner sees 0 versus 3 every decision and never begins the long job. Extending the horizon just moves the counterexample. Model real persistent partial progress, abstract actions through their next meaningful completion, or use a defensible continuation value; each has costs and no universal zero-tuning result follows.

3. A four-operation budget is consumed proposing geometry before the first one-operation candidate price. The search returns no executable result. Seeding a feasible action first preserves an answer at the same abstract budget. This is an operation-count illustration, not proof an actual native action fits a millisecond allowance.

4. A stale incumbent is cheap to retain but no longer feasible after the player leaves or a wall changes; an unconstrained optimiser then returns a nominally better invalid prefix. Every executing step needs cheap current validity, while deeper reevaluation can be resumable. If no safe/valid productive action is proven, companionship/hold remains legitimate; the claim cannot be 'always do useful work'.

5. An optimistic effect model places the Eye in a line after a shove but ignores its AI acceleration or terrain contact. The planner repeatedly prefers a manoeuvre the world never performs. Unknown physics must reduce confidence rather than produce guaranteed downstream reward. Measure predicted vs realised effects; branching over all conceivable futures is unaffordable.

6. Fresh short jobs repeatedly arrive. Maximising immediate reward rate can starve a long viable task; mandatory fairness can force low-value work after the player leaves. Both values cannot be universally guaranteed. Completion guarantees require a stated stable environment/work-set assumption, and the global objective must say when abandonment is correct.

## Costs and scope of measurements

Unrestricted sequences have branching exponential in depth. A beam reduces the search but loses optimality; evaluating M candidates across E enemies over T simulated steps already costs approximately M×E×T before branching, with projectile multiplicity and aim samples multiplying this. Distinct successor states carry life, debuffs, resources, trajectories, terrain/light overlays and dependencies; copying the entire world per node is inappropriate. Sparse immutable deltas have lower copy cost but complex cache/invalidation keys.

Pure source findings: the count-limited fallback has no clock bound; a held plan can re-simulate up to eight remaining uses under Unbounded; main proposal generation precedes useful plan pricing. No new CPU benchmark was run. A read-only exploratory parse of the two captured TSVs gave 0.30.6 combat_prepare_ms maximum 40.781, p99 by floor-index 5.495; brain_ms maximum 53.09, p99 7.46. 0.30.5 respective maxima 27.713 and 64.41. These are recorded columns, not a causal attribution or new performance trial; their instrumentation semantics require checking before treating them as every-frame component timings. In particular plan_ms names navigation planning and is not a substitute for combat_prepare_ms.

## What survives and what should be borrowed

Retaining a feasible course and improving it incrementally is a strong pattern. Event-driven consequence models can evaluate delayed effects without simulating every tick globally. Short precise tactical prediction can sit inside a longer coarse schedule; all levels must share effects, grants and objective meaning. Read-only live-world queries cannot simply be reused as hypothetical-state queries after a simulated kill, torch or terrain edit: the overlay is part of the interface.

Use continuation, stateful effects, and bounded improvement. Reject a fresh exhaustive whole-brain rollout each tick, prefix pruning across unlike states as if it were a proof, planner-only budgets, scalar reward as a substitute for semantics, and the assertion that warm starting alone proves liveness.

Strongest cheaper competitor is B when interactions are mostly known jobs with modest dependencies, route sharing and changing deadlines. C earns additional complexity only for state-changing actions whose downstream benefit B's task representation cannot express. A is an indispensable control for the capture's ownership failure.

## External grounding and limits

Rawlings, Mayne and Diehl, Model Predictive Control, second edition fourth printing (2022), section 2.7, PDF pp197–200: the optimiser can improve a feasible warm start or return it, and execution advances the sequence. Stability results explicitly assume a system model, admissibility and terminal conditions; they are not guarantees for this game's arbitrary changing tasks. https://sites.engineering.ucsb.edu/~jbraw/mpc/MPC-book-2nd-edition-4th-printing.pdf

Bobiti and Lazar (2017) describe sampled updates to a previous predicted sequence with bounded computational complexity and guarantees under their control assumptions. Read abstract only; no implementation replication. https://arxiv.org/abs/1701.02660

The forum API script was attempted for GitHub/HN and failed DNS in the shell. The web fallback then read the acados maintainer discussion at https://discourse.acados.org/t/maximal-solve-time/893: on 28 October 2024 the maintainer explains that the solver timeout predicts whether another iteration will fit and may still exceed its limit. No reaction score was exposed. The 2024 initialization discussion at https://discourse.acados.org/t/infeasible-initialization-breaks-the-solver/1497 also distinguishes a user's suspected initialization cause from solver subproblem infeasibility; it is not a confirmed diagnosis. The practitioner material adds operational limits to the theoretical result; it establishes no consensus about this game's architecture.

Follow-up verification resolved the telemetry freshness question above. The existing raw reader loaded 12,062 rows, all with choice_fresh=1 and distinct choice_id values. The main agent independently reproduced combat_prepare_ms max 40.781 and floor-index p99 5.495, brain_ms max 53.09 and p99 7.46, and navigation plan_ms max 6.55 and p99 2.09. These are fresh captured measurements for that run, not timings of C and not attribution of the spikes to any one source path. An initial generic CSV read failed because it did not handle the file preamble/BOM; using the established reader succeeded at exit 0.

## Probes and classification

Ran `python3 "research/Evaluation and Observability/Probes/ChallengeDecisionRules.py"`, exit 0. It reproduced six logical counterexamples and a narrow positive result: 100 exact static finite five-job scenes, 500 completed jobs, zero mid-job reversals, maximum 65 ticks. This is an executable check of equations, not Terraria simulation, a reproduction of the actual bug, or a measured algorithm comparison.

Classification: Partial as a global architecture; retain consequence modelling and incremental improvement, reject C as a monolithic solver until model coverage, first-action cost and complete-budget accounting are established. Falsifying tests are a native model mismatch on shove→pierce, no productive seed under the real allowance, a static repeated-switch run with identical outcomes, a long-job horizon starvation case, and a leaving-player case where future utility retains an obsolete fight.
