# Routes, returnability and physical execution

The baseline already retains search across ticks and validates movement from the actual body. Recommending either feature as wholly missing would repeat existing work. The unresolved questions concern what the graph can represent, what its searches actually establish, how those results are interpreted, and whether valid execution makes progress towards the activity's real purpose.

This source account is fixed to `d60b92b`. [Dynamic Platformer Navigation](<../Navigation Research/Dynamic Platformer Navigation.md>) compares the external algorithms; the [history](<../Historical Evidence/CLAUDE.md>) records why the present mechanisms exist. No new native execution experiment was conducted for this source study.

## There are several searches, with different meanings and budgets

| Search | State and purpose | Retention | Meaning of an incomplete result |
|---|---|---|---|
| `AStar.Find` through `WalkerReach` | Coarse tile/mobility route between snapped standing locations. Used for short reach questions. | A synchronous query; it may reuse edge caches. | `Unknown` on deadline or expansion limit. |
| `ContinueRouteSearch` with a goal | A persistent frontier and suspended edge generator for ordinary routes. | Across ticks while world revision and policy remain applicable. | A partial route or no publishable prefix; neither establishes impossibility. |
| Positioner's goal-free searches | Regions reached under different one-way policies, with origin-relative travel estimates. | Across ticks; reuse requires generated connections back to the original component. | Missing membership is unknown until the represented search exhausts. |
| `AStar.OneWay` reverse probe | Whether a sufficiently deep drop's landing can reach its departure under a bounded reverse search. | Memoised resolved results, invalidated or aged. | Unknown is permitted and not cached as a refusal. |
| `SearchControlSequences` | Short control sequences from actual continuous body state for local clearance or survival. | Frontier/control retention under validity checks. | Pending or no accepted local result, not global unreachable. |

The code gives each of these a legitimate job. Problems arise when a consumer reads one as a stronger answer to another. A coarse route is not a proven complete native excursion; an exhausted sampled graph is not a proof that no physically possible action exists; a completed movement step is not a completed mining task.[^1]

## “Returnable” currently means a policy-filtered region

`AStar.OneWay` skips the reverse test for falls no deeper than `Traversal.ClimbReachTiles`, or while already inside a probe. For larger falls it searches back towards the departure. On a deadline or expansion limit it returns `false`, meaning the edge is not rejected as one-way. The source explicitly acknowledges that a sufficiently large sealed region can therefore be admitted.[^2]

The positioner calls a flood with these refusals enabled and stores it in `returnable`. That name must not be interpreted as an explicit return certificate for every reached node. The search has excluded some known bad drops; it has also permitted unresolved ones and smaller transitions without an individual reverse proof. This distinction directly bears on entering a pit for ore or drops while the player remains above.

`ContinueRouteSearch.CanReuseFrom` answers a different question: its `canReturn` set propagates predecessor relationships among generated directed edges. It permits region reuse only for a new origin connected back to the original component. This is useful graph-level evidence and stronger than a distance heuristic. It still inherits the transition model and does not certify native execution, breath or resources for a future trip.

There is also a current-player exception. When the raw one-way-permitting region contains the player and the filtered region does not, position selection can open to the raw region. This observes present geometry. It is not historical evidence that the player traversed the same edge, nor proof that the companion possesses the player's movement abilities. The short player trail in observation is a separate record; it is not currently consumed as a traversal permission certificate.

The durable replacement for ambiguous labels is a set of explicit facts: outward route found; return route found from an applicable state; return not resolved; known incompatible return; and policy permits following the player despite that difference. Which unknowns may be explored remains a behavioural policy, but the evidence should not change name when the policy changes.

## A tile plus counters is not a complete motion state

`NavNode` contains a feet tile and `MobilityState`. The latter contains air jumps left, latch state and dash cooldown. `BodyState`, used in simulation and execution, separately contains continuous position, velocity, contacts, wet/liquid state and capabilities. The baseline graph therefore distinguishes mobility counters but aliases some physically different arrivals at one tile.[^3]

The initial expansion can use the actual starting pose. Later coarse edges generally begin from representative standing poses, and actual execution revalidates them when reached. That is a deliberate approximation with a validation boundary, not necessarily an error. It becomes insufficient when the only useful route depends on carrying momentum through successive edges, starting an ability in midair, or reaching the same position with a different resource history that the node omitted.

A return itinerary involving a double jump and a dash cannot be represented merely by adding fields to a record. The graph must generate the relevant airborne transitions, account for consuming and restoring resources, distinguish futures that depend on momentum, and terminate in a meaningful safe state. The current movement-ability declarations and mastery preview do not establish any of those behaviours.

The appropriate test compares representation against an independent native oracle: collect actual feasible transitions and ask whether the graph can propose them. Failure to propose a move is different from proposing it and failing to execute it. Increasing A*'s budget helps the latter only if the search was actually cut short; it cannot discover an absent edge.

## The current heuristic does not support a shortest-path guarantee

The retained search uses horizontal distance plus half the vertical tile distance as its heuristic. `Traversal.MovementCost` prices a drop or fall-through as one plus one fifth of downward tile distance, before additional environment costs. Consider a dry, unobstructed ten-tile vertical drop admitted as a direct edge to the goal: the heuristic is five, while that edge's cost is three. The heuristic overestimates this represented cost-to-go.[^4]

That arithmetic is enough to refute a blanket claim of admissibility for the current cost model. It does not prove that this overestimate caused any observed failure, and the project may prefer a useful heuristic to exact optimality. The important correction is to state the guarantee accurately. Comparing A*, D* Lite or an anytime alternative must use compatible costs and heuristic assumptions before attaching their theoretical bounds to this implementation.

The synchronous and retained searches also differ in closed-node handling: the retained query can reopen a node after a cheaper path is discovered. The safe research conclusion is not that all A* implementations here are equivalent. A controlled search comparison needs the same graph, costs, tie-breaking, stopping conditions, origin and target.

Travel estimates have their own limitation. Region membership can remain useful after moving within a connected component, but accumulated travel time remains rooted at the original search start. `EstimatedTicks` deliberately refuses to reuse a travel time from a different origin. Falling back to geometric travel estimates elsewhere is an approximation and should be marked as such in opportunity value and protection timing.

## Retention already supports acting while planning, but prefix choice has a strategic weakness

`ContinueRouteSearch.Advance` retains its queue, costs, predecessors and even an in-progress edge generator. It reserves one bounded operation after upstream work has consumed the shared deadline, which limits complete starvation but makes the deadline cooperative. `Navigator` can use validated partial routes while the search continues. The desired idea of moving while searching is therefore an extension of the baseline rather than a replacement premise.[^1]

The published partial endpoint is the reached node with the smallest heuristic distance encountered so far. If the first useful leg goes away from the goal, this criterion can publish no progress or prefer a misleading closer pocket. Retaining the frontier helps eventually discover the detour; it does not by itself make every intermediate endpoint strategically useful.

Safe intermediate travel needs two independent questions. First, can the body execute the prefix under current physical and resource constraints? Second, is entering that endpoint acceptable while the continuation remains unknown? A dry standing tile can be locally stable and still be a trap. A node closer to ore can be worse for following the player. A surviving control prefix is not automatically a useful exploration policy.

A good separating experiment keeps the current search while varying only the publication policy: closer endpoint; endpoint with a proven return; or endpoint selected for information gain and continued player relevance. It then measures useful progress, repeated visits, time before a usable route, abandoned excursions and stranded outcomes on the same captured geometry. The external report discusses algorithms that can help once this objective is specified.

## Physical execution has made progress without proving the whole route model complete

Movement traversals combine candidate generation and controls for walking, jumping, dropping and passing through platforms. `PlanLocalMovement` simulates the complete proposed macro from the real entry body, retains the controls beside expected states, and invalidates them on relevant divergence. If direct execution fails it searches preparatory controls whose continuation actually completes the move. A repeated stationary preparation is not accepted simply because it is safe.[^5]

This addresses the historical failure class in which a representative jump could be proved but never flown from the body's real position or velocity. Run-up now simulates the performer's take-off rather than assuming nominal profile speed. Completed ordinary traversals can enter world memory; a rejected move can be recorded and priced so it is not offered indefinitely from the same failed conditions.

The native adapter predicts using Terraria collision routines; portable text-world replay has a separate approximation. Earlier native parity results establish their tested collision inputs, not all gameplay: another mod may write velocity, damage can change state, the world can change between checks, and the coordinator can interrupt. A positive parity result is valuable because it excludes a narrower cause; it cannot establish successful long-horizon task execution.

The actual-state boundary also has a cost. Proving a full macro can require many native simulation calls. Those calls may be indivisible with respect to the cooperative deadline. A planner benchmark that measures node expansions while hiding collision calls or nested reverse probes can therefore misidentify the expensive component.

## Experience memory is executed directed evidence, with a deliberately limited capability domain

`RememberExecutedRoutes` stores completed ordinary transitions, their movement parameters and terrain fingerprints over their swept bounds. It does not learn from a proposed plan alone. Suffix search follows the directed archive towards the requested destination and reprices remembered steps with current costs. Changed fingerprints invalidate entries; physical failures can forget a step. Distant-follow recovery does not teach ordinary routes.[^6]

The recorder deliberately refuses non-basic capability or mobility states. That prevents today's basic graph from borrowing a transition that required future abilities, but it also means the archive is not a ready-made learning system for arbitrary mastery chains. Its entries omit a general capability signature and detailed resource transformation. Such additions are future work, not a cosmetic version bump.

Experience reduces repeated search when the relevant executed connection remains valid. It can also bias attention towards formerly useful routes, and a fingerprint only covers the terrain and semantics encoded in it. The acceptance check remains necessary. The correct evaluation separates memory hits, time saved, invalidations, false reuse, route quality and final activity progress; a larger archive is not itself an improvement.

## What would justify changing the navigation subsystem

| Evidence pattern | Leading interpretation | Appropriate first intervention |
|---|---|---|
| Native-feasible move absent from generated neighbours | Graph or transition-generation deficiency. | Refine the relevant state/primitive; preserve the search for comparison. |
| Useful route appears only with more search work | Budget, ordering or repeated computation problem. | Measure expensive primitives and retained frontier; compare search scheduling or incremental repair. |
| Planned move fails only from certain real entry states | Entry-state aliasing or incomplete preparation. | Refine the execution contract or split state classes where needed. |
| Same controls disagree with native motion | Simulation or adapter mismatch. | Repair the shared body boundary before judging route choice. |
| Every step completes but the actor loops | Goal/prefix progress or objective instability. | Measure activity-level progress and destination identity. |
| Route succeeds but ore cannot be worked | Terminal predicate mismatch. | Make arrival satisfy the interaction, including dynamic reach and exposed-face conditions. |
| Safe outward trip consumes the only way home | Resource or returnability representation gap. | Carry an applicable return/resource certificate; do not substitute a larger score. |

Several rows can occur in one episode. The first incorrect contract in that episode is the best repair target; its repair must then be followed forward to see whether another failure becomes visible. This is why the proposals branch on evidence rather than promise a sequence of three universal fixes.

## Sources

[^1]: [ContinueRouteSearch.cs](../../Companion/Brain/Infrastructure/Movement/RoutePlanning/ContinueRouteSearch.cs), `Advance`, `Result`, `EstimatedTicks`; [Reachability.cs](../../Companion/Brain/Infrastructure/Movement/RoutePlanning/Reachability.cs); [Navigator.cs](../../Companion/Brain/Infrastructure/Movement/MovementExecution/Navigator.cs).
[^2]: [AStar.cs](../../Companion/Brain/Infrastructure/Movement/RoutePlanning/AStar.cs), `OneWay`, `RememberedStepHasReturn`; [ChooseUsefulPosition.cs](../../Companion/Brain/Infrastructure/Position/ChooseUsefulPosition.cs), `RefreshReach`; [ObservePlayer.cs](../../Companion/Brain/Infrastructure/Observation/ObservePlayer.cs), `Trail`.
[^3]: [NavPath.cs](../../Companion/Brain/Infrastructure/Movement/RoutePlanning/NavPath.cs), `NavNode`; [BodyState.cs](../../Companion/Brain/Infrastructure/Movement/BodySimulation/BodyState.cs); [DescribeMovementCapabilities.cs](../../Companion/Brain/Infrastructure/Movement/MovementAbilities/DescribeMovementCapabilities.cs).
[^4]: [Traversal.cs](../../Companion/Brain/Infrastructure/Movement/MovementExecution/Traversal.cs), `MovementCost`; [ContinueRouteSearch.cs](../../Companion/Brain/Infrastructure/Movement/RoutePlanning/ContinueRouteSearch.cs), `Distance`.
[^5]: [MovementExecution/CLAUDE.md](../../Companion/Brain/Infrastructure/Movement/MovementExecution/CLAUDE.md), historical results and limits; [PlanLocalMovement.cs](../../Companion/Brain/Infrastructure/Movement/MovementExecution/PlanLocalMovement.cs); [SimulateTerrariaBody.cs](../../Companion/Brain/Infrastructure/Movement/TerrariaIntegration/SimulateTerrariaBody.cs). Historical measurements are not newly rerun tests.
[^6]: [RememberExecutedRoutes.cs](../../Companion/Brain/Infrastructure/Movement/RoutePlanning/RememberExecutedRoutes.cs), `Record`, `Suffixes`, `Fingerprint`.
