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
├─ VerifyMirrorAndShrink.cs  the corpus tools checking themselves: the reflection is exact and a reduction keeps its failure
├─ CompareJumpPaths.cs  one jump edge through both of the paths that claim to perform it
├─ ReplayOneBlock.cs   one block evaluated once — reset, plan, flood, trail, follow — and the signature a reduction must preserve
├─ MirrorScenarioWorlds.cs  a block reflected left to right: tiles, glyph pairs, actors, trail and the recorded body box
├─ ShrinkFailingScenario.cs  delta debugging over tile-shaped reductions, down to the smallest window that still fails the same way
├─ ExtractScenarioFromCapture.cs  a recorded tick's terrain cut out of a capture into a committed-format scenario
└─ Program.cs          parses blocks, runs planning/replay modes and renders the result
```

**`NavReplay.csproj` lists its own sources**, because `EnableDefaultCompileItems` is off: a file added
to this folder and not added there does not fail to build, it is simply absent, and whatever mode it
carried reports nothing.

Run from the repository root:

The self-test exercises tiny retained-search slices, minimum progress under an already-spent deadline, directed region reuse, actual traversal learning, archive reload, a new C-to-A approach using an executed A-to-B suffix, full current-cost pruning, liquid invalidation and external-displacement rejection. Returned-path assertions accompany work counters: a suffix merely offered to a frontier does not prove the returned path used it. The full historical corpus remains a separate regression surface with recorded partial and model-closed cases.

Endpoint-certified clearance is checked with an impossible goal and an improving distance heuristic across multiple search slices: no movement may be returned solely because that heuristic improves. Historical partial endpoints are checked separately from full arrival; remaining at a partial end does not establish that the requested destination was reached.

```
dotnet run --project Tools/NavReplay -- --self-test   one ledger row: the whole contract suite, pass or fail
dotnet run --project Tools/NavReplay -- Tools/Scenarios
dotnet run --project Tools/NavReplay -- --follow Tools/Scenarios
dotnet run --project Tools/NavReplay -- --no-cache Tools/Scenarios
dotnet run --project Tools/NavReplay -- --churn Tools/Scenarios
dotnet run --project Tools/NavReplay -- --edges X,Y <scenario.txt>
dotnet run --project Tools/NavReplay -- --trace-jump <scenario.txt>
dotnet run --project Tools/NavReplay -- --trace-walk X,Y,DIR <scenario.txt>
dotnet run --project Tools/NavReplay -- --follow-ticks A,B <scenario.txt>
dotnet run --project Tools/NavReplay -- --compare-jump FROMX,FROMY,TOX,TOY [--compare-ticks] [--entry-vx V] [--entry-state LEFT,BOTTOM,VX[,GROUND]] <scenario.txt>
dotnet run --project Tools/NavReplay -- --audit-jumps <scenario.txt | folder>
dotnet run --project Tools/NavReplay -- --mirror Tools/Scenarios
dotnet run --project Tools/NavReplay -- --shrink <scenario.txt> [--follow] [--shrink-budget N]
dotnet run --project Tools/NavReplay -- --extract-scenario <capture.tsv> <tick> [--size WxH]
```

The plain run establishes a route verdict. `--follow` establishes whether the same navigator
can execute that route under the body simulation. `--no-cache` must agree with the cached run,
and `--churn` must report no stale plans after it breaks each route tile. `--edges` and the two
trace modes are focused instruments: use them before changing a traversal whose failure is in
one section of a scenario.

## Every replay mode runs with the millisecond allowances lifted, and the self-test does not

`AStar.Find` and `AStar.Region` both turn their budget into a wall-clock deadline, so a search under
load reaches less of the world than the same search on an idle machine. Every mode that replays a
scenario therefore lifts the allowance before it starts, leaving each query's work-count limit as
the only bound: that is what makes a corpus verdict an oracle rather than a measurement of how busy
the machine was, and it is the precondition for the mirror relation meaning anything at all. It used
to hold only by the accident that nothing here set the static budget, which any later caller could
have left behind.

The self-test is deliberately outside that: some of its fixtures exist to tell an unfinished search
from exhausted terrain, and lifting the deadline under them makes the distinction they check
impossible to express. A deadline fixture is one of the few things that legitimately reads a clock.

## Three ways a scenario becomes evidence: reflected, reduced, or cut out of a recording

**`--mirror` runs every block twice, as captured and reflected left to right, and the row is that the
two agree.** Nothing about a tile world prefers a direction, so a route proven one way must be proven
the other, and a block that answers differently is an asymmetry in our own code rather than a fact
about terrain. The relation is checked rather than the verdict: a block model-closed both ways
satisfies it, which is what lets the mirror run inside `verify.sh` when the plain corpus — red for
months by design — cannot, because a run carrying a failure can never be a baseline. It runs on this
corpus and not on the native suite, because the one mirror relation implemented there is the
intermittent fixture and a relation checked against an oracle that disagrees with itself under load
tests the oracle.

Three things make the reflection exact and each is invisible once wrong. The axis is the window's
own span taken from the rows rather than from the header's declared bounds, since the parser sizes
the world from its longest row. A short row is padded with solid on the right before it is reversed,
or the reflection quietly moves that wall to the other side of the window. And a body is a box
rather than a point, so its left edge reflects through its own width — without it the body lands a
box to the side, which on a companion is a tile and a bit. The double reflection is required to be
the identity over every committed block, beside a check that at least one block actually changed,
because a transform that returned its input would satisfy an identity perfectly.

**A disagreement prints both roots, both flood sizes and whether the roots are reflections of each
other, and writes the reflected block to the temp directory**, because the relation only ever says
that two answers differ and every reader's next question is which half differs. The root line
separates two findings with different owners: `Ground` ends in `NearestStandable`, whose
neighbourhood order is not mirror-symmetric, so a reflected start can settle somewhere that is not
the reflection of the original's, and then the asymmetry is in choosing a root rather than in
flooding from one. The written block is what makes the other half reachable — without a file on disk
the reflection exists only inside the mirror loop, and `--edges` cannot be pointed at it. It is
written to the temp directory rather than beside the original on purpose: a reflected block is a
diagnostic, and a file in `Tools/Scenarios` is a fixture the whole corpus then replays.

**Reflecting the body cannot preserve its sub-tile offset, and this is arithmetic rather than a
defect to fix.** The body is wider than a tile, so reflecting its box about a tile boundary maps an
offset `o` within its tile to `(1 - boxTiles - o)` — exact in pixels, and a different fraction of a
tile. Anything diagnosing a mirror disagreement checks that before blaming the evaluator, by giving
the original the reflection's offset and re-running: if the verdict does not move, the offset was
not the cause.

**`--shrink` reduces a failing scenario to the smallest window that still fails the same way**, by
delta debugging (Zeller and Hildebrandt's ddmin) over reductions shaped like the thing being reduced
(Regehr et al.): rows emptied, rows walled, single tiles emptied, the trail cut to a prefix, and the
window trimmed from its edges. The signature is computed once from the original and every candidate
is equality against it — the verdict class, which region the model closed around, the follow outcome,
the first physical fault's kind and the first trail tile the grid refused. **A reduction that changes
the signature found a second finding rather than a smaller one**, and the distinction is not
academic: the first version's signature omitted which region closed, and the reducer duly turned a
body sealed into a pocket into a goal sealed into one and reported the failure preserved.

Two transforms are deliberately absent and both would look reasonable. Nothing fills a tile with
support that was not there, because the corpus's own rule is that a fixture is never altered to make
a move succeed. And an interior column is never deleted, only edge-trimmed: deleting one renumbers
every tile to its right, so the reduced file's coordinates would stop naming the places the capture
named, and a fixture whose tiles cannot be pointed at in the world is one nobody can check against
the world.

Two properties of the passes were learned by running them. **The walling pass may only touch rows the
emptying pass could not empty**: run over every row, it refilled with solid the rows just emptied, so
the two passes undid each other and the output was larger than the input — a reduction that can
reverse another reduction is not one. And **the interior passes may spend only part of the call
budget**, because the trim is last and is the reduction a reader actually feels; without a reserve the
rows and tiles consumed every call and the window came back its original size.

A passing scenario is refused rather than minimised: with no failure to preserve, ddmin would reduce
until the answer changed for a reason nobody asked about. The reduced file is written beside its
original carrying the original's name, the transforms applied and the signature — at the *end* of the
header line, because every recorded key is found by its first occurrence and provenance carrying the
word "goal" ahead of the real one would become the goal.

**`--extract-scenario` cuts the terrain around the companion at a recorded tick out of a capture's
own `terrain-snapshot` events**, with the recorded body, the destination the brain asked for on that
tick, the player's feet and their trail. It is how a failure the player can feel becomes a block the
tool replays without waiting for one of the recorder's detectors to have imagined it. **A tile no
snapshot covered is written solid and counted in the reported coverage, never left as air**: the
glyph alphabet reads anything outside it as air, so missing terrain would become open sky and a
window with holes in it would read as a window with an easy route. Snapshots are joined to the row on
the recorder's own stopwatch rather than on the tick, because the two streams have different
producers and only the elapsed millisecond means the same thing in both. **Coverage is coverage as
*last written*, not as it stood at the tick**, because the recorder writes a chunk only when it has
changed since it last wrote one: a chunk nobody was near keeps the shape it had when a snapshot last
reached it, and a fully covered window can still describe terrain that was mined afterwards. The
header therefore carries how far behind the tick the oldest contributing snapshot was, and that is
the number that prices a full-coverage window rather than the percentage. Which recorded columns are
pixels and which are tiles is read off the column and never inferred from the text — `npc_px` writes
its pixels without a decimal point, so a parser deciding by punctuation would cut the window fifty
thousand columns from anywhere anyone has been.

## Reading a jump that the planner proves and the body cannot make

A jump has two code paths that each claim to perform it, and they are both portable, so a disagreement between them is entirely ours and has nothing to do with the native body — `--compare-jump` is not a parity check and a reader who treats it as one will look for the defect in the wrong half. The proof path is `BodyPhysics.SimulateJump`, which is what `Candidates` calls to decide the edge exists. The execution path is `TraversalExecution` driving `JumpTraversal.Steer` into `BodyMotion.Step`, which is what `PlanLocalMovement.TryExecute` replays from the live body before the navigator will commit to the step, and it owns the run-up as well as the arc.

`--compare-jump` runs one edge through both and prints them **aligned on their take-off ticks rather than on tick 1**, because the execution path spends ticks backing away and running in, and a raw tick alignment reports that preparation as the divergence and buries the real one. What the alignment exposes is the state each arc starts from: a proof flown at a speed the runway cannot deliver is a proof of a jump the body never makes, and the take-off row says so in one line.

The reference row is now **the launch the edge names, reconstructed from the step alone** — the node pose shifted along the jump's heading and down by the launch offsets the step carries, at the speed it carries. Reconstructing rather than re-deriving is the point: the step is the only thing the performer is handed, so an arc that does not fly from what the step says is an edge nobody can reproduce. There is no nominal-speed row any more because there is no nominal on a step; the gap that defect class produced is now the gap between this reconstructed launch and where the performer starting from rest actually arrives, and a healthy edge prints the same pixel and the same speed on both rows.

`--compare-ticks` adds every tick of every run. `--entry-vx` supplies the sideways speed a plan dump's `npcbox` does not record, and a dump's body is used only where it stands on the edge's own take-off tile, because the box is whatever the body was doing at the tick the window was written. `--entry-state` overrides both and is used wherever the body stands: a `Rejection` record in an events file carries the left, bottom, velocity and contact the macro proof refused, and that state is usually a tile short of the take-off with the body still moving, which is exactly the case worth replaying and exactly the one a header pose cannot express. It is how a refusal seen in play becomes reproducible offline.

`--audit-jumps` is the same comparison over every node in every block, and it is the offline twin of the game's behaviour census: the census counts jumps begun against jumps completed and cannot say whether the ones that failed were ever possible, while this counts the proofs that do not survive their own execution path and needs no playtest to do it. Its four outcomes are deliberately not one number. *Flown to its tile* is the good case. *Satisfied at entry* is a step the shared arrival test closes before the body jumps at all, which happens on a one-row jump whose landing feet point sits inside `Traversal.ArriveSlack` of the pose it starts from. *Closed by the arrival slack elsewhere* is a body that flew and came to rest within that slack but on a neighbouring tile. Both of those are properties of `Traversal.Done` rather than of the arc, and they are counted apart from *unflyable* so that a real misland cannot hide inside the tally. Folding them together is how a regression check turns into a rubber stamp. The mode runs no plan cases, so it exits on its own property — zero unflyable proofs — rather than on the corpus pass count, which is zero by construction here and would otherwise report every audit as a failure whatever it found. It is red across the whole corpus today, on a residue that is counted and uninvestigated rather than explained.

The edge trace includes route replacements and combat interruptions. Those end an attempt
without counting as a traversal fault. Only actual failure outcomes contribute to the replay's
fault limit; otherwise richer recording alone can turn a previously successful route red.

A PASS is limited to this model and captured terrain. A SEALED result says the captured window
or body model cannot establish a route; it is never evidence that the live game world is sealed.
The engine-backed `IBodySimulationWorld` adapter is the runtime authority, while this text-world
backend keeps the corpus deterministic.

## Traps

The self-test includes captured sub-tile cave entries, rejects a stationary preparation allowance, asserts that a refused entry is proven again on the next tick rather than answered from a cache of refused states and that a preparation search over an immobile body reaches a verdict instead of exhausting its bound, verifies that an aged physical failure stays excluded until a fresh simulation succeeds after an unannounced world change, audits every jump the captured one-tile-runway window proves against the performer that has to fly it, and drives a navigator into a landing closed after its route was planned to check that a step refused before its first tick still prices its tile. Both of the last two were written against the old behaviour first and observed to fail; a check of this shape that has never been seen red is a check nobody can price. Deadline fixtures distinguish incomplete search from exhausted terrain. `--follow` prints the first rejected macro's entry and predicted failure state, so a route proposal and its execution can be diagnosed separately.

- A new movement-core source normally enters the project through the shared glob. Confirm it
  builds with this project after moving a file; a mod build alone does not exercise the replay.
- Marker glyphs are air. Move probes through a block header or command argument, never by
  writing a marker over a slope or platform.
- A local controller may only repair an uncommitted section. A committed traversal owns its
  preparation and flight controls because replacing a running jump's runway direction with a
  control that looks closer to its landing reverses the run-up and times out.
