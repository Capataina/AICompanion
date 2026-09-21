# Combat planning — stands, beam, vector, commitment

```
Planning/
├─ CLAUDE.md
├─ ProposeFiringStands.cs    seven generators from what the weapons do, none naming a category
├─ SearchAttackPlans.cs      beam over timed segments, depth three, width four
├─ EvaluateAttackOutcomes.cs the vector and the greedy continuation; prefix remaining-life and debuff threading
├─ WeighCombatObjectives.cs  one weight per objective, shaped from the senses
├─ KeepOnlyUndominated.cs    the front; a dominated plan never survives it
├─ CommitAttackPlan.cs       kept by validity, stalled by idle hands, deferred as a set
├─ ReevaluateAttackPlan.cs   a held plan against this tick's forecast
├─ AttackPlan.cs             segments, uses, validity, the named primary target
├─ CombatOutcome.cs          the eight-term vector
├─ SpendCombatDecisionWork.cs tactical consumers borrow the brain tick's one decision allowance
├─ ExportCombatSnapshot.cs   the audit's replay packet
└─ DescribeAttackPlan.cs     the record's one-line plan
```

This folder is the fight's decision. Generators name stands from simulated uses; the positioner returns verdicts; the search prices sequences on the vector; the commitment keeps the winner while the world it was admitted against still holds. It is pure enough that a fixture plants knowledge and a forecast and gets a plan without a live world. Geometry lives in `SimulateUse`; this folder does not trace a shot.

## How a plan is found

```
threats, weapons, forecast
        │
        ▼
ProposeFiringStands ──► HereAndCompany, BestRange, PierceLines,
                        FloorFlanks, AboveArea, BankShots, SafeRange
        │                 (half-tile dedup; AboveArea retags a drop the
        │                  even grid already named; PierceLines is one
        │                  spine per segmented body plus lines between
        │                  different enemies)
        ▼
AssessStands ──► reach, travel, harm at the stand and along the flight, allowance
        │
        ▼
price each reachable stand: greedy continuation from arrival
        │
        ▼
beam: depart after an early prefix fire, re-propose from that stand,
      thread remaining life and live debuffs into the next piece
        │
        ▼
KeepOnlyUndominated ──► argmax on the sense-driven weights
```

A generator's probe is one simulated use, never a priced plan. Each generator spends a share of the decision's remaining simulations so the search keeps at least half. BestRange flies four distances along the line from the target toward the body, toward the player, and up, plus two bearings, and then two close extras at 96 px and 48 px: reach is capped, so the even grid never asks inside the harm Inverse, and a spread weapon's peak lives there. BestRange does not sample an area weapon: AboveArea owns the drop, and a ranging peak beside the group is a contact throw that a one-segment grenade then beats every timed pair.

A specialised generator — AboveArea, PierceLines, FloorFlanks, BankShots — prices only the weapon it named the stand for. BestRange and HereAndCompany stay unbound, because binding them collapsed the goons-then-close plan onto fighting from here. SafeRange is unbound too: it names a place, not a weapon. An area weapon is priced only at an AboveArea stand (and on the audit's exhaustive grid): throwing it from here or a flank is a side toss that outscores the drop-then-pierce the generators exist to name.

SafeRange emits when a flyer is in the fight: two stands off that body's predicted corridor (perpendicular to its 45-tick lead), capped at ten tiles so a bow's 990 px reach does not steal the beam. A still flyer gets above and either side. It used to wait until missing life plus companion danger reached one, and then step toward the player — which is the Eye's lane — and it used to emit against walkers, which stole P-rows and C1. Harm at the stand is path occupancy for flyers, plus the 160 px contact pocket for walkers (the shotgun-hug P3 prices).

A consumable's remaining throws are the stack in the slot. The evaluator copies that map on entry and skips a weapon at zero, so a second grenade is never priced after the one the player handed. Ammo is free, so a bow is unlimited.

The vector maximises damage-per-second (in encounter-life units), threat removed and player harm prevented, and minimises companion harm, push danger, company gap, time to first damage and mana. Weights rise with the player's danger for prevention and time, fall for company, and answer the companion's missing life and his own danger for harm. A prefix that only rolled life priced a grenade-then-bow as if the burst never marked anything; the beam threads `initialDebuffs` / `endingDebuffs` beside remaining life, in the roll's own frame.

`AttackPlan.PrimaryTarget` is the use whose body had the highest sensed danger when the search named it (`NamedTarget`), not the first segment's first use. A two-segment plan that opens on a nearby zombie and then flies to the threat on the player is pursuing the latter.

## How a plan is kept

`CommitAttackPlan` holds one plan. It stays while the current segment's stand is still reachable (or the body is already there), the allowance still describes it, every targeted body is still alive or was killed by the plan, no *new* hostile above the notice floor has appeared and no known hostile has jumped past the hold slack (a pixel closer is the plan working), the intent region has not doubled the company gap, and the hands have made progress inside the stall window. Progress is a planned use leaving the hand or a planned hit landing on a planned generation. The clock runs from the segment's start: travel the plan ordained is not idleness. A stall defers every body the plan targeted, not only the primary. Suspension renews the window rather than consuming it.

Combat borrows the one `DecisionWorkBudget` opened by the brain tick. It does not create a combat deadline, run an unbounded held-plan re-evaluation, or spend a clock-free from-here search after the shared allowance cuts. A cut keeps a fully priced prefix only when that prefix was completed before the cut; otherwise combat remains unresolved and the course owner can keep a previously accepted legal action or schedule repair. `SpendCombatDecisionWork` names tactical charges on that shared owner; deterministic replay fixtures construct their own explicit operation allowance. The cross-tick sim cache still keys FireTick: a delayed bow after a grenade is not the same flight as firing now. Live HereAndCompany stands already cache at fire-delay 0; moving Eyes miss via the enemy hash. Company parks and routes pay the combined wall-and-enemy heat out to eight tiles. The accompanying walk reads that same combined field among its steps. Combat stands call `ClearanceHeat.NudgeOffTerrain` only when wall clearance is under one tile (a crack). HereAndCompany's region samples walk toward higher **enemy** clearance inside the region (`PreferClearer(..., enemiesOnly: true)`); walking off the floor as well created an air perch that dominated the far shotgun and P3 closed at low life. Weighted is untouched. `ProposalTargets` takes chain representatives only, capped by urgency then distance; PierceLines still groups every sensed segment of that head and flies one farthest-pair spine per chain plus lines between different enemies.

## Relations

`../FightEnemies.cs` is the only activity that asks for a search and the only one whose running tick fires. `../../../Infrastructure/Position/` assesses stands and resolves `FireFrom`. `../../../Infrastructure/WeaponKnowledge/Simulation/` is every probe and every priced use. `../../../Infrastructure/Interactions/Firing/FireDueUse.cs` fires the due use, or the best from here at the plan's targets while travelling.

## Traps

**Travel ticks from a body-rooted flood overestimate a hop past a wall.** The flood's remaining cost is from the body, not from the previous stand. A deeper leg is clamped to four times the straight-line estimate, floored at the straight line, or a grenade-then-pierce never prices a second segment inside the horizon.

**Half-tile dedup keeps one rock; the reason still has to be the drop or the pierce.** BestRange's up-line samples the same air AboveArea drops from, and its body-line samples the same rocks PierceLines proves as a chain. A grenade-then-pierce that reads as a ranging stand is this.

**Prefix remaining life without debuff marks prices the bow as unmarked.** A grenade that applies a mark the bow exploits must thread the marks, or firing at arrival always wins.

**A grenade's use time at the launch nerf is ninety ticks.** The hands are still busy when a short fuse bursts, so a second segment that waits for the explosion has room for one arrow. The P7 row plants the grenade at its item cadence so the pierce has horizon left; live play still carries the nerf until the tree lowers it.

**Damage-per-second punishes a second segment after a contact grenade.** Duration sits in the denominator: a five-tick contact from the drop outscores grenade-then-bow even when the bow would finish both bodies. The drop has to leave them wounded, and the pierce has to kill, or the one-segment throw wins.

**C1 measured alone is not the in-suite number.** A process that only searches the forty-hostile crowd reports ~39 ms cached and misses the frame; the same scene after the P-rows is ~12 ms. `--combat-cost` therefore runs the attack-planning suite, not C1 by itself. Three cached crowd warmups still sit in front of the twelve samples so p99 is not a compiling miss.

**A 0.01 urgency creep is not a new fight.** `new-urgent-hostile` used to dump the plan whenever any living threat exceeded the admitted max. At night that was 32 plans per second of combat.

**An exhausted allowance is not a refusal, and a budget's `Cut` flag is sticky.** `DecisionWorkBudget.Cut` stays set for the life of the allowance once any consumer anywhere has been refused one operation, so a check of the form "did the budget cut" asked late in a tick is really asking "did anything cut, ever, this tick". `ReevaluateAttackPlan` returned null on that flag for one day, `FightEnemies` read null as `uses-stopped-solving`, and the result was that a single cut anywhere on a tick released a committed fight the world had not invalidated and wrote a false reason into the record. The property, which outlives whichever function expresses it: **a bounded computation that ran out reports a third value, and only a completed computation may refuse.** It is the same three-valued rule the reach sense, the light sense and the offer vocabulary already keep, one layer further in, and re-pricing is where it was missing. `Repricing` is the type that carries it — `Priced`, `Refused`, `Unresolved` — and on `Unresolved` the held plan keeps the body, because `Validate` has already checked its stand, targets and admission on that same tick and nothing else about the world has spoken.

**The worth it keeps is the freshest one anything established for it, and that is not the plan's own `Outcome`.** `AttackPlan` is a sealed record and `Commit` runs at one site, so `Committed.Outcome` is written when the search commits the plan and never again; every successful re-price after that prices the plan against the tick's own forecast and, until `f7d6c97`, threw that number away. Falling back to `plan.Outcome` therefore offered the *oldest* thing ever known about the fight rather than the newest, and the gap widened for as long as the fight lasted — an enemy walking behind a wall over several ticks is scored at its open-ground worth on the tick an unrelated consumer cuts the allowance. `FightEnemies` keeps the last successful re-price beside the plan it belongs to and `WorthOf` returns it, keyed by reference identity so no plan can ever be offered at another's price. The general property, which outlives this function: **a fallback value is the freshest thing that was ever true, never the first thing that was true.**

Both halves are bounded rather than trusted, and that is what makes the retention safe rather than stubborn: the stall clock forces a committed plan out within `CombatPlanStallTicks` whatever re-pricing says, a prepared plan dies at its segment's end inside the horizon, and `FireDueUse` re-verifies geometry live at the muzzle, so a retained plan can waste priority and can never fire a wrong shot. A later consumer that reuses this retain-on-cut shape without both of those — an independent check at execution time, and a bounded staleness clock — inherits the staleness without the safety.

## Findings

**Pairing every worm segment exhausted Propose before a stand was priced.** Giant Worm is head+body+tail (ten-odd records). PierceLines used to probe every pair; 699 of 1,156 worm ticks in `2026-09-18_12-37-47-939` were `Unresolved:budget-cut` with nothing priced, so the keep-priced-on-cut path never ran. One spine per chain (farthest pair of members) plus inter-enemy lines is the generator; the search still prices only representatives.

**The opener guarantee fails on a cold simulation cache, which is every live tick, and the fixture that measures it mostly does not test that.** `SearchAttackPlans` prices one opener stand before broad discovery may spend the allowance, which is G04's promise that a crowd cannot delete combat. The `planning cost on a crowd fits a frame` row reports ten of twelve searches producing a plan and had that failure filed against the prelude — `EnsureForecast` forecasting forty hostiles before the opener is reached. **That cause is wrong**, measured on 21 September 2026 by timing each step of the opening on that scene: forecasting all forty, choosing targets, assessing the opener's stand and building the eval targets cost 0.04 to 0.09 ms together, and the entire 12 ms goes inside `PriceLevelOne` for the opener alone — 12 ms on a cold sim cache against 0.4 ms on a warm one.

What makes that worse rather than better is which runs are representative. `CacheSimulatedUses.ClearAtTick` clears only when the tick changes, and the fixture's twelve searches all share one `Senses.Tick`, so the first two fill the cache and the other ten read it. The brain tick advances every frame in play, so **every live search on a crowd starts cold**, and the two that fail are the only two measuring the game's own regime. The row therefore under-reports: it is not "nearly right at ten of twelve", it is "both runs in production's regime failed". It is the same mechanism under `AIC-445`'s 22 ms mean and 814 ms maximum on a four-hostile scene with a boss, because every tick pays the cold price.

Two fixes are open on `AIC-441` and they differ in what they give up. Pricing the opener against the most urgent target rather than all eligible ones makes the guarantee cheap by construction and gives up the opener being the *best* from-here shot, which G04 never promised. Invalidating the sim cache by what actually changed — the key already carries weapon, modifiers, muzzle, aim, fire tick, knowledge and terrain — rather than by the tick keeps the quality and needs proof that a tick boundary is not the only thing keeping stale entries out. Neither is safe to land without a play, because both change which shot a crowded fight opens with.

**Nudging every combat stand under the clearance cap moved P3 and P7.** Heat on the weighted pick already broke P1/P3/P6/P7. Replacing the zero-travel HereAndCompany body with an eight-tile lift did the same: P2's bow took BestRange at 275 px, P3's wounded shotgun stayed at 96 px because the lifted perch dominated the far BestRange then lost to the close one, P5 collapsed to one HereAndCompany segment. Combat's heat read is enemy-only on region samples, crack-nudge on every stand, Weighted unchanged. Company reads walls and enemies together on the walk.

## Current state — 18 September 2026

The retained-course migration replaces combat's private planning clock with the brain tick's shared decision allowance and removes the clock-free fallback that could resume tactical search after a cut. Re-evaluation now spends that same allowance, so a held plan cannot hide an unbounded solve. Precise multi-use knockback successor credit remains unsupported until the simulator receives a supported motion model and actual receipt lineage; the core reports such combinations as uncertain rather than certifying them. Earlier P-rows describe the pre-course tactical beam and need re-running under the new G04/G05/G06/G11/G15 fixtures before they are acceptance evidence.

## Operating

```
dotnet run --project Tools/EngineReplay -p:UseAppHost=false -- --attack-planning
dotnet run --project Tools/CombatAudit -p:UseAppHost=false -- --self-test
```

P-rows plant the named mutation and must go red with it. C1 prints 50th/90th/99th with the cache and without it.

## Planned work

Weight sweeps over a play capture. Search-audit share on the exhaustive front, scored on those captures. Live mastery modifiers instead of the planted S5 seam.

## Cross-folder

Observation owns `IsChainRepresentative`. Movement/FreeSpace owns `ClearanceHeat.NudgeOffTerrain` and MaxTiles 8. Position assesses stands and does not re-rank combat by heat. `../FightEnemies.cs` reclassifies a no-hit search winner as KnownUnusable.
