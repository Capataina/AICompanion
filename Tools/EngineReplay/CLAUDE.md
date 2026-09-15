# EngineReplay — the orb's fixtures, run against the installed game with no window

This console tool loads the installed tModLoader assembly headlessly and drives the real brain, the real senses and the real motor over Terraria's own tiles. It opens no window, runs no enemy AI unless a fixture spawns and ticks it, and loads no save file.

**What it is an oracle for changed with the body.** The walker had two bodies — a real NPC under Terraria's box collision, and a portable prediction under a shape approximation — and the suite's centrepiece was a collision matrix proving the two agreed. The orb has one body: the engine's tile collision is switched off for it and the motor runs the mod's own `CircleContact`, the same contact every headless tool runs, from the same source. So there is no second body to match and that matrix has no subject. `VerifyOrbContact` is the contact's proof now, and it proves the size rule directly rather than by agreement with a routine the body no longer uses. What the native suite adds over `Tools/NavReplay` is that this contact reads *Terraria's own tiles*, through the live tile reader, inside a real `CompanionNPC` the game's own AI entry point ticks.

```
EngineReplay/
├─ CLAUDE.md              this guide: the suite as a whole
├─ EngineReplay.csproj    references the mod under the `live` alias, and compiles part of it twice
├─ Program.cs             the entry point and the flag table
├─ ResetProcessState.cs   the per-process and per-case reset every instrument shares
├─ RunOneRow.cs           one named row, run through the ledger's emitter
├─ Combat/                hunting, guarding, firing, danger, encounter context, safety aftermath
├─ Gathering/             ore work, cooperation beside the player, remaining-work accounting
├─ Assistance/            lighting, collection, company strolls, courtesy, capability, the senses
├─ Lifecycle/             spawn and attachment, downing, stat mirroring, doors, HUD, the card
├─ Movement/              contact, free space, routes, following, recovery, escape, projectiles
└─ Observation/           the recorder, the event writer, family offers, evidence scenes, cost
```

Each child folder's own `CLAUDE.md` owns its fixtures. This file owns what is true of the suite rather than of any one fixture: how a case is run, what the aliases and the reset guarantee, the traps that bite everywhere, and the translation rules a walker-era fixture has to be read through. `Combat/CLAUDE.md` was left untouched by the body change on purpose, and it is **stale on four fixtures as of 15 September 2026**. `VerifyHuntAdmissibility` gained a stand-sweep settling loop, without which its sealed-enemy refusal read `firing-position-undecided` rather than a proven absence. `VerifyCombatPurpose` gained a `gate` diagnostic printing the eight numbers the combat-space threshold is decided from, the same settling loop inside its pursuit scene, a pursuit world twice as wide so the far reposition still prices a wait past the arsenal's horizon, and its spacing row moved last. `VerifyCombatActorMatrix`'s blocking pillar went from three tiles to twelve, because three was the walking body's height and an arcing shot goes over it — twelve being bounded above as well as below, since a column tall enough to push the flight around it past the arsenal's horizon prices both blocked arms at zero and makes the row comparing them pass against nothing. Each is explained where it lives; whoever owns combat next folds them into that guide.

## The default suite is a table of named cases, because the sum it replaced was an abort

`VerifyEngineMotion.Run` once ended in thirty-eight terms of `failed += Verify*.Run()`. Assertions here throw, so that expression was never a total: the first fixture to fail took every fixture after it with it, and the suite returned one exit code that could not distinguish twenty-two unrun fixtures from twenty-two passing ones.

It is a table now — forty-five entries in `DefaultCases()`, one named case per fixture, each run through `EmitLedgerRows.Case` with its own verdict and unable to reach its neighbours. The emitter's reset runs between them, so a case cannot inherit the world its predecessor left. The summed exit code is unchanged, so the suite means what it meant; what changed is that a red names one case and the other forty-four still say what they found.

The case name is the question the fixture answers, written as a sentence, because it is what the scoreboard prints and what `--case` matches. `AIC_LEDGER_CASE` selects by substring and `AIC_LEDGER_RUN` names the run file; a case not selected writes a skip carrying its reason, never silence.

**Case granularity here is the fixture file, not the assertion.** The verification plan wants a case per assertion site; that migration is not done. This is the granularity reachable without restructuring every fixture, and it is enough for per-case selection, rerun-red and a scoreboard that names what moved.

**Inside one fixture the abort is still real, and it is the single most misleading thing about reading a red here.** A fixture runs its rows in order and the first throw ends it, so every row after the failing one is unrun — and a fixture that fails at its third row tells you nothing whatever about its tenth. Clearing one red routinely reveals the next one behind it, which is progress rather than a regression, and a run's red count falling by one while a new name appears is the normal shape of that progress. Say which it is when reporting.

**So a row that is red on something nobody is about to change goes last in its fixture, and that position is a decision rather than an accident.** The three reds the rebuild left were all brain findings, and two of them were standing early enough to blind the rest of their file indefinitely: `VerifyCombatPurpose`'s spacing row stood second of nine and was hiding the pursuit valuation, both protection rows, the two identity rows, the whole of `VerifyEncounterContext` and the whole of `VerifyCombatActorMatrix`, and `VerifyResponsiveFollowing`'s oscillation row stood fourteenth and was hiding all five intent-region rows. Both are last now, with the reason written at the call site. Moving them cost one run each and was worth far more than that: **two of the seven rows uncovered behind the combat one were themselves walker-era defects** — a pursuit premise that never settled its stand sweep, and a pillar three tiles tall because that was a walking body's height — so the file had been reporting one red while carrying three. Clearing those three then uncovered a door row and a horizon premise behind them, and clearing the order leak between cases uncovered a fixture that had passed for months on another fixture's floor. A red that will clear this afternoon stays where it is; a red waiting on somebody's decision is moved, because the alternative is that the rows behind it are unrun for as long as the decision takes.

## Reading a verdict: four formats, and the exit code is the only reliable one

Fixture families report differently, and a grep tuned to one of them silently undercounts:

```
<case name> failed: <ExceptionType>: <message>    the ledger's Case wrapper — most fixtures
<family>: N case(s) failed                        a fixture that counts its own rows
RED <name> / FAIL <name>                          older rows that print their own verdict
Unhandled exception. System.…                     a standalone flag, where nothing wraps the throw
```

A run once showed sixty-four GREEN lines, zero `^RED` and zero "case(s) failed" and still exited 1. **Do not grep for a verdict prefix: read the log end to end — it is around 160 lines — and trust the exit code.** A fixture family's own verdict prefix is never the suite's signal.

The standalone flags are the fourth row's cause and it is structural rather than a defect: `Program.cs` dispatches most flags straight into the fixture, so an assertion escapes as an unhandled exception and the process exits 134. The same case inside the default suite is caught by the emitter and reported as one red row. A flag exiting 134 and a default-suite case failing with the same message are one failure seen twice.

## The flag table

```
(no flag)              the default suite: forty-five cases, exit 0 when none went red
--orb-contact          the circle contact's size rule, diagonal step, push-out and slide
--free-space           corridor widths, liquids as walls, that the flood finishes, and that a bounded flood exhausts inside its ball
--route-endings        pending against unreachable, and what an exhausted budget means
--attack-outcomes      the arsenal's forecast against what landed
--offer-validity       shot windows, cut searches, proven absences while the body walks
--capability           reach and tool power changing what is offered on the next preparation
--light-senses         the light field and the reach flood, and their three-valued answers
--doors                the native door helper, announced toggles, locked doors
--courtesy             placement and passage footprints moving the body out of the way
--render-ui            the profile, inventory, mastery and inspector pages, offscreen
--ore-work             the work-selection group: ore, preferences, activities, assistance,
                       cooperation, accounting, collection, trips, capability, the senses
--follow               responsive following, company local motion, courtesy
--protection-recovery  recovery-flight admission and guard retention
--observation          the recorder's lifecycle and the attempt-evidence producers
--travel-episodes      a journey recorded against the ticks the body could travel
--evidence-scenes[=dir] four stalls driven through the real recorder; keeps its captures
--brain-cost           per-phase timings and the recording-invariance proof
--combat-cost          the same, on a scene with four hostiles in it
--combat-purpose       the danger matrix, the pursuit rows, guard access and identity
--safety-aftermath     spacing, reflex suspension, an intervening hostile, surfacing
--dodge-repro          one arrow advanced by hand at body height on dry floor
--lifecycle            spawn and attachment, downing and revival, stat mirroring
--escape               the captured water escape
```

`--brain-cost`, `--combat-cost`, `--evidence-scenes` and `--dodge-repro` are instruments rather than suites: they measure or record, and the first two assert nothing about behaviour. Numbers any of them print describe the machine they ran on and are never asserted.

**Any timing from this suite is comparable only to another taken the same way.** A fixture run alone through its own flag pays JIT compilation for the whole planning path on first use, where the same fixture inside the default suite does not; a twelvefold difference was once credited to a change that had in fact moved the worst tick by nothing.

## The `live` alias, and the two copies of every static

`EngineReplay.csproj` references the whole mod under the `live` extern alias **and** compiles a handful of movement sources a second time, by an explicit per-file list, into this project itself.

That means **the movement core's statics exist in two copies**, and a reset or a world assignment naming one of them leaves the other holding the previous case's world. `ResetProcessState` writes both deliberately. Anything a fixture sets on `LimitPlanningWork`, `TerrainChanges`, `MovementQueries.World` or the search's world override has to be the `live::` one if the live brain is what reads it.

Three consequences that cost real time:

- **A new file that one of the listed sources calls must be added to the list too.** The mod's own build compiles everything and passes; this project fails with the new name missing, and every run afterwards silently uses the previous binary.
- **Types in a fixture are `live::`-qualified or they do not resolve.** `MovementQueries`, `CircleContact`, `OrbTerrain` and `PlaceSuppliedTorches` are the ones that bite, as CS0103.
- **Inside an interpolated string an alias-qualified expression must be parenthesised.** `{live::….Count}` compiles as the value `live` followed by a format specifier and fails with *"'&lt;global namespace&gt;' is a namespace but is used like a variable"* (CS0118); `{(live::….Count)}` is the expression. Hoisting the value into a local above the string is clearer than parenthesising and is what most fixtures now do. Search a file for `{live::` before building rather than finding each hole through its own compile error.

One more C# shape costs a build every time it is written: `FormattableString.Invariant($"…" + $"…")` does not compile, because concatenating two interpolated strings yields a `string`. Write one interpolated string, or build the pieces into locals first.

## No wall-clock allowance decides a verdict in the default suite

`EmitLedgerRows.Case` calls a reset before every case, and each instrument registers its own — the engine tier in `ResetProcessState.cs`. The reset lifts the millisecond allowances, leaving each query's work-count limits as the only bound, and returns the statics a case can reach to what a fresh process holds: the planning allowance, the shared clearance field, the search's world override, the terrain record, the body's immunities and the census. Both copies of every static are written, for the reason above.

A case whose subject *is* a deadline keeps the clock, and there are two ways to say so. A whole case tags itself `EmitLedgerRows.ProductionAllowancesTag`. A single row inside an otherwise lifted case saves the regime, turns the lift off, and restores **what it found** — never a literal, because a literal is the current default written down twice, and the day the real default moves every fixture ordered after that row runs under the stale one. The row's own mode is stamped by the emitter rather than passed by the caller, so a row always records the regime the process was actually in.

The walker's three deadline rows went with its planner and no orb row is about a deadline yet; the mechanism stays because the next one will be. Each of the three had announced itself by going red with its own premise assertion rather than by passing quietly — *"the starved run finished its search in one tick, so it proves nothing about deadlines"* — and an orb deadline row is held to the same bar.

## The verdict boundary: a fixture never prints its own PASS or FAIL

`Tools/check-navigation-boundary.sh` refuses any file under `Tools/` outside `Tools/Ledger/` that prints `PASS` or `FAIL`. A fixture that decides its verdict in a print looks in a terminal exactly like one that reported, while the ledger never saw it — so it cannot be compared against a baseline, selected by `--case`, rerun by `--rerun-red`, or noticed when it stops running, and every one of those is a silence that reads as health.

A fixture returns a failure count, throws, or calls `EmitLedgerRows.Detail` for a line a person should see; `Detail` prints exactly what the old line printed **and** folds it into the row. `GREEN`, `MEASURE` and plain descriptive lines are all fine — the refused words are `PASS` and `FAIL` specifically.

**`MEASURE` is the suite's word for a number nobody has declared a pass line for**, and it is used deliberately rather than as a way of retiring an inconvenient assertion. A row printed as `MEASURE` says what the body did and asserts nothing about it; the reason it is not asserted belongs in the file beside it.

## Reading a walker-era fixture: seven translations, and each one has bitten

Most of the reds this suite produced during the body change were one of seven shapes. None of them is a brain defect and all of them look like one.

**The body's point is its centre, and `Bottom` is the centre plus a radius.** A walker's assertions rest on feet standing on a floor; the orb's rest on a centre one radius clear of one. Every production caller of `FindToolAccess.InReach` passes `ctx.Npc.Center`, and the parameter is named `centre`; `FindToolAccess.EyeHeight` is zero, with the comment *"the orb's eye and its tools are its centre, and nothing sits above the body"*. A fixture reading the body point from `Bottom` and feeding it to anything reach-shaped, tile-shaped or distance-shaped is wrong by one row on a resting body — which is exactly where a boundary premise lives. Writes are different and are usually fine: `Npc.Bottom = (x, floorRow * 16)` lands the centre one radius above the floor, which is where a resting orb sits.

**A point at the player's feet floors onto the solid floor row.** A standing player's feet sit exactly on a tile boundary and `MovementQueries.Tile` is a plain `MathF.Floor` with no epsilon, so `Tile(player.Bottom)` is the floor itself — a tile no body occupies, which the flood never reaches and every reachability query answers `Unreachable` for. The cell a body actually rests in beside him is one radius up. This one is nastier than it reads because the failure is silent in three different places: a route priced to such a tile returns null and the caller keeps a fallback, a candidate snapped to such a tile is never priced, and a premise asking about it reports a proven absence from a complete region.

**A wall that stops short of the world margin is not a wall.** A pillar spanning the rows between a floor and a ceiling sealed a corridor for a body that walked along it; a flying body goes over the top. Every sealing wall in an orb fixture runs to row 0, and a scene that does not is usually caught by its own closing guard reporting that every case read reachable.

**Reach cannot gate whether work is offered at all.** A body that can hover beside any free cell reaches almost everything, so a premise of the form "at reach N this is refused" only holds at reach 0, and only for a solid target the body cannot occupy. An air target can never be refused by reach.

**State the orb holds permanently cannot witness anything.** `noGravity` and `noTileCollide` are set once at spawn and never cleared, so an assertion that recovery flight sets them passes before recovery starts. Re-express against the motor's own `RecoveryFlight`.

**A fact one component publishes for another has an order, and a fixture that arranges the fact instead cannot see it.** The motor's `ReadLiquid` runs inside `Commit`, at the end of a tick, and decides the liquid from the circle's own contact — then writes `npc.wet` itself, so a hand-set `wet` is both ignored and overwritten. A brain reading `Motor.InHurtingLiquid` at the top of the next tick is reading where the body was when it was last moved. The same one-tick skew governs the settled-speed reason: the sense decides at rest from the velocity the motor wrote last tick, so a fixture must sample the speed *before* `AI()`, not after `AI()` and `AdvanceNative()`. Both skews agree on every tick the answer is obvious and disagree only at the threshold, so they surface as a handful of ticks in hundreds rather than as a broken row.

**A bounded search needs its own clock driven, and each one keys off something different.** The reach flood advances per *rescore*, so it is grown by resolving the positioner. The firing stand sweep is keyed to `Senses.Tick`, so it is grown by advancing that clock — re-preparing an activity four hundred times without moving it re-reads one cached answer for ever. A premise that wants a proven absence must first establish that the relevant search *settled*; an exhausted bound is deliberately not a negative, and a row that reads `…-undecided` is the production rule working rather than failing.

## Traps

- **A scene must observe the world before it prepares, and prime the reach region.** Lighting and work read senses rather than measuring per candidate, so a fixture that writes a light map and immediately prepares is preparing against whatever the last scene left. Call `Senses.Update`, then resolve the positioner until the region is complete. The reach half fails in the opposite direction from the light half: work refuses an unfinished flood rather than walking at it, so an unprimed region makes every row read "not yet known" and the no-offer half of a pair passes for the wrong reason.
- **A stale region is complete, so priming by "resolve while incomplete" does nothing.** A row that edits terrain or moves a body after setup must throw the region away, not merely drive it.
- **Headless, nothing drives `LightingEngine.ProcessArea`,** so the engine never clears its own per-frame light list: one scene's carried lights are still discounted in the next. That reads as a sense defect and is a fixture leak. The light rows empty both that list and the sense's memory of it when they build a scene.
- **Forcing the light field to resample takes a real value, not `int.MaxValue`.** The gate is `++sinceRefresh < RefreshTicks`, so `int.MaxValue` overflows to `int.MinValue` and the sense returns early — the exact opposite of what the caller asked for, silently.
- **Headless, the game's spawn cap is whatever its initialiser left** (five), because `NPC.SpawnNPC` never runs. Any full-brain scene keeping reachable non-boss hostiles weighing more than five spawn slots in front of the observer will read an inferred encounter and zero every optional activity, with nothing in the scene that looks like an encounter. A scene with that many enemies writes the cap it means, as `VerifyEncounterContext` does, or keeps its reachable weight at five or less.
- **Hostile shots go in `Main.projectile[Main.maxProjectiles - 1]` at your peril.** `Main.ActiveProjectiles` iterates only the first `maxProjectiles` entries and the array's last element is the engine's dummy, so a shot placed there exists, reads active, and is never observed.
- **A recorded row with any hostile in it writes `NPC.TypeName`,** which reads `Lang._npcNameCache` — all null until localisation loads. A fixture that records combat supplies `LocalizedText.Empty` for exactly the null entries and restores them afterwards.
- **Loader templates are registered once per content type.** Registering a fresh template per fixture makes `ContentInstance<T>.Instance` null when more than one exists, so isolated tests pass while the combined process fails. Attach each fresh `ModPlayer` through its inherited `Entity` property and keep the one registration.
- **Native tile placement expects every player slot to contain an object,** inactive slots included; a null slot is a fixture defect before it is evidence about placement.
- **A mutation run against a row that sits late in an abort-on-first-failure list proves nothing about that row.** Hoist the row to the front before planting the defect it is supposed to catch.

## Current state — 15 September 2026

The suite builds with zero `error CS` and the default run exits 0: forty-five cases pass and thirteen rows print a `MEASURE`, on the whole-suite run of 15 September 2026 at `b9bf064` (`Tools/Ledger/runs/b9bf064-20260915-052728.jsonl`), after the reach flood became a disc and the swing gained its sight test. Earlier that morning the rebuild's own run had landed the brain fixes below. Three reds came out of the rebuild and each was a brain finding rather than a fixture speaking for the walker; all three are fixed in the brain, and the fixture that found each carries the evidence.

```
following responds to a player who departs        a stroll goal outside the intent region manufactured the reunion it then answered, and
                                                  a stroll un-settled the orb's speed-based arrival on its first moving tick; both fixed
ore work breaks ore without excavating …          the route home was priced to the floor row under the player's feet, which no flood holds;
                                                  the walker's FeetTile is restored and every "where he stands" site reads it
combat keeps its purpose across a substituted …   sight was the game's three-row beam, which a body one row above a floor never has along
                                                  that floor; sight is the single-tile walk now, for every consumer
```

Behind those, the rows that had never run at this body found two more: a journey still open at unload was dropped whenever the recorder's live lookup found no companion, so the travel-episode observer now closes the body it watched; and the per-process tile map handed one case's walls to the next, so the reset rebuilds the map before every case, which is what exposed a grounded-enemy scene that had stood on another fixture's floor since it was written.

**Every case starts from an empty hundred-square map.** `ResetProcessState.BeforeCase` rebuilds `Main.tile` before plugging the fresh world wrapper over it, so a fixture reads only terrain it wrote. A fixture that wants terrain builds it, in its own rows; the pursuit scene builds a wider map of its own and the next case gets the empty square back.

`--render-ui` additionally throws on the inspector's follow-comfort region, whose drawn boxes and containment test disagree at one probe point. That is a known defect of the intent-region positioning which the orb's positioning rewrite replaces, it is recorded on main, and it is not part of the default suite.
