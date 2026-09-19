> Final bounded recheck: PASS as an implementation plan. The initial FAIL and every correction are retained below so their counterexamples are not lost. This was a reused Sol/high review context, not a new isolated context. The [active plan](<../../proposal/Implement the Retained Course Brain.md>) owns the current specification. This review proves no native implementation or live gameplay outcome.

# Adversarial review — Implement the retained course brain

**Reviewed artefact:** `research/proposal/Implement the Retained Course Brain.md` as present over source baseline `d315bf2` on 19 September 2026. The proposal is untracked in the reviewed worktree and is being migrated alongside other documentation. This is a reused review context, not a cold independent context. I made no repository, Slate, memory, source, fixture, or gameplay changes and ran no build, replay, game, or UI process.

## Unasked question

**What happens when the player really departs, but Expected Behaviour says the autonomous work should continue?** The plan and G07 ask whether departure can end an obsolete surface fight. They do not ask the inverse question even though README Expected answers it: at 6:00 the player walks past a cave and the companion keeps lighting one dark patch after another until the useful lighting is finished (`README.md:71–77`); more generally, separation alone does not cancel worthwhile work (`README.md:33–35`). The proposed rule instead stops initiating optional uses whenever the current binding leaves the moving continuation region (`Implement…:149–157`). That is a product regression hidden by a one-sided departure fixture. The design does not survive this question.

## Requirement map and verdict matrix

The plan is a proposed implementation contract, so “proof” below means that the document fixes an implementable semantic and supplies a gate capable of falsifying it. It does not mean gameplay has been implemented.

| Criterion | Origin | Expected proof | Evidence actually gathered | Result | Caveat / reproduction |
|---|---|---|---|---|---|
| README Expected remains the product authority | Handed; plan §1 | Every course rule is compatible with the cited Expected scenes, or explicitly marks a product decision that changes them | The plan correctly names README as authority (`Implement…:3–5`), but §5 makes leaving the continuation region a mandatory stop (`:155`) while Expected says the drone may continue lighting a cave after the player walks past and only return when no lighting remains (`README.md:71–77`), and says separation alone does not outweigh worthwhile work (`README.md:33–35`) | **Fail** | `nl -ba README.md | sed -n '31,78p'; nl -ba research/proposal/Implement\ the\ Retained\ Course\ Brain.md | sed -n '147,159p'` |
| One explicit cross-outcome objective, with no hidden priority/count/percentage policy | Handed; plan §2 | A dimensionally defined loss whose terms have executable units and fixed semantics; G12 expected alternatives fixed before code | `w[e] * ticks` is added to per-event damage fractions (`:47–53`). With coefficients one, delaying one unit effect by one tick costs 1 while any sublethal one-event hit costs less than 1. If harm is meant to persist after impact, the document never defines that state, healing, or repeated-event accumulation. G12 permits the guaranteed policy failure to be repaired later by “one documented policy change” (`:65`, `:329`, `:351`) rather than selecting the policy now | **Fail** | The review probe printed that a 20% hit loses to a one-tick normalized-work delay under the stated event reading: `python3` calculation recorded below under Landed findings |
| Normalisation is partition-invariant rather than a raw count under another identity | Handed; G03/G12 | Equivalent current effects receive equal total worth regardless of artificial stack/pocket/vein partition, with conservation rules covering all identity histories | Loot assigns one unit per **original observed stack** and conserves that lineage through merges (`:60`, `:96`), while G03 requires the same future to be independent of artificial partition (`:320`). One original stack of 20 has worth 1; two originals of 10 merged into the same current stack retain worth 2. Pocket membership has a lineage rule but no canonical initial partition rule | **Fail** | `nl -ba … | sed -n '55,69p;83,97p;316,321p'`; counterexample printed by the local arithmetic probe |
| Sunk costs disappear while real future switching effects remain | Handed | Same now-state with different past travel produces the same choice; future lost partial work/cooldown/rejoin remains | §2 explicitly removes past costs and earned effects and keeps future switching effects (`:69`); G03 directly varies past travel (`:320`) | **Pass** | This is a plan-level pass only; no implementation evidence is claimed |
| Common endpoint `H` is computable, stable enough to compare, and cannot hide opportunity order | Handed; plan §2 | Deterministic reference construction and treatment of every incompatible/unproved opportunity; perturbation tests for unrelated set growth and reference discovery order | `H` is derived from a feasible serial reference; when that cannot be proved it becomes the longest individually proved result and “remaining opportunities” become unresolved (`:40–42`). The plan does not select which individually proved subset wins under budget, or test that source/refinement order cannot alter `H` and the set admitted into `L`. It does honestly name horizon/set changes as a risk (`:373`) | **Partial** | G11 tests eventual finite discovery but not equivalent decisions under different reference-proof order |
| Stable continuation admits a genuinely better newcomer without arbitrary hysteresis | Handed | Same physical future yields the same switch opportunity independent of route segmentation; uncertain newcomers have a defined admission rule | Between boundaries a challenger needs nonoverlapping justified bounds; at a boundary nominal loss alone may switch (`:73–75`). Therefore an identical route represented by a waypoint in 8 ticks switches then, while one with the next waypoint in 100 ticks retains for 100 ticks. “Sufficiently modelled,” “urgent,” and “material” are undefined policy predicates. The executor’s waypoint granularity becomes hidden hysteresis | **Fail** | `nl -ba … | sed -n '71,77p;100,117p'`; counterexample printed by the local probe |
| Wrong-choice cost is honestly bounded | Derived from the plan’s justification | If the document claims cost is bounded by the next control boundary, no allowed boundary action has irreversible consequences beyond it | The claim at `:77` conflicts with registered in-flight projectiles and actual mana/cooldown at use (`:127–133`), and with permanent world interactions. A shot or torch chosen from nominal-only evidence cannot be undone at the next waypoint | **Fail** | The mechanics are stated by the plan itself; no external assertion is needed |
| Random enemy motion has implementable uncertainty semantics | Handed | Defined residual state, sample sufficiency, reset/eviction, horizon/context grouping, and exact influence on nominal/bounds/admission | §4 correctly treats no data as unknown, detects discontinuities, and refuses to call empirical ranges guaranteed (`:137–145`). It leaves “bounded running residual summaries,” “relevant movement context,” “sufficiently modelled,” and the translation from residuals to comparison bounds unspecified. Those choices determine whether a new enemy can interrupt work and are behavioural tunables, not storage details | **Partial** | G06 catches zero-variance and stale-trajectory mutations, but two materially different residual estimators can both pass it |
| Player hit during companion projectile flight repairs from observed state without false causality | Handed | Separate external change, in-flight lineage, next-use rebinding, and censored intrinsic learning | The five-step contract explicitly keeps the projectile physical, dirties old target-path predictions, rebinds the next use, and separates intrinsic from interference-conditioned learning (`:127–145`). G05 and G15 attack predicted shove reuse, wrong hook attribution, contamination, and successor geometry (`:322`, `:332`) | **Pass** | Exact Terraria hook support remains to be established during implementation; unknown attribution is allowed rather than invented |
| Repeated weaving and actual cave departure are both handled without timer or telepathy | Handed | Paired fixtures: local reversal/weave retention; obsolete surface-work departure; worthwhile autonomous-work departure | The shared admission/continuation regions and coherence evidence avoid destination telepathy (`:147–153`). G07 only weaves **inside** the continuation region and then requires following after a cave descent (`:324`). It never tests a transient boundary crossing during a local weave or the README cave-lighting case. The hard region exit in `:155` decides the outcome before `L` can compare it | **Fail** | This is the principal product contradiction, not merely missing test coverage |
| Exactly one owner authorises physical actions and resources | Handed | Every native use, including opportunistic work, has a course-approved binding/resource phase before use or an explicit nonchoice physical contract | Combat is strong: late facts may veto, substitutions require a revised accepted binding, and no tactical fallback may secretly fire (`:100–117`). Incidental work remains ambiguous: the migration row says only “accepted binding/phase compatibility” and receipt-after-effect (`:270`), while the target layout leaves `ConsiderIncidentalInteractions` among preserved “other existing files” (`:175–257`). Today that component selects and performs a pot/torch target after the grant (`ConsiderIncidentalInteractions.cs:33–53`). The plan never says whether the incidental target/effect/resource is itself bound before the native use | **Partial** | Counterexample: one remaining cargo slot is forecast for an observed drop; an incidental pot is broken en route and its contact pickup fills the slot. The course repairs after an irreversible, unpriced resource change |
| Folder and source migration are exact enough to remove duplicate brains | Handed | Named target owners plus explicit retirement/move obligation for every current chooser, combat hold, grant, and incidental owner | §7 provides a concrete target tree and a useful removal table for selector, scheduler, validation, ordering, reunion factors, combat budget/hold, grants, observation, and diagnostics (`:173–272`). It does not resolve the incidental owner above, and player-to-enemy native hit ownership is split only broadly between EnemyIntegration and PlayerIntegration (`:179–205`, `:248–250`) | **Partial** | Stronger than the earlier proposal; the gap is a physical-action authority gap, not a demand for listing every unchanged file |
| Shared budget includes first usable prefix, cold start, setup, repair, combat and route work | Handed | One budget; deterministic operation cuts; wall-clock production cuts; no fallback escape; first action available before suffix; cold and warmed measurements | Tick order and §6 provide a cheap legal continuation before deeper search, fair resumable sources, one `DecisionWorkBudget`, explicit mandatory-work accounting, atomic-overrun measurement, and cold/warmed reporting (`:100–117`, `:161–171`, `:336–342`). Current source confirms the existing total planning setting is 12 ms and the private combat setting is 4 ms (`BehaviourWeights.cs:138–146`, `:380–395`), so the plan correctly avoids pretending the current whole brain has a demonstrated 4 ms total | **Pass** | No runtime result exists; the plan says so. Resource limits remain measurements/configuration to choose during implementation (`:171`, `:304`) |
| Whole-brain telemetry is causally useful and bounded | Handed | IDs/phases, selected and omitted coverage, receipt lineage, work slices, corruption/overflow handling, replay scope | §8 specifies phase-ordered identities, typed payloads, coverage forms, comparison terms, dependency invalidation, budget slices, bounded snapshots, reserved critical capacity, explicit gaps, causal-strength grading and counterfactual labels (`:274–310`) | **Pass** | Exact default byte/event caps are deliberately measurement-derived and do not prove live overhead |
| One end-to-end implementation run without staged playtest dependency | Handed | Dependency-ordered internal gates, complete brain switch/removal, full headless evidence, then one live acceptance | §10 explicitly makes stages internal, removes compatibility paths before final integration, requires full verification and review, and asks for one final live run only after the whole brain is delivered (`:344–357`) | **Pass** | A final live run may still find defects; the plan labels that honestly |
| G01 original swap | Handed | Capture-shaped geometry plus finite completion/no unchanged A→B→A | Gate retains missing-input limitations and attacks identity recreation, suffix loss, false torch credit (`:318`) | **Partial** | Exact causal replay is impossible if the capture lacks the frozen inputs; G01 must remain a bounded reconstruction, not proof of the original cause |
| G02 concrete order | Handed | Multi-site/drop/region course against matched reactive and exact oracle | Covers seven torches, drops, overlap, first-five mutation, route/time, and exact small enumeration (`:319`, `:338–340`) | **Pass** | The oracle validates the selected policy, not that the policy is right |
| G03 relative choice | Handed | Same future independent of sunk spend and artificial partition | Past-spend arm is good, but the original-stack worth rule contradicts partition invariance, and no route-boundary or `H` proof-order arm exists (`:60`, `:96`, `:320`) | **Fail** | Close the semantic contradiction before a fixture can have one expected answer |
| G04 useful attack now | Handed | Earliest native-valid accepted opener while deeper search is cut | Explicitly separates accepted opener, movement and forbidden fallback bypass (`:321`) and matches tick-order authority (`:107–117`) | **Pass** | It does not settle simultaneous direct tool-versus-shot ordering under incomplete discovery; coverage must remain explicit |
| G05 external hit | Handed | Realized target state, separate projectile, censored learning | Directly attacks predicted shove reuse, `OnHurt` confusion, and contaminated gravity learning (`:322`) | **Pass** | Requires an attributable hook where Terraria supplies one; unknown is accepted otherwise |
| G06 unpredictable motion | Handed | Seeded motion/event-order sequences; useful attacks without stale aim or identity churn | Strong mutation set (`:323`), but insufficient to select the undefined residual/context/bounds semantics in §4 | **Partial** | Add a discriminator for the estimator’s behavioural contract, not merely a second seed |
| G07 departure and weaving | Handed | Preserve local work through reversal and promptly drop obsolete work on departure | Tests only weaving that remains inside the continuation region and only a departure whose expected answer is follow (`:324`) | **Fail** | It omits the product’s “player walks past; companion keeps lighting” control and transient region-crossing weave |
| G08 repairs across domains | Handed | External satisfaction, reuse, capability and local/remote edits invalidate only dependencies | Wide cross-domain table with correct native-credit mutations (`:325`) | **Pass** | Does not by itself resolve whether an incidental action was authorised before it happened |
| G09 interleaving | Handed | Legal concurrency, resource compatibility, accepted combat only, earned resume | Covers travel/incidental, projectile/movement, quiet native mining, accepted combat interruption, vein resume/drop (`:326`) | **Partial** | Missing last-capacity/last-resource incidental counterexample; “legal incidental” can be declared after only phase compatibility |
| G10 global spatial contract | Handed | Same shared facts at discovery/binding/destination/use; 2×2 controls | Covers every domain and relevant consumer/mutations (`:327`) | **Pass** | Source implementation still has to prove consumer enumeration; the plan supplies the audit target |
| G11 budget and lifecycle | Handed | Cuts across phases, sustained changes, resets, no secondary solve, eventual finite discovery | Good forced-cut and mutation coverage (`:328`, `:334`) plus explicit whole-work timing (`:342`) | **Pass** | Stable finite liveness is intentionally weaker than liveness under adversarial arrival (`:15`) |
| G12 policy contrasts | Handed | Expected alternatives fixed before running and one objective selects them without a private category rule | The scenario list is appropriate, but the stated loss has the harm unit failure and the plan delegates the resulting central policy decision to the implementation run (`:65`, `:329`, `:351`) | **Fail** | A test cannot close an unspecified product tradeoff merely by failing one formula |
| G13 encounter conduct | Handed | Boss/event optional exclusion, survival before damage, player death continuity | Contract and mutations are explicit (`:34`, `:330`) | **Pass** | Ordinary combat remains governed by the unresolved objective, as intended |
| G14 observability | Handed | Writer-to-renderer, malformed/old/overflow, missing evidence stays missing | End-to-end path and destructive mutations are explicit (`:331`) | **Pass** | This review did not execute nonexistent fixtures |
| G15 model fidelity | Handed | Native-versus-predicted successor comparisons including realized knockback and interference | Covers position/velocity, cargo, mana, light, work, duplicate in-flight damage and repair (`:332`); §4 refuses unsupported precise chains (`:141–145`) | **Pass** | Advanced combination acceptance may remain unmet and must be reported as such (`:363`); that is an honest named limitation |

## Attack report

| Angle | What I tried | Outcome | Evidence |
|---|---|---|---|
| Alternative | Asked whether cheaper reactive A was weakened to make the retained course win | **Survived** | The reference receives identical candidates, models, policy, executor, observation and budget and differs only in suffix retention (`Implement…:338–340`). The rationale’s reopening condition likewise gives A the local tactical model and effects. This is a fair comparator, not a missing-feature strawman |
| Coverage gap | Crossed the departure rule against both departure outcomes already in Expected: abandon obsolete surface fight; continue worthwhile autonomous cave lighting | **Landed** | The proposal gates only the first (`:155–157`, `:324`). Expected requires both (`README.md:63–77`). A blanket continuation-region exit cannot express the second |
| Real input | Used (1) a sublethal predicted player hit versus one tick of a normalized effect, (2) equivalent route futures with different waypoint subdivision, and (3) an incidental pot before a capacity-reserved drop | **Landed** | (1) `:47–53`; (2) `:73–75`; (3) plan `:270` plus current `ConsiderIncidentalInteractions.cs:33–53` and pot execution in `CollectNearbyItems.cs:445–470` |
| Overhead | Looked for unbounded global permutations, hidden private combat time, setup outside the timer, cold-start omission and telemetry blocking | **Survived with a named limit** | Bounded local moves, fair resumable cursors, a global budget, atomic-slice reporting, first-prefix latency, warmed/cold runs, allocations/peak bytes and diagnostic cost are all required (`:161–171`, `:295`, `:338–342`). The plan correctly refuses to call the allowance a whole-frame hard guarantee (`:167`) |
| Residue | Traced whether old selector/hold/fallback/incidental authorities are explicitly retired or subordinated | **Landed narrowly** | Selector, scheduler, validation, ordering and combat hold paths have removal obligations (`:259–272`). The preserved post-grant incidental executor has no equally exact bind-before-use rule, leaving a second physical-choice seam |
| Smell with a future | Asked what implementation detail will silently become behaviour when the system evolves | **Landed** | Navigation waypoint placement controls when nominal-only alternatives may replace the current action (`:73–75`). Route smoothing, waypoint spacing or a new movement representation would therefore change decision retention without changing policy configuration |

## Landed findings and concrete counterexamples

### F0 — the endpoint makes a lone completed effect equal to omitting it

There is an even smaller failure than the cross-unit example below. Let the finite admitted set contain one worthwhile effect of worth `w`, with completion at `H` and zero rejoin time. The course that completes it pays `w * min(H, H) = wH`. The empty course omits it and also pays `wH`. Any positive companionship, harm, turning, or setup cost makes the empty course strictly win. Closing the episode and opening another does not repair this: the omitted effect is again compared at a new endpoint and can lose the same tie forever. Thus G01/G02’s finite stable completion requirement is not implied by the selected objective even with perfect estimates and unlimited compute.

An exponentially discounted delivery objective would distinguish every finite completion from omission and, with a single fixed discount scale applied to rewards and costs, would be memoryless under rebasing. It would **not** preserve the present linear weighted-completion ordering whenever effects trade timing. For equal effects, course A completing at ticks `[0,100]` and B at `[40,40]` gives linear delays 100 versus 80 (B wins), while exponential reward at scale 100 gives `1 + exp(-1) = 1.368` versus `2*exp(-0.4) = 1.341` (A wins). It preserves only componentwise/Pareto timing dominance. A scale derived from the currently proved action set is also an endogenous policy: discovery or episode change can alter the scale and reorder otherwise unchanged courses. Freezing it postpones that jump; it does not remove it. Harm events and companionship occupancy must use the same discount kernel and explicit units, or the claimed memoryless rebase fails.

**Smallest closure condition:** finite completion must be strictly preferable to omission when all other consequences are equal, while the plan states whether it preserves linear same-output ordering or replaces it with a different declared time preference; any time scale must be acknowledged as policy and held stable against irrelevant opportunity-set changes.

### F1 — `L` does not yet define an executable ordinary safety tradeoff

Sections 2.2–2.4 combine weighted completion delay, measured in normalized-effect ticks, with expected damage events, measured as a fraction of a life bar. “All coefficients are one” is still an exchange rate: it makes one tick of one full normalized effect equal to one complete life bar of event damage. The local arithmetic probe was:

```sh
python3 - <<'PY'
for damage_fraction in (0.01, 0.2, 1.0):
    for delay in (1, 2, 60):
        work_loss = damage_fraction
        safety_loss = delay
        print(damage_fraction, delay, work_loss, safety_loss)
PY
```

Under the event-local reading stated at line 53, a course that delivers one normalized effect now and accepts 20% player damage has loss `0.2`; delaying the effect one tick to prevent that hit has loss `1`, so the work-first course wins. Even a 100% hit loses against a two-tick delay. If the integral instead means that each hit creates a persistent deficit for all later ticks, that persistence, healing, death, and repeated-hit accounting are absent from the contract. Immediate self-safety and boss policy do not resolve ordinary predicted **player** harm. G12 is likely to reject the default, but “make one central policy change” leaves the architecture’s central cross-outcome policy to whoever implements the failed test.

**Smallest closure condition:** the plan must define one dimensionally complete ordinary harm/delivery comparison, including the temporal meaning of a damage event, and state G12’s expected alternatives under that definition before implementation begins.

### F2 — actual departure is turned into a category veto and breaks the autonomous-cave scene

Section 5 says that when an optional binding leaves the continuation region the brain stops initiating optional uses and repairs toward companionship. This bypasses `L`; no amount of remaining cave-light value can win. It correctly ends the obsolete surface-zombie case, but it also ends the README’s 6:00 cave-lighting case once the surface-walking player carries the moving continuation region away. The rationale had said a meaningful short finish may still fit and that leaving-player relevance should be tested, not hard-coded by category. G07’s “real cave descent” asserts only the abandon-and-follow answer, and G12’s “local versus remote darkness” does not name the walking-past-the-cave expected alternative.

**Smallest closure condition:** the acceptance contract must contain two otherwise comparable genuine departures—one where obsolete work is dropped and one where Expected’s worthwhile autonomous cave work continues—and the same declared relevance/rejoin policy must select both without a universal region-exit veto.

### F3 — executor boundary placement is an unowned switching threshold

At a normal execution boundary, lowest nominal loss wins even when comparison bounds overlap or do not exist. Between boundaries, that same challenger cannot replace a valid action unless both justified bounds prove the improvement. Consider current course A (`nominal=10`, no bounds) and executable B (`nominal=9`, no bounds) over one unchanged physical route. With a navigation waypoint in eight ticks the switch occurs at tick 8; after route smoothing removes that waypoint and the next mechanically required control boundary is at tick 100, it occurs at tick 100. Nothing about the effects, uncertainty or switch cost changed. A movement representation detail became the hold policy.

The claimed wrong-choice bound also fails for irreversible actions. A nominal-only shot spends mana/cooldown and leaves a projectile in flight; a permanent interaction can survive every later boundary. Continued safety checking does not bound that cost by the next boundary.

**Smallest closure condition:** equivalent physical continuations with different waypoint subdivision must expose the same policy-level reconsideration opportunity, and any claimed bound on a fallible nominal choice must include its irreversible effects rather than ending at the next executor callback.

### F4 — stable identity conserves an arbitrary original-stack count

One original stack of 20 is assigned one unit. Two original stacks of 10 that merge into the exact same current stack conserve two units. The executable future, accommodated quantity and route may be identical, but the choice changes because of an earlier engine partition. That conflicts with G03’s same-future/artificial-partition invariant and reintroduces raw counting at the lineage level. Pocket initial segmentation has the same unresolved risk even though later split/merge lineage is tracked.

**Smallest closure condition:** G03 and the worth rule must agree on whether two physically equivalent current opportunity sets with different partition histories may differ; if they may, the history-dependent preference must be declared as product policy rather than called partition invariance.

### F5 — random-motion uncertainty and newcomer eligibility still require behavioural choices

Section 4 does the important epistemic work: no samples is unknown rather than zero variance; empirical ranges are not guaranteed; discontinuities reset a motion episode; unsupported dynamics cannot certify a shove chain. It does not define how the bounded residual summary is maintained, what “relevant context” means, when it is sufficient, or how it becomes nominal/lower/upper estimates. Section 2 then relies on “sufficiently modelled” and “urgent” to decide whether an uncertain challenger can act or immediately invalidate the incumbent. Two implementations can take opposite actions on the same first-seen jumping enemy and both satisfy every sentence and G06 mutation.

**Smallest closure condition:** no-data, sample-sufficient, discontinuity and stale states must each have one declared effect on executability, nominal comparison, justified bounds and urgent invalidation; estimator resource limits may remain measurement-derived.

### F6 — incidental work has an after-the-fact receipt but no unambiguous pre-use owner

The plan is exact for shooting: only an accepted combat binding may fire. It is not exact for incidental pot/torch work. The current component chooses a target after the final grant and invokes the native method immediately. The migration table says “require accepted binding/phase compatibility” and repair dependencies in the same tick, which can mean either “the incidental action has its own accepted binding” or merely “the primary binding left the hand free.” Those are materially different architectures.

Concrete trigger: the course reserves the remaining cargo capacity for an observed drop. While traveling, an in-reach incidental pot passes the present cargo-space test, is broken, and its spawned/contact-collected contents consume the capacity. The receipt repairs the future, but the irreversible physical choice was neither compared nor resource-reserved by the sole course owner. The analogous lighting action can make a planned light effect redundant after an unpriced use, even though torches themselves are currently free.

**Smallest closure condition:** every optional native interaction must have an explicit, testable pre-use authority and resource/effect relationship to the accepted binding; G09 must include an incidental action whose effect conflicts with a reserved later resource/effect.

## Overall classification

**FAIL — not yet a complete implementation-ready plan.**

The retained-course direction, data ownership, effect receipts, combat authority, compute accounting, telemetry, matched reactive comparator and one-run construction sequence are substantially stronger than the earlier discussion proposal. The failure is narrower than rejecting that architecture. The document currently contains two conflicting product policies (departure versus autonomous cave work), one mathematically incomplete central comparison, and implementation-dependent switching/uncertainty/incidental-action semantics. Those choices sit above the coding line; letting the implementation run choose them would recreate the arbitrary hysteresis and duplicate-decision seams the plan is meant to remove.

## Must-fix before implementation versus honest named limitations

### Must-fix gates

| Failed gate | Single smallest condition that closes it |
|---|---|
| Objective / G01/G02/G12 | Finite completion is strictly better than omission when other consequences are equal, and one fully specified temporal harm-versus-effect loss produces the predeclared ordinary policy contrasts without an implementation-invented exchange rule |
| Departure / G07 / README authority | One shared rule passes both obsolete-work departure and worthwhile autonomous-cave departure; leaving the continuation region alone cannot predetermine both answers |
| Uncertainty / stable continuation | Equivalent physical futures do not switch at different times solely because their navigation waypoint list is subdivided differently; irreversible cost is included in any claimed bound |
| Partition / G03 | The worth semantics and G03 state one consistent answer for physically identical current loot/pocket sets with different partition lineage |
| Random motion / G06 | Every estimator evidence state has a declared effect on action eligibility, nominal comparison, bounds and urgent invalidation |
| Action ownership / G09 / migration | Every incidental native edit is either course-approved before use with declared resource/effect consequences or explicitly classified as a non-discretionary physical effect; the fixture distinguishes the two |

### Limitations that are already named honestly and need not block the plan

- No document or toy probe demonstrates a runtime or architectural win; §§9–12 preserve that limitation.
- G01 cannot reconstruct missing capture inputs and must remain capture-shaped rather than causal proof.
- Finite stable-world liveness does not promise optional progress through an adversarial emergency stream (`:15`).
- An unsupported enemy model may execute useful immediate attacks and repair while leaving advanced knockback-chain acceptance unmet (`:141–145`, `:363`).
- The whole-frame runtime is not guaranteed by a planner allowance; cold/warm, atomic overrun, allocations, retained bytes and diagnostic overhead are still to be measured (`:167–171`, `:342`).
- Mastery effects, gear onboarding, unsupported modded firing mechanisms, new world edits and closed-door routing remain separate work (`:359–363`).
- The requested one-shot implementation strategy necessarily defers live feel to the final acceptance; the plan does not falsely call headless evidence live proof (`:344–357`).

## History and what it implies

`git show -s --format=fuller d315bf2` shows that the selected retained-course checkpoint deliberately stopped short of a coding specification. Its commit body says the numerical policy for unlike outcomes was a pre-migration gate and that the recheck accepted only a direction for discussion, not a finished coding specification, runtime, or gameplay result. The same commit records that no Companion source changed and no alternative was implemented or benchmarked. The current file is untracked, so it has no path history of its own; the relevant predecessor history is `b3a4ad94` (Proposal 04 explicitly left unsettled) and `d315bf2` (six-way attack and selected direction).

That history matters because this plan’s strongest unresolved point is exactly the one `d315bf2` preserved as open. Assigning all effect coefficients one and promising to change the central policy if G12 fails is not evidence that the gate has now been crossed. The ownership and evidence work has advanced from the earlier proposal; the product tradeoff has not yet been made executable.

Commands used for the history check:

```sh
git show -s --format=fuller d315bf2
git log --all --format='%H%n%B%n---' -5 -- research/proposal 'research/Decision Architecture/Retained Course Design Rationale.md'
git status --short --untracked-files=all
```

No mutation testing was appropriate: the reviewed artefact is an unimplemented plan, its proposed fixtures do not exist, and the task prohibited builds/heavy processes. No file other than this requested `/tmp` review was created or changed by this reviewer.

---

# Bounded recheck — corrected active plan, 19 September 2026

This recheck covers only the corrections to §§2, 4, 5, 7, 9–10 and the new analytical probe. It does not replace the first-pass evidence above. Line references in this section refer to the corrected active file.

## Recheck requirement matrix

| Criterion | Origin | Expected proof | Corrected evidence | Result | Caveat / reproduction |
|---|---|---|---|---|---|
| Finite delivery beats omission without an arbitrary horizon edge | Original F0 | A lone finite effect has positive value at equal costs; no `H` truncation | `J` gives every finite effect `w*exp(-t/T)` and omission zero (`Implement…:46–57`); the document explicitly rejects the old endpoint tie (`:75`) | **Pass** | Analytical probe row `finite lone effect beats omission at equal costs` passes |
| Time scale is explicit and independent of candidate partition/discovery | Original F0/F1; handed no hidden tuning | `T` has one owner, units, zero-capability rule and change contract | `T` is resting base-region diagonal divided by cruise speed, at least one tick, frozen through the episode, changing only with body capability/base geometry (`:42–44`). It is explicitly policy, fingerprinted and sensitivity-tested | **Pass** | It remains a product preference, correctly labelled rather than claimed inevitable |
| Reward, harm and companionship have executable common units | Original F1 | Same discount kernel; dimensionless event and occupancy terms; unknown tails not zero | `J` discounts effect and harm events with `D`; gap uses `D/T`; current-life denominator, lethal handling, analytic interval integration and unknown tails are specified (`:46–57`). Harm never scales with candidate count (`:69`) | **Pass** | The equal-normalised prior still needs G12 product acceptance, as the plan says |
| Effect normalisation is partition-invariant and census-owned | Original F4/G03 | Stack splits/merges conserve worth; candidate/cache order does not change denominators | Loot uses quantity over frozen observed eligible quantity per type/prefix; stable cells/work units/life holders own other worth; denominators come from observation census rather than admitted candidates (`:59–71`) | **Pass** | New material world evidence starts a logged episode; that is an observable changed input, not hidden rescaling |
| Same-output timing claim matches the selected objective | Original exponential objection | Claim Pareto timing dominance only, and expose a timing trade-off where exponential differs from linear delay | The corrected plan states both properties exactly (`:75`). The probe tests `[0,100]` versus `[40,40]` and expects the exponential result | **Pass** | The test establishes arithmetic under the selected policy, not that players prefer it |
| Stable continuation is independent of route-waypoint subdivision | Original F3/G07 | Only semantic native boundaries permit nominal reelection; proven improvement and invalidation can interrupt continuous travel | Semantic boundaries are enumerated; arbitrary waypoints/samples/path subdivision are excluded; proven improvement and direct validity checks run on any tick (`:79–85`). G07 requires identical choices under waypoint subdivision (`:358`) | **Pass** | The corrected text withdraws the false claim that consequences end at the next boundary (`:85`) |
| Actual departure can abandon obsolete work or preserve worthwhile autonomy | Original F2/G07 | Paired controls under one comparison, with no region-only veto | Leaving the continuation region now updates cost/uncertainty but does not cancel. §5 names both the autonomous cave-lighting and obsolete surface-fight outcomes (`:173–185`); G07 includes the paired cave-mouth control (`:358`) | **Pass** | Invalid required return proof can still suspend dependent discretionary use; this is a proof condition, not distance priority |
| Random-motion uncertainty has implementable states and no invented statistical confidence | Original F5/G06 | Exact key/lifecycle, empty/evicted state, sufficiency definition, bounds provenance and urgency semantics | Per-generation/model-revision/age ring, min/max/RSE diagnostics, discontinuity/reset, eviction, `SufficientlyModelled`, certified-bound provenance and harm-based urgency are specified (`:157–171`) | **Pass** | Empirical min/max is deliberately not promoted to confidence; unsupported combinations remain visibly uncertified |
| Incidental pot/torch/tool actions are accepted before native use | Original F6/ownership | Post-grant chooser retired; one-step binding carries resources/effects before grant | Migration row requires an accepted binding before the native call and turns `ConsiderIncidentalInteractions` into a pre-grant proposal source (`:288–301`) | **Partial** | G09 (`:360`) still lacks the mutation that reinstates the current post-grant chooser or performs an incidental use without its binding; outcome-only “legal incidental work” would not necessarily detect that ownership regression |
| Failed policy contrasts cannot be hand-tuned by the implementer | Handed; original F1/G12 | One consistent instruction for what happens when fixed-policy fixtures contradict | §2 says the implementer must not change objective/constants to make tests green and a contradiction is a plan defect (`:73`). Construction step 4 still says “Resolve failed preference contrasts here, in the central policy with documented reasons” (`:385`) | **Fail** | These are mutually exclusive instructions at the exact gate intended to prevent implementation-time policy invention |
| The analytical probe proves only its stated arithmetic | Derived evidence-scope check | Narrow deterministic contracts pass; no native/runtime/acceptance claim | `CheckCourseObjectiveContracts.py` prints eight passing rows and explicitly says native implementation/behavioural fit remain untested | **Pass** | Command below; this does not upgrade the plan into runtime evidence |

## Recheck attack report

| Angle | What I tried | Outcome | Evidence |
|---|---|---|---|
| Alternative | Tried to make `T` depend on candidate duration/count, recreating the endogenous median-scale problem | **Survived** | `T` depends only on base body/intent geometry and cruise capability, not candidates, routes or heading (`:42–44`) |
| Coverage gap | Paired “leave obsolete fight” against “continue autonomous cave lighting” | **Survived** | Both are now normative in §5 and G07 (`:181`, `:358`) |
| Real input | Empty residual history, evicted prediction age, model revision, continuous correction, unsupported envelope | **Survived** | Each state has an explicit consequence in `:159–165`; no sample threshold or zero-variance default is permitted |
| Overhead | Checked whether infinite integration or residual history reopened unbounded work | **Survived** | Gap intervals are piecewise/analytic, unknown tails stay unresolved, residual forecasts are a bounded ring, and global budget/storage rules remain (`:57`, `:161`, `:193–197`) |
| Residue | Searched for old post-grant incidental authority and old finite-horizon/waypoint semantics | **Landed narrowly** | The migration removes post-grant choice in prose (`:299`), but G09 lacks a mutation that proves it; no operative old `H` or waypoint-boundary rule remains |
| Smell with a future | Looked for a future implementer instruction that could override the fixed policy gate | **Landed** | `:73` forbids tuning on failure while `:385` orders central-policy resolution during construction |

## Recheck classification

**PARTIAL — the six original architectural blockers are substantively closed; one direct instruction contradiction and one ownership-test gap remain.**

The objective now has coherent finite-delivery, omission, timing, harm and companionship semantics. The user’s cave-autonomy requirement and the opposite departure case coexist under one comparison. Semantic action boundaries no longer inherit navigation topology. Random motion has a concrete epistemic lifecycle. Incidental actions have a single declared pre-use owner. Nothing in this bounded recheck demonstrates gameplay quality or runtime cost, and the plan does not claim otherwise.

### Smallest remaining closure conditions

| Remaining gate | Single smallest condition |
|---|---|
| Fixed policy authority / G12 | Construction step 4 must say the same thing as §2: a contradictory preference fixture stops policy completion as a plan defect; it cannot authorize the implementer to change the objective merely to pass |
| Incidental ownership / G09 | G09 must fail when an incidental pot/torch/tool native call occurs without its accepted one-step binding before the grant |

## Recheck command and scope

```sh
python3 'research/Evaluation and Observability/Probes/CheckCourseObjectiveContracts.py'
```

Observed result: all eight analytical contracts passed. No build, native replay, game, UI, heavy timing process or repository mutation was run. I appended only this requested `/tmp` review.

---

# Final narrow closure check — 19 September 2026

| Remaining criterion | Evidence in corrected active plan | Result | Caveat |
|---|---|---|---|
| Fixed policy authority / G12 | Construction step 4 now matches §2: “A policy contradiction is a design defect to report with its counterexample; do not silently redefine the objective to make fixtures green” (`Implement…:385`) | **Pass** | This fixes the contradictory implementation instruction; it does not claim the selected policy has passed future native/product fixtures |
| Incidental ownership / G09 | G09 now fails an unbound pot/torch/tool use and restoration of the post-grant chooser, and includes the last-cargo-slot reservation case (`:360`) | **Pass** | The fixtures do not exist yet; this is acceptance-contract closure, not executed evidence |
| Pot-breaking weapon use versus combat-only hostile attacks | Feasibility distinguishes an accepted pot-targeted world-work binding from attacks on hostiles, preserves quiet mining/approach, and treats collateral native hits as receipts rather than retroactive authority (`:36`). G09 carries the corresponding pot-binding/unrelated-hostile control (`:360`) | **Pass** | A pot-breaking weapon use must still consume its declared native resources and cannot substitute a hostile target |

## Final classification

**PASS as a complete implementation plan.** The two residual conditions from the bounded recheck are now closed, and the pot-breaking clarification preserves both README’s bow/sword pot scene and the combat-only/quiet-mining ownership rules. This verdict accepts the plan’s internal contracts and falsifiable gates. It does not certify code that has not been written, native fixtures that have not run, the runtime envelope, or final gameplay feel.

No broader review was repeated. No repository, Slate, memory, source, fixture, build, replay, game or UI action was performed; only this `/tmp` review was appended.
