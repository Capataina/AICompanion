# Utility is a credible comparator whose scope remains an open question

This initial comparison is extended by [decision, commitment and computation research](<Decision Architecture/Decision, Commitment and Computation.md>) and the [three ranked conditional roadmaps](proposal/CLAUDE.md). The owner subsequently clarified contextual remaining work, action-level hands and safe intermediate movement; these are incorporated in the expanded behavioural contract.

Investigation date: 12 September 2026. Repository baseline: `4296f851b13e49ccd557d7255ec24bc223a29929`. Status: comparative research and provisional judgement, not an accepted architecture or implementation plan.

The comparison turns on requirements from the owner's request and the README: opportunism, coherent activity over time, simultaneous movement and shooting, useful behaviour under uncertainty, physical feasibility, predictable boundaries, and the ability to diagnose regressions while the system grows. Development effort matters because repeated local repairs have already consumed much of this short project's history. These criteria are the basis for comparison; no weighted numerical ranking has been measured.

**My present judgement is that utility deserves to remain a candidate for choosing between worthwhile activities, while the unit of an activity and the rules for interruption deserve more scrutiny than another round of score tuning.** I am not sure which complete architecture would perform best for this companion. No controlled comparison found in this research answers that question. A hierarchy, symbolic planner or learned component becomes more attractive when a specific recurring failure matches the problem it solves.

The closest credible alternative is a hierarchy with explicit conditions and task lifecycles, potentially using utility at selected branches. GOAP and HTN become stronger candidates if useful assistance requires choosing between genuinely different multi-step workflows. Learning is a real option for bounded policies or preferences, but needs an evaluation and training contract tailored to this game. These are overlapping choices, not mutually exclusive whole-brain brands.

## Utility answers a preference question

Utility selection assigns comparable values to available alternatives and chooses using those values. Response curves convert facts such as distance, health or urgency into preferences. The foundational Mark and Dill presentation includes both maximum selection and weighted random selection, and combinations of mathematical terms; the family does not require this repository's exact product of factors or a stateless decision every tick. [Mark and Dill, GDC 2010, *Improving AI Decision Modeling Through Utility Theory*](https://media.gdcvault.com/gdc10/slides/MarkDill_ImprovingAIUtilityTheory.pdf).

For this companion, the appealing example is a nearby drop becoming worth a small detour while the player is safe, then becoming less attractive as the player moves away or a hostile closes in. There need not be one permanent ordering of loot, following and protection. Utility gives those comparisons an explicit numerical form.

The comparison is only as useful as the alternatives and the facts behind them. If no lighting opportunity represents an unlit passage, a utility function cannot choose it. If a reachable firing spot is removed before expensive evaluation, its score cannot rescue it. If movement cannot perform an admitted jump, making the destination more desirable cannot make the jump physically valid. These are deductions from the data flow described in [Architecture and Behaviour Map](<Architecture and Behaviour Map.md>).

Utility can score goals, plans, targets, positions or continuing activities. The first architectural choice is therefore **what competes**, before selecting an aggregation formula. The present implementation compares action objects that already carry different amounts of retained state. A mine job is not the same temporal unit as an immediate positioning response.

## The current implementation makes several additional choices

[ChooseBehaviour.cs](../Companion/Brain/BehaviourSelection/ChooseBehaviour.cs) is a particular policy within the utility family. Its ordinary selection can be summarised as:

```text
for each registered activity:
    compute its raw score, which may also discover or update a job
    apply protection, incumbent commitment and excursion-horizon modifiers
discount following when useful nearby work is present
choose the greatest positive final score
exit the previous activity and enter the new one when it changes
```

The exact values live in source. This pseudocode is a description of the baseline, not a recommended replacement. The coordinator can bypass it for recovery or reflex control; the weapon/target decision is separate.

Six choices deserve independent judgement:

1. **Products turn weak factors into strong suppression.** A zero factor vetoes a candidate. That can be appropriate for a forbidden action, but it makes an uncertain estimate or accidental zero capable of deleting an otherwise useful option. Five hypothetical factors of 0.8 multiply to 0.32768, while one factor of 0.8 remains 0.8. This arithmetic does not prove the current scores are biased; it shows why adding a consideration changes cross-action comparability unless the model accounts for it. Correlated factors can also charge the same cost more than once.
2. **Preferences and requirements share numerical machinery.** Distance, opportunity and risk may be negotiable. Permission to alter a protected home is not. Some requirements are already enforced outside the chooser; the question is whether each hard boundary has the appropriate owner, and whether survival expectations are fully negotiable. A high weight is not a guarantee unless all competing score ranges are bounded accordingly.
3. **Commitment is encoded as an incumbent advantage.** This discourages small oscillations, but can retain an unproductive activity. The failed body-stall penalty shows that the evidence used to vary commitment matters as much as its size. Remaining benefit, completed preparation, genuine failure and opportunity cost are distinct facts. The [history](<History of Decisions and Failures.md>) does not establish a winning formula.
4. **The forecast is a heuristic, not a complete future plan.** The chooser prices excursions against protection and a bounded interruptible window. This can reasonably keep attention near the player. It does not, by itself, represent every consequence of a one-way drop, the benefit of revealing terrain, or a future sequence of opportunities. Those consequences must exist somewhere in the state and candidate descriptions if they matter.
5. **Scoring also does work.** Some `Score` paths acquire, retain or prune jobs and ask feasibility questions. The code is therefore more than a pure mathematical comparison over an immutable observation. Under a shared soft planning budget, which candidates receive computation is another policy to inspect. This is a potential source of order or timing effects, not a measured unfairness claim.
6. **The winner is not always the controller.** Reflex and recovery branches can own the body without a fresh chooser evaluation. A retained `guard` label during an avoidance tick does not establish that guard's score caused the applied control. A new selector would still need to coordinate those paths.

Each choice can be reconsidered without declaring utility as a family wrong. Conversely, preserving the word “utility” does not justify preserving these choices.

## Coherent work and immediate responsiveness must be compared together

Consider the README's sequence of helping with wood, gathering the drops, noticing a nearby pot and then working ore. A reactive selector can produce that sequence if the next useful opportunity becomes best at each step. That is a legitimate design, and no plan is necessary merely because the observed actions form a sequence.

Now change the situation: the pot is locally tempting but its approach is unproductive; the ore is useful but needs a short setup before its benefit becomes visible; the player briefly fights a slime and then resumes mining. The architecture needs to explain whether the companion remembers the ore, what interruption means, and why it returns or gives up. A flat score can incorporate those facts, but they must be represented and maintained somewhere.

A stateful activity could retain an identity, a still-valid target, an execution phase and a reason for completion or abandonment. Existing mining and survival already do parts of this. The research question is whether a common contract would remove repeated coordinator guesses or simply add abstraction. A behaviour tree, a small state machine, explicit code and a planner-backed executor could all implement such a contract.

The strongest case against adding temporal structure is stubbornness and maintenance cost. If the current retained jobs already explain the desired cases, a new framework buys little. If “continue until complete” masks a rapidly changing player objective, it violates the companion's purpose. A useful comparison must include both unnecessary abandonment and failure to abandon; measuring only longer commitment would reward the wrong behaviour.

## The alternatives solve different parts of the problem

The assessments in this table are project-specific inferences, not measured performance rankings. The following sections provide the external evidence and the counterarguments.

| Candidate | Natural role | What it could buy here | Cost or failure mode | Evidence that would favour it |
|---|---|---|---|---|
| Utility over immediate or continuing activities | Compare context-dependent preference | Opportunistic trade-offs remain explicit; the existing scores and traces provide a baseline. | Calibration across activities, unstable switches, missing future consequences or candidates. | Desired choices vary continuously and a small set of interpretable factors captures their ordering. |
| Flat finite state machine | Small set of exclusive phases | Direct transitions and easy local lifecycle reasoning. | Cross-cutting combinations require more states or transitions; whole-actor exclusivity conflicts with independent hands. | A bounded component has a few well-defined phases and little unrelated policy. |
| Hierarchical state machine | Nested phases and inherited interruption rules | Make an activity's preparation, performance and recovery explicit. | Important exceptions can accumulate at parent boundaries; hierarchy still needs a preference policy. | Most failures are illegal or forgotten transitions, and a compact hierarchy expresses the desired cases. |
| Behaviour tree | Compose conditional, sequential and fallback procedures | A reusable execution structure with visible success, failure and running status. | Selector order, retained sequence progress, abort behaviour and shared resources still require policy. | The owner's judgement maps cleanly to a stable hierarchy and the tree makes explanations easier than score tuning. |
| Goal-oriented action planning, GOAP | Search for an action sequence reaching a symbolic goal | Choose an alternative workflow when a prerequisite or action changes. | Incorrect preconditions/effects yield invalid plans; goals and interruption still need selection. | Multiple useful ways to achieve the same assistance goal repeatedly defeat local hand-authored sequences. |
| Hierarchical task network planning, HTN | Decompose goals through authored methods | Keep sequences within intended methods while choosing a suitable decomposition. | The method library is another place to encode and maintain decisions; physical feasibility remains separate. | Several meaningful methods share substeps, and controlled decomposition is preferable to broad symbolic search. |
| Reinforcement learning | Learn a policy or value estimate from reward and interaction | Optimise a bounded preference or control problem difficult to author directly. | Reward mismatch, coverage, reproducibility and native-world transfer become central work. | A stable measurable objective and training environment outperform a competent authored baseline on held-out cases. |
| Imitation or interactive learning | Learn from demonstrated and corrected decisions | Capture aspects of companion style that are difficult to specify numerically. | Demonstrations must include relevant states and recovery; the player's own controls are not automatically the desired companion controls. | Consistent labelled choices exist and a learned model generalises to unseen situations with acceptable failures. |
| Composition of the above | Assign different decisions to different mechanisms | Retain useful pieces and change a bounded responsibility. | Too many overlapping owners can make the whole harder to reason about. | Each boundary has a clear input, output, lifecycle and measurable reason to exist. |

### State machines should be judged at the scope where they operate

The failed pre-utility brain is evidence against its particular whole-actor priority arrangement. It does not invalidate state machines for `Approach → Swing → Collect`, a recovery manoeuvre or a downed lifecycle. Such phases express what must happen over time; a separate policy can decide whether the activity remains worthwhile.

The production precedent is instructive: F.E.A.R. used planning alongside a small execution FSM. Planning and state machines were complementary, with the former finding sequences and the latter performing the selected actions. Its designers used that separation to reduce the complexity of embedded behaviour logic. [Jeff Orkin, GDC 2006, *Three States and a Plan*](https://www.gamedevs.org/uploads/three-states-plan-ai-of-fear.pdf).

For this mod, the relevant test is how many interactions the local executor must encode. If mining's phases are few and self-contained, a small state machine may be the clearest representation. If every new activity changes several unrelated transitions, that is evidence to examine a hierarchy or planning. Neither outcome requires returning to the original exclusive brain.

### Behaviour trees have a serious case when explicit policy matters more than smooth preference

A behaviour tree composes tasks using control nodes, commonly including ordered fallback and sequence, with tasks reporting whether they are running, successful or failed. Reactive and memoryful sequences make different trade-offs when earlier conditions change. The original research on BT concurrency also shows why a parallel node is insufficient by itself: concurrent actions can conflict over progress and resources, requiring explicit coordination. [Colledanchise and Natale, 2021, *Handling Concurrency in Behavior Trees*](https://arxiv.org/abs/2110.11813).

Halo 2 supplies a counterargument to unrestricted scalar competition. Damian Isla describes a behaviour hierarchy that favoured binary relevancy and explicit decision schemes because tuning floating-point desires became difficult when many specific priorities had to hold. Context-dependent impulses provided a way to change the usual ordering. The account concerns a production combat system, not a controlled comparison with this mod, but its authoring concern is directly relevant. [Damian Isla, 11 March 2005, *Handling Complexity in the Halo 2 AI*](https://www.gamedeveloper.com/programming/gdc-2005-proceeding-handling-complexity-in-the-i-halo-2-i-ai).

The strongest BT case here would be that most desired decisions are conditionally explicit: preserve a viable escape, stop a prohibited edit, finish an already-safe swing, then compare optional work. A tree could expose that policy better than another family of multipliers. Its strongest countercase is a growing collection of overlapping conditions trying to approximate smooth choices about distance, reward and pressure. Utility could still select within the optional-work branch.

Choosing a tree would also require specifying what a halted action retains and what its next tick means. The tree shape alone does not settle whether ore is remembered after a dodge or whether the tool hand is available while travelling.

### GOAP earns its place through alternative workflows

GOAP represents actions by preconditions, effects and costs, and searches for a sequence satisfying a goal. F.E.A.R.'s case study shows the production value of separating goals from the actions available to achieve them; A* can search this symbolic action space as well as a navigation graph. [Orkin, *Three States and a Plan*](https://www.gamedevs.org/uploads/three-states-plan-ai-of-fear.pdf).

The closest present use case would be an assistance goal with several legal ways to achieve it, where a failed prerequisite changes the whole workflow. Merely walking to ore and swinging a pick does not establish that need: the route planner and a local activity can already represent movement followed by work. The closed abilities and prohibition on arbitrary digging, construction and inventory-driven item use also reduce the number of legitimate workflows. A research example must stay inside those product boundaries rather than invent crafting or bridge-building to make GOAP look useful.

GOAP could become valuable as abilities and work methods grow, especially if one action enables several later opportunities. Its main risk is moving uncertainty into crisp symbolic facts: `reachable=true`, `safe=true` or `enemyRemoved=true` can be wrong in a changing physical world. Procedural checks, execution monitoring and replanning are still needed. The deciding evidence would be recurring sequence-choice complexity beyond what the existing activities and navigation already own.

### HTN offers authored alternatives with visible decomposition

HTN planning starts with a task and chooses methods that break it into smaller tasks. Domain knowledge guides the search, which trades authoring effort for constrained, intentional plans. The 2007 game-planning paper discusses that trade-off, including its computational motivation. [Kelly, Botea and Koenig, *Planning with Hierarchical Task Networks in Video Games*](https://icaps07-satellite.icaps-conference.org/workshop8/Planning%20with%20Hierarchical%20Task%20Networks%20in%20Video%20Games.pdf).

This remains a production option rather than an obsolete theoretical alternative. Guerrilla's November 2024 Decima presentation describes HTN for high-level NPC decisions, with backtracking, generated code and visualisation for debugging decompositions. The page supplies implementation experience, not comparative performance numbers. [Tim Johan Verweij, *HTN Planning in Decima*](https://www.guerrilla-games.com/read/htn-planning-in-decima).

For the companion, HTN's attraction is control over the acceptable methods of helping. Its weakness is redundancy if each task has only one simple method: a planner may then be an elaborate way to execute an authored sequence. It becomes more compelling when real alternatives share useful subtasks and the chosen method depends on observations. Interrupting and revalidating that method remains a separate design obligation.

### Learning can target one decision without replacing the whole companion

Reinforcement learning learns behaviour in relation to reward obtained through interaction. That makes the reward and available observations part of the specification, whether the policy is tabular, a small function approximator or a larger neural model. Training need not happen during normal play, and a learned policy need not control raw movement. [Sutton and Barto, *Reinforcement Learning: An Introduction*, second edition, 2018](https://mitpress.mit.edu/9780262039246/reinforcement-learning/).

There is direct companion precedent. Sharifi, Zhao and Szafron used reinforcement learning for NPC action preferences in Neverwinter Nights, evaluating behaviour around traps. This establishes that companion preference learning is a real research direction. It does not establish a solution for Terraria's dynamic terrain, simultaneous hands and feet, or the desired social proximity. [*Learning Companion Behaviors Using Reinforcement Learning in Games*, 10 October 2010](https://ojs.aaai.org/index.php/AIIDE/article/view/12392).

The current repository already has replay and native verification infrastructure, so “there is no simulator at all” would be false. What this investigation has not established is a training-ready environment with the required reset, observation, action, reward and scenario coverage. Native collision parity is valuable but does not imply a simulator for player intention, enemy scripts, work opportunities and long-term companionship. A learned controller trained against the portable body would also need evidence that it transfers to native collision.

Several bounded uses remain plausible: learn a preference among already-legal nearby opportunities; estimate the likelihood of a move succeeding; propose response-curve parameters; or learn a local control policy behind a feasibility/safety boundary. These are hypotheses, not recommendations to implement them. Each needs a baseline and a specific outcome worth optimising.

The strongest case for learning is a stable, repeatedly observed judgement that is expensive to express by hand but easy to evaluate or demonstrate. The strongest case against using it now as the governing architecture is that several apparent judgement failures are still unseparated from missing candidates, stale evidence and control handoffs. Learning from those labels or rewarding those outcomes could encode the measurement error. Rewarding ore yield alone, for example, would not express the owner's preference that useful work remain near the player; the evaluation would have to preserve that distinction.

Imitation learning offers another route when the desired style is easier to show than to score. Interactive training research argues that human correction can improve on ordinary behaviour cloning, while its reported demonstrations remain much narrower than this companion. [Borovikov et al., 3 June 2019, *Towards Interactive Training of Non-Player Characters in Video Games*](https://arxiv.org/abs/1906.00535).

Here the useful demonstrations would be choices for the companion, not an unqualified recording of the player. The player may use abilities the companion lacks, pursue remote goals the companion should ignore, and know terrain the companion has not observed. A dataset must say whose observations, capabilities and objective a demonstrated choice assumes.

## The choice can remain local while the alternatives remain open

The following is a graph of research options, not a selected implementation. None of the live branches is declared universally superior.

```text
Which decision is failing?
├─ A worthwhile available activity loses the comparison
│  ├─ Utility policy: inspect factors, comparability and switch cost
│  └─ Explicit hierarchy: compare clarity of conditional ordering
├─ A sensible activity loses its meaning across time
│  ├─ Existing executor: establish what state is already retained
│  ├─ HFSM or behaviour tree: compare progress and interruption semantics
│  └─ GOAP or HTN: compare only if alternative workflows are required
├─ A good decision cannot be carried out
│  ├─ Feasibility model and native execution contract
│  └─ Control ownership and interruption of the active move
└─ A bounded policy resists authoring despite trustworthy inputs
   ├─ RL: specify reward and training/evaluation coverage
   └─ Imitation: specify demonstrations and recovery coverage
```

Two broad conclusions are premature. Replacing all decision-making with a tree or planner lacks evidence that the selector is the dominant source of failure. Declaring utility permanently correct lacks evidence that its growing numerical policy will stay understandable. Keeping the current executable baseline during research follows the owner's scope; it is not an architectural endorsement.

A composed system also has a cost. If utility selects a goal, a planner chooses a sequence, a tree executes it and a reflex can override the motor, four places may believe they own continuation. Composition earns its place only when each boundary is simpler and more testable than the alternative. The present system already demonstrates both the usefulness and risk of having several decision owners.

## The evidence favours distinguishing experiments over a universal ranking

The local history, the baseline code and the external implementation accounts agree that preference, temporal execution, physical feasibility and resource ownership are distinct concerns. They do not independently establish the best architecture for this specific companion. Several historical records and current documents were written from the same sessions and must not be counted as independent confirmations.

The practitioner material adds a useful warning: successful systems have used different combinations because their behavioural and authoring problems differed. One [r/gameai discussion from 13 May 2022](https://www.reddit.com/r/gameai/comments/up4rgz/high_level_goap_low_level_behavior_trees_good_idea/) was accessible, but the retrieved rendering exposed no vote scores. It is an anecdotal lead and supplies no technical claim used here. **The room holds no demonstrated consensus for this choice.** Primary production accounts support the existence of several credible approaches; they are not head-to-head trials.

Research covered original utility guidance, F.E.A.R.'s planning account, Halo 2's hierarchy, HTN research and Decima practice, formal BT concurrency, a companion RL study and interactive imitation research. The search did not establish a same-game comparison across these families for a Terraria-like companion. The independent review uses a fresh context within the same model family; it is not cross-family corroboration.

## My provisional direction has explicit reversal conditions

Begin the discussion by comparing **interruptible nearby activities**, using the existing mining, hunting and survival state as concrete examples. Keep utility on the table as their comparator. First establish what an activity offers, what it may consume, what it can prove, and what it reports when interrupted. This is a proposed framing for research, not a requirement to build an activity framework.

I would favour an explicit hierarchy more strongly if representative desired choices are mostly conditional obligations and the scalar model requires repeated range engineering to preserve them. I would favour GOAP or HTN more strongly if multiple legal workflows repeatedly need to be composed across activities. I would favour a learned component more strongly if trustworthy inputs, a repeatable evaluation and a bounded policy deficit already existed. I would favour leaving existing local state alone if it explains the observed cases with fewer rules and no recurring ownership ambiguity.

The owner has clarified the behaviour behind the initial intention question: unfinished work may become attractive again when it remains worthwhile, and reduced remaining effort may favour continuation. Returning is not compulsory, progress by the player counts, and elapsed effort alone does not justify obsolete work. Whether a retained activity, fresh opportunity evaluation or limited planning supplies that behaviour remains an implementation question. The expanded comparison tests those mechanisms rather than reopening the accepted behaviour.
