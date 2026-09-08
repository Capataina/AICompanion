# Body — one arithmetic for the simulated body and the real one

The body's numbers and shape tests live here and nowhere else: the motor in `../../../../Companion/` forwards to them, the planner simulates with them, the reflexes roll dodges out with them. A constant that existed in two places was the first navigation bug class (a jump the planner found and the body could not fly), and one home is the fix.

```
Body/
├─ CLAUDE.md
└─ BodyPhysics.cs    the body's numbers (20 by 42, the walk speed, the jump velocity, acceleration, slowdown, NPC gravity), StepVelocity (the player's shape: a small gain per tick up to the walk speed, a larger loss when stopping or reversing, ground and air alike), SteerToward (the in-air steering rule the follower and the jump simulation share), SimulateJump (the follower's jump tick by tick against the shapes, returning the landing pose and the flight time), and the shape tests: Fits (the rectangle overlaps no solid, half blocks and slope triangles included), RestBottom (the highest surface under a span, a platform top counted), Stand (a pose with the feet in a tile, trying a few sideways offsets), CanSlide (the body fits all along the line between two poses, riding the ground where it is higher than the line)
```

A pose is a left edge and a bottom, nothing else; the body is a rectangle of fixed size and a tile is a shape, so "does it fit" is rectangle against triangles and "where does it rest" is the highest surface under the span, which for a floor slope is the diagonal under the body's near edge, the game's own rule. The motor's step-up and step-down are the game's and run after the brain each tick, so a one-tile kerb is walked and never jumped; the simulation has no step-up yet, which is why a jump whose landing is one tile off is refused rather than stepped onto.
