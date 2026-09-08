# Decision matrix — how a choice is made

The five parts that turn the world into a decision, none of which knows what the decisions are about. An action names an ore; nothing in here does. That separation is what lets a new family of actions arrive without touching any of this.

```
DecisionMatrix/
├─ CLAUDE.md
├─ Senses/        the world model: player, threats, loot, light, tile hits; derives danger and the safety horizon
├─ Decision/      the utility chooser, the consideration curves, every tunable weight, the position request an action returns
├─ Positioning/   a position request becomes a feet position by scoring candidate tiles
├─ Navigation/    the grid, A*, the path follower, reachability for threats
└─ Reflexes/      the pre-scoring fast path: simulated dodges against predicted hitboxes
```

Data flows one way: senses → (reflexes) → decision → positioning → navigation. A part reading from a later part is wrong. The one thing that travels back up is a fact about the body from last tick's outcome, the brain's stranded flag on the action context, which decision reads the way it reads the breath: as a state the body is in, never as a decision navigation made.
