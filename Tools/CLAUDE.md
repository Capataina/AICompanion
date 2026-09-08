# Tools — the navigation core run without the game

Console tools and the scenario database, none of it mod code: `build.txt` ignores this folder for the `.tmod` and the mod's project excludes it from the compile. The tools exist because a navigation claim is checked here before a playtest and never by launching the game: the replay compiles the real planner, the real traversals and the real navigator against a text world read from a telemetry dump, so the plan it prints is the plan the game would have made, and the walk it performs is the walk the game's navigator would have driven over the simulated body. The one seam the game supplies is the tile world (`Brain/DecisionMatrix/Navigation/World/`), and `check-navigation-boundary.sh` is what keeps the core compilable without it.

```
Tools/
├─ CLAUDE.md
├─ check-navigation-boundary.sh   greps every code line under Brain/DecisionMatrix/Navigation/ for a Terraria type, the NPC, Main or the motor, outside World/GameTileWorld.cs; comments are excused; exit 0 is the pass
├─ NavReplay/                     the console project: a scenario file or folder in, a verdict per block out, with the flags below; its .csproj lists the navigation source files it compiles, so a new file under Navigation/ is added there or the tool does not see it
├─ WorldWindow/                   reshape.py: rewrites a plan dump's walls into the slope and half-block shapes the saved world carries, and with --pad grows the window from the world; needs the lihzahrd parser in a venv
└─ Scenarios/                     the committed database: one file per run's shaped dump (<stamp>-plans-shaped.txt, many blocks) and one per hand-cut case named for what it holds; every in-game failure becomes a block here, by ruling
```

## What a block is, and what the replay says about it

A block is one plan dump: a header line naming the tick, why it was dumped, the start, the goal, the companion's and the player's tiles and the window; a `markers` line (S start, G goal, N the companion, P the player); the player's `trail` of feet tiles when the dump has one; then the window, one glyph per tile, rows top to bottom (`#` block, `=` platform, `_` half block, `\` `/` floor slopes, `<` `>` ceiling slopes, `~` water, `L` lava, `.` air, and `o` for air the grid can stand in, which the telemetry writes over standable air so a map reads where the nodes are). The replay's text world reads the glyphs, treats the marker letters as air and remembers them, and treats the window's edge as a wall on three sides and a void below.

For every block the tool plans from the recorded start to the recorded goal and prints PASS, FAIL or SEALED; then whether the player's feet were reachable; then whether the goal and the player lie inside the region the positioner's flood reaches from the start, with a sealed region classed by whether the flood ever read past the window (SEALED START is a pocket the world closes and a rescue's job, SEALED GOAL a spot the positioner must not offer, both clipped is undecidable as cut and wants `--pad`); then, on the same line and only where the two differ, how many of those tiles the body could also come home from and which side of that line the goal falls, because the raw region is what the body can enter and the returnable one is what the positioner actually scores against, so a block where they differ is a block where prevention is doing something (45 of 104 on the corpus of 2026-09-08, the widest gap 151 tiles of 371); then the first trail tile the grid refuses; then the map with the path (`w j d f` for walk, jump, drop, fall-through) or, on a failure, every tile the search closed. A sealed block is its own count and never fails the run; the exit code is 0 only when every block passed, nothing was skipped, and with `--follow` every found plan was walked.

## Operating manual

From the repository root:

```
dotnet run --project Tools/NavReplay -- Telemetry/<stamp>-plans.txt         # a fresh run's failed plans
dotnet run --project Tools/NavReplay -- Tools/Scenarios                      # the whole database
dotnet run --project Tools/NavReplay -- --trace-jump <scenario.txt>          # every jump profile's arc from S to G, tick by tick
dotnet run --project Tools/NavReplay -- --trace-walk X,Y,DIR <scenario.txt>  # the walk proof from that tile that way (DIR -1 or 1), tick by tick, and where the body first stands past the next centre
dotnet run --project Tools/NavReplay -- --edges X,Y <scenario.txt>           # every edge each traversal offers from that tile: kind, target, cost, fall, ticks, profile, start speed, steer line
dotnet run --project Tools/NavReplay -- --follow-ticks A,B <scenario.txt>    # --follow, and every tick from A to B printed as it happens with the step the navigator is on, for a stall that never faults
dotnet run --project Tools/NavReplay -- --no-cache Tools/Scenarios           # every edge simulated afresh; must read the same verdicts as with the cache
dotnet run --project Tools/NavReplay -- --churn Tools/Scenarios              # breaks every tile under a found path in turn; the warm cache's plan must equal a cold one's (0 stale plans)
dotnet run --project Tools/NavReplay -- --follow Tools/Scenarios             # the real navigator walks each found plan over the simulated body: every edge with its proven and actual ticks and outcome, the state tick by tick at the first fault, and "walked" or "not" per block
sh Tools/check-navigation-boundary.sh                                         # the core names no Terraria type outside GameTileWorld.cs
/tmp/wldenv/bin/python Tools/WorldWindow/reshape.py Telemetry/<stamp>-plans.txt "<world>.wld" --pad 96   # a dump into a shaped scenario; the root CLAUDE.md has the venv
```

The four corpus passes are run together before any navigation commit and their four summary lines go in the commit body, because each proves a different thing: plain and `--no-cache` must agree (the cache changes no verdict), `--churn` must read zero stale plans (the cache's invalidation box is wide enough), and `--follow` is the count of plans the body can execute, which is the number the whole traversal design is judged by; its second count, plans walked to a partial plan's end, is the body doing what a budget-cut search asked and belongs to the search's budget (AIC-143), so only the third count, "not", is the follower's. A verdict that moves between two runs is stated with the block and the reason; a block that moves from PASS to SEALED is usually a real seal the old grid had walked through, and one that moves the other way is a link the old grid could not see.

A probe of one place is a block with its header's start and goal and its `markers` line rewritten (the replay takes the goal from the header when it has one), run with `--follow` or `--edges`, or the untouched block with `--trace-walk` and `--edges`, which take the tile as an argument; the sealed verdicts are only meaningful on a fresh cache, which the tool arranges itself. Reading a stall: `--follow` names the first fault and prints the body's last ticks before it, and a run that never arrives with no fault is read with `--follow-ticks` around the edge line where the trace stops making progress, because a loop (walk back, jump, land a column short, replan, walk back) shows no fault and no stillness.

## Traps

- **The window's edge is a wall to the tool and nothing else.** A block whose plan fails with a complete region and the goal outside is read from its flood line before its map, and padded from the world before the planner is touched.
- **A start tile with no pose is moved.** When the recorded start is not a node (the companion between nodes, or standing where the grid sees no pose), the tool plans from the nearest node it finds and says so on the recorded-goal line as `start A -> B`; a probe that means to start exactly there needs the tile to be a node.
- **A marker glyph replaces the tile it is written on.** The text world reads S, G, N and P as air, so a probe that writes a marker into the map to move the start erases the shape under it and traces a different world (a slope became air on the first walk trace, 2026-09-08); move a probe through the header's start and goal or the tile arguments of `--trace-walk` and `--edges`, never through the map. The `o` glyph the telemetry writes over standable air is not a shape either, and a reader takes the shape from a neighbouring column or the `--edges` output.
- **The tool compiles the navigation sources by name.** A new file under `Brain/DecisionMatrix/Navigation/` compiles in the mod and is invisible to the tool until `NavReplay.csproj` lists it; the first sign is a missing type at `dotnet run`.
- **A fish shell rejects `--include` globs in a pipeline** (`no matches found`); the boundary script runs under `sh` for that reason, and an ad-hoc grep of the sources goes through a plain pipe.
