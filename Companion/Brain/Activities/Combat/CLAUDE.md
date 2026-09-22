# Combat — one stance that alone fires

```
Combat/
├─ CLAUDE.md
├─ FightEnemies.cs            the one Combat activity: search, commit, FireFrom, stall
├─ CombatCourseOpportunity.cs the census a course reads: the facts, the use identity, the source and the binder
├─ AcceptedCombatUse.cs       the one course-approved use the firing interaction may consume
└─ Planning/                  stands, beam, vector, commitment — see that folder's guide
```

One activity fights, and only while it runs do the hands fire. `FightEnemies` offers a committed attack plan — stand, weapons, targets and aims chosen together — and `Brain.Engage` fires only while this is current; every other job leaves the hands quiet and the tick reads `not-fighting`. There is no guard side and no hunt side: protection is the harm-prevention term on the plan's vector, and pursuit is the travel the plan priced. Avoiding a hit is movement's evade layer on top of the stance's flight; the stance carries no dodge of its own.

`ResolveFiringOpportunity` is gone. The live surface is the planner in `Planning/`. The arsenal that used to pick a pair from here is gone the same way: `CompanionCombat` holds the gear enumeration, the forecast, the planner and the hands.

Course bindings preserve the requested firing stand separately from the movement model's final body position. Missing timed arrival evidence leaves the binding unresolved; subsequent projected travel uses the captured endpoint. A nominal attack simulated at the requested stand still needs native admission and reevaluation from the actual firing position, because arrival tolerance cannot certify identical shot geometry.

The retained-course seam captures every use in the planner's priced front before combat chooses its current winner. `CombatCourseFacts` serialises target generation, weapon slot and item type, stand, aim, launch, timing and the target-hit amount summed from the simulator trace into immutable decision facts; `CombatOpportunitySource` scans only that census under the shared cursor/budget, and `CombatOpportunityBinder` reads the chosen use, target, weapon and mana facts through its tracked reader. The binding keeps the demonstrated target damage as a nominal effect when its confidence lacks a bound; a use with no target-hit forecast stays unresolved instead of publishing a zero-value attack. `FightEnemies.ActivateCourseBinding` associates a published binding with the exact native `AttackPlan` use. It never finds a lookalike by target.

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

The value offered to the decision is a fight that actually hits. Hits means `DamagePerSecond > 0` and `TimeToFirstDamage` strictly inside the horizon; a priced plan that never lands is `KnownUnusable:no-damage-in-horizon` even if the search named it `Usable`. A hitting plan sits at `WanderFloor + CombatAboveWander` and then climbs with the clamped weighted outcome and the player's danger, so standing still cannot beat a real shot and a threat on him can take the body from a vein. The companion's own danger is not a second skin on the score — it is `CompanionHarmTaken` inside the vector. A segmented body is one eligible threat (`IsChainRepresentative`: the head when it is in the list, else the loudest piece); pierce still sees every segment.

`Execute` returns `FireFrom` at the current segment's stand, aimed at the primary target — **and since `0bb2c8a` on 21 September 2026 that returned request is discarded.** `CoordinateBrainTick` takes the position request from the course's bound step through `ExecuteCourseBinding.RequestFor`, and calls `Execute` only for the hand this activity reserves and the state it keeps. Letting the activity re-choose its own site would rebuild the post-grant second chooser the retained-course plan names for deletion. The paragraph below still describes what `Execute` computes, because the hands and the stall clock read it; what it no longer does is steer the body. A null resolution from the positioner invalidates the plan on the next rescore. The admission query does not dump the fight when that pixel is rock or unclaimed: unclaimed is unresolved, rock falls back to firing from here (`PrepareOffer` sets `fire-from-here` and clears `LastResolveFailed`; `Resolve` still refuses the rock so the body does not fly into it). The hands (`FireDueUse`) fire the plan's due use when the body is at the stand; while travelling they pick the best use from here that actually deals damage (`DamagePerSecond > 0`), even when its weighted value is slightly negative.

## A prepared offer may not outlive the hostiles it was searched against

**The front this stance publishes is the whole of what the course can ever know about a fight** — combat is the one domain whose opportunities are a tactical search rather than a world scan — and it is refreshed on exactly one line, where a fresh `SearchAttackPlans.Search` returns. Every re-pricing path returns above that line, so while a short-circuit holds, the course is being offered the world as the last search found it.

For a **prepared** offer that is now bounded: since 22 September 2026 the offer may only be re-priced while the admissible hostiles are unchanged, so an arrival or a departure forces a search. Without it, a hostile that arrives and happens not to disturb the offer was never priced at all and the course could not weigh it — reproduced on an ordinary floor as a front frozen at plan 1 for sixty ticks publishing two of three hostiles present, and recorded in the play of 0.38.13 as `offered plan=237` unchanged for five hundred ticks while five hostiles spawned and `combat=usable:3` never moved. The set is recorded where a prepared plan is born and nowhere else, and never cleared, because the gate reads it only when a prepared plan stands and a prepared plan cannot stand without having passed that line.

**It only bites on an arrival the offer survives**, which is why this took three geometries to reproduce: a hostile that walks into the line of fire makes the re-price find that its uses no longer hit the plan's target, which drops the offer and searches anyway.

For a **commitment** the front is deliberately unbounded, and the consequence is named rather than discovered: a hostile arriving mid-fight has no priced use until that fight ends. Re-searching under a commitment is opportunistic replacement, which this tree does not build, and closing the coverage gap there would be building it inside combat rather than at `RetainCourse.Consider` where it belongs.

**One thing was considered here and refused.** Making each re-pricing path publish a front of just the plan it re-priced is the honest reading of "the census gets what combat currently offers", and it would remove the stale pricing of the *other* plans in an old front. It was refused because it narrows discovery to a single target for every tick of every held fight — the course would see one hostile where it now sees the search's whole front — and no defect has been reproduced from that staleness, since the binder re-reads the target fact and the travel before it binds anything. The cost is real and unmeasured: a course binding one of those alternatives binds a stand and an aim priced against an older world.

## The stance is what an unfinished decision continues

**`FightEnemies.Continuation` is the one thing any domain offers the brain on a tick whose decision has not settled**, and it is combat's because combat is the only domain whose opportunities are a tactical search it has already run: its next move is standing there in a committed plan, where every other domain's next move is a course step that arrives when the decision settles. It answers the committed plan's current stand and nothing else, and null otherwise, which sends the body back to keeping the player company.

**The property is that a decision in flight may continue a fight the body is already committed to and may never start one**, and it is a rule rather than a cautious reading of one. The first version of this also answered the *offered* plan — the opener the plan's own tick order names, which `G04 retained opener` proves combat produces even under a cut — and a review on 22 September 2026 refused that branch. An offer is a shot nobody took, so continuing it selects combat on a tick for a fight no course ever chose; `Prepare` computes `running` from the selection *before* the decision, so the next tick's preparation would `CommitAndRecord` that plan, and the moment the decision settled on anything else the commitment would be released `activity-exited` — which is the loop `Continuation` exists to close, re-entered from the other end. The branch was also unreachable: instrumented over the whole default table, no scene produced a single unsettled tick in which the opener fired.

Why it had to exist is a defect that lived one layer up and killed this stance forty times a second. The tick selects the decision's activity; a decision in flight used to answer "keep the player company"; selecting that exits combat; `Exit` releases the committed plan with `activity-exited`; the course's accepted use is then absent, so the course is released; releasing it starts a decision. Measured in the play of 0.38.13 as 212 attempts at a median of one tick, 181 `replaced-before-attacking`, 211 plans invalidated `activity-exited`, and twenty shots in a minute. The property worth keeping out of it, because the mechanism will come back wearing a different activity: **a fallback the tick reaches for while the planner is still thinking is not neutral — it is a selection, and selection tears down whatever was running.**

`Tools/EngineReplay/DecisionMaking/VerifyADecisionInFlightKeepsTheFight.cs` is the guard, and the way it forces the state is worth knowing before writing another row like it. The suite lifts every millisecond allowance, so a decision cannot be made to span ticks by a clock; it takes an operation cap, `Brain.PlanningOperationAllowance`. The window is narrow because every activity's `Prepare` spends the same allowance before the decision does — measured 22 September 2026, at a hundred operations combat offers no plan at all and at four thousand every search finishes, so the fixture measures the cap rather than hard-coding one.

## A bound fight is priced as the fight, because it is charged as one

**`CombatOpportunityBinder` predicts the damage the captured front does to its target, not the damage of the next shot, since 22 September 2026.** The order that binds a fire step pays a companionship gap integrated over the whole excursion — out to the reunion at the end of the fight — so crediting it one arrow compares a whole trip's cost against a sixth of its benefit, and the empty course wins whatever the fight is worth. That is an accounting error rather than a calibration: the two sides of one subtraction were measured over different spans.

The world run over the play of 0.38.13 is what named it. A 45-life zombie 363 px away with a six-use front totalling 48 damage priced at `useful 0.0083`, `harm 0.0000`, `gap 0.0203`, total **−0.0120**, against an empty course at exactly zero — held for 513 consecutive ticks with a bow in hand, nothing refused and the search not exhausted. `useful` is `damage / life × exp(−impact / timescale)`: 10 of 45 discounted over 188 ticks at a 57.4-tick scale is 0.0083 to the fourth decimal, so the arithmetic was never in doubt, only what the numerator should be.

Each use is timed by its own place in the plan — travel to the stand, plus how long after the opening shot the plan fires it, plus that shot's flight — because the plan's fire ticks are absolute and its later stands are reached during the fight. The total is capped at the observed life, so an over-killing front claims a kill and no more. The nominal tick is the **damage-weighted mean** impact with the interval carrying the first and the last: a first-tick nominal prices a long fight as instant and a last-tick nominal discounts the opening shot as hard as the closing one. Evidence stays `Nominal`, so this claims no justified bounds, and over-claiming a fight abandoned after one shot is what the effect ledger's receipts and the forecast-error observer are for.

**What this did not fix on its own, measured rather than assumed.** On the same capture, with only this change in, it moved the stepless-with-work stretch from 513 ticks to 509 and turned 14 of 358 priced combat orders positive where almost none had been. Where the fight is near it now wins outright — one window went from −0.0027 to +0.0485 with useful 0.0669 against gap 0.0184. Where the hostile is 800 px away and will not reach anyone for nineteen seconds, the gap still dominates and the companion still declines, which README's own scene says is right. The residual is that **the companionship gap is priced by distance over time alone, so a stationary player and a walking one cost a fight the same** — and README line 51 says they must not, because a fight costs nothing while the player is stopped at a tree. Setting that is a calibration against a play and it is the owner's, not a constant to invent here.

**Together with the positioning work merged at `019909b`, that stretch is gone.** On the merged tree the same play-measures run reports the longest stepless-with-work stretch as **36 ticks**, the stepless share at 6.79% (55 of 810) and the share of ticks publishing no step at 22.9% — and the old 980–1489 window is a *held fight*, `activity: combat` with a step bound on 503 of its 510 ticks under `course-retained`. Neither change alone did that, so neither can be credited with it.

**What the whole capture says about what was being declined, measured 22 September 2026 against README's own fight criteria.** Across 4,583 hostile-ticks: **not one hostile is ever inside the player's intent region**, 27 hostile-ticks have one reaching somebody inside the 180-tick horizon, 94 have the player stopped with a shot solving, and 3,997 — 87% — are hostiles README says to decline, at a median 1,665 px from him. On every stationary-player tick with hostiles observed the nearest is 22 tiles at best and 68 at the median, never inside his region. On the 138 ticks README does want a fight the companion is already fighting on 92 (`course-retained`, a step bound) and has an unsettled decision with nothing admitted on the other 46, with **zero orders refused on all 138** — so no README-wanted fight is priced and beaten anywhere in the capture. The residual above is therefore a real gap in the model and not the cause of anything observed in this play.

**One candidate was tested and refused, and the refusal is the more useful record.** Lifting combat's work-radius admission — `ChainAllowed`, which refuses any hostile outside the shared `AllowsTarget` radius, and which the funnel shows refusing *every* candidate on *every* tick of both stretches — turns lane B's row green at 42 ticks. It does so by halving the row's own denominator, from 1,340 admitting ticks to 651, while the share of ticks publishing no step *rises* from 39% to 53% and the play's own symptom, the post-kill firing share, does not move by a single tick. Combat spends the allowance discovering hostiles it cannot then price. This tree has paid three times for a change measured against an instrument that is itself wrong, and this is a fourth; the radius question is real and stays open rather than being closed by a row it games.

## What it deliberately does not do

It does not fire during mining, lighting, collection or company. It does not pick a weapon independently of the stand. It does not start recovery flight. It does not treat an unfinished reach flood as a proven absence: a stand the flood has not claimed is undecided, and the offer is unresolved at value zero until the flood answers.

## Relations

The planner in `Planning/` is this folder's decision. `../../Infrastructure/Interactions/Firing/` is the hand: the item-backed weapon, the forecast, the due use. `../../Infrastructure/WeaponKnowledge/` is what a use does, learned from shots, simulated for the search. `../../Infrastructure/Position/` returns verdicts for proposed stands and resolves `FireFrom`. The threat sense's intervention estimate is the plan's predicted kill ticks, not a second removal arithmetic.

## Source

`FightEnemies.cs` is the stance. The search, generators and commitment live in `Planning/`. Chain membership is `ThreatRecord.IsChainRepresentative` / `ObserveThreats.MarkChainRepresentatives`. FireFrom admission is `ChooseUsefulPosition.PrepareOffer`. Travelling shots are `FireDueUse.FireBestFromHere`. The offer band constants live in `BehaviourWeights` (`WanderFloor`, `CombatAboveWander`, `CombatValueScale`).

## Traps

**The search can still return `Usable:planned-attack` for a plan that never hits.** `OfferFromPlan` is the gate: no-hit becomes `KnownUnusable`. Reading the search result's eligibility as the offer is how a never-landing plan used to beat company by sitting at score 0 against the wander floor.

**A gear stamp that is not a contract releases the plan as gear-changed before a stall can fire.** `System.HashCode` is not stable across calls; stack changing as a throwing knife is simulated is not a loadout change. The live stamp is a deterministic mixer of type and prefix. The first populate of an empty hand is not a loadout change either — `CompanionCombat.Refresh` releases only when a committed plan's weapon types actually moved.

**The stall clock starts at the segment, not the search.** A BestRange stand a flight away still looks idle at search-plus-window. Fixtures that jump that far without waiting for arrival will read a live plan and a leftover invalidation, not a stall.

**A use is identified by the shot it is, never by the search that found it, and getting that wrong released every course by construction.** `CombatCourseFacts.UseId` built a use's key as `plan:{plan}/segment:{segment}/use:{index}`, and the plan number comes from `combat.NextPlanId++`, which rises on every attack search. Once the course owned the tick, combat re-searched every frame so the course could weigh a shot it would otherwise never see — and every one of those searches minted brand-new keys for the same physical shots, so the tick after a course bound one, `ValidateNextUse` looked the key up, found nothing and answered `accepted-use-not-present`. The loop is what made the symptom look like indecision rather than invalidation: a released course starts a fresh decision, a decision spans ticks by design, and the next tick released it again before it could settle. On the shared-eagerness scene combat took the body for 5 ticks of 30 with the shot found, priced at 3.476 over a front of 21, and `decision=deciding orders=0/0` after thirty ticks.

The identity is target slot and generation, weapon slot, stand rounded to whole pixels, and the index within that stand's sequence — what makes the same shot re-found next tick the same use. Two plans proposing one shot collide on one key deliberately and the freshest capture wins, because they describe one opportunity; a shot that genuinely changed is still caught, because `ValidateNextUse` compares the stand pose and the tool and answers `accepted-use-changed`. The version dropped the plan and segment numbers for the same reason the key did: a stable key with a moving version is the same defect one level down and harder to see, because the fact would still be present and only the dependency manifests would quietly dirty every tick. **And the derivation lives at one site** — `FightEnemies` finding the accepted use inside its plan and `FireDueUse` verifying it before pulling the trigger both call the `UseId(plan, segment, index)` overload, because three copies of an identity derivation is exactly how the capture and the firing path come to disagree about which shot a course accepted.

**Close BestRange samples are the close peak.** Reach is capped, so an even grid's first sample sits hundreds of pixels out. A spread weapon's peak and a goons-then-close stand live inside that. Skipping the 48 px and 96 px extras when the even grid missed is how those rows go to one HereAndCompany segment.

## Findings

**A hitting fight that sat at score 0 lost to company.** Capture `2026-09-18_12-37-47-939` (4d44794): company ~73.9%, combat ~12.4%, 984 of 1,383 planned-attack-then-company ticks had raw 0 because `Clamp(weighted, 0, 1)` mapped a never-in-horizon plan (typical weighted −0.18) onto the floor. The offer now refuses that plan, and a hitting plan starts above `WanderFloor`. Travelling hands used to start best at 0 and skip a slightly-negative opportunistic shot (223 of 281 one-tick episodes); they now pick among shots that deal damage.

**A Giant Worm is ten threat records and used to be ten enemies.** Danger multiplied the segments; PierceLines paired every pair and burned the 4 ms budget before a stand was priced (699 of 1,156 worm ticks `Unresolved:budget-cut`). Eligibility, danger and the proposal cap of three now count one representative; every segment stays on the list for pierce, heat and forecasts. Capping how many pieces are visible was refused.

**The companion now fights the zombie standing on the player and flies over the one in between, and that changed on 21 September 2026 without anyone aiming at it.** It used to fight whatever was nearest, and the reason was a defect rather than a rule: the re-pricing re-flew every use at the aim the segment's opening tick solved, so a moving target stopped intercepting, the plan died of `uses-stopped-solving`, and combat re-searched and settled on the nearest body. With each use re-solved at its own fire time (`da5e416`), the plan binds the threat on the player from tick 3 and holds it for all 480 traced ticks, never refused and never cut. The intervening-hostile row in `Tools/EngineReplay/Combat/VerifySafetyIsALayerOnTheJob.cs` had recorded the old behaviour as its expectation and was restated, because asserting it would be asserting the defect against this project's own ruling that a target is picked by the harm an attack removes rather than by distance. This was probed rather than believed: the first reading — that correcting the aims removed a systematic bias against distant plans — was a tidy story with no tick of evidence behind it.

**A rock FireFrom used to dump the fight.** 3,519 ticks `fire-stand-unreachable` with combat raw p50 1.02. Unclaimed stays unresolved; rock falls back to here.

## Current state — 21 September 2026

**The stance is whole, it is bound by a course rather than chosen by a family, and every combat row is green.** The last fully clean whole-suite run is `Tools/Ledger/runs/4abf171-20260921-212441.jsonl` — 208 rows, 158 pass, nothing red and nothing skipped. The *first* clean run since `30fef2b` on 17 September was `da5e416` at 17:25 that day, which is the commit that closed the target hold. Three things landed on this folder that day and all three were found by reading the code against this guide or by a probe rather than in play.

A use is now identified by the shot it is, which is what lets a course hold a fight across ticks at all; the trap above carries the mechanism. A committed plan now releases on `hostile-moved-off-its-track`, so invalidation is spatial on the *bodies* as well as identity-keyed. And each use's aim is re-solved at its own fire time by both readers, which took the target hold from 0 of 3 ticks of ordinary motion to 3 of 3 and changed which hostile the companion fights — the finding above says how.

Built and unchanged: the seven generators, the beam, the overlay's committed-plan layer, `CheckTheFight`'s performed and churn checks, name-keyed knowledge save/load, and the extra-projectile modifier seam. `4d44794` (0.30.4) landed the hitting-fight band, the chain representative, travelling DPS-gated shots and FireFrom-from-here; 0.30.6 keeps a hitting `ServesPlayerDirectly` fight's score when the path to the stand is long, and a search cut that priced nothing still offers from here. Mastery pierce and extra-projectile bonuses are still planted in the S5 row, because there is no live bonuses record yet.

**None of it has been played.** Every claim above is a headless row or a traced probe, and the stance under a course brain has no play capture behind it.

What is consciously left: invalidation on the *terrain* is still the world's global edit counter rather than a spatial box, so the player mining a screen away still re-ranks the target. And the opener costs about 12 ms on a cold simulation cache while every live search on a crowd starts cold, which is `AIC-445` — a real cost that nothing in the suite asserts, and which is the only sense in which a fight is expensive: under the production clock the whole brain's worst tick of six hundred on a boss scene is 15.23 ms against a 16.67 ms frame.

## Operating

```
dotnet run --project Tools/EngineReplay -p:UseAppHost=false -- --combat-purpose
dotnet run --project Tools/EngineReplay -p:UseAppHost=false -- --attack-planning
dotnet run --project Tools/EngineReplay -p:UseAppHost=false -- --hunt-progress
dotnet run --project Tools/CombatAudit -p:UseAppHost=false -- --self-test
```

Always `DYLD_LIBRARY_PATH` to tModLoader's native OSX libraries. Never launch Terraria to prove a row.

## Planned work

Play the stance under the course brain — nothing here has a play capture behind it, and the target the companion now picks is a behaviour change nobody has watched. Wire mastery pierce and extra-projectile to a live bonuses record when one exists. Make combat-target *terrain* invalidation spatial, with a box the flight simulation actually bounds; the bodies half landed on 21 September 2026. `FightEnemies.cs` is 589 lines against a folder median of about 190 and now holds three separable jobs — the preparation and its funnel, the offer's value band, and the course-binding surface (`ActivateCourseBinding`, `AcceptedUse`, `ClearAcceptedUse`) that arrived with the course brain; the binding surface is the clean cut and `AcceptedCombatUse.cs` is already its sibling. It is code, so this is a plan: a later session sweeps the call sites under `Companion/Brain/Infrastructure/Selection/`, `.../Interactions/Firing/FireDueUse.cs` and `Tools/EngineReplay/Combat/`, and this guide's map tree. The stale `PlannedUse.AimPoint` is `Planning/`'s to carry back into the search, and its guide owns the mechanism and the two wrong ways to fix it.

## Cross-folder

Observation owns chain heads (`ChainHeadOf` / `MarkChainRepresentatives`) and the danger product that used to count every segment. Position owns FireFrom admission (`PrepareOffer` rock fallback). Movement/FreeSpace owns eight-tile heat, `ClearanceHeat.NudgeOffTerrain` (combat stands step only when clearance is under one tile) and `PreferClearer` (company walk combined; combat HereAndCompany enemy-only). Firing owns `FireBestFromHere`. Selection owns `WanderFloor` and `CombatAboveWander`.
