# CombatAudit — every combat decision a play made, restored and decided again

The other instruments here ask whether the brain is broken. This one asks a question none of them can: **was the fight the companion actually chose the best one available to it, and would it choose the same thing twice?** A playtest produces a committed attack plan per decision and no account of what it beat, so "the companion fought badly" is a feeling with nothing under it. The audit takes every `combat-snapshot` the recorder wrote, rebuilds the world that decision was made in, and re-runs the real search over it — then prices the committed plan against an exhaustive grid the live search never had time to look at.

It is the only tool in this tree that grades a *decision* rather than a behaviour. A fixture in `../EngineReplay` plants a scene and asserts what the brain does in it, which proves a rule and says nothing about a real fight; this one reads a real fight and says what the brain gave up.

## What one snapshot costs, and what each pass answers

Every decision goes through five passes in a fixed order, and the order is load-bearing rather than convenient:

```
RestoreSnapshot.Restore      the world that decision saw: tiles, actors, knowledge, tracks, ledgers,
                             the region — shifted from live world coordinates into a local window
        │
        ├─ AuditSearch.Replay        did it reproduce?      the live budget, the live proposals and
        │                                                   verdicts, the sampler seeded by the restored
        │                                                   tick, compared plan against plan
        ├─ AuditSearch.Exhaustive    was it on the front?   every half-tile of the proposal region with a
        │                                                   completed flood, unbounded budget, means
        │                                                   forced → on-front, weighted regret, and which
        │                                                   generator would have proposed the better stand
        ├─ AuditWeights.Sweep        what is a weight for?  each objective halved and doubled, the decision
        │                                                   replayed, whatever moved about the winner
        │                                                   reported — stand, weapons, targets
        └─ AuditHold.Revalidate      would it still stand?  the shifted plan committed and the commitment's
                                     (LAST, always)         own Validate run on the snapshot's tick
```

**The hold runs last because committing mutates the restored planner**, and every pass above it has to read the restore as the restore left it. That ordering is written at the call site in `Program.cs` and it is the first thing to check if a verdict starts disagreeing with itself.

Over the whole capture, `AuditKnowledge.Audit` does something different in kind: it pairs each shot's *predicted* hits against what the world recorded afterwards. Predicted bodies arrive as live NPC slots and landed damage as stable identities, so the `npc-spawn` and `npc-death` records join them across the capture; a prediction matches a landed hit on the same body inside a tolerance on both the tick and the damage, whose two values live at the top of `AuditKnowledge.Audit` and nowhere else. Per projectile type it reports the pairing rate, the tick and damage error, the wall-contact surprise, and the residual learner's factor at the shot's tick. That is the calibration of the weapon knowledge measured against the world rather than against itself.

## Map

```
CombatAudit/
├─ CLAUDE.md              this guide
├─ CombatAudit.csproj     the mod under the `live` alias, plus three source files compiled in from siblings
├─ Program.cs             the entry point, the five ledger measures, and the `-combat-audit.json` sidecar
├─ RestoreSnapshot.cs     one snapshot into the host: tiles, actors, knowledge, region, coordinate shift
├─ AuditHost.cs           the headless world — sized per snapshot, every brain static reset between them
├─ AuditSearch.cs         the replay and the exhaustive front, with the regret and generator attribution
├─ AuditWeights.cs        each objective halved and doubled, and what moved
├─ AuditKnowledge.cs      predicted hits against landed damage, per projectile type
├─ AuditHold.cs           the shifted plan committed, and the commitment's own Validate run on the snapshot's tick
└─ SelfTest.cs            eight rows, each proving its own verdict by a mutation baked in beside it
```

`RestoreSnapshot.cs` and `SelfTest.cs` carry two-thirds of the lines between them, and that ratio is the shape of the tool rather than a defect: restoring a decision exactly is most of the work, and a tool whose whole output is a judgement has to prove the judgement can fire.

## Where it sits among the other instruments

```
the mod, in play ──► ExportCombatSnapshot ──► a `combat-snapshot` god's-eye event per decision
                                                      │
                                    ┌─────────────────┴──────────────────┐
                                    ▼                                    ▼
                            CombatAudit                          ../SessionReport
                     restores and re-decides it             reads the capture as a whole
                                    │                                    │
                      -combat-audit.json beside the capture ────────────►│
                                    │                        Read/DescribeCombatAudit.cs
                                    ▼                        renders the Combat decisions section
                            ../Ledger — five measures under instrument `combat-audit`
```

Three sources are compiled straight into this project rather than referenced: `../Ledger/EmitLedgerRows.cs`, `../SessionReport/Read/ReadGodsEyeEvents.cs` and `../SessionReport/Session.cs`. So this tool reads a capture through the session reader's own parser and files rows through the ledger's own emitter, and neither relationship goes through a printed line — which is the same rule the rest of `Tools/` keeps, because the moment one program parses another's output every print becomes an undeclared contract.

The division of labour with the session reader is worth stating plainly, because both of them are "the thing that reads a playtest". **The reader decides nothing about combat quality and this tool decides nothing about the capture.** `DescribeCombatAudit` renders this tool's sidecar and adds no judgement of its own; a missing sidecar prints as "unmeasured, not clean", because a Combat decisions section that vanished when the audit never ran would read as a clean fight.

## Operating manual

```
dotnet run -p:UseAppHost=false --project Tools/CombatAudit -- --self-test                   eight rows, exit 0
dotnet run -p:UseAppHost=false --project Tools/CombatAudit -- Telemetry/<stamp>.tsv         audit a capture
dotnet run -p:UseAppHost=false --project Tools/CombatAudit -- Telemetry/<stamp>.tsv --write and write the sidecar
                                          --snapshot N                     one snapshot by index
                                          --no-sweep                       skip the weight sweep
```

The first argument may be the `.tsv` or the `-events.jsonl` directly; handed the TSV it resolves the sidecar itself and also loads the telemetry, which is what the learner's per-tick factor and the `plan_cut` share are read from. Exit 2 means it could not be asked — no events file, or no arguments — and is neither a pass nor a violation. `--write` lands `<stamp>-combat-audit.json` beside the capture, which is the only thing `../SessionReport` reads.

`../verify.sh` runs **`--self-test` and nothing else**, and hands a non-zero exit to `ledger error` under instrument `combat-audit`. That is deliberate and it is also the tool's largest gap: the audit over a real capture is not part of the repository check, because `Telemetry/` is gitignored and no committed capture exists to run it against.

The five measures land under instrument `combat-audit`, suite `CombatAudit`: the share of decisions on the exhaustive front (up), the mean weighted regret (down), the share of committed decisions that still hold (up), the share of searches cut by budget (down, read from the capture's `plan_cut` column), and a calibration miss rate per projectile type (down). None is graded here, by the ledger's rule that a number nobody declared a pass line for must not become one.

## What the self-test proves, and why each row carries its own mutation

Eight rows, and every one of them asserts its verdict *and* the mutation that must break it in the same run, rather than leaving the mutation to be applied by hand and reverted:

```
a snapshot replayed with its live budget and proposals reproduces its plan   drop the verdicts → replay fails
the exhaustive audit finds a better stand and names no generator             hand it only the live proposals → nothing
the weight sweep reports what a doubled weight moves                         sweep at factor 1.0 → no moves
the knowledge audit splits matched shots from miscalibrated types            a mismatched shot must not pair
the hold audit reproduces the live commitment's verdict                      one release from each invalidation class
a count-capped search cuts at the same simulation twice and replays its cut   both runs cut in the same subsystem
danger commits the harm-prevention plan over the damage plan                  ← mutation not verified here
an unchanged scene keeps one plan across rescores                             ← mutation not verified here
```

The last two rows were added after the file's own docstring was written, which still describes only the first six; whether they carry a baked-in mutation is unchecked rather than known, and the table says so rather than implying a coverage this guide did not establish.

**A row that cannot fire and a row that correctly does not fire look identical in a row count**, which is why the mutation is baked in as the second assertion. The same discipline caught a live case on 21 September: a new check was traced directly to confirm it never fires on any scene in the suite, rather than inferred from the suite being green (`a19e1ca`).

## Traps

- **The audit is the first thing that stops compiling when combat's internals move, and a tool that will not build looks almost exactly like a tool with nothing to say.** On 21 September `0895e57` removed combat's private `PlanningBudget` for the shared `DecisionWorkBudget` and this project still named the old type at eighteen call sites; it failed to build, `verify.sh` handed the non-zero exit to the ledger, and the run carried one `error` row — "exited 1 without filing a red row of its own" (`5fd0b4f`). That row is the only thing between a broken audit and silence, so **an `error` row under instrument `combat-audit` is read as "the tool is broken", never as "combat is fine"**.
- **Never write a subsystem name into this tool as a string literal.** The shared allowance counts every consumer by name, and the migration above was tempted to put `"combat-simulation"` here to recover the old `budget.Simulations`. A second copy of that string would have kept compiling on the day the string moved and would have silently counted nothing. `SpendCombatDecisionWork` exposes the name it charges to and a `Simulations()` accessor over it, and the audit reads the same counter the search writes.
- **The replay forces no means and the sweep does, and swapping that breaks both.** The sampler draws deterministically at the restored tick, so a calm live search replays exactly; forcing means in the replay would price it at the posterior mean against live samples and diverge on every calm snapshot. The sweep forces them, because noise-free comparison between two weightings is the whole point of a sweep.
- **The exhaustive reprice gets a fresh unbounded allowance, not the one the grid search just spent.** The committed plan and the grid have to be priced under the same conditions for the regret to mean anything; a reprice charged to an exhausted budget cuts instantly and reports the committed plan as *unpriceable* rather than as worse — which reads as a catastrophic regret and is an artefact.
- **The grid spirals out from the committed stand and stops at a cap, and the verdict says when it hit one rather than hiding it.** Past the cap the front is a lower bound and not the front, so `capped` and the stand count are carried on the verdict and printed; a regret read off a capped grid is a floor and not the regret. The cap's value lives in `AuditSearch.Grid`.
- **The margin ring around a restored window reads solid, on purpose.** Unrecorded space is unknown space, and the audit must not route through what the snapshot never saw. A route that looks absurdly constrained near the window edge is this rule, not a planner defect.
- **Every brain static is reset between snapshots and the list is explicit**, so a new learner, cache or ledger in the mod is a new line in `AuditHost.ResetStatics` that nobody will be prompted to add. Missing one means a replay reads the *previous* snapshot's flood or forecast and diverges for a reason the verdict will attribute to the decision — the same class of order dependence that bit `../EngineReplay`'s per-case reset through `CompanionPreferences.Current`.
- **`SelfTest.cs` prints `PASS` and the verdict-boundary check does not catch it**, because `check-navigation-boundary.sh` matches `PASS` only at the very start of the written string and these lines are indented two spaces. It is not a boundary violation in substance — every row calls `EmitLedgerRows.Pass` before it prints, so the ledger sees it — but nobody should read the check's silence here as the check having looked.
- **A snapshot written before a field existed restores that field as absent, which skips its check rather than failing it.** That is the rule for every extension to the snapshot DTO, and `a19e1ca`'s recorded-centre field is the worked example. The schema itself is a flat integer, `ExportCombatSnapshot.SchemaVersion`, currently 1, and a mismatch throws `AuditException` and reports the snapshot as unrestorable rather than guessing.
- **Modded content does not resolve headless and is collected rather than fatal.** The replay runs at priors for those weapons and the verdict says so; a divergence on a snapshot with unresolved names is weak evidence about the brain.

## Gaps and planned work

- **The audit has never been run against a real capture in anger, and that is still the headline gap.** Every row it has ever filed comes from `SelfTest`'s own synthetic scenes. `Telemetry/` is gitignored so `verify.sh` cannot run it over a recording. The retained-course brain, wired on 21 September, was first played on 22 September (capture `2026-09-22_10-05-56-125`), and no `-combat-audit.json` sidecar for that capture exists in this checkout — that first play is the first real input this tool *could* see, and it has not yet been pointed at it.
- **Committing a capture, or backfilling one through `../backfill-capture.sh`, is what would turn the five measures into a trend** rather than five numbers produced once. Until then a scoreboard comparison on `combat-audit` compares nothing.
- **The exhaustive grid's cap is a constant with no measurement behind it.** It was chosen to bound the run and nobody has measured what share of real decisions hit it; the verdict reports `capped` per decision, so the first real capture answers this for free.
- **Two files here are size outliers and both are code, so both are plans and neither is an edit.** `SelfTest.cs` is 754 lines at 3.9× this folder's median and divides at the row — eight independent scenes sharing only their `Row` wrapper, which is the cleanest split boundary in the tree. `RestoreSnapshot.cs` is 534 lines at 2.7× and divides by what it restores: tiles and world, actors, knowledge and ledgers, the plan and its proposals. Neither is urgent; both would be cheap, and the reference surface is the project file's compile list plus the internal call sites, which is enumerable in one search.
- **Nothing here grades the calibration.** `AuditKnowledge` reports a pairing rate and a tick error per projectile type and the ledger files a miss rate as a measure; what miss rate means a flight law is wrong is undeclared, and it stays undeclared until a real capture gives somebody a distribution to declare it against.
