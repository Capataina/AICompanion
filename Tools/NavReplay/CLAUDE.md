# NavReplay — headless evidence for the movement core

`Program.cs` runs the real game-free movement core against a text-world capture. It is the
first check for a navigation claim: a scenario supplies the terrain, recorded start and goal;
the tool plans a route, optionally follows it through `Navigator`, and prints the plan, flood
classification and executed-edge trace. It does not launch Terraria or infer success from a
picture.

```
NavReplay/
├─ CLAUDE.md          this operating contract
├─ NavReplay.csproj   compiles every shared-movement source except TerrariaIntegration
├─ VerifyMovementContracts.cs  state, safety, retention and policy-isolation tests
└─ Program.cs          parses blocks, runs planning/replay modes and renders the result
```

Run from the repository root:

```
dotnet run --project Tools/NavReplay -- --self-test
dotnet run --project Tools/NavReplay -- Tools/Scenarios
dotnet run --project Tools/NavReplay -- --follow Tools/Scenarios
dotnet run --project Tools/NavReplay -- --no-cache Tools/Scenarios
dotnet run --project Tools/NavReplay -- --churn Tools/Scenarios
dotnet run --project Tools/NavReplay -- --edges X,Y <scenario.txt>
dotnet run --project Tools/NavReplay -- --trace-jump <scenario.txt>
dotnet run --project Tools/NavReplay -- --trace-walk X,Y,DIR <scenario.txt>
dotnet run --project Tools/NavReplay -- --follow-ticks A,B <scenario.txt>
```

The plain run establishes a route verdict. `--follow` establishes whether the same navigator
can execute that route under the body simulation. `--no-cache` must agree with the cached run,
and `--churn` must report no stale plans after it breaks each route tile. `--edges` and the two
trace modes are focused instruments: use them before changing a traversal whose failure is in
one section of a scenario.

The edge trace includes route replacements and combat interruptions. Those end an attempt
without counting as a traversal fault. Only actual failure outcomes contribute to the replay's
fault limit; otherwise richer recording alone can turn a previously successful route red.

A PASS is limited to this model and captured terrain. A SEALED result says the captured window
or body model cannot establish a route; it is never evidence that the live game world is sealed.
The engine-backed `IBodySimulationWorld` adapter is the runtime authority, while this text-world
backend keeps the corpus deterministic.

## Traps

- A new movement-core source normally enters the project through the shared glob. Confirm it
  builds with this project after moving a file; a mod build alone does not exercise the replay.
- Marker glyphs are air. Move probes through a block header or command argument, never by
  writing a marker over a slope or platform.
- A local controller may only repair an uncommitted section. A committed traversal owns its
  preparation and flight controls because replacing a running jump's runway direction with a
  control that looks closer to its landing reverses the run-up and times out.
