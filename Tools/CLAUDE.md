# Tools — headless evidence for companion behaviour

These console tools are excluded from the mod build and package. They consume telemetry and the portable movement core without launching Terraria, so a route or record can be inspected before a playtest result is interpreted.

```
Tools/
├─ CLAUDE.md                         this guide
├─ check-navigation-boundary.sh      verifies that portable movement code remains game-free
├─ verify.sh                         repository verification entry point
├─ corpus.sh                         scenario corpus helper
├─ decompile.sh                      Terraria source lookup helper
├─ EngineReplay/                     compares adapter predictions with native NPC collision
├─ NavReplay/                        replays portable movement plans and scenarios
├─ SessionReport/                    reads telemetry into definitive, potential and odd findings
├─ WorldWindow/                      reshapes old plan windows from saved-world tile shapes
└─ Scenarios/                        committed movement cases
```

`check-navigation-boundary.sh` is the architecture check: it ensures the portable core does not name Terraria outside the dedicated adapter. A green boundary check establishes a source boundary only; it does not establish that portable movement matches a live NPC.

Run `dotnet run --project Tools/SessionReport -- Telemetry` after a playtest. It reads the newest session, says which checks its schema supports, and exits non-zero for definitive faults. `NavReplay/CLAUDE.md` owns planner replay flags and verdict meanings; `WorldWindow/CLAUDE.md` owns saved-world reshaping; `EngineReplay/CLAUDE.md` owns native collision verification. `sh Tools/verify.sh` runs the build, boundary check, movement contracts, chronology tests and native engine cases.

## Traps

- Replay success is evidence about the portable movement core. It never substitutes for a playtest or the native collision comparison.
- **A check whose search tool is missing prints exactly what a held boundary prints.** `check-navigation-boundary.sh` pipes ripgrep into two greps and reports the boundary green when the pipeline is empty, so on a machine without `rg` it printed "movement boundary holds" while having searched nothing (2026-09-11). It now refuses with exit 2 when `rg` is absent. Any check built here states what it could not run rather than letting an absent tool read as a pass.
