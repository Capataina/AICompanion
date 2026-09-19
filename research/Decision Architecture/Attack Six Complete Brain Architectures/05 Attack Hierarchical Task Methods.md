# Approach E attack — utility-ranked hierarchical task networks

This is a read-only architecture attack of Approach E at `b3a4ad94f`. It treats an HTN as a planner that chooses among applicable methods and decomposes a concrete task into partially ordered primitive work; it does **not** equate HTN with fixed family priority, a FIFO queue, or one uninterruptible script. Every comparison below grants the common execution foundation in the brief: observed facts, causal target binding, effect receipts, feasible body/hand grants, player intent, tri-state search, bounded incremental work, and a valid executable action while refinement runs.

## The question that decides E is narrower than “can an HTN plan?”

An HTN can represent the requested mixed work. Its question is whether authoring decompositions is the right primary source of choice for an opportunistic companion whose valuable sequence changes continuously with individual drops, dark tiles, enemy trajectories, player movement and learned weapon effects. The issue is not expressive impossibility. The issue is the amount of live state and online comparison that must be reconstructed inside methods before the hierarchy still decides better than the existing concrete-candidate machinery.

The product requires a companion that orders *specific* torch sites, drops, enemies, ore and weapon moves; continues useful work without ping-pong; stops an ordinary surface fight when the player descends; performs a useful available action while a more expensive answer remains unfinished; and changes a combat stretch when knockback creates piercing geometry. Those are explicit Expected-Behaviour demands in [README.md](../../../README.md), especially 2:00, 4:00, 8:00, 12:00 and 16:00. The existing 0.30.6 capture gives the negative: 68 direct collect↔torch transitions in ticks 7,773–11,125, including 700 ticks of only those two actions, where every torch attempt ended `replaced-before-interaction` and every collection attempt ended `replaced-with-drop-still-in-world`.

The repository already proves why task grain matters. `CollectNearbyItems` reselects the first current usable drop every preparation, while lighting has a current torch-site identity; `OwnCurrentActivity` concludes the open attempt when that identity changes. Together these are a supported mechanism capable of producing the recorded replacement loop, not an isolated causal attribution for the capture's exact score collapse. An HTN that only has tasks named `collect` and `light` would recreate that mechanism; an HTN whose methods bind a particular item and tile could avoid it, but then must specify the rebinding and repair policy this project needs anyway.

## Requirement matrix

| Requirement | What an online, utility-ranked HTN can provide | What has to be supplied beyond ordinary decomposition | Assessment |
|---|---|---|---|
| Finish one meaningful drop or torch without a percentage hold | A bound primitive can remain a method subtask until its receipt or invalidation. | A concrete identity, remaining effort, replacement rule and repair boundary. | Viable; the common execution foundation does most of the safety work. |
| Seven torch sites, three drops and a changing player | Partially ordered alternatives can represent several sites. | Dynamic candidate generation, dominance/pruning and an online way to rank permutations. | Viable only with a scheduler/search embedded in or beside methods. |
| Move player takes step one | Failed precondition can invalidate the affected subtree and repair from its frontier. | Spatial invalidation, target receipts and a current safe executable action. | Viable, but repair policy is a first-class design, not a free HTN property. |
| Bow knockback then shurikens through a new line | A method can encode a known tactical combo. | Prediction/learning over geometry, effects, timing, and alternative-move evaluation. | Poor as the primary representation when opportunities are discovered online. |
| Stop fighting outside while player descends | A fight method can be abandoned when its player-fit/reunion condition becomes false. | The same live intent, duration and separation calculation every contender needs. | No unique advantage; common-foundation contract. |
| Do useful work under a 4 ms-scale budget | A depth-first method can emit an early primitive action. | Incremental search state, explicit fallback quality, fairness and bounded expansion. | Feasible only if “pick first applicable method” is rejected. |
| Never mistake unknown reach for no reach | A method can defer rather than fail. | The common tri-state reach type and retry semantics. | Shared prerequisite, neither benefit nor limitation unique to HTN. |
| No arbitrary category ranking | Utility can rank applicable concrete methods. | Comparable utility across unlike methods and a tie/quality policy. | Viable, but an HTN method order is an arbitrary rank unless utility controls it. |

## What HTN genuinely contributes

HTN methods encode domain knowledge that pure goal/effect planning lacks. SHOP3 describes a method as a prescription that decomposes a task into subtasks and a planner that recursively chooses applicable methods until primitive actions, with backtracking on infeasible constraints; it also notes that ordered SHOP2 produces actions in execution order. That is directly useful for *known, reusable tactical shapes*: `dislodge-slime` can prefer “bow from the player side, confirm knockback, then damage”; `light-pocket` can carry “approach a site, place, reassess local darkness”; and `finish-vein` can make “mine current reachable tile, collect its output if still cheap, reconsider” an explicit local protocol. [Goldman & Kuter, 2019, pp. 2–3](https://rpgoldman.goldman-tribe.org/papers/2019-els-SHOP3.pdf)

HTN also gives a useful place to declare *why* a local sequence exists. A method can state its resources, preconditions, expected receipts, bounded scope and replan trigger. That would make the common execution foundation inspectable rather than scattering continuation conditions through activity classes. Goldman’s HTN semantics work specifically identifies the plan-time/execution-time split, monitors, plan revision and repair as executive concerns rather than things the decomposition alone settles. [Goldman, 2009](https://ojs.aaai.org/index.php/ICAPS/article/view/13377)

Those strengths survive the attack if E is constrained to compact, general method schemas with live binding. A method such as `remove-imminent-threat-while-passing(worksite, threats, weapons)` is general; hard-coding `slime-near-copper-then-torch` is a recipe. The former delegates site, target and weapon choice to current evaluators and can repair on a receipt. The latter is a brittle authored scene.

## The central cost is method knowledge, not the decomposition loop

The primary external literature agrees that the domain model is substantial human work. The HGN comparison says HTN methods require task-specific domain models and that even writing a new model generally takes considerable human effort; its experiment used roughly half the SHOP2 model size while retaining comparable performance, precisely because goal semantics removed SHOP2 bookkeeping methods. [Shivashankar et al., 2012, pp. 1–2](https://www.cs.umd.edu/~nau/papers/shivashankar2012hierarchical.pdf). SHOP3 likewise describes methods as the means by which the hierarchy encodes preferences; in normal SHOP2, applicable methods are considered in their specification order. [Goldman & Kuter, 2019, p. 3](https://rpgoldman.goldman-tribe.org/papers/2019-els-SHOP3.pdf)

That is a concrete danger for this companion. Method alternatives must distinguish at least: a torch now, a drop now, a drop while returning, a drop after a torch, several torches in an order, a pot whose contents are unknown, a threat that must die before work, a threat worth a cheap shot from the current body, a fight whose player-relative reason expires, a constrained cargo state, and a player that changes direction. If every interaction becomes a distinct method, the hierarchy grows with scene combinations and moves the arbitrary category ranking from `switch` statements to applicability predicates and method order. If it does *not* enumerate those combinations, it needs a general online candidate/order evaluator inside the method, which means the proposed HTN has ceded the hard part to a schedule/receding-horizon layer.

This is intrinsic to E only when E claims authored decomposition is the primary chooser. It is **not** an intrinsic HTN flaw when the hierarchy is merely a library of local protocols and utility/search chooses concrete bindings and method alternatives. In that narrower role, it is a useful execution vocabulary, but not enough to be the top-level architecture.

## Counterexamples

| State | E’s failure if implemented naively | Minimal condition that avoids it | Classification |
|---|---|---|---|
| Equal useful drop and dark torch, both two seconds away | `collect` and `light` methods alternate whenever their local estimates cross. | Bind the selected item/tile and compare remaining course value against a repaired rival. | Missing continuation/rebinding contract, shared with all contenders. |
| Seven overlapping torch sites and three drops | One authored `light-area` method commits to a site order that becomes bad after a placement changes darkness or a player moves. | Generate sites online; retain only causal completed/invalidated work; re-rank next site after receipts. | HTN methods alone cannot provide this selection quality. |
| Player picks up the planned drop | A total-order plan treats the missing primitive as failure and either throws away useful suffixes or continues invalidly. | Mark the binding invalid; repair only dependencies that read it; preserve unaffected torch work. | Executive/repair burden, not a reason to reject all HTNs. |
| Bow hit creates a piercing line | A pre-authored `bow-then-shuriken` sequence misses an emergent better shot; authoring every geometry pattern is impossible. | Primitive combat move selection remains online and consequence-aware after every hit. | Intrinsic limitation of recipe-first tactical HTN. |
| Trivial foe beside nearly finished ore | A combat-root method wins because “fight” is high-level, or an ore method holds because it is already decomposed. | Compare specific remaining harm and specific remaining work under common utility; no fixed root rank. | Avoidable only by utility at concrete-method level. |
| Enemy outside while player descends | A stored `finish-fight` method keeps fighting outside, or repair drops all work without acting while it searches. | Player-fit invalidates the fight subtree; common executor keeps immediate safe return/shot choice available. | Shared live-intent/execution prerequisite. |
| Boss or world event starts | A generic boss branch that merely marks a category may prescribe the wrong response. But the Expected Behaviour explicitly requires the encounter policy to stop optional work, range wider, and optimise survival before damage. | Bind that explicit encounter contract to the game's boss/event facts; use live danger and completion consequences within it, rather than smuggling in a general combat-always-first rank. | This is a required product exception, not evidence against all priority branches; the HTN cost is only the additional event contract and its repair surface. |
| Unfinished flood / 4 ms cut before first costly candidate | Depth-first decomposition reports “no method” or spends the frame searching. | Carry `Unknown`, resume bounded frontier and execute a validated incumbent/from-here action. | Shared incremental-search prerequisite; classic HTN does not supply it. |
| Repeated new drops or enemies | New methods constantly become applicable and starve older, still-useful work. | Fair bounded repair plus a course/value comparison based on remaining work, not arrival time. | Unique pressure on online utility-ranked E; cannot be fixed by a static method order. |

## Performance and implementation attack

No Terraria timing has been measured for E. The current repository’s 4 ms combat history shows why an unbounded planner cannot be asserted safe: the recorded 40 ms failure was a unit conversion bug, and the later remedy retained a 4 ms budget with an explicit from-here fallback rather than raising the limit (`git log -1 --format='%B' 457b168`; `145be5a`). The source itself proves a candidate scan can dominate: the NearbyAssistance guide records 7.4 ms with a three-site bound, 25.5 ms unbounded, and 13.4 ms under the tick deadline for a wholly dark floor. Those are dated suite measurements for that existing scan, not forecasts for an HTN.

Asymptotically, decomposition is cheap only when one method applies and binding is already known. At one compound task with `b` applicable methods and depth `d`, a naïve full search has a branching worst case exponential in the decomposition tree. Partial ordering, alternative weapon moves, target sets and optional insertions make the branching multiply. SHOP3 explicitly describes recursive depth-first search, backtracking, mutable rollback state and stack exhaustion for difficult searches or long plans. [Goldman & Kuter, 2019, pp. 3–4](https://rpgoldman.goldman-tribe.org/papers/2019-els-SHOP3.pdf). The conclusion is not that E cannot meet the budget; it is that a 4 ms contract requires explicit resumable frontier state, dominance pruning, an expansion budget, and a quality-ranked executable prefix. Those mechanisms are materially closer to Approach C’s incremental consequence search or Approach B’s repairable schedule than to a conventional HTN executor.

The implementation surface is large even after granting the shared foundation: a method language or typed method objects; live bindings for targets/resources/observations; partial-order and resource compatibility representation; invalidation dependency graph; incremental method-search frontier; utility comparison across method alternatives; prefix executor; local repair; discard rules; recorder explanations that distinguish method unavailable, unknown, superseded, invalidated and completed. Every one needs fixtures that mutate the specific world fact it depends on. The hierarchy does not remove current candidate, trajectory, reach or native-interaction work; it consumes their outputs.

## What should be borrowed and what should be refused

Borrow HTN’s **method contract**, not an HTN root that chooses the whole companion’s day. A typed local method should declare: concrete binding(s); prerequisites read; primitive grants; receipt that marks progress; effects it expects rather than assumes; bounded validity region; and the smallest repair frontier when the world changes. This is valuable for a vein’s mine/collect loop, a torch-region visit, a weapon maneuver that has a learned causal purpose, and possibly a drop-on-the-way insertion.

Reject three shapes. Reject ordered method declaration as policy: SHOP-style ordered consideration is exactly an arbitrary priority if no online utility ranks methods. Reject a permanent stack/queue of “then do X,” because the project deliberately gives ownership to one current activity and the player can remove or invalidate step one. Reject authored scene recipes as the general way to obtain emergent combat: Demon-Eye knockback geometry requires online opportunity evaluation after actual hits.

The strongest competing approach is **B, an event-repaired route and task schedule**, provided its schedule is over concrete, online-generated opportunities and has the common causal execution foundation. B wins where the dominant problems are present in this project: item/tile identities re-auditioned each tick, player/world edits invalidating only part of a course, and a need to keep doing a useful prefix while discovery remains incomplete. Its condition of victory is that local consequence evaluation already prices the tactical effects that matter. If measured scenes show recurring, reusable protocols whose causal effects cannot be expressed without manually rebuilding the same precondition/receipt/repair logic, then a narrow HTN method layer above B wins by removing duplicated protocol code.

Approach E wins over B only under a falsifiable condition: the critical player-visible improvements come from a small set of stable, reusable decomposition schemas, and those schemas retain a substantially smaller/clearer repair surface than B’s direct schedule while matching it on the mixed-site and moving-player scenes. Nothing in the current capture establishes that condition.

## First falsifying experiments

1. **Mixed-site online binding.** Give a method layer seven dark sites, three drops, a moving player and exact current reach facts. Bound it to the same planning allowance as the existing chooser. It must emit a legal useful primitive before full expansion, produce no collect/torch alternation until a recorded relevant invalidation, and name its selected bindings. A failure with stable facts disproves the claim that utility-ranked methods alone supply continuity.

2. **Receipt-local repair.** Start `collect(item A) → torch(tile T)`, then let the player take A. Assert that the method tree preserves T if its prerequisites remain true, discards only A-dependent work, and emits either the first repair primitive or the valid prior prefix within the budget. A full restart or an execution of A falsifies local repair.

3. **Emergent combat geometry.** Construct a moving Demon Eye, zombies, bow and shurikens. After a first hit changes the line, assert the second move is selected from observed geometry and weapon knowledge rather than a pre-authored target sequence. If adding a new projectile behaviour requires a scene-specific method, reject E as tactical primary control.

4. **Arrival flood.** Insert one new low-value drop every rescore beside a nearly complete ore and a more urgent torch. Verify older work cannot starve merely because new methods appear, while no percentage incumbent bonus or fixed category ordering exists. Failure separates an online-scheduling defect from HTN decomposition itself.

5. **Cost instrument.** Count method candidates, bindings, expanded nodes, repaired nodes, discarded nodes, and time per decision over the existing dark-floor, crowd and pure-swap fixtures. Compare equal-quality useful-prefix rate and worst decision time against B. Until this exists, no 4 ms claim is established.

## External evidence and limits

Desk research supports the narrow claims above. SHOP3 provides primary implementation evidence that HTN methods are domain prescriptions, method choice/backtracking is search, ordered SHOP2 can encode preferences, and long/difficult search stresses the recursive executor. Goldman’s ICAPS semantics paper supports treating execution, monitoring and repair as explicit work. The HGN comparison supports the model-authoring cost and gives counterevidence to fatalism: hybrid goal/method semantics used smaller models with comparable performance in its evaluated domains. Those are planning-domain results, not evidence of Terraria performance.

The practitioner room did not produce usable corroboration in this pass. Search surfaced r/gameai discussions about loop prevention and dynamic goal changes, but the readable thread fetches returned platform errors; I do not count their snippets as evidence. The room therefore holds **no consensus** in this report. The corpus was r/gameai/r/gamedev and public planner repositories, which is where game-practitioner implementation experience would live; the external claims above rest on primary papers instead.

## Final classification

E is **a valuable local-protocol layer and a weak primary architecture for this companion unless paired with an online concrete schedule or bounded consequence planner**. It can represent the desired work and should not be rejected as “rigid scripts.” Its durable contribution is named decomposition, resource/receipt contracts and repair boundaries for recurring local protocols. Its failure mode is moving the actual choice problem into an expanding authored method catalogue or hiding an ordinary scheduler/search inside methods. The current evidence favours B as the primary continuation/repair shape, with narrow HTN methods admitted only where repeated, measured local protocols justify them.

## Commands and outcomes

```text
git status --short
# exit 0; user-modified proposal 04 and ten untracked Ledger JSONLs remained present.

python3 "research/Evaluation and Observability/Probes/ReproduceDecisionCaptureCounts.py" --root <repository>
# exit 0; reproduced the 0.30.5/0.30.6 combat, swap, attempt-cause and credit counts cited above.

git log -1 --format='%B' 573d9d4
git log -1 --format='%B' 457b168
git log -1 --format='%B' 145be5a
# exit 0; read history of current order, four-millisecond budget, and combat hold.
```
