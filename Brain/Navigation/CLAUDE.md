# Navigation — getting there

```
Navigation/
├─ CLAUDE.md
├─ NavGrid.cs        what a node is (a feet tile the 1×3 body can stand on) and the jump/drop limits
├─ AStar.cs          the search; neighbours generated on the fly: walk, step, drop to first standable, jump within reach
├─ NavPath.cs        the steps, first first, with the move kind per step
├─ Navigator.cs      plans on goal change or every 30 ticks or when stuck, follows the path through the motor, falls back to straight walking
└─ Reachability.cs   can a walker/flyer get from A to B; bounded searches that answer "yes" when the budget runs out
```

Jump reach (`JumpHeightTiles` 5, `JumpGapTiles` 4) is derived from the motor's jump velocity and NPC gravity, not measured in play; the first navigation playtest should confirm a 5-tile step is climbed and a 4-tile gap cleared, and lower the numbers if not. The grid never plans through tiles: no digging, no building, by ruling.

## Traps

- **The body is 20 px wide, the grid is one tile wide.** The motor drifts across tile columns; the follower advances a step when the feet are within 10 px, so a path is a suggestion the motor approximates.
- **A jump edge only checks the apex column and the landing.** A low ceiling mid-arc is not detected; the follower's stuck counter (40 ticks) forces a replan when that bites.
- **Budget exhaustion in Reachability means reachable.** A cave full of enemies far from the player costs the walker budget per enemy per second.
