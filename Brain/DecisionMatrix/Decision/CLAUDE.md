# Decision — utility scoring

`Chooser.cs` asks every action in `../../Actions/` for a score, multiplies the incumbent by `Weights.Commitment`, charges any action whose `ForecastTicks` exceeds the senses' horizon (falling to zero over `Weights.HorizonOverrunToZero` ticks of overrun), and runs the winner. `LastScores` is kept for the overlay.

```
Decision/
├─ CLAUDE.md
├─ Chooser.cs          the scorer and the action list, the one place every action family is named
├─ Consideration.cs    named curves: Inverse, Rising, Band, Step, AtLeast
├─ Weights.cs          every tunable number of the brain
└─ PositionRequest.cs  what an action asks the positioner for: WithPlayer, Guard, LineOfFire, Exact, Retreat, Hold
```

The actions themselves live in `../../Actions/`, grouped by family; the chooser imports each family's namespace and lists its instances. Adding a family means one `using` and its entries here, nothing else.
