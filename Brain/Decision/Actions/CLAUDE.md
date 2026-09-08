# Actions — one file per thing the companion can be doing

See `../CLAUDE.md` for the contract. The list order in `Chooser.cs` is only the order of the overlay; ties are decided by score, not position. Every action here returns a `PositionRequest` and never moves the NPC itself.
