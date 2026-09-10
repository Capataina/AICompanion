# Work actions — nearby jobs selected by policy

```
Work/
├─ CLAUDE.md
├─ ChopAction.cs   nearby trees under mimic or opportunistic policy, with a cached reachable approach
├─ MineAction.cs   a retained ore-only vein job, triggered by the selected mining policy and resumed after interruption
└─ WorkPolicies.cs  runtime Disabled/Mimic/Opportunistic policy for mining and chopping; opportunistic is the default while UI and persistence remain elsewhere
```

Mimic triggers come from the tile damage watcher in `../../WorldObservation/`, which sees real axe and pickaxe hits through the game's KillTile hook. Opportunistic triggers discover nearby resources without requiring a player hit. Both read the player's held tool's numbers, with a basic tool fallback. The work itself is in `../../WorldInteractions/Chopping/` and `../../WorldInteractions/Mining/`.

Mining separates its policy trigger from its retained job. Mimic starts only while the player has recently hit ore; opportunistic can discover ore near the player or companion. Once a job exists it holds the bounded same-type vein across guard and self-defence action switches, revalidates tile type, native pick damage, line of sight and approach on every resume, then relocates to the next reachable tile. A route query that proves no approach ends the reachable portion; a bounded `Unknown` stays retryable and never becomes a permanent inaccessible verdict or suppresses another useful action while waiting. Discovery filters native pick damage before ranking distance, so a weak-pick ore does not hide a farther usable one. The job exposes its id, policy, status, remaining-tile count and current target/stand for diagnostics. It does not target dirt or carve a route.

Chopping uses the same policy names through the existing `TreeFinder` and `TileChopper` path. Mimic retains the established recent-hit window and avoids the player's active tree; opportunistic begins from a nearby real tree and keeps the action's normal target lifetime. It has no second chopping tool or speculative settings surface.
