# EngineReplay — native NPC collision is the independent oracle

This console tool loads the installed tModLoader assembly without opening a game window. It compares SimulateTerrariaBody with Terraria’s own NPC collision wrapper; it does not compare two copies of the portable text-world simulator.

```
EngineReplay/
├─ CLAUDE.md                 setup, scope and evidence limits
├─ EngineReplay.csproj       compiles the movement core and native adapter against the installed game
├─ Program.cs               resolves the installed game’s library dependencies
└─ VerifyEngineMotion.cs     terrain/liquid matrix, scratch-state assertions and native route checks
```

From the repository root run `dotnet run --project Tools/EngineReplay`. Exit zero requires every matrix entry to match and all native route fixtures to arrive. `sh Tools/verify.sh` includes this command. The game location can be supplied as the executable’s first argument; MSBuild’s TModLoaderRoot controls the reference location when compiling on another installation.

The setup assigns Terraria.Program.SavePath before Main’s static constructor, constructs a small Tilemap through its non-public constructor and marks the process dedicated-server for headless operation. Each comparison creates an ordinary NPC, runs its private gravity setup, applies shared controls and the step helpers, adds the engine’s gravity, then invokes private UpdateCollision. It fixes wetCount to suppress liquid entry/exit audiovisual effects. No game AI, enemy spawning, save loading or full NPC update is invoked.

The matrix covers floor and ceiling slope orientations, flat/sloped platforms, dry movement, water, honey, shimmer, liquid transitions, offsets, jump and fall-through controls, and both space-scaled and ordinary gravity. Position, velocity, wet state, liquid priority and stair state must agree; prediction must restore all seven Collision scratch fields. Native route cases cover flat travel, a two-tile ledge, staircase ascent and staircase descent. Their output reports recovery faults even when the destination is reached.

These are repeatable collision and route fixtures. They do not establish general world navigation, threat prediction quality or comfortable companionship. The historical text-world corpus and a recorded playtest answer those different questions. Private engine method names are intentional verification dependencies: if a game update removes them, the test must fail visibly rather than silently substitute another simulator.
