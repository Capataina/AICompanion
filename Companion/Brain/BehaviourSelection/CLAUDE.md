# Behaviour selection — utility scoring

`ChooseBehaviour.cs` asks every behaviour in `../Behaviours/` for a score, multiplies the incumbent by `BehaviourWeights.Commitment`, charges any behaviour whose forecast exceeds the observed threat horizon, and runs the winner. `LastScores` remains for diagnostics and the overlay. It receives world facts from `../WorldObservation/` and returns a `PositionRequest` to `../PositionSelection/`; it neither selects a tile nor controls the body.

Regroup time uses available route-step durations and the positioner's observed travel estimate as well as straight-line distance. Excursions are discounted by protection urgency; their own acquisition and continuation bounds determine whether distance permits the job. Charging them again for leaving the short follow-comfort band would defeat that permission. Interruptible work pays the time until it can yield, rather than the duration of its entire job, against the threat horizon. Unknown intervention is conservative; the companion remains autonomous while the player is dead, with player-protection pressure removed.

```
BehaviourSelection/
├─ CLAUDE.md
├─ ChooseBehaviour.cs          the scorer and the sole list of behaviour instances
└─ EvaluateConsiderations.cs   named scoring curves
```

## The urgency ladder, and why an action topping out at one can never interrupt anything

Regrouping raises following's existing utility rather than introducing a behaviour. It uses the selected distance mode's comfort region, and drops to zero when both axes and local sight satisfy arrival. A viable targeted excursion discounts ordinary following inside its permitted activity envelope; protection and survival are unaffected. `IsExcursion` defaults on, with survival, protection, kiting and following opting out. `RegroupUrgency` and `EstimatedReturnTicks` remain available to diagnostics.

The selected job earns continuation by identity on every winning tick, including consecutive targets inside one behaviour. A moving enemy or drop keeps its identity as its position changes; a different entity must pass acquisition again. Mining retains a vein id, chopping a trunk tile, and small world interactions a target tile. Successful work also records a short-lived collection site so the resulting drops can be gathered under the same ongoing allowance. The shared bounds check both the companion and its target against the player.

An ordinary action scores a product of considerations in 0..1, and the chooser multiplies the incumbent by `Weights.Commitment`. So a running action does not sit at its own score, it sits above it, and an action that has to *interrupt* one has to clear that product rather than merely reach the top of the band. An action whose raw expression cannot exceed 1 therefore loses to a committed ordinary action by construction, on every tick, at every input — not narrowly and not on a weight. Guard was that action, which is why a following companion stood holding a torch with the player's danger reading full, and why turning the danger term up was never going to move it.

The answer is a ladder rather than three independent ceilings. `Weights.GuardUrgency` is scaled to clear a committed ordinary action, and `Weights.SurviveUrgency` to clear a committed guard, so each rung is sized against the rung below it *including* the bonus that rung carries as the incumbent. The constants live beside each other in `Weights.cs` with the arithmetic in their doc comment and they move together: raising one without the one above it loses a rung, and the failure is silent — no crash and no wrong number in any column, just an action that never wins again.

A fourth action that must interrupt guard is placed against these two, never given a constant of its own. What was rejected is a priority tier or an interrupt flag on the chooser, which would express this directly and reintroduce the priority chain the whole design exists to avoid: a tier says guard is more important than following, where a score says guard is worth this much given everything at once, and only the second can weigh a nearly-dead slime against a player who is fine. Scaling keeps every action commensurable and keeps the ordering a consequence of the numbers.

`Tools/EngineReplay/VerifyFollowRecoveryAndProtection.cs` exercises immediate guard entry against committed following, continued protection through a small retreat, and maximum survival against committed protection. These checks constrain the intended ordering while leaving ordinary choices to utility scores; they do not prove every combination of threat and action factors.

The behaviours themselves live in `../Behaviours/`, grouped by family; the chooser imports each family's namespace and lists its instances. Adding a family means one `using` and its entries here, nothing else. The data flow is one way: observation → reflex assessment or selection → position selection → shared movement → motor. Movement outcomes are returned only as recorded body facts, such as being stranded, for the next selection tick.
