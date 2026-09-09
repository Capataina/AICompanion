# World window — restore real terrain shapes to an old plan capture

`reshape.py` converts a telemetry plan window whose tiles predate slope and half-block capture into a scenario built from the saved Terraria world. It exists because treating every solid tile as a wall turns a worldgen staircase into a false planner failure.

```
WorldWindow/
├─ CLAUDE.md   this guide
└─ reshape.py  reads a plan dump and world file, then writes a shaped scenario
```

Create the isolated parser environment once, then reshape the relevant capture from the repository root:

```
python3 -m venv /tmp/wldenv
/tmp/wldenv/bin/pip install lihzahrd
/tmp/wldenv/bin/python Tools/WorldWindow/reshape.py Telemetry/<stamp>-plans.txt "~/Library/Application Support/Terraria/tModLoader/Worlds/<world>.wld" --pad 48
```

The output belongs in `Tools/Scenarios/` and is committed when it captures a real defect. `--pad` adds world tiles beyond the original capture, because the replay treats a window edge as a wall. Increase padding until the replay no longer reports a result undecidable because of the cut; the saved world is the state at the end of the session, so a padded tile can differ from the moment that capture was taken.

## Trap

- Reshaping is a diagnostic repair for missing terrain shape data. Do not use it to turn a sealed world pocket into an invented escape route.
- Old flat-platform glyphs can hide a hammered stair. Restoring from the saved world preserves platform-ness beside slope or half-block shape, including in padded tiles; the saved world can still differ from the capture's moment.
