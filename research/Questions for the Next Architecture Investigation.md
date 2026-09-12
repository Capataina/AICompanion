# Questions for the Next Architecture Investigation

Prepared from the owner's discussion following the first research pass and expanded during the authorised investigation on 12 September 2026. The initial agenda baseline was `d6b353d`; the expanded investigation uses `d60b92b`, whose gameplay source is unchanged from `4296f85`. Answers, evidence limits and separating experiments are indexed in [Question Answers and Remaining Evidence](<Evaluation and Observability/Question Answers and Remaining Evidence.md>). Recommendations remain provisional rather than accepted implementation decisions.

There are **140 questions in sixteen workstreams**. They can be investigated without another general requirements interview, although some depend on findings from other workstreams or on future experiments. They are an evidence inventory, not 140 independent dispatches or proposed changes. An explicit answer that current evidence cannot settle a question must identify what would settle it.

The objective is to determine whether the project is making durable progress within suitable abstractions, repeatedly compensating for unsuitable ones, or doing both in different subsystems. Possible outcomes include retaining a working component, adjusting a policy, repairing an interface, changing a representation, or replacing a bounded subsystem. Neither preserving utility/A* nor replacing them is the assumed answer.

## The owner's clarifications define the comparison

The companion is an autonomous nearby collaborator with a closed repertoire of authored weapons, tools and movement abilities. Broad compatibility is a primary reason for that closed repertoire. Player-derived statistics and mastery abilities are expected to change what it can do throughout the system. These are behavioural and product constraints; the mechanism that fulfils them remains open.

- Progress matters through the current opportunity, regardless of who made that progress. The companion may help mine the same ore deposit as the player; for chopping it should prefer its own tree.
- Travel and shooting may coexist even while the broad activity is mining or chopping. Active pickaxe or axe work occupies the hand as a coherent working period, including pauses between hits; a weapon cooldown must not cause repeated tool/weapon/tool swapping. This is about active work, not a blanket prohibition attached to the behaviour name.
- Held light may yield to shooting and return afterwards as a quality-of-life exception. Torch placement and holding a torch need to be distinguished where their costs or resource use differ.
- Mining, collecting, defending, continuing and leaving are separate context-dependent decisions. Breaking a block does not create a rule to collect its drops. Drops in a pit may be unsuitable now and worthwhile after abilities or the player's position change.
- Damage tolerance depends on the actual threat and circumstances: effective damage, health, regeneration, enemy damage rate, ability to intervene, remaining action time and danger to the player. Neither always avoiding a hit nor always finishing nearly completed work is accepted as a universal rule.
- The desired direction for uncertain travel is useful progress through safe intermediate positions while further search continues. What can be established about that safety, and what must be reconsidered when circumstances change, are research questions. This does not settle a search algorithm or require recalculating everything at every step.
- Sustained fast travel should discourage repeated work excursions. Remaining active within an area can leave room for the companion to work independently even when the player is doing something else.
- A nearly finished task may become more attractive because its remaining effort or future benefit changed. An earlier intention creates no unconditional obligation to resume it, and elapsed effort must not make a futile pursuit increasingly compulsory.

Behaviour groups such as harvesting, combat or companionship are proposals to compare. A*, D* and other search approaches are candidates to examine; their interchangeability and migration cost must be established rather than assumed. The named case studies are sources of questions, not endorsements of their design or claims about how they are implemented.

## Each investigation should return evidence that can change the judgement

For each question, record the applicable source revision or publication date, the actual observation, competing explanations, supporting and refuting evidence, remaining uncertainty, and the smallest check that separates the explanations. A result may be that current evidence cannot answer the question; name the missing evidence instead of filling the gap with a plausible story.

Use commits as the chronology, Slate's identity, architecture, design, state and record fields plus its roadmap as the recorded project context, and transcripts to recover the requests, alternatives and reasoning around pivotal changes. Start with the requested window of roughly ten messages before and after, then expand only when needed to capture the whole decision. Distinguish the owner's statements from an assistant's interpretation, a proposal from an accepted decision, and a claimed fix from a demonstrated result. Transcript material stays local; committed reports use relevant dates, commit references and the minimum paraphrased context rather than raw conversations or private conversation identifiers.

For external case studies, identify the actual project and version, inspect public source where available, read primary implementation accounts and user-reported failures, and state inaccessible evidence. Establish the base game's behaviour before attributing a feature to a mod. A demonstration of one successful route is not evidence of universally reliable platforming.

## Establish the behavioural contract without inheriting the implementation

Expected result: A set of observable expectations and conditional trade-offs, with accepted constraints separated from proposed mechanisms.

1. Which README statements describe observable behaviour, and which prescribe machinery or fixed action sequences that the owner has reopened?
2. Which constraints are firm product boundaries, and which choices should vary with benefit, effective danger, remaining effort and current capabilities?
3. How should remaining work influence a choice when the same progress could have been made by the player, the companion or the environment?
4. How should cooperation distinguish helping with the same ore vein from choosing a separate tree, without turning those examples into an unrelated collection of exceptions?
5. What observable evidence distinguishes sustained travel from remaining active within an area, and what useful autonomy should each situation permit?
6. How should the acceptable distance and duration of an excursion depend on the player's movement and the companion's ability to return?
7. What can the companion reasonably know, remember or remain uncertain about when evaluating nearby terrain, enemies, work and previously noticed opportunities?
8. What behaviour should change when movement, combat or tool capabilities change, and which expectations should remain invariant?

## Reconstruct how decisions and regressions actually happened

Expected result: A commit-and-conversation chronology showing each problem, request, interpretation, attempt, rejection and demonstrated result.

9. Which commits, Slate fields and roadmap entries, and Claude/Codex transcripts correspond to each pivotal development episode, including conversations preceding commits?
10. What do roughly ten messages before and after each pivotal decision reveal, expanding the window when the actual question or resolution lies outside it?
11. Why was the original priority state machine replaced, what alternatives were considered, and which stated expectations drove that choice?
12. How did A* and its terrain graph evolve from the first implementation to the present search, positioning and execution arrangement?
13. Why was executed-route memory or experience-graph-like reuse introduced, what did it actually implement, and what improvement was demonstrated?
14. How did reachability, returnability, one-way permission and the meaning of an unknown result change across conversations and code?
15. What led to the commitment and progress experiments, and how did the observed regression differ from the reasoning that motivated them?
16. Which apparent repairs were measured progress, which revisited the same failed assumption, and which were useful intermediate steps whose original problem remained open?

## Trace observed failures to the first incorrect decision or contract

Expected result: An episode-level attribution table that distinguishes a demonstrated cause from a plausible explanation and names what evidence is missing.

17. Which captured episodes represent the actual complaints, and which exact code, capabilities, configuration and capture coverage applied to each?
18. What do the relevant telemetry fields and events really measure, including freshness, phase, retained values and identity changes?
19. When the companion stands beside ore without mining, where does the chain first fail: discovery, choice, working position, approach, control ownership or tool use?
20. When hunting goes too far or pursues an inaccessible enemy, which stage admits the pursuit and which information should have prevented it?
21. When a jump or escape is interrupted, was the interruption necessary, an activity switch, a replan, or a conflict between movement owners?
22. Does an apparent missed attack originate in pursuit or positioning, candidate admission, a rejected trajectory, weapon readiness or firing execution?
23. When the brain becomes expensive, which computation consumes the time, and does that delay or starve the decision that needed the result?
24. Across representative episodes, which failures support tuning, which expose a defective interface or representation, and which genuinely argue for replacing a subsystem?

## Examine the facts and opportunities that any decision system needs

Expected result: A map of required information, existing producers, consumers, uncertainty and missing distinctions.

25. What must describe an opportunity so that its benefit, remaining effort, approach, risk, uncertainty and relevance can be compared with unlike activities?
26. How should danger, attackability, pursuit feasibility and the value of intervention remain distinguishable while sharing the facts they have in common?
27. How should estimated harm account for defence, remaining life, damage rate, regeneration, status effects and the different exposure of player and companion?
28. What can be predicted about unfamiliar or modded enemies from observation, and how should uncertainty affect decisions without an enemy-specific rule catalogue?
29. How should player movement, recent activity and changing pace inform local autonomy without hard-coding a mode for every combination?
30. What are the distinct purposes of remembered opportunities, player trails, explored coverage and executed-route memory, and which decisions actually need each?
31. What information makes an unlit area a useful destination, including player benefit, terrain access, supplies and world-edit restrictions?
32. Can candidate filtering, cached evidence, stateful scoring or shared computation budgets make useful opportunities invisible before they are fairly considered?

## Compare ways of grouping behaviours and defining their boundaries

Expected result: Several concrete grouping candidates, with the behaviours each can express, their coupling and their failure modes.

33. What do the current behaviours actually own: motivation, target choice, position intent, work phase, resources, retention and completion?
34. Would grouping by purpose, physical activity or resource use produce clearer boundaries than the current flat set, and where do those groupings disagree?
35. Which parts of mining, chopping and pot breaking genuinely generalise into harvesting, and which differences must remain explicit?
36. Which parts of hunting, guarding and kiting can share a combat structure without losing their different reasons for moving?
37. Should following, wandering and providing light share a group, or does lighting sometimes create a distinct spatial objective?
38. What should be the unit of comparison: an action, a target-specific opportunity, a continuing activity, a behaviour group or a goal?
39. How would groups compete without hiding a valuable child, rewarding groups merely for having more children, or starving uncommon activities?
40. Would a hierarchy reduce cross-system edits and conflicting ownership, or mainly relocate the same special cases into parent-child transitions?

## Judge utility and its alternatives against the clarified behaviour

Expected result: A comparison of expressive fit, authoring burden, debugging, runtime cost and evidence that would favour each approach.

41. Can utility express the required conditional trade-offs with understandable inputs and comparisons, and which expectations remain awkward or unrepresentable?
42. What problems arise from the present score composition, including vetoing zeroes, factor count, correlated terms and incomparable score ranges?
43. Where are explicit constraints justified, and where would a fixed priority remove a trade-off the owner expects the companion to make?
44. How can score meaning remain coherent as damage, movement, tool reach, enemy strength and available abilities change throughout progression?
45. How do flat utility, hierarchical utility and utility over target-specific opportunities compare on the same representative decisions?
46. Where would a finite state machine or behaviour tree clarify execution or selection, and where would it accumulate brittle conditional branches?
47. Which real assistance problems warrant GOAP or HTN sequence selection, beyond what local activity state and route planning already provide?
48. What bounded role could reinforcement learning, imitation learning or learned value estimates serve, and what data, simulation and evaluation would make that role testable?

## Investigate progress, continuation and opportunistic combinations

Expected result: A model of continuation based on future consequences, compared against alternatives without assuming an intention stack or fixed job sequence.

49. What constitutes meaningful progress for travelling, mining, chopping, hunting, lighting and collecting, including successful work while standing still?
50. How should lower remaining effort and higher completion value affect continuation without rewarding sunk effort on a futile or newly dangerous activity?
51. How should target identity, changed circumstances and lack of progress affect retention without allowing target switches to reset failure evidence indefinitely?
52. What time window matters for an action when an enemy approaches, including preparation, interruption, defensive response and uncertainty?
53. How should mining and collecting remain separately evaluated when drops land elsewhere, especially when returnability changes with abilities or the player's movement?
54. How can nearby or on-the-way work be valued by its actual extra travel and action cost, rather than proximity alone or mandatory pickup rules?
55. When can reevaluating opportunities produce useful action sequences, and when does a locally unattractive step require explicit reasoning about later benefits?
56. What mechanisms reduce indecisive switching while remaining responsive to genuinely better opportunities and urgent changes?

## Investigate coexistence, hand use and movement coordination

Expected result: A compatibility map of simultaneous activities and a comparison of control ownership models.

57. Which current tick branches choose an objective, impose a constraint, take a resource or directly determine controls, and which bypass other decisions?
58. What defines a coherent period of active pickaxe or axe work, including pauses between hits, while still permitting shooting during the approach?
59. How should held light yield to weapon use and return afterwards, and how does placing a torch differ from simply holding one?
60. Which activities can coexist across movement, aiming, tools and presentation, and which require an explicit change of activity?
61. Can immediate avoidance preserve a useful destination and an executing traversal, and when must it replace or abandon them?
62. How should local avoidance be judged against longer-term harm, escape completion and protecting the player, rather than only the next collision?
63. What is the proper behavioural relationship between ordinary travel, distant-follow recovery, survival and downing?
64. What state must remain meaningful across a control handoff, including momentum, ability resources, work progress and the reason an action was interrupted?

## Separate route representation, search and retained experience

Expected result: An account of the current navigation algorithms and bounded alternatives, with the source of each limitation identified.

65. What distinct searches currently answer route, region, returnability and synchronous reachability questions, and how do their limits and result meanings differ?
66. Which state distinctions are necessary for future movement decisions: tile, exact position, velocity, support, liquid state and consumable movement resources?
67. Does the graph contain the physically available moves, including slopes, platforms, doors, narrow spaces and ability-dependent transitions?
68. What claims about feasibility or optimality do the actual costs, heuristics, pruning and stopping rules support?
69. When an origin, goal or terrain changes, which work could be retained or repaired, and what would alternatives such as D* or D* Lite actually require?
70. What does current route experience remember, how is it reused and invalidated, and when does it reduce repeated work or preserve a mistaken assumption?
71. What makes an intermediate position safe enough to enter while the full route is still being searched, including a viable continuation or return?
72. How should unfinished search choose useful progress on routes whose first leg goes away from the destination, without repeatedly restarting or entering a dead end?

## Test the boundary between a route and the real body

Expected result: A physical-contract map covering admission, execution, interruption, failure and changing capabilities.

73. When does a move proved from a representative state fail from the state the real body reaches, and which differences cause that failure?
74. Where do portable simulation and native collision disagree, and which existing parity checks cover slopes, platforms, liquids and body dimensions?
75. How should run-up, take-off, airborne control, landing and settling respond to changing movement statistics and newly available abilities?
76. How should breath, health exposure, jumps, dash availability and other limited resources affect the feasibility of a trip and its return?
77. What constitutes evidence that an excursion is returnable, and how does the player's own traversal affect the permission to take a one-way route?
78. Which changes in terrain, threats, capabilities or body state invalidate a route or local proof, and which allow useful work to continue?
79. What physical and tool conditions make an ore approach genuinely usable, including reach, obstruction, swing geometry and terrain changes during mining?
80. How should execution failures be classified and remembered so that bounded retries learn something without permanently rejecting a move that becomes valid later?

## Study the named games and mods as concrete case studies

Expected result: Code-grounded case studies with baseline behaviour, changes, trade-offs, failure reports, compatibility concerns and a transfer assessment.

81. How does RimWorld's underlying work and job system handle discovery, priority, reservations, interruption and completion, so that mod changes have a clear baseline?
82. What does Pick Up And Haul change about collection and carrying, how does it choose opportunities, and what costs or undesirable behaviour result?
83. What does While You're Up change about work performed on the way, and how does it decide when an additional action is worth the detour?
84. What does While You Are Nearby add, how does it define a worthwhile nearby opportunity, and how does that differ from route-based opportunism?
85. What does Common Sense change about task preparation, ordering and continuation, and which improvements rely on domain-specific rules?
86. How does Free Will actually choose work, does it use utility scoring, and how does its policy handle changing needs, progress and interruptions?
87. How do these RimWorld mods interact, overlap or conflict, and what do their compatibility reports reveal about multiple systems trying to schedule the same pawn?
88. How do Starsector and relevant AI mods coordinate pursuit, manoeuvring, immediate danger, target choice and weapon use, and what changes demonstrably improve behaviour?
89. What can Terraria's native AI and other companion or NPC mods teach us about reusable interactions, capability limits and compatibility?
90. What do published platformer AI implementations and studies do for dynamic terrain, moving goals, jump planning and recovery, and what failures or limits accompany their reported successes?

## Define compatibility, comparisons and the standard for changing direction

Expected result: An evaluation strategy and decision criteria for retaining, tuning, restructuring or replacing a bounded component.

91. How should player-derived statistics and mastery-derived abilities propagate consistently into movement, tools, combat, risk, opportunity value and returnability?
92. Which assumptions about vanilla enemies, tiles, items or interactions threaten compatibility, and which can be replaced by observable properties or shared capability queries?
93. Which relationships should remain consistent when a capability changes, and which apparent inconsistencies have legitimate physical explanations?
94. What representative real scenarios and held-out cases would test cooperation, danger, work, pursuit, exploration and movement across progression?
95. How can competing designs be compared fairly when the companion's actions change the world, making an unchanged recorded future an imperfect counterfactual?
96. What measures reflect the desired experience: useful work, harm, proximity, wasted movement, avoidable switching, successful arrival and computational cost?
97. What evidence would distinguish an actual improvement from a favourable fixture, incomplete capture, changed environment or a shifted failure elsewhere?
98. What is the smallest informative experiment for each serious alternative, and what existing infrastructure could support it without a whole-brain rewrite?
99. What criteria would justify keeping a system, changing its tuning, repairing its contracts, changing its representation or replacing it entirely?
100. How should conclusions preserve supporting evidence, counterevidence, uncertainty and reversal conditions so that later work does not repeat a rejected idea under a new name?

## Verify the observer and the provenance of its claims

101. Which clocks identify a world tick, a brain update, an event order and elapsed wall time, and where does downing or pausing break a naive join?
102. Does each reported decision identify a fresh chooser execution, or only a retained label or changed serialised snapshot?
103. Which work statuses describe candidate evaluation rather than selected execution or productive native effects?
104. Which movement counts describe planned edges, begun attempts, sampled retained outcomes and stable arrivals?
105. Can mixed or incomplete weapon-pair evidence produce a false universal conclusion in the analyser?
106. Which source revision, package, configuration, capability profile and loaded mods are recoverable from each old capture?
107. Which terrain fields are captured, when are chunks sampled, and what world state remains unknowable at an earlier tick?
108. How much do the recorder, inspector and retrospective analysis cost, independently of ordinary AI computation?
109. How should the God’s-eye view separate information the agent actually had from later reference or counterfactual evidence?
110. What known-positive, known-negative, missing-data and malformed-event cases would make the observer itself falsifiable?

## Test the hidden contracts between information and action

111. Does a reverse-search timeout permit a voluntary one-way edge, and how does that differ from directed-region reuse?
112. Does a sampled search for firing positions mistake an omitted candidate for proof that no position exists?
113. Can the navigator's arrival tolerance stop a body outside the actual work predicate's tolerance?
114. Does a successful tool invocation prove damage or completion, and where should productive progress be measured?
115. Can independent firing or unrelated displacement renew a pursuit that has made no progress on its own target?
116. Do target-motion and capability changes invalidate every cache whose answer depends on them?
117. Which current A* heuristic/cost combinations invalidate shortest-path assumptions, and which claims remain valid without optimality?
118. Can a retained frontier make progress on an away-first detour while its selected partial endpoint fails to advance?
119. Does family gating alter discovery/state maintenance because current score functions have side effects?
120. Can an early-return reflex or recovery path bypass the coherent hand-use rules applied on ordinary ticks?

## Compare planning depth, computation policy and changing capabilities

121. When can a cheap upper/lower bound decide whether more candidate evidence could change the chosen action?
122. How should urgent physical checks avoid starvation without evaluating every possible work candidate each tick?
123. When does a persistent goal and sparse edge-change stream justify incremental graph repair over retained ordinary search?
124. When does local entry-state refinement suffice, and when does irreducible state aliasing require a richer global representation?
125. Which finite-horizon choices look beneficial only because their bad return or resource consequence lies beyond the horizon?
126. What measurable success event could train a bounded value or transition estimator without learning hard permissions?
127. How should saved route experience, remembered opportunities and transient activity state survive reload or capability changes?
128. Which metamorphic relationships should hold when irrelevant candidates are added, tasks are regrouped or remaining work comes from another actor?
129. How should ability/resource use account for both the outward journey and the return without treating recovery flight as unlimited ordinary mobility?
130. What evidence distinguishes uncertain player-intent inference from a feature that silently assumes the player has issued an order?

## Keep the proposals useful after the code changes

131. What source-quality and version checks prevent a similarly named mod or old fork from becoming evidence about the wrong implementation?
132. Where do mod maintainer reports corroborate, contradict or add to the technical account, and which reports lack prevalence or causal evidence?
133. Which findings are immutable dated observations, which are durable principles and which must be re-evaluated after a particular change?
134. What is the smallest common preparatory work that remains useful whichever of the three architectures wins?
135. What evidence promotes the second or third proposal above the first, rather than merely adding another mechanism to it?
136. Where should a branch stop or roll back because it increases complexity without improving an accepted behaviour?
137. How does each roadmap reach the full behavioural contract beyond fixing the immediate ore and hunting complaints?
138. What independent checks distinguish complete research coverage from a confident answer to a subset of questions?
139. What remains impossible to settle through desk research, existing recordings and current source alone?
140. What concrete implementation experiment should start the next session, with its expected outcome and alternatives declared before it runs?

## The investigations have dependencies, but they do not require another requirements interview

The behavioural contract, historical reconstruction and external case studies can begin as separate reading lanes. Detailed source tracing supplies the failure attribution. Architectural comparisons then use those findings to avoid comparing solutions to an incorrectly diagnosed problem.

```text
Behavioural contract ───────────────────────────────────────┐
History and transcript reconstruction ──► Failure attribution│
External game and mod case studies ─────────────────────────┤
                                                           ▼
                       Information, grouping and decision comparisons
                                  │
                         Progress and control ownership
                                  │
                      Navigation and physical-contract comparisons
                                  │
                         Compatibility and fair evaluation
                                  ▼
               Evidence for keeping, tuning, restructuring or replacing
```

Some reading can run alongside other reading; conclusions about replacements depend on the diagnosis. Granular questions sharing the same sources should be handled together. Discovering a missing prerequisite changes the order, not the behavioural goal.

The research identifies the strongest source defects and observed episodes separately from unisolated causes of ore-positioning and pursuit complaints. It compares those findings with earlier conversations and commits, and uses the broader case studies to challenge the diagnosis. The proposed implementation experiments have not been conducted by writing this research.

## Existing reports are the baseline to extend

- [Architecture and Behaviour Map](<Architecture and Behaviour Map.md>) holds the first responsibility map.
- [History of Decisions and Failures](<History of Decisions and Failures.md>) holds the initial chronological synthesis.
- [Utility AI and Its Alternatives](<Utility AI and Its Alternatives.md>) holds the first external comparison.
- [Evidence and Open Questions](<Evidence and Open Questions.md>) holds reproducible capture observations and documentation discrepancies.

The authorised follow-through corrects README evidence and adds the owner's behavioural clarifications without changing gameplay. The ranked branching roadmaps are in [proposal/CLAUDE.md](proposal/CLAUDE.md).
