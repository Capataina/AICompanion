# Starsector ship and weapon AI: independent control under resource risk

**Accessed 2026-09-12.** This case asks a narrow question: what does public mod code show about running movement and weapons independently while preserving recovery/risk and compatibility ownership? Starsector is real-time inertial ship combat; it is evidence about control separation, not Terraria platform movement or companion social behaviour.

## Evidence and mechanism

Starsector base AI is proprietary and was not retrieved. The public case is Halke1986’s AI Tweaks at commit [`6ced7000cfdad6511f8e7d48d82b70fc24f981e5`](https://github.com/Halke1986/starsector-ai-tweaks/tree/6ced7000cfdad6511f8e7d48d82b70fc24f981e5). The maintainer describes a rewritten autofire AI and mostly rewritten ship AI, but also warns that faster reactions can defeat intentionally human-like vanilla delay and make behaviour look “uncanny, machine-like.” This is author intent/product judgement, not a controlled player study. [README](https://github.com/Halke1986/starsector-ai-tweaks/blob/6ced7000cfdad6511f8e7d48d82b70fc24f981e5/README.md).

[`CustomShipAI.kt`](https://github.com/Halke1986/starsector-ai-tweaks/blob/6ced7000cfdad6511f8e7d48d82b70fc24f981e5/src/com/genir/aitweaks/core/shipai/CustomShipAI.kt) keeps separate manoeuvre, attack and burst targets, invokes vanilla advance, handles assignment/vent/system/manoeuvre in an update loop, and yields on RETREAT assignment. [`AutofireAI.kt`](https://github.com/Halke1986/starsector-ai-tweaks/blob/6ced7000cfdad6511f8e7d48d82b70fc24f981e5/src/com/genir/aitweaks/core/shipai/autofire/AutofireAI.kt) refreshes weapon targets every 0.25–0.50 seconds and fire eligibility every 0.10–0.20 seconds; invalid/untrackable targets refresh immediately, while a firing sequence is not interrupted. [`SelectTarget.kt`](https://github.com/Halke1986/starsector-ai-tweaks/blob/6ced7000cfdad6511f8e7d48d82b70fc24f981e5/src/com/genir/aitweaks/core/shipai/autofire/SelectTarget.kt) searches out to 1.5 times engagement range and filters tracking and occlusion.

[`VentModule.kt`](https://github.com/Halke1986/starsector-ai-tweaks/blob/6ced7000cfdad6511f8e7d48d82b70fc24f981e5/src/com/genir/aitweaks/core/shipai/VentModule.kt) carries flux/damage history, evaluates threat and retreat pressure, waits around bursts, and can continue an initiated vent when the start condition holds even if safety later worsens. That is a concrete commitment rule: cancellation is governed by task-owned conditions, not every momentary signal.

DesperatePeter’s [Advanced Weapon Control README at commit `b19cd7c47770dcbc9ccdac3ae6eb7535b2b62d56`](https://github.com/DesperatePeter/starsector-advanced-weapon-control/blob/b19cd7c47770dcbc9ccdac3ae6eb7535b2b62d56/README.md) says it can coexist with custom weapon AI only where the other mod does not manipulate weapon AI in combat. That is direct maintainer compatibility evidence for one writer per output surface; it does not establish an engine-level enforcement mechanism.

## Comparison and limits

| Finding | What it actually governs | AICompanion transfer limit | Evidence that would favour transfer |
|---|---|---|---|
| Lower-frequency target refresh, higher-frequency firing | Weapon aiming/fire policy separate from ship steering cadence. | Terraria projectiles and NPC feet have different collision, target and hand constraints. | Logs show legal shots missed because body replans monopolise aiming. |
| Do not interrupt a firing sequence | Short action commitment. | Terraria tools and weapons may have different cancellation safety. | Aiming traces show target churn causes worse hit/useful-damage results. |
| Vent commitment has an initiation predicate | Recovery can retain ownership through a transient adverse signal. | Flux is not health, breath, terrain or player distance. | Survival/work traces show oscillation between begin/cancel despite the task still being viable. |
| One active weapon-AI writer | Compatibility/output ownership. | AICompanion is one mod, but subsystems can still collide. | Per-tick hand/weapon claims reveal contradictory control writes. |

The countercase to splitting feet and weapons is the maintainer’s own human-delay warning: technical reaction speed can degrade the readable/companion-like feel. A shared global controller might also prevent conflicts, but it risks serialising legal simultaneous work. The discriminating experiment is to trace feet owner, aim owner, tool/weapon state, denied action and target validity, comparing independently scheduled aim with a resource arbiter. Measure conflicts, missed legal shots, tool interruption and visible aim/body mismatch.

## Source ledger and gaps

| Source | Date / retrieved | Evidence quality | Claim and caveat |
|---|---|---|---|
| AI Tweaks source `6ced700` | revision current at retrieval / 2026-09-12 | Primary implementation | Timings, target refresh and vent logic are direct code facts; no player outcome follows. |
| AI Tweaks README | undated rolling repository / 2026-09-12 | Primary maintainer | Balance/readability warning is intent/judgement. |
| Advanced Weapon Control source and README `b19cd7` | revision current at retrieval / 2026-09-12 | Primary maintainer/source | Simultaneous-writer warning and distinct tag/ship-AI surfaces; exact runtime conflict coverage unknown. |

No base-game source, authoritative API documentation, benchmark, or scored player corpus was recovered in this lane. No claim is made about Starsector’s native pursuit, flux, shield or retreat formulae, and no broad consensus is asserted.

## Mechanism walkthrough: three targets, not one

The useful distinction in AI Tweaks is not merely “movement and weapons are separate.” [`CustomShipAI.kt`](https://github.com/Halke1986/starsector-ai-tweaks/blob/6ced7000cfdad6511f8e7d48d82b70fc24f981e5/src/com/genir/aitweaks/core/shipai/CustomShipAI.kt) keeps three target roles with different retention rules:

| State | Meaning in the code | When it changes | Why it is not a general target variable |
|---|---|---|---|
| `maneuverTarget` | Ship whose position informs hull movement. | Periodic target update, assignment/elimination, fleet segmentation, nearest eligible target, or no target during exploration/navigation. | It can be absent while the ship navigates, and it is chosen for fleet/position concerns. |
| `attackTarget` | Target paired with a selected weapon group. | Periodic evaluation over group/target opportunities, with range, angle, flux, occlusion and assignment factors. | It may differ from the manoeuvre target and is null while navigation has priority. |
| `finishBurstTarget` | Previous attack target retained only while its old weapon group is still in a firing sequence. | Created on a target change; cleared when invalid or no former-group weapon is still firing at it. | It prevents a target refresh from cancelling an already-owned burst. |

The AI updates threat-vector motion every frame but performs the heavier state/target refresh on `defaultAIInterval()`. It then advances vanilla support, assignment, vent, system and manoeuvre each frame. This is a deliberate multi-rate design: smooth controls use continuously updated facts, while target policy is not recalculated at every render/physics step. The source does not establish the exact `defaultAIInterval` duration in the excerpts analysed here, so it should not be reported as a fixed number.

`maneuverTarget` is selected through an ordered policy: retain while a system holds targets; null it when navigating; respect an elimination assignment; prefer a skirmisher’s nearby visible target or fleet-segmentation target; stay uncommitted in exploration; then fall back to nearest valid target. Its target score uses exposed angular area squared divided by distance squared, after an occlusion/visibility filter. This is not a shortest-path reachability proof. In open inertial space, “reachable” is a continuous question of heading, speed, turn rate, combat assignment and threat geometry; the code uses an opportunity/risk heuristic rather than a binary tile route.

`attackTarget` evaluates each weapon group separately before taking the group-target pair with the best score. `evaluateTarget` penalises turning time, range scaled by group DPS at that range, and remaining target flux; it favours already-overloaded/venting targets, allied focus, continuity if the current target is in range and arc, and an explicit eliminate assignment. It penalises occluded targets and some poor matchup shapes. Thus “shoot the same thing the hull chases” is a counterfactual simplification: a weapon group can choose a useful available target even when body manoeuvre is solving a different fleet/position problem.

This selection remains a heuristic, not a utility architecture in the formal sense. Scores are hand-authored additive terms with hard ordered branches around them; values are not calibrated against a shared reward. That is an important countercase for the project’s utility discussion: output separation and commitment can coexist with local heuristic/rule selection.

## Flux, safety, and cancellation are a state machine—not a scalar veto

[`VentModule.kt`](https://github.com/Halke1986/starsector-ai-tweaks/blob/6ced7000cfdad6511f8e7d48d82b70fc24f981e5/src/com/genir/aitweaks/core/shipai/VentModule.kt) tracks damage and flux history, weapon threat and missile threat, and maintains separate `isBackingOff`, `shouldFinishTarget`, `ventTrigger`, `isSafe`, and `isSafeWhileParked` state. This is materially richer than a condition such as “high flux means retreat.”

The code begins backing off for several distinct reasons: a shieldless ship cannot pay a non-burst weapon’s flux cost; a shielded ship exceeds hard-flux threshold; predicted flux growth passes a hold-fire threshold; shields are down while recent damage is high; or it began venting while under fire. It has separate conditions that keep a finishing opportunity from being abandoned. While backing away it computes a standoff distance: if safety at rest is unknown it moves effectively far away; when a safe position is reached it keeps enough distance to stop attacking and dissipate flux; later attained distance becomes a baseline rather than an invitation to close again. Its heading is then either a target-relative position offset by the smoothed threat vector, a direction opposite threat, or the current course when no threat vector exists.

For venting, the module first calculates whether it should initiate and estimates duration with an optimistic modifier before venting and a pessimistic modifier while venting. If initiation and predicted safety both hold, it starts. If the initiation condition and a prior `ventTrigger` remain true, it continues even after safety worsens; the comment explains that otherwise a ship can lose every opportunity to vent. Before issuing `VENT_FLUX`, it waits for weapon bursts to subside, checking that condition every frame. This gives three distinct cancellation semantics:

1. A newly proposed vent needs an initiation condition and predicted safety.
2. A started vent retains ownership under a narrower continuation condition rather than re-running the entire start test.
3. A burst can delay the *start* of venting, so a safety action does not sever an output already in a firing sequence.

That form transfers more safely than flux thresholds: a Terraria recovery, work swing, or firing burst can distinguish **admission**, **continuation**, and **release** predicates. Flux itself, threat radii, open-space standoff, shields and vent duration do not transfer. The countercase is an action whose continued execution creates irreversible harm; such an action needs a stronger cancellation rule than AI Tweaks’ “do not lose opportunities” policy.

## Three concrete source paths, rendered as labelled hypotheticals

These are walkthroughs of code paths, not observations of a live Starsector battle.

### A. The hull repositions while a burst finishes

*Hypothetical inputs:* weapon group A is firing at target X; a periodic update finds weapon group B has a higher-valued target Y; movement continues to prefer manoeuvre target Z.

1. `updateAttackTarget` installs Y/B and records X/A as `finishBurstTarget` / `finishBurstWeaponGroup` when X remains valid.
2. `updateFinishBurstTarget` retains X only while an A-group weapon reports both `isInFiringSequence` and `target == X`.
3. The manoeuvre subsystem continues from its independent manoeuvre target, while autofire’s short interval and firing-cycle logic keep the already-issued burst coherent.

The relevant ownership contract is not “weapons always override feet.” It is that a target-policy change does not retroactively invalidate an action that already owns a burst. A Terraria analogue would be a bow/knife shot that has already become a projectile: later target choice must not pretend that the old shot never existed. A disconfirming trace would show that burst retention repeatedly fires into no-longer-valid space or materially blocks urgent defensive targets; then an interruption rule should be tightened.

### B. Flux pressure changes a movement objective without erasing target knowledge

*Hypothetical inputs:* a shielded ship predicts dangerous flux growth; an enemy remains a valid manoeuvre target; the threat vector includes several nearby hostile hulls.

1. `shouldBackOff` becomes true from flux/risk conditions, and `VentModule.overrideHeading` takes movement authority.
2. If a safe parked state exists, it produces a target-relative standoff position displaced by the aggregated threat vector. If no stable target-relative safety exists, it flies opposite the threat vector or retains current course.
3. `attackTarget` and weapon-group selection remain state, but movement’s expected heading becomes recovery-oriented; a later safe/vent condition decides whether venting can begin.

The transferable point is that threat avoidance can override body destination while preserving aim/target context. It does **not** prove that AICompanion should fire during every recovery; that depends on the real arm/tool conflict, projectile trajectory and player-facing readability. The project already has the more appropriate local form: feet recovery can coexist with independent shots when no work tool owns the arm.

### C. There is no good attack target, without declaring the world impossible

*Hypothetical inputs:* no considered weapon group has a non-occluded target within effective range; an assignment location or manoeuvre target exists.

1. `findNewAttackTarget` yields no best group-target pair.
2. It falls back to the manoeuvre target, or a threat near an assigned navigation point, rather than certifying every enemy unattackable.
3. `focusOnNavigating()` can explicitly set attack target null while movement has priority.

This is a useful distinction for AICompanion: `Arsenal.BestTarget` returning null means no useful shot among a bounded, urgency-first shortlist from the present muzzle and terrain state. It is not a global proof that enemies are unreachable, nor a reason to treat movement reachability as false. Any bridge between “no shot here” and “walk to a firing position” must be separately represented and verified.

## Advanced Weapon Control: compatibility is more than load order

The previously cited Advanced Weapon Control evidence is now pinned to commit [`b19cd7c47770dcbc9ccdac3ae6eb7535b2b62d56`](https://github.com/DesperatePeter/starsector-advanced-weapon-control/tree/b19cd7c47770dcbc9ccdac3ae6eb7535b2b62d56), rather than an unpinned `master` link. Its source contains separate tag-based weapon-AI and ship-AI surfaces, including `TagBasedAI`, `SpecificAIPluginBase`, and ship modes such as `VentShipAI`, `StayAwayAI`, `AutofireShipAI` and `ChargeShipAI`. The project therefore supplies a concrete compatibility shape: custom weapon policy and custom ship policy can both be configured, but concurrent writers to one weapon-AI surface remain a conflict. [Pinned README](https://github.com/DesperatePeter/starsector-advanced-weapon-control/blob/b19cd7c47770dcbc9ccdac3ae6eb7535b2b62d56/README.md); [`TagBasedAI.kt`](https://github.com/DesperatePeter/starsector-advanced-weapon-control/blob/b19cd7c47770dcbc9ccdac3ae6eb7535b2b62d56/src/main/kotlin/com/dp/advancedgunnerycontrol/weaponais/TagBasedAI.kt).

This is a boundary, not validation of all combinations. The source’s breadth also warns against treating “weapon” as one output: targeting, rate of fire, ammunition/flux policy, projectile class and player-selected tags can have their own control surfaces. AICompanion’s deliberately closed kit avoids that mod-specific configuration space, which makes its source-level pair evaluation tractable but means it cannot claim compatibility with arbitrary modded player items or enemy mechanics.

## Decision matrix and evidence that would change the conclusion

| Candidate pattern | Strongest source support | Countercase / cost | Test needed in AICompanion |
|---|---|---|---|
| One selector produces both feet and weapon decisions every tick | Simple mental model only; not what the inspected AI Tweaks code does. | Cancels bursts and serialises legal independent output. | Compare missed legal shots and target churn against a separate-hand trace. |
| Independent feet and weapons with no commitment | Separate update cadence exists. | AI Tweaks explicitly retains bursts and delays venting around them. | Count output reversals and shots/tools cancelled after admission. |
| Independent channels plus named action commitment | `finishBurstTarget`, `ventTrigger`, and burst wait show distinct continuation rules. | Can preserve a now-harmful action. | Record admission, continuation and cancellation reasons with target/terrain changes. |
| Single safety veto that stops all actions | No direct support in inspected source. | Vent/backoff redirects movement while target/burst state persists. | Test whether dangerous body states actually require arm suspension. |

The report’s scope remains limited to accessible mod code and maintainer statements. There is no claim that these policies are native Starsector behaviour, optimal play, or a measured player preference. The relevant no-consensus result is narrower: the inspected implementations support an explicit ownership-and-commitment layer when outputs have different cadence and failure modes; they do not settle how broad AICompanion’s independent-hand permission should be.
