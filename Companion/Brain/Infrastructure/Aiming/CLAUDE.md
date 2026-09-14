# Projectile aiming — the arc solver

```
Aiming/
├─ CLAUDE.md
└─ SolveProjectileTrajectory.cs   vanilla-motion profiles, one flight step, solved impact facts and swept terrain/target traces
```

Shared by every ranged weapon in `../../Weapons/` and by the positioner, which asks it whether a candidate spot has a line of fire. It is in the brain because it is how the companion aims, whatever it holds; the weapons are what it holds.

Two questions are asked of a shot and they are different. `Solve` asks whether the target can be hit at all and returns the launch velocity that does it, flattest arc first so a clear shot is a straight shot and a lob is a last resort. `PathHits` takes that launch and flies it again to ask what *else* is in the way — the bodies the arc actually crosses, in the order it reaches them — which is what a piercing weapon is worth. Counting hostiles near the target instead would be wrong in both directions: two enemies abreast of the muzzle are on the arc and two abreast of the target may be behind a wall the arc stops at.

`ProjectileFlight.Advance` is the one motion step used by solve, pierce scoring and final firing validation. A profile carries the native projectile's AI phase at which gravity begins, vertical gravity, horizontal drag, terminal fall speed and hitbox. The arrow and knife profiles are checked against Terraria's `VanillaAI` in EngineReplay; the knife is not a generic thrown object: it has an opening straight phase, then gains gravity and drag. A candidate translation is sampled across its whole distance with `Collision.SolidCollision`, so a fast projectile cannot tunnel past a thin tile merely because both endpoints are clear.

**A shot can be asked for now or for a number of ticks from now, and the positioner asks for later.** `TrySolve` carries a tick-offset overload, because a firing stand is chosen now and stood on after a walk, and an arc proved against the target's present centre is a proof about a moment that will have passed by the time the body arrives. The offset is a *time*, not a displacement, and that distinction is the whole reason the overload exists rather than the caller shifting the target box itself: the motion forecast is terrain-constrained, so a straight-line shift equals it only while the motion happens to be straight. Every interception test inside the trace is taken at the offset plus the flight tick, so the flight lead the aimer already applies survives on top of the wait.

Where the forecast carries too little measured confidence — no error samples, or a trip long enough that the measured continuation has decayed past its floor — the current position is asked instead and the answer records which was used, because refusing a stand on an unmeasured guess is worse than the staleness it replaces. The limitation is worth knowing rather than rediscovering: **on a long walk the forecast is not confident, so a far stand is still judged on where the target is now**, and the rule therefore bites on short and middling trips only.

`TrySolve` returns a `TrajectorySolution`, including the launch, actual predicted impact point and impact tick. `TryTrace` reuses the same walk after accuracy noise rotates a launch; the arsenal fires only if this concrete rotated launch still reaches the target through clear terrain. This preserves imperfect aim without turning an earlier proof into permission for a different, unsafe trajectory. Target boxes come from `WorldObservation.PredictObservedMotion`, the same terrain-constrained observed-motion forecast used by threat records; projectile aiming does not carry a second enemy-physics model.

`PathHits` runs over a hostile list the caller supplies rather than scanning every NPC slot, because it is asked once per weapon per target while the brain is deciding, and a scan of the world inside a per-tick flight loop is a few tens of thousands of rectangle tests a frame.
