# Approach B attack — event-repaired route and task schedule

**Surface attacked:** an event-repaired schedule of concrete opportunities carrying effects, travel and work durations, dependencies, deadlines, resource compatibility, marginal coverage, and repair on relevant changes, with local combat supplying schedulable work. This is an architecture review, not an implementation review. Repository HEAD was `b3a4ad94f1f9ea503fca829e45a1ce63c12eb852`; `build.txt` was 0.30.6. I made no repository, Slate, memory, production, or test edits and ran no build or visible game. The only file I created is this scratch report.

## The unasked question

**What is the smallest unit whose predicted effect stays true long enough to schedule?**

The discussion assumes that a torch site, drop, ore vein, enemy, and combat move can all become comparable schedule nodes. They cannot safely share one granularity. A drop pickup and torch placement are short, externally observable effects. A vein is a retained job whose next tile has a native remaining-work estimate. A fight is already a bounded local plan with up to three timed stand/use segments. An enemy's position can become stale within a tick, and the current combat forecast explicitly carries remaining life but not the position change caused by knockback (`Companion/Brain/Infrastructure/Observation/ForecastEnemies.cs:59-63`).

Approach B survives this question only if the schedule is hierarchical and its executable combat unit is a **locally generated, interruptible combat segment**, not an opaque “finish fight” node and not every combat tick. A segment must expose a stand or region, timed uses, a validity footprint, resource phases, a predicted effect delta, an interrupt-safe boundary, and actual receipts. The global scheduler may place that segment before or after retained ore, a pickup, or a torch. The local combat planner continues to own weapon geometry and its short beam. A region such as “four dark spots, two pots, one enemy” is likewise a candidate summary used to choose a direction, not a script obliging completion of everything inside it.

Without that boundary, B either becomes an unresponsive macro queue that fights until every target dies, or it duplicates the combat simulator and mixed-action consequence search at global scale. The first fails Expected Behaviour; the second does not fit the measured planning envelope.

## Discovery map: requirements, artefacts, history, and data chain

### Requirements derived before judgement

| Requirement | Origin | Artefact that should prove it | Current evidence and implication |
|---|---|---|---|
| Order concrete sites and identities across kinds of work | Handed brief; README story 12:00; Behaviour rows | A course containing exact item generation, torch tile, enemy generation, vein/job, and combat segment identities | The product explicitly requires seven torch sites, three enemies, and mixed work in one question (`README.md:119-139`, `README.md:677-679`). Current preparation exposes only one candidate per activity to the parent (`ChooseBehaviour.cs:114-140`). |
| Preserve a useful course without oscillation, yet replace it for a materially better course | Handed; README | Retained schedule identity, immutable execution frontier, repair reasons, switch comparison | Current order is discarded every comparison (`OrderNearbyTasks.cs:25-33`); live history records 68 collect/torch swaps and 53 torch attempts with no companion placement (`e0c80b`). |
| Follow live player intent into a cave and let old work expire | Handed; README | Player-intent validity/deadline on every suffix and a dropping repair | Expected explicitly leaves the surface enemy/vein as the player departs (`README.md:125`, `README.md:137`, `README.md:163`). Current activities have an allowance and forecasted fit, but no retained multi-step suffix. |
| Do useful work while planning is unfinished | Handed common foundation; README | Executable incumbent or cheap valid fallback; unfinished distinct from absent | The current chooser preserves `Unresolved` and returns another valid action (`ChooseBehaviour.cs:190-207`); combat also has a from-here fallback after a cut. B must keep this rather than block on schedule completion. |
| Mix combat and work without fight-until-dead or perfect-shot waiting | Handed; README | Interruptible local combat segments with resource phases and tail retention | Expected has two-hit ore before non-imminent combat, bow/shuriken interruption then the same ore, and useful work while waiting (`README.md:157-165`, `README.md:676`, `README.md:695`). Current hands fire only while combat owns the activity (`CoordinateBrainTick.cs:241-271`). |
| Account for sequence-dependent effects and endogenous opportunities | Derived from Expected outcome | State-transition deltas plus observations that add/remove dependent nodes | The story requires knockback that creates later pierce geometry and drops created by kills/mining (`README.md:157-159`). Current combat rolling omits push displacement (`ForecastEnemies.cs:59-63`), and loot observation has no relationship to the action that creates the drop (`ObserveLoot.cs:28-40`). |
| Respect capacity and phase resources cumulatively | Handed; derived from executor | Per-step cumulative cargo and time-varying hand/body requirements | Current collection checks each live drop against current capacity (`CollectNearbyItems.cs:141-147`); current grant has only one body owner and `Available`, `WorkTool`, `Unavailable` hand state (`GrantActivityControls.cs:9-20`). |
| Avoid planner-induced starvation under arrivals and budget cuts | Handed | Fair opportunity discovery, retained executable prefix, and a named policy for persistent backlog | Current preparation rotates optional activities to prevent a family from spending every comparison (`ScheduleOpportunityQueries.cs:11-18`, `:42-64`). That fairness does not extend to concrete sites or an incrementally repaired suffix. |
| Remain inside real 4 ms-scale planning pressure | Handed; history | Deadline-aware incremental search with cost telemetry and a usable answer at every cut | `457b168` establishes that the intended combat clock is 4 ms and that 40 ms was a unit bug. `03986f0` measured a warmed 40-hostile combat search around 12.4 ms p99 and no-cache around 1 second; global exhaustive sequencing cannot be added as if combat were free. |
| Preserve the navigation/body contract | Handed; README | Schedule requests goals; movement remains authoritative for 2x2 legality and clearance preference | Expected requires two-tile passages and centred travel (`README.md:692`). No scheduler should duplicate route search; it should consume travel estimates and submit one destination through the existing boundary. |
| Decide different output sets without hidden category priorities | Handed; proposal hole; derived | A small explicit policy surface, Pareto/dominance treatment, and deterministic unresolved ties | Proposal 04 admits that leftover time cannot decide urgency (`research/proposal/04 Plan a Course of Action and Follow It.md:29-34`, `:610-617`). “Player time saved” or a normalized scalar would still encode category conversion weights. |

### Actual chain traced

```text
Senses (player intent, threat, light, loot, reach)
    -> ScheduleOpportunityQueries (bounded/rotating activity preparation)
    -> each CompanionAction.Prepare (one prepared identity/site per activity)
    -> captured raw/forecast/task ticks/binding
    -> EvaluatePreparedActivities
    -> OrderNearbyTasks (up to a capped close set; exhaustive permutations; first step only)
    -> family nomination and activation-time validation
    -> OwnCurrentActivity (one purpose, one attempt, no tail)
    -> action.Execute -> one PositionRequest
    -> position resolver -> movement/navigation -> evade
    -> one body grant plus one coarse hand grant
    -> combat hands only if FightEnemies is current
    -> incidental in-reach pot/torch if hand and remaining budget allow
    -> native effect observations and the next tick's senses
```

The first point at which a retained mixed course is absent is not the executor. It is the preparation/selection representation: `PreparedActivity` captures one site per activity, `OrderNearbyTasks` throws away the order, and `OwnCurrentActivity` stores only the current purpose (`CompanionAction.cs:22-28`; `OrderNearbyTasks.cs:25-33`; `OwnCurrentActivity.cs:11-14`). A scheduler needs a separate course owner above the existing single-step executor. Replacing `OwnCurrentActivity` with a queue would lose its effect attribution and interruption contracts.

## Requirement matrix

| Criterion | Origin | Expected proof | Evidence gathered | Result | Caveat |
|---|---|---|---|---|---|
| Concrete cross-kind ordering | Handed | One retained course names exact sites/entities/jobs | `README.md:677-679`; `ChooseBehaviour.cs:114-140` captures one candidate per registered activity | **Partial** | B naturally represents concrete nodes, but current producers do not enumerate the required set. |
| Seven overlapping torch sites and three drops | Handed | Multiple site nodes with sequence-dependent coverage and cargo effects | `LightUsefulArea.cs:18-32` chooses the nearest site and retains “lighting” as a job; `CollectNearbyItems.cs:114-210` returns the first valid drop | **Fail** | Static independent node values double-count overlapping light and independent pickup capacity. |
| Stable execution under tiny fluctuations | Handed/derived | Identity-stable prefix; repair only on named constraint/effect/value events | Current order is recomputed and discarded (`OrderNearbyTasks.cs:25-33`); `457b168` shows 0.01 urgency creep once caused 32 combat plans/sec | **Partial** | Event repair can solve this, but “relevant change” is not yet a defined predicate. |
| Player takes one step without needless rewrite | Handed | Intent update changes forecast, not identity, until feasibility/deadline/dominance changes | Intent is a moving region; jobs already project fit (`ChooseBehaviour.cs:229-235`) | **Pass in principle** | Raw travel costs still move each tick; using them as repair triggers would reintroduce churn. |
| Drop obsolete surface work when player descends | Handed | Suffix deadline/validity based on live intent and return cost | Expected at `README.md:125`, `:137`, `:163`; proposal has only an unresolved leftover idea | **Partial** | Repair needs a semantic deadline/constraint, not a score epsilon. |
| Irreversible/in-flight action survives repair | Derived gate | Past and launched effects are immutable; only suffix repairs; receipts retire predictions | CASPER literature disallows changing activities in the past; current combat records fires/hits (`FightEnemies.cs:416-485`) | **Fail** | B is unsafe without an execution frontier and in-flight effect ledger; otherwise it can double-credit or “cancel” a projectile. |
| Trivial foe versus nearly finished ore | Handed | Ore remains prefix when harm arrives after remaining work; fight segment can insert when imminent | Mining exposes `RemainingWork` (`MineOre.cs:59-84`); combat exposes aggregate plan duration (`FightEnemies.cs:378-385`) | **Partial** | The global layer lacks a shared time-to-harm constraint and segment delta. |
| Enemy outside while player descends | Handed | Non-serving combat loses validity/deadline and course repairs toward player | Expected at `README.md:693`; current `AllowsTarget` is intent-region anchored (`CompanionAction.cs:41-60`) | **Partial** | A schedule can propagate this, but an opaque fight macro can hold too long. |
| Boss versus ordinary fight | Handed/README | Boss world-state suppresses optional suffix, widens leash, survival-first; normal combat remains mixable | Behaviour table specifies the strict boss/event contract (`README.md:674`) | **Partial** | This is an explicit product constraint, not an arbitrary category priority. Existing system has only partial encounter handling. |
| Moving enemy/opportunity expiry | Handed | Latest useful start/finish and generation validity; local replan | Forecast is valid for the tick snapshot while boxes project up to 180 ticks (`ForecastEnemies.cs:10-15`) | **Fail** | No durable enemy deadline or confidence-bounded segment window is exported to a global schedule. |
| Knockback creates later pierce geometry | Handed | Local combat delta includes a confidence-bounded positional result, then later segment is re-solved/repaired | Current rolled forecast explicitly does not carry push displacement (`ForecastEnemies.cs:59-63`) | **Fail** | A global scheduler cannot repair from a delta the local planner cannot produce. |
| Repeated arrivals do not starve old work through mechanics | Handed | New insertions cannot perpetually prevent an executable prefix; semantic expiry remains allowed | Query rotation prevents activity-level preparation starvation (`ScheduleOpportunityQueries.cs:11-18`) | **Partial** | A persistent low-value job may correctly never run; the architecture must distinguish semantic from planner starvation. |
| Full cargo and cumulative pickups | Handed | Ordered capacity state; pickup effects consume capacity; capacity change invalidates dependents | Each drop is checked independently against current cargo (`CollectNearbyItems.cs:141-147`) | **Fail** | Two drops can each fit initially and overflow jointly; a boolean “compatible” flag is insufficient. |
| Disconnected versus unknown reach | Handed/common | Unknown nodes never enter executable prefix; known prefix remains; No removes node | Current offer and method admission distinguish unresolved from known-unusable (`ChooseBehaviour.cs:190-207`) | **Pass in principle** | A pending candidate may stay outside the executable schedule; B must not freeze an unanswered route. |
| Search cut before first expensive candidate | Handed/common | Executable incumbent/fallback exists before refinement | Current combat has a from-here fallback after a cut; current chooser can use another candidate | **Pass only with foundation** | A new scheduler that requires one complete course before acting would regress this. |
| Real 4 ms-scale bound | Handed/history | Incremental operation count and live measured tail under simultaneous discovery/combat | Current route-order code explores `n!`; probe printed `10! = 3,628,800`; combat alone has historical warmed p99 ~12.4 ms (`03986f0`) | **Fail as exhaustive search; Partial as bounded local repair** | No live timing was measured here, by brief. Big-O and historical timing are separate evidence. |
| No perfect-shot idle | Handed/README | Local combat supplies a valid lower-quality segment or scheduler runs compatible work during wait | Hands fire only while combat current (`CoordinateBrainTick.cs:241-271`); incidental is pose-only and cannot detour (`ConsiderIncidentalInteractions.cs:15-22`) | **Fail** | Scheduling an opaque attack plan preserves the wait. Flattening every shot into the global scheduler is too expensive. |
| Resource compatibility | Handed/derived | Time-varying body/hand/cargo/mana/consumable profiles | Current executor exposes one movement owner and three hand states (`GrantActivityControls.cs:9-20`) | **Partial** | Phase-level profiles are implementable but are a substantial new contract; job-level booleans are wrong. |
| Choose a rich region, then adapt inside it | Parent synthesis/README | Region summaries compare reachable clusters; selected region expands incrementally into specific nodes | README's four-dark/two-pot/enemy cluster beats smaller regions (`README.md:129`, `README.md:679`) | **Partial** | Treating a region as a fixed script violates the same rows when the player leaves or a new threat arrives. |
| Objective has no disguised category weights | Handed/derived | Explicit constraints plus a small outcome policy; incomparable effects stay visible | Combat already keeps eight natural-unit objectives before weighting (`CombatOutcome.cs:5-29`); proposal admits urgency is outside leftover | **Fail if described as one “work” scalar** | Scheduling cannot remove the product decision. It can keep the policy surface small and auditable. |

## Attack report

| Angle | What I tried | Outcome | Evidence |
|---|---|---|---|
| Alternative | Compared B with the cheaper retained-reactive shape and with numerical receding-horizon planning. Asked whether holding the current identity plus local insertion is enough. | **Landed** | For independent local jobs, A plus frozen identity is cheaper and may match B. B earns its extra state only for clusters, deadlines, dependencies, cumulative resources, and route reuse. C wins when cross-action state transitions dominate and a cheap faithful simulator exists. |
| Coverage gap | Traced every required effect through current producers and asked whether a schedule node could carry it. | **Landed** | Lighting and collect expose one candidate; loot has no expiry; combat omits push displacement; current course owner has no tail. Paths: `LightUsefulArea.cs:18-32`, `CollectNearbyItems.cs:114-210`, `ObserveLoot.cs:10-52`, `ForecastEnemies.cs:59-63`, `OwnCurrentActivity.cs:11-14`. |
| Real input | Exercised overlap, cumulative capacity, continual urgent arrivals, unknown reach, and an event after an irreversible launch. | **Landed** | Toy outputs: static overlap tied A+B to A+C at 5 although unions were 3 and 5; two quantity-2 drops each fit capacity 3 but together do not; twenty higher-valued expiring arrivals left old work unfinished. These refute independent additivity/capacity and deadline-as-fairness, not Terraria performance. |
| Overhead | Counted current exhaustive order space and bounded-repair radius; compared with historical combat cost. | **Landed** | `5!=120`, `7!=5040`, `10!=3628800`, `15!=1307674368000`. Current recursive enumeration is `OrderNearbyTasks.cs:79-115`. Google OR-Tools likewise documents exponential routing growth. Incremental insertion avoids factorial work but sequence-dependent effects can invalidate an entire suffix. |
| Residue | Asked which existing machinery becomes duplicated or falsely authoritative. | **Landed** | A second route planner would violate the movement boundary; a second combat simulator would diverge; a queue replacing `OwnCurrentActivity` would lose attempt/effect ownership. B should retain these and add a course owner, not fork them. |
| Smell with a future | Projected “repair on relevant change” into a moving combat/lighting world. | **Landed** | If relevance means “any value changed,” player/enemy motion repairs every tick. If it means only infeasibility, an urgent new threat never enters. The missing split is constraint repair versus optional improvement, each with a causal sensitivity footprint and bounded cadence. |

## Overall classification

**Partial.**

An event-repaired route/task schedule is the strongest fit of the six approaches for **stable, concrete, spatially clustered work**: it can preserve exact identity, account for already-paid travel, insert nearby work, carry deadlines/cumulative capacity, and repair only the affected suffix. It directly attacks collect/torch oscillation, seven-torch recrossing, expiring drops, and “two copper on the way home.”

It is **not acceptable as a complete whole-brain architecture as currently stated**. The missing gates are not implementation polish: the schedulable unit, execution frontier, phase resources, deadline model, marginal sequence effects, repair trigger, non-scalar policy for incomparable outcomes, and segment-level combat interface determine whether the system does the requested thing at all. Treating combat as opaque fails mixed combat/work and perfect-shot waits; flattening combat into global route nodes duplicates the most expensive and least stable planner.

The defensible scope is: **B as a bounded course-maintenance layer over concrete opportunities and local planners, with A-like executable fallback and a segment-level bridge to combat.** It should not promise an optimal schedule. It should improve and repair a valid suffix while the current prefix acts.

## Smallest conditions that close each failed gate

| Failed gate | Single smallest condition that must become true |
|---|---|
| Overlapping torch/drop effects | Every schedulable effect has a marginal evaluator against the prefix state, and placing a torch invalidates or revalues every coverage-dependent suffix node. |
| Irreversible/in-flight actions | The course has an immutable execution frontier: completed and launched effects cannot be removed, and repair operates only on the suffix after observed receipts. |
| Moving-enemy expiry | Every combat segment carries a latest useful start/finish derived from forecast confidence and is revalidated before execution. |
| Knockback-to-pierce | The local combat planner can export a confidence-bounded post-segment position delta, or the later geometry is explicitly contingent and re-solved only after the hit receipt. |
| Full cargo | Cargo is a cumulative route state updated by each scheduled pickup and rechecked after every actual transfer. |
| 4 ms planning | The implementation always has an executable prefix and bounds each repair operation; no tick enumerates the full order set. |
| Perfect-shot idle | Local combat exposes an executable lower-quality segment or an interruptible wait window in which the scheduler may run a compatible nearby effect. |
| Objective policy | Incomparable outcome sets remain a visible Pareto choice governed by a small explicit product policy; no invented common currency silently resolves them. |

## History and what it implies

I read the relevant full commit bodies, not only subjects.

- `573d9d4` introduced worth-per-time and `OrderNearbyTasks`. It already recognized route order, but deliberately executed only the first step and recomputed. This proves the first-step arithmetic is not enough; it does not prove a retained course will work.
- `9b402cb` reverted a progress-conditioned incumbent modifier after churn worsened from 55.5 to 22.3 ticks per decision. Its root cause was ownership: body stall belonged to whoever held the feet, not to the job being judged. B must consume job-owned progress/effect receipts; shared movement change is not a repair reason by itself.
- `457b168` fixed 4 ms being implemented as 40 ms and changed combat invalidation so 0.01 urgency creep no longer caused roughly 32 plan changes per second. This is direct history against value-change-driven repair.
- `03986f0` measured the local combat planner. Warmed cached C1 after the P rows was about 12.4 ms p99; uncached about 1.0 second. The exact values are historical fixture timings, not measurements of B. They show why global duplication of that search is not a credible 4 ms architecture.
- `754e5e8` fixed aggregate combat duration: pricing only the current segment made a three-segment plan look like 63 ticks while it occupied the body for 180. B must price the duration/resource tail it actually reserves, even if it executes one segment at a time.
- `e0c80b` and `b3a4ad94f` record the current live negative and explicitly demote Proposal 04 from implementation plan to unfinished handoff. The strongest implication is that frozen identity is necessary but not sufficient: combat-as-endless-step-one, urgency versus leftover, new identities, and concrete site grain remained open.
- Folder documentation is consistent with the source on the attacked surface: Selection explicitly says the live negative is the discarded order, Combat says only the running combat activity fires, and Grants says incidental work is in-reach only. The root `CLAUDE.md` “Current state” header is older than HEAD, but its architectural constraints are not the evidence used for the 0.30.6 findings.

No relevant revert or abandoned branch supplied evidence that a retained dynamic schedule had already been built and rejected. The nearest prior attempt is the discarded order and the failed progress-conditioned commitment; neither closes B's open representation problem.

## The strongest coherent version of B

This is the minimum complete version worth testing. It avoids attacking a naive fixed FIFO.

### 1. Opportunity records, not activity labels

Each admitted opportunity is a concrete identity with:

- causal purpose and generation/material binding;
- one effect or a bounded effect interval;
- a destination or admissible region, with route authority left to Movement;
- work/travel duration interval and earliest/latest useful time;
- spatial and state sensitivity footprint;
- prerequisites and successors;
- a **phase profile** for body, hand/tool/weapon, cargo, mana, and consumable state;
- a marginal evaluator against prefix state;
- an execution receipt and explicit completion/invalid/partial result.

This is more than today's `PreparedActivity`, which captures raw value, a forecast, task ticks, one site, and a few flags (`ChooseBehaviour.cs:114-140`). The current target-binding and attempt receipts are worth reusing.

### 2. Region summaries are branch generators

The README's left/right/straight scene (`README.md:129`, Behaviour row `README.md:679`) asks for choosing a place where useful work clusters. B should summarize each reachable region by conservative attainable effects, lower-bound travel, return/separation cost, and known expiry. “Straight: four dark, two pots, one enemy” can dominate “left: one slime, one drop” on completed accepted effects before return.

After choosing straight, the summary expands only enough to produce an executable site and a bounded suffix. It does not become “finish seven things straight ahead.” A player departure, unreachable subregion, or new threat can repair or discard that suffix. This keeps the region as a candidate summary rather than a mission or script.

### 3. Immutable prefix, repairable suffix

Course state divides into:

1. **observed past**, immutable;
2. **in flight**, immutable but unresolved (projectile, started native swing/use);
3. **current executable segment**, retained while valid;
4. **repairable suffix**, freely inserted/reordered/dropped under bounds;
5. **pending unknown opportunities**, not executable until proved.

This is the direct answer to irreversible actions and search cuts. It is also standard in the external iterative-repair literature: past activities are not changeable, conflicts are repaired until consistent or the computation bound expires, and the current plan is continually updated rather than regenerated as a blocking batch.

### 4. Split repair from improvement

“Repair on relevant change” needs two different triggers:

- **Constraint repair:** target vanished/generation changed; permission or tool changed; route became No; capacity/mana/consumable conflict; latest finish missed; boss/encounter constraint changed; effect receipt contradicted prediction. Repair is immediate and limited to the causal suffix footprint.
- **Optional improvement:** new opportunity, cheaper route, changed forecast, player motion, or a better nondominated course. Improvement is bounded and can be skipped while the existing course remains valid.

Raw-score jitter and ordinary pixel motion are neither. A new enemy can be an improvement event; an enemy whose arrival creates a harm deadline can become a constraint event. This avoids the false choice between repairing every tick and never inserting urgent work.

### 5. Bounded local repair, never full enumeration

Start with the retained suffix. For each new or invalidated node, try bounded insertion/removal/substitution and local swaps against cached travel and marginal effects. Keep the best fully evaluated suffix from the prior iteration. An unfinished improvement never empties the current executable course.

Simple insertion is `O(n)` positions per node if transition costs are local. With pairwise travel caching it is `O(n^2)` storage/build for `n` opportunities. Marginal coverage, cumulative resources, and deadlines can force recomputation of the entire downstream suffix for each insertion, making a simple event `O(n^2)`; multiple mutually dependent effects or local search can go higher. Moving enemies can create an event every tick. “Incremental” reduces work when changes are local; it is not a complexity exemption.

The current factorial recursion (`OrderNearbyTasks.cs:79-115`) is useful only as a tiny reference oracle. It cannot be generalized: ten jobs are 3,628,800 orders before enemies, veins, or combat moves; fifteen are about 1.31 trillion.

## Can B share numerical effects with the local combat planner?

**Yes, but at the segment boundary, and not with the current aggregate API alone.**

The current combat representation is close to the right producer:

- `AttackSegment` already names a stand, arrival/start/end ticks, planned uses, and end reason (`AttackPlan.cs:78-87`).
- `AttackPlan` has up to three timed segments and a validity record (`AttackPlan.cs:89-105`).
- `CombatOutcome` preserves eight natural-unit objectives before weighting (`CombatOutcome.cs:5-29`).
- the evaluator already threads remaining life, debuffs, mana/consumable use, and in-flight damage inside its local beam (`EvaluateAttackOutcomes.cs:49-67`, `:154-180`).

The schedule-facing combat option should therefore be one local segment or a very short indivisible bundle when effects cannot be separated. Its contract needs:

```text
identity/generations
admissible stand or stand region
[earliest start, latest useful finish]
duration and interrupt-safe boundary
phase resources:
  travel: body reserved, hand normally free
  firing/use: body at/near stand, weapon hand reserved at due ticks
predicted delta:
  target remaining life and death probabilities
  debuff/mana/consumable changes
  player/companion harm change
  in-flight projectile commitments
  positional/push result only when confidence is sufficient
validity/sensitivity footprint
actual fire/hit/death/position receipts
```

The global schedule consumes a small nondominated set of these options. It does not generate stands, solve trajectories, choose aims, or replay projectile physics. After executing the segment, actual receipts update the world and repair the suffix. If push displacement is not predictable, “later pierce line exists” remains a contingent successor that is generated only after the hit is observed. That is honest and still permits the Eye-to-zombies behaviour when learning/model confidence eventually supports it.

This granularity avoids both failure modes:

- **Opaque whole fight:** a 1,500 HP harmless body does not reserve the body until death; a combat segment can lose to two-hit ore, and the retained ore purpose remains the schedule tail.
- **Global per-tick combat:** the course owner does not duplicate seven stand generators, trajectory simulation, weapon sequencing, or the combat beam.

It also explains how ore purpose survives combat. The course can read `[combat segment against arriving slime] -> [same vein job id, remaining tile binding]`. `OwnCurrentActivity` still executes only the current step and opens a new attempt after interruption; the course owner retains the tail identity above it. Actual mining validation decides whether the vein still exists on resumption.

The present interface is insufficient in three ways. `AttackPlan.Outcome` is aggregate rather than per-segment; `AttackSegment` does not expose ending life/debuff/resource state; and forecast rolling explicitly omits push displacement. These are named implementation gaps, not reasons to make combat opaque.

## Objective attack: scheduling cannot eliminate policy

For a fixed accepted effect set, B can order without category weights: minimize expired effects, completion latency, travel/setup, or makespan; compare remaining suffix including already-spent travel; preserve Pareto dominance. This is where B is strongest.

For different effect sets, there is no neutral answer. “Player time saved,” normalized contribution, “work done,” or a count of nodes all encode exchange rates between a torch, a gel, a pot, one ore tile, and damage to an enemy. Hiding those conversions in a common currency does not remove weights.

A defensible small policy surface is:

1. **Hard feasibility and product constraints:** permissions, reach, resource capacity, native validity, boss/event contract, and the player's maximum separation envelope.
2. **Observed deadlines:** prevent imminent player/companion harm and avoid known opportunity loss. The deadline comes from arrival/despawn/validity evidence, not an activity-name priority.
3. **Pareto dominance in natural outcome dimensions:** completed causal effects, harm prevented/taken, expired accepted value, player separation, time, and resource use. Within one outcome kind, retain its native measure; do not normalize each decision's candidates into a changing scalar.
4. **Same accepted output set:** minimize lost effects and remaining completion/travel cost; this is the scheduler's policy-free core.
5. **Genuinely incomparable fronts:** retain the current valid course unless a rival completes an additional accepted effect before a known deadline without losing one the current course would complete. With no current course, use a stable identity tie-break after the explicit product constraints. If that tie produces wrong play, Expected Behaviour must add the missing product preference; the scheduler must not invent it.

The cluster scene can be handled without saying “lighting beats combat”: a reachable straight region promises seven accepted effects before return while left/right promise two, subject to harm and separation deadlines. This **is still a declared policy**—counting causally verified effects—and should be tested as such. It may choose two junk pickups over one rare item unless the native loot dimension dominates; that is a known limit, not a claim of human judgement.

Boss/event behaviour is a legitimate explicit exception because the Expected column itself says the world is about one thing and optional work stops (`README.md:674`). Treating that as a hard world-state constraint is different from silently making combat always first.

## Concrete counterexamples

| State | What a plausible B decides | Failure | Minimum condition that avoids it |
|---|---|---|---|
| Equal drop and torch, same geometry, no deadlines | Static scores tie; tiny travel changes swap them at each repair | Recreates collect/torch ping-pong | Stable identities plus no improvement repair unless the rival course dominates including switch cost. |
| Seven torch sites with overlapping light footprints and three drops | Sum each site's standalone benefit, then route | Double-counts light and may order sites whose benefit vanishes after the first placement | Marginal effect evaluation against prefix state and coverage-triggered suffix revaluation. |
| Player takes one step | Reprice every edge and “repair on change” | Continuous repair despite unchanged physical opportunity | Player motion updates forecasts; it triggers repair only on a crossed constraint/deadline/dominance boundary. |
| Projectile fired, rival course becomes better next tick | Remove attack node and switch | Already-launched shot later kills/moves target; suffix double-credits or collides | Immutable in-flight frontier and receipts. |
| Two-hit ore, harmless 1,500 HP enemy | Opaque combat job reports positive value and long tail | Combat owns activity indefinitely | Expose a bounded combat segment and zero/impossibly-late harm deadline; compare segment versus ore completion. |
| Surface enemy while player descends | Fight remains valid by target generation | Companion finishes outside while player enters cave | Live intent/separation latest-finish invalidates or dominates the non-serving suffix. |
| Boss plus drops/torches | Generic scheduler inserts cheap work | Violates strict boss behaviour | Encounter world-state makes optional work infeasible until it ends. |
| Moving enemy | Node remains in suffix with yesterday's firing stand | Wasted travel or perfect-shot hold | Confidence-bounded time window and pre-execution local revalidation. |
| Bow knocks Eye toward zombies | Static successor assumes old positions, or no successor exists | Misses the requested pierce geometry | Post-segment position delta when modelled; otherwise generate contingent successor after hit observation. |
| New urgent opportunity each tick | Deadline-aware insertion always takes the new one | Old persistent job never executes | Distinguish correct semantic starvation from planner starvation; ensure the executable prefix completes when no newcomer dominates on actual loss/harm. No age bonus is implied. |
| Capacity 3, two drops of quantity 2 | Both nodes are individually feasible from current bag | Route overfills; second trip is wasted | Cumulative capacity state along the route. |
| Reach flood unfinished for best node | Freeze it into first position | Body waits at a maybe | Pending unknown is outside executable prefix; use current proven prefix/fallback. |
| 4 ms expires before first expensive combat candidate | No new schedule returned | Idle or empty selection | Keep prior valid course; local planner supplies from-here/cheap option; unfinished improvement is not absence. |
| Waiting 40 ticks for planned line; torch/drop within reach | Whole combat plan owns the activity and hand policy | Empty-handed wait | Combat segment advertises wait/resource windows or supplies a lower-quality due use; scheduler may insert only work whose execution preserves the combat timing/validity. |
| Four-dark/two-pot/enemy cluster ahead; single slime/drop left; enemy/dark right | Expand every site globally before choosing | Budget spent before an executable node; or region becomes a compulsory script | Compare conservative region summaries first, then refine only the selected/front regions while retaining a valid current action. |

## Costs

### Implementation cost

1. **Opportunity enumeration changes.** Lighting and collection must expose several candidates with identities and refusals rather than only their first winner. Mining must expose the retained vein and perhaps its next few tile effects without promising the whole unknown vein.
2. **A course owner.** It stores prefix/suffix, pending unknowns, dependency/resource state, repair events, and reasons, while `OwnCurrentActivity` remains the executor/effect owner for one step.
3. **Validity and sensitivity footprints.** Terrain/light/cargo/player intent/enemy generation/knowledge changes need causal dependency indices. A global revision is known to cause needless resets.
4. **Phase resource model.** Body, hand, tool, weapon, torch, cargo, mana, consumable stack, and cooldown cannot be one compatibility bit. Travel to a vein leaves the hand free; swinging owns it; a combat use owns it at due ticks; incidental work is pose-only.
5. **Combat segment adapter.** Aggregate plan output must expose segment deltas, validity, resource phases, and receipts. Push geometry remains contingent until model support exists.
6. **Deadline and loss observation.** Loot currently has no age/despawn deadline; enemy forecasts are tick snapshots; light and ore are durable but player relevance expires. These clocks need explicit evidence.
7. **Instrumentation.** Record course identity, prefix/frontier, suffix identities, repair trigger/class, rejected insert, cumulative resources, deadline slack, predicted delta, receipt, and planning work. Without this, “busy” can hide another loop.
8. **Test state space.** Fixtures need mutation rows for overlap, capacity, deadline, in-flight effect, repair footprint, unknown reach, and budget cut; live jungle/region play remains the acceptance layer.

This is a large architectural addition. Most cost lies in truthful producers and execution/resource contracts, not the route-order data structure.

### Runtime and memory cost

- Full permutation is `O(n!)` and rejected.
- A travel/setup matrix is `O(n^2)` time and memory unless computed lazily.
- One best-position insertion is `O(n)` with truly local edge costs. Re-evaluating every insertion under cumulative resources/deadlines/marginal effects is commonly `O(n^2)` per arriving opportunity.
- A light placement, kill, pickup, cargo transfer, or player-intent boundary can invalidate a suffix. With dependency indexing the repair radius is the affected suffix; in the worst case it is all nodes.
- Moving enemies can produce updates every tick. Treating every forecast shift as a repair event destroys the benefit of incrementalism.
- Local combat search already consumes material budget. B must schedule around it, reuse its front, and stop before movement/finalization starve. It cannot assume the nominal 4 ms belongs entirely to course repair.

No B implementation was benchmarked. The factorial counts are analytic; the 4 ms and 12.4 ms figures are historical measurements of current combat only.

### Gameplay cost and future failure shapes

- **Conservatism:** confidence-bounded deadlines can reject clever long combos and make the companion look shortsighted.
- **Stale tails:** retained routes can look stubborn when an improvement event is not classified as relevant.
- **Repair storms:** broad relevance makes the body churn while the scheduler reports constant “improvement.”
- **False precision:** exact predicted durations invite brittle ordering around moving enemies and modded effects.
- **Semantic starvation:** sealed slimes may correctly wait forever; players may still read awareness without action as broken. Reporting must distinguish “outvalued” from forgotten.
- **Cluster fixation:** region summaries can overvalue many trivial effects and lead away from one high-impact opportunity.
- **Resource overconstraint:** coarse reservations can suppress compatible work; underconstraint can issue two arm uses or overflow cargo.
- **Contingent-effect disappointment:** a course that visually promises Eye-to-zombies before the first knockback lands may look erratic when observation forces repair. The UI should reveal only current intent, not an uncertain full graph.

## Strengths that survived the attack

- Concrete retained identity directly addresses the observed same-two-job oscillation without an incumbent percentage bonus.
- Explicit travel/setup and already-paid route make “pot/torch on the way to ore” and “two copper on the way home” natural rather than category exceptions.
- Time windows and cumulative dimensions are the right representation for expiring drops, player departure, cargo, mana, and consumables.
- An immutable prefix plus suffix repair fits irreversible game actions better than wholesale replanning.
- Region summaries give the requested place-level judgement while allowing concrete site-level execution.
- Repair can be spatial and causal, preserving the project's established rule that unrelated world edits should not restart work.
- B produces unusually strong observability: a schedule can name exactly what was kept, inserted, invalidated, or dropped and why.

## What to borrow and what to reject

**Borrow:** concrete identities; causal validity; effect receipts; region summaries; marginal prefix evaluation; explicit deadlines; cumulative resource dimensions; immutable execution frontier; bounded insertion/local swaps; retained executable course; tri-state pending candidates; combat's local nondominated front; movement's authoritative route/clearance contract.

**Reject:** static additive task value; full-order enumeration; fixed FIFO; activity-family nodes as the final grain; opaque fight-until-dead jobs; global per-tick combat simulation; value-change-as-repair; broad world revisions; job-level compatibility booleans; age bonuses sold as starvation fixes; “player time saved” as a magic scalar; any claim of optimality.

## Strongest competitor and its win condition

**C, numerical receding-horizon consequence planning, is B's strongest competitor when marginal cross-action effects dominate route reuse and a compact simulator can predict them cheaply.** It naturally represents bow knockback creating a later pierce, light changing subsequent coverage, and a new enemy changing the best mixed prefix. It also avoids pretending that independent task nodes have additive value.

C wins if a shared, calibrated simulator can generate and compare mixed prefixes within the real budget while always retaining a valid incumbent. Today that condition is false: combat simulation is already expensive, its forecast does not carry push displacement, lighting/loot/work do not share one transition model, and modded effects are only partly knowable.

**A, retained reactive utility, is the cheaper competitor when opportunities are independent, short, and volatile.** It wins when the schedule tail invalidates so often that route reuse does not repay the state and dependency machinery. B earns its place only where clusters, deadlines, cumulative resources, or route reuse are measured to improve completed effects.

The best combined architecture is therefore likely A-like admission/fallback, B for the retained spatial/temporal course, and C-like numerical consequence search inside local combat—not one global copy of all three.

## First falsifying experiments

1. **Same pair, stable course.** Plant one equal drop and torch at the two live Exact destinations. Hold both identities valid for 700 ticks. Fail if the running identity changes more than once before an effect/invalidity, or if neither effect occurs.
2. **Marginal lighting.** Plant seven legal sites with deliberately overlapping light footprints and three drops. Compare a static-additive mutation with prefix-marginal revaluation. Fail if a now-redundant site remains ahead of a still-useful site/drop or if the body recrosses a completed region.
3. **Capacity route.** Bag capacity three; two quantity-two drops plus one quantity-one drop. Fail if the planned prefix ever exceeds capacity, retries an impossible second pickup without capacity change, or hides a feasible ordering.
4. **Execution frontier.** Fire a slow projectile, then inject a better job before impact. Fail if repair removes the in-flight effect from predicted state or credits its later hit twice.
5. **Ore/combat segment seam.** Two-hit ore under the body; first an immortal zero-harm dummy, then a slime with harm arriving before two hits, then after two hits. Fail if the dummy owns the body, if the imminent slime is ignored, if the late slime interrupts, or if the ore identity is lost after the combat insert.
6. **Push contingency.** Eye above three zombies. First disable positional push prediction and require post-hit generation; then supply a calibrated push interval. Fail if the scheduler promises an unproved pierce, or if the calibrated case never exposes the line as a successor.
7. **Player-intent expiry.** Start a long surface fight, then replay a descent. Fail if a non-serving suffix survives after finishing-plus-return misses the separation constraint; pair with a one-hit enemy that should finish.
8. **Budget cut.** Interrupt discovery before its first expensive combat candidate on every other tick. Fail if current movement/effect stops, if Unknown becomes No, or if a rotating family/site is never examined.
9. **Repair radius.** Apply terrain/light/cargo/enemy changes outside and inside recorded sensitivity footprints. Fail if outside changes rewrite the course or inside changes leave an infeasible suffix.
10. **Planner starvation versus semantic starvation.** Inject one genuinely expiring valuable opportunity per tick for a finite burst, then stop. Fail if the old valid job was never prepared during the burst due to compute, or does not become executable after the burst when still best. Do not fail merely because correct deadline policy postponed it.
11. **Region selection.** Left single slime+drop; right enemy+dark site; straight four dark+two pots+enemy. Fail if global refinement spends the whole budget before choosing an executable direction, if unreachable straight wins, or if straight becomes an obligation after the player leaves.
12. **Live acceptance.** Instrument a jungle play with course/frontier/repair receipts. The headless rows above cannot establish that mixed play looks competent, follows into the cave, or avoids perfect-shot idling.

## Probe commands and outcomes

All probes were small analytic checks; none is a Terraria benchmark.

```sh
python3 -c 'import math; print({n: math.factorial(n) for n in (5,7,10,15)})'
# {5: 120, 7: 5040, 10: 3628800, 15: 1307674368000}

python3 -c 'a={1,2,3}; b={1,2}; c={4,5}; print("static A+B",len(a)+len(b),"actual",len(a|b)); print("static A+C",len(a)+len(c),"actual",len(a|c))'
# static A+B 5 actual 3
# static A+C 5 actual 5

python3 -c 'capacity=3; drops=[2,2]; print("each_fits_initially",[q<=capacity for q in drops],"route_total",sum(drops),"route_feasible",sum(drops)<=capacity)'
# each_fits_initially [True, True] route_total 4 route_feasible False

python3 -c 'old_done=False; trace=[]
for tick in range(20):
 trace.append((tick,"new-expiring" if 2>1 else "old"))
 if trace[-1][1]=="old": old_done=True
print("old_done",old_done,"trace_tail",trace[-5:])'
# old_done False trace_tail [(15, 'new-expiring'), ..., (19, 'new-expiring')]

git status --short
# M research/proposal/04 Plan a Course of Action and Follow It.md
# ?? ten pre-existing Tools/Ledger/runs/c2eac88-*.jsonl files
```

The overlap probe refutes static additive coverage. The capacity probe refutes independent feasibility. The starvation probe shows that deadlines/optimal insertion alone do not promise fairness; it does not establish what the Terraria reward should be. The factorial probe confirms the exact search count represented by the current recursive permutation code; it does not measure runtime.

## External evidence and limits

- Google's [OR-Tools routing overview](https://developers.google.com/optimization/routing) documents the combinatorial growth directly (10 locations: 362,880 orders; 20: about 2.43e18), and treats capacities, time windows, resource constraints, and dropped visits as distinct routing concerns. It also states that general routing is intractable and solver results may be good rather than optimal. This supports rejecting exhaustive schedule search and unsupported optimality; it does not benchmark Terraria or prescribe OR-Tools as a dependency.
- Google's [routing dimensions guide](https://developers.google.com/optimization/routing/dimensions) models cumulative travel time and load along a route. This supports treating cargo and time as ordered cumulative state, rather than checking each pickup independently. It is an analogy to the representation, not evidence that a vehicle-routing solver matches a game tick loop.
- Gallagher, Zimmerman, and Smith, [“Incremental Scheduling to Maximize Quality in a Dynamic Environment” (ICAPS 2006)](https://cdn.aaai.org/ICAPS/2006/ICAPS06-023.pdf), explicitly studies continually arriving activities, deadlines/lost opportunities, resources, sequence-dependent setup, and the quality/cost/stability tradeoff between incremental and regenerative scheduling. Its domain assumes an explicit priority/quality function and mostly exclusive resources; it therefore demonstrates the need for a declared value model rather than solving this project's cross-category policy.
- Chien et al., [“Robust Planning with Imperfect Models” (AAAI Technical Report, 2001)](https://cdn.aaai.org/Symposia/Spring/2001/SS-01-06/SS01-06-017.pdf), describes iterative repair of resource/state/temporal/dependency conflicts, bounded repair, continuous updates, and prohibition on changing executed past activities. This supports the immutable frontier and constraint-repair split. Its spacecraft domains change much more slowly and have authored models; applicability to 60 Hz Terraria remains unmeasured.
- Chien et al., [“Using Iterative Repair to Automate Planning and Scheduling of Shuttle Payload Operations” (IAAI 1999)](https://cdn.aaai.org/IAAI/1999/IAAI99-115.pdf), requires explicit activity duration/subactivities/temporal constraints and distinguishes state, concurrency, depletable, non-depletable, and simple resources. This supports phase/resource modelling and also exposes its cost: “resource compatible” is not one boolean. The paper does not validate this project's model completeness or real-time budget.

## Final judgement

Approach B should advance only as a **bounded, event-repaired course layer with concrete identities, region summaries, cumulative resources, an immutable execution frontier, and segment-level local-combat options**. It should retain the existing tri-state/fallback, movement, native execution, and effect-accounting contracts. It should be rejected as a whole-brain opaque task queue, as a static route over additive values, and as a global exhaustive mixed-action planner.

The win condition is measurable: compared with retained reactive utility, B must complete more causally verified effects with less recrossing and no worse live intent-following, while planning remains bounded and current action never stops for refinement. Until the segment API, objective policy, repair predicates, and cumulative state exist on paper and survive the falsifiers above, implementation would be premature.

