# Brain — how the companion decides

The companion's mind, as four parts plus reflexes, ticked in one fixed order by `Brain.cs`: senses read the world, reflexes may take the body, the chooser scores every action and runs the best, the action asks the positioner for a spot, the navigator walks there. Nothing here draws or touches health; that is `../Companion/`. Nothing here is a state machine across jobs: a sequence like kill, loot, return is three actions winning in turn because the safety horizon lets them.

```
Brain/
├─ CLAUDE.md
├─ Brain.cs            the tick order; holds one of each part and the last request, for the overlay and telemetry
├─ Senses/             the world model and the derived numbers (danger, horizon); never decides
├─ Decision/           the chooser, the considerations, the weights, and the actions it scores
├─ Positioning/        where to stand, as a score over candidate tiles
├─ Navigation/         how to get there: grid, A*, path following, reachability
├─ Reflexes/           the fast path that skips scoring: dodge-jump, step-back
└─ Debug/              the overlay (key left of 1) that shows all of the above
```

## Where a new thing goes

A new fact about the world is a field on a sense in `Senses/`, computed once. A new thing the companion can do is a `CompanionAction` in `Decision/Actions/` with a score, a forecast and a position request. A new reason to prefer one spot over another is a factor in `Positioning/Positioner.cs`. A new kind of move (a grapple, a mount) is an edge kind in `Navigation/`. A number you want to tune is in `Decision/Weights.cs` and nowhere else.

## Traps

- **An action that computes a world fact itself is wrong even if it works**, because the next action computes it differently; put it on a sense.
- **The horizon is float.MaxValue with no threats.** Any arithmetic on it must handle that, or a forecast compared against it overflows into nonsense.
- **Scores are 0..1 and considerations multiply.** A consideration returning 0 vetoes; use `Consideration.AtLeast` when an action should stay eligible.
