# 8 Verification — rows that prove each behaviour, and the mutation each must fail

Every row names what it varies and the mutation that must turn it red, because a row that stays green with the change reverted proves nothing. Rows run in `sh Tools/verify.sh` and file into the ledger like every existing fixture.

## What headless can and cannot prove

Headless fixtures can run vanilla projectile AI natively (`VerifyArcLearning` calls `Projectile.VanillaAI()` tick for tick today), so learning is tested against the game's own code for vanilla types. Whether a full `Projectile.Update`, with tile collision and hooks, runs headless is **unverified** and is phase 0's first probe; rows marked ◇ depend on it and fall back to `VanillaAI` plus the engine's `Collision.TileCollision` if it does not. No modded item exists headless, so modded behaviour is covered by the knowledge audit on real captures and by the play protocol below, and the report says which weapons were only checked in play.

Knowledge rows (K) plant nothing and learn from native flights. Simulation rows (S) plant knowledge and compare the simulator against native flights. Planning rows (P) plant knowledge and forecasts, so they test planning and not learning. Activity rows (F) run the brain.

## Knowledge

| Row | Asserts | Mutation that must fail it |
|---|---|---|
| K0 learned from the player | a law fitted only from player-owned flights of a type predicts the companion's shot of that type | watch companion shots only |
| K1 delayed gravity | the throwing knife's law matches native tick for tick | drop the onset term |
| K2 bounce ◇ | Water Bolt's learned law and wall response predict its bounce points in a fixture box | disable the reflect response |
| K3 homing | a Chlorophyte bullet's learned homing predicts its path to a placed body within tolerance | drop the homing term |
| K4 pass-through | a `tileCollide = false` type is predicted through a wall | force die-on-contact |
| K5 children | a splitting vanilla projectile's children are predicted by trigger and count | drop child learning |
| K6 cursor spoof | a mouse-reading vanilla projectile follows the companion's aim point | spoof off: it follows the player's mouse |
| K7 modifier isolation | traces with a +1 pierce modifier leave the learned law and hit response unchanged | let modified traces update the hit response |
| K8 volley grouping | a synthetic spawn stream of a four-pellet same-tick use and a three-shot burst across the animation groups into one use each | group by tick only |
| K9 unpredictable still fires | a type the library cannot fit is fired and valued by outcomes, not refused | restore refusal on unfittability |
| K10 saved by name | knowledge saved and loaded under shuffled numeric ids resolves to the same weapons | key by numeric id |

## Simulation

| Row | Asserts | Mutation that must fail it |
|---|---|---|
| S1 no cap | an unlimited-pierce use along twenty segments strikes twenty | reinstate a cap of eight |
| S2 overkill once | four pellets on a body with less life than one pellet record four hits, and the plan credits one kill and the body's life | reserve nothing across hits |
| S3 damage share | a volley whose slots carry a quarter share each lands a quarter per pellet | treat every slot as full damage |
| S4 delayed hit | a timed child's hits land at their simulated tick, after the parent's | land children at the parent's tick |
| S5 extra projectile | an Extra projectile modifier adds its spawn with its damage and spacing, and the prediction changes at once | apply modifiers after simulation |
| S6 reproducible | the same decision simulated twice returns identical hits | draw spread angles at random |

## Planning

| Row | Asserts | Mutation that must fail it |
|---|---|---|
| P1 company | a slime behind a travelling player is fought from inside his predicted region | drop the company gap objective |
| P2 range by weapon | a flat long weapon's stand is at range and a spread weapon's close, against one lone target | ignore the volley's spread |
| P3 spread closes on a boss | with a shotgun that wins only close, the plan closes in at full life and holds range at low life | constant companion-harm weight |
| P4 worm line | the committed stand lines a pierce along a segment chain | remove line proposals |
| P5 goons then boss | a two-segment plan: far stand while goons live, close stand after | depth one |
| P6 floor roller | a low flank stand for a gravity-and-floor piercer against a group | remove floor proposals |
| P7 grenade then pierce | an above stand for the area use, then a flank pierce timed to the explosion | start segments at arrival only |
| P8 bank shot | a target behind a corner is planned with a bouncing weapon and not with a straight one | disable bounce aims |
| P9 danger | high player danger commits the harm-prevention plan over the damage plan | constant weights |
| P10 stable | an unchanged scene keeps one plan across rescores | a score-bonus hold instead of validity |
| P11 budget cut | a starved budget reports unresolved, not no attack | report a cut as absence |
| P12 undominated | a plan worse on every objective than another is never committed whatever the weights | skip the dominance filter |

## Activity

| Row | Asserts | Mutation that must fail it |
|---|---|---|
| F1 only combat fires | with a hostile in reach and a clear line, mining, lighting, collecting and keeping company fire nothing and record `not-fighting` | let the hands fire on any activity's tick |
| F2 combat is eager | a damageable hostile in reach makes Combat beat keeping company | today's hunting and guarding scores unchanged |
| F3 danger lifts combat over work | an enemy on the player takes the body from a vein; an idle enemy far off does not | drop the player-danger lift |
| F4 dodging survives | a projectile at the companion mid-vein bends the body and the vein continues | gate the evade layer on Combat |
| F5 unarmed | empty weapon slots offer no Combat | offer Combat on a hostile regardless of weapons |
| F6 old preference | a save with `"hunting" = 0` loads as Combat off | read only the new key |

## Audit tool

| Row | Asserts | Mutation that must fail it |
|---|---|---|
| A1 snapshot fidelity | a snapshot replayed with the live budget and proposals reproduces the committed plan exactly | omit the stand verdicts from the snapshot |
| A2 finds a better stand | with a planted scene whose best stand no generator proposes, the exhaustive audit reports the regret and "no generator" | give the audit the live proposals |
| A3 weight sweep moves the decision | doubling the damage weight in P3's scene moves the stand closer | sweep a weight the evaluator ignores |
| A4 calibration | a planted wrong law is reported by the knowledge audit on a synthetic capture | pair predictions with the wrong shot |

## Cost

`C1` in `Tools/EngineReplay/Combat/Planning/`, measured rather than asserted, through the existing `--combat-cost` path: forty hostiles, four weapons, one forty-pellet volley, the full proposal set; per-rescore planning time at the 50th, 90th and 99th percentiles against the frame budget, with and without the simulation cache. Phase E is not accepted until the 99th percentile fits.

## The world run

The existing world-run card (a hostile placed in the recorded world, grading distance and stillness) gains a combat variant once phase A lands: a zombie beside the recorded route, graded on Combat winning within a stated time, no `not-fighting` while it is on the player, and the companion rejoining the route after the kill.

## The play protocol

Headless rows cannot hold modded weapons, so each phase that changes fighting ends with a named play session the owner runs, captured, and read through the report and the audit:

1. vanilla: wooden bow, a shotgun, Water Bolt, Chlorophyte bullets in a gun, a grenade, a sniper rifle — one at a time against slimes on the surface, then paired;
2. the Eater of Worlds with a piercing weapon, and Skeletron with a shotgun and a sniper;
3. with Calamity enabled: a bouncing shotgun, a homing bow, a splitting magic weapon, a rogue throwable, each named in the capture's preamble;
4. with mastery points unlimited: Piercing and Extra projectile levelled mid-fight.

Each session is graded by the knowledge audit's calibration per weapon, the search audit's share on the front, `CheckTheFight`'s churn and eagerness, and the owner's own notes, which are verified against the capture before anything is fixed.
