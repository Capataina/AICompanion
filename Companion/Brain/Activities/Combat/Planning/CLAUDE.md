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
├─ PlanningBudget.cs         simulations and milliseconds, one decision
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
        │                  even grid already named, PierceLines a chain)
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

`PlanningBudget` is the one decision's allowance, in milliseconds compared to TickCount64. A previous conversion stored TimeSpan ticks / 1000, so a configured 4 ms ran as 40. A cut search offers unresolved at value zero; it is not a proven absence. The cross-tick sim cache still keys FireTick: a delayed bow after a grenade is not the same flight as firing now. Live HereAndCompany stands already cache at fire-delay 0; moving Eyes miss via the enemy hash.

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

## Current state — 18 September 2026

The seven generators, the beam, the overlay layer, C1 (cache p99 inside one frame after the P-rows), P1–P8, P9–P12 and the audit's A1–A4 are built. P7 commits an AboveArea grenade then a FloorFlanks bow that starts after arrival. SafeRange stands off the loudest corridor at full life; harm at a stand is path occupancy. The planning clock stores milliseconds; the cross-tick cache ignores FireTick; a hold survives urgency creep inside the slack. Weight constants are file 4's shapes, not yet swept over a play capture. `CombatValueScale` is 1.0. `--combat-cost` is the attack-planning suite, not C1 alone.

## Operating

```
dotnet run --project Tools/EngineReplay -p:UseAppHost=false -- --attack-planning
dotnet run --project Tools/CombatAudit -p:UseAppHost=false -- --self-test
```

P-rows plant the named mutation and must go red with it. C1 prints 50th/90th/99th with the cache and without it.

## Planned work

Weight sweeps over a play capture. Search-audit share on the exhaustive front, scored on those captures. Live mastery modifiers instead of the planted S5 seam.
