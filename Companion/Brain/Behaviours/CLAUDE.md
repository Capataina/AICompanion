# Behaviours — what the companion can be doing, by family

One file per action, grouped by the kind of thing it is, so the list never becomes a flat pile. The contract is `CompanionAction.cs` here: `Score` (a product of considerations in 0..1, zero for "not now"), `ForecastTicks` (how long it keeps the companion away from the player), `Enter`/`Exit`, and `Execute`, which acts (fire, swing, set the held item) and returns a `PositionRequest`; an action never moves the NPC, the navigator does. Internal steps stay as private state on the action.

```
Behaviours/
├─ CLAUDE.md
├─ CompanionAction.cs   the base and ActionContext (the companion, observations, player, and stranded fact)
├─ Survival/            the body's own rescue: survive, which is the one action that scores above the scale
├─ Companionship/       being with the player: walk-with, guard, wander
├─ Combat/              going after or backing off from enemies: hunt, kite
├─ Gathering/           picking things up: loot
└─ Work/                doing what the player is doing: chop, mine
```

## Adding an action, or a family

`IsExcursion` declares whether an action is optional time away from the player. It defaults to true, so newly added opportunistic actions automatically yield to regroup pressure. Following, protection, kiting and self-rescue opt out because they maintain companionship or immediate safety. Independent weapon firing remains outside the movement competition; a follow request can still shoot while travelling.

A behaviour joins the family it belongs to and is listed once in `../BehaviourSelection/ChooseBehaviour.cs`; list order is only the overlay's order, ties are decided by score. Behaviours are deliberately opportunistic: they follow the player loosely and act on what is already happening nearby. Player-directed missions were abandoned; mastery may later unlock movement abilities such as air jumps, dash, swimming and flight, but none is built. The tool a work behaviour drives lives in `../WorldInteractions/`, never inside the behaviour.
