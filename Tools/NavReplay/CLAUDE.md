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
├─ CompareJumpPaths.cs  one jump edge through both of the paths that claim to perform it
└─ Program.cs          parses blocks, runs planning/replay modes and renders the result
```

Run from the repository root:

The self-test exercises tiny retained-search slices, minimum progress under an already-spent deadline, directed region reuse, actual traversal learning, archive reload, a new C-to-A approach using an executed A-to-B suffix, full current-cost pruning, liquid invalidation and external-displacement rejection. Returned-path assertions accompany work counters: a suffix merely offered to a frontier does not prove the returned path used it. The full historical corpus remains a separate regression surface with recorded partial and model-closed cases.

Endpoint-certified clearance is checked with an impossible goal and an improving distance heuristic across multiple search slices: no movement may be returned solely because that heuristic improves. Historical partial endpoints are checked separately from full arrival; remaining at a partial end does not establish that the requested destination was reached.

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
dotnet run --project Tools/NavReplay -- --compare-jump FROMX,FROMY,TOX,TOY [--compare-ticks] [--entry-vx V] <scenario.txt>
dotnet run --project Tools/NavReplay -- --audit-jumps <scenario.txt | folder>
```

The plain run establishes a route verdict. `--follow` establishes whether the same navigator
can execute that route under the body simulation. `--no-cache` must agree with the cached run,
and `--churn` must report no stale plans after it breaks each route tile. `--edges` and the two
trace modes are focused instruments: use them before changing a traversal whose failure is in
one section of a scenario.

## Reading a jump that the planner proves and the body cannot make

A jump has two code paths that each claim to perform it, and they are both portable, so a disagreement between them is entirely ours and has nothing to do with the native body — `--compare-jump` is not a parity check and a reader who treats it as one will look for the defect in the wrong half. The proof path is `BodyPhysics.SimulateJump`, which is what `Candidates` calls to decide the edge exists. The execution path is `TraversalExecution` driving `JumpTraversal.Steer` into `BodyMotion.Step`, which is what `PlanLocalMovement.TryExecute` replays from the live body before the navigator will commit to the step, and it owns the run-up as well as the arc.

`--compare-jump` runs one edge through both and prints them **aligned on their take-off ticks rather than on tick 1**, because the execution path spends ticks backing away and running in, and a raw tick alignment reports that preparation as the divergence and buries the real one. What the alignment exposes is the state each arc starts from: a proof flown at a speed the runway cannot deliver is a proof of a jump the body never makes, and the take-off row says so in one line. The nominal-speed proof is printed beside the achieved-take-off proof on purpose, so the gap that defect class produces stays recognisable after it has been fixed once. `--compare-ticks` adds every tick of every run; `--entry-vx` supplies the sideways speed a plan dump's `npcbox` does not record. A recorded entry is used only where the body stands on the edge's own take-off tile, and ignored with a printed reason otherwise, because the dump's box is whatever the body was doing at the tick the window was written.

`--audit-jumps` is the same comparison over every node in every block, and it is the offline twin of the game's behaviour census: the census counts jumps begun against jumps completed and cannot say whether the ones that failed were ever possible, while this counts the proofs that do not survive their own execution path and needs no playtest to do it. Its four outcomes are deliberately not one number. *Flown to its tile* is the good case. *Satisfied at entry* is a step the shared arrival test closes before the body jumps at all, which happens on a one-row jump whose landing feet point sits inside `Traversal.ArriveSlack` of the pose it starts from. *Closed by the arrival slack elsewhere* is a body that flew and came to rest within that slack but on a neighbouring tile. Both of those are properties of `Traversal.Done` rather than of the arc, and they are counted apart from *unflyable* so that a real misland cannot hide inside the tally. Folding them together is how a regression check turns into a rubber stamp. The mode runs no plan cases, so it exits on its own property — zero unflyable proofs — rather than on the corpus pass count, which is zero by construction here and would otherwise report every audit as a failure whatever it found. It is red across the whole corpus today, on a residue that is counted and uninvestigated rather than explained.

The edge trace includes route replacements and combat interruptions. Those end an attempt
without counting as a traversal fault. Only actual failure outcomes contribute to the replay's
fault limit; otherwise richer recording alone can turn a previously successful route red.

A PASS is limited to this model and captured terrain. A SEALED result says the captured window
or body model cannot establish a route; it is never evidence that the live game world is sealed.
The engine-backed `IBodySimulationWorld` adapter is the runtime authority, while this text-world
backend keeps the corpus deterministic.

## Traps

The self-test includes captured sub-tile cave entries, rejects a stationary preparation allowance, checks rejection reuse across new execution objects, verifies that an aged physical failure stays excluded until a fresh simulation succeeds after an unannounced world change, audits every jump the captured one-tile-runway window proves against the performer that has to fly it, and drives a navigator into a landing closed after its route was planned to check that a step refused before its first tick still prices its tile. Both of the last two were written against the old behaviour first and observed to fail; a check of this shape that has never been seen red is a check nobody can price. Deadline fixtures distinguish incomplete search from exhausted terrain. `--follow` prints the first rejected macro's entry and predicted failure state, so a route proposal and its execution can be diagnosed separately.

- A new movement-core source normally enters the project through the shared glob. Confirm it
  builds with this project after moving a file; a mod build alone does not exercise the replay.
- Marker glyphs are air. Move probes through a block header or command argument, never by
  writing a marker over a slope or platform.
- A local controller may only repair an uncommitted section. A committed traversal owns its
  preparation and flight controls because replacing a running jump's runway direction with a
  control that looks closer to its landing reverses the run-up and times out.
