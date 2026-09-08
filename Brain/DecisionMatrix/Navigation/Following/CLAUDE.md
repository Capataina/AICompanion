# Following — performing the path the planner proved

The navigator takes a body state and a feet position each tick and returns the controls for that tick; it plans through `../Planning/` when the goal or the cadence says so and hands each step of the path to the traversal in `../Traversals/` that proved it. It names nothing of the game: the motor in `../../../../Companion/` builds the body state from the NPC and applies the controls to it, and the replay tool builds the state from a pose and applies the controls with `BodyMotion.Step`, so the navigator that runs in the game is the navigator the replay's `--follow` runs, and a failed plan reaches the telemetry through the `PlanFailed` seam that the brain wires and the tool leaves empty.

```
Following/
├─ CLAUDE.md
└─ Navigator.cs      MoveTo(BodyState, target) → Controls: plans on goal change, on a cadence, when stuck or after a fault, from the ground only; advances past every step its traversal says is done; asks the current step's traversal to check and steer; a fault prices the step, counts a strike and replans on the next ground tick while the body asks for nothing; a stuck body (not moved for a while with a path) prices its step and counts a strike too; two strikes are what the brain reads as a spot to refuse; falls back to straight walking with no path; every step that finishes or faults is an EdgeReport (kind, from, to, ticks proven, ticks taken, outcome) the telemetry writes and the replay prints
```

The rule the follower lives by is that a step is performed the way it was proved, and the follower itself no longer knows how any step is performed: that knowledge is the traversal's, both halves of it. What stays here is the cadence, the strikes, the fallback and the report of what each step cost against what it was proven to cost, which is the trace the game and the simulation are read against each other by.
