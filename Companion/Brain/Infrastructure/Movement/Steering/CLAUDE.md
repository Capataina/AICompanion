# Steering — a route, and the momentum that follows it

The navigator owns one route and one search at a time and turns them into a velocity every tick; the steering law is the pure function under it. Nothing here writes the body: the answer is a `Controls`, the velocity the motor should accelerate toward, and the motor is what applies it.

```
Steering/
├─ CLAUDE.md            this guide
├─ OrbState.cs          the body at one tick: centre, velocity, the liquid it touches, whether it is pinned
├─ Controls.cs          what the brain asks for one tick: the velocity to accelerate toward; zero is a brake
├─ OrbPace.cs           the speed cap and acceleration the motor will apply this tick, published for the game-free core
├─ Route.cs             the points from the body to the goal, the segment the body is on, validity, and line-of-sight smoothing
├─ SteerAlongRoute.cs   the steering law: a lookahead point, a brake for the goal, a cap through the next bend
├─ Navigator.cs         plans, re-plans, steers, dodges, and scores how each attempt ended
└─ BehaviourCensus.cs   the whole run's accounting: places asked for, reached and abandoned, plans made and found nothing
```

## The law

The body is steered at a point a fixed distance ahead of its projection on the route, which is what makes it lean into a turn and cut its inside rather than track every waypoint, and a body that has drifted off the route is steered at the lookahead from its projection, which pulls it back onto the route rather than back to where it left. The speed asked for is the cap, lowered toward the goal so the body arrives rather than overshoots — the speed from which it can stop over the remaining route, floored by the straight distance to the goal, because a body that has overshot projects onto the goal itself and reads no route left — and lowered into a bend in proportion to how sharply the route turns at the next waypoint within braking distance.

The motor accelerates toward that velocity by at most the acceleration, in any direction, so the acceleration is also the turn authority and the turning radius at speed is the square of the speed over the acceleration. A body that cannot turn inside a corridor's width scrapes the far wall whatever the route says; the corridor fixture's minimum-clearance row is where that showed, and the acceleration multiple is what fixed it.

## A route is smoothed without giving back what the search paid for

Smoothing skips waypoints while the straight segment past them keeps the body clear of walls, from the front, using the contact's own swept test. It also refuses a skip where the chord, at the point standing in for a skipped waypoint, has less clearance than that waypoint had by more than the half tile the corner lattice cannot resolve, because the search paid for the middle of a corridor through its edge cost and a chord from one wall-hugging end of a corridor to the other is clear of walls while spending its whole length nearer them. The first smoother did exactly that and undid the search, and the corridor fixture's smoothed row is what refuses it now.

## Arrival, replanning, and what the navigator reports

Arrival is a radius around the goal, reserved inside every region the positioner admits a destination against, so a spot on a region's boundary cannot be arrived at outside it. Inside the radius the navigator asks for nothing and drops its route; the motor's brake decays the momentum, and the body settles inside the radius rather than coasting out and replanning — `Tools/NavReplay`'s arrival row drives that tick by tick and holds it for a hundred and twenty ticks on one search. A goal that drifts a little re-aims the last segment; one that moves materially cancels the attempt and plans afresh.

A route is re-planned when the world is edited under it (the route keeps the box it was planned over and asks the edit record), when the body's immunities no longer match the ones it was planned under, when the segment ahead is no longer clear for the body, or when net displacement over a window stays under a floor — two such windows on one goal are two strikes, and the brain hands the goal back to the positioner, which bans the spot for a while. The status names the difference between a route being flown, a search still pending with the body following the route it had or steering straight where the line is clear, a proven absence, and arrival; every attempt ends completed, cancelled, pre-empted or failed, and the census counts places asked for against places reached so the record reads "asked for forty places, reached eleven" rather than a minute of following as thousands of requests.

## Traps

- `OrbPace` is written by the motor every tick from the live player and defaults to the fallback pace in `Selection/BehaviourWeights.cs`; a headless tool that never writes it runs the body a plain player would produce, and a fixture that changes the pace mid-run changes the braking distance the law reads next tick.
- The lookahead is the point drawn gold on the overlay and written to the record, and it is a steering target and not a waypoint: a body steering at it is often not heading at any point on the route.
- `AvoidThreats` interrupts the route as pre-empted; a caller that dodges every tick a threat is merely near is a caller that never keeps a route.
