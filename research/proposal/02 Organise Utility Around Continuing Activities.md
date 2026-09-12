# Path 2 — strengthen execution ownership beneath the three purpose families

**Rank: second. Confidence: medium, conditional on execution coordination problems surviving Path 1.** The three purpose families and lightweight activity-owned progress now belong to the owner-selected Path 1 direction. This route adds a stronger common execution framework: reusable local actions with uniform initiation, running, interruption and termination semantics across activities. It is not the first introduction of hierarchy or activity identity.

The attraction is not a tidier diagram. It is the possibility that a continuing activity becomes the one place where the system can explain what it is trying to achieve, what has actually advanced, which resources it needs and why it stopped. If the present actions can expose those facts clearly with smaller changes, Path 1 remains preferable.

The [follow-up discussion](<../Decision Architecture/Purpose Families and Shared Companionship.md>) owns the changed ranking and family membership. In simple language, Path 1 makes each job's facts and promises consistent; Path 2 gives jobs a common way to perform and interrupt their smaller steps. The following lifecycle and hierarchy experiments remain relevant as controls, but the decision for this path is whether stronger shared execution earns its additional structure.

## Evidence that makes this a serious alternative

The project already has partial continuity: retained ore jobs, guard identity, movement sequences, route memory and incumbent preference. Their lifetimes differ and a broad activity label can survive while reflex or recovery owns the body. The failed shared-stall experiment demonstrates the danger of assigning one subsystem's delayed state to another activity. A single activity lifecycle could make those relationships explicit.[^internal]

The external evidence gives several compatible forms rather than one prescription. BDI distinguishes selected intentions from beliefs and desires; options define initiation, internal policy and termination; behaviour trees and state machines make execution/abort policy visible. Halo's production hierarchy uses more than a rigid priority list. RimWorld jobs and Starsector firing sequences demonstrate useful retained state, while their compatibility problems show why independent schedulers still need one output owner.[^external]

The strongest counterargument is that a common executor adds lifecycle structure without removing any defect the lighter Path 1 ownership could not handle. Parent, activity and local-action retention can also combine into stubbornness. E04's grouping checks remain a prerequisite inherited from Path 1; compare this path with family offers held constant to isolate the additional execution framework.

## Proposed structure and alternatives within it

An **activity** is a target-specific useful purpose that may span approach, coherent use and reconsideration. It owns a stable identity, current assumptions, remaining work, measured progress, termination reasons and requests for shared output channels. A **local option** is a bounded coherent action, such as following a validated movement prefix or using a pickaxe, with explicit start and stop conditions. Neither requires reinforcement learning.

```text
World, player context and capability evidence
                │
                ▼
Purpose families expose eligible activity offers
  Gathering     Combat and safety     Nearby assistance
                │
        utility compares declared offers
                │
                ▼
Selected continuing activity
  target + phase + progress + invalidation + cancellation
                │
                ▼
Local options request feet / tool / aim / light
                │
                ▼
One output arbiter and shared native movement/tool interfaces
                │
                └─ native outcomes return to their owning activity
```

Use Path 1's three-family membership. Mining and chopping share discovery/approach/use contracts while preserving native differences. Pots remain incidental actions. Pursuit and protection retain different purposes while sharing attack-position mechanics; kiting is a seek-safety method. Light can accompany another purpose or create a useful destination. Family ownership never makes contact pickup, held light or compatible travel-time firing exclusive to one family.

| Structural choice | First candidate | Alternative worth testing | Separator |
|---|---|---|---|
| Family comparison | Expose the best eligible child without an extra preference multiplier. | Authored family value or bounded aggregate opportunity value. | Does the added family policy produce accepted context-sensitive choices without rewarding child count? |
| Local execution | Small explicit phases for approach/use/interruption/completion. | BT execution with well-defined reactive/memoryful abort semantics. | Which form makes real transitions and cancellation easier to verify and maintain? |
| Continuation | One activity identity owns remaining-work and context revalidation. | Existing distributed state with a common reporting contract. | Does consolidation remove actual cross-owner errors rather than merely move fields? |
| Urgent response | One arbiter grants a bounded reflex/survival option control. | Existing coordinator early returns with equivalent explicit grants. | Identical behaviour, clearer ownership and no extra control writers. |

Starting with maximum eligible child value keeps family organisation close to flat selection. If this produces no runtime decision difference, its only possible benefit is authoring, scheduling or lifecycle clarity. That benefit must be demonstrated through the cases below; a hierarchy does not deserve deployment merely because it is conventional.

## First establish a fair baseline

Run E01/E02 and fix evidence/physical contract defects that would contaminate both architectures. This does not mean implementing all of Path 1 first. It means a candidate cannot be called safe because a reverse query timed out, a tool status cannot count as damage, and a recorded guard label cannot stand in for the current feet owner.

Extract or expose activity lifecycle facts without changing selection. Replay the ore/pot interval, temporary defence followed by remaining work, the arrived-hunt interval and the pool handoff. If the passive lifecycle trace cannot account for them, it is not ready to control them.

| Baseline finding | Next move | Expected observation | Failure branch |
|---|---|---|---|
| Existing actions already have consistent identity/progress/cancellation. | Keep them unless shared execution removes a demonstrated duplication or maintenance cost. | Equivalent outcomes with fewer repeated phase/abort responsibilities. | If it only adds structure, stop at Path 1; if observation changes choices, isolate candidate side effects. |
| Progress or failure attaches to the wrong activity. | Move that fact to the activity identity that produced it. | No inherited stall penalty or unrelated-shot progress. | If attribution remains ambiguous, refine event ownership rather than add longer holds. |
| A local action restarts on every selection tick. | Give that coherent action an explicit running phase and stop condition. | It completes across harmless re-evaluations while urgent interruption remains possible. | If it becomes uninterruptible, the stop condition is wrong; do not blame utility. |
| The fixed purpose still cannot execute. | Return to physical candidate/entry validation. | The same native-valid option is executable by both architectures. | Do not compare hierarchies using different movement models. |

## Pilot the stronger executor within one existing family

Work is the best first comparison because mining and chopping expose approach, active tool use, completion and incidental drops, while the user's examples clearly distinguish those phases. Treat pot breaking as a counterexample: it is expected to be incidental and should not automatically inherit mining's willingness to travel.

The first work activity should enter approach without claiming the tool hand, switch to coherent tool use only when the native predicate holds, and leave use on completion, invalidation or a justified interruption. It should preserve enough target/progress evidence for later comparison without creating a mandatory resume stack. Re-evaluate collection as a separate opportunity.

E03/E05 require that travel may shoot, active pick/axe use cannot weave with weapon cooldown, torch holding may yield and a genuine danger may cancel use. They also require an almost-complete job to be abandoned when its current cost becomes unacceptable. A hierarchy that passes only the “finish work” half is incomplete.

If the pilot reduces ownership errors and explains the accepted cases, extend the common executor to Combat and safety and Nearby assistance. If it only adds transitions around already-correct code, retain Path 1's lighter implementation. If different interactions force unrelated rules into a common executor, share only the applicable physical actions and preserve their purpose-specific logic.

## Compare family-level utility without hiding the best opportunity

Use an identical concrete candidate set in flat and grouped trials. Test zero-value additions, duplicate opportunities, reordering, a valuable child in a usually weak family and an urgent cross-family change. Parent and child commitment must not multiply accidentally.

```text
Grouped selector differs from the flat baseline
├─ Difference matches an accepted contextual choice
│  └─ Keep the policy, then test the opposite case and held-out scenes
├─ Difference depends only on child count or enumeration order
│  └─ Repair aggregation or return that family to flat comparison
├─ Urgent useful child is hidden by the parent
│  └─ Expose child eligibility/value before parent exclusion
├─ Parent and child both retain obsolete work
│  └─ Keep continuation at one declared activity boundary
└─ No decision difference and no measurable authoring/cost benefit
   └─ Reject runtime hierarchy; retain shared contracts and code organisation
```

A group may legitimately gate costly discovery under hard eligibility, such as a disabled user policy. A soft estimate that a family is uninteresting is different: it must not suppress the only evidence that could show a valuable child. E12 measures query allocation and starvation explicitly.

## Give urgent movement a lifecycle of its own

Immediate avoidance and sustained survival are different durations of response. Both should request shared movement with a reason, an intended safe terminal state and a rule for resuming or abandoning the interrupted purpose. The activity does not get to promise that an airborne manoeuvre can stop instantly; the physical executor supplies that boundary.

If a reflex can preserve a valid route/purpose while changing a few controls, preserve it. If changed threats make the route invalid, terminate or replan it explicitly. If the only safe response spans many ticks, retain an escape option through to a useful landing rather than reinitialising it as a sequence of unrelated reflexes. Record the opportunity cost of that commitment, because blindly finishing an old escape can also become wrong.

E06/E10 decide whether the harm forecast and terminal state are adequate. Fewer immediate collisions with worse total damage rejects the local criterion. A low-health companion refusing all useful movement indicates overconservative admission or a missing escape option, not necessarily a wrong family score. A new controller must still respect the separate ordinary-follow recovery and downing lifecycle.

## Reach the rest of the behaviour through shared options, not copied sequences

| Product surface | Activity-level expression | Discriminator and branch |
|---|---|---|
| Following and local autonomy | A companionship purpose observes player motion/local activity while work offers include time apart and return cost. | E14: if it stops at every ore during rapid travel, improve context/value; if it cannot execute a valid follow route, fix movement. |
| Independent lighting | A light opportunity has a spatial benefit, available supply, permission and return assumptions; placement is a bounded action. | If its family suppresses useful lighting, repair offer preparation/aggregation; compare the flat reference if the grouping itself remains the cause. |
| Incidental pot/loot work | An opportunity can briefly replace or coexist with a compatible phase when its marginal cost is low. | E13: if local re-evaluation yields the expected trip, avoid a task queue; if future prerequisites matter, compare Path 3. |
| Pursuit, protection and seek safety | Purposes share attack-position and evasion options; kiting is a safety method and aim/fire retains its immediate target decision. | If grouping makes voluntary hunting as urgent as player protection, separate purpose values; if the spot is useless, repair E07. |
| Boss/event context | Encounter facts alter the relevant purpose offers and movement envelope without rewriting each weapon. | If event recognition fails for an unfamiliar mod, improve supported observation/fallback; do not add one boss-name branch per case. |
| New movement abilities | A capability revision enables local options and changes route/return preconditions. | E15: if an old phase assumes a consumed jump remains, repair resource transitions; if choosing among methods needs future value, E13. |
| Courtesy | A position preference recognises real placement/passage interference while the activity retains its purpose. | If it abandons a good combat pose unnecessarily, reduce the courtesy trade-off or improve interference prediction. |
| Cargo, homes, revival and recovery | Existing native/policy authorities stay below all families, with explicit terminal/invalidation events. | Any forbidden edit, silent transfer loss or recovery started by optional work fails independently of family score. |
| Player-facing explanation | State the current useful purpose and actual phase, with interruption or inability reason. | If the display says mining during a reflex-controlled escape, join to the granted owner and suspended activity explicitly. |

The table reaches every class of the expected story, but each branch still needs native and held-out evaluation. A shared option may generalise across several activities; a universal executor that cannot express their different success/cancellation predicates merely hides the differences again.

## Navigation remains a separately testable service

This path does not use a behaviour tree or HFSM as a pathfinder. A movement option consumes a route or local control proof with actual-entry, resource and terminal assumptions. The same E08–E11 branches from Path 1 apply: fix a missing transition, compare budgets on a fixed graph, refine state aliasing, or compare D* Lite when sparse incremental repair dominates.

A hierarchy may help retain a stable goal while search continues, which could improve reuse. That is a measurable hypothesis, not automatic evidence for D* Lite. Measure goal lifetime, invalidation cause, first-control latency, native completion and player separation. If the hierarchy keeps goals stable only by ignoring relevant changes, reject the stability gain.

## Recorder and God’s-eye changes specific to this path

In addition to Path 1's family-offer and E01 evidence, record the shared executor's active phase/local option, its initiation and termination predicates, and abort propagation. Continue recording where continuation was applied so parent, activity and option retention cannot silently multiply. Every outcome should retain activity, target, option, route and attempt identities.

The inspector should show one selected activity with its actual resource grants and the family/child alternatives considered at that decision. A suspended work activity should remain visible as suspended, not falsely displayed as controlling the NPC. A graph of states is useful only if its highlighted node comes from the executed trace; drawing the intended phase is not evidence.

Control overhead is part of acceptance. Compare update count, allocations, phase churn and added timer cost to the smaller contract-based design. A hierarchy that is legible but makes the main thread substantially more expensive without behavioural benefit loses this comparison.

## Conditions for selecting, stopping or abandoning this route

Promote this path above Path 1 when a common executor repeatedly removes cross-purpose phase, resource and cancellation defects that lightweight activity ownership cannot economically prevent. Hold the three-family offers and movement model constant to isolate that benefit. The deciding evidence is matched improvement across multiple purposes, not one tidy mining implementation.

Keep the lifecycle but flatten family selection when grouping hides candidates, introduces double commitment or yields no benefit. Replace an overcomplicated BT with simple local phases if abort semantics are harder to explain than the action itself. Replace a sprawling local FSM with an execution tree only when actual repeated structure and tests justify it.

Move to Path 3 for the bounded choices where E13 shows that activity continuity cannot represent a necessary enabling consequence. Do not install utility, a hierarchy, a planner, an execution tree and a second reflex owner everywhere simply because each has a respectable paper. Every layer needs a distinct question and an observable result.

Finish at E16 with the full README responsibility matrix, held-out terrain/capabilities and an authorised live judgement of autonomy, danger and readability. If unresolved failures still come from native movement or inaccurate observations, return to those contracts rather than redesigning the hierarchy again.

## Appendix: why this is second rather than a weaker version of first

This route makes a larger structural bet on reusable execution machinery beneath the already-grouped utility decisions. It can make capability growth and coherent interruption easier to author, but its cost is another abstraction whose semantics must be verified. Path 1 includes lightweight activity identity, phases and grants already; Path 2 earns its place only when centralising reusable execution removes recurring defects or authoring costs beyond those repairs.

[^internal]: [Decision/control implementation evidence](<../Implementation Evidence/Decisions, Activities and Shared Controls.md>), [recorded episodes](<../Evaluation and Observability/Recorded Episodes and Measurement Limits.md>) and [commit/conversation history](<../Historical Evidence/Pivotal Decisions and Conversation Evidence.md>).
[^external]: [Decision, Commitment and Computation](<../Decision Architecture/Decision, Commitment and Computation.md>) includes primary BDI/options/BT/Halo sources; [RimWorld](<../Game and Mod Case Studies/RimWorld Work Scheduling.md>) and [Starsector](<../Game and Mod Case Studies/Starsector Ship and Weapon AI.md>) provide pinned implementation cases and their limitations.
