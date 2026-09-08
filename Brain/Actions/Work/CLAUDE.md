# Work actions — doing what the player is doing

```
Work/
├─ CLAUDE.md
├─ ChopAction.cs   the player is really chopping: the nearest other tree, kept 2 s past the last hit
└─ MineAction.cs   the player is really mining an ore: the same ore outside the player's vein, else any ore, the whole patch, kept 10 s past the last hit
```

Both start from the tile damage watcher in `../../DecisionMatrix/Senses/`, which sees every real axe or pickaxe hit through the game's own KillTile hook, and both read the player's held tool's numbers rather than owning a tool. The work itself is in `../../Work/Chopping/` and `../../Work/Mining/`. Mining as a whole patch: after a tile dies the next is the nearest patch tile still in reach of the current stand, and only when none is does it re-approach.
