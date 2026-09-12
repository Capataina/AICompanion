# RimWorld work scheduling, incidental work, and coexistence

**Accessed 2026-09-12.** This is a source-grounded case study, not a claim that RimWorld's architecture should be imported into AICompanion. RimWorld is a grid colony game with discrete pawn jobs, shared reservations and an inventory/stockpile economy; AICompanion is one real-time NPC alongside one player. Those differences limit every transfer below.

## Questions, starting hypothesis, and falsifier

The working hypothesis was that safe opportunism needs an explicit interruption/continuation contract and that coexisting schedulers need named ownership. I searched for the countercase: a scheduler that safely injects work without preserving the interrupted job or distinguishing ownership. The public implementations instead repeatedly queue the original job, reject recursive insertion, or patch the same job-selection surface. That supports the hypothesis only within this small case set; it does not establish a universal necessity.

Questions resolved here are: what is actually selected (priority, work giver, job, target, or path); how an incidental task is bounded; who owns continuation; whether a concrete detour calculation exists; and what real compatibility reports say. I deliberately exclude closed vanilla algorithms and performance claims without a measurement.

## Baseline: what the sources can and cannot establish

RimWorld's proprietary base source was not inspected. The mod source exposes its public integration shape: `Pawn_JobTracker.StartJob`, `EndCurrentJob`, job queues, `WorkGiver` scanners, work priorities, targets and reservations. It is defensible to describe a pawn as having a current job with engine-managed target/reservation semantics. It is **not** defensible from this lane to state the exact vanilla global job-scoring, pathfinding, or reservation algorithm.

| Concern | Source-supported baseline | Unestablished here |
|---|---|---|
| Current work | Mods patch job start/end or issue work-giver jobs. | Exact vanilla selection ordering. |
| Contention | PUAH calls `CanReserve`; Common Sense relies on jobs/queues. | Full engine reservation invariant. |
| Continuation | WYU and Common Sense preserve/requeue a prior job. | That old work remains useful after interruption. |
| Path cost | Common Sense optionally asks the native path finder. | Vanilla's path model and cost function. |

## Pick Up And Haul: carry-state batching with reservations and recovery

At commit [`09f1fac96e70cc2a93d647615a5abd37734a3eba`](https://github.com/Mehni/PickUpAndHaul/tree/09f1fac96e70cc2a93d647615a5abd37734a3eba), [`WorkGiver_HaulToInventory.cs`](https://github.com/Mehni/PickUpAndHaul/blob/09f1fac96e70cc2a93d647615a5abd37734a3eba/Source/PickUpAndHaul/WorkGiver_HaulToInventory.cs) first filters spawned, reservable and non-forbidden objects, orders candidate haulables by squared pawn distance, then creates a hauling job with queued extra targets. It sets `SEARCH_FOR_OTHERS_RANGE_FRACTION = 0.5`; a store-target distance determines a search radius with a floor of 12 tiles. This is a concrete bounded batching rule, but it is not a general route planner.

[`JobDriver_HaulToInventory.cs`](https://github.com/Mehni/PickUpAndHaul/blob/09f1fac96e70cc2a93d647615a5abd37734a3eba/Source/PickUpAndHaul/JobDriver_HaulToInventory.cs) reserves both primary and queued targets before executing, loads queued objects until capacity, then scans locally around the pawn for more objects only when their reservations succeed. It subsequently selects delivery/unload work. [`PawnUnloadChecker.cs`](https://github.com/Mehni/PickUpAndHaul/blob/09f1fac96e70cc2a93d647615a5abd37734a3eba/Source/PickUpAndHaul/PawnUnloadChecker.cs) requests unloading at a 90% capacity condition or forced/cargo conditions, protects deteriorating goods, and checks an inventory-index mismatch every 50 ticks before clearing it and requesting recovery unloading.

The code therefore supports a narrow, transferable claim: a system retaining multiple nearby opportunities needs durable cargo identity and a divergence recovery path. It does not show that a capacity-driven detour improves player companionship. The maintainer calls unloading “not 100% perfect” in the project material, and [issue #31 (2020-02-26)](https://github.com/Mehni/PickUpAndHaul/issues/31) is one report of pawns failing to haul. Those are failure shapes, not prevalence or measured effect.

## Common Sense: a real detour test and explicit queue ownership

At commit [`03c1cf622fdd020be19489aba224abceb992c9d4`](https://github.com/catgirlfighter/RimWorld_CommonSense/tree/03c1cf622fdd020be19489aba224abceb992c9d4), [`OpportunisticTasks.cs`](https://github.com/catgirlfighter/RimWorld_CommonSense/blob/03c1cf622fdd020be19489aba224abceb992c9d4/Source/CommonSense13/CommonSense/OpportunisticTasks.cs) Harmony-patches `Pawn_JobTracker.StartJob`. On a qualifying non-queued, non-player-forced job, it can construct a cleaning job before the original. It rejects incapable/downed pawns, respects `allowOpportunisticPrefix` / `CleanOnOpportunity`, and only inserts work for configured job categories.

Its `Cleaning_Opportunity` is the strongest public detour algorithm found in this lane. With full paths enabled it gets three native path costs: current pawn to intended target (`stot`), pawn to original building (`stob`), and building to intended target (`btot`). It rejects the clean detour when the building is distant (`stob > 500`) and `stot / (stob + btot) < 0.7`; with the cheaper geometric mode the corresponding gates are `stob > 10` and the same ratio. This means the code avoids a particular shortcut shape, rather than simply cleaning what is spatially close. The exact thresholds are Common Sense/RimWorld tuning, not a Terraria recommendation.

When an opportunity exists, the code conditionally `EnqueueFirst(newJob)` and always `EnqueueFirst(job)`, then prevents normal `StartJob` handling for that transition. The `!fromQueue && !newJob.playerForced` checks guard the cleaning `else if`, not every opportunistic insertion: the preceding `DoBill` hauling-opportunity branch bypasses those two checks. In `EndCurrentJob`, certain post-food/post-tending cleaning jobs require a successful job and an empty queue. This supports explicit continuation and re-entry rules for those paths, while the hauling exception prevents a claim that the mod universally excludes queued or player-forced work.

[`Utility.cs`](https://github.com/catgirlfighter/RimWorld_CommonSense/blob/03c1cf622fdd020be19489aba224abceb992c9d4/Source/CommonSense13/CommonSense/Utility.cs) obtains the vanilla `CleanFilth` work giver and calls its `HasJobOnThing` predicate when building the filth queue. It then applies a greedy nearest-next ordering. This supports an API-boundary lesson: reuse the authoritative validity predicate when a mod ecosystem may replace it. It does **not** prove the greedy order is globally shortest or good in a dynamic cave.

## While You're Up: lineage and modern compatibility countercase

The original author describes incidental hauling without a substantial diversion, checked when a job starts. The pawn then attempts to return to its earlier work, whose relevance may have changed. [kevlou, *While You’re Up*, Ludeon forum, 2017, accessed 2026-09-12](https://ludeon.com/forums/index.php?topic=37503.0). This author account does not publish a formula, so no numerical detour threshold can be attributed to it.

The 2025 workshop item is a separate, temporary build: [*While You're Up (1.6 patch)*, posted 2025-07-04](https://steamcommunity.com/sharedfiles/filedetails/?id=3516529053) calls itself an “UNOFFICIAL 1.6 patched version,” links original workshop item `2034960453`, credits CodeOptimist, and says it was rebuilt from decompiled source because the linked GitHub source was out of date. This establishes a version/lineage boundary: the old 2017 account and modern patch cannot be treated as one implementation without checking a revision.

The patch maintainer writes that 1.6 makes it “less required” through opportunistic-hauling fixes but that the mod remains more aggressive. Its FAQ attributes standing idle to “10 jobs in 1 tick,” says the patch exaggerates rather than directly causes it, and says a rewrite may help; it identifies large mod packs and threading as unresolved. This is maintainer diagnosis, not a measured causal proof. Later user reports show a Harmony transpiler failing to match `WorkGiver_ConstructDeliverResources.ResourceDeliverJobFor`, including a reported conflict with Build From Inventory. Those reports establish a fragile patch point, not that the named other mod is necessarily at fault.

## While You Are Nearby: priority transformation, not pathfinding

On the author’s workshop comments for [pureMJ’s *While You Are Nearby*](https://steamcommunity.com/sharedfiles/filedetails/comments/2784585275), pureMJ explained on 2025-07-15, in Chinese, that nearby work is selected by changing work priorities rather than pathfinding. This is an English paraphrase of the maintainer’s description, not an independently verified implementation formula. Public source was not found in this pass, so its exact ranking, distance and job-selection mechanism remain unknown.

Two dated comments show why that absence matters. On 2025-07-26 the author advised users to set a “larger priority difference” or “smaller priority boost”; this confirms a configurable boost relationship but not its formula. On 2025-08-10, after a user reported that WYAN plus the updated WYU caused map generation to emit JobGiver errors until logging stopped, the author replied: “Good point. Crossed out WYU as a recommendation.” Each works alone according to that user report. Scores are unavailable in the retrieved Steam rendering. Treat this as a high-relevance compatibility incident, not proof of a universal incompatibility or performance rate.

Other reports describe growers switching to indoor cleaning after lunch, forced-work spreading under Achtung!, and autonomous work failure in some 1.6 configurations. These are useful countercases against calling a priority boost harmless. They lack controlled reproductions and scores in the retrieved rendering; no causal claim beyond the reporters’ observations is warranted.

## Free Will / YouDoYou: identity corrected and scalar scoring scoped

Search found the original [*YouDoYou* workshop comments, item `2019368012`](https://steamcommunity.com/sharedfiles/filedetails/comments/2019368012), where author `freemapa` says in 2021 that the AI does not understand Work Tab subtasks and recommends users choose either that micromanagement mod or YouDoYou. This is primary author evidence of an incompatibility boundary. The modern project calls itself Free Will and source evidence indicates it adds a free-will ideology/precept; third-party catalogue text says YouDoYou was its beta name, but that secondary statement alone cannot prove a complete code lineage. The safe conclusion is: **the author identity and product theme align, but exact revision equivalence between the 2021 YouDoYou workshop build and the current public repository was not established.**

The current public source at [`47ec7c9f3a05876b263c14f349a4c2ddda784701`](https://github.com/paul-freeman/rimworld-freewill/tree/47ec7c9f3a05876b263c14f349a4c2ddda784701) is a scalar heuristic system, contrary to the earlier name-based dismissal. [`Priority.cs`](https://github.com/paul-freeman/rimworld-freewill/blob/47ec7c9f3a05876b263c14f349a4c2ddda784701/Priority.cs) starts from a default scalar, invokes per-work strategies, uses hard `NeverDo`/`AlwaysDo` gates plus additions/multipliers, then quantises to RimWorld’s work-priority levels 0–4. It boosts the current work type in `ConsiderCompletingTask`. [`HaulingStrategy.cs`](https://github.com/paul-freeman/rimworld-freewill/blob/47ec7c9f3a05876b263c14f349a4c2ddda784701/Strategies/HaulingStrategy.cs) begins at 0.3 and combines food, deterioration, refuelling, health, carrying capacity, current task and colony policy.

This is utility-like **priority scoring over work types**, not evidence that it selects concrete targets/actions every tick. The distinction matters: a work-priority multiplier can coexist with a separate vanilla job/target selector. It supports testing explainable scalar considerations for AICompanion; it does not support replacing its movement/action commitment machinery with work-type priorities.

## What combines, what substitutes, and what would decide a transfer

| Mechanism | Governs | Combines with | True substitute? | Evidence that would favour it for AICompanion |
|---|---|---|---|---|
| PUAH queue/cargo ledger | Multiple already-accepted haul targets and unload recovery | A target selector plus bag implementation | No; it does not decide combat/work priority. | Telemetry shows repeated pickup churn or cargo-state divergence. |
| Common Sense detour + requeue | An incidental task before a discrete job | A preference/utility selector and native validity predicate | No; it assumes discrete queueable jobs. | Route evidence shows nearby drops/work can be inserted and revalidated without player-delay regressions. |
| WYU boundary insertion | One haul detour and attempted return | Common Sense-style revalidation, but collision likely | No; it lacks published current code/formula. | A low-rate, clearly bounded diversion improves successful pickup without recurring resume failure. |
| WYAN priority boost | Work ordering after a nearby-work preference | Only if it owns a distinct priority layer | Possibly at work-type level, not motion. | Same task has several valid sites and proximity is a demonstrated player preference. |
| Free Will scalar work priorities | Long-lived policy weighting | Target/motion execution below it | No. | Explainability and policy continuity matter more than urgent, target-specific response. |

**Transfer hypothesis:** retain only opportunities that are already reachable on an owned movement route, give them a bounded completion window, and revalidate the interrupted player-relevant purpose. **Countercase:** if drops are frequently discovered safely mid-route, a job-start-only gate loses useful assistance. A discriminating experiment must record accepted/rejected opportunity, detour time, interrupted-purpose validity, completion/cancellation reason, and player separation/danger; compare an already-on-route policy with a bag-capacity or raw-distance policy.

## Source ledger, confidence, and gaps

| Source | Date / retrieved | Tier and passage | Supports | Limit |
|---|---|---|---|---|
| PUAH source, commit `09f1fac` | revision current at retrieval / 2026-09-12 | Primary implementation; source calls `CanReserve` and queues targets. | Bounded multi-haul and recovery mechanism. | No effectiveness measurement. |
| Common Sense source, commit `03c1cf6` | revision current at retrieval / 2026-09-12 | Primary implementation; path ratio is visible in `Cleaning_Opportunity`. | Concrete detour/requeue/no-recursion behaviour. | One mod/version, RimWorld API assumptions. |
| kevlou forum | 2017 / 2026-09-12 | Primary author; “at the start of a job.” | WYU boundary/resume semantics. | No formula or current revision proof. |
| WYU 1.6 workshop | 2025-07-04, updated 2025-07-05 / 2026-09-12 | Primary patch maintainer; “UNOFFICIAL 1.6 patched version.” | Separate lineage and compatibility caveat. | Maintainer interpretation, not benchmark. |
| WYAN Steam comments | 2025-07-15 to 2025-08-10 / 2026-09-12 | Primary author plus unscored user incident. | Priority—not-pathfinding claim and real WYU conflict report. | Algorithm/source unavailable; reports uncontrolled. |
| Free Will source + YouDoYou comments | 2021 and current revision / 2026-09-12 | Primary author/source. | Scalar work priorities; Work Tab incompatibility. | Exact YouDoYou-to-current revision lineage unproved. |

Confidence is high for source-level mechanics, medium for author-described product intent, and low for population-level compatibility/performance effects. I looked in public GitHub repositories, original/modern workshop pages, Ludeon author forum posts and Steam comments. Missing: closed RimWorld base code, WYAN public implementation, verified original-WYU source revision, controlled performance/compatibility measurements, and scored community evidence. No consensus about an overall best scheduler can be inferred from those gaps.
