This is the separate review of Proposal 05 and its subsequent recheck. Original line references name the earlier draft `RecommendedBrainArchitecture.md`; the findings are retained as history and the appended recheck points to the durable proposal. The [current proposal](<../../proposal/05 Retain a Course and Repair Its Future.md>) incorporates the corrections. A pass here means discussion-ready architecture, not measured gameplay.

# Adversarial review — combined retained-course recommendation

## Unasked question

**After a combat option becomes the course's current step, who is allowed to change what the companion will actually fire?**

The report correctly assigns the retained executable continuation to the course owner (`RecommendedBrainArchitecture.md:101-107`) and makes the local combat planner an option producer. It then says the local planner continues to refine stand/weapon/target/aim and that, while travelling toward a stronger position, combat chooses useful current shots (`:106`, `:144-146`). Those statements leave a second authority alive inside an accepted course step.

That ambiguity is observable, not terminological. Suppose the course accepts a segment whose successor predicts a zombie alive until a due bow use, then schedules a pickup after the fight. While travelling, the local planner's useful from-here shuriken kills the zombie and consumes one finite throw. The selected segment's remaining-life, consumable and drop predictions are now wrong. The native receipt can repair the next suffix, but the actual shot still needs an owner in the current decision: either it was a conditional use inside the accepted segment, or the local planner changed the course without the course owner accepting a replacement.

Current source makes the collision concrete. `CoordinateBrainTick.cs:241-271` allows the hands to fire only while `FightEnemies` is current, and `FireDueUse.cs:42-82` may fire the plan's due use or a best-from-here use while travelling. A new course layer can retain that responsiveness, but it cannot leave the committed combat segment and the opportunistic hand as independent decision owners.

This question lands. The recommendation has the right three levels—course, local option producer, current executor—but it needs one accepted-segment authority boundary. That is a conceptual condition, not an implementation detail.

## Requirement matrix

| Criterion | Origin | Expected proof | Evidence | Result | Caveat / smallest condition to close |
|---|---|---|---|---|---|
| One ownership/data-flow chain supports granular mixed work without a second brain | Handed rubric; derived from present ownership | One authority for course choice, one producer for local combat options, one executor/effect owner; accepted combat options cannot be mutated independently | The owner table is structurally sound (`RecommendedBrainArchitecture.md:101-109`) and the implementation sequence removes the discarded chooser rather than retaining two permanent paths (`:247`). But local combat both exports a chosen segment and continues choosing useful current shots (`:144-146`) | **Partial** | Every actual combat use must be part of the accepted segment's declared conditional behavior or a proposed replacement accepted by the course owner; local refinement cannot silently mutate course state |
| Combat is granular rather than an opaque fight-to-death reservation | Handed | Short segment options expose time window, duration, uses, resources, effects, interruption and dependencies | The report explicitly rejects whole-fight default units and prices full duration/risk for inseparable bundles (`:144-150`) | **Pass** | Current aggregate combat APIs do not yet provide the contract, which the report correctly treats as an implementation gate rather than existing behavior |
| Weapons remain quiet during mining and its approach | Handed/README | Explicit policy plus the same grant boundary at execution | The report states the rule verbatim (`:138`, `:209`); current source enforces combat-only firing at `CoordinateBrainTick.cs:241-271` | **Pass** | Incidental noncombat work during combat must also be suppressed by the boss/event constraint; this needs a boss gate below |
| Same accepted outputs are ordered without category weights | Handed | Remaining travel, completion time, danger, resources and return compare equal output sets | The report cleanly separates same-output ordering and dominance (`:64-67`) and rejects time-only comparison of unequal outcomes | **Pass** | “Accepted” must mean feasible/product-admissible, not a hidden value cutoff that removes ordinary items or the seventh site before comparison |
| Different output sets are compared without hiding counts, priorities or 15% hysteresis | Handed | Natural consequence vector, explicit product policy, no raw count, no unnamed incumbent percentage | The report admits there is no physical exchange rate, preserves a natural-unit vector, rejects raw effect counts and declares the utility policy unfinished (`:68-76`) | **Partial, but honest enough for architectural discussion** | The representation can be recommended conditionally; migration cannot be approved until contrasting README scenes define a testable policy and show that “accepted effects” is not a disguised prefilter. The report already names this gate |
| A genuinely better newcomer can replace the incumbent under bounded and unequal evidence | Handed | A comparison rule that recognizes proven lower-bound improvement without demanding an impossible complete future | A newcomer replaces only when its “complete evaluated course” is better; unresolved challengers cannot displace a proven action (`:117`). Uncertainty is retained and refined (`:126`) | **Partial** | “Fully/complete evaluated” needs a finite decision-sufficiency rule: a challenger whose conservative bound already beats the incumbent may replace it without waiting for every suffix uncertainty; otherwise bounded search can recreate incumbency through epistemic delay |
| Continuation survives tiny fluctuations, step-one disappearance and interruption without sunk cost | Handed | Semantic identity/validity, affected-suffix repair, remaining-cost comparison, explicit in-flight frontier | Purpose/binding separation (`:111-113`), repair versus improvement (`:119-126`), no past-cost credit (`:124`), and player-completes-step-one scene (`:179`) directly satisfy this | **Pass** | The still-needed uncertainty rule above is the only material gap; no arbitrary hold percentage is proposed |
| Planner-induced starvation is separated from correct semantic postponement | Handed | Stable finite work eventually executes after a finite arrival burst, without age forcing work during emergency | The report limits its guarantee correctly (`:128`), rotates incomplete discovery (`:134`), and names a finite-burst experiment (`:242`) | **Pass in concept** | It does not promise completion under an infinite genuine emergency, which is correct and should not be treated as a gap |
| Aggregate planning, cold start, first prefix and fallback are all bounded honestly | Handed | One account includes setup, generation, validation, repair, tactical work, cache and fallback; a usable action precedes refinement; cold and warm measured | Lines `168-174` explicitly cover all charges, remove live nested unbounded escape, protect execution work and name cold start. Lines `26-27` preserve measured current overruns; `:241` defines the experiment | **Pass in design; runtime Blocked** | No combined implementation or runtime measurement exists. The report does not invent one. A future pass requires a measured total envelope, not only a configured allowance |
| Predicted effects, in-flight actions, resources and live receipts cannot double-count or mutate reality | Handed | Immutable past/in-flight frontier, prefix-state marginal effects, cumulative resources, hypothetical isolation, native receipt replacement | Lines `99`, `111`, `136` and `138` establish all of these. The overlapping-light, capacity and launched-projectile experiments are named at `:236-240` | **Pass in architecture** | The combat from-here ownership ambiguity must be closed so every actual use belongs to the effect ledger it changes |
| Learned knockback can support planned enabling geometry without pretending current support | Handed | Learned push plus enemy motion/terrain uncertainty propagate to a later collision; no-shove control; conditional fallback until then | The report names the current omission (`:28`), requires hit timing, push, motion and terrain (`:148`), refuses the strong claim until established, and specifies a mutation-sensitive experiment (`:240`) | **Pass** | This is appropriately a future gate. It does not claim that source/capture evidence already demonstrates shove-then-pierce |
| Shared global facts reach all relevant consumers without a soft preference becoming a wall | Handed | One authority and per-consumer semantics; two-tile passages remain legal; close work remains possible | The consumer audit at `:152-166` is strong and explicitly distinguishes preference from obstacle | **Partial** | Body clearance passes. Player-interference semantics do not: the disposition at `:192` collapses an immediate obstruction rule into one soft preference. That separate fact needs the two-level contract described below |
| All 32 README responsibility rows have an explicit disposition | Handed | One-to-one inventory, with core requirements preserved rather than weakened | A mechanical comparison confirms 32 README rows (`README.md:673-704`) and 32 recommendation rows (`RecommendedBrainArchitecture.md:192-223`) in the same order | **Partial** | The inventory is complete, but boss/event and getting-out-of-the-way are mistranslated, and torch-supply semantics remain an explicitly unresolved product contract |
| Boss/event policy is strict, includes continuation after player death, and cannot be compensated by ordinary utility | Handed/README | Encounter makes optional work unavailable; own survival precedes damage; player death does not end the fight; an acceptance gate covers all three | The disposition says “survival-first” and continuation after death (`:193`), but the objective says only “survival wins damage ties” (`:68`) and self-preservation calls it a “tie policy” (`:202`). The production experiments (`:233-245`) contain no boss/event/player-death row. Current combat still refuses a dead player (`FightEnemies.cs:136-140`) | **Fail** | Boss/event state must impose the README's lexicographic survival-before-damage and optional-work exclusion, preserve combat after player death, and have one explicit acceptance experiment exercising those together |
| Immediate player obstruction is prevented without turning general interference into a prohibition | Handed/README; derived world-fit | Hard response for the tile about to be placed/passage currently entered; soft cost for possible/general interference and firing positions | Recommendation line `192` calls the Expected row inconsistent and resolves all courtesy as soft. README `:673` actually states two scopes: “never” for an imminent block/passage and “small rather than absolute” for general interference | **Fail** | Preserve both scopes: imminent, evidenced obstruction is a non-compensatory execution/destination condition; possible or diffuse interference remains a soft cost and cannot close legal passages or discard useful combat positions |
| Reopening criteria compare the retained course fairly with cheaper A | Handed | Same candidate identities, observations, effect models, local combat successor model, policy, executor, diagnostics and total budget; only retained course/repair differs | The report names a budget-matched A experiment (`:245`) and says A wins if it matches complex scenes at lower cost (`:259`). It does not explicitly hold the local C combat model and multi-candidate producers equal; requiring A to match “enabling-action” can therefore charge A for a capability assigned to C in the hybrid | **Partial** | Define the cheaper control by removing only retained multi-step course ownership/repair while holding every shared producer and local tactical capability constant |
| Empirical claims remain within source, captures and toy probes | Handed | Explicit limits; no future behavior or runtime presented as verified | Lines `5`, `11-15`, `24`, `27-32`, `186`, `229-231`, `249-255` and `261` repeatedly separate source, observational captures, toy arithmetic and unrun acceptance | **Pass** | “Most direct route” is an engineering judgement, not a measurement; the report labels its conditions and reopening criteria, so this does not become a false verification claim |

## Landed attacks

### 1. Accepted combat segment versus live local firing

**Report locations:** owner table `101-109`; combat contract `142-150`; shared effects `99`, `136`; migration `247`.

The report avoids both obvious failures—one global combat simulator and one opaque fight job—but leaves the replacement boundary underspecified. The course owns the current executable continuation; the local combat planner exports segments; the executor owns grants and receipts. Yet the local planner is also invited to choose current shots while moving. If that choice is not already a conditional branch of the accepted segment, it can change target life, knockback geometry, in-flight damage, mana/consumables and later drops outside the course's predicted transition.

This is the exact trigger that would create a duplicate brain: the course chose the future, while the local hand chose a different state transition. A next-tick repair makes the model responsive but does not establish single ownership.

**Smallest closing condition:** after acceptance, a combat segment is the sole authority for combat uses until its interrupt boundary. Local search may propose a replacement or resolve a declared conditional use, but cannot silently alter the selected segment's effects.

### 2. Boss strict order is weakened to a tie rule

**Report locations:** product preferences `68`; all-rows table `193`, `202`; production experiments `233-245`.

README's boss/event contract is strict: avoid being hit first, then deal damage; optional ordinary work stops; the fight continues after the player dies (`README.md:674`, with the narrated behavior at `:223-237`). “Survival wins damage ties” is weaker. It permits a higher-damage course with higher expected harm whenever damage is not tied. Calling the table entry “survival-first” does not repair the evaluator if the shared policy still treats survival as a tie-break or compensable utility dimension.

The omission matters because the report otherwise insists that product constraints sit outside ordinary trade-offs. Boss conduct is the clearest such constraint. It also has no named production experiment despite the current `FightEnemies` refusal on player death.

**Smallest closing condition:** the architecture statement must place boss/event survival and optional-work exclusion in the constraint/admission layer, state the survival-before-damage ordering, and add one experiment that includes player death during the encounter.

### 3. Immediate obstruction and general courtesy are different facts

**Report locations:** all-rows table `192`; global fact contract `152-166`; proposed narrow-cave scene `184`.

The report says the row's “never” and “small cost” conflict and chooses the soft reading. The README scopes them differently. An evidenced, imminent obstruction—the companion occupying the block placement tile or the passage the player is currently entering—is forbidden behavior. The general possibility of being in the way is a small cost so the companion does not skitter or abandon a good firing stand.

Collapsing both into a soft scalar can let enough combat value compensate for blocking the click. Collapsing both into a hard obstacle would produce the opposite failure the report already warns about: valid two-tile travel closes or the companion flees whenever the cursor glances past it.

**Smallest closing condition:** name imminent interference as a specific execution/destination validity fact and general interference as a soft preference, then require both sides in one paired acceptance case.

### 4. “Fully evaluated newcomer” can become epistemic incumbency

**Report locations:** retention rule `115-128`; bounded discovery `132-140`; budget `168-174`.

The report correctly refuses to let an optimistic first step displace a proven action. The phrase “complete evaluated course,” however, has no finite meaning in a changing world with bounded discovery, contingent combat outcomes and unresolved suffix opportunities. If a challenger has a conservative lower bound already better than the incumbent but one irrelevant tail estimate remains open, requiring full evaluation preserves the incumbent for the same practical reason as a percentage bonus: the rival is never allowed to become comparable in time.

This is not a request to act on unknown reach. A candidate can have a proven executable prefix and a bounded guaranteed consequence while optional suffix refinement remains incomplete. The proposal's uncertainty language is compatible with this distinction, but it does not state it.

**Smallest closing condition:** define sufficient evaluation in terms of comparison-changing bounds or constraints, rather than completion of every future estimate. Unknown feasibility stays non-executable; irrelevant residual uncertainty cannot protect the incumbent.

### 5. The cheaper-A control is not yet capability-matched

**Report locations:** six-approach roles `49-58`; production comparison `245`; reopening `257-261`.

The proposed empirical comparison is budget-matched, which is necessary. It is not yet explicitly capability-matched. The hybrid receives concrete multi-candidate producers, marginal effect state and a C-derived local combat successor model. Line `259` asks cheaper A to match the enabling-action case, even though the earlier architecture allocation assigns that capability to C, not to the retained course layer.

If A loses because it receives current aggregate combat while the hybrid receives shove-aware local combat, the experiment has not measured the course. It has measured two changes at once. The same applies if A sees one torch/drop per family while the hybrid sees all concrete sites.

**Smallest closing condition:** the A control shares the exact candidate set, consequence policy, local combat options, global facts, executor, instrumentation and total compute allowance. It differs only by selecting/reacting without a retained repairable suffix.

### 6. The unequal-output policy is an honest gate, not yet an architectural win

**Report locations:** objective `60-76`; acceptance scenes `176-186`; evidence boundary `249-261`.

This attack does **not** land as a disqualifier for discussion. The report says directly that physics cannot derive the exchange rate among a rare item, darkness, harm and ore; preserves natural consequence dimensions; removes dominated courses; and assigns the remaining policy to Selection. That is substantially more honest than renaming counts, time or priorities as an objective.

It does limit the recommendation's confidence. Most visible choices among different output sets remain unknown until the policy tests exist. Therefore the document supports choosing the **representation and ownership direction**, not claiming that the hybrid already has the behavioral objective that makes it outperform A.

**Smallest closing condition:** the existing stated gate is adequate—before migration controls the brain, contrasting README scenes must pin the product policy and expose every non-physical exchange term by name.

## Concrete counterexamples

| State | Proposal as currently written can do | Failure | Minimum condition |
|---|---|---|---|
| Accepted combat segment predicts bow after arrival; a shuriken from here can kill the target during travel | Local combat takes the useful current shot while the course retains the original segment | Two authorities change life, throw count, future drop and due use | Current shot is an accepted segment conditional or a course-approved replacement |
| Boss line A has lower harm and lower damage; line B has higher harm and much higher damage | General policy can choose B because survival only wins a tie | Violates strict survival-before-damage | Encounter constraint orders feasible survival first, damage second |
| Player dies while boss remains; a drop and torch are nearby | Current `FightEnemies` refuses, generic fallback can schedule ordinary work | Stops the one required activity and performs forbidden optional work | Encounter remains valid after player death and keeps optional work inadmissible |
| Companion occupies the exact tile under the player's active block-placement cursor while holding a strong firing stand | Soft interference loses to combat value | Blocks the player's click despite Expected “never” | Imminent obstruction is a hard local validity condition; alternate firing stand can still compete |
| Player merely looks through the companion with a placeable block while walking a valid two-tile passage | If courtesy were made globally hard to repair the prior case, destination/path may be rejected | Skittering or forbidden passage | Non-imminent interference remains a soft cost and never changes body fit |
| Challenger has proven reachable pickup before expiry and a conservative benefit above the incumbent; its unrelated later suffix is still being refined | “Complete evaluated course” rule holds incumbent | Hidden incumbent privilege through incomplete tail | Bound-based decision sufficiency; only decision-relevant uncertainty blocks switching |
| Budget-matched A gets concrete jobs but current knockback-blind combat; hybrid gets shove-aware local combat | Hybrid wins enabling-action test | Attributes local-model improvement to retained scheduling | Capability-matched control differing only in course retention/repair |
| Two feasible outputs differ: ordinary drop now versus unique light before player enters the area | Course vector leaves them incomparable; “accepted effects” is used as a prefilter | A hidden admission threshold becomes category priority | All feasible effects enter the natural vector; only explicit product constraints may exclude one before trade-off policy |

## Attack report

| Angle | What I tried | Outcome | Evidence |
|---|---|---|---|
| Alternative | Removed only the retained suffix and asked whether capability-matched A could reach the same scenes | **Landed** | The named A experiment is budget-matched but does not explicitly share local C combat and producer expansion (`RecommendedBrainArchitecture.md:245,259`) |
| Coverage gap | Traced all 32 disposition rows and compared strict wording for boss and player interference | **Landed** | Exact row count is 32/32, but `:192` weakens imminent obstruction and `:68,193,202` disagree on boss survival semantics |
| Real input | Exercised a best-from-here kill during a retained combat segment, player death during boss, conservative-dominant newcomer with incomplete tail, and an active placement cursor | **Landed** | These reach ownership, strict constraints and bounded-comparison gaps; current firing and dead-player source paths are `CoordinateBrainTick.cs:241-271` and `FightEnemies.cs:136-140` |
| Overhead | Looked for excluded setup/fallback work, warm-only assumptions and invented timing | **Survived** | `:168-174` charges the full discretionary path and calls out cold start; `:15,27,229-231` keep measurements scoped. Performance remains unverified rather than falsely passed |
| Residue | Asked which old chooser/commitment paths could remain authoritative after migration | **Landed conditionally** | The migration removes the discarded chooser path (`:247`), but accepted course state and live local combat refinement need a single post-selection authority |
| Smell with a future | Projected “fully evaluated,” “accepted effects,” and “survival tie” into cases where incomplete evidence or a larger reward persists | **Landed** | These phrases can respectively become hidden incumbency, hidden prefiltering and compensable boss risk unless their declared gates are sharpened |

## Overall classification

**Partial — discussion-ready as a provisional architectural direction, not acceptable unchanged as the recommended behavior contract.**

The central course shape survives: one retained mixed-work owner above the existing single-step executor, bounded repair, natural consequence vectors, shared facts, phase resources, immutable in-flight effects, local tactical options and actual receipts. The proposal is unusually disciplined about what is not measured. It also gives the cheaper alternative a real reopening path and makes runtime/model fidelity gates explicit.

Four conditions must become true before the recommendation itself can be accepted rather than merely discussed:

1. An accepted combat segment has one authority over every shot that changes its predicted successor.
2. Boss/event survival is lexicographically prior to damage, optional work is excluded, and combat remains valid after player death.
3. Immediate evidenced player obstruction is separated from general soft interference.
4. The A comparison holds every non-course capability constant.

The newcomer-evidence rule also needs a finite bound-based meaning before implementation, but it does not overturn the retained-course direction.

## History and what it implies

- `9b402cb` showed that a body-stall signal attached to activity progress worsened churn. The combined proposal preserves task-owned native progress and does not revive motion as benefit (`RecommendedBrainArchitecture.md:38`).
- `573d9d4` established the useful ordering arithmetic and the missing retained continuation. The proposal keeps the insight and replaces factorial stateless ordering with bounded local repair (`:40`, `:132-140`).
- `b89abee` and `a0be32f` established one-purpose execution, suspension and causal receipts. The proposal correctly layers the course above that owner instead of replacing it (`:41`, `:105-107`).
- `457b168`, `0a2a98e` and `145be5a` established clock-unit failure, urgency churn, partial-results behavior and the need for a first answer. The proposal incorporates aggregate accounting and cold-start testing rather than claiming the current 4 ms combat setting is an end-to-end bound (`:42`, `:168-174`).
- `03986f0` and `754e5e8` established that combat search is materially expensive and that reserved duration must be priced. The short-segment boundary responds directly (`:43`, `:144-150`).
- Current source still refuses combat when the player is dead (`FightEnemies.cs:136-140`) and permits plan-or-best-from-here firing while combat owns the activity. Those are the two live boundaries the combined recommendation must change or constrain; naming boss continuation and shared effects does not by itself settle their authority.

No reviewed history demonstrates this combined architecture in play. The capture and toy evidence justify the problem and refute several shortcuts; they do not verify the proposed future behavior. The recommendation states that limit accurately.

---

## Recheck disposition — 19 September 2026

**Surface rechecked:** the five landed conditions above, against the durable proposal at `research/proposal/05 Retain a Course and Repair Its Future.md`. This was a narrow document recheck. No implementation, runtime, build, game or broader research evidence was added.

| Prior finding | Revised proposal evidence | Disposition | Remaining caveat |
|---|---|---|---|
| Accepted combat segment versus live local firing | The accepted combat step is now the sole authority for actual weapon uses. Conditional from-here execution resolves target, resources, effects and validity into that same step before the hand grant; any other shot requires course-owner replacement acceptance (`Proposal 05:148-154`) | **Closed for architectural discussion** | The segment adapter and effect registration still require implementation and tests. No current runtime behavior is inferred |
| Boss strict order weakened to a tie rule | Ordinary safety ties are separated from the boss/event contract. Optional work is inadmissible, survival is compared before damage, combat remains valid after player death, and a paired boss/event experiment names all three (`:70`, `:76`, `:200`, `:209`, `:263`) | **Closed** | “Equally survivable” still needs the declared numerical policy calibration, which is an acknowledged pre-migration gate rather than a missing architectural rule |
| Immediate obstruction collapsed into general courtesy | Player interference is now a shared fact with two scopes: evidenced imminent placement/passage obstruction excludes a destination/execution; diffuse interference remains a soft cost. The disposition rejects telepathy and a cursor glance becoming terrain, and a paired test covers both (`:164`, `:199`, `:264`) | **Closed** | Observation uncertainty must remain visible in implementation; the proposal expressly says so |
| Fully evaluated newcomer recreates epistemic incumbency | Sufficient evaluation now has a finite definition: proven executable prefix plus conservative comparison that resolves the choice despite tail uncertainty. A calibrated lower bound may beat an incumbent upper bound; unfinished feasibility still cannot authorise optional execution. A dedicated case covers the distinction (`:121`, `:259`) | **Closed in substance** | Lines `126` and `130` retain the phrase “fully evaluated.” In context `:121` controls and removes the old meaning, but “comparison-sufficiently evaluated” would be less ambiguous. This is terminology residue, not a discussion blocker |
| Cheaper-A experiment not capability-matched | The production experiment now shares concrete candidates, marginal effects, local successor-aware combat, objective policy, facts, executor, diagnostics and total allowance; only retained mixed-work course ownership/repair differs. The reopening section also forbids crediting a better knockback model to course retention (`:265`, `:279`) | **Closed** | Runtime results remain intentionally absent |

### Recheck classification

**Pass for the requested threshold: the recommendation is ready for discussion as a proposed architectural direction.**

No conceptual blocker from the five prior findings remains. The durable proposal now has a coherent single-owner combat path, an explicit non-compensatory encounter policy, two-level player-interference semantics, a finite conservative-bound newcomer rule, and a fair cheaper-A control.

The original review's broader limits still stand and are correctly preserved by the proposal:

- the cross-output numerical policy is not yet calibrated or complete;
- the combat successor model, course layer, producer expansion and consumer audit are not implemented;
- aggregate cold/warm runtime and first-action quality are not measured;
- headless acceptance and a later fresh game judgement remain ahead;
- none of the proposed future play has become verified behavior merely because the document is now coherent.

Those are named design, implementation and evidence gates. They do not prevent discussing or selecting this direction; they prevent treating it as a completed coding specification or demonstrated architectural win.

