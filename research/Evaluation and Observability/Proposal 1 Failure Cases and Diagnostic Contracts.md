# Proposal 1 failure cases and how to distinguish their causes

This is a prospective failure analysis dated 12 September 2026, following the owner's elevation-and-ore drawing and two-enemy example. It adds acceptance cases to [Proposal 1](<../proposal/01 Preserve Utility and Repair Activity Contracts.md>); it does not implement them or claim that every listed failure has been observed. The inspected source baseline is `3049a89`, with the same gameplay source as the prior research baseline. The owner's drawing is a reported symptom, not a captured Terraria geometry or a proven reproduction.

There are 64 cases: ten for top-level family selection, three for each of three families, and five for each of nine proposed activities. The owner mentioned both three and five per activity; the larger count is used. This is a structured first coverage set, not an exhaustive list of every possible Terraria failure. Each row names a plausible first owner, a general mechanism and the evidence that could distinguish it. Ownership is a hypothesis to test; a visual symptom alone cannot identify its cause.

## The health bar shows the family and the actual activity

Accepted display requirement: the family appears to the left of the top health bar and the activity to the right. Examples are Gathering / Mining, Gathering / Chopping, Combat / Hunting, Combat / Guarding and Combat / Surviving. The assistance family includes lighting, collection, keeping company and local exploration. “Wandering” was the owner's example label for that family; this document retains Nearby assistance as its architectural name. Player-facing wording can be shorter without creating another family.

Both labels must come from one coherent activity snapshot. Selected activity, execution phase and temporary control owner are distinct: a suspended mining activity must not masquerade as active mining during an escape or downed state. A compact phase such as “Mining · approaching” or “Mining · blocked” can preserve the distinction; lifecycle overrides such as Downed or Catching up need an explicit display rule, not a fictitious fourth utility family. The exact visual treatment is an implementation/design checkpoint. The normal HUD stays readable; raw scores, reason codes and query budgets belong in the inspector.

The native offscreen UI check should exercise long labels, viewport/UI scales, dragging, rapid transitions, suspended work, recovery and downing. Verify both geometry and snapshot consistency. A screenshot of a resting Gathering / Mining label cannot establish any of those transitions.

The older Slate planning item AIC-138, read on 12 September 2026, specifies job on the left and weapon/ore/tree detail on the right. The owner's newer family-left/activity-right requirement supersedes that text. The board remains unchanged under this research-only scope; reconcile that existing item before implementing the HUD rather than creating a competing item or following its obsolete label semantics.

## In simple language, the proposed flow is

1. The companion observes you, itself, enemies, terrain, nearby work and its current abilities.
2. Gathering, Combat and safety, and Nearby assistance each consider concrete things they could do.
3. Each family offers its best available activity, including the intended result and what is known about carrying it out.
4. The top-level utility chooses among those offers, selecting the activity together with its family.
5. Positioning and movement find and execute an approach that should enable that activity.
6. The activity uses the permitted tools or interactions; compatible shooting remains independent.
7. The companion checks actual results, updates the relevant evidence and reconsiders when progress, danger, your movement or the world changes.

## In simple language, the current flow is

1. The companion observes you, itself, enemies, terrain and nearby opportunities.
2. An active distant-return recovery or immediate avoidance can take control before ordinary behaviour selection.
3. Otherwise one utility chooser compares the current individual behaviours; some scoring also discovers or updates work.
4. The winning behaviour operates its local work logic and asks for a kind of position, or asks to hold.
5. Position selection chooses a place, and shared movement tries to get the body there or execute its local escape.
6. The independent arsenal chooses and fires an available shot when the hand is permitted; work tools and light use the same hand boundary.
7. Existing job state, movement checks and recordings track parts of the outcome, but do not consistently distinguish activity value, usable arrival, productive work and control interruption.

This is a simplified flow, not a claim that every branch calls every stage. The current coordinator has recovery, reflex and survival paths. Mining already has a retained vein, nearest-in-reach selection, range/visibility tests and limited interaction-jump handling. Current source does not lack all reach or progress awareness.

## In simple language, the change is

Families organise the possibilities. Concrete offers prevent a family winning with no useful activity. Purpose-specific position checks ask whether the body can actually do the intended thing from where it will arrive. Activity-owned results separate progress from attempts. Shared companionship and safety affect every activity. A failure updates the approach, target or permission that failed; it does not automatically become evidence that the whole family was a bad choice.

Useful work can remain desirable after a failed attempt, while becoming temporarily unavailable because no usable method is currently known. Those statements can both be true. Retaining value must not force infinite retries; withdrawing an unusable offer must not teach a blanket dislike of mining.

## The elevation sketch requires a geometry investigation, not just a timer

The owner reports ore visible beside a raised lip, the companion on the lip's left side in mining mode, no productive mining, and successful mining after the player removes the lip. That intervention changes visibility, standing positions, collision geometry and possibly route evidence simultaneously. It supports investigating the approach, but does not isolate A*, reach, occlusion or target selection.

Current source already implements relevant pieces. [OreFinder.cs](<../../Companion/Brain/WorldInteractions/Mining/OreFinder.cs>) ranks initial accepted ore by distance from the companion. Its `InReach` uses horizontal/vertical range and exposed-face visibility; touching ore is unnecessary. `Approach` searches standable positions within that range, tests additional horizontal slack and favours distance from the work point to the ore rather than the cheapest whole excursion. [MineAction.cs](<../../Companion/Brain/Behaviours/Work/MineAction.cs>) first chooses the closest currently in-reach tile in `NextInPatch`, then considers interaction jumps and relocation; those later loops return the first accepted candidate rather than jointly ranking every tile/approach. `TileMiner.Swing` reports invocation of the native operation, not proof of productive damage. These are source observations; none proves the sketch's historical cause.

The desired general contract is a **tile-and-body-position pair**: a particular ore tile, an actual body pose or interaction window from which the permitted tool can affect it, and a route or local movement that can deliver that pose. The approach should use the full effective tool reach wherever valid and avoid unnecessary closeness. Arrival slack must not deliver the body outside the valid interaction region. A static standing point is insufficient where a timed jump is the useful method.

“Closest ore” means a preference for nearby usable work. Absolute nearest-only selection would keep choosing a blocked tile while a slightly farther exposed tile is mineable. Evaluate the current body position first; if it can work, avoid unnecessary travel. Otherwise compare valid nearby tile/pose pairs by access effort, risk, remaining work and reunion cost, with distance as a useful preference or tie-breaker. Retain a partly worked tile when continuing is worthwhile, rather than retargeting on tiny distance changes. Occlusion and the closed edit policy still apply: full reach is not permission to mine through arbitrary walls or remove non-ore terrain.

The future reproduction needs paired native scenes: raised lip and lip removed, plus mirrored geometry, different effective reach, a blocked nearest tile with an exposed farther one, and a jump-only interaction. Record the original signature: selected mining, target identity, actual pose, no productive effect. Then hold the activity while changing only the validated target/pose; finally hold a native-valid pose while exercising the real interaction. If changing the pose solves it, repair positioning; if a valid pose still fails, inspect reach/permission/tool integration; if the body cannot reach the pose, inspect movement. A progress alarm triggers this distinction and never substitutes for it.

## Two enemies require separate movement and firing decisions

For a hidden enemy at 5% health and a visible enemy at 100%, immediate firing and movement answer different questions. The arsenal evaluates the shots available from the current muzzle. Hunting, guarding and survival evaluate whether a different position would be useful and safe. A lack of a current shot at the nearly defeated enemy does not prohibit worthwhile repositioning; its low health does not require chasing it.

If a short safe move creates a finishing shot, pursuit may offer it while the arsenal shoots the visible enemy during travel. If the other enemy threatens either actor, protection or safety may determine movement while firing addresses the best currently legal opportunity. If the hidden enemy requires an expensive, unsafe or unreturnable excursion, leave it. Killing the healthy enemy first is a possible outcome, never a mandatory sequence. Reconsider as either enemy moves or dies.

Keep the existing arsenal algorithms for this restructuring. Share their feasible-attack evidence through the existing boundary, and keep movement-purpose target, aiming target and actual hit target distinct. Damage to the visible enemy is useful combat output; it must not automatically reset an unsuccessful pursuit of the hidden enemy. Prediction and actual contact remain separate. Health percentage alone is insufficient: absolute remaining health, weapon effect, attack timing, threat, route and reunion all affect the trade-off.

## A small set of shared contracts addresses many cases

The remedies below repeat because the same mechanism should cover several activities. They are not 64 special-case branches.

- **Concrete, comparable offers:** eligibility, uncertainty, predicted benefit, remaining cost and relevant shared factors travel with target and purpose identity. Scoring does not secretly advance the work. Maximum-child aggregation is the initial baseline; duplicated candidates and reordered evaluation must not change the winner when all facts and tie rules are held constant.
- **Purpose-valid physical methods:** interaction eligibility, position and actual-entry movement use consistent current capabilities. A route, an arrival and a productive result remain separately reported. Different tools have genuine different native operations; the common contract does not pretend they are identical.
- **Causal outcome ownership:** separate attempted, executed, partial, complete, invalidated, interrupted and abandoned. Preserve target/attempt lineage. Credit output to its observed cause or mark attribution unknown. Failure changes only the evidence it actually refutes.
- **Shared consequences and companionship:** current threat, harm, time apart, remaining work and practical reunion inform every candidate once. Estimates expose uncertainty. Safety includes a viable aftermath, not merely avoiding the next contact.
- **Explicit resources and controls:** one owner grants movement and incompatible hand use. Cooldown gaps remain part of coherent tool work. Reflex, recovery and lifecycle paths use the same admission/accounting.
- **Bounded, revisable knowledge:** query exhaustion is not physical impossibility. Keep unresolved coverage and fair refinement; invalidate relevant caches on world/ability changes. A failed approach is scoped evidence, not an eternal target ban.
- **One event record for all views:** health bar, inspector, recorder and reports consume identified decision/activity/outcome events. Displays never create another scoring or world-query pipeline.

These are proposal contracts, not proof of sufficiency. Prefer implementing the smallest shared boundary needed by the observed failing cases. If a common executor is repeatedly necessary across unrelated activities, compare Path 2; if accurate immediate values still miss valuable enabling sequences, compare Path 3. A family-specific workaround is justified only by an actual difference in the activity's semantics, not by the example that exposed the bug.

## The failure matrix maps symptoms to discriminating evidence

For every row, the prospective test is to create the stated condition, verify that its evidence distinguishes the first failed contract, then change the relevant condition and check that the response changes without a scenario-name branch. Run paired conditions against the same baseline and keep held-out variants. “Looks like this” is not a reproduction; the activity, failure signature and actual outcome must match.

### Top-level family selection

| Case | Failure or misleading success | First owner to investigate | General remedy proposed for Path 1 | Evidence that separates it |
|---|---|---|---|---|
| T01 | Combat wins although it offered no activity. | Parent admission | Select the offered child identity; an empty family cannot win. | Submitted offers and selected ID on the same decision. |
| T02 | Mining loses because an interrupted route was called failed work. | Outcome attribution | Separate work value, approach feasibility and control interruption; revise only the evidence invalidated. | Activity, attempt and control-owner IDs with the first failed condition. |
| T03 | Combat always wins merely because enemies exist. | Threat valuation | Compare effective harm and useful intervention against concrete work, rather than enemy count. | Threat arrival, effective damage and family value components. |
| T04 | Gathering always wins even while the player is in serious danger. | Cross-family calibration | Use a common value meaning and shared player/self risk; no unconditional family ladder. | Paired scenes differing only in consequential threat. |
| T05 | The companion endlessly changes families on almost equal values. | Continuation | Use remaining cost, real switching cost and stable ties; retain identity through irrelevant noise. | Winner margin, switching cost, input change and activity duration. |
| T06 | Almost-finished work never yields even as reunion becomes impossible. | Companionship | Compare remaining benefit with current and projected reunion cost; past effort is not an unlimited bonus. | Remaining effort, time apart, player motion and return evidence. |
| T07 | One family wins because it has more children. | Aggregation | Begin with maximum eligible child, not sum or child count; test duplicate candidates. | Same winner after duplicating an identical offer. |
| T08 | Assistance never wins because gathering consumes the query budget. | Computation scheduling | Retain unresolved candidates and fairly refine candidates that could change the result; report starvation. | Per-family query spend, deferred ages and coverage. |
| T09 | A previously good offer wins using stale world or ability evidence. | Freshness | Bind relevant dependency revisions to offers and revalidate before use. | Terrain, body, tool, target and capability revisions. |
| T10 | No optional job is offered, and it freezes while the player leaves. | Fallback and lifecycle | Keep-company supplies actual reunion movement; a safe hold or permitted recovery is explicit when movement is unavailable. | Fallback reason, movement request and recovery admission. |

### Gathering family selection

| Case | Failure or misleading success | First owner to investigate | General remedy proposed for Path 1 | Evidence that separates it |
|---|---|---|---|---|
| G01 | A nearby unmineable ore masks a usable tree. | Eligibility | Compare eligible concrete work before proximity; keep unresolved work separate. | Rejected tile power/permission versus offered tree. |
| G02 | A remembered vein hides a newly worthwhile tree. | Local utility | Retain a job without locking the family to it; compare remaining value across candidates. | Incumbent and alternative values on identical facts. |
| G03 | Mining and chopping count distance or progress differently. | Shared valuation | Use shared time, reunion and consequence meanings with activity-specific outcome evidence. | Factor provenance and units across both children. |

### Combat and safety family selection

| Case | Failure or misleading success | First owner to investigate | General remedy proposed for Path 1 | Evidence that separates it |
|---|---|---|---|---|
| C01 | Guarding sends it through a zombie to reach the player. | Intervention positioning | Value an achievable intervention and the safety of its approach, rather than proximity to the player alone. | Approach harm, time to help and alternative firing positions. |
| C02 | Kiting away from an enemy prevents surfacing. | Safety assessment and control | Compare combined hazards through a viable terminal state; seek safety owns the escape purpose. | Breath forecast, controls and sustained head clearance. |
| C03 | A 5%-health hidden enemy monopolises pursuit while another is dangerous. | Pursuit valuation | Compare cost to create a shot and harm removed; immediate arsenal may shoot a different legal target. | Movement-purpose target, shot target and relevance of each outcome. |

### Nearby assistance family selection

| Case | Failure or misleading success | First owner to investigate | General remedy proposed for Path 1 | Evidence that separates it |
|---|---|---|---|---|
| A01 | Fixed torch-first ordering ignores an urgent reunion. | Local utility | Compare lighting, collection and companionship by current benefit and excursion cost. | All local offers and shared reunion factor. |
| A02 | The same drop earns separate reward as incidental pickup and collection. | Opportunity ownership | Deduplicate the physical benefit; value an incidental action by its additional cost and benefit. | Item generation and credited transfer IDs. |
| A03 | An endless series of tiny diversions prevents useful exploration or reunion. | Journey accounting | Keep cumulative separation across child and family changes; changing labels cannot reset cost. | Journey context, detour time and updated alternatives. |

### Mining activity

| Case | Failure or misleading success | First owner to investigate | General remedy proposed for Path 1 | Evidence that separates it |
|---|---|---|---|---|
| M01 | Arrived beside the drawn elevation, no ore is damaged, labelled mining. | Purpose geometry | Evaluate tile and usable body pose together with effective reach, permitted exposed access and arrival tolerance; try another valid pair. | Actual pose, selected tile/face, reach/occlusion results, destination and tool effect. |
| M02 | Closest tile is blocked, while another tile in the vein is usable. | Tile selection | Prefer nearby usable ore across the reachable vein surface; jointly compare access cost instead of absolute nearest-only or collection iteration order. | Examined tile/pose pairs with distance, exclusions and unexamined coverage. |
| M03 | A swing is reported as progress although the tile takes no damage. | Interaction outcome | Separate attempt from attributable native damage or destruction; keep unsupported effects unknown. | Pre/post tile identity, native damage evidence, permission and tool power. |
| M04 | A ceiling hop repeatedly occurs after the valid swing window has passed. | Movement/tool timing | Prove and execute a body trajectory with an interaction window and safe landing, using current ability/cooldown state. | Predicted and actual pose at tool use, launch state and landing. |
| M05 | The vein shrinks because tiles were pruned or the player broke them, reported as companion success. | Work accounting | Separate remaining work, removed-from-scope work and actor-attributed production; partial completion is explicit. | Tile-set changes by reason and observed producer, with unknown attribution retained. |

### Wood chopping activity

| Case | Failure or misleading success | First owner to investigate | General remedy proposed for Path 1 | Evidence that separates it |
|---|---|---|---|---|
| W01 | It approaches a tree from a side the axe cannot reach. | Purpose geometry | Use the same tile/pose/tool feasibility contract as mining with tree-specific native interaction. | Trunk target, actual axe reach, approach and effect. |
| W02 | The player switches trees and both crowd the same trunk. | Cooperation observation | Re-evaluate the player's current work and working space; a separate tree is a preference, not a global reservation system. | Player tool contact, trunk identity and chosen alternatives. |
| W03 | A tree falls externally but the companion claims successful chopping. | Outcome attribution | Distinguish job no longer needed from attributable tool production and later collection. | Tree-generation changes, tool events and pickup identities. |
| W04 | A nearly completed tree survives despite a newly dangerous return. | Remaining-value comparison | Re-evaluate remaining time and reunion/safety cost; do not make completion compulsory. | Remaining-effort estimate and changed threat/return evidence. |
| W05 | Policy or protection changes after approach, yet a swing still occurs. | Permission lifecycle | Revalidate edit permission at the actual mutation and release tool ownership if invalidated. | Policy/protection revision and mutation admission. |

### Hunting activity

| Case | Failure or misleading success | First owner to investigate | General remedy proposed for Path 1 | Evidence that separates it |
|---|---|---|---|---|
| H01 | It reaches a spot that cannot produce the intended shot. | Purpose positioning | Share the arsenal's feasible-attack query with position validation; distinguish sampled absence from exhaustive absence. | Candidate shot tests, live muzzle and arrival pose. |
| H02 | Shooting another enemy indefinitely renews a failed pursuit. | Progress attribution | Credit incidental damage as useful combat output without crediting the pursued target's progress. | Pursuit target, shot target, projected hit and actual hit generations. |
| H03 | Target changes repeatedly reset its stall history. | Attempt lifecycle | Keep unsuccessful approach evidence across target switching when the same obstruction or attempt persists. | Activity/attempt lineage and relevant input changes. |
| H04 | An almost-dead enemy escapes and pursuit carries it far from the player. | Remaining-value comparison | Compare achievable finishing benefit with chase, risk and reunion cost; health fraction alone is insufficient. | Absolute health, predicted damage, route cost and player context. |
| H05 | Target becomes invulnerable or its NPC slot is reused, yet hunt remains valid. | Target identity | Bind generation and damageability to opportunity evidence; preserve hazard sensing independently. | Generation, attack eligibility and invalidation event. |

### Guarding activity

| Case | Failure or misleading success | First owner to investigate | General remedy proposed for Path 1 | Evidence that separates it |
|---|---|---|---|---|
| P01 | It arrives near the player but never helps against the threat. | Purpose definition | Evaluate a position enabling useful intervention; player proximity alone is not success. | Relevant threat, intervention opportunity and actual result. |
| P02 | It picks the closest threat although another will hit sooner. | Threat forecast | Compare estimated consequence and time to intervene, exposing uncertainty for unknown scripts. | Arrival estimates, target victim and effective damage. |
| P03 | The chosen intervention would take longer than the threat has to arrive. | Feasibility and timing | Include movement, cooldown and projectile travel in help estimates; consider useful alternative responses. | Estimated intervention versus threat arrival with actual timing. |
| P04 | Guarding persists after the player has removed the threat. | Freshness | Retain identity through noise but withdraw when the purpose has gone; reconsider other work. | Threat-generation termination and next offered activities. |
| P05 | No damage occurred, so it claims protection without evidence it caused that. | Outcome interpretation | Separate predicted harm avoided from measured damage and observable intervention; do not assert the counterfactual. | Threat movement, actions, actual harm and labelled estimates. |

### Survival activity

| Case | Failure or misleading success | First owner to investigate | General remedy proposed for Path 1 | Evidence that separates it |
|---|---|---|---|---|
| S01 | One projectile dodge lands in deeper water or lava. | Terminal safety | Evaluate combined consequences through a viable next state, not just immediate separation. | Hazard exposure, resources and stable terminal state. |
| S02 | It briefly surfaces and reports escape before falling underwater again. | Completion | Require sustained usable safety appropriate to the escape, rather than one favourable frame. | Head clearance over time, footing, breath and re-entry. |
| S03 | The safest static spot prevents all useful activity indefinitely. | Utility and objective scope | Value being alive and useful; accept contextual low harm without rewarding endless harmless retreat. | Risk estimates, viable alternatives and time without useful work. |
| S04 | An escape spends the jump needed to leave its landing place. | Capability/resource model | Validate the resulting resource state and continuation/return options. | Abilities before/after each move and successor feasibility. |
| S05 | Reflex interrupts the planned escape and the escape is blamed for failure. | Control coordination | Assess the proposed movement, record pre-emption, and resume or replace only after revalidation. | Requested/granted controls and interrupted attempt outcome. |

### Lighting activity

| Case | Failure or misleading success | First owner to investigate | General remedy proposed for Path 1 | Evidence that separates it |
|---|---|---|---|---|
| L01 | Unsampled darkness is treated as a measured dark area. | Observation coverage | Carry known/unknown lighting coverage with freshness; investigate only within safe bounds. | Sample coverage, light source context and candidate revision. |
| L02 | It reaches a torch site but native placement is refused. | Purpose and permission | Validate supply, native placement and protection at use; distinguish approach from placement. | Placement predicate results, supply source and actual tile change. |
| L03 | Its held torch makes the area look permanently solved. | Benefit measurement | Separate transient held illumination from persistent useful placed light. | Light source identity and coverage before/after departure. |
| L04 | Two neighbouring placements earn the same lighting benefit. | Marginal valuation | Value additional useful coverage and refresh after world/player lighting changes. | Coverage overlap and realised additional benefit. |
| L05 | A short lighting detour ends below an unreturnable ledge. | Return feasibility | Apply the same outward/return contract and resource accounting as gathering. | Directed route evidence and remaining abilities. |

### Collection activity

| Case | Failure or misleading success | First owner to investigate | General remedy proposed for Path 1 | Evidence that separates it |
|---|---|---|---|---|
| I01 | It touches a drop but the full bag receives nothing, reported complete. | Transfer outcome | Measure actual accepted quantity, not distance or contact. | Item generation, before/after quantity and destination capacity. |
| I02 | A drop moves or is collected by the player while its old location stays targeted. | Identity and freshness | Revalidate moving item generation and remaining quantity before travel and transfer. | Position revision, despawn/transfer reason and updated offer. |
| I03 | Ore falls into a pit and collection inherits mining's reach proof. | Separate purpose feasibility | Price and prove the collection excursion independently; mining reach says nothing about return from the drop. | Distinct mining and collection offers and routes. |
| I04 | It collects part of a stack and reports the whole goal done. | Partial outcomes | Record transferred and remaining amounts with explicit partial completion. | Requested, accepted and outstanding quantities. |
| I05 | Incidental pickup during travel is ignored, causing a redundant return trip. | Shared observations | All actual transfer paths update the same opportunity state and benefit accounting. | Contact pickup event linked to item and existing collection goal. |

### Keeping company activity

| Case | Failure or misleading success | First owner to investigate | General remedy proposed for Path 1 | Evidence that separates it |
|---|---|---|---|---|
| K01 | It stands near the player horizontally but on an unusable different floor. | Purpose geometry | Use practical companionship and accessible reunion, including vertical geometry. | Current objective region, line connection and route evidence. |
| K02 | A momentary turn makes it race away in the wrong direction. | Player intent observation | Use short motion/activity context with uncertainty; avoid treating each velocity change as a new destination. | Observed motion window and inferred intent confidence. |
| K03 | Required detour initially moves away and is called a stall. | Movement progress | Use valid route/attempt progress rather than only Euclidean closeness. | Route-step advancement and actual body progress. |
| K04 | Removing follow also removes the trigger for last-resort return. | Lifecycle admission | Connect permitted recovery to explicit reunion need; work cannot invoke flight to its resource. | Reunion purpose, recovery reason and no route-memory learning. |
| K05 | Already separated, it starts fresh jobs repeatedly and never catches up. | Shared companionship | Carry cumulative time apart through all activity changes and compare remaining work against reunion. | Separation history and rejected/accepted detour values. |

### Local exploration activity

| Case | Failure or misleading success | First owner to investigate | General remedy proposed for Path 1 | Evidence that separates it |
|---|---|---|---|---|
| X01 | It loops over the same familiar patch and calls that exploration. | Observation and progress | Represent coverage gained separately from walking and directed route experience. | Newly observed region versus repeated traversal. |
| X02 | It enters an unreturnable area because outward movement is possible. | Return feasibility | Require safe useful prefixes with current exit/continuation evidence. | Outward and return statuses, body resources and uncertainty. |
| X03 | A changing world invalidates the remembered visited area or route. | Memory dependencies | Separate seen-before from currently traversable; invalidate changed geometry without erasing all history. | Observation age, terrain fingerprint and current route proof. |
| X04 | Wandering wins while useful work was never examined. | Candidate scheduling | Report unexamined opportunities and refine promising candidates within shared budgets. | Candidate coverage and age, not only evaluated winners. |
| X05 | It keeps exploring after the player begins sustained travel. | Shared companionship | Re-evaluate exploration gain against updated reunion cost, retaining accumulated separation. | Player context, exploration gain and selected return movement. |

## The recorder distinguishes prediction, execution and effect

Extend the existing recorder, rather than add a separate failure logger. A decision snapshot carries decision ID, world/capability dependencies, evaluated family/child offers, factor provenance, unknown coverage and the selected offer. The continuing activity carries purpose ID, target generation, phase, remaining-work estimate with its basis, observed progress, and whether it is running or suspended. Each movement or interaction attempt carries its own ID and expected success condition.

Movement evidence includes actual starting pose/velocity/resources, selected working region, search stop reason, outward/return status, predicted versus delivered terminal state and requested/granted controls. Interaction evidence includes target tile/item/NPC identity, range and occlusion results, native permission/tool admission, actual invocation and observed effect. Combat records movement-purpose, aiming and hit identities separately. Shared context includes player motion evidence, time apart, predicted reunion cost and estimate uncertainty. No unsupported damage attribution or hypothetical prevented hit becomes a measured fact.

A failed condition should emit a transition event with context, not a new identical failure every frame. Keep a bounded pre/post-event window for rare failures, including local terrain and relevant configuration. Record dropped events, buffer limits, sampling cadence and stale snapshots. Bound alternative retention; retain the strongest evaluated alternatives plus rejection-category counts and unexamined coverage. The absence of a retained candidate is not evidence that it was evaluated and rejected. Record observer cost separately and compare recording on/off before trusting frame-time claims.

The God's-eye view should let the reader select an unproductive interval and see: what was offered; what won; what position was requested; what conditions made it usable; which controls actually ran; and what changed in the world. For the lip case, draw the actual body, effective reach region, selected ore/face, intended working position, actual stopped position and the failed predicate. For combat, draw distinct pursuit and firing targets. Display hypothetical validators and replayed alternatives as retrospective evidence, never as what the old brain knew.

Add prospective invariant checks where the contradiction is objective: selected offer absent; parent/child identity mismatch; incompatible simultaneous hand grants; stale evidence used after an invalidating revision; declared successful arrival outside the activity's success region; claimed transfer without quantity; and unrelated target damage credited as pursuit progress. Some failures are only detectable retrospectively: an unlucky enemy action does not prove the preceding estimate irrational. Diagnostics can detect contradictions before a bad action and explain failed predictions afterwards; they cannot predict every unfamiliar enemy script.

## Bundle the work into Proposal 1 through existing experiments

| Existing experiment | Added cases and implementation obligation | Prediction and branch if it fails |
|---|---|---|
| E01–E02: observer and causal separation | All cases' IDs; lip scene and separate combat targets; repair analyser semantics before comparing runs. | Evidence identifies a first failed contract. If it cannot, improve capture rather than change utility weights. |
| E03: remaining value | T05–T06, G02, W04, H04, K05; pair just-started and almost-finished work while the player leaves. | Cheap completion survives where reunion remains practical; worsening separation still interrupts. If not, separate bad remaining-time estimates from bad comparison. |
| E04: family offers | T01, T03–T04, T07–T10 and all nine family cases. | Same candidates and maximum-child aggregation preserve the flat winner; empty offers cannot win. If not, inspect double factors, ordering side effects and identity mismatch. |
| E05–E06: control and danger | C01–C03, P01–P05, S01–S05, coherent tools and temporary display phases. | Useful safety/intervention survives handoffs. If a dodge harms escape, inspect terminal consequences and granted control rather than invent enemy-specific priority. |
| E07–E10: position, uncertainty and native motion | M01–M04, W01, H01, L02/L05, I03, K01/K03, X02. | A claimed usable destination enables the real activity from actual entry. If only an externally supplied valid position works, repair candidate coverage; if that also fails, inspect native execution. |
| E11–E12: memory and computation | T08–T09, H03/H05, X03/X04, stale targets and query starvation. | Relevant changes reopen candidates without erasing useful evidence or monopolising ticks. If compute dominates, measure query allocation and retention before choosing another search algorithm. |
| E13–E14: opportunism and companionship | A01–A03, L03/L04, I05, K02/K04/K05, X01/X05. | Incidental work adds benefit without unbounded detours. If accurate immediate comparisons miss enabling sequences, compare bounded planning on those cases. |
| E15–E16: compatibility and held-out acceptance | M05, W02/W03/W05, I01/I02/I04; all cases across altered terrain, capabilities and relevant native/mod integrations; HUD transition checks. | Claims remain scoped to actual actors, permissions and outcomes. New native incompatibility belongs at its integration boundary; an attractive HUD is not proof of gameplay correctness. |

Acceptance counts and performance ceilings must be chosen before implementation measurements, using existing fixture behaviour and the actual frame-cost budget. No percentage gain is predicted here. Keep each row's failure reproducible, count how many variants ran and expose cases not exercised. After two genuine failed repairs of the same signature, re-examine the first incorrect contract.

## Evidence and limits

Primary local sources for this follow-up are the owner's reported drawing and behavioural requirements, the inspected current source linked above, [the coordinator](<../../Companion/Brain/CoordinateBrainTick.cs>), [the current weapon boundary](<../../Companion/Weapons/CLAUDE.md>), and the previously sourced [decision/control audit](<../Implementation Evidence/Decisions, Activities and Shared Controls.md>) and [physical-execution audit](<../Implementation Evidence/Routes, Returnability and Physical Execution.md>). Existing [experiments](<Experiments and Recorder Requirements.md>) and the [acceptance matrix](<Behavioural Acceptance Matrix.md>) remain the wider test programme.

This follow-up is engineering failure analysis grounded in those inputs, not a new external literature review or a live test. The previous research's dated external sources support the architectural alternatives within their stated limits. Neither a generic remedy in a table nor a passing documentation review proves the proposed gameplay improvement. The exact native lip reproduction, new instrumentation, proposed HUD and all new behavioural tests remain unimplemented.
