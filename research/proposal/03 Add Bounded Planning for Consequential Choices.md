# Path 3 — keep contextual goals and plan only consequential choices

**Rank: third among three credible routes. Confidence: medium for a bounded consequential subproblem, lower for replacing the whole brain.** This path introduces a short planning layer beneath contextual goal selection. Utility can still choose which outcome matters; a planner compares legal ways to achieve it when the consequences of an early step materially change later options. Shared native movement, tool and resource contracts remain authoritative.

This is not the proposal to script mine → collect → guard → resume. The owner's examples explicitly reject unconditional sequences. A plan is a contingent hypothesis about useful future actions, revised or abandoned when its assumptions or value change. It may choose not to collect newly mined ore, not to finish an old intention, or not to make a return trip that current abilities cannot support.

## Why planning remains in the top three

Some desired behaviours depend on consequences beyond the next attractive action: preserving a jump for the way back, choosing a short detour that enables several useful opportunities, or deciding between movement methods with different resource/risk effects. A purely local value model may need increasingly indirect features to represent these. A bounded planner can expose those dependencies directly.[^decision]

The current failures do not yet establish that such planning is the first missing component. An arrived firing spot with no shot and an unknown return treated as permissible are already wrong inputs to a planner. The history repeatedly shows that richer machinery can inherit a false physical fact. Planning ranks third because it requires accurate action models and stable evidence in addition to the shared repairs required by the other paths.[^internal]

Production HTN/GOAP accounts show that planning can be practical with carefully chosen tasks, methods and tools. Options and hierarchical navigation show ways to keep physical action sequences bounded. They also expose authoring cost, state abstraction and horizon assumptions. None gives a controlled Terraria companion result.[^external]

## First prove that an enabling consequence is missing

Run E13 on two matched kinds of scene. The first is ordinary opportunism: ore, a bat and nearby/distant drops, where re-evaluating current opportunities may already produce the desired order. The second contains a real consequence: one step that is unattractive alone changes the value or feasibility of later useful actions, within the closed ability and edit set.

Possible consequential fixtures include choosing between two approach/return methods that spend different movement resources, or a short safe detour that serves multiple nearby opportunities before the player moves on. The fixture must not invent bridge-building, crafting, chest opening or arbitrary excavation merely to make a planner useful. Resource-aware navigation may already solve the first example without a high-level planner; that is an explicit competing explanation.

| Observation | What it means | Next move |
|---|---|---|
| Reactive utility plus activity state produces the accepted sequence. | Multiple steps alone do not justify planning. | Retain Path 1 or 2 for that scope; test a genuinely consequential example. |
| The only missing consequence is movement-resource feasibility. | The deficit belongs in route/local action state. | Extend the physical contract and compare again before adding goal planning. |
| A locally weak step unlocks a better accepted assistance outcome and the baseline consistently misses it. | There is a concrete planning use case. | Define a bounded action/effect model for that choice. |
| Planning appears to win only with knowledge absent from the actor. | The comparison uses an unfair oracle. | Restrict both policies to the same evidence or make information gathering an explicit uncertain action. |
| A future objective cannot be specified without asking what the player values. | The behavioural comparison is underspecified. | Prepare a small side-by-side choice for the owner rather than infer a universal reward. |

The pass line is a repeatable improvement on the consequential case without weakening permissions, physical safety or ordinary responsiveness. An invented aggregate reward is not sufficient if the owner finds the resulting detour intrusive.

## Define the smallest truthful action model

An action model states initiation conditions, required resources, expected duration, possible native outcomes, invalidation causes and a terminal condition. Examples are approach a usable work pose, perform a coherent tool action, take a validated movement prefix, use a supplied torch or collect by contact. Their preconditions call shared physical/native queries; the planner does not independently approximate tool reach or declare an unknown route reachable.

Represent uncertainty explicitly. A failed budget is not a false predicate, and a promising model result is not native proof. Plans may include a bounded investigation that terminates at a safe intermediate state, but not an irreversible action whose safety rests on future information the companion may never obtain. Return feasibility and player-following permission remain separate from forward travel.

The plan stores only what it needs to explain and validate the sequence: goal, activity/target generations, capability and terrain dependencies, selected method, expected outcomes, resources reserved/consumed and reconsideration reasons. It does not keep obsolete targets alive just because a list contains them.

## Choose GOAP, HTN or short rollout by the actual decision

| Planning form | What it makes explicit | Best initial fit | Failure signature |
|---|---|---|---|
| Bounded GOAP | Search over actions with preconditions, effects and costs. | Several legal combinations achieve one outcome and their consequences can be modelled compactly. | State explosion, implausible combinations or brittle boolean physical facts. |
| HTN with utility-ranked methods | Authored legal decompositions and alternative methods. | A small repertoire of meaningful ways to help or traverse whose constraints should remain visible. | One method per task merely restates an executor; many special-case methods recreate scenario scripting. |
| Short receding-horizon rollout | Compare predicted local consequences, execute a first action and re-observe. | Positioning, bounded incidental work or resource trade-offs with a trustworthy short model. | Good-looking prefixes end in bad states just beyond the horizon; repeated replanning never completes anything. |

Start with the smallest form that represents the demonstrated fixture. HTN is not automatically the safe choice: an authored method can be wrong about a modded predicate. GOAP is not automatically more general: its action model may exclude the desired behaviour. A short rollout is not free of commitment: it still needs a coherent local action and a safe interruption boundary.

Compare at least one simpler nonplanning baseline on the same observations and oracle. If the planner's advantage disappears after fixing one missing current-value feature, keep the simpler policy unless the explicit model offers a demonstrated authoring benefit across several real cases.

## Execute one useful part and monitor the consequences

The chosen goal should remain contextual. A plan can stay valid yet become no longer worthwhile when the player moves or danger rises. Conversely, a temporary reflex does not automatically invalidate every remaining step. Re-evaluate the goal, then revalidate the next action's assumptions; retain already useful computation only where dependencies still hold.

```text
Plan selected for a currently useful goal
├─ Next action's physical precondition is proven under current assumptions
│  └─ Request resources and execute a coherent local option
│     ├─ Expected native effect occurs → update remaining plan and goal value
│     ├─ Different effect occurs → invalidate affected assumptions and reconsider
│     └─ Urgent handoff occurs → record suspended/abandoned state and reason
├─ Precondition is unknown
│  ├─ Safe information-gathering prefix exists → execute that prefix and continue inquiry
│  └─ No such prefix → defer this means or choose another useful goal
├─ Precondition is disproven now → choose another legal method or abandon the goal
└─ Goal no longer worthwhile → stop at the declared safe boundary and select again
```

The point of the plan is to represent an enabling dependency, not to override observed reality. Native effects update the model. A tool call with no damage is a different outcome from partial progress; a route waypoint reached without a usable shot does not satisfy attack positioning; an item picked up by the player is no longer a collection target.

## Bound the horizon by the reliability of the model

Longer lookahead can expose a return-resource problem but can also invent a future enemy or player trajectory. Use E06/E10/E12 to compare prediction accuracy, computation cost and terminal safety. Retain intervals or explicit unknowns where a precise forecast is unavailable.

| Failure after adding lookahead | Proposed response | What would justify retaining the response |
|---|---|---|
| The plan strands the companion just beyond its horizon. | Add a terminal continuation/return condition or shorten to certified prefixes. | Unplanned irreversible entries disappear without hiding the difficult scenarios. |
| The plan misses urgent threats while thinking. | Reserve physical/urgent query work, cap plan work and allow interruption with explicit ownership. | Response latency improves without stale or contradictory control writes. |
| Replanning every tick prevents useful completion. | Retain a valid local option and dependency-scoped plan state. | Coherent execution improves while changed context still cancels promptly. |
| Retaining a plan makes the companion ignore new context. | Reconsider goal value and relevant preconditions at the right events/cadence. | Obsolete work is abandoned without returning to meaningless per-tick resets. |
| Predictions are accurate only for familiar vanilla attacks. | Limit the forecast to observable/common properties and expose uncertainty. | Held-out modded attack cases remain safe enough under declared fallback, with the lost opportunity cost reported. |
| Planning time dominates but rarely changes actions. | Audit query/plan value and remove planning from those decisions. | The simpler baseline preserves outcomes at lower measured cost. |

Do not claim a global expected utility model unless the probabilities, costs and observations are actually defined and calibrated. An interpretable bounded heuristic plan can be preferable to a mathematically elaborate model whose inputs are guesses.

## Reach the broader companion behaviour without planning everything

Routine following, current-shot aiming, a simple nearby pickup and a single coherent tool action should remain cheap local decisions when no future dependency changes them. Planning earns a place where the accepted behaviour needs its consequences represented.

| Expected behaviour | Likely planning scope | Branch deciding whether to expand |
|---|---|---|
| Ore–threat–drops opportunism | Usually current value plus activity state. | Add a short plan only when a measured detour/enabling consequence defeats that baseline. |
| Spatial lighting and nearby work | A few candidate opportunities and their marginal trip cost. | If region order matters enough to change useful coverage/player delay, compare bounded route-aware sequencing; otherwise choose locally. |
| Return-resource management | Physical route and movement option state first. | Add method selection above it only when several feasible methods have meaningful later resource consequences. |
| Guarding and serious fights | Fast threat/position decisions with short consequence checks where trustworthy. | Expand only if forecast calibration improves protection; boss recognition alone does not provide future attack scripts. |
| Player-relative autonomy | An uncertain estimate of available local time and separation cost. | Replan/abandon when the player leaves; do not turn the goal into a remote errand. |
| Courtesy | Local position interference cost. | Keep it outside broad planning unless a real narrow-passage coordination case needs temporal reservation. |
| Tool/weapon/light coexistence | Resource-aware local options. | A planner may coordinate them, but must preserve the user's coherent pick/axe period and held-torch exception. |
| Inventory, protection and lifecycle | Authoritative hard predicates and native effects. | No plan can override a full bag, protected edit or downed control owner by assigning enough value. |
| Future mastery/scaling | Capability revision changes action availability, duration, resource effects and proof. | A new method enters through those contracts; stale plans and route memories must revalidate. |

This produces a hybrid with a deliberately bounded planning surface. It is still a substantial architectural route because the system gains an explicit model of meaningful future effects. It does not need to replace utility, target aiming, NPC lifecycle or every local executor to obtain that benefit.

## Navigation alternatives remain subordinate to physical evidence

Planning a goal sequence cannot repair an absent jump edge or a native collision mismatch. E08–E11 remain prerequisites. An HTN method that says “take the upper route” needs a real directed route and return/resource evidence. A GOAP action that says “be at ore” needs a native-usable terminal pose. A short rollout needs a native or calibrated model for the controls it predicts.

Compare retained A*, incremental repair, region graphs and richer motion state only at the layer their guarantees concern. If stable planned subgoals create enough repeated sparse graph changes, D* Lite may become more attractive than under constantly moving destinations; measure that traffic. If a plan holds a subgoal stable only to preserve a cache while the player has left, the optimisation has inverted the product purpose.

## Learning can assist a bounded estimator after the outcome stream exists

A learned value residual, transition-success estimate or query-priority model is a possible later branch, not a required component. Its target needs observable labels: actual native success, owner-labelled preference or whether extra computation changed a useful decision. The training split should hold out terrain, capability combinations and relevant enemy behaviours.

If a learned estimate improves held-out choice while preserving hard boundaries and native validation, retain it as a bounded assistant to the plan. If it merely fits familiar fixtures or replaces uncertainty with false confidence, withdraw it. A whole learned companion remains a separate, higher-cost research programme requiring resettable simulation, reward specification and reproducible evaluation. The present evidence does not rank that programme above these three routes.

## Recorder and God’s-eye changes specific to planning

Record the goal and method/plan identity, candidate plan coverage, preconditions and their evidence revisions, predicted native effects, durations/resource use, horizon/terminal condition, expected value components, actual effects and reconsideration reason. An unavailable or untested plan should not appear as a rejected one. Plan cost must be separated from route, trajectory and ordinary selection cost.

The timeline should compare the predicted consequence with what actually happened at each executed action, alongside the simpler policy's counterfactual when a replay can validly produce it. A counterfactual uses the same starting observation, not an unchanged recorded future after the candidate action alters the world. Show the divergence point and uncertainty rather than a single “planner was better” score.

The inspector can reveal the next coherent action and the condition that would change it, with deeper alternatives on demand. A large plan graph should not become the product's explanation. The player needs to understand why the companion went for that ore or left that fight; the researcher needs the plan dependencies underneath.

## Conditions for promoting or withdrawing this route

Promote this route above the others only when E13 establishes repeated consequential failures that simpler accurate opportunity/activity models do not economically resolve, and the bounded planner improves accepted outcomes under the same evidence and physical oracle. Its prediction errors, runtime cost and invalidation work must be measured alongside the gain.

Withdraw planning from a scope when the baseline matches its behaviour more simply, modelling errors dominate the outcome, or the horizon must become impractically large to see the relevant consequence. Keep a useful local executor or capability model even if plan search loses. A failed GOAP trial does not disprove all planning, and an HTN that accumulates a method for every observed scene is evidence that its abstraction needs reconsideration.

Complete the route through E15/E16: capability/persistence stress, held-out worlds and enemies, all twenty-seven README responsibilities and live judgement of helpfulness. If the result still fails because observations cannot identify safe movement or the body cannot execute it, improve that evidence boundary rather than adding a deeper goal hierarchy.

## Appendix: why third place is still a credible choice

Planning offers the strongest explicit representation of future enabling consequences among the three paths. It may ultimately be the right architecture for richer mastery methods and regional assistance. It ranks third now because current telemetry more directly implicates invalid evidence, destination quality and control ownership, and because many owner examples can arise from current-value re-evaluation without a stored sequence. The branch becomes first-class when a controlled example distinguishes those explanations. That promotion condition is stronger than either dismissing planning as overkill or adopting it because the desired behaviour sounds intelligent.

[^decision]: [Behaviour, Opportunities and Comparison Criteria](<../Decision Architecture/Behaviour, Opportunities and Comparison Criteria.md>) and [Decision, Commitment and Computation](<../Decision Architecture/Decision, Commitment and Computation.md>), especially consequential-workflow, options, metareasoning and learning sections.
[^internal]: [Historical reconstruction](<../Historical Evidence/Pivotal Decisions and Conversation Evidence.md>), [implementation audit](<../Implementation Evidence/Decisions, Activities and Shared Controls.md>) and [recorded episodes](<../Evaluation and Observability/Recorded Episodes and Measurement Limits.md>).
[^external]: [Utility AI and Its Alternatives](<../Utility AI and Its Alternatives.md>) supplies FEAR/Decima/HTN production sources; [navigation research](<../Navigation Research/Dynamic Platformer Navigation.md>) supplies motion/skill/state and safe-prefix assumptions; [case studies](<../Game and Mod Case Studies/CLAUDE.md>) supply concrete execution and compatibility countercases.
