> Final bounded recheck: all six evidence-contract blockers closed. The initial findings and recheck are retained below. This was a reused Terra/medium source-review context. The [active plan](<../../proposal/Implement the Retained Course Brain.md>) owns the current specification. This review proves no native implementation or live gameplay outcome.

# Evidence review — `Implement the Retained Course Brain.md`

**Verdict:** the draft has the right evidence boundary and, unlike the earlier proposal, explicitly distinguishes bounded reconstruction from historical replay. Its G14/G15 gates, source/schema migration, event-loss disclosure, no-second-log rule and WorldRun disclaimer are strong. Five issues must be fixed in the plan before implementation; otherwise its random/interference examples can produce a coherent-looking trace that cannot establish ordering, attribution, or replayability. The optional notes improve diagnosis but do not block the one-run implementation.

## Must fix 1 — define the one native receipt pipeline and strike de-duplication

The draft assigns “native hostile-effect receipts” to `EnemyIntegration/` (`Implement…:180`) while the current producers are split: generic projectile/NPC effects are recorded in `Companion/Brain/Infrastructure/Diagnostics/ObserveNativeCombatEvents.cs:19-24`; companion hit lineage and learning are in `Interactions/Firing/TrackLandedHits.cs:171-180`; and player melee/item/projectile damage is already observed through `Companion/Progression/CreditKillsAndFights.cs:270-293` / its `CompanionPlayer.OnHitNPC`. The existing stream can therefore see the same NPC strike through more than one hook, with no course receipt identity.

The plan must name one `NativeEffectAdapter` owner and one exact per-strike receipt rule before files move:

1. `ObserveNativeCombatEvents.OnHitByProjectile` is the generic projectile source and provides projectile lineage.
2. `CompanionPlayer.OnHitNPC` is the required player-to-enemy post-strike source; `ModifyHitNPC` is only pre-strike classification and must not be receipt evidence.
3. Companion swing/projectile classification passes through the existing bracket/lineage mechanism, so the player hook neither steals nor duplicates companion damage.
4. Each physical strike obtains one native receipt ID and one actor attribution (`companion`, `player`, `external`, `unknown`), with links to generic observation only where both hooks saw it. The adapter, not SessionReport heuristics, suppresses duplicate credit.

Without this, G05's player hit during companion projectile flight cannot say whether a changed target life/pose came from the player, companion, both, or duplicated records. The course may correctly dirty its successor but cannot correctly censor intrinsic learning or allocate harm/work/XP once. This is a core acceptance requirement, not merely telemetry polish.

## Must fix 2 — make native phase and receipt draining an executable ordering contract

The plan correctly says tick alone cannot order a native hit and a brain decision in one frame (`Implement…:280`), but current `EventRecord` stores tick and writer sequence only (`RecordGodsEyeEvents.cs:418-440`). It has no native-phase field, and records are written synchronously from hooks. Section 3 says “drain native receipts” before observing (`Implement…:102-112`) without defining where receipts occurring after the drain but before/after a use become visible.

Add a precise contract: native hooks append immutable receipts to a bounded receipt queue with `(origin tick, native phase, per-phase sequence, receipt id)`; `CollectObservedEffects` drains only records earlier than a named brain-cut watermark; effects after that watermark are applied next tick. The course trace records that watermark and the phase/order of both expected use and receipt. The queue must define the overflow result as a `recording-gap` plus affected-course uncertainty, never silently discard a receipt while allowing later effects to be called reconciled.

This is required for the player-hit/projectile-in-flight example, same-tick incidental interaction, and the stated rule that a late fact can veto a use but cannot launch an unaccepted substitute. An event file's serialization order is not a substitute for a game-phase ordering model.

## Must fix 3 — turn “typed JSON payload” into a concrete envelope migration and reader rule

Section 8 requires versioned typed payloads (`Implement…:284`), but the actual God’s-eye envelope contains only string `detail` (`RecordGodsEyeEvents.cs:439-440`); existing readers parse semicolon fields via `Field()` and already use malformed/sequence/closure coverage (`ReadGodsEyeEvents.cs:29-82`, `CheckOffersAttemptsAndGrants.cs:302-321`). The draft must state whether it adds a `payload_json` plus `payload_version`, or places a JSON value in `detail`, and specify the old-reader/new-reader compatibility path.

The recommended minimum is add envelope fields `payload_kind` and `payload_json` while preserving legacy `detail` for existing event kinds. `ReadCourseChronicle` validates typed payload version per event kind and returns **missing/partial coverage**, not an empty candidate/event list, for unknown/malformed course payloads. Every new event writer must use one serialization helper; no producer constructs JSON text itself. `VerifyGodsEyeEvents` needs round-trip and malformed-version mutations.

Otherwise the plan's most important promises—exact candidate coverage, resource intervals, effect allocation and spatial footprint—are strings whose parse failures can be misread as no candidate, no effect or no invalidation.

## Must fix 4 — bound snapshots by declared read footprints, and split exact replay from explanation

The draft’s disclaimer is right: WorldRun is not original NPC/projectile/item chronology reconstruction (`Implement…:310`). It also says a snapshot is reproducible only if dependencies are present (`:300-306`). The missing enforceable bridge is that `Bind`/`Predict` receive `hypotheticalState` (`:98`) but the contract does not require them to return an actual read-footprint manifest. A writer cannot later prove that a snapshot contains every fact the decision consulted merely by serializing “relevant” facts after the fact.

Require `ObservedBrainFacts`/hypothetical overlay access through tracked fact readers. `Bind` and `Predict` return a footprint digest plus the keys/versions actually read; `CourseComparison` records that digest; `CaptureDecisionSnapshot` captures exactly those key/value/version records and reports each missing/evicted one. The snapshot result must be one of:

| Result | Reader/replay meaning |
|---|---|
| `exact-input-complete` | Pure snapshot driver may reproduce this decision with matching source/model/policy/scheduler versions. |
| `explanatory-partial` | Timeline can show recorded intent and causal chain, but driver refuses exact replay and names missing dependencies. |
| `historical-unavailable` | Old capture/no snapshot; only raw observed history can be reported. |

This prevents “full proof” claims from a bounded ring. It is crucial for random target motion: an omitted enemy movement context, terrain chunk, player intent revision, cached model revision or scheduler cursor can change the decision without appearing in a reconstructed trace. The plan already names most inputs at `:302`; it needs the tracked-access rule that proves completeness.

## Must fix 5 — specify diagnostics backpressure without a motor-blocking writer

The proposal promises bounded encoding/writing that cannot block the motor, reserved capacity for selected/native/gap records, and a gap summary on overflow (`Implement…:304`). Current `GodsEyeEvents.Write` directly `writer.WriteLine(JsonSerializer.Serialize(...))` from the producer and catches failure only after it happened (`RecordGodsEyeEvents.cs:418-437`). That does not satisfy the promised nonblocking property, and current `Dropped` largely describes a disabled writer rather than a bounded queue's category-specific loss.

Specify a diagnostics-owned bounded queue/ring, its sole flush point and ownership, and the reporting semantics before code begins. This is compatible with section 3’s prohibition on an asynchronous **planner** thread; it must say whether the writer is flushed synchronously at a bounded post-tick point or by a diagnostics worker, and how close/unload drains or marks remaining records. Reserve capacity must be real partitions or admission rules, not a promise. The TSV/end marker needs per-kind offered/written/coalesced/dropped and `capture-complete` status; SessionReport must downgrade all receipt-dependent conclusions after a relevant gap.

This blocks a false “complete” trace under the high event rate of random motion, multi-projectile combat, terrain edits and candidate funnels—the exact scenes the plan uses to prove global diagnosis.

## Must fix 6 — G01 must not overclaim reconstruction from the 0.30.6 capture

G01 asks to reconstruct collect/torch decision inputs from 0.30.6 ticks 7773–11125 “where available” (`Implement…:318`). That capture predates course ids, binding identities, typed opportunity coverage, source footprints, receipt watermarks and event payloads. It can reproduce the observed swap signature, selected action/raw scores, destination samples and existing attempt/effect records; it cannot prove a new retained-course decision would have seen all sites, had an executable prefix, or held a correct dependency set.

Split G01 in the plan:

* **G01a historical regression reader:** reproduce the capture counts/signature and mark every unavailable course field as historical-unavailable. It protects against rewriting the evidence.
* **G01b capture-shaped production fixture:** construct only the recoverable geometry/identities/effects in EngineReplay, run the new writer→chronicle pipeline, and assert the no-unchanged-pair-reversal and completion properties. It is not a replay of the live capture.

The distinction follows the existing reader discipline: missing sidecar/schema coverage is a skip rather than clean (`CheckEvents.SidecarUnavailable`, `CheckOffersAttemptsAndGrants.cs:424-438`) and WorldRun refuses to invent absent route capability facts (`ReadRecordedRoute.cs:14-19`).

## Optional but high-value notes

* **Make producer locators stable.** `producer symbol` in `Implement…:280` should be a source-level locator (`assembly source revision`, type, member, domain contract version), not a line number. The folder-move table at `:257-272` must include the diagnostic producer locator references and reflection fixtures; it says this generally, but a course event’s locator is an input to diagnosis and should be validated by a source-map test.
* **Do not delete historical row fields until their reader adapter exists.** The deletion obligation at `:272` is right. Preserve old score fields as historical schema fields and add a versioned adapter; do not rename them as `course_*`, or old capture measures will acquire fabricated course meaning.
* **Record cost at the whole boundary.** G11/G14 and `:342` should log source time, snapshot serialization/enqueue time, flush time, queue depth and drop reason separately from `decision_work_used`. Current `record_ms` is a prior-row timing and cannot prove that a new event writer stayed out of planning/motor time.
* **Effect allocation needs one deterministic rule in the payload.** Section 8 says a receipt may satisfy several needs but amount is allocated once (`:306`). Record the allocation map plus unallocated remainder and its policy revision, so SessionReport can test conservation instead of recomputing a possibly changed rule.
* **Folder ownership wording needs a correction, not more layers.** Keep physical hooks where tModLoader requires them, make Diagnostics the event writer and Observation the receipt consumer. `EnemyIntegration` may provide hostile-specific attribution data, but should not become a second receipt writer. This preserves the actual current boundary while satisfying the new player-to-enemy hook requirement.

## What survived review

The draft correctly prohibits native effects from becoming predicted success, preserves unknown reach rather than forcing absence, keeps receipt observation/motor work outside the planning budget, names the global terrain invalidation defect, and requires reader-side gap handling. Its `WorldRun` statement agrees with `ReadRecordedRoute.cs:4-19`: that tool drives a contiguous player track through a named saved world, not original dynamic entities. With the six fixes above, the proposed instrumentation can diagnose retained-course decisions globally without pretending that bounded logs reconstruct every unrecorded fact.

## Recheck after the evidence-contract revision — all six required fixes are now present

This recheck read only the revised native-receipt, diagnostics/replay and G01 paragraphs in `research/proposal/Implement the Retained Course Brain.md`, plus the actual player-strike hook. The initial six findings are closed as plan defects:

| Initial finding | Revised plan evidence | Recheck result |
|---|---|---|
| One receipt pipeline / no duplicate strikes | `:110-118` places `CollectNativeEffectReceipts` in Observation, keeps hooks in their real integrations, names `ObservePlayerStrikesForExperience.OnHitNPC` as the player-to-enemy post-strike hook, and extends the existing bracket to a nested-safe shared strike token. `EnemyIntegration` is explicitly not another writer. | **Closed.** The corrected hook name matches `Companion/Progression/CreditKillsAndFights.cs:289-293`. The plan correctly rejects tick/target/damage de-duplication. |
| Native phase and drain ordering | `:116` defines immutable queue records with world epoch, origin tick, actual observed phase, global ordinal and receipt id; step 1 drains through a captured watermark and step 9 rechecks later affected-object versions/ordinal. | **Closed.** A late same-tick observation has a stated route to veto an accepted use without rewriting the earlier snapshot. |
| Typed envelope migration | `:313` adds `payload_kind`, `payload_version`, JSON `payload`, phase, ordinal and watermark, preserves historic `detail`, requires one serializer, and makes malformed/unknown versions partial coverage. | **Closed.** This is now a schema/read/fixture migration rather than an intention to put structured facts into a string. |
| Bounded snapshot versus full proof | `:108` makes pure evaluation use a tracked fact reader and return exact key/version/digest manifests; `:331-337` makes exact replay conditional on all tracked reads and names `exact-input-complete`, `explanatory-partial`, and `historical-unavailable`. | **Closed.** The explicit source-boundary check against direct `Main.*` reads is the enforceable bridge that was missing. |
| Nonblocking diagnostic backpressure | `:333-337` separates gameplay receipts from diagnostics, gives `QueueDiagnosticRecords` required/optional/gap partitions and sticky incompleteness, and assigns streams/serialization/teardown to one `FlushDiagnosticRecords` worker. It also correctly limits the claim to bounded game-thread waiting rather than forcible managed-I/O termination. | **Closed.** Per-kind offered/enqueued/written/coalesced/dropped counters and incomplete-first footer semantics prevent a loss-free claim after silent loss. |
| G01 historical overclaim | `:351-352` separates G01a capture-count/signature reproduction, whose unavailable course fields are `historical-unavailable`, from G01b’s declared capture-shaped native fixture and explicitly denies exact replay. | **Closed.** This matches the existing route-reader rule that absent recorded facts are not inferred. |

Two details are particularly strong. The plan now separates the gameplay receipt queue from optional recording, so diagnostic loss cannot make the live course forget a real strike; and it names producer ordinals in TSV/sidecar records, so a worker’s scheduling cannot manufacture causal order. The added G14 loss/corruption cases and native callback-order fixture set remain necessary implementation gates, but there is no remaining observability, replay or budget blocker in the written design.
