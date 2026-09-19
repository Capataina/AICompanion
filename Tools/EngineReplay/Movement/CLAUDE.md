# Movement fixtures — the contact, the free space, the route and the body that flies it

Everything about getting the orb from where it is to where it was asked to be, proved against Terraria's own tiles. `VerifyEngineMotion.cs` is also the default suite's entry point: it holds the default case table and the flag dispatch, which the parent guide describes.

The default table also registers retained-course policy/projection contracts, native receipt observation, diagnostic transport and the shared-budget opener case from their owning folders. Focused flags are convenience entry points; those checks must remain in the default table so the ledger sees a failing new contract during whole-project verification. Pure-core rows retain their narrower evidence scope even when the native suite runs them.

```
Movement/
├─ CLAUDE.md
├─ VerifyEngineMotion.cs              the default case table, the flag dispatch, and the five route scenes
├─ VerifyOrbContact.cs                the circle contact: size rule, diagonal step, push-out and slide
├─ VerifyFreeSpace.cs                 corridor widths, liquids as open as air, and that the flood finishes
├─ VerifyRouteEndings.cs              pending against unreachable, and what an exhausted budget means
├─ VerifyResponsiveFollowing.cs       travel intent, meeting places, reunion, method settling
├─ VerifyAccompanyingThePlayer.cs     moving about the player's whole region: never still, never trailing, moving at once
├─ VerifyWithThePlayerNeedsAWayToHim.cs  a body inside the region but cut off from the player by a sealed wall is outside it and comes round
├─ VerifyFollowRecoveryAndProtection.cs  what may start recovery flight, and guard retention
├─ VerifyLiquidsAreAir.cs             every liquid as air: flight through all four at the air pace, and a flooded passage reached through it
├─ VerifyObservedMotion.cs            the shared enemy forecast against native collision
└─ (the projectile-arc fixture is `../Combat/VerifyArcLearning.cs` since the authored kit died: arcs are learned per projectile type from the companion's own shots, so what it asks is a combat question)
```

## The contact is the body, and it is proved directly

`VerifyOrbContact` replaced the collision matrix rather than inheriting it. The walker's matrix compared our adapter against Terraria's own NPC collision routine because the walker used that routine; the engine's tile collision is switched off for the orb, so there is nothing to agree with and the contact is proved on its own terms:

- **The size rule.** A twenty-pixel body fits every two-by-two gap and no one-by-one gap, in every direction. This is the whole of "how big is it" stated as behaviour rather than as a constant, so it survives the radius being retuned.
- **The diagonal step.** Crossing a one-tile diagonal step never overlaps a wall at any point of the crossing, which is the case a swept test passes and a per-tick point test does not.
- **Push-out and slide.** Contact pushes the body out of a wall, kills the velocity *into* the wall, and keeps the component along it. A contact that zeroed the whole velocity would stop a body that should slide along a surface, and the two halves are asserted separately.

`VerifyFreeSpace` is the graph over that contact: a two-wide corridor is open to the flood, a one-wide is closed, and water or lava across a corridor leaves it exactly as open as a dry one. Its second case is a standing guard against the flood that never finishes — over a screen-sized room it must settle in a handful of slices — because "not yet known" is only a useful third answer if it eventually stops being the answer.

## The five route scenes each name the mechanism they exist for

`VerifyRoutes` drives the live navigator and the live motor with a real `CompanionNPC` over native tiles, so what it proves is the whole chain: search, smooth, steer, contact, and the engine adding the displacement.

Each scene names its mechanism, because *"it arrived"* is satisfied by three different code paths here and only one of them is route-following. The open floor exists to prove the navigator does **not** plan when the straight line is clear. The two-tile ledge, the sloped staircase and the two-wide shaft each put a wall across that line, so they can only arrive through a planned route, and each asserts that it saw one. The one-wide shaft is the refusal: the body does not fit, and the scene requires `Unreachable` and no arrival.

```
open floor          93 ticks   arrived  clear line, no route planned
two-tile ledge      96 ticks   arrived  line blocked, route planned
sloped staircase    99 ticks   arrived  line blocked, route planned
two-wide shaft     123 ticks   arrived  line blocked, route planned
one-wide shaft       0 ticks   refused  unreachable, never started
```

**`ExecutionStatus.Direct` does not mean "no route was needed".** It means only that no route exists *yet* and the swept line is clear; once a `Path` exists the status is `Executable` however open the floor is. A scene asserting `Direct` on an open floor is asserting something about search timing rather than about geometry, and the open-floor scene therefore asserts the clear line and the absence of a planned route instead.

`VerifyRouteEndings` owns the distinction the rest of the brain depends on: a search that ran out of budget is **pending**, and only an exhausted one is **unreachable**. It reports how many slices a budget-limited query stayed pending, over how many corners the absence was finally proved, and how a walled body against a travelling body differ in strikes and distance covered. An exhausted bound is not a negative anywhere in this tree, and this is where that rule is measured rather than assumed.

## Following, and what the two matched arms can no longer tell apart

`VerifyResponsiveFollowing` drives `PlayerSense` around a repeated local path and requires it not to report travel; feeds the production intent estimator matched movement with and without local-work evidence, brief reversal, sustained backtracking, pause and vertical movement; and requires duplicate timestamps not to manufacture samples while observation gaps and position corrections clear the history. Those establish the observation sequences, not knowledge of the player's future destination.

Its meeting-place rows price a place on the player's apparent journey against the companion's own free-space flood. Four scenes end the player on the same tile after different recent activity — travelling, placing torches in place, a brief turn, and sustained backtracking — so the pairs compare activity rather than position, and each asserts the player really did end on the same tile before any verdict is read.

**One matched pair was deleted here rather than retuned, and the reason generalises.** `VerifyMeetingPlacesFollowTheCompanionsOwnRoutes` runs two terrains that differ only in whether the lower route reconnects to the player's upper floor through a gap ahead, or ends at a cliff. For the walker, which way the body set off was the observable consequence of which opening existed. The orb is still stopped by tiles — its circle contact is the body and it no more passes through a floor than the walker did — but the upper floor is open at its left end, which it can simply rise through with no ledge to climb and no jump to prove. From the companion's start that opening is about nine tiles behind, against a gap forty-one tiles ahead that then has to be walked back from, so the way up behind is the cheaper route in **both** arms:

```
              +0     +30    +60    +90    +120   +150   +180    arrived   ended
gap ahead     0.0    -7.1   +1.9   +13.1  +24.4  +35.6  +41.8   tick 395  (1486.03, 1177.41)
cliff         0.0    -5.6   +4.1   +15.2  +26.5  +37.6  +42.5   tick 396  (1486.18, 1177.46)
```

Both arms set off backwards and finish a tile and a half and one tick apart. The directional half of the contract was failing on a sign rather than a margin, so no threshold restores it. What survives, asserted on both terrains, is that the player is met on the journey ahead of him, priced from the companion's own routes, and that reunion completes through the production brain. Reviving a directional contract for this body needs a scene where the opening ahead is genuinely the only one — an upper floor sealed at both ends — and this geometry is not that.

Both arms are now measured before either is judged, because they are a pair whose whole point is the difference between them and asserting inside the loop meant the first arm's failure aborted before the second produced the number it is compared against.

## Recovery, liquids and the two forecasts

`VerifyFollowRecoveryAndProtection` exercises recovery admission with one alternate executor issuing reunion, exact-work, guard and hold requests at the same distant player position: **only reunion may start flight.** That catches a follow-class dependency and a coordinate-only shortcut independently of the flight and clearance checks. Its guard rows replace the observation's most urgent enemy after scoring and require the prepared identity, score and requested anchor to stay bound to the original enemy, including at entry; removing that enemy must produce `Hold` at execution. These expose a score-to-execution substitution; they do not establish arrival at a useful firing pose or a native protective hit.

Recovery-flight assertions are expressed against the motor's own `RecoveryFlight`, never against `noGravity` or `noTileCollide` — the orb holds both permanently, so an assertion on them passes before recovery starts and witnesses nothing.

`VerifyLiquidsAreAir` holds the owner's ruling of 15 September 2026 that every liquid is air to the orb, in two scenes on native tiles with the real `CompanionNPC`. The first drives the live motor straight at its cap through a full-height column of water, honey, lava and shimmer in turn and compares every tick inside a liquid with the cruising tick in air, against a tolerance declared before its first run that only float rounding may use; it also requires each liquid to be touched for at least ten ticks, life never to fall, and no shimmer buff or transparency. The second floods the only passage through a sealed wall with each liquid in turn and asks the route search for a way through it within a quarter over the straight line, then drives the whole brain with keeping company as its only activity and requires the reach sense to read the player's tile reachable and never unreachable, the body to touch the liquid and arrive inside his region past the wall, no life lost, no suspending owner, and no evade reason but off or kept. Measured on 15 September 2026 with each mutation shown red and then restored: a liquid wall put back in the free-space terrain reddened the passage for all four liquids, the search exhausting with no route; a strike put back for water and lava reddened the flight (life 100 to 66) and the water and lava passages (100 to 17); and the engine's own wet slowdown applied to the motor reddened the flight by exactly the engine's shares of a 9 px step, 4.5 px in water and lava, 2.25 in honey and 3.375 in shimmer. Neither scene reaches the engine's own NPC update, which no headless tool runs.

The escape fixture that stood here went with the escape. The property it paid for binds any takeover: the motor publishes which liquid the body touches only inside its own application, so a brain reading it on the body's first tick somewhere is a tick behind, and a hover anchored on that tick floated the body straight back into the pool the escape had carried it out of.

`VerifyObservedMotion` runs the shared target forecast against the same initialised tile map: a stationary grounded hostile stays supported, a tile-colliding flyer stops at a wall while a phaser crosses it, observed acceleration changes the short forecast, a jump is not extrapolated as a repeated impulse, `Forget` removes a reused slot's old track, and a position correction during the same engine tick replaces an already-built forecast. It snapshots Terraria's collision scratch flags around each forecast, because a target forecast that changes shared collision state corrupts the movement prediction it exists to inform.

The projectile fixture that used to sit here compared an authored kit's flight profiles against native `Projectile.VanillaAI`. The kit is gone, and with it the profiles: a projectile's arc is now a prior read from the game's own AI style and then learned from the companion's shots, and `../Combat/VerifyArcLearning.cs` measures that learning as hits per shot before and after. The swept trace's terrain sampling is still exercised there.

## What these fixtures deliberately do not establish

Reliable movement through every live cave, comfortable spacing in real play, behaviour under enemy pressure, or compatibility with any other mod. They establish the recorded scenes, on this machine, against the installed game's tiles.

## Current state — 15 September 2026

**`VerifyResponsiveFollowing` runs every row and reports each one before it fails, and the reason is a regression that hid.** Its rows used to be called in sequence and throw, so the first red took every row after it. The intent-region commit `40478e5` put four rows red where its parent `78b8f2a` ran the whole case green, and only the first was ever seen, until a copy of the fixture was run row by row. Two of those four were the old pull inside the region — the reunion curve's step and the stopped player's method flips — and went green when rejoining became zero inside the region. The other two named mechanisms a region that always holds the player left with nothing true to report, and the motion commit restated them rather than retuning: the meeting-activity row, whose "met three tiles ahead" held only while the region could be led off his feet, now requires a travelling player not to be met behind him, with being ahead moved to `VerifyAccompanyingThePlayer`'s trailing share; and the sloped-ground row, which waited for a settled streak nothing waits for any more, now asserts that a body over the slope inside the region is with the player. Each restated row carries its reason at the row.

`VerifyAccompanyingThePlayer` holds the owner's ruling that inside the region the companion moves about it and never stops: an idle player's companion never still for ten ticks, inside the region once there, moved by the accompanying owner and through both outer thirds and both halves of the box; a travelling player's companion never still, inside the region, not trailing him and behind his centre on at most a quarter of the ticks; a slow player walked long enough for the walk to cross the box and come back held to the same lines, because in the shorter walk the target never reaches the box's rear and the row measured the same trajectory with the rear left open; and a companion that begins inside moving from its first ticks.

`VerifyWithThePlayerNeedsAWayToHim` holds the owner's ruling that being with the player needs a way to him inside his region. A wall runs from the top of the world into the ground, the player stands east of it, the companion starts inside his region's box on the west, and the only passage is a trench whose floor lies below everything the sense's bounded flood may enter. A premise asserts that last part, because a shallower passage inside the box joins the two sides by the rule and the scene would then prove nothing; the first draft put the passage one row too high and the premise refused it. The sense must read the body as not with him on the first tick, and within six hundred ticks the body must cross the wall and be with him on his side. Measured 15 September 2026: with the rule it crossed at tick 58 and was with him from tick 87; with membership ignoring the side flood it read as with him on the first tick and never crossed.

The stopped-player row that was red before is worth keeping as a record, because it found two brain defects rather than one.

**`VerifyAStoppedPlayerDoesNotOscillateTheMethod` failed with three method changes over 600 ticks against a ceiling of one, and then with eight.** The first trace named a stroll goal outside the follow comfort: `KeepCompany`'s stroll band is wider than the intent region at rest, so a goal chosen in the outer band left the follow objective unsatisfied by construction, reunion took the method back, the body returned, and local company took it again. The stroll picks only cells inside the region now. That fix made the row worse, eight flips, and the second trace named the orb's own arrival: the walker's rest test was the ground, which a walking stroll keeps, and the orb's is its speed, which a stroll breaks on its first moving tick — so every stroll un-settled the arrival and reunion won again. The settled state latches while the body stays inside the region, and `../../../Companion/Brain/Infrastructure/Observation/CLAUDE.md` carries the rule. The stroll itself was deleted later the same day, when the orb became a body that hovers wherever it settles, and the company row that required both a rest and a stroll now requires that company beside a resting player holds and that holding still completes the reach region — the class the stroll's deletion briefly broke, recorded under the Gathering fixtures.

**The row runs last in the fixture, and that is why the intent-region rows before it report at all.** It stood fourteenth of nineteen, and because assertions here throw, every run ended there: the region rows — the rejoin pull being zero inside the region with no step at its edge, the companion leading a travelling player, a climbing player growing the region upwards, and settled arrival on sloped ground — had not run at this body since the change. A red waiting on a decision does not get to hold the rows behind it hostage for as long as the decision takes. The same abort is why a change here is only verified when the fixture's rows have been run one by one: the region commit's four reds above hid behind the first of them.

**`VerifyOccludedPlayerStillProvidesADestination` is the orb's contract now, not the walker's.** A closed door is a wall to the flood, so no candidate on the player's side is reachable and the positioner chooses nothing; a follow request with nothing chosen aims the navigator at the anchor itself, which is how the body reaches the door for the door interaction to open. The row accepts either a chosen place beyond the door or the navigator's goal beyond it; the walker's version demanded a chosen place, which the orb's positioner deliberately no longer invents. Its companion starts beyond the player's region, and the row asserts that before its verdict. It used to start eight tiles away, inside the region, and passed through a floor that raised rejoining whenever sight was lost; losing sight is not distance, so inside the region there is no rejoin to ask about, and the door is asked about where rejoining is asked.

**`VerifyObservedMotion`'s grounded enemy stood on another fixture's floor for as long as it existed.** The tile map was built once per process, so the free-space case before it left a floor under the enemy; the moment every case started from an empty map the enemy fell through nothing. The scene lays its own floor now. A fixture that reads terrain it did not write is order-dependent by construction, and the reset rebuilding the map before every case is what makes that class visible instead of latent. The same fixture's regroup row orders urgency by the gap beyond the player's region rather than by distance to his feet: zero inside it with a leaving player, a long route and a stall all present, and rising with the gap, the departure and the stall outside it.

## Traps

- **A fixture that starves a static by writing it must restore what it found, not a literal.** Restoring a literal is the current default written down twice: the day the real default moves, every fixture ordered after that row runs under the stale one, and a suite that already carries one unattributed flake gains a second failure nobody can attribute.
- **A reading compared against a decision has to be taken on the tick the decision read.** The brain decides from what the motor wrote last tick, so a speed read after `AI()` and `AdvanceNative()` is the velocity that replaced the one the decision was about; the two agree except at a threshold, which is exactly where a row about a threshold lives. It surfaced as four mislabelled ticks in six hundred on a settled-or-moving reason that has since been deleted, and it applies to any row that grades a per-tick decision against the body.
- **`Vector2.Distance(companion.NPC.Bottom, player.Bottom)` compares two different kinds of point.** The body's is a centre plus a radius; the player's is feet. Comparable measures use the companion's `Center`.
