# Gathering activities — mine a vein or chop a tree

Mining and chopping animate accepted native swings, but credit their retained worksite only when the observed tool outcome shows increased damage or removal. They publish that same immutable outcome to the recorder. A cooldown, refused effect or changed tile frame cannot manufacture productive work credit; the native interaction owns the observation and the behaviour consumes it.

Tool ownership spans cooldowns only while that work is executing. Exit and suspension clear the swinging phase independently of retaining a vein or tree; otherwise an interrupted job reports an occupied hand after safety has already taken control. The current activity grant determines whether the arsenal may use the released hand.

Execution rechecks the work policy before requesting travel or taking the tool. Turning mining or chopping off revokes a previously prepared candidate, clears its retained job and releases its admission. Native tool permission remains a separate mutation check; an earlier positive utility value is never permission to keep acting after the user disables the work.

```
Gathering/
├─ CLAUDE.md              job ownership, preparation and native evidence limits
├─ MineOre.cs             retained ore-only vein work under the chosen policy
├─ ChopTree.cs            separate-tree preference and retained reachable trunk work
└─ DescribeOreJobEnd.cs   original bounded vein state at job end, with separate removal attribution
```

Both activities declare PurposeFamily.Gathering and register once in `../../BehaviourSelection/ChooseBehaviour.cs`. They use the common activity contract and the thin policy readers in `../../Behaviours/Work/WorkPolicies.cs`; persistent preferences remain owned by PlayerIntegration. Native tools remain in WorldInteractions, and shared safety can interrupt either activity without becoming a gathering child.

Mimic triggers come from the tile damage watcher in `../../WorldObservation/`, which sees real axe and pickaxe hits through the game's KillTile hook. Opportunistic triggers discover nearby resources without requiring a player hit. Both read the player's held tool's numbers, with a basic tool fallback. The work itself is in `../../WorldInteractions/Chopping/` and `../../WorldInteractions/Mining/`.

Mining separates its policy trigger from its retained job. Mimic starts only while the player has recently hit ore; opportunistic can discover ore near the player or companion. Once a job exists it holds the bounded same-type vein across guard and self-defence action switches, revalidates tile type, native pick damage, line of sight and approach on every resume, then relocates to the next reachable tile. A route query that proves no approach ends the reachable portion; a bounded `Unknown` stays retryable and never becomes a permanent inaccessible verdict or suppresses another useful action while waiting.

Ending a mining job snapshots its original bounded tile set before clearing eligible work. Present original material, changed material, missing tiles and unobserved coordinates remain distinct counts. Clearing an eligibility list cannot prove harvesting. The job separately counts unique tracked sites removed by its own accepted native tool calls; missing world tiles alone earn no such credit. The retained conclusion carries its original job identity and tick through later idle periods and new discovery. ObservedClear means every tracked coordinate was empty at that observation, not that the entire world vein was discovered or that drops were collected. Resumed targets retain the discovered material type rather than inheriting a placeholder type from a missing approach.

An undecided ore approach retains a discounted candidate tied to that ore, renewed only while the body covers ground. This lets bounded movement refinement continue without granting an unresolved target the value of a proven job. Lack of physical progress can release that approach; it does not establish that mining was an undesirable purpose or that every route is impossible.

The ore search retains the nearest unresolved tile separately from its proven target. Mining approaches that exact candidate after rechecking ore presence, tool power, activity allowance and home protection; it never substitutes the nearest arbitrary ore for an unknown reported elsewhere. Retained-vein relocation carries the same identity. The prepared activity exposes this provisional target and travel estimate without claiming a usable stand. Discovery filters native pick damage before ranking distance, so a weak-pick ore does not hide a farther usable one. The job exposes its id, policy, status, remaining-tile count and current target/stand for diagnostics. It does not target dirt or carve a route.

Chopping uses the same policy names through `TreeFinder` and `TileChopper`. Mimic retains the recent-hit window and avoids the player's active tree. Opportunistic discovery prefers admitted separate trunks across both search origins, then permits the player's trunk when no alternative remains. A change in the player's observed trunk refreshes discovery if it overlaps retained work; unrelated retained trees continue normally. This is a local discovery preference, not a world reservation. Opportunistic work retains a trunk through protective interruptions, but releases one outside its allowance or inside a protected home. Failed or unfinished approaches are deferred briefly so one tree cannot permanently mask local work. Resource admission measures the trunk rather than whichever side supplies its standing point.

Mining and chopping update discovery, approach caches and retry timers only during `Prepare`. They capture the resulting target, value and trip duration; Score and ForecastTicks read that result without advancing any timer or querying terrain. External ore removal is incorporated during preparation, not during repeated comparison. A missing prepared chopping candidate cannot execute a retained tree merely because the discovery cache still contains it.

Their prepared tile bindings carry coordinate and material through activation and execution. Disappearance or replacement rejects the old offer, releases the tool phase and requests fresh preparation; eligible replacement material may become a new job. Chopping's continuation identity includes material, so a different tree type at the same coordinate does not inherit its predecessor's activity. Mining keeps its bounded vein identity while its individual tile binding is refreshed within that job.

Relocating within a retained vein consumes its search cadence only while that vein remains active. If relocation ends the job, it preserves a requested fresh discovery; otherwise a material-change rejection can clear the old work and then accidentally postpone evaluating its replacement.

For an established target, the trip duration combines approach distance with the tool's estimate of its next native tile completion. Remaining hits and outstanding cooldown replace fixed work durations. The captured RemainingWork is nullable: missing or incapable work is not a finite completion promise. Mining's unresolved approach still carries its provisional discovery estimate and no native completion estimate; it must not be interpreted as proven work. Completing one ore does not commit the remainder of the vein or its drops.

Mining uses actual tool reach to decide whether to swing. A navigator's approximate arrival tolerance can leave the body outside that reach; returning Hold from that pose would prevent the movement needed to make mining possible. Retained vein pruning uses the same actor-and-target distance contract as discovery, and a removed target cannot survive merely because other vein tiles remain.

Chopping uses that same shared tool-access query for discovery, retained approaches and actual strikes. Current usable reach replaces the cached stand with the actual feet; otherwise the prepared trip includes a reachable working pose. Execution cannot infer axe access from distance to that pose. Terrain or reach changes may invalidate the access independently of whether the tree still exists.

