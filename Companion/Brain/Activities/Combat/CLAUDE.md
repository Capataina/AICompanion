# Combat — one stance that alone fires

```
Combat/
├─ CLAUDE.md
├─ FightEnemies.cs     the one Combat activity: search, commit, FireFrom, stall
└─ Planning/           stands, beam, vector, commitment — see that folder's guide
```

One activity fights, and only while it runs do the hands fire. `FightEnemies` offers a committed attack plan — stand, weapons, targets and aims chosen together — and `Brain.Engage` fires only while this is current; every other job leaves the hands quiet and the tick reads `not-fighting`. There is no guard side and no hunt side: protection is the harm-prevention term on the plan's vector, and pursuit is the travel the plan priced. Avoiding a hit is movement's evade layer on top of the stance's flight; the stance carries no dodge of its own.

`ResolveFiringOpportunity` is gone. The live surface is the planner in `Planning/`. The arsenal that used to pick a pair from here is gone the same way: `CompanionCombat` holds the gear enumeration, the forecast, the planner and the hands.

## How a tick prepares

`Prepare` is the only world read. It captures a value, a plan and whose bodies the plan was admitted against, so later `Score` cannot retarget.

```
no Combat preference ──► NoOpportunity, combat-disabled
no weapon in either slot ──► NoOpportunity, no-weapon
player dead ──► NoOpportunity, no-weapon-side
threats ──► chaseable, inside the activity allowance, not deferred
              │
              ├─ a committed plan still valid ──► re-evaluate, offer it
              ├─ a prepared plan still valid ──► same, commit if already running
              └─ search ──► offer the front's argmax, commit if running
```

A committed plan is kept by membership, never by a bonus: terrain revision, knowledge revision, the admitted bodies' generations, the highest urgency it saw, the intent region it priced company against. Any one failing re-searches. The stall clock runs only while this activity is current, from the current segment's start rather than the search — ordained travel is the plan working — and a window of idle hands with no planned use fired and no planned hit landed defers every body the plan targeted. Changing target is not progress.

The funnel names every threat the preparation met, earliest stage first: not chaseable, outside the allowance, deferred, unplannable, outvalued, or offered. The offered plan's primary target is the use whose body had the highest sensed danger when the search named it, not the greedy opener of the first segment: a two-segment plan that farms a nearby zombie and then stands over the threat on the player is pursuing the latter.

The value offered to the chooser is the plan's weighted outcome mapped into the utility band and lifted by the player's danger, so a threat on him can take the body from a vein. The companion's own danger is not a second skin on the score — it is `CompanionHarmTaken` inside the vector.

`Execute` returns `FireFrom` at the current segment's stand, aimed at the primary target. A null resolution from the positioner invalidates the plan on the next rescore. The hands (`FireDueUse`) fire the plan's due use when the body is at the stand, and the best use from here at the plan's targets while travelling.

## What it deliberately does not do

It does not fire during mining, lighting, collection or company. It does not pick a weapon independently of the stand. It does not start recovery flight. It does not treat an unfinished reach flood as a proven absence: a stand the flood has not claimed is undecided, and the offer is unresolved at value zero until the flood answers.

## Relations

The planner in `Planning/` is this folder's decision. `../../Infrastructure/Interactions/Firing/` is the hand: the item-backed weapon, the forecast, the due use. `../../Infrastructure/WeaponKnowledge/` is what a use does, learned from shots, simulated for the search. `../../Infrastructure/Position/` returns verdicts for proposed stands and resolves `FireFrom`. The threat sense's intervention estimate is the plan's predicted kill ticks, not a second removal arithmetic.

## Traps

**A gear stamp that is not a contract releases the plan as gear-changed before a stall can fire.** `System.HashCode` is not stable across calls; stack changing as a throwing knife is simulated is not a loadout change. The live stamp is a deterministic mixer of type and prefix. The first populate of an empty hand is not a loadout change either — `CompanionCombat.Refresh` releases only when a committed plan's weapon types actually moved.

**The stall clock starts at the segment, not the search.** A BestRange stand a flight away still looks idle at search-plus-window. Fixtures that jump that far without waiting for arrival will read a live plan and a leftover invalidation, not a stall.

**Close BestRange samples are the close peak.** Reach is capped, so an even grid's first sample sits hundreds of pixels out. A spread weapon's peak and a goons-then-close stand live inside that. Skipping the 48 px and 96 px extras when the even grid missed is how those rows go to one HereAndCompany segment.

## Current state — 18 September 2026

The stance, the seven generators, the beam, the overlay's committed-plan layer, `CheckTheFight`'s performed and churn checks, name-keyed knowledge save/load, and the extra-projectile modifier seam are built. Headless: `sh Tools/verify.sh --case combat` (and `--case "attack planning"` / `--case weapon`) files no red row; C1's cache 99th percentile sits inside one frame. `CombatValueScale` is still 1.0, waiting on a play capture. Mastery pierce and extra-projectile bonuses are planted in the S5 row — there is no live bonuses record yet. Invalidation of a combat target is still the world's global edit counter rather than a spatial box.

## Operating

```
dotnet run --project Tools/EngineReplay -p:UseAppHost=false -- --combat-purpose
dotnet run --project Tools/EngineReplay -p:UseAppHost=false -- --attack-planning
dotnet run --project Tools/EngineReplay -p:UseAppHost=false -- --hunt-progress
dotnet run --project Tools/CombatAudit -p:UseAppHost=false -- --self-test
```

Always `DYLD_LIBRARY_PATH` to tModLoader's native OSX libraries. Never launch Terraria to prove a row.

## Planned work

Tune `CombatValueScale` and the objective-weight constants from a live capture — that is the owner's play, not a headless row. Wire mastery pierce and extra-projectile to a live bonuses record when one exists. Make combat-target invalidation spatial, with a box the flight simulation actually bounds.
