# Decision — utility scoring

`Chooser.cs` asks every action in `Actions/` for a score, multiplies the incumbent by `Weights.Commitment`, charges any action whose `ForecastTicks` exceeds the senses' horizon (falling to zero over `Weights.HorizonOverrunToZero` ticks of overrun), and runs the winner. `LastScores` is kept for the overlay.

```
Decision/
├─ CLAUDE.md
├─ Chooser.cs          the scorer and the action list
├─ Consideration.cs    named curves: Inverse, Rising, Band, Step, AtLeast
├─ Weights.cs          every tunable number of the brain
├─ PositionRequest.cs  what an action asks the positioner for: WithPlayer, Guard, LineOfFire, Exact, Retreat, Hold
└─ Actions/            one file per thing the companion can be doing
   ├─ CLAUDE.md
   ├─ CompanionAction.cs        the base: Score, ForecastTicks, Enter, Exit, Execute → PositionRequest
   ├─ WalkWithPlayerAction.cs   follow the player's predicted position; hard leash at 1400 px
   ├─ GuardAction.cs            danger high: stand by the player with sight lines, shoot the most urgent threat
   ├─ HuntAction.cs             a reachable target the player is safe enough to leave for; forecasts the trip
   ├─ LootAction.cs             nearest pickup that fits somewhere; forecasts the trip
   ├─ ChopAction.cs             the player is really chopping: nearest other tree, kept 2 s past the last hit
   ├─ KiteAction.cs             a walker inside melee reach: retreat while firing
   └─ WanderAction.cs           the floor score; stand, stroll, hop inside the calm band
```

## Writing an action

Score is a product of considerations in 0..1; return 0 for "not now". Forecast the ticks the action keeps the companion away from the player (0 if it does not). In Execute, act (fire, swing, set the held item) and return the request; do not move the NPC yourself, the navigator does. Keep any internal steps as private state on the action.
