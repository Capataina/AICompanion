# Combat planning fixtures — the plan search, its allowance, and what a course binds out of it

These three files hold the fight's *decision* rather than its hands: which stand a plan names, which weapon it prices there, how many segments it earns, what happens when the tick's allowance runs out underneath it, and which exact shot a retained course is allowed to carry across ticks. The planner they drive is `../../../../Companion/Brain/Activities/Combat/Planning/`, and it is pure enough that these rows plant knowledge and a forecast and get a plan back without a live fight — no enemy AI runs and no projectile is ever advanced.

```
Planning/
├─ CLAUDE.md
├─ VerifyAttackPlanning.cs       the P-rows and C1: generators, beam, segments, stands, front, cost
├─ VerifyRetainedCombatBudget.cs G11 — one allowance shared by tactical work and course repair
└─ VerifyCombatCourseBinding.cs  G04/G11 — a captured use binding and validating exactly
```

## The P-rows are scenes, and each one names a plan shape rather than a number

`VerifyAttackPlanning` is a list of scenes, each built so that exactly one plan shape can win it: company fights from inside the predicted region; range follows the weapon against one lone target; a spread weapon closes at full life and holds range at low life; two in a line are fought from the line, nearer first; goons then boss earns two segments; a floor roller takes the low flank; a grenade then pierce is timed to the explosion; a bank shot plans with a bouncing weapon only. Two more are about the front rather than a scene — a dominated plan never survives it whatever the weights, and near-equal plans that differ by rounding are not different plans — and the rest cover the overlay layer, the planning clock's units, SafeRange stepping off a horizontal flyer, harm at a stand being path occupancy, a hold surviving urgency creep, closeness to a body being a heat and not a wall, and a segmented body counting as one threat for danger.

**Every P-row was proven by planting the mutation it exists to catch and watching that row go red.** A row that has never been shown red is a row nobody knows is connected to anything.

## The crowd row has had three diagnoses, and the third is that the instrument was the defect

`planning cost on a crowd fits a frame` is the most instructive row in this folder and the one most likely to mislead a reader who finds it red. Its history, in order, because each step looked convincing:

```
e2bf53d  the row had never passed — twenty-one recorded runs, no pass — and it was measuring a
         question production never asks. FixtureBudget() is (∞, long.MaxValue) with a clock that
         never advances, so it required an *unbounded* forty-hostile search to fit a 16.67 ms frame.
         In the game Brain.Tick always has an allowance open and the search borrows it.
         → the unbounded percentiles became measures; two new assertions replaced the pass line

ec62a61  of the two assertions, "a cut search still returns a usable plan" failed 2 of 12. Filed
         against the prelude (EnsureForecast on forty hostiles). Timing each step refuted that —
         the prelude costs 0.04 to 0.09 ms and the whole 12 ms goes inside PriceLevelOne for the
         opener alone, 12 ms cold against 0.4 ms warm. New diagnosis: the row's twelve searches
         share one Senses.Tick, CacheSimulatedUses.ClearAtTick clears only on a tick change, so
         the first two fill the cache and the other ten read it — and the brain tick advances every
         frame in play, so the two failures were the only two in production's regime

185a567  wrong as well. Clearing the cache before *every* measured search, so all twelve are cold
         as production is, and putting two untimed searches in front of them, returns twelve of
         twelve with the brain untouched. The two failures were the loop's first searches paying
         one-time costs belonging to neither production nor the deadline
```

A fix was built on the second diagnosis — pricing the opener against the most urgent target alone — and it moved the row from ten of twelve to eleven, which reads exactly like a fix working. It was **reverted**, because with the warm-ups in, the unnarrowed opener also reaches twelve, so the narrowing would have given up the opener being the best from-here shot in exchange for nothing. `SearchAttackPlans` carries a comment saying that narrowing is deliberately absent, because it is the first thing the next reader will propose.

**The durable shape, which this tree has now paid for three times: a change measured against an instrument that is itself wrong reads as an improvement in proportion to how wrong the instrument is.**

What survives all of it is a real cost that nothing here asserts: the opener is about 12 ms on a cold simulation cache, every live search on a crowd starts cold, and that is `AIC-445`. The row is about the *guarantee* and not about the cost, and conflating the two is what let a wrong cause sit on the card for a day.

## Any timing here carries its regime, or it is not a number

`Program.cs` lifts the millisecond allowances for the whole process before any flag dispatches, so a figure taken without putting the clock back is a brain with no deadline at all. That is a real quantity — the work the brain would like to do — and it is not what a frame costs. C1's own cached percentiles, and the twelve searches under `DecisionWorkBudget(Weights.TotalPlanningMilliseconds)` beside them, are two different arms of the same row and are not comparable to each other. A figure quoted out of here names which arm it came from, or it says nothing. `../CLAUDE.md` owns the worked example and the whole-brain figures that came out of it; this section is only the rule as it applies to C1.

The second half of "the same way" is JIT. A C1-only process measures about 39 ms cached because the planning path has never run; after P1–P8 the same scene is about 12 ms. That is why `--combat-cost` is the whole attack-planning suite rather than C1 alone, and why three untimed cached crowd searches sit in front of the twelve samples.

The uncached arm is a control rather than a distribution and takes two samples, not twelve: one search without the cache measured 29999, 30025 and 30050 ms across three samples — a spread of two tenths of one percent — and twelve of them cost about six minutes of every suite run to establish nothing a pair does not.

## The allowance rows, and the one rule about supplying your own

`VerifyRetainedCombatBudget` is G11's smallest deterministic contract and it deliberately touches no Terraria state, so an exhausted budget cannot hide behind a warmed simulator cache or a wall-clock race. Its first row requires tactical work and course repair to spend one shared `DecisionWorkBudget` and a cut to survive a second consumer. Its second, `a useful combat opener fires while broader search is cut`, is G04's live seam: an already-useful bow shot is priced before broad stand generation, and with one extra broad operation admitted the broad search must cut while combat still commits the opener and hands firing that same budget. Its third requires a nonzero attacking plan to export a nonzero target allocation before course capture, so combat cannot become a known-usable, zero-effect opportunity.

**A row that supplies its own allowance is asserting something about that allowance, so it takes over rather than borrows.** `CombatFixture.BeginDecision` once borrowed an ambient allowance when one was active, which silently discarded the explicit budget its caller passed and made the cut that the opener row exists to observe stop happening. A supplied budget wins now; only a row with no budget of its own borrows, which is what still keeps a nested call from minting a second deadline.

## The course binding is exact, never a lookalike

`VerifyCombatCourseBinding` holds that a captured combat use binds and validates without reading live Terraria, and `FightEnemies.ActivateCourseBinding` finds the accepted use again by its id inside the exact plan it belongs to. It never recovers a committed plan by target similarity. The identity a course binds is the shot — target slot and generation, weapon slot, stand rounded to whole pixels, and the index within that stand's sequence — and the reason it is not the search's plan number is `68f07bd`, recorded in `../../../../Companion/Brain/Activities/Combat/CLAUDE.md`.

## Traps

**A red here that sits late in the file proves nothing about the rows behind it.** This fixture aborts on its first throw, so a mutation planted against a late row must be hoisted to the front before it means anything, and clearing one red routinely reveals the next — which is progress rather than a regression.

**Three untimed warm-ups before the cached arm and two before the allowance arm are load-bearing, not caution.** Removing either turns the row into a measurement of the compiler.

**`FixtureBudget()` is infinite by design and is the wrong instrument for any question about production.** Anything asking what the game does builds a real `DecisionWorkBudget` from `Weights.TotalPlanningMilliseconds`, whose default constructor takes `Stopwatch.GetTimestamp` and its own frequency — which is the budget the tick actually opens.

**A cost assertion is usually a behaviour assertion in disguise.** Twice in one day a row was found measuring an unbounded regime against a frame budget — this one and the lighting one. The risk in a crowd was never that the tick runs long; it is that the search wants three times the time it has and the plan is made on a fraction of it. Assert that, not the milliseconds.

## Flags

```
(default suite)           eleven of VerifyAttackPlanning's nineteen cases, plus the two
                          VerifyRetainedCombatBudget allowance rows
--attack-planning         all nineteen: eighteen P-rows and C1
--combat-cost             the attack-planning suite then C1, because C1 alone is a compiling miss
--retained-course-budget  the shared allowance cutting every consumer, and the opener surviving a cut
--retained-course-combat  the captured use binding, and a planned use carrying simulator damage
```

`VerifyCombatCourseBinding`'s row and `PlannedUseCarriesSimulatorTargetDamage` are reachable **only** through `--retained-course-combat`; neither is in the default-case table, so a whole-suite run does not exercise them.

## Current state — 21 September 2026

Every case in this folder passes. `planning cost on a crowd fits a frame` is green for the first time in its life after `185a567` — twelve of twelve producing a plan with the brain untouched — and its entire production diff that day was fourteen lines of comment.

## Planned work

`VerifyAttackPlanning.cs` is 1,549 lines, about nine times this suite's median file and roughly four times what one reading holds, and it is the largest split candidate in the combat tree. The boundary it would follow is already visible in the file: the eight scene rows that name a plan shape, the front and weights rows that touch no scene at all, and the cost rows whose whole subject is the harness's own regime. It is code, and nothing under this tree edits code, so this is a plan rather than an edit. A later session must move the nineteen `RunOneRow.Case` registrations in `Tools/EngineReplay/Program.cs`'s `AttackPlanning()` and the eleven entries in `Tools/EngineReplay/Movement/VerifyEngineMotion.cs`'s default-case table, keep every case *name* byte-identical so the ledger's baseline still matches, and re-check the abort ordering — the cost rows currently sit where they do because a red on something filed goes last.

## Cross-folder

`../../../../Companion/Brain/Activities/Combat/Planning/` is what every row here drives, and its guide owns the generators, the beam, the vector and the commitment. `../Knowledge/` establishes the beliefs a plan is priced from; `../Simulation/` holds what a priced use puts into the world. `../CLAUDE.md` owns the shared gear and `CombatFixture`. `../../CLAUDE.md` owns the reset, the `live` alias and the rule that no wall clock decides a verdict.
