# Decision fixtures distinguish core contracts from native behaviour

The retained-course plan's gates require production paths. Pure policy and lifecycle fixtures here call the same classes compiled into the mod, through the `live` alias. They establish the stated arithmetic and state transitions, not real tile effects, combat accuracy or the full integrated brain.

```text
DecisionMaking/
├─ CLAUDE.md               evidence scope and execution
├─ VerifyCourseCore.cs     fixed objective, identity, bounds, resources, repair and budget cuts
├─ VerifyProjectionContracts.cs causal effect reads, resource phases, fair discovery and receipt retirement
├─ VerifyCourseOrderProjection.cs one-operation cuts, completed-query extensions, empty-order cost handoff, frozen-input guards, shared region-curve equivalence, and which end of the course the consequence forecast is priced from
├─ VerifyCompanionshipForecast.cs region integration, subdivision invariance, whole-course suspension and captured arrival evidence
├─ VerifyCourseTravelScheduling.cs native query fairness, model-catalogue completion, deferred terrain edits, capacity and reset
├─ VerifyNativeConsequencePricing.cs the native provider's honesty: travel and enemy motion suspended and forwarded, a hostile on the flight path priced as real harm and the same hostile off it costing nothing, the tail unresolved even with every enemy modelled, each order priced from its own start
├─ VerifyWholeCoursePricing.cs one non-empty course through the real binder and the real forecast: a contact priced during the prefix, the origin handoff, and the live pricing branch asserted rather than assumed
└─ VerifyCourseBindingExecution.cs a bound step's activity and position request, and every source having a binder at all
```

**Two of these files were each other's blind spot, and `VerifyWholeCoursePricing` is the row that crosses them.** `VerifyNativeConsequencePricing` drives the real forecast and prices an empty order in every row, so the state a course starts from and the state it ends at are the same value; `VerifyCourseOrderProjection` drives real multi-step orders through a stand-in forecast that ignores its successor. A binder handing the post-course state to a forecast whose contract wants the pre-course one was therefore invisible from both sides at once, and for an order of duration T the whole prefix went unpriced. The new row binds one real step, prices it through `ForecastCourseConsequences` with `RetainCourseModelQueries` answering travel and enemy motion, and measures a hostile contact at tick 31 against an arrival at tick 67 — harm during the journey, which a forecast handed the end state cannot produce. Restoring `state.Fork()` at the binder's handoff reddens it.

Three things that fixture had to learn, each of which is what a stand-in binder lets you skip and a real one cannot:

- **A binder binds against the captured travel, it does not assert numbers at it.** Hard-coding a travel duration is refused as `bound-travel-duration-changed` and letting the arrival pose default to the requested point is refused as `bound-arrival-pose-changed`, because native arrival has slack and must not teleport the hypothetical body onto the point that was asked for. `ReadCourseTravel.Read` is what every real binder uses and what this one uses.
- **A suspended binder must name what it waits on.** Returning `Unresolved` without a `RequiredTravel` request parks the candidate for ever; the fixture stalled at "pending=1, asking for nothing" until the request was carried on the `BindingResult`.
- **Only a snapshot the owner actually appended to is a model extension.** Re-handing the same snapshot is refused by contract, and rightly: a search told its inputs changed when they did not restarts a frozen comparison for nothing.

**The projection clock is relative to the decision and always starts at zero**, which is worth knowing before writing another scene here. `DecideCourseEachTick` builds its initial state with no start tick, and `SampleContactTrajectory` refuses any contact trajectory whose first pose is not at tick zero. A scene starting elsewhere is not more realistic — it is a different clock, and it throws at that guard.

**These eight files are reached two different ways, and the flag does not cover them all.** Five run together under `--retained-course-core` — `VerifyCourseCore`, `VerifyProjectionContracts`, `VerifyCourseOrderProjection`, `VerifyCompanionshipForecast` and `VerifyCourseTravelScheduling`. The other three are named directly in `../Movement/VerifyEngineMotion.cs`'s default-case table, each as its own case with no flag of its own, and those names are what the scoreboard and `--case` match:

```
a course is priced for the harm it flies into and still cannot certify that it harms nobody   VerifyNativeConsequencePricing
a non-empty course is priced end to end from the tick it starts at                            VerifyWholeCoursePricing
a bound course step names the activity that performs it and the place it happens              VerifyCourseBindingExecution
```

So a change to one of those three is not exercised by the flag, and a reader looking for them under `--retained-course-core` will conclude they do not run. Run the five from the repository root with `dotnet run --project Tools/EngineReplay -p:UseAppHost=false -- --retained-course-core`, and the three through the default suite or `sh ../../verify.sh --case "<fragment>"`. Each row states the relevant plan gate, but a row covering a core seam does not complete that gate's native scene. Generated native event sequences, matched reactive/exact comparison, model fidelity and exact snapshot replay remain distinct obligations of the full plan.
