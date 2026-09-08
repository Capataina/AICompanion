# Actions — what the companion can be doing, by family

One file per action, grouped by the kind of thing it is, so the list never becomes a flat pile. The contract is `CompanionAction.cs` here: `Score` (a product of considerations in 0..1, zero for "not now"), `ForecastTicks` (how long it keeps the companion away from the player), `Enter`/`Exit`, and `Execute`, which acts (fire, swing, set the held item) and returns a `PositionRequest`; an action never moves the NPC, the navigator does. Internal steps stay as private state on the action.

```
Actions/
├─ CLAUDE.md
├─ CompanionAction.cs   the base and ActionContext (the companion, the senses, the player)
├─ Companionship/       being with the player: walk-with, guard, wander
├─ Combat/              going after or backing off from enemies: hunt, kite
├─ Gathering/           picking things up: loot
└─ Work/                doing what the player is doing: chop, mine
```

## Adding an action, or a family

An action joins the family it belongs to and is listed once in `../DecisionMatrix/Decision/Chooser.cs`; list order is only the overlay's order, ties are decided by score. A new family (opportunistic tasks that fire on sight, missions the player orders) is a new folder beside these four with its own `CLAUDE.md`, its namespace imported in the chooser. The tool an action drives is under `../Work/`, never inside the action.
