# Work actions — nearby jobs selected by policy

```
Work/
├─ CLAUDE.md
├─ ChopAction.cs   nearby trees under mimic or opportunistic policy, with a cached reachable approach
├─ MineAction.cs   a retained ore-only vein job, triggered by the selected mining policy and resumed after interruption
├─ PerformNearbyWorldWork.cs bounded discovery and safe interaction jumps for pots and permanent torches
└─ WorkPolicies.cs  Disabled/Mimic/Opportunistic readers backed by per-character preferences
```

Mimic triggers come from the tile damage watcher in `../../WorldObservation/`, which sees real axe and pickaxe hits through the game's KillTile hook. Opportunistic triggers discover nearby resources without requiring a player hit. Both read the player's held tool's numbers, with a basic tool fallback. The work itself is in `../../WorldInteractions/Chopping/` and `../../WorldInteractions/Mining/`.

Mining separates its policy trigger from its retained job. Mimic starts only while the player has recently hit ore; opportunistic can discover ore near the player or companion. Once a job exists it holds the bounded same-type vein across guard and self-defence action switches, revalidates tile type, native pick damage, line of sight and approach on every resume, then relocates to the next reachable tile. A route query that proves no approach ends the reachable portion; a bounded `Unknown` stays retryable and never becomes a permanent inaccessible verdict or suppresses another useful action while waiting. Discovery filters native pick damage before ranking distance, so a weak-pick ore does not hide a farther usable one. The job exposes its id, policy, status, remaining-tile count and current target/stand for diagnostics. It does not target dirt or carve a route.

Chopping uses the same policy names through `TreeFinder` and `TileChopper`. Mimic retains the recent-hit window and avoids the player's active tree. Opportunistic work retains a trunk through protective interruptions, but releases one outside its allowance or inside a protected home. Failed or unfinished approaches are deferred briefly so one tree cannot permanently mask local work. Resource admission measures the trunk rather than whichever side supplies its standing point.

Mining uses actual tool reach to decide whether to swing. A navigator's approximate arrival tolerance can leave the body outside that reach; returning Hold from that pose would prevent the movement needed to make mining possible. Retained vein pruning uses the same actor-and-target distance contract as discovery, and a removed target cannot survive merely because other vein tiles remain.

Pots and permanent torches share one bounded candidate/approach executor. Pot discovery accepts the native pot tile family and delegates destruction/drops to the engine. Torch candidates use the game's torch Smart Cursor rules through `WorldInteractions/Torch/`, rank elevated left/right sites, and may use a ground jump proven to reach the interaction and land safely. An interruption invalidates a prepared interaction jump. Both recheck home protection at the actual mutation, so discovery permission is never treated as permission to edit a changed world.
