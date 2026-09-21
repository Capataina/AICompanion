# Combat activity fixtures — the stance competing against the other jobs, through the whole brain

One file, seven rows, and a different subject from every other folder under `Combat/`. The planning fixtures ask whether a good plan is found; these ask whether the companion *takes* the fight — and whether it declines to when it should. Every row but the last drives a whole `Brain.Tick` on native tiles with a real player, a real scene and the real decision, so a red here is about what the companion would do rather than about a number.

```
Activity/
├─ CLAUDE.md
└─ VerifyCombatActivity.cs   F1–F6: only combat fires, shared eagerness, danger over work,
                             a distant idle enemy, a dodge bending mining, an unarmed companion,
                             and the hunting-off save migration
```

## The six behaviours, and what each scene is built to make impossible

```
F1  only combat fires          mining owns the body for 120 ticks with a shootable zombie 300px
                               away; fired must be exactly 0, and the silence must read
                               `not-fighting` — the gate's own word — rather than an accidental quiet
F2  shared eagerness           the companion trails 750px behind a standing player, outside the
                               intent region so keeping company pulls reunion, with a motionless
                               zombie overhead that a bow shot solves from where it stands. Only the
                               stance's own eagerness can lift combat over that pull
F3  danger over work           a zombie walked into contact with a wounded player must beat a vein
F4  distant idle enemy         the same mining scene with the zombie at 560px: mining keeps the body
                               for over 100 of 120 ticks and combat takes none
F5  dodge bends mining         a hostile shot on a collision course must bend some tick's motion,
                               neither suspend nor replace mining, and leave zero hits
F6  unarmed offers nothing     both weapon slots emptied: score 0, `NoOpportunity/no-weapon`, and
                               sixty ticks that never run combat and never fire
    hunting-off migration      a legacy `hunting` key loads as combat off; a save that wrote
                               `combat` reads that and not the legacy key; neither key defaults on
```

**F1 and F4 both need their negative half to be earned rather than assumed**, and they earn it differently. F1 asserts the zombie is shootable on some tick before it asserts nothing fired, because silence over an unshootable target proves nothing. F4 has no such guard and relies on distance alone, which is worth knowing before reading it as the stronger of the two.

## The scene defect that inverted a verdict, and the rule it left behind

`danger lifts combat over work` was red, and adjudicating it on 21 September 2026 found three defects in the scene and none in the brain. Each is a rule for anything written here afterwards.

**A hostile that cannot reach its victim cannot test a preference for defending it.** Enemy AI does not run headless, so a hostile spawned clear of its victim stands exactly where it was put for the whole scene. This is not the familiar "the fixture is quieter than the game" caveat — it *inverts* the row's verdict, because a correct consequence forecast looks at a threat that provably never makes contact and prices the harm at zero, which is the right answer to the world the scene actually built. The row's zombie sat at `player.Bottom + (48, 0)`:

```
moved 0.0px over 120 ticks · closest approach 48.0px
boxes 18x40 (zombie) against 20x42 (player) — half-widths sum to 19px
```

Twenty-nine pixels of clear air, permanently, while `ProtectionUrgency` read 0.757 because the threat sense reasons about a hostile *near* a wounded player rather than about a simulated contact. Headlessly the threat sense is the one that is wrong about this world, and **no change to the brain could have turned that row green**.

**Moving an entity by writing `position` teaches the forecast nothing.** `PredictObservedMotion.Observe` derives its track from `npc.velocity` and never from a position delta, so a hand-walked hostile that moves only its position exports a track with zero velocity and every consumer simulates it standing still for the whole horizon. Contact was then predicted only once the boxes already overlapped, which made whether any harm was priced at all depend on which ticks happened to publish a course — the row's harm read `0.3500` in one run and `0.0000` in another, and the `0.3500` reached three files before a review found it was not reproducible. **Any scene here that hand-walks a hostile writes both.** It walks it *before* the brain tick, so the observation frozen that tick sees it where it is, and it stops four pixels past touching rather than at touching, because contact is a strict overlap and two boxes sharing an edge read as no contact.

**An overridden stat that made a scene convenient under a scoring chooser can make it unwinnable under a consequence forecast.** The row overrode its zombie to 400 life and 20 damage, which was harmless when combat won by being eligible and nothing was priced, and became unwinnable the moment consequences were priced: a hostile the companion cannot kill inside the 180-tick forecast horizon lands its hit whatever the companion does, so defending is worth exactly nothing and preferring the vein is the objective being *right*. It uses the game's own life and contact damage for the type now. **The tell is a row whose every candidate prices the same harm.**

The premise is a row of its own now, printing the threat's displacement and closest approach before any outcome is read.

**The same caution applies in reverse to every row here that passes.** F1 and F4 both use a 400-life zombie that never moves, so they are testing the arithmetic of a static tableau — which is usually exactly what was wanted, and is worth saying out loud rather than leaving the next reader to assume either way.

## Two process-global fixtures, both scoped

`ScreenFollow.CentredOn` centres the screen on a point the way the live game centres it on the player, because the fixture's screen otherwise sits at the origin and every on-screen rule answers for a camera nobody plays with — F2 needs it, since the stance's nearness reads the screen. `ClearAfter.At` wipes a planted hostile slot on scope exit, pass or fail, so a zombie never leaks into the next scene's observation: `MostUrgent` and the urgencies survive a threat-list rebuild. Both restore what they found on dispose. **A row here that plants a hostile without `ClearAfter` hands it to the next case**, and the per-case reset does not catch it.

## Traps

**The file aborts on its first throw, so a mutation planted against a late row proves nothing until the row is hoisted to the front.** Clearing one red here routinely reveals the next, which is progress rather than a regression — `68f07bd` cleared `a damageable hostile in reach takes the body off keeping company` and uncovered `safety bends the body inside its job` behind it in the same run, with the suite's red count unchanged and the name different.

**F2 was the row that exposed the use-identity defect, and it is the scene to reach for on any "the companion cannot hold a fight" symptom.** It read combat winning 5 ticks of 30 with the plan priced at 3.476 over a front of 21 — the shot found, priced, and impossible to hold — because a course's accepted use was keyed on the search's plan number, which rises every frame. It reads 30 of 30 now with `decision=course-retained activity=combat steps=1`. The mechanism is in `../../../../Companion/Brain/Activities/Combat/CLAUDE.md`.

**`hunting-off migration` is the one row here that drives no brain at all.** It reads and writes `Preferences` tags directly, so it is fast, deterministic and unaffected by every scene rule above.

## Flags

```
(default suite)     all seven rows as named cases in VerifyEngineMotion's table
--combat-activity   the seven rows on their own
```

## Current state — 21 September 2026

Every case in this folder passes on the last fully clean whole-suite run, `Tools/Ledger/runs/4abf171-20260921-212441.jsonl` — including `a damageable hostile in reach takes the body off keeping company` and `danger lifts combat over work`, both of which were red for most of 21 September while the course brain was adjudicated row by row.

## Planned work

`VerifyCombatActivity.cs` is 416 lines, about one reading, and is **not** worth splitting: seven rows sharing one scene builder and two scoped fixtures, where dividing them would put the fixtures in one file and their callers in another. Recorded as a considered no.

## Cross-folder

`../../../../Companion/Brain/Activities/Combat/` is the stance every row here drives, and `../../../../Companion/Brain/Infrastructure/Selection/` owns the course that now decides the tick. `../Planning/` asks whether a good plan is found; this folder asks whether it is taken. `../../Movement/VerifyEngineMotion.cs` holds the default-case table these rows are named in.
