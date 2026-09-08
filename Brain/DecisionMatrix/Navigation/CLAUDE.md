# Navigation — getting there

```
Navigation/
├─ CLAUDE.md
├─ NavGrid.cs        what a node is (a feet tile the 1×3 body can stand on, with or without lava allowed in the column), liquid, submerged-head, lava-count and platform-underfoot tests, and the jump/drop limits
├─ AStar.cs          the search; neighbours generated on the fly: walk, step, drop to first standable, fall through the platform underfoot, jump within reach; from liquid every edge costs double and the jump envelope is halved; arriving with the head under water multiplies again; each lava tile in a node adds a flat price, and lava nodes exist only while AllowLava is true (the brain sets it from the companion's life each tick)
├─ NavPath.cs        the steps, first first, with the move kind per step (walk, jump, drop, fall-through)
├─ Navigator.cs      plans on goal change or on a cadence or when stuck, follows the path through the motor, falls back to straight walking; jumps only for a wall two tiles high read from the tiles, or a rise of two or more; a fall-through step sets the motor's one-tick flag
└─ Reachability.cs   can a walker/flyer get from A to B; bounded searches that answer "yes" when the budget runs out
```

Jump reach is derived from the motor's jump velocity and NPC gravity, not measured in play; a navigation playtest should confirm the tallest step is climbed and the widest gap cleared, and lower the limits if not. The grid never plans through tiles: no digging, no building, by ruling. Water and lava are priced rather than banned, by Caner's ruling on 2026-09-08 (a player crosses a pool, and jumps a lava stream to escape two zombies): the price shapes are in `AStar.Price`, and the one thing not priced yet is breath along a path, so a submerged route longer than the breath is planned like any other and the survive action in `../../Actions/Survival/` is the backstop.

## Traps

- **A one-tile rise is a step, never a jump.** The motor runs the game's `Collision.StepUp`/`StepDown` after the brain each tick, as the fighter AI does. The follower jumps only for rises of two tiles or more (at the fighter AI's heights: -6, -7, -8, then the full jump) or for a wall, and a wall is read from the tiles, solid at the feet row and the row above in the walking direction, never from `collideX` alone: the game sets that flag for a kerb on the tick it is met, before StepUp lifts the body, so a follower that trusted the flag still hopped every kerb after the StepUp fix (second run, 2026-09-08, "frantic jumping"). Straight-line walking uses the same wall test.
- **Water is not air to the body.** The game halves a wet NPC's movement, so a jump edge planned from inside a pool reaches about half its envelope and the follower jumps in place under the ledge for as long as the plan says jump (second run, 2026-09-08, the companion stuck at the pool's edge). Edges from liquid cost double and the jump envelope from liquid is halved, so the search walks out along the floor where it can.
- **A platform is a ceiling to a grid with no fall-through edge.** The companion waited above a shaft capped with platforms (second run, 2026-09-08). The fall-through edge and the NPC's `CanFallThroughPlatforms` hook, set for one tick by the follower, are what let it press "down".
- **The body is 20 px wide, the grid is one tile wide.** The motor drifts across tile columns; the follower advances a step when the feet are within 10 px, so a path is a suggestion the motor approximates.
- **A jump edge only checks the apex column and the landing.** A low ceiling mid-arc is not detected; the follower's stuck counter (40 ticks) forces a replan when that bites.
- **Budget exhaustion in Reachability means reachable.** A cave full of enemies far from the player costs the walker budget per enemy per second.
