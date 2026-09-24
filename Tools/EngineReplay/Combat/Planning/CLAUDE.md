# Combat planning fixtures — the plan search, its allowance, and what a course binds out of it

These three files hold the fight's *decision* rather than its hands: which stand a plan names, which weapon it prices there, how many segments it earns, what happens when the tick's allowance runs out underneath it, and which exact shot a retained course is allowed to carry across ticks. The planner they drive is `../../../../Companion/Brain/Activities/Combat/Planning/`, and it is pure enough that these rows plant knowledge and a forecast and get a plan back without a live fight — no enemy AI runs and no projectile is ever advanced.

```
Planning/
├─ CLAUDE.md
├─ VerifyAttackPlanning.cs       the P-rows and C1: generators, beam, segments, stands, front, cost
├─ VerifyRetainedCombatBudget.cs G11 — one allowance shared by tactical work and course repair
├─ VerifyCombatCourseBinding.cs  G04/G11 — a captured use binding and validating exactly
└─ VerifyTheCensusFrontIsCurrent.cs the whole tick: the front describes the hostiles that are there now, at a bounded number of searches
```

## The P-rows are scenes, and each one names a plan shape rather than a number

`VerifyAttackPlanning` is a list of scenes, each built so that exactly one plan shape can win it: company fights from inside the predicted region; range follows the weapon against one lone target; a spread weapon closes at full life and holds range at low life; two in a line are fought from the line, nearer first; goons then boss earns two segments; a floor roller takes the low flank; a grenade then pierce is timed to the explosion; a bank shot plans with a bouncing weapon only. Two more are about the front rather than a scene — a dominated plan never survives it whatever the weights, and near-equal plans that differ by rounding are not different plans — and the rest cover the overlay layer, the planning clock's units, SafeRange stepping off a horizontal flyer, harm at a stand being path occupancy, a hold surviving urgency creep, closeness to a body being a heat and not a wall, and a segmented body counting as one threat for danger.

**Every P-row was proven by planting the mutation it exists to catch and watching that row go red.** A row that has never been shown red is a row nobody knows is connected to anything.

## The crowd row has had three diagnoses, and the third is that the instrument was the defect

`attack planning on a forty-hostile crowd is measured unbounded and under the tick's own allowance` is the most instructive row in this folder, and it cannot go red any more: every figure it files is a measure, the plan count included, because each depends on how fast the machine ran. It was `planning cost on a crowd fits a frame` until 24 September 2026, and the rename started its ledger history again. Its history, in order, because each step looked convincing:

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

24 Sep   both assertions became measures under the owner's ruling that no fixture goes red because
         something took too long. The deadline one timed the machine outright; the plan count did
         too, because the opener costs about 12 ms cold against a 12 ms allowance on the real
         clock, so a slow machine cuts it before it is priced. The guarantee the count stood for is
         proven deterministically by `a useful combat opener fires while broader search is cut`
```

A fix was built on the second diagnosis — pricing the opener against the most urgent target alone — and it moved the row from ten of twelve to eleven, which reads exactly like a fix working. It was **reverted**, because with the warm-ups in, the unnarrowed opener also reaches twelve, so the narrowing would have given up the opener being the best from-here shot in exchange for nothing. `SearchAttackPlans` carries a comment saying that narrowing is deliberately absent, because it is the first thing the next reader will propose.

**The durable shape, which this tree has now paid for three times: a change measured against an instrument that is itself wrong reads as an improvement in proportion to how wrong the instrument is.**

What survives all of it is a real cost: the opener is about 12 ms on a cold simulation cache, every live search on a crowd starts cold, and that is `AIC-445`. The guarantee and the cost are two questions, and conflating them is what let a wrong cause sit on the card for a day. The guarantee is proven with an operation cap and a clock that never advances, in `VerifyRetainedCombatBudget`; this row files how often it holds at forty hostiles on real time, beside what the searches cost, and a count below twelve is read against the row's own history rather than as a defect.

The row carries `timed` and `perf-tier` in `../../Movement/VerifyEngineMotion.cs`'s `CaseTags`, because it is 138 s of a 232 s suite at `987319df`, almost all of it the three uncached searches of about thirty seconds each, and its subject is cost. An ordinary verify therefore skips it.

## Any timing here carries its regime, or it is not a number

`Program.cs` lifts the millisecond allowances for the whole process before any flag dispatches, so a figure taken without putting the clock back is a brain with no deadline at all. That is a real quantity — the work the brain would like to do — and it is not what a frame costs. C1's own cached percentiles, and the twelve searches under `DecisionWorkBudget(Weights.TotalPlanningMilliseconds)` beside them, are two different arms of the same row and are not comparable to each other. A figure quoted out of here names which arm it came from, or it says nothing. `../CLAUDE.md` owns the worked example and the whole-brain figures that came out of it; this section is only the rule as it applies to C1.

The second half of "the same way" is JIT. A C1-only process measures about 39 ms cached because the planning path has never run; after P1–P8 the same scene is about 12 ms. That is why `--combat-cost` is the whole attack-planning suite rather than C1 alone, and why three untimed cached crowd searches sit in front of the twelve samples.

The uncached arm is a control rather than a distribution and takes two samples, not twelve: one search without the cache measured 29999, 30025 and 30050 ms across three samples — a spread of two tenths of one percent — and twelve of them cost about six minutes of every suite run to establish nothing a pair does not.

## The allowance rows, and the one rule about supplying your own

`VerifyRetainedCombatBudget` is G11's smallest deterministic contract and it deliberately touches no Terraria state, so an exhausted budget cannot hide behind a warmed simulator cache or a wall-clock race. Its first row requires tactical work and course repair to spend one shared `DecisionWorkBudget` and a cut to survive a second consumer. Its second, `a useful combat opener fires while broader search is cut`, is G04's live seam: an already-useful bow shot is priced before broad stand generation, and with one extra broad operation admitted the broad search must cut while combat still commits the opener and hands firing that same budget. Its third requires a nonzero attacking plan to export a nonzero target allocation before course capture, so combat cannot become a known-usable, zero-effect opportunity.

**A row that supplies its own allowance is asserting something about that allowance, so it takes over rather than borrows.** `CombatFixture.BeginDecision` once borrowed an ambient allowance when one was active, which silently discarded the explicit budget its caller passed and made the cut that the opener row exists to observe stop happening. A supplied budget wins now; only a row with no budget of its own borrows, which is what still keeps a nested call from minting a second deadline.

## The course binding is exact, never a lookalike

**`VerifyTheCensusFrontIsCurrent` is the only file here that drives `Brain.Tick`**, because its subject is what the *census* receives rather than what the search computes, and that is only observable at the far end of a whole tick. It asks one thing: a hostile that arrives after the last search is still minted a use. The scene is two hostiles in reach with two drops beside a standing player, so collecting wins the body and combat holds a *prepared* plan it never commits — which is the state the play of 0.38.13 sat in from tick 1,816, with `offered plan=237` unchanged for five hundred ticks while `combat-plan` events, the commitment stream, had stopped.

**Where the new hostile is placed is the whole of whether the row proves anything, and two geometries came back green before the third worked.** A hostile that arrives into the line of fire invalidates the held offer by itself — the re-price finds its uses no longer hit the plan's target, the offer is dropped and a fresh search runs — so the defect is invisible there. It only bites on an arrival the offer *survives*, which the row arranges by placing the newcomer twenty-six tiles off, away from everything. On the parent tree the front then stays at plan 1 for sixty ticks, publishing uses for two of three hostiles present with the third absent.

**Its second row is the cost half, and it exists because making the front current turned out to be expensive.** `a hostile flickering in and out of the set cannot buy a search a tick` takes a hostile out of the world and puts it back on alternate ticks, which is the cheapest thing that changes the admissible set without changing anything else about it, and counts searches over sixty ticks against a quiet sixty first — the quiet control is a premise rather than decoration, because "few searches" says nothing until the scene is shown to force none when nothing moves. Measured 22 September 2026: **six searches over sixty churning ticks against zero over sixty quiet ones**, with 27 of the sixty publishing a cut front, where the gate's interval set to zero gives sixty of sixty. It also requires at least one cut front, because the rate limit is only honest while the offer it holds back is labelled as unfinished, and a limit that published a stale front as a current one would satisfy the count and defeat the point.

Two assertions are deliberately *not* made in the first row, and the reason is the same for both. The row does not require uses published to equal hostiles present: combat publishes priced shots, and a hostile no plan solves against — out of range, no line, the budget spent — legitimately has none, so an equality would be a row about the search rather than about the census. And it does not require the front to be rebuilt every tick: it bounds the searches after the change at two over sixty ticks, because a gate that re-searched on every tick would satisfy freshness by paying for it continuously. Measured after the fix: one search on the arrival, one on the departure.

**`VerifyCombatCourseBinding` gained a second row on 22 September 2026, and it is the one in the default table.** `binding a fight claims the front rather than its first shot` builds three uses against one 30-life target from one stand, firing 0, 30 and 80 ticks apart and landing ten ticks after each, and requires the bound effect to claim 30 damage at a damage-weighted mean tick of 38 over an interval of 10 to 90. Every wrong answer is a different number by construction — 12 at tick 10 is the first shot alone, which is what the binder claimed until that day; 36 is the front uncapped by what is there to kill; 90 is a last-impact nominal; and 46.67 is the unweighted mean of the three impacts, which is what a reader gets by forgetting that the third shot is a partial hit. It was proven red by planting the single-use claim back and watching it report `claims 12 damage where the front takes the target's whole 30 life`. It reads no live Terraria — it builds a fact snapshot and binds it — which is why it is safe anywhere in the default case table, and it is there rather than only behind `--retained-course-combat` because the pricing it guards is what a whole-suite run would otherwise never exercise. `../../../../Companion/Brain/Activities/Combat/CLAUDE.md` carries the world-run measurement that named the defect and the residual it does not fix.

**A third row joined it later the same day**, `a bound fight is occupied and declared for its whole length`, and it holds two halves of one claim on the same scene because they are two halves of one claim. The **occupancy**: the step's `useTicks` and both its `ResourcePhase`s must reach the front's last impact, not end one use after arrival, because a step declared free while the fight it carries is still being fought lets the order search book a second step inside the fight's own window and charges one use's companionship gap against a whole fight's credit — reverting `useTicks` to the single use reddens it with "the step declares 20 ticks of use from an arrival at 0, ending at 20, while the effect it carries runs to 90". The **manifest**: every use the effect was computed from must appear in `binding.Dependencies.Reads`, asserted by key rather than by count, because a count passes against a binder that read three arbitrary facts; deserialising the front out of the raw snapshot instead of through the tracked reader reddens it naming `use:1` and `use:2` as absent, which is what production did until that day and what made a change confined to the second or third shot invisible to `DependencyManifest.Changed`.

`VerifyCombatCourseBinding` also holds that a captured combat use binds and validates without reading live Terraria, and `FightEnemies.ActivateCourseBinding` finds the accepted use again by its id inside the exact plan it belongs to. It never recovers a committed plan by target similarity. The identity a course binds is the shot — target slot and generation, weapon slot, stand rounded to whole pixels, and the index within that stand's sequence — and the reason it is not the search's plan number is `68f07bd`, recorded in `../../../../Companion/Brain/Activities/Combat/CLAUDE.md`.

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

**Every row behind those flags is also a default case, and the suite's reach check keeps it so.** Until 24 September 2026 `VerifyCombatCourseBinding.CapturedUseBindsWithoutReadingLiveTerraria`, `PlannedUseCarriesSimulatorTargetDamage` and eight attack-planning rows (the planning clock, SafeRange, harm at a stand, the hold under creep, closeness as heat, company in clear air, HereAndCompany off a body, the segmented body) ran only under their flags, and the flags wrapped each in a lambda that threw away the `int` the row returns. The captured-use row had rotted there: it wrote its use identity as the pre-`UseId` literal `plan:7/segment:0/use:0`, so `FightAhead`'s front, which reads uses by the target's `npc:<slot>.<generation>/` prefix, found none and claimed 0 damage at tick 0. It now mints the identity through `CombatCourseFacts.UseId` and passes. `every fixture entry is reachable from the default suite or exempt by name with its reason` (`../../VerifyEveryFixtureIsReachable.cs`) is what reds if a row here is added behind a flag alone again.

## Current state — 24 September 2026

Every case in this folder passes, and none of them asserts on wall-clock time. The crowd row became green for the first time in its life after `185a567` (twelve of twelve producing a plan with the brain untouched, and a production diff of fourteen lines of comment), and on 24 September 2026 it stopped having a verdict at all: its cached, uncached and under-allowance searches and its plan count are measures filed through `../../EmitTimingMeasures.cs`, under the new name above.

## Planned work

`VerifyAttackPlanning.cs` is 1,532 lines, about nine times this suite's median file and roughly four times what one reading holds, and it is the largest split candidate in the combat tree. The boundary it would follow is already visible in the file: the eight scene rows that name a plan shape, the front and weights rows that touch no scene at all, and the cost rows whose whole subject is the harness's own regime. It is code, and nothing under this tree edits code, so this is a plan rather than an edit. A later session must move the nineteen `RunOneRow.Case` registrations in `Tools/EngineReplay/Program.cs`'s `AttackPlanning()` and the eleven entries in `Tools/EngineReplay/Movement/VerifyEngineMotion.cs`'s default-case table, keep every case *name* byte-identical so the ledger's baseline still matches, and carry each name's `CaseTags` entry with it, since a tag keyed on a name the table no longer holds throws at the first lookup.

## Cross-folder

`../../../../Companion/Brain/Activities/Combat/Planning/` is what every row here drives, and its guide owns the generators, the beam, the vector and the commitment. `../Knowledge/` establishes the beliefs a plan is priced from; `../Simulation/` holds what a priced use puts into the world. `../CLAUDE.md` owns the shared gear and `CombatFixture`. `../../CLAUDE.md` owns the reset, the `live` alias and the rule that no wall clock decides a verdict.
