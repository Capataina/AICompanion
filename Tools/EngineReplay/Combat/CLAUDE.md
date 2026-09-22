# Combat fixtures — one stance, driven headlessly against the installed game

Everything about fighting is held here, from what a projectile's arc is believed to be up to whether the companion takes the fight at all. There is one combat activity and it is the only one whose running tick fires; the walker's guarding and hunting sides are gone, so a fixture name or comment still dividing them is naming something that no longer exists.

```
Combat/
├─ CLAUDE.md                        this guide: what is true of every combat fixture
├─ CombatFixture.cs                 the shared hand: search unbounded and allow-all, commit, fire once
├─ Knowledge/                       what a weapon is believed to do — arcs, laws, learned outcomes, slots
├─ Simulation/                      what a use is predicted to put into the world, and what leaves the muzzle
├─ Planning/                        the plan search, its allowance, and the use a course binds
├─ Activity/                        the stance competing against the other jobs, through the whole brain
├─ VerifyCombatPurpose.cs           P08's matched scenes: who a fight threatens, and the danger matrix
├─ VerifyCombatActorMatrix.cs       who is threatened follows placement; whether a shot solves follows the pillar
├─ VerifyHuntAdmissibility.cs       a target is admissible only where the planner can stand and shoot
├─ VerifyHuntProgress.cs            the committed plan's stall clock, and what does and does not consume it
├─ VerifyOfferValidity.cs           a FireFrom stand holds the point the plan priced, and refuses what the flood unclaims
├─ VerifyKnockbackAwareness.cs      the push prior against the game's strike, learned, charged, and read at a stand
├─ VerifyThreatAnticipation.cs      harmful nonchaseable hazards, forecast confidence, and the walker's jump envelope
├─ VerifyPersonalDanger.cs          the threat sense reading danger from sealed chambers
├─ VerifyEncounterContext.cs        blood moons, invasions, the Old One's Army and an observed Eye, by depth
├─ VerifySafetyIsALayerOnTheJob.cs  safety bends the job's flight and never takes the body
└─ MeasureCombatCost.cs             the whole brain over 600 ticks with four hostiles and a boss, in both regimes
```

Each subfolder's own guide owns its fixtures. This file owns what is true of every combat fixture rather than of any one of them: the gear every scene starts with, the shared hand, and the traps that bite across all four children.

## The gear every fixture's companion starts with, and why it is that pair

`VerifyCompanionLifecycle.Create` hands the companion a wooden bow and a throwing knife in its weapon slots and a copper pickaxe and axe in its tool slots. That pair is the two items the authored kit read, because the combat scenes were calibrated against their speeds: a reposition priced at the edge of the evaluation window is inside it for the knife and outside it for the bow alone.

Two scenes deliberately hold less. The actor matrix's rows hold the bow alone, because a thrown knife lobbed over the blocking pillar onto a body a few tiles away within the trace — a correct answer, and not the one the matrix varies — and its guard pair's guarded life is sized so the walk's share of access plus removal is a clear margin under the bow's own damage. That rationale was written when the pillar was three tiles, a walking body's height; it is twelve now and the shot arcs from a centre one radius off the floor, so whether the bow-only restriction is still load-bearing has not been re-measured.

**A scene that leaves the gear empty when it returns has changed what the next scene tests**, because a fixture that reuses the player without creating a fresh companion inherits it. A scene needing a particular item in hand sets the gear itself.

## The shared hand, and the one rule about supplying a budget

`CombatFixture` searches a plan the way the activity does but unbounded and allow-all, commits it and fires once, so a row asserting what leaves the muzzle searches a real plan first rather than driving the arsenal's choice directly — the choice is the planner's now. `BeginDecision` gives a fixture entering *below* the tick the ambient decision allowance a live `Brain.Tick` owns, because combat's tactical work borrows `LimitPlanningWork.Current` and throws when nothing is standing.

**A row that supplies its own allowance is asserting something about that allowance, so it takes over rather than borrows.** Borrowing an ambient one silently discarded the explicit budget its caller passed and made the cut that `a useful combat opener fires while broader search is cut` exists to observe stop happening. Only a row with no budget of its own borrows.

## A hostile that cannot reach its victim cannot test a preference for defending it

**Enemy AI does not run headless**, so a hostile spawned clear of its victim stands exactly where it was put for the whole scene, and any row asking the brain to prefer protecting that victim is asking for a preference no honest objective could hold. This is not the familiar "the fixture is quieter than the game" caveat; it inverts the row's own verdict, because a correct consequence forecast looks at a threat that provably never makes contact and prices the harm at zero — which is the right answer to the world the scene actually built. **Such a row cannot go green by any change to the brain**, so a session spent tuning the objective against it is a session spent against an instrument that cannot resolve what it measures.

A scene that wants a defend-the-player preference builds a threat that can genuinely reach him — placed in contact, or hand-walked — and asserts the reachability as a premise before reading any outcome. Two details in doing that cost a run each and are the general rules for every fixture here:

- **Moving an entity by writing `position` teaches the forecast nothing**, because every predictor in this tree extrapolates from `velocity`. A hand-walked hostile writes both, and walks *before* the brain tick so the observation frozen that tick sees it where it is.
- **An overridden stat that made a scene convenient under a scoring chooser can make it unwinnable under a consequence forecast.** A hostile the companion cannot kill inside the 180-tick forecast horizon lands its hit whatever the companion does, so defending is worth nothing and preferring the vein is the objective being right. The tell is a row whose every candidate prices the same harm.

`Activity/CLAUDE.md` carries the measured case this came from, with the numbers. The same caution applies in reverse to every combat scene here that passes: a scene whose hostile never moves is testing the arithmetic of a static tableau, which is usually exactly what was wanted, and is worth saying out loud rather than leaving for the next reader to assume either way.

## The stance's own fixtures, at the top level

`VerifyCombatPurpose` holds P08's matched scenes, and every scene asserts who it threatens before reading an outcome. Its consequence matrix holds one zombie between player and companion and varies one input per scene — player defence, difficulty, endurance, companion defence, or either actor's remaining life — requiring each to move only its own actor's urgency in the direction the game's damage arithmetic moves it. The baseline row recomputes the replaced raw-damage share from public facts and requires equality at zero defence and full health. Its spacing row was rewritten when combat spacing was deleted and now asserts that a wounded companion weighs the same small attack as more dangerous without any safety response taking the body.

`VerifyCombatActorMatrix` runs four scenes — a damageable zombie threatening nobody, the player, the companion or both — twice each on the same floor, once open and once with a tall pillar beside the companion that blocks the line to every enemy without sealing it. Who is threatened must follow placement alone; whether a shot solves must follow the pillar alone, and the offered plan is worth more clear than blocked because the pillar forces a reposition the clear arm does not pay.

**The pillar's height is bounded from both ends and neither bound is arbitrary.** Three tiles was the walking body's own height and sealed a walker's chest-to-chest line; an orb fires from a centre one radius off the floor and the shot arcs, so three dropped the value without ever making an enemy unshootable from here. Twenty-four killed the arc but pushed the detour past the evaluator's 180-tick horizon, so both blocked arms priced it at exactly zero and any positive clear value beat zero — honest and vacuous. Twelve puts the waits inside the horizon, so the two arms compare two priced fights. It is also deliberately short of the world margin: a column run to row 0 would seal the detour too, and the verdict would become an absence rather than the priced reposition both blocked arms are about.

`VerifyHuntAdmissibility` holds the owner's rule — can I hit this enemy, if not can I move to hit it, and if neither it should not be fought. **The second half is the one that matters**: applying the from-here shot test to every target is the obvious change and rejects every enemy the companion would simply have had to fly toward, which is worse than the behaviour it replaces, so a target with no shot from where the companion floats but a reachable place that can see it stays admissible. Its sealed-enemy refusal needs its stand sweep settled first, or it reads `firing-position-undecided` rather than a proven absence.

`VerifyHuntProgress` holds the committed plan's stall clock: the firing phase may go one stall window without a planned use firing or a planned hit landing before the plan stalls and defers every body it targeted. Ordained travel does not consume the window, suspension renews it, and an unattributed "fired" the hands never reported renews nothing. The window is shorter than the horizon on purpose — one as long as the horizon would never bind, because the segment always ends complete first.

`VerifyOfferValidity` holds the FireFrom stand. The positioner no longer scores firing stands, so what remains to prove is the hold: `Resolve` answers the anchor exactly, `PrepareOffer` admits it as settled once the flood claims it, reports undecided while the flood has not, and refuses rock and dead targets alike. **The six rows this file used to carry — shot windows, shortlist budgets, refusal memory, the opportunity resolver — went with the scored path rather than being rewritten against behaviour that no longer exists.**

`VerifyThreatAnticipation` holds harmful nonchaseable hazards, player-death isolation of the two dangers, measured forecast confidence, and the walker's jump envelope in three matched arms that differ only in how high the orb hovers. **The high arm is the one that had no guard**: the first play of the orb read danger at nine tenths with zombies underneath while taking no hit, and an envelope that always answered yes left the whole native suite green. The middle arm sits above the fighter AI's ordinary jump and inside its tallest-step jump, so it fails if the envelope is ever put back to the ordinary hop.

`VerifyPersonalDanger` holds the threat sense reading danger out of sealed chambers correctly. `VerifyEncounterContext` lost its evaluator arm on 22 September 2026 with the family chooser (`AIC-419`) and kept its course arm, which is the one that matters: `TheCourseChargesAnEncounterOnceAndOnlyToOptionalNonCombatWork` holds the pairing row a product would fail and everything else would pass — urgency 0.6 with an equal encounter costing 0.4 once rather than 0.16 — so the rule the deleted arm asserted over a prepared board is still asserted over the term that decides it. It saves and restores every world-event global it sets and runs a fresh real `ThreatSense` and `EncounterSense` per row with the player's depth set by moving the surface line: a blood moon recognised on the surface and absent underground; the Old One's Army and an observed Eye at any depth; invasion rows following the spawner's gate; and the pressure rows writing the spawn cap the game would compute, because the spawner cannot run headless. Every observation in that file is its own engine tick, because the pressure baseline is the admission ceiling.

## Knockback: the arithmetic, its learning, and its price at a stand

`VerifyKnockbackAwareness` holds what the owner's playtest of 15 September 2026 asked for, when the companion kept knocking enemies into the player. Its prior row strikes a zombie through `Player.ApplyDamageToNPC` and requires the prior to predict the velocity the strike left exactly, in three arms chosen so each branch of the game's arithmetic is the only one that can pass it: a heavy hit, a light hit on a high-life zombie already moving the other way — where the resistance lands a second time — and a zombie that resists all knockback. A swing row requires one landed swing to file one push and one damage sample at a factor of one. A learning row feeds half-pushes and requires half the settled push for that pair and the prior for every other, a reversed push to turn the settled push round, and a crit and a heavy hit on an enemy already at the push's speed to file no push sample.

Its acceptance scene stands the player left of a zombie and asks about two mirrored muzzles. The zombie's resistance is set to full there, because a wooden bow at the zombie's own resistance pushes about half as far and every number would sit closer to rounding. The orb's half of the charge is weighed at the muzzle the forecast is asked about, so each muzzle's own shot pushes the zombie away from itself and any difference between the two is the player's. **Mirrored stands are never compared for equality** — the aim sweep can land the mirrored arcs a tick apart, and a tick of timing moves the value more than a tolerance — so the far shot is compared with its own push-free self and the stand row compares one push asked from two sides.

Every pass line was declared before the first run, and each row was proven by planting its mechanism's absence and watching that row go red. No projectile is advanced, so this proves the arithmetic and the decisions it feeds rather than a fight, and it does not exercise the projectile hook path, whose arithmetic is the method the swing row drives.

## Safety is a layer on the job, and one row's contract changed under it

`VerifySafetyIsALayerOnTheJob` (`--safety-layer`; it was `VerifySafetyAftermath` while an escape still released the body after danger, and was renamed once nothing did) holds what the owner ruled on 15 September 2026. Its first scene is the reproduction of the first orb play's freeze: a damaging zombie beside the orb while the player walks forty tiles away, where the activity must never be suspended, the body must never sit still for the session reader's still run while he walks, and it must arrive after he stops. At the commit before the change that scene was red with spacing owning the feet and the activity suspended; it carries the takeover's signature rather than the thirteen-second duration, because enemy AI does not run headless. The firing rows require a companion beside a zombie to keep firing its granted hand while nothing is suspended, and the shot row requires a handful of ticks owned `evade` with the hand still available, the same identity throughout, and no hit.

**The intervening-hostile row asserts nothing about which hostile is fought, and it arrived at that in two steps on 21 September.** The companion starts on the far side of an intervening zombie from a player who has a second zombie on him, and the row's live requirements are that combat is selected, that the raw hitbox never overlaps the intervening zombie's, and that the body travels more than three tiles — well under the distance to that zombie and well over any hover wobble, because standing still also avoids contact and a frozen body reads identically to a body that chose to engage from a stand.

Step one (`572b61e`) deleted the original pass line, which required the body to get *past* the intervening zombie. That line was written for a guard that meant "stand beside the player"; the stance chooses a target and a firing stand instead. It was also unreachable in this harness whatever the brain did — no headless tool simulates projectile damage, so the target never dies and the companion never finishes with it — and a row that can only pass if the companion ignores the nearer threat is asking for the wrong behaviour to prove the right one. At that moment the stance was fighting `npc:31`, the zombie in the way, and that reading was written down as the better behaviour.

Step two (`da5e416`) moved the behaviour again, and **that is why the row names no target at all now**. With each use's aim re-solved at its own fire time, the stance commits to `npc:30`, the zombie standing *on* the player, from tick 3 and holds it for all 480 ticks — never refused, never cut — flying *over* the one in between, which is why contact stays 0 while the horizontal centre gap closes to 2.2 px. The old nearest-target behaviour was combat re-searching after each false release and settling on whatever was closest, so pinning the row to either hostile would have pinned it to whichever defect was current. This project's ruling is that a target is picked by the harm an attack removes rather than by distance, and the row holds only that the stance is fighting rather than ignoring the fight.

**A row that drove the old chooser usually still runs, and reports on nothing.** Four rows this week had a lever or a pass line belonging to a mechanism the course brain replaced — the lifecycle row's empty board, the encounter row's score ledger, the recovery row's stub activity and this one.

## Any timing from these fixtures carries its regime, or it is not a number

`Program.cs` lifts the millisecond allowances for the whole process before any flag dispatches, which is the right default for a suite of behaviour fixtures and is also how a cost instrument comes to time a brain with no deadline at all. `MeasureCombatCost` ran that way for a day — 22 ms mean and 814 ms maximum on a four-hostile-plus-boss scene, quoted against a 16.67 ms frame and filed as the brain halving the frame rate whenever a fight starts. Under the clock the game actually runs, on the same scene and the same 600 ticks:

```
phase           p50      p95      max     mean        against a 16.67 ms frame
decide        1.035   12.752   14.112    4.153
brain total   1.200   13.224   15.231    4.459
```

Nothing is dropping a frame. With the allowances lifted the same scene reads 25.29 mean and 975 max, which is a real quantity — the work the brain would like to do — and **must never be compared to a frame budget**. The instrument prints both regimes, each labelled with what it answers, because a reader who sees only one cannot tell a brain that is slow from a brain that is being cut hard, and those want opposite fixes.

What the correct measurement does expose is behavioural rather than dropped frames: `decide` sits at its 12 ms allowance on 5% of ticks, and combat holds 593 of 600 ticks unbounded against 406 under the clock, with keep-company taking the difference. On a boss scene the companion spends about a third of the fight not fighting because its search ran out of time.

The second half of "the same way" is JIT, and it points the opposite direction: a fixture run alone through its own flag pays compilation for the whole planning path where the same fixture inside the default suite does not. `Planning/CLAUDE.md` carries the numbers for `--combat-cost`.

## Traps

**A subset flag reaches its fixture before the default suite's own setup has run**, so a fixture that touches `Main` must set Terraria's save path itself or the static constructor throws on a null path.

**Every one of these fixtures aborts on its first throw**, so a red names one row and says nothing about the rows behind it, and clearing one routinely reveals the next. A red waiting on somebody's decision is moved to the end of its file so the rows behind it still report; a red that will clear this afternoon stays where it is.

**A mutation run against a row that sits late in an abort-on-first-failure list proves nothing about that row.** Hoist it to the front before planting the defect it is supposed to catch.

**Headless, the game's spawn cap is whatever its initialiser left (five)**, because `NPC.SpawnNPC` never runs. Any full-brain scene keeping reachable non-boss hostiles weighing more than five spawn slots in front of the observer reads an inferred encounter and zeroes every optional activity, with nothing in the scene that looks like an encounter. A scene with that many enemies writes the cap it means, as `VerifyEncounterContext` does.

**A recorded row with any hostile in it writes `NPC.TypeName`**, which reads `Lang._npcNameCache` — all null until localisation loads. A fixture that records combat supplies `LocalizedText.Empty` for exactly the null entries and restores them afterwards.

**Hostile shots go in `Main.projectile[Main.maxProjectiles - 1]` at your peril.** `Main.ActiveProjectiles` iterates only the first `maxProjectiles` entries and the array's last element is the engine's dummy, so a shot placed there exists, reads active, and is never observed.

## Flags

Every file in this tree runs in the default suite as named cases in `../Movement/VerifyEngineMotion.cs`'s table. The subset flags are `--combat-purpose`, `--combat-activity`, `--attack-planning`, `--combat-cost`, `--crowd-cost`, `--safety-layer`, `--offer-validity`, `--hunt-progress`, `--hunt-admissibility`, `--knockback`, `--weapon-learning`, `--weapon-outcome-credit`, `--volley-learning`, `--flight-laws`, `--simulated-uses`, `--attack-outcomes`, `--retained-course-combat` and `--retained-course-budget`; each child guide lists its own. Always `DYLD_LIBRARY_PATH` at tModLoader's `Libraries/Native/OSX`. Never launch Terraria to prove a row.

## Current state — 21 September 2026

Every combat case passes on the last fully clean whole-suite run, `Tools/Ledger/runs/4abf171-20260921-212441.jsonl` — 208 rows, 158 pass, nothing red and nothing skipped. The *first* clean run since `30fef2b` on 17 September was `da5e416` at 17:25 that day; the stretch between the two was the course brain becoming the live decision and thirteen behaviour rows being adjudicated one at a time.

The two combat reds the session started with closed in opposite ways, and the difference is worth keeping. `planning cost on a crowd fits a frame` closed by **measurement** — the instrument was the defect and the brain was never changed, and a fix built against the broken instrument was reverted. `the target hold survives ordinary motion` closed by a **fix** to `ReevaluateAttackPlan`, after a probe killed two earlier hypotheses that had both been recorded as the cause.

## Planned work

The two split candidates in this tree are `Planning/VerifyAttackPlanning.cs` at 1,549 lines and `Knowledge/VerifyWeaponLearning.cs` at 925, and both are recorded at execution granularity in their own folders' guides. Both are code, so they are plans rather than edits.

Three top-level files are large and none of them divides, which is recorded so the measurement is not raised again as a finding. `VerifyCombatPurpose.cs` at 835 lines is one matched-scene matrix whose rows vary a single input each against a shared scene builder; splitting it would put the builder in one file and every caller in another. `VerifyEncounterContext.cs` at 516 lines is one save-and-restore harness around a list of world-event rows and is indivisible for the same reason. `VerifyKnockbackAwareness.cs` at 361 lines is under one reading. Size alone was never the argument.

**One fixture weakness is recorded rather than fixed**, because nothing under this tree edits code: `Activity/VerifyCombatActivity.DistantIdleEnemyKeepsOffTheVein` asserts that a zombie 560 px away takes no combat ticks, without first asserting that the zombie was ever shootable. Its sibling `OnlyCombatFires` does make that premise a row. As written, the distant-enemy row would pass equally against a brain that could not see the zombie at all.

## Cross-folder

`../../../Companion/Brain/Activities/Combat/` is the stance and `Planning/` under it is the decision; `../../../Companion/Brain/Infrastructure/WeaponKnowledge/` is what a weapon does, `.../Interactions/Firing/` is the hand, `.../Position/` assesses stands and resolves FireFrom, and `.../Selection/` owns the course that decides the tick. `../CLAUDE.md` owns the reset, the `live` alias, the verdict boundary and the walker-era translations. `../../Ledger/` owns the row schema and the scoreboard.
