# Work actions — nearby jobs selected by policy

Mining and chopping animate accepted native swings, but credit their retained worksite only when the observed tool outcome shows increased damage or removal. They publish that same immutable outcome to the recorder. A cooldown, refused effect or changed tile frame cannot manufacture productive work credit; the native interaction owns the observation and the behaviour consumes it.

```
Work/
├─ CLAUDE.md
├─ ChopAction.cs   nearby trees under mimic or opportunistic policy, with a cached reachable approach
├─ MineAction.cs   a retained ore-only vein job, triggered by the selected mining policy and resumed after interruption
├─ PerformNearbyWorldWork.cs bounded discovery and safe interaction jumps for pots and permanent torches
└─ WorkPolicies.cs  Disabled/Mimic/Opportunistic readers backed by per-character preferences
```

Mimic triggers come from the tile damage watcher in `../../WorldObservation/`, which sees real axe and pickaxe hits through the game's KillTile hook. Opportunistic triggers discover nearby resources without requiring a player hit. Both read the player's held tool's numbers, with a basic tool fallback. The work itself is in `../../WorldInteractions/Chopping/` and `../../WorldInteractions/Mining/`.

Mining separates its policy trigger from its retained job. Mimic starts only while the player has recently hit ore; opportunistic can discover ore near the player or companion. Once a job exists it holds the bounded same-type vein across guard and self-defence action switches, revalidates tile type, native pick damage, line of sight and approach on every resume, then relocates to the next reachable tile. A route query that proves no approach ends the reachable portion; a bounded `Unknown` stays retryable and never becomes a permanent inaccessible verdict or suppresses another useful action while waiting.

**An undecided approach is answered by walking at the ore, because scoring it zero is self-fulfilling.** Whether the companion can reach an ore is a bounded search run fresh from its feet each time, keeping no frontier between calls, so a body that does not move receives the identical "could not tell" for ever — and walking closer, the only thing that shortens the search, is exactly what a zero score prevents. Mining therefore holds a discounted score while it walks at an undecided ore, renewed only while the body is actually covering ground, so the property the zero was protecting still holds: the chooser is never held by mining that is going nowhere. The discount keeps it under a proven job, so reachable ore always wins.

The ore search retains the nearest unresolved tile separately from its proven target. Mining approaches that exact candidate after rechecking ore presence, tool power, activity allowance and home protection; it never substitutes the nearest arbitrary ore for an unknown reported elsewhere. Retained-vein relocation carries the same identity. The prepared activity exposes this provisional target and travel estimate without claiming a usable stand. Discovery filters native pick damage before ranking distance, so a weak-pick ore does not hide a farther usable one. The job exposes its id, policy, status, remaining-tile count and current target/stand for diagnostics. It does not target dirt or carve a route.

Chopping uses the same policy names through `TreeFinder` and `TileChopper`. Mimic retains the recent-hit window and avoids the player's active tree. Opportunistic work retains a trunk through protective interruptions, but releases one outside its allowance or inside a protected home. Failed or unfinished approaches are deferred briefly so one tree cannot permanently mask local work. Resource admission measures the trunk rather than whichever side supplies its standing point.

Mining and chopping update discovery, approach caches and retry timers only during `Prepare`. They capture the resulting target, value and trip duration; Score and ForecastTicks read that result without advancing any timer or querying terrain. External ore removal is incorporated during preparation, not during repeated comparison. A missing prepared chopping candidate cannot execute a retained tree merely because the discovery cache still contains it.

Mining uses actual tool reach to decide whether to swing. A navigator's approximate arrival tolerance can leave the body outside that reach; returning Hold from that pose would prevent the movement needed to make mining possible. Retained vein pruning uses the same actor-and-target distance contract as discovery, and a removed target cannot survive merely because other vein tiles remain.

Pots and permanent torches share one bounded candidate/approach executor. Pot discovery accepts the native pot tile family and delegates destruction/drops to the engine. Torch candidates use the game's torch Smart Cursor rules through `WorldInteractions/Torch/`, rank elevated left/right sites, and may use a ground jump proven to reach the interaction and land safely. An interruption invalidates a prepared interaction jump. Both recheck home protection at the actual mutation, so discovery permission is never treated as permission to edit a changed world.

Their candidate search and deferred-approach cleanup run during Prepare. Comparison reads the captured value and target without invoking native torch rules or repeating a jump proof. Execution retains its own validation and progress accounting; a comparison never counts as another attempted interaction.
