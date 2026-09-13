# Decisions, activities and shared controls

The baseline has a useful separation between observation, preference, positioning, movement and weapons, but several boundaries carry weaker facts than their names suggest. The strongest evidence for change concerns those facts and their ownership. It does not establish that utility selection is intrinsically incapable of the expected behaviour.

This is a source inspection at `d60b92b`, whose gameplay files match `4296f85`. Citations identify the file and relevant symbol; `git show d60b92b:<path>` retrieves the exact baseline. Recorded outcomes and their limitations are in [the telemetry study](<../Evaluation and Observability/Recorded Episodes and Measurement Limits.md>). Historical causes and rejected attempts are in [Historical Evidence](<../Historical Evidence/CLAUDE.md>).

## The coordinator decides which decisions are allowed to run

The actual outer control policy is `Brain.TickPhases`. It updates observation, derives an intervention estimate, continues an active distant-follow recovery, assesses immediate collision threats, and only then calls the behaviour chooser. A reflex can apply avoidance controls, run independent weapon engagement and return before selection. On an ordinary tick, selection executes the chosen activity, survival may claim its escape controller, positioning resolves the request, navigation supplies controls, and engagement runs afterwards. Recovery is another early branch. Downing is handled by the NPC lifecycle outside this ordinary decision flow.[^1]

```text
NPC lifecycle and world update
└─ Brain coordinator
   ├─ observation and intervention estimate
   ├─ active distant-follow recovery → motor and weapons
   ├─ imminent-collision reflex → avoidance and weapons
   └─ ordinary selection
      ├─ activity → position request
      ├─ survival escape, when applicable → motor
      └─ position → route/execution → motor
         └─ independent engagement, unless the active work owns the hand
```

This structure answers the question about three parallel systems more precisely than counting lanes. Weapon evaluation is a separate responsibility, while recovery and reflexes currently determine whether other responsibilities run at all. A replacement utility formula cannot repair a decision that the coordinator never calls. Conversely, invoking all decisions every tick would not resolve contradictory control commands unless somebody owns the final output.

The appropriate comparison is between control contracts: who proposes a destination; who claims the body or hand; what may interrupt an activity; what remains valid afterwards; and which outcome closes an attempt. Three boxes with arrows can still share hidden mutable state. A small coordinator with explicit grants can be coherent without making every choice pass through one enormous score.

## The chooser compares eleven behaviour objects, each of which may already have selected a target

`Chooser.Actions` contains survival, guard, kite, hunt, loot, chop, mine, pot breaking, torch placement, following and wandering. `Choose` calls every object's `Score`, then composes raw utility with protection, incumbent commitment and horizon factors. A later pass discounts following when a sufficiently valued excursion exists within the activity envelope. The highest positive final score wins; exact ties follow list order, and an all-zero board falls back to wandering.[^2]

The raw scores are not probabilities. Mining and chopping start from a fixed work value multiplied by a player-danger term; hunting multiplies safety, distance relevance, target worth, activity admission, personal danger and firing opportunity. Guard and survival can exceed the ordinary normalised band through urgency multipliers. Consequently, identical numeric changes in two factors need not have comparable behavioural meaning. A product containing an extra uncertain factor also has an extra opportunity to collapse to zero or become small.

There is no theorem saying this composition is wrong. There is equally no evidence that its factors form one calibrated objective. It should be evaluated against paired situations: the same ore with a faster pick; the same bat against more effective defence; the same target with a proven shot rather than an unknown approach. A useful scorer should change in the intended direction for a stated reason. Multiplying more terms into an expression is not sufficient evidence of that property.

`Score` is not a read-only function over an immutable observation. Mining searches and prunes its vein, refreshes approach state and increments retry counters inside scoring. Chopping discovers or defers trees there. Hunting replaces its target and reads or refreshes firing-opportunity caches. Loot selects one item before its utility competes. This matters for any proposed hierarchy: moving one behaviour behind a parent can change how often its discovery and maintenance execute, even if the mathematical score stays identical. A hierarchy experiment must separate candidate maintenance from evaluation before interpreting an outcome as an effect of grouping.[^2][^3]

The current incumbent bonus belongs to the behaviour object. Target-specific `ActivityIdentity` exists for some admission and retention decisions, but that does not make every part of utility or progress target-specific. A pot job and a mining job can have different internal histories while sharing one body-wide `MovementStalled` signal. The historical reversal of a stall-conditioned bonus is evidence against borrowing that signal as task failure; it is not evidence against measuring progress at the task that owns it.

## Mining contains several different feasibility questions

Mining first needs an eligible ore tile, a tool that can damage it, an allowed work location and a usable place from which to swing. `OreFinder.IsOre` uses `TileID.Sets.Ore`. `TileMiner.CanMine` calls the game's private pickaxe-damage method through a bound delegate and checks tile destruction eligibility. `OreFinder.InReach` checks the player's reach dimensions from an eye point, then a line to an exposed adjacent face. The exposed-face test is a companion rule on top of the native pick path.[^3][^4]

An approach search enumerates standing positions around the ore, checks tool reach there and at two horizontally displaced positions, then calls bounded walker reachability. It returns a target with a standing position only for `Yes`. An unsuccessful bounded search can return `Unknown`, which is retained separately from finding no suitable target. Discovery runs near the player and the companion in automatic mode, allowing shared ore work.

The work action retains a bounded vein and a job identity. It prunes tiles that have disappeared, cannot be mined or are no longer admitted. If the feet, terrain revision or pick power changes, it refreshes approach evidence. A previously held ore remains usable immediately when the current feet can swing at it. Otherwise the action tries another standing position, or enters its unknown-approach path. This history explains why merely standing near a vein cannot establish that mining should currently execute.

The unknown-approach path selects an eligible ore without a reach proof and asks for an exact position near that ore. Position resolution first prefers a known reachable stand, then falls back to a geometrically standable one. That is an exploratory admission policy, not a certificate of reaching or returning from the ore. It can be sensible only if the executed prefix has its own safety conditions and the attempt has a meaningful stopping rule. The current code contains movement-based continuation and retry logic, but this is not the comprehensive safe-checkpoint contract described in the desired behaviour.

Once the current body is in tool reach, `MineAction.Execute` holds the pickaxe and sets `HandsBusy = true` even when the miner's swing cooldown has not expired. `ChopAction` does the analogous thing near its tree stand. During the approach the hand is free. Thus the ordinary work path already expresses an important part of the requested action-level exclusivity. Replacing it with a broad rule that mining behaviour can never shoot would remove useful travel-time firing.[^3][^5]

Three limitations deserve separate experiments:

| Boundary | Source finding | What remains unproven | Separating observation |
|---|---|---|---|
| Discovery → opportunity | A nearest geometric candidate can consume expensive approach queries before a farther, better opportunity is compared. | How often candidate order suppresses useful work in actual play. | Record all admitted, rejected and deferred candidates with query cost and reason. |
| Approach → arrival | The approach reserves an eight-pixel horizontal margin, while navigator arrival can accept a grounded body within twelve pixels of its goal. Tool reach is a different predicate. | A particular recorded idle episode caused by the residual tolerance gap. | Capture live feet, selected stand and `InReach` together; test offsets around a marginal ore face. |
| Tool request → productive work | `TileMiner.Swing` returns success after calling `PickTile`; this proves a swing was issued, not that ore damage or yield increased. | Whether actual native/modded target refusal is being counted as progress. | Record tile damage or tile-change outcome separately from tool animation and requested hit. |

The tolerance difference is a source-demonstrated incompatibility in possible acceptance sets, not a claimed reproduction. The corrective contract is that navigation arrival for a work request must imply the work's terminal predicate, or hand control to a local adjustment that can satisfy it. A global reduction of every arrival tolerance would be a much broader intervention and could damage following or route completion.

## Chopping, collecting and incidental work do not yet share one lifecycle

Chopping retains a tree across interruption and has its own deferred-target dictionary. Mimic mode excludes the tree the player is hitting. Automatic mode currently calls the finder without that exclusion, so the clarified preference for a separate tree is not uniformly represented. This is a bounded behavioural discrepancy, not a reason to redesign all harvesting.[^5]

Looting scans pickups in their existing order, asks whether the bag can accept them and whether a standing tile exists nearby, then compares the chosen pickup using distance, value and player safety. A nearby standing tile does not establish a route to it or a return. Actual collection is contact-driven outside the loot behaviour, so travelling, guarding or hunting may collect a drop without selecting a loot task. That is useful coexistence to preserve.[^6]

The chooser remembers a recent worksite to widen nearby collection retention. This is a form of local continuity; it does not force every mined item into a mine-then-collect sequence. It also does not associate a particular spawned item with a particular broken tile. A proposal about evaluating ore drops in a pit needs either an explicit association or a clearly stated approximation. Counting every nearby item as the result of a job would misattribute unrelated loot.

Mining, chopping and pot breaking therefore share discovery, approach, tool/action occupancy and environmental change, but differ in target identity, destruction mechanics, player cooperation, tool use and result structure. A useful harvesting abstraction would unify those contracts while keeping native predicates and target-specific work explicit. Renaming three classes under a parent would change little; forcing all three into an identical scripted sequence would change too much.

## Pursuit and firing are distinct, but their evidence must agree on what each promises

Independent aiming and weapon selection are appropriate for travel-time firing. `HuntAction`, however, chooses a movement purpose. It should admit an enemy because getting a useful attack opportunity is worthwhile, while the arsenal should choose a legal shot available from the current body. A hunt can reasonably begin without a current shot. It should not repeatedly finish its movement request at a position from which no useful shot exists and report that as completed hunting.[^7]

The current hunt checks a current attack first, then samples standable tiles near the enemy at a stride of two tiles. It filters by weapon reach and a straight sight ray, then asks whether the positioner's retained reachable region contains a sampled tile. It distinguishes a current shot, an opportunity after movement, unknown and none. Unknown is discounted but admitted; none vetoes the candidate.

The code's absence claim is stronger than the sampling procedure. `Resolve` returns `None` if no sampled position passes, including when a narrow stand falls between sample rows or columns. The comment saying a missed ledge only makes the result more cautious is not sufficient: the actual result may be a refusal. Similarly, a clear straight ray is neither a complete enumeration of arcing shots nor a guarantee that the selected weapon's real trajectory succeeds. These are source-level reasons to label the result as search coverage and evidence rather than physical absence.


Position selection subsequently ranks cheap candidates and runs the full trajectory solver on only a bounded shortlist. An unsolved candidate is not evaluated further, while a solved failure retains a small fallback value. This protects frame time but means the maximum among evaluated candidates is not necessarily the maximum over all usable positions. The distinction should be visible in the inspector: number discovered, number eligible, number shortlisted, number solved, and why search stopped.[^8]

Hunting's progress monitor counts displacement, damage to the same enemy and a fired/cooldown weapon outcome. The last condition deserves a target-association check: an independent weapon can be cooling down from shooting a different threat. That may be useful combat, but it does not necessarily establish progress on the movement purpose being retained. Deferring all target generations involved in a stalled stretch addresses identity thrashing; target-associated progress is the remaining conceptual question.

## Danger is an observation-derived preference, not an estimate of expected lost health

`ThreatSense` classifies movement using `noTileCollide` and `noGravity`, retains observed peak speed, and estimates arrival from distance divided by speed. Unknown reach remains potentially dangerous. A recent hostile projectile with an engine-provided NPC parent identifies shooting capability; missing source attribution remains missing. This is a generic design that can recognise unfamiliar threats without a list of enemy names.[^9]

Its limits are substantial. A hostile that has not yet emitted an attributable projectile may not be known as a shooter. Walking reach queries use companion-style transitions as an approximation for enemy movement. Arrival from current separation and speed does not establish the enemy's future route, target or script. The urgency calculation uses raw expected damage relative to maximum life with a nonzero floor, rather than consistently calculating effective post-defence damage, regeneration, present health exposure and interruption cost. A weak late-game bat can therefore remain more important than the desired one-damage scenario suggests.

Several threats combine through one minus the product of their complements. That shape makes a crowd contribute more than one threat while saturating, but the inputs are heuristic urgencies, not calibrated independent probabilities. Calling the result the probability that at least one attack lands would overstate its meaning. Correlated attacks, common obstacles and one intervention removing several threats all violate a naive independence interpretation.

The reflex is more absolute. It predicts the passive body under `Controls.None` and asks whether any hostile contact or hostile projectile overlaps it within a lookahead. Any qualifying overlap can take control before ordinary utility, without pricing effective damage or checking whether the activity's proposed trajectory was already safe. This does not prove the reflex caused every interruption; it identifies why ordinary preference tuning alone cannot fulfil the clarified willingness to accept trivial damage in some circumstances.[^10]

A revised system needs three distinct outputs: predicted harm and uncertainty; a request for an evasive or defensive response; and the final feasible controls after considering the intended movement. Treating all three as one boolean makes a distant weak projectile and a lethal immediate impact structurally similar at the point that owns the body.

## Capability scaling exists in pieces and must become a shared contract

The NPC already mirrors the player's maximum life and defence. Mining reads the held pickaxe's power and use time, with a copper fallback, and tool reach reads the game's player reach dimensions. Movement exposes capability and mobility records; the live companion currently supplies the basic kit. Ground and possible air-jump transitions have a shared application seam, but dash, swimming, flight and arbitrary airborne chains are not implemented as complete gameplay abilities.[^11]

This is not the all-system capability model the desired behaviour needs. Movement constants, graph envelopes, tool selection, defence, threat estimates and route archives have different owners. A future speed change must affect generated transitions, actual controls, travel-time forecasts, intervention timing and cache validity. A tool reach change must affect discovery and terminal reach. An extra jump must affect both the excursion and the resources remaining for return.

The proposal is not to equip arbitrary player items. It is to derive one versioned description of the companion's effective authored capabilities and pass it through the systems that predict and execute them. Native game predicates can remain authoritative where applicable. Compatibility still requires declared limits when a mod changes physics, visibility, damage or tile semantics outside the observation contract; no general scorer can infer an unobserved rule with certainty.

## What this evidence supports changing first

The baseline supports preserving several useful pieces: independent weapon engagement; hand occupancy during active ordinary tool work; explicit activity identity; typed route outcomes; retained searches; native collision prediction; and deliberate separation of harmfulness from attackability. It also supports investigating concrete mismatches: incomplete search described as absence, inconsistent arrival predicates, task progress inferred from body or unrelated weapon state, and bypassed decisions described as fresh choice.

The least speculative architectural step is to make those distinctions observable and consistent before comparing larger selectors. If the same eligible opportunities and executable actions produce poor choices under a stable control contract, that is evidence for changing utility composition or hierarchy. If useful multi-step opportunities remain invisible because their first step has little local value, that is evidence for planning over consequences. Neither outcome can be fairly inferred while a useful option was never offered or its selected controls never executed.

## Sources

[^1]: [CoordinateBrainTick.cs](../../Companion/Brain/CoordinateBrainTick.cs), `Tick`, `TickPhases`, `TryFollowRecovery`, `Engage`; [CompanionNPC.cs](../../Companion/CharacterBody/CompanionNPC.cs), lifecycle and downing.
[^2]: [ChooseBehaviour.cs](../../Companion/Brain/Infrastructure/Selection/ChooseBehaviour.cs), `Actions`, `Choose`, `RecordWork`; [CompanionAction.cs](../../Companion/Brain/Activities/CompanionAction.cs), activity identity and admission.
[^3]: [MineAction.cs](../../Companion/Brain/Activities/MineAction.cs), `Score`, `UnprovenApproach`, `Execute`, `NextInPatch`.
[^4]: [OreFinder.cs](../../Companion/Brain/Infrastructure/Interactions/Mining/OreFinder.cs), `Approach`, `InReach`, `HasLineToExposedFace`; [TileMiner.cs](../../Companion/Brain/Infrastructure/Interactions/Mining/TileMiner.cs), `CanMine`, `Swing`.
[^5]: [ChopAction.cs](../../Companion/Brain/Activities/ChopAction.cs), mode-specific discovery, `Score`, `Execute`.
[^6]: [LootAction.cs](../../Companion/Brain/Activities/Gathering/LootAction.cs); [CompanionNPC.cs](../../Companion/CharacterBody/CompanionNPC.cs), `CollectTouchedItems`.
[^7]: [HuntAction.cs](../../Companion/Brain/Activities/Combat/HuntAction.cs), `Resolve`, `PickTarget`, `ObserveOutcome`; [Arsenal.cs](../../Companion/Weapons/Arsenal.cs), attack evaluation and firing.
[^8]: [ChooseUsefulPosition.cs](../../Companion/Brain/Infrastructure/Position/ChooseUsefulPosition.cs), `Best`; [Navigator.cs](../../Companion/Brain/Infrastructure/Movement/MovementExecution/Navigator.cs), arrival acceptance.
[^9]: [ObserveThreats.cs](../../Companion/Brain/Infrastructure/Observation/ObserveThreats.cs); [ObserveHostileAttackSources.cs](../../Companion/Brain/Infrastructure/Observation/ObserveHostileAttackSources.cs); [ObserveCompanion.cs](../../Companion/Brain/Infrastructure/Observation/ObserveCompanion.cs).
[^10]: [AssessImmediateThreats.cs](../../Companion/Brain/SharedBehaviours/Safety/AssessImmediateThreats.cs), `TryAssess`.
[^11]: [CompanionNPC.cs](../../Companion/CharacterBody/CompanionNPC.cs), stat scaling; [DescribeMovementCapabilities.cs](../../Companion/Brain/Infrastructure/Movement/MovementAbilities/DescribeMovementCapabilities.cs); [ApplyMovementAbilities.cs](../../Companion/Brain/Infrastructure/Movement/MovementAbilities/ApplyMovementAbilities.cs).
