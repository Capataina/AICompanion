# Following — performing the path the planner proved

The navigator takes a feet position each tick from the brain, plans through `../Planning/` when the goal or the cadence says so, and drives the motor along the path one step at a time. It is the only part of navigation that touches the motor, and the motor is the only part of the mod that names the NPC's fields, which is the boundary that keeps the planner replayable.

```
Following/
├─ CLAUDE.md
└─ Navigator.cs      plans on goal change or on a cadence or when stuck, follows the path through the motor, falls back to straight walking; a jump step is made as planned, settling onto the take-off for a standing jump and backing away for a run-up when the profile's speed is missing (FollowJump, Runway); a walk step jumps only for a wall two tiles high read from the tiles, or a rise of two or more; a drop or fall-through step steers to the plan's own steer line; a fall-through step sets the motor's one-tick flag; a partial path is followed and still counted as a failed plan; a stuck replan prices the step it stood at for a while and counts a strike, and the brain reads two strikes as a spot to refuse; every failed plan asks the telemetry to dump the tile window
```

The rule the follower lives by is that a step is performed the way it was proved: the same steering function, the same start speed, the same line down the opening. Every stretch where the body stood with a path in hand (runs 3, 5, 6 and 7 of 2026-09-08) was a step performed by a different rule than the one that proved it, which is why the traversal work under AIC-177 moves both halves of each move into one object and leaves this file's class with the cadence, the strikes and the fallback.
