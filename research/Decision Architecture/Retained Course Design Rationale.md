# Retain a course and repair its future

**Recommendation, 19 September 2026; proposed, not accepted or implemented.** Build one retained course owner over concrete opportunities, with event-driven repair, bounded numerical evaluation of consequences, and an immediately executable current step. Use a coarse schedule for journeys and work; use the existing local combat planner, extended with truthful successor state, for short tactical sequences. Both read the same facts, publish the same kinds of effects and obey the same controls. This is B as the backbone, C for consequential choices, A for immediate responsiveness, and small pieces of D/E for causal and interaction contracts. F is an offline challenger initially, not a second runtime chooser.

The recommendation is judged on the owner's stated axes: closeness to README Expected Behaviour, useful completion, concrete ordering, adaptation to player intent, action under limited computation, global consistency, explainability and few arbitrary tuning knobs. It is an engineering judgement supported by source, history, captures, counterexamples and primary literature. It is not a benchmark win, a proof of optimality, or a claim that all expected behaviours already exist.

## The investigation and its boundary

The request was to attack all six complete approaches, weigh implementation/performance/game-logic costs, choose a combined direction, and investigate autonomously without launching Terraria. The follow-up asked to preserve the investigation under research to prevent repeating it. No production brain implementation is authorised by this proposal.

The source baseline is `b3a4ad94f1f9ea503fca829e45a1ce63c12eb852`, build 0.30.6. The orientation read the last 50 commit bodies, the root and relevant folder guides, README, proposals 01–04 and 04's linked sources, the five Slate project fields, source and both 18 September captures. A direct Grok transcript was not found in the inspected local records; 04's quotations and commit bodies are available conversation evidence, not a substitute claim of having read the original transcript.

There were six dedicated evaluators: two new Sol/high sentinel workers for A/B, three reused workers for D/E/F, and the main agent for C. The runtime refused another new thread; no claim of six fresh isolated contexts is made. All six received the same execution foundation so that algorithm names were not compared against an unfairly richer hybrid. The combined recommendation receives a separate adversarial reading after synthesis. Worker report grades often mix current implementation gaps with approach risks; their matrices are evidence inventories, not six measured scores.

The raw capture reader and small logical probes ran headlessly. No new game, build, native replay, frame-time benchmark or live acceptance run was performed. The probes refute equations and demonstrate narrow model properties; they do not reproduce Terraria physics.

The [attack index](<Attack Six Complete Brain Architectures/CLAUDE.md>) links all six full evaluations. The [probe guide](<../Evaluation and Observability/Probes/CLAUDE.md>) gives commands, assumptions and missing-capture behaviour. Reproduce the history window with `git log -50 --format=fuller b3a4ad94f`; source line references in this proposal name files under `Companion/Brain/`, with exact full paths in the attacks. This document is the synthesis, so later decisions amend it rather than creating an unlinked seventh summary.

## The present system explains why another commitment multiplier is too small a fix

Current flow is observation → one prepared candidate per activity → shared utility and temporary local ordering → family nominations → target validation → one current activity → position/movement and interaction grants → observed effects. The order is discarded after choosing its first step. The system retains several kinds of identity below that decision, but no shared mixed-work course owns the remaining journey.

| Verified surface | Finding | Implication for the proposal |
|---|---|---|
| `ChooseBehaviour.cs:26–36,114–140`; `OrderNearbyTasks.cs:25–33` | The parent sees activity representatives, and the computed order is discarded. | Expose multiple concrete opportunities and retain the selected continuation. Changing only the last comparison cannot choose seven torch sites or several drops. |
| 0.30.6 capture, ticks 7773–11125 | 68 direct collect/torch switches. The pure 9401–10100 window is 359 collect ticks and 341 torch ticks; 20 collection attempts end with the drop present and 23 torch attempts end before interaction. The wider swap window has 53 attempted torch trips and zero companion torch credits. | The repeated switch is observed. Exact attribution of the raw score collapse to one preparation factor remains unisolated; do not promote the plausible explanation in 04 into a proven root cause. |
| `OwnCurrentActivity.cs`; activity attempt receipts | Ownership and native effects are distinct from selection and travel. | Keep those contracts. A course is a layer above the current step executor, not a queue replacing effect ownership. |
| `PlanningBudget.cs:45–56`; `ReevaluateAttackPlan.cs:47–64`; `FightEnemies.cs:182–211` | The nominal four-millisecond search excludes held-plan reevaluation using unbounded simulations and a count-limited, clock-free fallback. | Budget the whole discretionary decision, including setup, validation, fallback, repair and refinement. |
| Fresh timing columns in all 12,062 rows of the 0.30.6 capture | Combat preparation maximum 40.781 ms, floor-index p99 5.495 ms; brain maximum 53.09 ms, p99 7.46 ms. `choice_fresh=1` on every row, with distinct choice IDs. | These are captured component timings, not a new benchmark or a cause attribution. A four-millisecond setting is not evidence of a four-millisecond end-to-end ceiling. |
| `SimulateUse.cs:269–294`; `SearchAttackPlans.cs:418–445`; `ForecastEnemies.cs:59–63` | Later segments inherit life/debuff changes, not knockback-displaced enemy trajectories. | Search cannot discover shove-then-pierce geometry until the model represents it. Reactive exploitation after observing a shove is possible but weaker than deliberately choosing the shove for that future. |
| `SearchAttackPlans.cs:194–224,748–783` | Prefixes are pruned by current outcome even when successor states differ; some stand generators exclude weapon categories before outcome comparison. | Preserve enabling successor states and make incomplete candidate coverage visible. Heuristic omission must not masquerade as physical impossibility. |
| `FightEnemies.cs:136–139`; README Expected row 674 | Current combat refuses a dead player; Expected boss/event behaviour continues fighting. | Shared encounter policy must distinguish this explicit product requirement. It is separate from the torch/drop defect. |

The earlier capture's combat run median was one tick; the newer capture's is thirteen. They contain different play and cannot establish a controlled causal improvement. Neither no-use count means every such tick was waiting for perfect aim.

The last-50-commit reading matters because the same classes recur:

| Historical change | What it established | What survives into this proposal |
|---|---|---|
| `9b402cb` | A body-stall signal used as activity progress worsened churn; its ownership was wrong. | Progress is task-owned native effect or persistent partial work, never generic motion. |
| `6892189`, `7a6b473`, `16a39de` | Destination/movement continuation improved when retained by semantic membership. | Stable purpose and valid bindings; fresh scores do not manufacture new jobs. |
| `573d9d4` | Ordering by work/time already exists, but the order is discarded. | Preserve its mathematical insight, replace stateless exhaustive sequencing. |
| `b89abee`, `a0be32f` | One-purpose ownership, suspension and causal outcomes were made explicit. | Reuse the executor and receipts. Do not revive a compulsory resume stack. |
| `457b168`, `0a2a98e`, `145be5a` | Four milliseconds had become forty; slight urgency changes churned plans; cuts before a priced candidate needed an executable fallback. | A valid first action precedes refinement; incomplete search remains incomplete. |
| `03986f0`, `754e5e8` | Combat search has material cost; reserving several segments while pricing only one understates duration. | Combat exports interruptible bounded segments and the full consequences of any reserved bundle. |
| `c5a1374` | Clearance was extended beyond route cost into hover consumers. | The original global-clearance gap is partly repaired already. Audit consumers rather than claiming the old gap is wholly unchanged. |
| `e0c80b8`, `b3a4ad9` | The live negative remained; 04 was demoted to unfinished discussion. | 04 supplies questions and rejected shortcuts, not implementation permission or proof. |

## The six attacks select complementary responsibilities

| Complete approach | Strength that survives | Implementation and runtime cost | Game-logic failure to prevent | Role in the recommendation; reopening condition |
|---|---|---|---|---|
| A: retained reactive utility and local ordering | Fast response to current intent, threats, capacity and validity; cheap when opportunities are independent. | Multiple concrete candidates still require a new producer surface. Exhaustive order grows factorially; ten candidates have 3,628,800 permutations. | Local value misses enabling moves; repeated fresh jobs can starve old ones; accurate immediate scoring alone does not own a journey. | Keep admission, cheap current actions and responsive observations. Prefer A alone if matched complex scenes show the course adds no useful effects or route savings. |
| B: event-repaired route/task schedule | Retains specific work, routes, deadlines and cumulative resources; repairs a changed future. | New course state, dependency indices, marginal effect evaluation and combat adapter. Local repair can still touch an entire suffix. | Static task values double-count light; opaque fights hold too long; repair on every value change churns; a fixed queue becomes stubborn. | Backbone, with bounded insertion/replacement/reordering. Reject if upkeep consumes budget without a measured completion or travel benefit over A. |
| C: numerical receding-horizon consequence planning | Evaluates an action by the later state it creates, including enabling moves. | Continuous geometry and action branching; model calibration and sparse hypothetical state; expensive precise simulation. | Short horizons starve long work; inaccurate futures optimise the wrong world; outcome-only prefix pruning kills useful setups. | Shared consequence semantics and local precise tactical search inside a coarser course. Expand global simulation only after model fidelity and first-action cost are measured. |
| D: GOAP | Explicit preconditions/effects compose alternative ways to reach an outcome. | Concrete grounding, continuous numbers, multi-goal timing and resource scheduling must be added. | Choosing a named goal before comparing mixed routes restores category arbitration; symbolic facts can hide unresolved geometry. | Borrow causal dependency contracts. Adopt a GOAP subplanner only when several real alternate workflows justify regression search. |
| E: utility-ranked HTN | Reusable methods make interaction phases, preconditions and repair boundaries explicit. | Authoring and maintaining methods; dynamic bindings, backtracking and online order comparison remain necessary. | Method order becomes hidden priority; scene combinations become authored recipes. | Use compact local interaction methods, not a global story tree. Expand only when repeated protocols demonstrably reduce code and repair complexity. |
| F: sampled rollout/MCTS | Can compare stochastic futures when a faithful simulator and sufficient samples exist. | Branch sampling, repeated simulation, state copying and invalidation; runtime depends on required confidence. | Search inherits reward/model errors; sparse samples miss rare harm; anytime improvement does not ensure a first usable answer. | Offline challenger and later tightly bounded local uncertainty experiments. Runtime adoption requires measured extra quality per computation and reliable outcome models. |

These are not six simultaneously running brains. The simpler surviving boundary is one course owner, one shared world/effect vocabulary, one objective policy and one executor, with local producers doing the geometry they already own.

## The objective is useful outcomes delivered in context, not perpetual activity

The broad objective is **deliver useful effects while remaining an attentive, survivable companion, accounting for when those effects arrive and what opportunities the journey costs**. Empty movement, number of selections, number of attempted interactions and damage to a harmless dummy do not earn credit. Movement towards a valid job is execution progress, not completed benefit. Mining damage that persists is partial productive work; repeated aiming is not.

There are three distinct comparisons:

1. **Same accepted outcomes, different order.** Compare remaining travel, completion times, danger, resource use and return. The actual costs can decide drop→torch versus torch→drop without a lighting-versus-loot priority. Earlier delivery and earlier return can still trade off; a stated course objective supplies that preference.
2. **One course dominates another.** If it delivers at least the same accepted effects with no worse harm, timing or resources, and improves one dimension, prefer it. Apply dominance to sufficiently evaluated courses, not arbitrary prefixes with different future opportunities.
3. **Different outcomes trade off.** A rare item, a dark player, a weak enemy and a nearly finished vein have no universal physical exchange rate. Use one explicit context-sensitive utility policy over their predicted marginal effects. The README supplies preferences: player darkness before remote darkness, meaningful danger before trivial help, ordinary items get a chance, ordinary safety wins damage ties, and departing-player relevance changes every job. Boss/event conduct has a stricter contract: optional work is inadmissible, own survival is compared before damage, and the fight remains valid after player death. Utility expresses ordinary contextual preferences; it cannot buy its way out of those explicit encounter constraints.

The recommendation deliberately does not take the reviewers' most conservative suggestion—retain any incomparable incumbent—as the entire value policy. That would let the first tiny job block a much more useful but different course. Nor does it sum counts of effects: two dirt pickups are not automatically twice a rare drop. Nor does it rename all benefits “player seconds saved”; visibility, risk and modded item value are not reliably convertible into seconds.

A practical evaluator retains a vector of natural consequences (unique darkness reduced, items actually accommodated, persistent work, threat/harm change, time, reunion cost and resources), removes dominated complete candidates, and applies the common product policy to the remaining trade-offs. The policy should preserve multiplicative gating where appropriate and must avoid counting the same travel, risk or effect twice at different layers. There is no requirement to force every decision into a linear weighted sum.

“Accepted effects” means effects admitted by actual feasibility and explicit product constraints, not a hidden utility threshold that removes ordinary items before comparison. In boss/event conditions, compare survival among meaningful feasible combat courses first, then damage among equally survivable ones; generic ordinary-work utility does not enter that choice. This needs its own paired cases so “survival first” does not accidentally authorise an idle non-fight that the combat admission contract rejects.

**The policy is a genuine remaining design/calibration obligation.** Architecture can remove the arbitrary 15% incumbent bonus and per-family exceptions; it cannot infer every preference from physics. Before production migration, encode the README's contrasting scenes as preference tests, reuse already justified contextual value terms where they pass those tests, and replace terms that fail. The policy owner is Selection; activity producers report facts and effects, never private exchange rates. This proposal does not claim that the complete numerical utility function is already derived. The next implementation scope includes making that function concrete and testable before it controls the whole brain.

Physical constants, measured durations, learning confidence and computational limits remain numbers. Removing those would remove knowledge or resource bounds. The goal is fewer interacting behavioural knobs, each with a named product purpose, rather than an impossible parameter-free agent.

## One course owner coordinates two prediction scales

```text
Observed world, live intent, capability and shared spatial facts
                              │
         concrete opportunities and local method alternatives
                              │
          retained course + shared consequence comparison
              │                           │
       coarse work/route future     precise local combat future
              └─────────────┬─────────────┘
                       current step
                            │
              existing activity owner and grants
                            │
              position → movement/evade → native effect
                            │
                  observed effect receipts
                            └────→ repair affected future
```

The coarse future advances between meaningful events: arriving at a site, one ore tile breaking, a pickup, a torch placement, a weapon use or a useful short combat segment. It does not simulate every empty tick. Precise tactical prediction handles the short intervals where trajectories, aim, knockback and enemy movement change the result. The two levels share hypothetical state and effect identity so that a kill, drop, light patch or resource expenditure cannot be counted twice.

| Owner | Contract and data direction | What it must not own |
|---|---|---|
| Observation | Live and tri-state facts, identities/generations, capability snapshot, intent, relevant spatial changes and observed native effects. | Private activity policies or imagined successes. |
| Activity/opportunity producers | Enumerate specific candidates incrementally; report prerequisite evidence, remaining-work/duration estimates, effect model, resource phases and validity footprint. | Nominate only one hidden winner per category or decide global category exchanges. |
| Selection/course owner | Retain a current executable continuation and repairable future; compare courses under one policy; own discovery/refinement work allocation and reasons for change. | Move the NPC, simulate every projectile itself, or force resumed work after its purpose expires. |
| Local combat planner | Produce useful attack options from the current pose first; refine stand/weapon/target/aim and short successor sequences; export interruptible segments. | Reserve the companion until an entire fight ends or treat missing samples as proven inability. |
| Current activity owner/grants | Activate the current step, revalidate live preconditions, arbitrate body and hand use, track attempts and receipts. | Invent a second course or award predicted effects as real learning. |
| Position/movement/evade | Own legal destinations, one body/contact model, route progress and all-job avoidance. Consume shared preferences consistently. | Change the task's value because a private safety or clearance proxy disagrees with the shared facts. |
| Diagnostics | Record proposal, commitment, refinement, rejection, execution and actual outcomes separately. | Treat busyness, selection or a successful return code as behavioural acceptance. |

A course holds five kinds of state: observed past; irreversible effects already in flight; the currently executable continuation; a repairable future; and opportunities still unresolved. Historical work is immutable, but its past cost does not buy extra future priority. An in-flight projectile is irreversible; the companion's remaining movement is not necessarily so. The future may contain branches conditioned on real outcomes rather than pretending every prediction is certain.

Purpose and target binding are distinct. “Work this still-relevant vein” can survive one tile being mined by the player; the next tile binding must be revalidated. A deleted drop cannot retain an item-slot identity after another item reuses the slot. An externally placed torch can satisfy a lighting purpose without crediting the companion with the placement.

## Retention is based on future comparison, with explicit repair and uncertainty

Each tick, update immediate facts and cheaply validate the executing step. If it is invalid, repair or release it. If it is still valid, keep acting while discretionary search improves the future. A newcomer can replace the next step when sufficiently evaluated future consequences are better after real switching effects, or when a changed product constraint makes the current one unacceptable. An optimistic first step alone cannot displace a proven action. Sufficient evaluation has a finite meaning: the executing prefix has proven preconditions, and the conservative comparison already resolves the choice despite any remaining tail uncertainty. Where calibrated bounds are available, a rival whose lower value bound exceeds the incumbent's upper bound may replace it without completing irrelevant suffix estimates. Bounds that still overlap receive refinement where it could change the answer; unfinished feasibility never authorises an optional action.

Separate two events:

- **Required repair:** target disappears, permission changes, the needed route becomes proven unavailable, the player/encounter context invalidates the purpose, capacity changes, a necessary timing window closes, or an observed effect contradicts a prerequisite. Revalidate the affected dependencies immediately.
- **Optional improvement:** a new opportunity, revised duration, cheaper route or better tactical possibility. Evaluate with the remaining budget and retain the best course supported by sufficient evidence until an improvement is established. Ordinary motion updates estimates; it does not recreate identities or automatically restart the entire course.

Compare both alternatives from the same current snapshot. Continuing gets the benefit of genuinely shorter remaining travel or work. Switching pays future turning, return, setup, lost future opportunity and any real restart cost. It never pays already-spent travel again. A probe with finish=8 and switch=5 shows that adding twenty past units to switching falsely reverses the correct decision.

For equal evidence and outcomes, retain the current course. For a new course with uncertain estimates, retain uncertainty explicitly and request refinement where the decision could change. A measured uncertainty interval is not a freely chosen 15% margin. Confidence bounds need calibration; intervals guessed by the designer are not a proof of improvement. If two alternatives have sufficient evidence and still trade off, the common product policy decides; incumbency is only the final tie rule.

There is no unconditional promise that every old job finishes under an infinite stream of more important arrivals. The achievable guarantee to test is narrower: with a finite, stable, feasible set of worthwhile jobs and enough execution time, the planner does not manufacture reversals or starve a job through discovery, restart or refinement mechanics. A sustained genuine emergency can postpone ore indefinitely. No age bonus should force it to mine during that emergency.

## Concrete choices, shared effects and resource phases prevent a disguised queue

Expose specific drop stacks, torch sites, pots, reachable ore tiles within a vein purpose, tree purposes and enemy/weapon/stand options. Region summaries help decide which nearby cluster deserves refinement, but a region is not a mission to complete everything inside it. Its aggregate must use compatible marginal effects and feasible access/return, not a raw count of icons.

The scheduler starts from the retained future and tries bounded insertion, removal, substitution and local swaps. It can occasionally consider a different region/course to escape a bad local order, within the same budget. Unknown/unexpanded candidates remain recorded and receive rotating search attention. No permanent fixed “five best” cutoff silently removes the seventh torch. Memory and per-tick work still have explicit limits; bounded discovery is not exhaustive optimality.

Effects are evaluated against the predicted prefix state. The second torch receives only its added illumination after the first, the second drop sees cargo after the first, and a shot sees remaining life and in-flight damage. Predicted loot stays uncertain until it appears. Pure hypothetical overlays must not mutate Terraria, live cargo or weapon learning. Native receipts replace predictions and cause local repair.

Reserve resources by phase. Travelling and an already-launched projectile may overlap. A tool use occupies its hand; a future pickup consumes capacity only if transferred. A firing segment names its stance/timing and interrupt boundary. The existing README rule remains: **weapons fire only while fighting; mining, including its approach, stays quiet**. Interleaving means switch into a short fight and later resume the still-valid vein, not silently add shooting while mining. Incidental noncombat work during a combat gap must respect the same grant and edit contracts.

The Diner Dash analogy transfers to routes, deadlines, setup costs and passive effects. It does not mean manufacture motion when nothing useful is valid. Calm drifting beside the player is correct behaviour. A two-second action is not free merely because the return journey takes longer: its detour, interaction and delay still belong to the comparison.

## Combat needs a faithful model and an immediately useful opener

The global layer receives a bounded set of local combat options, each with identity/generations, stand or admitted region, duration/time window, due weapon uses, resource phases, predicted outcome delta, interruption conditions and dependency footprint. A short inseparable combination may be a bundle, but its entire reserved duration and risk must be priced. A whole enemy-to-death job is not the default scheduling unit.

Combat obtains a cheap legal from-here use or other executable continuation before expensive stand refinement. While moving towards a stronger position, it chooses useful current shots inside the active fight. If no legal productive action is known, it may move towards a proven useful region or keep company under the existing admission rules; it must not claim a shot exists merely to satisfy an “always busy” metric.

After selection, the accepted combat step is the sole authority for actual weapon uses. It may declare a conditional from-here use whose target, resources, effect accounting and validity are resolved by the local planner at execution. That conditional result is registered in the same step before the hand receives its grant; its changed successor invalidates the relevant predicted suffix immediately. A shot outside that declared contract is a proposed step replacement and needs course-owner acceptance before firing. Local refinement can propose alternatives, not mutate the accepted course behind its owner. This preserves current-shot responsiveness without two independent firing decisions or a mandatory full course search before every shot.

To deliberately create a later piercing line, the local transition model must include hit timing, knockback direction/magnitude, enemy movement and terrain interaction with uncertainty. Until that model is established, the safe claim is conditional exploitation: observe where the hit actually sent the enemy, then solve the new shot. That is useful but does not satisfy the stronger planned-enabling-action requirement, so the latter remains an explicit implementation gate.

Do not prune a low-damage opener solely because another prefix already did more damage. The resulting states can support different futures. Prune on genuine successor equivalence or defensible bounds; otherwise keep useful state diversity subject to the computational allowance and report coverage as incomplete. No beam width or sampler promises to find every combination.

## Global facts must reach discovery, choice, destination and execution

The heatmap lesson generalises to a consumer contract, not a single scalar multiplied everywhere. A body-clearance fact, a danger prediction, player intent, capability and edit permission each have one authority. All relevant consumers read that authority with consistent semantics. A preference for clearance must not become an additional wall that closes a valid two-tile passage, and it must influence waiting/hover destinations as well as routes. A legitimate close approach for a sword or tool remains available when its benefit warrants the clearance cost.

| Shared fact | Consumers requiring an explicit audit |
|---|---|
| Player interference | Distinguish evidenced immediate placement/passage obstruction from possible general interference; destinations and execution consume that distinction without changing body-fit geometry. |
| Body fit and clearance | Opportunity/stand admission, travel estimates, route search/smoothing, final stand, idle hover and evade endpoints. |
| Threat and harm | Job comparison, approach, waiting, local attack choice, mining/chopping risk, retreat and evasion. |
| Player intent and ability to rejoin | Discovery region, course value/tail, combat stands, current-purpose validity, recovery eligibility. |
| Capabilities and resources | Candidate feasibility, durations, predicted transitions, phase grants and native activation; no mastery-specific brain permission. |
| Unknown versus impossible | Every producer, course candidate, destination query, fallback and diagnostic reason. |
| Permission and causal effects | Proposed edits, native activation, predicted coverage/cargo, actual receipts and learning. |
| Spatial/causal invalidation | Reach, light, routes, combat, course estimates and retained bindings; unrelated edits cannot continually restart everything. |

The audit must also prevent double charging. A route's predicted threat exposure cannot be added again as though the destination layer observed a second independent journey. “Global” means one coherent world interpretation, not repeated penalties at every call site.

## The budget covers the whole decision and never blocks execution on an unfinished improvement

Keep one overall discretionary planning account. Its charges include candidate generation, current-plan reevaluation beyond cheap validity, hypothetical effects, suffix repair, tactical refinement, cache work and any fallback search. Reserve the cost of observation, required validity, grants and movement rather than letting optional refinement consume them. No nested `Unbounded` escape remains in a live planning path.

Use an elapsed-time deadline plus bounded work units. Deadline checks between large indivisible operations cannot create a hard real-time guarantee; allocations, engine calls and diagnostics need separate measurement. The actual target is a predictable measured envelope and graceful quality reduction under smaller allowances. Cold start is part of the test: a system that is fast only with a warm valid incumbent has not solved first-action latency.

Reuse retained courses, spatial query results and simulation results with keys that cover the facts they read. Coalesce relevant changes, resume expensive searches, and rotate incomplete candidate work. Do not move live Terraria queries onto a background thread merely to hide the cost; any future worker simulation requires an immutable snapshot and stale-result validation.

## What play should look like if the design passes its gates

1. **A mixed cave visit.** You pause near ore. The drone compares the nearby work as a journey, picks up a drop that is genuinely cheap on the way, places the torch whose added light covers the next useful patch, then drills. A placement that makes two later torch sites redundant removes their value. The body does not repeatedly cross the same chamber just because the nearest activity changed.
2. **The player takes step one.** You collect its intended drop. The pickup node is satisfied externally or invalidated, its cargo forecast is corrected, and the still-useful torch/ore future is re-evaluated. Nothing waits for an item that no longer exists, and unrelated work need not restart.
3. **A nearly finished vein meets an enemy.** With enough time before harm, finish the remaining cut. With imminent useful combat, interrupt at the native boundary, make the fight segment, then reconsider the same vein. Resumption is earned by remaining relevance, not owed because it was once in a list.
4. **You descend into the cave.** The course evaluates finishing plus the route to your live intent region. A meaningful short finish may still fit; a long fight outside loses its purpose. If the enemy still threatens your path, fight from along that path. Do not fly backwards merely to preserve the old victim.
5. **The good shot is still forming.** Take a useful available shot while moving, or fit an admissible incidental interaction into a real free interval. Waiting for a better line does not monopolise every useful control. Firing remains confined to an active fight.
6. **A setup makes the next move stronger.** Once knockback transition fidelity is established, a weaker first hit can be selected because it puts several bodies into a later piercing line. If the real shove differs, observe and repair instead of firing into the prediction.
7. **The cave narrows.** Choose the room's free air for ordinary hover, approach surfaces only as useful work requires, and pass through valid two-tile gaps without turning the clearance preference into a wall.

These are proposed acceptance scenes, not claims of current play. They are why the combined design earns more work than a collect/torch identity patch.

## All 32 Expected rows have a disposition

| README responsibility | What this proposal owns; what remains distinct |
|---|---|
| Getting out of the player's way | Recommended interpretation: evidenced imminent placement/passage obstruction is a destination/execution exclusion; diffuse possible interference is a soft cost. This preserves both clauses rather than weakening either. The observation must distinguish active, evidenced intent from a cursor glance; uncertain intent is not telepathy or an extra terrain wall. |
| Boss/event behaviour | Shared encounter constraint, survival-first objective and continuation after player death; event observation itself still needs coverage. |
| Reading player direction | Live intent conditions every course; preserve the actual intent-region and movement mechanisms. |
| Enemy selection | Concrete target/weapon/stand sequences and honest enabling effects; threat and weapon models remain prerequisites. |
| Chaining jobs | Primary new capability: marginal concrete effects, routes, phases and cumulative resources. |
| Several steps ahead | Primary new capability: retained course, causal successors and repair. |
| Choosing a place | Region summaries generate alternatives, then concrete online bindings; no compulsory region mission. |
| Knowing inability | Shared tri-state contract and distinct incomplete candidate coverage. |
| Commitment | Future-cost comparison, stable identities and explicit release; no percentage bonus. |
| Firing position | Existing local planner supplies options; shared clearance/intent, usable current shots and faithful successor state. |
| Self-preservation | Shared harm and health-relative costs, ordinary safety tie policy, and the stricter boss/event survival-before-damage ordering; existing body/liquid immunity preserved. |
| Lighting | Multiple sites and marginal persistent coverage; actual light sensing and native placement remain authoritative. Expected supplies consumption conflicts with existing free-placement descriptions, so no planner silently changes the inventory contract. |
| Threat judgement | Common forecast consumed everywhere; movement/attack uncertainty cannot be repaired by scheduling alone. |
| Movement | Existing orb/motor/route boundary retained; this architecture does not itself prove all cave flight. |
| Player protection | One fight policy with player-harm effects, not a second combat controller. |
| Containers | Uncertain contents, reachable break, capacity and prospective collection effects. |
| Dodge/kite | Existing all-job evade remains; its endpoint and delay must remain visible to course execution. Sustained evasion cannot be assumed to finish a job. |
| Mining | Vein purpose plus native tile progress and handed pick; no dig-to-reach expansion or shooting during approach. |
| Reporting visually | Better behaviour/receipts support diagnostics; thruster/beam/streak presentation is separate implementation. No task labels substituted for conduct. |
| Routes | Consume one route authority; no new planner-body divergence. |
| Staying together | Live relevance and finish-plus-return comparison on every purpose; preserve calm companionship as a valid fallback. |
| Looting | Specific stacks, cumulative capacity, context value; modded importance remains uncertain. |
| Weapons | Local consequence planner and learned effects; unsupported modded firing mechanisms are not solved by a course owner. |
| Unreachable recovery | Local proven work while reunion is unresolved; recovery remains continuous and follows its existing initiation contract. |
| World edits | Closed edit set and home protection before proposal and native activation. |
| Speed | Capability facts update travel/duration/feasibility; no judgement unlocks. |
| Doors | Planning cannot create an opening mechanism or route-edge admission. Closed-door support is a separate known physical/navigation gap. |
| Downed recovery | Existing lifecycle owns this; ordinary course releases controls and revalidates after recovery. |
| Chopping | Tree purpose with coherent tool phases and separate prospective collection. |
| Progression | Capacity/physics changes feed shared facts; mastery gameplay effects remain separate unbuilt work. |
| Handed gear | Preserve four slots and mechanisms; discoverability and mod compatibility require their own acceptance. |
| Magic fatigue | Shared numerical mana/fatigue state in local and coarse predictions, never a false hard no-cast gate. |

The wording of leaving-player versus worthwhile brief finishes also needs consistent tests: this proposal allows a brief finish only if its current relevance and comfortable reunion still hold. It does not retain a long obsolete fight merely because a hard leash has not yet fired.

## Headless checks already performed and the gates still ahead

`ChallengeDecisionRules.py` ran at exit 0. Its six counterexamples cover sunk cost, outcome-only prefix pruning, overlapping light rewards, short-horizon starvation, spending the budget before a first answer, and a supposedly free insertion that delays return. In its deliberately narrow positive model, 100 static finite one-dimensional scenes completed all 500 jobs with zero mid-job reversals. This is exact fixed-output arithmetic, not a Terraria result.

The independently reproduced raw capture counts above use `ReproduceDecisionCaptureCounts.py`. Captures are gitignored; a clone cannot recreate those counts without the named local TSV/JSONL files. A separate read using the same parser established timing freshness. No exact causal fix was tested against the live ping-pong signature.

The durable reader now reproduces the timing/freshness check in the same invocation and prints input hashes. Both durable commands exited zero after filing. The inspected SHA-256 values are:

| Input | SHA-256 |
|---|---|
| `2026-09-18_16-35-26-353.tsv` | `a26523dd8a6e3ee6fafe7ddcdafd3f2156e525f74dcc24843f346b4f353691e7` |
| `2026-09-18_16-35-26-353-events.jsonl` | `9105147820502aeade8a30e717e39d81c84f9745d9464e4e2e240a67ccbdede0` |
| `2026-09-18_18-57-09-481.tsv` | `c4bcd2dccd5ce97ff8314a35575d49ffb27c71922f89653f53f45027dcacb2ce` |
| `2026-09-18_18-57-09-481-events.jsonl` | `a64306253f00feaecb62ebed1092df24b3a7a2d12aed45b4098b37c7b18c0b3c` |

| Production experiment after design approval | Pass/fail distinction fixed before implementation |
|---|---|
| Recorded equal drop/torch geometry and persistent identities | Complete native effects without repeated same-pair ownership reversals; retain the actual capture signature rather than inventing only a tidy two-job fixture. |
| Seven sites, three drops, overlapping light and cumulative capacity | No redundant light credit, capacity overcommit or permanently omitted candidate; bounded first useful action. |
| Player completes step one / projectile already launched | Repair only affected future, retain real in-flight consequences, credit no effect twice. |
| Useful current shot changes a selected combat segment | Every shot belongs to an accepted conditional use or explicit step replacement; update in-flight/resource state and invalidate affected suffix before subsequent grants. No independent local firing authority. |
| Ore almost done with harmless, late and imminent enemies | Different concrete consequences select the expected different winner; retain the vein purpose only while relevant. |
| Player descent with long fight versus short useful finish | Drop obsolete work; preserve only a finish whose current relevance and return meet the stated product test. |
| Enabling knockback, direct damage and inaccurate-shove variant | Deliberately select the enabling move when predicted/observed geometry supports it; repair on mismatch. Removing successor position propagation must make the enabling row fail. |
| Cut before first expensive candidate, cold and warm | Continue a valid current action or cheap admitted baseline; unresolved stays unknown and search resumes. Measure aggregate work including fallback/validation. |
| Better bounded challenger with unfinished irrelevant tail | A proven executable prefix whose conservative comparison already wins may replace the incumbent. Unknown physical feasibility still prevents optional execution. |
| Finite burst of arrivals, followed by stable work | No discovery/restart starvation; old still-useful work can execute after the burst. Infinite emergencies are not a promised completion case. |
| Relevant versus remote edits | Relevant dependencies repair; unrelated edits do not repeatedly erase progress. |
| Cross-family clearance and threat | Route, final stand, hover and evade obey consistent facts while legal two-tile passages remain usable. |
| Boss/event, player death and nearby optional work | Optional work stays inadmissible, survival precedes damage, and the fight remains active after player death. Ordinary combat is the control with ordinary contextual trade-offs. |
| Active placement/passage versus cursor glance | Evidenced immediate obstruction is avoided at destination/execution; a mere glance cannot force skittering or make a legal passage unavailable. Document any observation uncertainty rather than claiming future clicks are known. |
| Capability- and budget-matched A baseline versus retained course | Both receive the same concrete candidates, marginal effects, local successor-aware combat, objective policy, facts, executor, diagnostics and total allowance. Only retained mixed-work course ownership/repair differs. Require more relevant native effects or less travel for the same effects, without worse departure response or material frame-time regression; otherwise shrink the architecture. |

The implementation sequence is: (1) preference/receipt/budget instruments and capture-shaped contract rows; (2) concrete multi-candidate producers and shared marginal effects; (3) one retained course owner with bounded repair and removal of the discarded-order chooser path; (4) combat segment adapter and successor model; (5) global consumer audit and combined headless cases. Each replaces its obsolete path in the same completed migration rather than leaving two permanent brains behind flags. A final fresh game acceptance is still needed after headless gates; nothing in this research asks the owner to launch it now.

## External evidence supports the pieces, not a universal winner

The primary literature supports retaining and improving a feasible course. [Rawlings, Mayne and Diehl, Model Predictive Control, section 2.7](https://sites.engineering.ucsb.edu/~jbraw/mpc/MPC-book-2nd-edition-4th-printing.pdf) explains warm-started feasible improvement under explicit system and terminal assumptions. Those guarantees do not transfer automatically to a changing Terraria world. [Gallagher, Zimmerman and Smith, ICAPS 2006](https://cdn.aaai.org/ICAPS/2006/ICAPS06-023.pdf) studies dynamic scheduling with durations, deadlines, resources, setup costs and quality/stability trade-offs; its declared value model is part of the problem, not something scheduling discovers.

The practical room **adds implementation limits and holds no consensus on this companion**. In the [acados timeout discussion](https://discourse.acados.org/t/maximal-solve-time/893), the maintainer's 28 October 2024 answer says the timeout predicts whether another iteration will fit and can still overrun; no reaction score was exposed. That supports measuring complete work units rather than trusting a timeout setting. GOAP/HTN/MCTS implementation and practitioner evidence is preserved with dates and retrieval limits in the individual attacks. The reports do not establish that one architecture is state of the art for this product.

RimWorld's priority/left-to-right analogy illustrates an explicit policy surface. [Spatial Priorities' implementation description](https://github.com/fluffy-mods/SpatialPriorities) documents work-type priority, left-to-right order and subsequent work-giver/distance choices. That is a coherent different product. This proposal keeps contextual outcome comparison because the README explicitly asks for mixed work, changing relevance and granular order.

## What would make this recommendation wrong

The [combined adversarial review and recheck](<Attack Six Complete Brain Architectures/07 Review the Combined Course Proposal.md>) landed five corrections: single authority over accepted combat uses; strict boss/event conduct; immediate versus diffuse interference; finite sufficient evidence for newcomers; and a capability-matched A control. Those corrections are incorporated above. The recheck passed the proposal as a direction ready for discussion, explicitly not as a complete coding specification, verified runtime or demonstrated improvement. The reviewer reused an existing context and did not author the proposal.

The strongest cheaper alternative is A with the same truthful candidate/effect surface, local tactical consequence planner, policy and executor, but without the retained mixed-work future. It wins if it matches the mixed-site, moving-player and regional-work cases at lower cost. Both must share enabling-action capability: a better knockback model cannot be credited as evidence for course retention. C as a broader numerical planner wins if a compact faithful mixed-action simulator becomes cheap enough that coarse scheduling misses important cross-action effects. HTN/GOAP gain ground if the actual difficulty shifts to many reusable alternate causal workflows. MCTS gains ground only when uncertainty sampling produces a measured advantage over deterministic alternatives within the same budget.

The current recommendation is strong on representation and ownership, conditional on objective calibration, model fidelity and measured runtime. It offers the most direct route from this codebase to the requested companion without assuming an accurate world simulator already exists. It does not claim maximum possible autonomy, complete expected behaviour, or freedom from future issues. The next concrete decision is whether to develop this proposal through the named headless implementation gates; the research itself leaves gameplay unchanged.
