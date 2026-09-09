# Behaviour selection — utility scoring

`ChooseBehaviour.cs` asks every behaviour in `../Behaviours/` for a score, multiplies the incumbent by `BehaviourWeights.Commitment`, charges any behaviour whose forecast exceeds the observed threat horizon, and runs the winner. `LastScores` remains for diagnostics and the overlay. It receives world facts from `../WorldObservation/` and returns a `PositionRequest` to `../PositionSelection/`; it neither selects a tile nor controls the body.

```
BehaviourSelection/
├─ CLAUDE.md
├─ ChooseBehaviour.cs          the scorer and the sole list of behaviour instances
└─ EvaluateConsiderations.cs   named scoring curves
```

## The urgency ladder, and why an action topping out at one can never interrupt anything

Regrouping is pressure on existing utility scores, not another behaviour. The chooser derives return time from separation and the remaining player-directed route, then combines it with the player's movement away and recent lack of body progress. Optional excursions multiply their score by the remaining freedom to stay away; `IsExcursion` defaults on so new discretionary behaviours participate automatically. Survival, protection, kiting and following opt out. Following's own score rises with the same return pressure, while the hands keep shooting independently. `RegroupUrgency` and `EstimatedReturnTicks` remain available to diagnostics.

An ordinary action scores a product of considerations in 0..1, and the chooser multiplies the incumbent by `Weights.Commitment`. So a running action does not sit at its own score, it sits above it, and an action that has to *interrupt* one has to clear that product rather than merely reach the top of the band. An action whose raw expression cannot exceed 1 therefore loses to a committed ordinary action by construction, on every tick, at every input — not narrowly and not on a weight. Guard was that action, which is why a following companion stood holding a torch with the player's danger reading full, and why turning the danger term up was never going to move it.

The answer is a ladder rather than three independent ceilings. `Weights.GuardUrgency` is scaled to clear a committed ordinary action, and `Weights.SurviveUrgency` to clear a committed guard, so each rung is sized against the rung below it *including* the bonus that rung carries as the incumbent. The constants live beside each other in `Weights.cs` with the arithmetic in their doc comment and they move together: raising one without the one above it loses a rung, and the failure is silent — no crash and no wrong number in any column, just an action that never wins again.

A fourth action that must interrupt guard is placed against these two, never given a constant of its own. What was rejected is a priority tier or an interrupt flag on the chooser, which would express this directly and reintroduce the priority chain the whole design exists to avoid: a tier says guard is more important than following, where a score says guard is worth this much given everything at once, and only the second can weigh a nearly-dead slime against a player who is fine. Scaling keeps every action commensurable and keeps the ordering a consequence of the numbers.

**Nothing enforces the ordering, and that is a known gap rather than an oversight** (recorded 2026-09-09, unfixed): it is arithmetic in a comment, so a tuning session can invert a rung and the only symptom is a behaviour that stops happening. A test that scores two stub actions against each other and asserts each rung beats the committed rung below it is what would catch it.

The behaviours themselves live in `../Behaviours/`, grouped by family; the chooser imports each family's namespace and lists its instances. Adding a family means one `using` and its entries here, nothing else. The data flow is one way: observation → reflex assessment or selection → position selection → shared movement → motor. Movement outcomes are returned only as recorded body facts, such as being stranded, for the next selection tick.
