# Behavioural requirements determine the architecture comparison

**Status: research synthesis, 12 September 2026.** The owner’s descriptions in this conversation and the README’s Expected Behaviour are the product authority. Historical conversations clarify how the requirements developed; source and telemetry describe implementation and outcomes. A research paper can show what a mechanism represents, but cannot decide what this particular companion should value.

The purpose is a second presence alongside a single player: capable of autonomous nearby help, responsive protection, competent movement and coherent actions. It has a deliberately closed set of authored abilities and weapons for compatibility. It does not accept arbitrary player equipment or become an independently commanded worker. The architecture must reduce the gap between that behaviour and recorded play without making every new cave, enemy or mastery upgrade demand a new scenario branch.

## A contract above utility exists even if it is not another AI

Utility compares things the product permits and the body can attempt. Product boundaries, authoritative observations, capability descriptions and action ownership therefore sit logically above or beside a chooser. This does not require one giant controller. It requires agreement about what each result means.

```text
Product contract: companion role, permitted effects, closed abilities, player relationship
        │
        ├─ World and capability evidence ──► eligible opportunities and uncertainty
        │                                      │
        ├─ Preference / selection policy ◄──────┤
        │              │                       │
        │              ▼                       │
        ├─ Activity progress and action phase  │
        │              │                       │
        ├─ Position / route / local control ◄──┘
        │              │
        └─ Shared output ownership ──► native body, tool, weapon, light effects
                                       │
                                       └─ measured outcomes update evidence
```

The map is a set of responsibilities, not a class diagram to implement. Existing code already owns portions of each. Replacing utility need not replace sensors, native collision, projectile tracing, cargo or rendering if the contracts between them remain explicit. Conversely, retaining utility does not require preserving its current candidate representation or coordinator order.

## Accepted requirements and unresolved choices stay distinct

| ID | Behavioural requirement | Why it matters for design | Current authority |
|---|---|---|---|
| B01 | Stay usefully near the player while allowing short independent excursions. | Player-relative time and opportunity cost matter more than a single leash distance. | README story and current owner clarification. |
| B02 | Sustained rapid travel discourages optional work; moving within one area can still invite help. | Player intent must be inferred from context with uncertainty, not equated to held tool or instantaneous velocity. | Current owner’s cave/torch/travel examples. |
| B03 | Shoot during ordinary travel when a legal worthwhile shot exists. | Pursuit positioning and immediate aiming have different targets and cadence. | Current owner clarification and historical independent-hands decision. |
| B04 | Active pickaxe/axe work occupies a coherent hand-use period, including normal gaps between strikes. | Action phases, not a whole mining/chopping behaviour label, govern exclusivity. | Explicit current owner clarification. |
| B05 | Held light may yield to firing and resume; supplied placement is a separate action. | Useful light is a QoL exception, not evidence that every tool can weave with weapons. | Current owner clarification and README. |
| B06 | Reconsider interrupted work by current value and remaining work, without obligatory resume or abandonment. | Identity and progress may be retained while preference changes. | Ore–bat–drops and tree examples. |
| B07 | Equivalent remaining work counts irrespective of who did earlier work. | A progress signal cannot reward the companion’s own sunk effort. | Explicit current owner answer. |
| B08 | Sharing an ore vein is acceptable; usually choose a separate tree when the player is chopping one. | Task/site selection needs cooperative context; a blanket reservation on the player’s work is wrong. | Explicit current owner answer. |
| B09 | Weigh effective harm, time to harm, escape, player risk and remaining effort. | Enemy proximity or raw contact damage alone cannot settle whether to finish, dodge or guard. | Owner’s weak zombie, strong worm and incoming projectile examples. |
| B10 | Drops are independent opportunities; breaking something does not obligate collecting its drops. | Collection needs fresh reach, capacity, return and marginal-cost evidence. | Explicit pit/ore/drop examples. |
| B11 | Begin useful travel under uncertainty only through suitably safe intermediate states. | Full-route-first and blind greedy advance are both inadequate defaults. | Owner’s progressive-search example. |
| B12 | Ordinary voluntary excursions should not strand the companion; following a player into a one-way region has different permission. | Forward feasibility, return feasibility and product permission are separate facts. | Current pit example and historical one-way discussion. |
| B13 | New or changed capabilities affect movement, reach, tool eligibility and risk coherently. | Capabilities and cached proofs need one versioned authority. | Player scaling and mastery examples. |
| B14 | Pursuit should have a comprehensible nearby purpose and a feasible prospect of useful positioning. | Being sensed, harmful, shootable now and worth pursuing are different predicates. | Owner’s distant/behind-wall hunting complaint. |
| B15 | Protect homes and other explicit edit boundaries; do not excavate arbitrary terrain for a route. | Some conditions are permissions, not preferences that another benefit can outweigh. | README, project rulings and historical rejection of rescue mining. |
| B16 | Continue a coherent NPC lifecycle through damage, downing, revival, player death and recovery, without teleporting. | Exceptional control modes need ownership and termination independently of ordinary work. | README and project rules. |
| B17 | Expose understandable native controls and diagnostics without making the player micromanage tasks. | More planner power must not turn the companion into an orders interface. | README and project identity. |
| B18 | Generalise across representative changing terrain and modded enemies using supported observations and a closed ability kit. | Compatibility requires authority boundaries and uncertainty, not enumeration of all possible scripts. | Explicit compatibility rationale. |

The owner has **not** selected flat versus grouped behaviours, a particular utility formula, a mandatory persistent intention mechanism, a universal acceptable damage threshold, or a replacement search algorithm. These remain research alternatives. “It depends” is a requirement for contextual trade-offs, not permission to leave the decision undefined forever; the implementation will need an inspectable policy whose consequences can be judged.

## An opportunity is more concrete than a behaviour name

A behaviour such as mining names a kind of help. An opportunity names the particular ore/site, what success means, what must hold to attempt it, what remains, and what it costs to leave the player and return. A useful common representation can therefore be shared without pretending that mining, combat and light use have identical mechanics.

For each opportunity, compare benefit, remaining effort, approach/return time, projected harm, player relevance, uncertainty and switching cost. Its target identity must survive a moving body but end when the target ceases to exist or changes generation. Feasibility should carry the scope of its evidence: a current shot, a reachable firing pose, a safe route prefix or a native tool hit. One generic `reachable=true` cannot represent all four.

This does not imply modelling every quantity as a probability. A bounded heuristic with a named source and failure envelope can be more honest than a fictitious expected-value calculation. Damage observations can gradually calibrate threat estimates; geometry can establish certain movement facts; utility can compare preferences. The contracts should distinguish these kinds of evidence rather than convert them all to arbitrary floating-point confidence.

## Progress changes future cost; effort already spent is not a reward

In the tree example, one hit remaining often makes finishing attractive because its **future** cost is small. That remains true if the player delivered the earlier hits. If the player leaves or a major threat appears, the tree’s current benefit may no longer justify even that small cost. Conversely, a small interruption need not erase a still-valid target or the completed physical work.

A candidate reasoning model is:

`current value ≈ remaining benefit − remaining effort − context-specific risk − player delay − switching cost`

This is an explanatory decomposition, not a proposed literal additive implementation or unit system. Multiplicative utility, a constrained value model, ranked preferences or a short planner can encode parts of it. The research comparison asks which representation preserves the desired relationships, remains interpretable and is affordable. Hard permissions stay outside a compensatory trade-off: no amount of ore value permits a protected edit.

Enemy health illustrates a subtle limit. A nearly dead enemy can be inexpensive to finish, but low health alone cannot justify chasing it far from the player or through a sealed wall. The marginal benefit includes threat removed, useful loot and the purpose of voluntary pursuit; remaining effort includes getting another useful shot. “Damage dealt by this companion” is a poor universal progress signal, especially when the player or another enemy changed the target’s health.

## Incidental actions compete by marginal cost

The ore–bat–drops sequence can arise through repeated contextual choices. Guarding becomes valuable because the player is threatened. After the threat ends, nearby drops may have almost no additional travel cost and can be collected; distant drops may lose to the ore. When other useful work ends, those same drops can become the best local opportunity. Nothing in that account requires a permanent script or forbids retained task knowledge.

The transferable lesson from [RimWorld work scheduling](<../Game and Mod Case Studies/RimWorld Work Scheduling.md>) is to make interruption and marginal detour explicit. Its queues and reservation machinery solve a different game’s problems. Here the item can fall into a pit, the player can move away, or a capability can make a formerly one-way pickup returnable. Eligibility and value must be recalculated from those facts rather than inherited from the item’s origin.

The same distinction prevents an overly broad harvesting family. Ore, trees and pots share search/approach/tool or break interactions, but differ in progress, drop value, target shape and cooperation. A common activity contract is justified by these shared questions. A mandatory shared parent winner is a separate hypothesis to test.

## Three grouping alternatives deserve a fair comparison

| Grouping | What selects | What can coexist | Main advantage | Main risk and deciding evidence |
|---|---|---|---|---|
| Flat opportunities with shared activity contracts | Concrete alternatives across all eligible purposes. | Travel, aiming and light under action-level resource grants. | Preserves direct cross-purpose comparison and minimises new arbitration. | Large candidate sets or side-effectful scoring; E04/E12 measure coverage and cost. |
| Utility families with utility children | A family such as companionship, protection or work, then a concrete activity. | The same resource-compatible outputs; grouping does not decide concurrency. | Concentrates authoring and relevance checks when families have meaningful common policy. | Parent/child score distortion and double commitment; E04 tests family-size and urgency invariants. |
| Utility goals with local plans/options | A desired outcome, then an explicit means or short sequence. | Compatible actions whose preconditions and shared resources permit them. | Represents enabling consequences and alternative methods. | False symbolic facts, horizon effects and unnecessary planning; E13 supplies the discriminator. |

Hunt and guard share combat mechanics but different product purposes: voluntary opportunity versus player protection. Following, wandering and light may all support companionship but have different success predicates. The folder taxonomy should not erase these distinctions. A family can expose shared observations and default cost models while preserving purpose-specific value and evidence.

## Compare alternatives on these axes before choosing a winner

The axes come from the owner’s concern about churn, their accepted behaviour examples and the repository’s repeated planner/body/observer mismatches. They are not inferred from which architecture is already built.

1. **Behavioural expressiveness:** can the mechanism represent context-sensitive autonomy, protection, progress and opportunism without one rule per scenario?
2. **Physical and epistemic honesty:** can it carry uncertain observations, directed returnability, changing abilities and native execution outcomes without pretending a preference is proof?
3. **Concurrent-action coherence:** can it keep travel/aiming legal while preserving coherent tool work and urgent cancellation?
4. **Computational fit:** can expensive queries be retained, scheduled and measured under the actual tick budget?
5. **Compatibility:** does it consume stable native/closed-kit contracts rather than assume every external item/enemy script?
6. **Diagnosability:** can an observed failure be attributed to one stage using reproducible evidence?
7. **Cost of change and reversibility:** can a limited experiment establish value before a wide rewrite, preserving verified movement and gameplay behaviour?
8. **Durability of authoring:** can a new ability or target type attach through a documented contract without rewriting every behaviour?

The ranking is ordinal and conditional. There is no credible dataset here from which to calculate that one architecture has a numerical probability of achieving the entire README. A roadmap earns confidence by explaining more observed failure classes with fewer unverified dependencies, while exposing what would falsify it.

## Alternatives outside the top three remain explicit

A pure whole-brain FSM can be sufficient for a simpler companion and remains useful for local lifecycles. A reactive priority BT can express urgent boundaries clearly; a utility-aware BT can combine graded choices with execution. Neither is rejected merely by its name. They enter the comparison when their explicit abort/ownership semantics outperform the existing coordination, not because a tree diagram itself grants intelligence.

GOAP and HTN differ: GOAP can search action-effect combinations, while HTN constrains legal methods through authored decomposition. Either can remain local beneath utility goals. A full POMDP would represent belief and uncertainty more formally but needs transition/observation models, values and a computational approximation the project has not established. A receding-horizon controller can improve local controls without owning high-level preferences.

Reinforcement learning, imitation learning and learned residuals remain research candidates, particularly for bounded estimates or preferences. A whole learned companion currently asks for training data, reproducible simulation, reward design, held-out evaluation and safe behaviour under unseen mods simultaneously. An LLM used as a per-tick controller adds latency, external model/runtime dependence and weak physical guarantees; no retrieved evidence establishes an advantage for this closed, fast control problem. Offline assistance with authoring or analysis is a different use and does not require inserting a model into gameplay.

These alternatives are not permanently rejected. The [decision literature](<Decision, Commitment and Computation.md>) and [utility comparison](<../Utility AI and Its Alternatives.md>) state their evidence and reopening conditions. The three roadmaps use this comparison to choose incremental tests, not to certify that all remaining architecture questions have one correct answer.
