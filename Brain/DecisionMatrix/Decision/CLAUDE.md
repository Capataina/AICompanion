# Decision — utility scoring

`Chooser.cs` asks every action in `../../Actions/` for a score, multiplies the incumbent by `Weights.Commitment`, charges any action whose `ForecastTicks` exceeds the senses' horizon (falling to zero over `Weights.HorizonOverrunToZero` ticks of overrun), and runs the winner. `LastScores` is kept for the overlay.

```
Decision/
├─ CLAUDE.md
├─ Chooser.cs          the scorer and the action list, the one place every action family is named
├─ Consideration.cs    named curves: Inverse, Rising, Band, Step, AtLeast
├─ Weights.cs          every tunable number of the brain
└─ PositionRequest.cs  what an action asks the positioner for: WithPlayer, Guard, LineOfFire, Exact, Retreat, Hold, Roam
```

## The urgency ladder, and why an action topping out at one can never interrupt anything

An ordinary action scores a product of considerations in 0..1, and the chooser multiplies the incumbent by `Weights.Commitment`. So a running action does not sit at its own score, it sits above it, and an action that has to *interrupt* one has to clear that product rather than merely reach the top of the band. An action whose raw expression cannot exceed 1 therefore loses to a committed ordinary action by construction, on every tick, at every input — not narrowly and not on a weight. Guard was that action, which is why a following companion stood holding a torch with the player's danger reading full, and why turning the danger term up was never going to move it.

The answer is a ladder rather than three independent ceilings. `Weights.GuardUrgency` is scaled to clear a committed ordinary action, and `Weights.SurviveUrgency` to clear a committed guard, so each rung is sized against the rung below it *including* the bonus that rung carries as the incumbent. The constants live beside each other in `Weights.cs` with the arithmetic in their doc comment and they move together: raising one without the one above it loses a rung, and the failure is silent — no crash and no wrong number in any column, just an action that never wins again.

A fourth action that must interrupt guard is placed against these two, never given a constant of its own. What was rejected is a priority tier or an interrupt flag on the chooser, which would express this directly and reintroduce the priority chain the whole design exists to avoid: a tier says guard is more important than following, where a score says guard is worth this much given everything at once, and only the second can weigh a nearly-dead slime against a player who is fine. Scaling keeps every action commensurable and keeps the ordering a consequence of the numbers.

**Nothing enforces the ordering, and that is a known gap rather than an oversight** (recorded 2026-09-09, unfixed): it is arithmetic in a comment, so a tuning session can invert a rung and the only symptom is a behaviour that stops happening. A test that scores two stub actions against each other and asserts each rung beats the committed rung below it is what would catch it.

The actions themselves live in `../../Actions/`, grouped by family; the chooser imports each family's namespace and lists its instances. Adding a family means one `using` and its entries here, nothing else.
