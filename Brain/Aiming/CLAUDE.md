# Aiming — the arc solver

```
Aiming/
├─ CLAUDE.md
└─ TrajectoryAimer.cs   WeaponProfile (speed, straight ticks, gravity, cap) and Solve: sweep launch angles flattest first, simulate the arc against solid tiles with target lead, return the first that lands
```

Shared by every ranged weapon in `../../Combat/Weapons/` and by the positioner, which asks it whether a candidate spot has a line of fire. It is in the brain because it is how the companion aims, whatever it holds; the weapons are what it holds.
