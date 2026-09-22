# Combat knowledge fixtures — what the companion believes a weapon does, and where it learned it

Everything the planner prices rests on two beliefs: how a projectile flies, and what a shot achieved when it landed. This folder holds the fixtures that hold both against the game itself rather than against the mod's own arithmetic. A flight law is checked by flying Terraria's own `Projectile.VanillaAI` beside the fitted law and comparing tick for tick; a learned outcome is checked by feeding the learner a stream and reading what it believes afterwards; and the slot rules that decide which items ever reach either are checked against a written table.

```
Knowledge/
├─ CLAUDE.md
├─ VerifyHandedGear.cs      which real items fit which of the four slots, and the pick power the slot reports
├─ VerifyFlightLaws.cs      the fitted law against the engine: delayed gravity, bounce, homing, pass-through, children
├─ VerifyVolleyLearning.cs  a spread or a burst as one use, arcs from the player's own flights, name-keyed save
├─ VerifyArcLearning.cs     the two priors against native AI, and hits per shot before and after learning
└─ VerifyWeaponLearning.cs  weapon, target, stand and aim valued by what the companion's own shots achieved
```

`VerifyHandedGear` is slot admission rather than learning, and it sits among learner files without an argument for being here. Nothing depends on the placement; it is named so the next reader does not spend time looking for the reason.

## The two things a fixture here can and cannot reach

**The global projectile hooks do not run headless**, so no real outcome is ever delivered to the learner by the game. Every learner row therefore feeds a synthetic outcome stream from a seeded generator, and every arsenal row puts a real item in the gear and asks the real arsenal with the learner's state either taught from that stream or planted directly. A row that needed a real hook to fire would be a row that cannot exist here, and the ones in that class are named as unverified where they sit.

**What the engine does supply is the flight itself.** `VerifyFlightLaws` and `VerifyArcLearning` spawn a real projectile, advance it with the game's own `VanillaAI` through the recorder's step, and hold the fitted law against what the game did — phase, gravity, drag, terminal velocity and default hitbox. That is the half of the belief that can be made true rather than merely consistent, and it is why an arc row is worth more than a learner row about the same weapon.

## The session's load-bearing row lives here

`the target hold survives ordinary motion and breaks on a change that should change the choice` is `VerifyWeaponLearning.TheTargetHoldSurvivesOrdinaryMotion`, and on 21 September 2026 it was the row that exposed the aim defect the whole combat lane turned on. A committed fight was released on the very next tick whenever its target moved faster than about 0.4 px/tick — roughly a quarter of a zombie's walking pace — because every use in a segment carried the aim the segment's *opening* tick solved, and the re-pricing re-flew those stale aims and confirmed nothing. The hold went from **0 of 3 ticks of ordinary motion to 3 of 3** once `ReevaluateAttackPlan` began re-solving each use at its own fire time (`da5e416`); the mechanism itself is documented where it lives, in `../../../../Companion/Brain/Activities/Combat/Planning/CLAUDE.md`.

**The row is about the plan's commitment and not about the learner, despite living in the learner's file.** `Reranked` reads `combat.Planner.Committed?.Id` before and after each change, so what it holds is: ordinary motion of both the target and the orb keeps the hold for 3 of 3 ticks, while a hostile appearing, a hostile leaving, the learner revising a belief, and a hundred-pixel jump each re-rank at once. The last of those four is the scene most likely to exercise `hostile-moved-off-its-track`, the release reason `a19e1ca` added — but the row asserts only that the commitment ended, never which of the checks ended it, so treat that as the likely path rather than a proven one. It prints both reasons beside the verdict (`LastInvalidation / Reprice.LastRefusal`), because a row saying "kept 0 of 3" and stopping names a symptom and sends the reader back to rebuild the scene.

**This row is also the nearest thing the tree has to gate G06, and the gap between the two is one assertion.** G06 asks for seeded jump, dash and teleport sequences with no stale aim on a new motion episode, no whole-course identity churn from ordinary movement, and a legal useful attack still occurring; it has no fixture row anywhere and no case name carries its tag. Three of its four clauses need exactly this scene, and the one that costs nothing is the jump row asserting **which** check released the hold — `Require(combat.Planner.LastInvalidation == "hostile-moved-off-its-track", …)` beside the existing `Require(jumping, …)`, with the mutation being `CommitAttackPlan`'s track test removed, which is the plan's own `retain teleport trajectory` column. The other three want a dash and a teleport added to the same ladder, and the course's identity read through `Diagnostics/ReadLiveCourseForAudit` rather than the planner's. The fourth clause, `forecast with zero unknown variance must fail`, cannot be planted at all: nothing on the live path consumes a forecast variance, and the class that held per-generation residuals had no production caller and was deleted on 22 September 2026 (`Companion/Brain/Infrastructure/Observation/CLAUDE.md` carries its properties in planned work). Recorded here rather than built because this folder was outside the owning lane's files; the assertion is one line and the scene already exists.

Its target walks *before* the plan is searched rather than starting to walk on the first motion tick, deliberately: a standstill-to-walk flip moves the intercept sixty pixels and the re-flown aims rightly miss, which is a behaviour change rather than the ordinary drift the row is about.

**Three probes are kept, environment-gated, because the next person diagnosing this needs them and each one killed a plausible wrong cause:**

```
AIC_TRACE_USEAIM   prints every use's solved aim beside its scheduled fire tick — three shots sixty
                   ticks apart all reading one aim is what proved the defect (d6b2119)
AIC_TRACE_HOLD     prints the motion forecast's predicted impact per tick — identical to the pixel
                   across four ticks of real motion, which killed the "non-invariant forecast"
                   hypothesis the card was built on (c03e236)
AIC_TRACE_REPRICE  prints why a re-price came back empty — simHits=0 on every refusal, including
                   ones taken immediately after the plan was established (c03e236)
```

They print rather than assert, deliberately: whether the aim tick and the fire tick may differ at all was the open question, and a row asserting either answer would have been asserting the fix before it was chosen.

## Traps

**The target hold row is the one thing in this file that is not about learning at all.** It sits here because the learner's own reference file is where it was written, and it drives `combat.Planner`. A reader looking for the plan commitment's rows will look in `../Planning/` and not find it.

**Everything goes through the `live` alias, or it proves nothing.** This project compiles its own copy of the Aiming folder beside the mod's, so a fixture that watches calibration samples into one copy and solves from the other passes against two learners that never met. `VerifyArcLearning` is explicit about this and the rest inherit it.

**A measurement distance is chosen against the straight guess, not for tidiness.** The arrow is measured at thirty tiles rather than twenty because under twenty its drop is inside a zombie's half-height and a believed-straight arc would hit anyway, which makes the before-and-after arms indistinguishable; the knife is measured at twenty. Both figures, and the calibration-shot counts, are ledger measures rather than pass lines.

**A headless player holds no damage modifiers.** The game rebuilds them every tick and nothing here does, so any row that composes item numbers stands them up itself; without that the composition is zero and every share divides by nothing.

**A row that asserts against a threshold can pass on overlapping noise.** The hands row in `VerifyWeaponLearning` compared a positive and a negative aim coefficient against thresholds that overlapped across the bow's own aim noise and passed both arms at the same angle. The noise is set to nothing there now, and the zombie floats far enough away that the widest offset genuinely misses its box.

## Flags

```
(default suite)          every file here runs as named cases in VerifyEngineMotion's table
--weapon-learning        the whole learner file: aim, debuff pairs, attribution, danger, cost
--weapon-outcome-credit  who a shot's outcome is charged to, including the target hold
--flight-laws            the seven law rows against the engine
--volley-learning        spread and burst grouping, player-flight arcs, name-keyed save
--knockback              lives one folder up; the push prior is a weapon belief but its rows
                         are about stands and charges, so they sit with the stance's fixtures
```

Always `DYLD_LIBRARY_PATH` at tModLoader's `Libraries/Native/OSX`. A subset flag reaches its fixture before the default suite's own setup has run, so a fixture that touches `Main` must set Terraria's save path itself or the static constructor throws on a null path.

## Current state — 21 September 2026

Every case in this folder passes on the last fully clean whole-suite run, `Tools/Ledger/runs/4abf171-20260921-212441.jsonl` — 208 rows, 158 pass, nothing red and nothing skipped. The seven arc-learning figures file as measures rather than pass lines, as they always have.

## Planned work

`VerifyWeaponLearning.cs` is 925 lines against a folder median of about 370, and it divides cleanly along a line the file already draws: the learner rows that feed synthetic streams, and the six outcome-credit rows that `--weapon-outcome-credit` runs as ledger cases of their own. That is a code split and nothing under this tree edits code, so it is recorded rather than made. A later session doing it must move both halves' `RunOneRow.Case` registrations in `Tools/EngineReplay/Program.cs` and the seven entries in `Tools/EngineReplay/Movement/VerifyEngineMotion.cs`'s default-case table, keeping every case *name* byte-identical, because a renamed case is a new case to the ledger and loses its whole history against the baseline.

`VerifyFlightLaws.cs` at 587 lines is measured as a split candidate and is **not** one: seven law rows sharing one reset and one native-flight driver, where the driver's only callers are those seven. Recorded as a considered no.

Two contracts stay unverified here and neither is reachable headlessly: a modded item with its own `channel` plus overridden `ModItem.Shoot`, and a homing or steered modded projectile. Both wait on the play protocol.

## Cross-folder

`../../../../Companion/Brain/Infrastructure/WeaponKnowledge/` is what every row here holds — the laws, the learner and the simulator. `../Simulation/` takes the beliefs this folder establishes and checks what a simulated use puts into the world. `../Planning/` prices plans out of both. `../CLAUDE.md` owns the default gear pair every fixture's companion is created with.
