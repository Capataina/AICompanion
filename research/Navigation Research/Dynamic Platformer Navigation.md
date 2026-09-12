# Dynamic platformer navigation: what changes when a route is also a body

## Questions this investigation resolves

This report addresses questions 65–80 and 90 of [the research agenda](../Questions for the Next Architecture Investigation.md): what each search result can mean; the state a platformer route must retain; incremental, real-time and experience-based alternatives; safe partial progress; and the boundary where a graph claim meets the actual Terraria NPC.

The easy answer was: *replace the current A* with D* Lite or a platformer motion planner and moving goals, changed terrain and unknown passages become handled.* The evidence falsifies that as a general prescription. D* Lite repairs shortest paths over a supplied finite directed graph with changed edge costs. A platformer route becomes that object only after the system defines state, generates directed transitions, validates their physical executability, and states how unknown observations and resource depletion change them. None of those jobs is an A* substitution.

The useful separation is:

```text
observed Terraria world + capability/resource state
        │                         │
        ├── representation ───────┤   state equivalence; directed edges; costs
        │                         │
        ├── search ───────────────┤   A*, incremental repair, anytime or local search
        │                         │
        ├── transition proof ─────┤   native collision/control from actual entry state
        │                         │
        └── execution policy ─────┘   retain, interrupt, replan, or refuse/return
```

An optimal result at one layer does not certify the next. This is especially material here: the present coarse `NavNode` is a tile plus mobility resource state while full pose and velocity are held by local simulation; the repository has already identified that two bodies at one tile can need different controls.[^repo-map]

## The claims and their limits

| Claim | Evidence and exact scope | What it does **not** establish for this companion | Confidence |
|---|---|---|---|
| A* finds an optimal path only relative to its finite graph, edge costs and admissible/consistent heuristic assumptions. | LaValle separates discrete search from planning under differential constraints; the latter needs state containing dynamics and trajectory generation.[^lavalle] | A graph node at a tile represents every velocity, support, liquid and control history that can reach it. | High |
| LPA* reuses prior work when a fixed-start/fixed-goal graph has local cost/topology changes; its first search is A*-like. | Koenig, Likhachev and Furcy say it repeatedly solves paths as edges change or vertices are inserted/deleted, and report advantage when changes are small and near the goal.[^lpa] | Reuse is useful when a player goal moves often, when every tick produces unrelated changes, or when the transition generator itself changes meaning. | High |
| D* Lite is goal-to-current-state LPA* for changing directed graphs; it requires predecessor and successor enumeration, edge-cost updates and a graph meaning that remains stable across repair. | Its construction reverses the LPA* search direction, explicitly requires predecessors and successors, and models unknown terrain as initially traversable grid edges whose costs become infinity when observed blocked.[^dstarlite] | A discovered collision mismatch can be encoded merely as a changed cost instead of revising pose/resource state or re-proving a transition. | High |
| Weighted/anytime search trades optimality for a bounded solution-quality factor only on the represented graph. | ARA* starts with an inflated heuristic and repairs toward better solutions while retaining a bound; AD* combines that with graph-change repair.[^ara][^adstar] | A returned route is physically executable or safe under current breath, damage, hostile pressure or a changed body. | High |
| Real-time search can make useful moves before a full plan, but this is a policy that learns/updates estimates under time limits, not proof that a partial route is safe. | Real-Time Adaptive A* updates local heuristic values; LSS-LRTA* can beat D* Lite under time pressure or when heuristics are not misleading.[^rtaa][^lss] | Budget exhaustion means no path exists, or that advancing toward the current best frontier is safe or returnable. | High |
| Platformer planning must often use a phase state rather than a tile graph: position, velocity/fall state, time, discrete jump availability and action duration can change the successor set. | Tremblay et al. define a state including 2D position, time, vertical fall velocity, double-jump state and horizontal mode, then simulate each action for a frame.[^icanjump] | The particular simplified physics in that study transfers to Terraria, which has slopes, platforms, liquids, doors, NPC collision and abilities. | High for the abstraction; low for direct transfer |
| High-level local skills can reduce lookahead, but their plan quality depends on the success estimator and on state aliasing at a waypoint. | Dann et al. use learned local skills plus Dijkstra over negative log success; their later steps assume arrival at a tile centre with zero velocity and explicitly call that a crude approximation.[^svp] | A coarse waypoint is a safe state boundary, or a probability estimate is a native proof. | High |
| Reused routes/experience can aid repeatable cases but become a source of bias and invalidation work in changed worlds. | Phillips’ Experience Graphs reuse paths, add lazy/incremental variants, and show an on-the-fly validation variant failing when an old heuristic keeps pulling search into newly blocked space.[^egraphs] | An executed traversal remains valid after terrain, capability, body state or endpoint changes. | High |

## What the major families actually require

| Family | Mathematical object and guarantee | Runtime prerequisites beyond current A* | Where it can help | Failure mode that a swap cannot fix |
|---|---|---|---|---|
| A* | A shortest path in a known weighted graph under its heuristic assumptions. | Canonical state key; successor generation; cost and termination semantics. | A stable, bounded local standing-location graph. | Missing jump, door, platform or slope transition; node merges incompatible arrival velocities. |
| Weighted A* / ARA* | A graph path with bounded suboptimality relative to the chosen weight and represented costs.[^ara] | A declared quality bound, a route usable before refinement, and instrumentation that distinguishes first-route latency from final quality. | When a valid ordinary route is needed quickly and a later improvement is optional. | A cheap but non-executable route looks preferable; a budget ending before any route is still `unknown`, not failed. |
| LPA* | Repairs paths after graph edge/vertex changes for the same start/goal series.[^lpa] | Stable vertex identities; changed-edge journal; `g`/`rhs` consistency and invalidation discipline. | Repeated feasibility queries to a retained goal while only a few local edges change. | Moving companion origin or player-derived goal can discard much of the reuse; generated edges changing from new body facts are not a small cost patch by default. |
| D* Lite / AD* | D* Lite repairs goal-distance estimates as the *current start* moves; AD* additionally returns increasingly good bounded graph paths.[^dstarlite][^adstar] | Directed predecessor enumeration, goal-rooted distance state, edge-change notifications, `km` start-motion maintenance, and a way to update every affected predecessor. | A persistent chosen destination through incrementally revealed traversability changes. | It does not decide which changing player position is the goal, choose a safe prefix, or model one-way fall edges as reversible. |
| Real-time adaptive / LSS-LRTA* | Interleaves bounded lookahead and action; learns heuristic values to reduce repeated scrubbing.[^rtaa][^lss] | A defined action horizon, state revisit/learning policy, a fallback action and a safety admission rule independent of heuristic learning. | Responsive pursuit on an untrusted/partially observed map when a bounded move is known safe. | A greedy frontier action can enter a one-way drop; learned estimates can be stale after terrain or ability changes. |
| Hierarchical/waypoint graph | An abstraction whose macro edges stand for local traversals. No generic physical guarantee follows. | Entry/exit state contract per macro edge, refinement/repair mechanism, and an abstraction-error trace. | Long, familiar corridors and region-level returnability queries. | “Platform A to platform B” hides run-up, take-off velocity, platform phase and landing support. |
| Experience graph / archive | Biases search toward previously successful paths; completeness/optimality depends on the base planner and evaluation policy.[^egraphs] | Fingerprint of terrain, ability/resource/body conditions, directed entry/exit state, evidence of native execution, expiry/invalidation and bounded memory. | Repeatedly returning through already traversed local caves. | An archive records a former success, not a universal edge; undirected reuse silently invents a return path. |
| Motion-primitives / kinodynamic search | Searches simulated control sequences over a state that includes dynamic variables. | Calibrated native transition oracle or explicitly bounded model error; state discretisation; primitive set; collision over the entire trajectory. | Jumps, dashes, airborne steering, liquid exits and short escape/control sequences. | A portable simulator is only a model; model success is not Terraria success without parity/native validation. |
| Receding-horizon local trajectory search / MPC | Optimises a finite horizon, replans from new observation; safety guarantees require an invariant/terminal set, not merely a horizon cost. | A terminal safe/returnable set, bounded model error, feasibility monitor and collision/resource envelope. | Immediate clearance, landing correction and survival escape while longer search continues. | “Make local progress” can drive into a dead end or exhaust breath before a return exists. |

## Directedness and state are the main platformer problem

The platformer literature gives a concrete counterexample to a tile-only graph. Tremblay et al. model a state as position, time, fall velocity, a double-jump bit and horizontal mode; actions run for a frame through a simulator.[^icanjump] Their representation admits moving platforms because time is state. It also makes clear why a single tile cannot be one node when arrival speed, airborne status or remaining jump changes what follows.

For this project, a transition should be treated as an evidence-bearing directed object:

```text
Entry equivalence class
  = support/contact + exact local pose bucket + velocity/run-up class
    + liquid/breath class + abilities/resources + relevant terrain fingerprint

Directed transition
  = controls/primitive + predicted envelope + required permissions
    + native validation result + actual terminal class + failure classification
```

The entry class need not be a globally fine discretisation. A useful hybrid can keep global search coarse *provided that* it records what local controller contract the macro edge requires, validates the live entry state before taking it, and refuses/replans when that contract does not hold. That is a conditional engineering claim, not a theorem: it needs matched native cases that show a coarse route plus local proof keeps routes that full-phase global search finds and does not admit a route the native NPC cannot execute.

Falls, drops and many liquid transitions are directed. A returnability query therefore cannot be an undirected connectivity test. It needs either (a) a directed path from the proposed terminal state back to an acceptable region, including current resource consumption, or (b) a product rule permitting the one-way edge based on evidence the player has taken it. The latter is permission policy, not physical proof; it must stay labelled as such.

## Unknown terrain, safe prefixes and the meaning of `unknown`

D* Lite's benchmark world is deliberately narrower than the companion's: an eight-connected grid, adjacent-cell observation, unit-cost movement, and unknown cells assumed traversable until observed blocked.[^dstarlite] It is evidence that incremental repair can reduce graph work under those assumptions. It is not evidence that optimistic entry to an unobserved pit is safe.

The safe-motion literature makes the missing distinction explicit. Janson, Hu and Pavone formulate planning with initially unknown obstacles and line-of-sight revelation; their policies guarantee no collision by constraining motion relative to what can be safely stopped/handled under current information.[^safeunknown] Viability theory similarly calls a state viable only when some control evolution can remain inside constraints indefinitely.[^viability] These are stronger contracts than “the search has not disproved the path.” They require a dynamics model, a sensing boundary and an admissible/safe set.

The platformer translation is not to compute a global viability kernel every tick. It is to make an explicit three-way result part of every feasibility API:

| Result | Meaning | Permitted decision | Required evidence |
|---|---|---|---|
| `proven` | A route/transition has met the selected native or model proof contract. | Admit the job/edge under that contract. | Route identity, current capability/resource assumptions, validation result and terminating state. |
| `unknown` | Search budget, observation, model fidelity or candidate generation was insufficient to decide. | Continue only along a separately proven safe prefix; otherwise defer. | Stop reason: time/node cap, unobserved boundary, no generated candidate, stale model, or interrupted query. |
| `disproven-now` | Under stated current assumptions, a required route/edge/return fails. | Reject now; retain a qualified failure record. | Counterexample state plus terrain/capability/resource fingerprint. |

`unknown` must never be collapsed into `disproven`, because finite expansion can miss a route, and it must never be collapsed into `proven`, because absence of a counterexample is not a control contract. The repository's own agenda already calls for this separation; literature supports why the distinction remains necessary under incremental and real-time search.[^repo-map]

For a useful safe prefix, require every prefix terminal state to pass a cheaper invariant than “full trip proven”: for example, stable support or controlled flight plus a known local brake/landing/return transition under the *currently observed terrain, body, resource and sensing assumptions*. This is conditional safety, never an absolute claim about unobserved terrain, hostile intervention or later capability changes. If the terminal assertion cannot be checked in native state, call it a *model-safe prefix*, not a safe prefix. This is the right boundary for local trajectory/MPC-style search: it can choose controls inside an invariant envelope while global search is unfinished, but it cannot manufacture a return guarantee from a finite local horizon.

Return status deserves its own dimension. `route-proven` can mean that a directed path to the selected work/position exists under its transition contract; it does not by itself mean `return-proven`. A result such as `return-unknown` may still permit waiting or a separately qualified safe prefix, but it must not silently inherit a known one-way permission, a budget timeout, or an optimistic graph edge as a verified return path. Conversely, a policy may permit a known one-way traversal for product reasons while preserving the fact that no physical return proof exists.

## What D* Lite would require here, beyond replacing a call to A*

The following is an implementation prerequisite map, not a proposal to implement it.

```text
Current route model
  ├─ canonical directed state keys
  │    └─ decide what is intentionally abstracted; detect unsafe aliasing
  ├─ exact successor AND predecessor enumeration
  │    └─ directed drops/jumps/doors must expose reverse predecessor relations
  ├─ monotone terrain/capability revision stream
  │    └─ identify every transition whose cost/existence changed
  ├─ persistent g/rhs/open state rooted at a defined goal set
  │    └─ reset or repair when the goal definition changes
  ├─ current-start movement accounting (D* Lite's key modifier)
  ├─ route extraction with a live-entry validation boundary
  └─ a result protocol: repaired route / no graph route / unknown due to work

Native movement truth
  ├─ transition generator proves or refutes a primitive from actual entry
  ├─ execution reports landing, collision, interruption or resource failure
  └─ invalidation distinguishes changed terrain from bad abstraction/model
```

D* Lite itself assumes it can identify changed directed edge costs and update affected states; it searches backwards from goal and asks for both successors and predecessors.[^dstarlite] A Terraria route generator that learns a jump failed because an actual run-up state differed has not merely received a new edge cost. It has found an abstraction or transition-contract failure. Repairing D* Lite's queue without revising the transition's entry class would efficiently preserve the false claim.

Moving goals are a second choice, not an automatic D* Lite fit. A following destination is often a *region defined by player position and comfort*, not one fixed goal vertex. Replacing a goal set every tick can discard the reuse that makes incremental search attractive. One separating experiment should compare: a stable retained work goal; a player goal translated by a small number of nodes; and a player goal that changes side/height/region. Measure queue repairs, fresh expansions, route retention, first-control latency and native arrival quality; do not infer the result from algorithm names.

## Production platformer evidence, including its counterexamples

The 2009 Mario benchmark gave A*-based forward simulation a strong but qualified result. The published competition account says the two leading A* controllers cleared every competition level, then asks whether that showed a final answer or a benchmark that omitted original-game challenges; it identifies dead ends as a salient omitted challenge.[^mario-benchmark] That is a direct warning against treating an A* victory on rightward progress as evidence for companion returnability.

Baumgarten's released agent stores a copied world state and action in each search node and performs the search in `AStarSimulator`, rather than navigating a tile adjacency graph.[^baumgarten] Its strength was a forward model whose simulated velocity changes subsequent possibilities. Its transfer limit is equally direct: it optimises an observed Mario window and a competition progression objective, while this companion must choose whether to leave the player, preserve returnability and honour native Terraria collision and abilities.

Dann, Zambetta and Thangarajah challenge the opposite simplification: full low-level forward search was not their preferred answer in maze-like Infinite Mario. They say granular search limited real-time lookahead and a direct-goal heuristic could be led astray; their skill abstraction outperformed their low-level baseline in that task.[^svp] But the paper also publishes the assumption that exposes the risk for this repository: later macro steps are estimated as beginning at a tile centre with zero velocity.[^svp] The work is evidence for a hybrid candidate, not for trusting waypoint state aliasing.

Tremblay et al. make the state-cost explicit and useful for diagnostics: their offline platformer tool searches a simulator-defined state containing time, velocity and discrete jump/motion modes, and compares RRT, A* and MCTS.[^icanjump] It is evidence that a motion search must make these variables visible. It is not runtime evidence: their horizontal air-control model allows instantaneous changes and deliberately omits horizontal momentum, unlike an NPC whose controls are interpreted through Terraria.

## Conditional experiments that would separate the choices

No experiment should compare “algorithm A versus algorithm B” across different graphs or body models. Hold observed terrain, capability state, player trace and start body state fixed; record graph result and native result separately.

| Experiment | Competing explanation it separates | Inputs and instrument | Pass condition | What each outcome means |
|---|---|---|---|---|
| Graph sufficiency matrix | Misses come from absent/aliased transitions, not search effort. | Existing native fixtures plus fixtures for run-up-sensitive jump, slope/platform, door, liquid exit, drop/return and changed ability. Record native existence, graph edge existence, and selected entry state. | Every native-executable required traversal is either represented with a compatible entry contract or explicitly marked outside the graph contract. | Missing graph edge or unsafe state merge defeats an A*/D* comparison; represented-but-unfound cases justify search comparison. |
| Budget ladder | Current `no route` is actually resource exhaustion. | Same graph/model, fixed destinations; run escalating node/time budgets and retained-tick continuation. Record termination reason, expansions, frontier best state and a route if one exists. | Outcomes remain `unknown` until exhaustion under a declared complete finite graph; never report impossibility from a cap. | More budget discovering a native-successful route supports search-work diagnosis; no graph route supports representation diagnosis only. |
| Incremental-repair locality | D* Lite/LPA* amortise the actual pattern of changes. | Replay fixed start/goal cases with one changed edge near/far from goal, moving origin, moving goal region and many unrelated terrain revisions. Compare fresh A*, LPA*, D* Lite on identical directed graph. | Report expansions/queue operations, first valid route time and route cost; verify equality to fresh A* when an exact comparison applies. | Improvement only for stable goals/local changes is the expected conditional result; universal gain would be surprising and needs inspection. |
| Entry-state refinement | Coarse route plus local proof is sufficient. | For each coarse macro edge, replay diverse actual entry velocities/support/liquid/resources; compare coarse prediction, portable local proof and native result. | No admitted edge may native-fail without its precondition becoming observable and logged. | Failures clustered by state dimension say what the state contract must include; scattered failures implicate model/parity or execution. |
| Safe-prefix excursion | Local progress can coexist with return safety in unknown space. | Start at a known lip with hidden/changed geometry; require every executed prefix to end in a native-validated recoverable state or stop. Track breath/health/resources and return query. | Zero unplanned irreversible entries across the fixture set; classify deferred exploration separately from completed expedition. | A better frontier advance that loses returnability rejects the policy; conservative no-progress identifies an information/ability gap, not a search failure. |
| Route-memory replay | Executed experience helps without fossilising old assumptions. | Re-run an archived directed transition unchanged, with changed terrain, different approach state and changed capability. | Same-fingerprint native replay may accelerate; mismatch must revalidate or invalidate before control. | Success only in same fingerprints supports bounded cache reuse; false reuse means fingerprint/entry contract is too weak. |

The first two experiments are prerequisites for any algorithm replacement. They identify whether the current bottleneck is graph representation, bounded search work, controller execution, or native/portable disagreement. Only the third begins to test D* Lite versus A*.

## Design implications kept deliberately conditional

1. **Keep a coarse global route only if it becomes a contract rather than a claim.** It should name the local state conditions a step needs and accept a native execution report. This preserves the practical authoring/debugging advantages of tile/region graphs while treating motion primitives as the authority for jumps, dashes, landing and liquid exits.

2. **Use incremental graph repair only for a persistent graph question.** A retained work destination, stable directed state identities and sparse observable edge revisions meet the literature's premise. A newly selected player-comfort region every tick, changed ability semantics, or a discovered state aliasing defect does not. The experiment above can establish which traffic exists before maintaining `g/rhs` machinery.

3. **Represent route memory as qualified evidence, not a shortcut that says “this place is reachable.”** The archive key needs direction, terrain revision/fingerprint, capability/resource preconditions and native execution outcome. Physical failure invalidates the relevant transition; a changed context asks again. This follows the experience-graph lesson without importing its robotic assumptions wholesale.[^egraphs]

4. **Make `unknown` a productive result.** A bounded query can retain its frontier and may advance only through a separately demonstrated safe prefix. It must carry why it stopped. This avoids both pathological rediscovery and the false statement that a budget cap proved a cave impassable.

5. **Do not promise universal platformer movement.** Published successes either use a forward model, simplifications, learned local skills, fixed observation/goal conditions, or offline computation. Terraria's slopes, half-blocks, platforms, doors, fluids, breath, NPC body and changing mastery abilities create combinations these papers did not measure. Native fixture coverage is therefore the source of a game-specific guarantee; source inspection can only establish the algorithmic contract.

## Source ledger

All technical sources below are primary papers, author-hosted publications or the released implementation. Accessed 12 September 2026. Quotes in this report are limited to short phrases; all other use is paraphrase.

[^repo-map]: **Repository primary record.** *Architecture and Behaviour Map*, “A* needs a separate investigation into its graph and execution contract”, and *Questions for the Next Architecture Investigation*, questions 65–80 and 90. Current HEAD `d60b92b`, accessed 12 September 2026. It establishes the project’s current model boundary, not external algorithm performance.

[^lavalle]: Steven M. LaValle, *Planning Algorithms* (2006), especially Chapters 2, 11–14, author edition updated 14 August 2020. [Book and chapter index](https://lavalle.pl/planning/web.html); [Chapter 14 PDF](https://lavalle.pl/planning/ch14.pdf). It distinguishes discrete planning from planning under differential constraints and lists phase-space, reachability and motion primitives; it does not measure Terraria.

[^lpa]: Sven Koenig, Maxim Likhachev and David Furcy, “Lifelong Planning A*,” *Artificial Intelligence* 155(1–2), May 2004, pp. 93–146. [Author publication page](https://publications.ri.cmu.edu/lifelong-planning-a). Relevant abstract/overview; it reports experiments only in its selected domains and says the advantage is conditional on small, goal-near changes.

[^dstarlite]: Sven Koenig and Maxim Likhachev, “D* Lite,” AAAI 2002. [Author-hosted PDF](https://www.cs.cmu.edu/afs/cs/Web/People/motionplanning/papers/sbp_papers/integrated3/koenig_dstarlite_aaai02b.pdf), pp. 1, 4–7. Relevant assumptions: finite state space, predecessor/successor access, goal-directed eight-connected unknown grid, and changed edge costs. The reported 50-random-terrain results are not a platformer evaluation.

[^ara]: Maxim Likhachev, Geoff Gordon and Sebastian Thrun, “ARA*: Anytime A* with Provable Bounds on Sub-Optimality,” NIPS 2003 / CMU-CS-03-148 technical report. [Author publication index](https://www.cs.cmu.edu/~ggordon/). The author describes a first suboptimal plan followed by repairs while time remains; applicability is to the defined graph cost.

[^adstar]: Maxim Likhachev, Dave Ferguson, Geoff Gordon, Anthony Stentz and Sebastian Thrun, “Anytime Dynamic A*: An Anytime, Replanning Algorithm,” ICAPS 2005. [Author-hosted PDF](https://www.cs.cmu.edu/~ggordon/likhachev-etal.anytime-dstar.pdf). Abstract and algorithm sections combine bounded-suboptimal planning with incremental repair; the evaluation includes a simulated arm and dynamic path-planning applications, not platformer collision.

[^rtaa]: Sven Koenig and Maxim Likhachev, “Real-Time Adaptive A*,” AAMAS 2006, pp. 281–288. [Author publication page](https://publications.ri.cmu.edu/real-time-adaptive-a). The paper measures trajectory cost under fixed per-episode limits in unknown game terrain; it is not a safety proof.

[^lss]: Sven Koenig and Xiaoxun Sun, “Comparing Real-Time and Incremental Heuristic Search for Real-Time Situated Agents,” *Autonomous Agents and Multi-Agent Systems* 18(3), 2009, pp. 313–341. [Author abstract and paper link](https://idm-lab.org/bib/abstracts/Koen09a.html). It reports LSS-LRTA* beating D* Lite under time pressure or non-misleading supplied heuristics, conditions that must be measured here rather than presumed.

[^icanjump]: Jonathan Tremblay, Alexander Borodovski and Clark Verbrugge, “I Can Jump! Exploring Search Algorithms for Simulating Platformer Players,” *Experimental Artificial Intelligence in Games: Papers from the 2014 AIIDE Workshop*, AAAI Technical Report WS-14-16, pp. 58–64. [Official PDF](https://cdn.aaai.org/ojs/12744/12744-52-16261-1-2-20201228.pdf), especially pp. 59–60; DOI 10.1609/aiide.v10i3.12744. The state/physics model is explicit, including a stated simplification of instantaneous horizontal air control. Publication identity was checked against the official paper during independent review.

[^svp]: Michael Dann, Fabio Zambetta and John Thangarajah, “Real-Time Navigation in Classical Platform Games via Skill Reuse,” IJCAI 2017, pp. 1582–1588. [Paper page](https://www.ijcai.org/proceedings/2017/219); [PDF](https://www.ijcai.org/proceedings/2017/0219.pdf), pp. 1582–1585. It evaluates Infinite Mario on maze-like levels; its macro plan’s zero-velocity/tile-centre later-step approximation is explicitly disclosed.

[^egraphs]: Mike Phillips, *Experience Graphs: Leveraging Experience in Planning*, CMU-RI-TR-15-10, May 2015. [Thesis PDF](https://publications.ri.cmu.edu/storage/publications/2018/01/Experience-Graphs_-Leveraging-Experience-in-Planning-compressed.pdf), pp. 4–7, 107–118. The dynamic-environment case documents that an on-the-fly validation variant can fail when an old heuristic remains misleading after an obstacle change.

[^safeunknown]: Lucas Janson, Tommy Hu and Marco Pavone, “Safe Motion Planning in Unknown Environments: Optimality Benchmarks and Tractable Policies,” 2018. [arXiv record](https://arxiv.org/abs/1804.05804). The claim used here is the paper’s stated problem: safe motion with line-of-sight incremental revelation; its concrete guarantees are for its vehicle/sensor formulation, not platformer jumps.

[^viability]: Mohamed Amine Bouguerra, Thierry Fraichard and Mohamed Fezari, “Safe Motion using Viability Kernels,” IEEE ICRA 2015, pp. 3259–3264, DOI [10.1109/ICRA.2015.7139648](https://doi.org/10.1109/ICRA.2015.7139648). [Author institution deposit](https://inria.hal.science/hal-01143861v1). Used for the viability distinction, not for a Terraria safety guarantee. Independent review corrected the original draft's erroneous author attribution; access to the institution page was intermittently blocked, so this report does not claim a fresh full-text verification of its algorithm.

[^mario-benchmark]: Julian Togelius et al., “The Mario AI Benchmark and Competitions,” *IEEE Transactions on Computational Intelligence and AI in Games* 2010. [Open paper record](https://pure.itu.dk/en/publications/the-mario-ai-benchmark-and-competitions/). The cited comparison and dead-end caveat appear in the paper’s 2009 competition discussion. Its scores measure benchmark progression, not safe round-trip navigation.

[^baumgarten]: Robin Baumgarten, “A* Mario AI v1.1,” released source, [archive README pinned at `3bff21316e46324aca194f50e088ec03f304e987`](https://github.com/jumoel/mario-astar-robinbaumgarten/blob/3bff21316e46324aca194f50e088ec03f304e987/Readme.txt), revision 20 February 2018, accessed 12 September 2026. README identifies `SearchNode` as holding action and copied world state and `AStarSimulator` as containing search. The repository is a two-commit archived clone of the 2009 agent, so it is implementation evidence, not a controlled evaluation.

## Gaps that remain gaps

* No primary study retrieved here evaluates D* Lite, LPA*, ARA*/AD*, Experience Graphs or viability methods against Terraria NPC collision, tModLoader timing, liquid breath, hostile interrupts or this project’s no-teleport and player-derived one-way permission policy. The separating experiments above are required for those claims.
* The exact current route-search/caching implementation, current telemetry populations and prior failed implementations belong to the parent’s code and historical evidence lanes. This report deliberately does not infer them from external literature.
* Community/maintainer experience was outside this technical-primary-source lane. No room claim, date or score is used as evidence here.
* The viability source supports terminology only here; full-text access was unreliable, and no implementation recommendation depends on reproducing its particular algorithm.
