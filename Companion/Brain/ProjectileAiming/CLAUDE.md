# Aiming — the arc solver

```
Aiming/
├─ CLAUDE.md
└─ TrajectoryAimer.cs   WeaponProfile (speed, straight ticks, gravity, cap); Solve sweeps launch angles flattest first and simulates the arc against solid tiles with target lead, returning the first that lands; PathHits re-flies a solved launch and reports every hostile it passes through, in order
```

Shared by every ranged weapon in `../../Weapons/` and by the positioner, which asks it whether a candidate spot has a line of fire. It is in the brain because it is how the companion aims, whatever it holds; the weapons are what it holds.

Two questions are asked of a shot and they are different. `Solve` asks whether the target can be hit at all and returns the launch velocity that does it, flattest arc first so a clear shot is a straight shot and a lob is a last resort. `PathHits` takes that launch and flies it again to ask what *else* is in the way — the bodies the arc actually crosses, in the order it reaches them — which is what a piercing weapon is worth. Counting hostiles near the target instead would be wrong in both directions: two enemies abreast of the muzzle are on the arc and two abreast of the target may be behind a wall the arc stops at.

Both walks step through one private `Advance`, which is the whole of the projectile physics. That is deliberate rather than tidy: this project has produced four defects from a mechanism being modelled one way in the code that decides and another way in the code that acts, so an arc that is proven and an arc that is counted are the same arc by construction.

`PathHits` runs over a hostile list the caller supplies rather than scanning every NPC slot, because it is asked once per weapon per target while the brain is deciding, and a scan of the world inside a per-tick flight loop is a few tens of thousands of rectangle tests a frame.
