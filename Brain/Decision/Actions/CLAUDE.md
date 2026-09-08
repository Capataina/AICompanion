# Actions — one file per thing the companion can be doing

See `../CLAUDE.md` for the contract. The list order in `Chooser.cs` is only the order of the overlay; ties are decided by score, not position. Every action here returns a `PositionRequest` and never moves the NPC itself.

`MineAction.cs` mirrors `ChopAction.cs`: the player hitting an ore (from the tile damage watcher) starts it, the job survives ten seconds past the player's last hit, and the target is the nearest same-type ore outside the player's vein, else any ore, each mined as a whole patch: after a tile dies the next is the nearest patch tile still in reach of the current stand, and only when none is does it re-approach. Both use the player's own tool numbers and never a tool of their own.
