# Three purpose families with shared companionship and safety

## The discussion selects a direction, not a verified implementation

On 12 September 2026, the owner chose to fold a three-family organisation into the preferred proposal before implementing it. The motivation is clearer responsibilities and extensibility while preserving contextual choices. The accepted direction is Gathering, Combat, and Nearby assistance. Local utility comparisons choose concrete activities; a top-level utility comparison chooses between the families' offers. This document owns the follow-up discussion and its alternatives. The implementation simplification limits are in [Keep the implementation smaller than the responsibility diagram](<../proposal/01 Preserve Utility and Repair Activity Contracts.md#keep-the-implementation-smaller-than-the-responsibility-diagram>): shared scoring, one safety owner, one grant boundary and one evidence stream, with separate facts retained where outcomes can disagree. [Path 1](<../proposal/01 Preserve Utility and Repair Activity Contracts.md>) owns the conditional implementation roadmap. No gameplay change or live improvement follows from this documentation decision.

The original research ranked flat contract repair ahead of grouping. The discussion changes that recommendation: family organisation joins Path 1, with the old flat comparison retained as an experimental reference. Path 2 now asks whether a larger shared activity-execution framework is warranted beyond the lightweight ownership already required in Path 1. Path 3 still asks whether bounded foresight improves consequential decisions. They can build on one another, but neither further layer is mandatory.

## The system in ordinary language

The companion notices the world, the player's recent activity and its own actual capabilities. It estimates what is dangerous, what useful work exists, and what separation and reunion would cost. Each family considers its available activities and prepares a concrete offer. The top-level chooser compares those three offers. The selected activity asks for a position that enables its purpose, and shared movement tries to get there or make a safe useful intermediate step. Actual outcomes tell the activity whether to continue, adjust, stop or compete again.

Weapons continue choosing available attacks independently when the hand permits. Reflexes still use shared movement, but their interruption must account for the movement and activity already underway. Ordinary following recovery and downing retain their own lifecycle restrictions.

| Family | Activities | Boundary |
|---|---|---|
| Gathering | Mine ore; chop trees. | Share approach, work and cancellation mechanics without making trees and veins identical. Sharing a vein is acceptable; prefer a separate tree from the player's. |
| Combat | Protect the player; pursue a worthwhile attack opportunity. | Enemy presence alone does not justify choosing this family. Safety, including kiting and sustained escape, is shared across all families. |
| Nearby assistance | Improve lighting; collect worthwhile drops or accessible pot contents; keep company. | Keep-company supplies actual movement or idling even when no optional opportunity exists. Pot breaking can be incidental or part of a worthwhile collection trip; it has no separate behaviour. |

These are ownership groups, not exclusive permissions. Contact pickup can happen during combat; held light can support another purpose; an incidental pot may be handled during compatible travel. Active pick/axe use occupies the hand coherently, including the gaps between strikes. A genuine interruption can end that use; weapon cooldown alone cannot justify weaving a shot between tool hits. An incidental action must fit the available channels and preserve the main activity's meaning.

## Each family uses utility, with one common meaning for an offer

The intended answer to “does each family have its own small utility AI?” is yes at the level of decisions, but not three separately invented scoring frameworks. Use shared evaluation machinery and common offer fields. Family-specific considerations describe why an activity is useful; shared considerations describe risk, capabilities, remaining work, separation and reunion. Hard native permissions remain permissions, not small scores that can be outweighed.

The initial comparison should follow this order:

1. Observation and candidate maintenance establish the available evidence independently of whether a family won last time. Scoring must not secretly advance a job or mutate another candidate.
2. Each family evaluates its eligible concrete activities on the common scale. It retains the best offer and its identity, evidence, uncertainty and explanation.
3. The parent compares those offers. Initially the family value is the best eligible child's value, with no extra family multiplier or duplicate commitment bonus.
4. The winning family executes that offered activity. If its target or feasibility changed during preparation, revalidate or reconsider; do not silently execute a different, lower-value child under the old offer.
5. Ongoing activity state can retain useful work and computation, but current value and meaningful changes remain grounds for interruption or abandonment.

For an unchanged candidate set, deterministic maximum selection at both levels selects the same maximum as flat selection, provided eligibility, adjustments and tie-breaking match. That is a useful reference property, not evidence of smarter decisions or lower CPU cost. Summing children rewards a family for having more candidates; averaging can punish it for exposing harmless weak candidates. Multiplying parent and child distance penalties can suppress worthwhile work twice. More expressive aggregation remains an experiment requiring a demonstrated benefit.

Expensive route and trajectory queries need bounded refinement. An unexamined child must remain unknown, not disappear as impossible. A family that has not won recently still needs enough observation/refinement to discover a new urgent or valuable offer. The selected family's internal search is not licensed to freeze out its siblings. Exact refresh cadence and scheduling budgets remain implementation questions to settle against measured costs.

The owner explicitly asked how to diagnose the parent choosing combat while combat finds no valuable action. [Path 1's offer-diagnostic table](<../proposal/01 Preserve Utility and Repair Activity Contracts.md#a-chosen-family-must-have-a-concrete-activity-to-deliver>) records the answer. Prevent the ordinary case by requiring a specific eligible child before a family can win. Then distinguish normal world changes from a parent selecting an absent/stale offer, a family omitting a useful child, unchanged-input disagreement between evaluation and execution, an unusable destination, and a physical/control failure. This is a contract to test, not evidence that either utility level is inherently defective.

## Companionship belongs to every activity, with an explicit way to rejoin

The owner rejected following as an unrelated top-level competitor. Every opportunity should account for the player's location, recent motion and local activity, without claiming to know a future destination. What matters is the expected time apart and practical reunion cost, not merely current straight-line distance. A short exposed ore job ahead can fit a journey better than slow work immediately behind the companion. Terrain, available movement, danger and changes in player pace can reverse the preference.

Removing the top-level follow choice does not remove the need to generate a reunion destination. Nearby assistance's keep-company activity provides useful movement when no stronger opportunity exists, and the shared companionship assessment discourages optional work as separation becomes costly. Repeated individually cheap detours must account for separation already accumulated; otherwise “one more quick job” can indefinitely postpone reunion. Avoid a mandatory collect step after mining: inaccessible drops can remain while the companion returns.

This is a proposed architecture for an accepted product requirement. [README Expected Behaviour](../../README.md) already describes sustained travel versus local activity and now explicitly allows quick useful work during travel. Its prose remains independent of family names and algorithms.

## Shared safety can adjust an activity or temporarily own escape

Kiting, climbing to air, jumping over an enemy, taking cover and reaching high ground are methods of reaching a safer situation. They should be compared against the same observed threats and physical consequences. A move that avoids a zombie but prevents surfacing is not successful safety merely because one collision disappeared.

Shared safety applies during every family and owns both imminent avoidance and sustained escape. It can adjust an ordinary activity's method, or suspend that activity and request movement towards a viable safe state when ordinary work cannot continue sensibly. It must generate escape movement even when no family offers useful work and no enemy is present. Reflexes are short responses within this shared responsibility. All responses use the single movement/control boundary and consider the currently proposed trajectory, interruption cost and resulting state.

The objective is remaining alive and useful, not maximising stored health regardless of everything else. Trivial effective damage can be acceptable for worthwhile work; lethal exposure or a stranding move is a different cost. Player protection must evaluate the actual approach, so a zombie between the actors does not become an obstacle the companion blindly runs through. Shooting from here, jumping over, taking another route or first seeking space can all be alternatives. No universal “never take damage to guard” rule was accepted.

Boss and event encounters may change the context in which these shared activities are evaluated: movement envelope, optional work and protection policy can change without duplicating a full boss controller. Recognising a boss flag alone does not implement the expected encounter behaviour or predict unfamiliar scripts. Existing authored boss/event requirements remain distinct from ordinary small-risk trade-offs.

## Preferences are contextual rather than a fixed family ladder

The owner's concern is that Terraria almost always contains enemies, so incidental enemy presence must not monopolise activity selection. Gathering should commonly beat unnecessary pursuit. That does not establish Gathering > Combat > Assistance in every situation, nor an unconditional order inside a family.

| Paired situation | Desired comparison |
|---|---|
| Proven usable ore versus a remote nonthreatening enemy | Gathering usually wins. |
| The same ore versus a consequential imminent threat to the player | Effective protection usually wins. |
| One remaining ore hit versus an enemy that cannot arrive before completion | Finishing can win, subject to actual harm and escape conditions. |
| Slow difficult work versus useful cheap lighting | Nearby assistance can win. |
| Enemies exist but there is no worthwhile intervention or pursuit | Presence alone must not make combat win. |
| No optional work fits sustained player travel | Keep-company generates movement rather than merely assigning every job a low score. |

Values still require authoring and calibration. A general architecture does not remove product judgement. Compare these paired situations rather than inflating one family weight until a single scene passes. The fixed torch → pot → loot → wander ordering was considered and declined because it can prefer costly lighting over nearly free loot and make every pot a compulsory destination.

## Three corrections prevent the explanation becoming a false diagnosis

**Current mining already has feasibility checks and partial progress state.** It retains a vein, checks tool/approach conditions and revisits uncertain approaches. Movement progression and tool invocation are weaker than productive job progress, which is why the research calls for consistent activity-owned outcomes. The claim “the current system has no progress judgement” was an overgeneralisation, not an accepted finding.

**Current reflexes already use shared movement.** `CoordinateBrainTick` calls `Movement.AvoidThreats`; `CoordinateMovement` delegates to `Navigator.AvoidThreats`, which interrupts the retained route and requests movement with threat information. The concern is interruption policy and resulting safety, not a second unrelated jump implementation. Source establishes that path; it does not alone prove which captured downing it caused.

**Route, arrival and productive outcome remain separate facts.** A route proposes travel; arrival describes the body's achieved position; a purpose-specific check says whether that position enables work; a native outcome says whether the work occurred. Path 1 connects them and exposes failure at their boundary. It must not rename route discovery as guaranteed mining success.

See the [dated source audit](<../Implementation Evidence/Decisions, Activities and Shared Controls.md>) and current [coordinator](../../Companion/Brain/CoordinateBrainTick.cs), [shared movement](../../Companion/Brain/SharedMovementSystem/CoordinateMovement.cs), and [navigator](../../Companion/Brain/SharedMovementSystem/MovementExecution/Navigator.cs). Follow-up source was read at `739995a`; gameplay source still matches the research baseline.

## The arsenal remains an independent consumer of compatible hand time

The accepted scope preserves the weapon/target/trajectory algorithms for this restructuring. Pursuit asks whether moving can create a useful attack opportunity; the arsenal chooses the useful attack available from the current body. Those targets need not be identical. Positioning can query existing attack feasibility without rewriting weapon selection. Activity progress must not use a shot at an unrelated target as proof that pursuit succeeded.

Hand admission and shared control remain in scope, including exceptional paths. Keeping arsenal logic does not mean granting weapons the hand during coherent tool work or certifying universal aiming correctness. Any independently discovered arsenal defect needs its own evidence and scope decision.

## Research supports the components, not an automatic three-family win

Kevin Dill's [Structural Architecture: Tricks of the Trade](https://www.gameaipro.com/papers/structural-architecture-tricks-of-the-trade.html), published in Game AI Pro (2013), describes hierarchical reasoning, multiple decision-makers and reusable logic across game-AI architectures. It supports grouping for organisation and execution; it does not prove a performance or quality gain for this companion. David Graham's [An Introduction to Utility Theory](https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter09_An_Introduction_to_Utility_Theory.pdf), also Game AI Pro (2013), supports contextual factors and consistently comparable utility. Sutton, Precup and Singh's [Between MDPs and Semi-MDPs](https://people.eecs.berkeley.edu/~russell/classes/cs294/f05/papers/sutton%2Bal-1999.pdf), Artificial Intelligence (1999), formalises temporally extended options with initiation, internal policy and termination; applying the organisational concept does not require learning the companion through reinforcement learning. These sources were retrieved on 12 September 2026 during the discussion.

The [Starsector case study](<../Game and Mod Case Studies/Starsector Ship and Weapon AI.md>) supplies inspected mod examples of distinct movement, attack and firing-sequence ownership. Its base-game source was unavailable, and the mod does not establish this exact family arrangement. The [RimWorld case study](<../Game and Mod Case Studies/RimWorld Work Scheduling.md>) supplies contextual work and incidental-action examples with compatibility limits. Together these sources support composition and explicit execution boundaries; there is no controlled comparative result or practitioner consensus establishing three families as optimal here.

## What would change the recommendation

Retain the three-family direction while measuring whether it makes ownership clearer and preserves valuable choices. If grouping alone changes a matched winner, inspect candidate maintenance, tie-breaking, double penalties and hidden children before changing preferences. If the same valid activity repeatedly fails physically, repair its destination or movement contract. If common ownership remains cumbersome across many activities, compare Path 2's stronger shared execution framework. If correct immediate evaluation misses an enabling consequence, compare Path 3 locally. If grouping adds no useful organisation or scheduling benefit, flatten the runtime comparison while preserving the shared contracts and recorded reason for the result.

The initial held-out comparisons should include an empty world with a moving player, a cheap job during travel, cumulative detours, useful work with harmless enemies present, urgent protection in a usually low-valued family, drowning during gathering, a defensive move that compromises surfacing, a pot encountered during another journey, and changed capability/terrain invalidating a once-good offer. The recorder must identify family and child offers, what was unexamined, shared context, requested and granted controls, interruption reasons, destination predicates and native outcomes. No experiment has been run by this documentation update.


## Seven activities, purposeful movement and situation-dependent companionship

The owner's further clarification removes independent local exploration and the Survival child. Gathering contains mining and chopping; Combat contains hunting and guarding; Nearby assistance contains lighting, collecting and keeping company. This supersedes the earlier nine-activity arrangement without removing the responsibilities it attempted to cover. No extra family is introduced.

Lighting investigates useful darkness when a real lighting method is available. Collecting can investigate a reachable pot for its uncertain contents as well as known drops; the later instruction permits such trips, superseding incidental-only pot policy. Pot contents must not be treated as known or counted twice before and after breaking. Keeping company includes relaxed, varied local walking and safe changes of elevation around the player. Resting is also legitimate. Neither novelty nor perpetual movement is an independent reward. Observation coverage remains useful to these activities without a standalone ExploreLocally executor.

The reason to retain independent exploration would be an explicit product purpose of discovering unknown opportunities beyond currently useful work. That purpose is not selected here. If later evidence shows that useful opportunities cannot be discovered during ordinary sensing and purposeful travel, first investigate observation coverage. Do not restore a novelty-seeking behaviour just to mask a blind sensor.

Companionship estimates the consequence of being elsewhere. Inputs include likely need for intervention near the player, time to help, current effective harm, confidence in observed safety, player travel, terrain, available abilities, useful work near both actors and accumulated separation. Calm known surroundings permit wider worthwhile excursions. Credible danger can tighten the useful working area before a combat action becomes worthwhile. Darkness affects visibility and lighting value; it is not proof that enemies exist. Nearby loot matters through the value of collecting it, not an additional penalty merely for being distant from loot.

Avoid multiplying a danger penalty at the activity, family, separation and safety layers for the same predicted consequence. Record direct risk to the companion separately from the cost of being unavailable to help the player, then compare both once using common evidence. A fresh world/threat/capability change invalidates affected estimates. Ordinary small changes should not provoke constant reversals, but emergency changes must not wait behind smoothing.

Safety owns temporary execution, not another list of competing family activities. Its response record includes the interrupted activity, control owner, response goal, observed hazard, expected consequence, actual progress, exit condition and unresolved alternatives. With no ordinary offer it can still request escape. On completion it releases control and re-evaluates current offers rather than blindly resuming old work. If no safe method is known, report that limitation and choose the least harmful supported response; do not invent a proof or allow forbidden teleportation. Repeated interruption is evidence to reconsider the worksite, not a reason to alternate mining and dodging forever.

The top HUD keeps family icon / health / behaviour icon. A temporarily suspended behaviour remains identifiable, with its existing icon visually subdued while shared safety owns execution; this adds no weapon or detail field. The inspector states the actual safety response and suspended activity separately. If there is no ordinary activity, show a neutral activity icon rather than falsely labelling escape as Mining. The visual treatment remains subject to native HUD acceptance.

## Interaction cases test shared mechanisms rather than separate patches

These are prospective design tests, not observed defects or promises about unknown enemy scripts. They extend the original failure matrix with J01–J16. Each case needs paired circumstances and the common offer, control and outcome evidence.

| Case | Situation and desired distinction | Shared mechanism and diagnostic evidence | Proposal 1 packages |
|---|---|---|---|
| J01 | The same ore lies away from the player in a calm known area versus near credible approaching threats. Wider work can win in the first scene; faster availability to help matters in the second without requiring combat in either. | Compare excursion benefit, intervention delay, predicted harm and confidence on the same geometry; enemy count alone must not explain the change. | P05/P08/P12 |
| J02 | Slimes are trapped in a pit; a bat can cross it. The first may contribute little current danger, the second may matter. Opening the pit changes the first judgement. | Threat reach/timing and terrain revision, with unresolved reach kept distinct from proven containment. | P05/P07/P08 |
| J03 | A relevant enemy makes mining unsafe; an unrelated enemy or a terrain lip does not explain the same failure. Combat can enable work only in the first case. | Name the work-blocking condition; estimate achievable threat removal versus direct work or an alternate pose. Record whether that condition actually disappears. | P04/P08/P09 |
| J04 | An enemy cannot be reached, killed or removed within a useful time. The companion must not enter endless combat merely because gathering is unsafe. | Compare escape, another worksite and keeping company; bounded enabling estimates cannot certify an impossible intervention. Escalate to Path 3 only if a valid multi-step consequence model is demonstrably needed. | P03/P08/P14 |
| J05 | The last torch is gone and the area is already lit. No phantom lighting or exploration trip should remain; nearby company can still move naturally. | Native supply and light-source availability, useful coverage and method eligibility. Held light is separately valued only when it actually helps. | P10/P12 |
| J06 | Everything useful is done. It may stroll or rest nearby, but should not jump repeatedly into danger to appear busy. | KeepCompany chooses safe local poses with continuity and bounded movement cost; locomotion and new observations are not productive-work rewards. | P05/P07/P10 |
| J07 | A lit ledge has a pot; its drops might fall into a pit. A collection excursion depends on access, uncertain reward and practical pickup, not the pot's presence alone. | Container generation, expected versus realised contents, attack access, drop region uncertainty, bag capacity and return evidence. Re-evaluate after breaking. | P09/P10/P12 |
| J08 | A lighting trip passes a pot and loose loot. Count each benefit once and allow cheap compatible actions without three movement owners. | Opportunity identities, marginal detour cost, hand/feet grants, realised transfer and lighting effect. | P02/P06/P10 |
| J09 | The companion is far away when the player accelerates or a new threat appears. A formerly sensible excursion may stop being sensible. | Fresh player/threat context, remaining work, time to intervene/reunite and reason for interruption; repeated small trips retain cumulative separation. | P05/P06 |
| J10 | The companion is drowning with no enemies and no useful family offer. It must still find and execute an escape. | Shared safety generates a goal independently, retains progress until viable safety, records no-method/unknown results and respects capability limits. | P06/P07/P08 |
| J11 | A projectile arrives during mining. One dodge clears it but lands in water, or an alternate step preserves tool reach. | Compare proposed control sequences and aftermath; record the chosen response, actual grant, tool interruption and achieved terminal state. | P04/P06/P08 |
| J12 | Hunting and guarding each offer a safe manoeuvre, while a reflex asks for a different jump. The body must receive one coherent response. | Shared safety evaluates the proposed activity motion before replacing it; the single motor receives one grant. No duplicated survival controllers. | P06/P08 |
| J13 | Tiny effective damage is possible during valuable work. Shared safety must not suppress all work forever, but the same attack at low health can require escape. | Compare effective harm, remaining health/resources, work benefit and supported alternatives; safety is not zero-risk filtering. | P05/P08 |
| J14 | Safety repeatedly interrupts the same ore attempt. No damage occurs but no ore is mined. Neither system may claim the whole episode succeeded. | Attribute successful avoidance separately from suspended/failed work; reconsider the worksite or activity with interruption frequency and native effects visible. | P02/P04/P06/P12 |
| J15 | Both actors are threatened from different directions. Running straight towards the player is harmful; staying far away indefinitely also fails to help. | Compare combined consequences of achievable interventions and escape, retain uncertainty, and record direct self-risk separately from missed-help cost. | P05/P08 |
| J16 | A safety response ends while the ore is gone, the player has moved, or a new ability changes the return route. Resume must not revive stale work. | Release control, invalidate changed dependencies and re-evaluate offers; inspector/HUD distinguish suspended purpose from actual execution throughout. | P02/P06/P11/P12/P13 |

Record the basis of an excursion decision, not just its final score: expected additional work, time away, reunion and intervention estimates, relevant threat identities, containment confidence, direct self-risk, missed-help cost, observed coverage, and uncertainties. Record safety request/grant/release events with response identity and termination reason. God's Eye should join these to actual motion, damage, tool effects and pickups. A contradiction can be detected before execution; a wrong threat forecast requires later outcome evidence and is not automatically a bad choice.

Use matched scenes before tuning: hold geometry and work constant while varying containment, player travel, health, supplies or capability one at a time. If scores change with identical evidence, investigate evaluation; if evidence misses the relevant hazard, investigate observation; if a sensible response receives no controls, investigate ownership; if granted controls fail physically, investigate movement; if the effect occurs but the report disagrees, investigate observation of outcomes. These branches are the general diagnosis procedure for all sixteen cases.
