# Brain — how the companion behaves

Everything about how the companion decides and what it does when it has decided lives here, in two halves that must not be confused. The **decision matrix** is the machinery that turns the world into a choice: senses, scoring, positioning, navigation, reflexes. It knows nothing about ore or trees or bows. **Actions** are the things the companion can choose to do, one file each, grouped by kind so a new one has an obvious home. **Work** and **Aiming** are how a chosen action is carried out: the tools that swing an axe, mine a vein, hold a torch, solve an arc. Nothing here draws or touches health; that is `../Companion/`. The weapons themselves are equipment and stay in `../Combat/`.

```
Brain/
├─ CLAUDE.md
├─ Brain.cs            the tick order; holds one of each decision-matrix part and the last request, for the overlay and telemetry
├─ DecisionMatrix/     how a choice is made, independent of what the choices are
│  ├─ Senses/          the world model and the derived numbers (danger, horizon, light); never decides
│  ├─ Decision/        the chooser, the considerations, the weights, position requests
│  ├─ Positioning/     where to stand, as a score over candidate tiles
│  ├─ Navigation/      how to get there: grid, A*, path following, reachability
│  └─ Reflexes/        the fast path that skips scoring: dodge-jump, step-back
├─ Actions/            what can be chosen, by family; each scores itself and asks for a spot
│  ├─ Companionship/   being with the player: walk-with, guard, wander
│  ├─ Combat/          hunt, kite
│  ├─ Gathering/       loot
│  └─ Work/            doing what the player does: chop, mine
├─ Work/               the tools an action drives once chosen
│  ├─ Chopping/        trees and the axe
│  ├─ Mining/          ores and the pickaxe
│  └─ Torch/           the torch in the dark
├─ Aiming/             the arc solver every ranged weapon and the positioner share
└─ Debug/              the overlay that shows all of the above
```

## Where a new thing goes

A new fact about the world is a field on a sense in `DecisionMatrix/Senses/`, computed once. A new thing the companion can do is a `CompanionAction` in the `Actions/` family it belongs to, with a score, a forecast and a position request; a new family (opportunistic tasks, missions) is a new folder beside the four, and the chooser's list is the only other place it is named. The tool that action drives is a class under `Work/` in its own subfolder. A new reason to prefer one spot over another is a factor in `DecisionMatrix/Positioning/`. A new kind of move (a grapple, a mount) is an edge kind in `DecisionMatrix/Navigation/`. A number you want to tune is in `DecisionMatrix/Decision/Weights.cs` and nowhere else.

The tick order in `Brain.cs`: senses read the world, reflexes may take the body, the chooser scores every action and runs the best, the action asks the positioner for a spot, the navigator walks there. A sequence like kill, loot, return is three actions winning in turn because the safety horizon lets them, never a state machine across jobs.

## Traps

- **An action that computes a world fact itself is wrong even if it works**, because the next action computes it differently; put it on a sense.
- **The horizon is float.MaxValue with no threats.** Any arithmetic on it must handle that, or a forecast compared against it overflows into nonsense.
- **Scores are 0..1 and considerations multiply.** A consideration returning 0 vetoes; use `Consideration.AtLeast` when an action should stay eligible.
- **`Senses` and `Reflexes` are both a namespace and a class.** From outside `DecisionMatrix` write `DecisionMatrix.Senses.Senses`; inside it the short form resolves.
