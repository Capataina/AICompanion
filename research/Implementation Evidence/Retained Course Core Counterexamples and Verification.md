# Retained course core counterexamples and verification

This is implementation evidence from 19 September 2026, beginning with the working implementation based on `0d7d722` and checkpointed through `0d44fb1`. It records the attacks and the checks they produced so a future investigation inherits their conclusions. It is not a completion report for [the implementation plan](../proposal/Implement%20the%20Retained%20Course%20Brain.md). At this checkpoint the live chooser in `Companion/Brain/Infrastructure/Selection/ChooseBehaviour.cs` still selects legacy activity offers; the course core is not yet the gameplay authority.

## Green arithmetic did not establish a safe publication boundary

The first independent review ran seventeen core checks successfully, then found counterexamples outside their inputs. The common defect was that a caller could hand the owner a record labelled complete without passing the invariants that made it safe to execute.

| Attack | Counterexample | Implemented response | Evidence scope |
|---|---|---|---|
| Re-observation became another chooser | An accepted `[A,B]` became `[B,A]` through `ObserveRemaining`, without a semantic boundary or comparison. | Re-observation preserves every accepted concrete use and its order. Removal, substitution and exchange go through publication. | `VerifyCourseCore.ObservationCannotSelect` calls the actual owner and checks that rejection leaves its revision untouched. |
| Partial repair erased its remaining work | `PublishTail` accepted a revision while descendants remained in the invalidation queue, then cleared the queue. | No publication occurs with pending invalidation. Every retained binding/effect must have complete current dependencies before an atomic replacement. | `UnfinishedRepairCannotPublish` cuts traversal and separately supplies an unresolved suffix. |
| Resource names duplicated the hand | Overlapping `Hand` phases with different capacity keys were each charged against a separate capacity of one. | Body and hand have one physical capacity each. Their phases must lie inside the binding's absolute time interval. | `VerifyProjectionContracts` checks keyed overlap, past intervals and repeated binding identity. |
| Effect dependencies stopped before binding dependencies | A binder read a predicted impact, but the reverse index held only binding IDs; the effect's own fact reads never dirtied its successors. | Bindings and effects enter one globally unique causal graph. A hypothetical read names its effect parent, and the effect names its own reads and owner. | Effect-only fact invalidation reaches the dependent binding. The graph also refuses a future effect being consumed before its due time. |
| An evidence label became exact damage | Damage/gap carried nominal numbers and a `ModelBound` label; both objective bounds subtracted the same nominal cost. | Numerical damage/time and gap ranges propagate into the objective bounds. An absent range remains uncertain. Encounter interruption compares justified self-harm bounds. | `CostUncertainty` checks a label without ranges and nondegenerate ranges. |
| Dispatch consumed the whole minimum slice | A one-operation slice spent that operation on dispatch, leaving every source unable to examine a site forever. | Dispatch checks the allowance; the actual source operation spends it. | A repeated one-operation budget reaches both finite source sets. The older fairness assertion was corrected to allow a completed source to begin another sweep while still requiring every distinct candidate. |
| Deduplication grew with world lifetime | Receipt sets and effect confirmations accumulated until world reset. | The course ledger can retire behind a fully completed issued-ID floor while rejecting replay below that floor; retired effects lose their confirmation entries. | Pure ledger lifecycle checks pass. The live coordinator still has to supply and advance the correct completed floor. |

The source owners are `Courses/RetainCourse.cs`, `CompareCourseOutcomes.cs`, `ProjectCourseEffects.cs`, `TrackCourseDependencies.cs` and `Opportunities/BindOpportunity.cs` and `DiscoverOpportunities.cs`, all under `Companion/Brain/Infrastructure/Selection/`. Their folder guides carry the enduring contracts; this file records why the first tests were insufficient.

The expanded focused runner printed twenty-eight rows and exited zero:

```sh
dotnet Tools/EngineReplay/bin/Debug/net8.0/EngineReplay.dll --retained-course-core
```

That command was run after a successful EngineReplay build. It establishes those component inputs and state transitions. It does not establish the capture-shaped torch/drop scene, all-domain candidate discovery, the actual executor's adherence to a course, or improved gameplay.

The second independent review found that every outstanding effect still lived under an executable step. Advancing past a completed shot therefore lost its unresolved impact, while retaining the impact kept the completed shot executable. Receipt retirement compounded that coupling: a partial confirmation could be forgotten when a replacement course no longer contained the old effect. The owner now separates explicitly dispatched effects from pending uses, advances on native-use completion, retains original claimed totals across replacement, and rebases outstanding timing against the new observation origin. A native terminal event, rather than selection, ends physical ownership. The owner-level fixture confirms three of eight units, replaces the course, retires receipt IDs, then permits only the remaining five units to be allocated.

The same review found a narrower uncertainty defect: a parent with earliest/nominal/latest times of 5/10/20 could enable a child at 13 because causal validation used nominal time. Both projection and dependency construction now use the parent's finite justified latest time, with a paired later-child control. An older fixture initially failed because its child also preceded its own binding; moving its binding before the child isolated the intended parent-timing violation. The subsequent focused runner printed thirty-one rows and exited zero after a successful EngineReplay build. Independent re-review of these changes remains separate from that fixture result.

## A valid present action and an invalid future step require different responses

The third review found three publication-state defects after the physical/executable split. A changed target manifest could leave an issued projectile's old bounded knockback prediction certified; a completed action's boundary could remain available after the next retained action began; and completed repair could leave a dirty flag that authorised an unrelated worse tail. The owner now censors stale physical predictions, explicitly consumes the old boundary when the next use begins, and closes repair authority when a refreshed graph is published. The fresh focused runner printed thirty-five rows and exited zero, including those discriminators.

Strengthening refresh validation then exposed a fourth defect: `ObserveRemaining` treated every rejected projection as an invalid current use. In an accepted `A → B`, changing only B's cargo/light/target facts could therefore cancel A. Validation now identifies the failed node, preserving a valid prefix while queuing suffix repair. A separate physical-chain counterexample concerned two already-issued effects: when P misses, dependent Q still exists, but its old conditional outcome cannot remain certified. Terminal handling retains Q's identity and receipt allocation while censoring its predicted delta; the old graph remains available to propagate repair. These added fixtures were written after the thirty-five-row run; that earlier run is not their proof.

The route search exposed another missing borrower: its elapsed-time check did not charge node expansion to the deterministic shared allowance. `FreeSpaceSearch.Advance` now spends before removing a frontier node, and a fixture supplies zero operations followed by single-operation slices. It checks both refusal under an exhausted allowance and eventual arrival without resetting the frontier. A travel-capture fixture also drives the actual route/steering/contact law around an intervening wall and preserves arrival momentum into the next projected leg. The subsequent EngineReplay build exited zero, and `dotnet Tools/EngineReplay/bin/Debug/net8.0/EngineReplay.dll --retained-course-core` printed thirty-nine rows and exited zero, including suffix preservation, issued descendant censoring and both travel checks. These are focused component checks, not whole-brain performance acceptance.

A later inspection found that clamping a rebased expired impact time to zero before testing expiry made an overdue effect look due now when a new projection began at zero. The censor now compares the signed remaining time before clamping its display interval. The subsequent forty-row core run exited zero and includes that expiry discriminator; the preceding thirty-nine-row run alone did not prove it.

## The native adapter must precede each domain's private winner filter

Inspection of the live path found `Brain.Tick → Chooser.Choose → activity.Prepare → OwnCurrentActivity → Execute → Positioner → grants → native interaction`. Each private winner filter below would hide alternatives from a course selector attached after preparation:

| Domain | Concrete alternatives originate at | Actual effect authority | Integration requirement |
|---|---|---|---|
| Drops | `Senses.Loot.Pickups`, before `CollectNearbyItems.PrepareDrop` returns its first candidate | Successful native bag transfer in `CompanionNPC.CollectTouchedItems` | Capture item lifecycle, landing, legal contact pose, capacity and allowance; touching a drop is a receipt, not a predicted pickup. |
| Lighting | Dark regions and `LightUsefulArea` site enumeration | `PlaceTorches.Place` | Capture individual sites and their shared persistent deficit cells; placement alone is not proof of useful coverage. |
| Pots | The world-work scan before collection's private pot/drop comparison | Native pot mutation | One physical multi-tile container owns one need; unknown contents cannot pay for the trip. |
| Ore | `OreFinder` candidates and bounded connected membership, before `FindNearest` | `TileMiner.Swing` progress and removal | Preserve original native work units, membership coverage and the actual admitted tool pose. |
| Trees | `TreeFinder` trunk candidates before its nearest choice | `TileChopper.Swing` | Preserve trunk identity and actual axe/protection/access predicates. |
| Combat | `SearchAttackPlans` concrete front before `FightEnemies` keeps one plan | `FireDueUse.Release`, projectile/swing native effects | Bind target generation, weapon, stand and aim; a fallback cannot execute an unaccepted substitution. |
| Company | `ChooseMeetingPlace.Candidates` and local intent-region geometry | Observed body progress | Supply reunion/companionship consequences without inventing a useful-effect reward for movement. |

Initial adapter drafts did not meet this contract. Assistance capture introduced an arbitrary pot radius, duplicated a multi-tile pot, omitted native contact/work admission, and incremented every fact version on unchanged captures. Combat capture represented missing confidence as zero expected damage, which would make a useful attack lose the shared comparison. These drafts were rejected for integration. The required distinction is ordinary nominal model evidence versus a justified bound; zero is an amount, not a notation for uncertainty. Reusing real domain predicates and emitting stable semantic facts is part of the implementation, not a later telemetry improvement.

## Native receipt identity needs two orders

Strike IDs are issued before native execution, while observation ordinals record when effects become visible. A nested hit can receive the later ID and complete before its parent. Draining by an issued-ID ordering cannot substitute for draining through the snapshot's observation boundary. `CollectNativeEffectReceipts.DrainThroughOrdinal` and the nested synthetic collector check preserve the observed child-before-parent order. Overflow records a dirty-state gap instead of implying there were no effects.

Native pick/axe progress and removal also have different units. A hit-table damage delta cannot share a field's meaning with a removed-tile count of one. The producers now emit separate progress and removal receipt kinds. A removal is still not a measured loot transfer or a proof of original vein work units; census/receipt allocation must establish that relationship during integration. Cargo still reports an unknown world-item generation rather than inventing one from its reusable slot.

The focused observation runner exited zero. Its nested-order and lifecycle seams are synthetic invocations of the real collector, not a proof of every Terraria/mod-loader callback order:

```sh
dotnet Tools/EngineReplay/bin/Debug/net8.0/EngineReplay.dll --retained-course-observation
```

## A test can conceal the same second-budget escape it is meant to catch

The first G04 fixture measured an entire combat search as the opener's cost, then granted that total plus another operation. It failed its own premise that broad search was cut. It also gave firing a fresh budget in the same observation tick, which could have hidden the live defect even if the first assertion passed.

The corrected fixture isolates the current-stand, depth-one calculation, cuts the broader search with the same decision allowance, then attempts firing inside that same allowance. It subsequently exited zero, measuring twenty-two opener operations, cutting after twenty-three, retaining `FireFrom` and firing. A further cold-instance control cleared both aim caches and constructed a new equivalent companion after calibration; it also exited zero with the same twenty-two/twenty-three operation result and a fired attack. `FireDueUse` reads `LimitPlanningWork.Current`, so giving the hand a fresh allowance in the same tick remains an invalid test.

An earlier opener implementation returned immediately after finding a useful shot. That prevented the normal planner from comparing firing positions and made existing spread, floor-roller and multi-segment combat cases fail. The opener now seeds the candidate pool while broader comparison continues. The subsequent `dotnet Tools/EngineReplay/bin/Debug/net8.0/EngineReplay.dll --attack-planning` run completed all nineteen rows and exited zero, including those cases. Its internal crowd-cost row reported cached p50/p90/p99 of 11.202/11.610/11.647 ms and uncached 1005.868/1013.780/1014.283 ms. These are standalone fixture measurements with lifted allowances, not a before/after result or proof of the whole live brain's frame cost. The earlier interrupted default-suite attempt is not verification.

## Recording coverage needs a complete control before damaged variants

The diagnostic transport checks passed for preserving offer order, sticky overflow and refusing replacement of an active writer. Additional injected-sink checks now pass for normal closure with row count, a terminal write held beyond its close deadline, an injected write failure, and retaining in-flight byte reservations while charging every legacy envelope string. The timeout case holds the terminal write itself: holding an earlier row cannot expose a clean footer computed before the deadline and written afterwards. A later incomplete footer overrides the earlier status. The focused runner printed eight rows; the real God's-eye event fixture wrote seventy-one records and passed its chronology assertions. These checks do not establish all course transition events or exact decision replay.

The snapshot reader originally accepted digest-only facts and could let one complete snapshot conceal another incomplete one. It now requires actual values matching their hashes, one expected/captured entry per key, and matching snapshot/event timing context. Null, scalar and array snapshots are partial evidence. Null manifest entries, null keys, duplicate manifests, changed value digests and sequence gaps are exercised beside a complete writer-format control. The initial mixed-snapshot fixture had different phase case in its supposedly good envelope, so a partial-only assertion could pass for that unrelated reason; the complete control prevents that class of false proof.

The SessionReport self-test subsequently exited zero, printing forty-four assertion groups and reproducing seventeen pinned before-numbers with no named skip against `2026-09-15_08-30-31-684.tsv`:

```sh
dotnet Tools/SessionReport/bin/Debug/net8.0/SessionReport.dll --self-test
```

`exact-input-complete` currently validates the captured manifest contract. It is not an executed replay, and it cannot certify a producer's undeclared reads. Input/model/scheduler capture and a replay of the complete live course decision remain implementation obligations.

## The remaining integration cannot inherit these passes

The first local checkpoints preserve the implementation's boundaries:

| Commit | Responsibility | Fresh focused evidence before staging |
|---|---|---|
| `d59bf69` | Course model, retention, projection, shared budget and captured travel | Forty core rows, exit zero. |
| `afca7df` | Native receipts, immutable capability facts, continuation geometry and motion evidence | Observation checks, exit zero; assembled EngineReplay build, zero errors and thirty-nine warnings. |
| `d7ae891` | Ordered bounded diagnostic transport and course chronology reader | Eight recording rows including seventy-one native writer records; SessionReport forty-four assertion groups and seventeen pinned values, all exit zero. |
| `0895e57` | Combat shared allowance, useful opener and exact-use adapter | Shared-budget and opener checks, two binding/damage rows and nineteen existing attack-planning rows, all exit zero. |
| `0d44fb1` | Sliced native drop/ore capture and immutable discovery | Ten opportunity rows including native mutable-stack and native ore progress/removal cases, exit zero. |

These checks ran on the assembled working tree. Individual partially staged snapshots were not separately built, and the whole-project suite was not run at this checkpoint. Independent reviews above apply to the core revisions they examined; the later native integration has not inherited a fresh independent verdict. Additional reviewer dispatch was unavailable after the runtime's agent-thread cap was reached. The unfinished lighting capture/projection, tree enumeration helper and remaining integration are outside these five checkpoints. No commits were pushed.

The full plan remains open: concrete sources and binders across every domain, a real world census and course projector, the single live chooser and accepted incidental uses, execution/receipt/grant lineage, combat successor fidelity and interference learning, global budget/frontier lifecycle, complete God's View producers and replay, capture-shaped and matched-policy experiments, whole-suite checks, and final live acceptance. Optional Modules remain excluded. No game launch, packaging or user gameplay acceptance occurred for this implementation checkpoint.
