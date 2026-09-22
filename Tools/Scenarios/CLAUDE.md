# Scenarios — preserved terrain from play, and the two windows the orb is judged in

These files preserve terrain and actor states cut out of playtest recordings, plus small fixtures the walker's era isolated by hand. Each block is a header line of `key value` pairs, a `markers` line naming every actor position outright, a `trail` of the player's feet tiles, and a glyph grid one character per tile: `#` solid, `=` platform, `_` half block, `\` and `/` floor slopes, `<` and `>` ceiling slopes, `~` water or honey, `L` lava, `.` air. The body is the `orb x,y` header key, its centre in world pixels; the grid never carries the body, because a marker written over a slope or half block erased the support under it once. A file holding several blocks holds several independent inputs.

```
Scenarios/
├─ CLAUDE.md
├─ extracted-2026-09-14_19-55-52-468-tick-7224.txt  the statue ledge from the last walker play: the orb starts where the walker stalled and must reach the player's final position
├─ extracted-2026-09-14_20-00-40-039-tick-5300.txt  the water pocket from the same evening: the orb must reach the player from the pocket the walker looped in
├─ extracted-2026-09-14_13-27-46-345-tick-8127.txt  the platform jump of the 13:27 capture that was watched dying on a Hold issued mid-air, cut for the settled-arrival rule
├─ extracted-2026-09-22_10-05-56-125-tick-1950.txt  the pocket the accompanying tour lived on the face of for two hundred ticks: a five-wide pillar, a westward-opening wedge above the body, solid below it, and the way out west and down
├─ 2026-09-15_13-16-33-496-player-track.txt         not terrain: the third orb play's player track, one line per consecutive tick of his feet and velocity, which the intent region's replay row drives through the real lead filter
├─ 2026-09-08_13-48-44-plans-shaped.txt             earlier captured failures reshaped from a saved world
├─ 2026-09-08_16-04-10-plans-shaped.txt             later shaped failures including the wet arrival regression
├─ 2026-09-09_18-25-59-plans-underground-house-0.8.3.txt  raw structure and pool failures
├─ 2026-09-09_18-25-59-census-0.8.3.txt             the walker's traversal census accompanying that recording
├─ actual-entry-drop-run-9.txt                      recorded body offset before a cave descent
├─ gap-three-wide-two-up-from-run-4-case-9.txt      a gap isolated from a captured failure
├─ half-block-floor-landed-through-a-platform.txt   platform descent onto half-height support
├─ jump-from-a-one-tile-runway-under-a-ceiling.txt  the 2026-09-11 window where the walker refused a jump proven at a speed its runway could not build
├─ ledge-four-up-lowest-arc-lands-run-5.txt         a four-tile ledge
├─ ledge-two-tiles-needs-one-jump.txt               an ordinary two-tile ledge
├─ ledge-under-overhang-to-corridor-run-5.txt       clearance under a ceiling
├─ markers-line-keeps-half-block-support.txt        actor markers must preserve the terrain beneath them
├─ platform-cap-fall-through-beside-a-block-run-7.txt  descent beside adjacent solid support
├─ platform-intercepts-a-jump-from-below.txt        intermediate platform contact
├─ platform-lip-one-row-down-is-a-walk-run-5.txt    a shallow lip
├─ shaft-the-body-freezes-in.txt                    the walker's platform-lift freeze
├─ shaft-two-wide-descended-from-the-lip.txt        a narrow shaft
└─ slope-lip-one-row-up-is-a-walk-not-a-hop.txt     the lip at 3496,479 cut out of run 4 block 15
```

## The two checkpoints are the orb's pass line, and they run in the real world

The first two files are the evidence the orb was built against: the last two plays of the walker, 19:55 and 20:00 on 14 September 2026, where the statue ledge stayed unreached and the companion looped in a water pocket. `sh Tools/verify.sh` plays each as a headless world run — `dotnet run --project Tools/WorldRun -- --scenario=<file> --world=<wld>` — in the saved world the capture was made in, with the player standing still where the recording left him and the orb placed at the header's centre, and files three kinds of row: the grid's agreement with the world (solid against air over the window), the ticks until the orb is within following's vertical comfort of the player and until it enters his region, the ticks it spends inside that region, the closest distance and the minimum clearance it flew at, and the pass line — the orb reaches the player, which is being inside his region and staying there on a share of the run. The world file is not committed, so a machine without it files a skip rather than a red.

The grid is the recording's picture of the terrain and the run uses the world's; the agreement row is what tells a divergence in the orb from a divergence in the ground, and on both files it is within a handful of tiles. The water-pocket window holds no wet tile in the saved world — the pocket the walker looped in was drained or lies outside the cut. It stays committed because the reach row is the row that failed for the walker. A second pass line, that the orb never touches water or lava, went on 15 September 2026 when every liquid became air to the orb.

## The walker's fixtures are history, kept as terrain

Everything from the fourth file down was cut for a walking body: jumps, lips, shafts, platform descents. None of them is replayed by anything now — NavReplay's corpus replay and mirror verdict went with the walker's planner, and `Tools/NavReplay/CLAUDE.md` says what its self-test still does with these files, which is prove the reflection transform an involution over them. They are kept because a terrain window cut from play is evidence nobody can recut, and because a scenario mode that can play any of these against the orb exists; playing them is a decision for whoever wants a row about that terrain, not something the suite does by default. Their header lines still carry the walker's keys beside `orb`; the parser reads what it names and ignores the rest.

## How a scenario is made

`dotnet run --project Tools/NavReplay -- --extract-scenario <capture.tsv> <tick> [--size WxH]` cuts the window around the companion at a recorded tick out of the capture's terrain snapshots, with the orb's centre from `npc_px`, the destination the brain asked for, the player's feet and their trail. **Its header carries what the snapshots did not cover**, because a tile no snapshot reached is written solid — unknown is closed — and a window at low coverage is a window whose verdict is about the recording rather than about the world. Read the coverage before reading a row, and the snapshot age beside it, because the recorder writes a chunk only when it changed, so a fully covered window can describe terrain mined a minute before the tick.

Do not alter a fixture to make a move succeed; a window is a recording. Reshaping from a saved world can introduce terrain edited after the failure, which the agreement row is there to show. A liquid the text world cannot express — a partial cell, honey, shimmer — is missing coverage and belongs in the row, not in the grid.
