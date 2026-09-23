# Implement the retained course brain

**Status: selected design, ready for a later end-to-end implementation instruction; no gameplay implementation is claimed by this document.** This is the single active proposal, formerly Proposal 05. The owner selected its direction on 19 September 2026 and requested this complete plan. Implement the entire core and its headless evidence in one coding run, with internal dependency gates and one final live acceptance. Intermediate playtests are not prerequisites for completing successive pieces of this plan.

The product remains [README Expected Behaviour](../../README.md#expected-behaviour). The [retained-course rationale](<../Decision Architecture/Retained Course Design Rationale.md>) preserves the investigation, all six approaches, the 32-row disposition and measured captures. The [six architecture attacks](<../Decision Architecture/Attack Six Complete Brain Architectures/CLAUDE.md>) preserve the objections and defeated alternatives. [Archived proposals](<../Historical Evidence/Archived Brain Proposals/CLAUDE.md>) preserve 01–04, including the latest owner-edited 04. They are historical evidence, not parallel implementation instructions. This document supersedes the rationale's unresolved-design paragraphs wherever it makes a concrete first-version choice.

## 1. Deliver useful work while correcting the future from what actually happened

The brain keeps a **course**, a revisable sequence of concrete useful effects, their enabling actions and their resource use. A purpose such as lighting a pocket or working a vein can survive a changed tile, route or firing position. Each physical application has a separate **binding**, the exact object, pose, weapon/tool and facts that make the next action executable.

The behavioural promise is conditional and checkable: **while a worthwhile course has a valid next action, unfinished improvement search does not stop that action; when its prerequisites fail, the brain prevents the invalid next effect, retains unaffected work and repairs from the observed world.** It never awards an anticipated hit, pickup, light patch or ore break as an observed success.

This addresses the recorded collect/torch swap through the whole decision process: several concrete sites enter comparison, their order survives, later effects are marginal, player relevance applies to the whole journey, and the executor checks each next use. It also addresses fighting without waiting for an ideal alignment, using the current pose to produce a useful opener while movement improves later opportunities.

This is not a claim of globally optimal planning, prediction of arbitrary random events, or diagnosis of unrecorded facts. An adversarial stream of genuine emergencies can prevent optional work forever. The finite stable-world contract is stronger: finite feasible worthwhile jobs, sufficient execution time and available observation/computation must not be starved by the brain's own identity churn, discovery order or repeated restarts.

### The first version includes every changing-world example

| Changed situation | Immediate response | What remains | Observable acceptance |
|---|---|---|---|
| The player hits our target while our projectile is in flight | Observe the new target state; dirty predictions that read its old trajectory. Keep the projectile registered until native resolution. Re-solve the next use from the actual muzzle and current target state. | Combat purpose and any still-useful firing region; unrelated torch/drop/vein work. | No following shot consumes the anticipated displacement as a fact. The trace separates our projectile, the external event if attributable, and the changed target observation. |
| A slime changes jump, an enemy dashes or teleports | Update its motion evidence. A discontinuity expires the old trajectory immediately; ordinary movement refines it. Cheap valid attacks continue; movement targets a usable region rather than chasing an exact forecast coordinate every tick. | The intention to deal with a relevant threat; a still-admitted pose and course. | No cached aim survives a discontinuity; no per-tick whole-course recreation; useful attacks occur whenever the real accepted opener permits them. |
| The player removes the next drop, ore tile, pot or torch need | Match the observed change to the effect or prerequisite. Mark externally satisfied, remaining work changed, or invalidated as appropriate. | Other bindings and the purpose if it still has useful work. | No double credit, no item-slot reuse, no compulsory trip to a vanished target. |
| Torch → drop → slime → ore was planned; the player jumps down a cave | Reassess the live companionship envelope and finish-plus-rejoin consequences now. Cancel obsolete optional suffixes; stop at the next mechanically cancellable boundary; following becomes the current step when the old course no longer fits. | Already placed light, collected cargo, launched projectiles and useful work still on the revised trip. | The old surface fight cannot hold the body until enemy death merely because it started. Unknown return reach is recorded as unknown. |
| The player weaves left/right without leaving the area | Update held direction immediately, lower journey coherence, and reprice forward projections. A heading revision alone does not revoke a valid local purpose. | Concrete work still inside the shared local continuation envelope. | Small reversals cause neither repeated release/reacquisition nor a delayed response to a later real departure. |
| A new threat arrives or a gear slot changes | Invalidate only dependent admission, harm or capability facts; compare the required repair and the best executable alternative. | Other work and valid physical motion. | Combat is not unconditionally step one. A threat the drone cannot hurt still affects safety/proximity without causing an impossible attack. |

## 2. The objective is explicit, shared and relative

The numerical first-version policy below is a design choice to implement and attack, not a result proven by the existing captures. The acceptance fixtures constrain it before it controls the live brain. No implementer is to invent private per-activity exchange rates, priority branches or an incumbent percentage while building it.

### Feasibility and product constraints precede preference

All candidates read the same capability, world-protection, inventory, encounter, intent and spatial facts. Proven forbidden actions cannot buy admission with reward. Unanswered reach or missing execution evidence is `Unresolved`, not forbidden and not executable optional work. A target dummy with no meaningful effect is not a combat task. During a boss/event, optional work is inadmissible, survival is compared before damage among meaningful combat courses, and player death does not end the encounter. Ordinary downing/recovery still owns the body through its established lifecycle.

The closed world-edit set and four handed slots remain. Attacks on hostiles fire only under an accepted combat step; mining and its approach remain quiet. README also permits using a bow or sword to break a pot: this is a separately accepted world-work binding with its own concrete pot target, native weapon resources and effect receipts, not permission for the incidental executor to attack enemies under mining/collection. The course switches into that pot-breaking step before use. Collateral native hits remain real attributed receipts and may change the future; they do not retroactively turn an unaccepted enemy attack into an accepted one. Passing an unsafe destination to the motor and hoping avoidance rescues it is not admission. Conversely, soft discomfort near walls never makes a physically passable two-by-two passage a wall.

### Compare discounted useful effects and future costs in one dimensionless policy

At each comparison, freeze one observed snapshot, one finite discovered opportunity set and one **comparison episode**. An episode changes for material opportunities, capability/policy changes or changed outcome needs, not for every tick of travel. Work not yet discovered is outside that comparison and is explicitly reported as coverage, not assigned zero worth.

Each unique effect `e` has a contextual nonnegative worth `w[e]`. Define the common time scale `T` as the diagonal of the shared **base**, zero-lead, resting intent-region geometry divided by the drone's current cruise speed, in game ticks. A body with no positive cruising capability uses its shortest positive native interaction cycle for local-only choices and marks travel unavailable; with neither capability it can only keep its legal resting state. T is at least one native game tick and fixed through the comparison episode. It changes only for a changed body capability or base region geometry, not because another candidate was discovered, a route was subdivided or the player pressed the opposite key.

This is an explicit product preference: effects delivered within one local traversal matter more than distant promises. It uses existing world/body scale instead of adding a hand-tuned stay bonus or per-activity time window. It is still a preference, not a theorem of physics. Record T and run sensitivity controls using the same scenes; changing base geometry or body speed must not silently change policy without a policy fingerprint.

For a course C, higher value is better:

```text
D(t) = exp(-t / T)
J(C) = sum_e [w[e] * D(time_to_effect(e, C))]
       - sum_h [predicted_damage(h) / actor_current_life * D(time_of_harm(h))]
       - integral_0^infinity [companionship_gap(C, t) * D(t) / T] dt
```

The actor in a harm event is explicitly the player or companion; each uses its own current positive life. Lethal predictions are not clipped into harmless small fractions. For a dead player outside a relevant player-protection objective, that actor's harm term is absent rather than divided by zero. Boss/event survival-first comparison still precedes this ordinary policy. A forecast kill reduces later harm events instead of earning the same prevented hit twice; nonlethal damage to a relevant threat also reduces its remaining combat-work need and can count without claiming that it prevented a hit.

An omitted effect contributes zero. Native partial work earns its actual fractional effect at the time it occurs, once. Persistent changes can affect later predictions, but are not paid again per tick. The companionship gap is the existing shared region-gap curve extracted without any private activity copy, zero inside the comfortable region. The integral is dimensionless because of division by T; event rewards and harm are dimensionless too. Piecewise coarse travel/interaction/rejoin intervals have analytic discounted integrals. After a proven rejoin the gap is zero. An unresolved tail carries an interval/unresolved value; it is never integrated as zero cost. Future harm beyond a model's supported interval is unknown, not assumed absent.

The following rules fix w for V1:

| Effect | Worth in the frozen episode | Avoided distortion |
|---|---|---|
| Persistent illumination | The uniquely covered persistent-darkness deficit divided by the total admitted unique deficit, multiplied by the existing player/current-work relevance curve. Stable world cells own deficit; pocket splitting cannot duplicate it. | Two torches covering the same cells cannot earn full credit twice. Carried light does not satisfy permanent lighting. |
| Accepted loot | Accepted quantity divided by total observed eligible quantity of that item type and prefix in the episode's world census. Every such item group has unit total worth under the existing item admission policy; capacity limits the accepted fraction. | One stack of twenty and two stacks of ten have equal worth. Splits, merges and inventory rearrangements conserve quantity; rarity/sell price is not silently declared player preference. |
| Ore/tree work | Fraction of original admitted persistent native work for the material/purpose group, multiplied by existing player-work relevance. Connected vein/trunk identity groups execution, while original work units own worth and survive splits. | Partitioning a vein does not create new full rewards. Native damage counts only while it persists. |
| Combat progress | Expected fraction of a meaningful hostile's original episode life removed, capped by remaining life, multiplied by its existing threat relevance; shared-life parts use one life holder. | Harmless dummies, overkill, repeated forecasts and duplicate in-flight damage earn nothing. |
| Pot/container | Existing admitted container interest as one unit of optional work. Until a supported contents distribution exists, assign no invented item reward; observed contents create actual loot opportunities. | A hypothetical rich drop cannot pay for its own pot trip, and the same hypothetical contents are not credited twice. |

All effect-class and harm coefficients are one in V1; this is an explicit equal-normalised-need prior, **not a physical truth that one item group equals the whole darkness deficit or a whole life**. Losing a fraction of remaining life has that dimensionless cost, whereas delaying a useful effect changes its discounted value rather than costing one utility unit per tick. The health charge never scales with the number of discovered jobs. These are visible product trade-offs and may need correction after evidence; they cannot be hidden in an activity. The extracted relevance curves retain existing product preferences. List their exact names, expressions and owners in the implementation diff and telemetry fingerprint. Strip distance, commitment, elapsed travel, multiplicative time, family rank and duplicate risk from them; route/time/risk enter J once. Delete unused old coefficients.

Normalisation denominators come from one frozen observation census, not the admitted candidate list, cache occupancy or producer enumeration order. An unresolved census carries its coverage and fixes the known denominator for that episode; learning more about the same snapshot cannot silently rescale already compared effects. Material new world evidence starts an explicitly logged new episode. Adding a candidate already represented in that census, splitting a stack or changing a waypoint count cannot change T, a harm charge or another effect's worth. Distinct item types/materials represent distinct useful outcomes under this default; deciding that one is rarer or more desirable requires an explicit product preference, not a hidden native sell-price proxy.

Safety breaks an otherwise equal ordinary-work comparison before time; then prefer earlier legal reunion, then retain the incumbent, then use deterministic opportunity identity ordering on cold start. These tie rules do not rank categories. G12's contrasting fixtures test this fixed policy; the implementer must not choose an alternative global objective or tune constants until tests turn green. A contradiction in those fixtures is reported as a plan defect with a concrete counterexample, while unrelated implementation continues. Passing code checks cannot conceal an unsolved product-policy conflict.

For the same effects, making every effect arrive no later with no extra harm/gap cannot worsen J. When some effects become earlier and others later, this discounted policy makes the trade-off; it does not claim to preserve linear weighted-completion ordering. A finite effect is always worth more than omitting the same effect at equal costs, including a lone job ending at its own return time. This avoids the rejected finite-horizon loss where an effect delivered exactly at H was worth no more than never doing it.

Past costs and delivered effects are removed from both alternatives. Real future turning, lost persistent progress, cooldown, setup, detours and rejoin remain. With fixed T and episode inputs, advancing the time origin multiplies all remaining discounted rewards and costs by the same positive factor; it cannot reverse their ordering. New facts can change the ordering, and are logged as such. There is no positive waiting reward and no reward for empty movement.

### Uncertain comparison must allow action without licensing churn

Every estimate carries nominal value, justified lower/upper bounds where available, and an evidence status. Unknown probabilities remain unknown; nominal prediction is a model, never labelled a confidence bound. At a normal **execution boundary**—native effect, current native action completion, exhausted current binding or invalidation—choose the highest nominal value among sufficiently modelled, executable candidates, recording uncertainty. Safety evaluates observed immediate danger and conservative known motion envelopes independently; unknown reward cannot buy a forbidden action.

Between boundaries, retain a valid current action unless (a) required repair removes its admission, or (b) another executable continuation proves a higher future value, including real switching effects, under both candidates' justified comparison bounds. Overlapping or unavailable bounds permit refinement and tail edits, not repeated uncertain cancellation of the current action. A boundary is generated by semantic execution: collection contact, completed placement, native tile-work pulse, weapon-use readiness/completion or exhausted purpose. Arbitrary route waypoints, additional samples and path subdivision never create reelection boundaries. Proof of improvement can interrupt continuous travel on any tick; direct safety/admission checks also run each tick. An urgent newly observed threat can invalidate the current harm/admission assumptions immediately; it need not wait for a distant waypoint.

This deliberately permits a fallible first choice. The next meaningful control boundary is an opportunity to correct it, not a bound on its consequences: an already-launched projectile, spent mana/cooldown, broken block or missed opportunity may have irreversible effects. It does not promise retention through genuinely superior arrivals or promise exact optimality from finite candidate coverage.

## 3. One owner retains purpose, bindings, dependencies and actual effects

Use value records and explicit methods in the existing brain assembly. No general plugin registry, service container, asynchronous planner thread or second copy of the world is needed.

| Record/interface | Required fields and semantics | Owner |
|---|---|---|
| `ObservedBrainFacts` | Tick/phase, world epoch, entity generations, player motion evidence, capability/policy/encounter revisions, sense verdicts and receipt watermark. Immutable view for one bounded evaluation slice. | Observation |
| `PlayerMotionEvidence` | Live pose/velocity/held direction; bounded displacement/path-length summary and coherence; observation tick; motion episode; reversal/discontinuity flags. No declared future destination. | Observation |
| `Opportunity` | Stable purpose/target key and generation; producer; observed useful need; `KnownUsable`, `KnownUnusable` or `Unresolved`; supported methods; source/revision/coverage. | Domain producer |
| `StepBinding` | Monotonic ID, opportunity ID, concrete target/pose/tool/weapon/use; preconditions and dependency keys; resource phases; next mechanical cancellation boundary; expected effects and proof freshness. | Course + domain binder |
| `PredictedEffect` | ID, parent binding(s), conditional successor delta, earliest/latest/nominal time, uncertainty provenance and dependency effects. No live mutations. | Domain prediction |
| `ObservedEffectReceipt` | Monotonic ID, source tick and native phase, object generation, kind/amount/state delta, actor attribution, native source identity, affected revisions. `Unknown` attribution is valid. | Native effect adapter |
| `RetainedCourse` | ID, revision, comparison episode, current executable step, optional suffix DAG, effect ledger, resource timeline, dependency index, pending frontier and release reason. DAG means dependencies can branch, not that V1 constructs a full policy tree. | Selection/Courses |
| `CourseComparison` | Same snapshot/episode IDs; evaluated alternative keys; value components, estimate statuses, binding coverage, switch consequences and chosen reason. | Selection/Courses |
| `ExecutionReceipt` | Binding/attempt IDs, requested controls, granted controls, actual use, native receipt links, progress/cancellation outcome. | Grants/current activity owner |
| `DecisionWorkBudget` | Shared wall-clock deadline, deterministic operation allowance, consumed/cut counters by subsystem and resumable cursor. Checks occur before expensive atomic work. | Selection; borrowed by all planners |

Opportunity identity survives ordinary motion and score updates. An NPC/item slot needs its spawn generation. A vein/pocket/tree purpose has a stable observed seed and membership revision; split/merge records carry lineage and conserved worth. A binding changes when its physical application changes. Repair keeps course ID and increments revision; replacing its purpose set creates a successor course ID with explicit lineage. An opportunity can remain known after release, but there is no stack that forces its resumption.

`IOpportunitySource.Continue(facts, cursor, budget)` returns newly examined concrete candidates, dispositions and a coverage cursor. `Bind(opportunity, hypotheticalState, budget)` returns an executable binding, a resumable unresolved result or a reasoned rejection. `Predict(binding, hypotheticalState, budget)` returns effects and bounds without reading unlisted live state. `ValidateNextUse(binding, liveFacts)` is a bounded check immediately before activation. `Execute(binding, grants)` invokes existing native mechanisms. These are contracts implemented by the six existing domains, not one class for every object in the world.

> **Divergence note, 22 September 2026.** `ObservedBrainFacts` and `PlayerMotionEvidence` above are not what the live brain reads. `ObservedBrainFacts` was built, was never constructed by anything but a fixture, and was deleted on that date; the record the tracked reader actually reads over is `DecisionFactSnapshot`, assembled per tick by `AssembleCourseSnapshot`, which carries this row's tick, world epoch, observation ordinal and receipt watermark as four of the five identity fields `IsModelExtensionOf` compares, with the capability, policy and encounter revisions reached through `ObserveDecisionCapabilities` and the intent regions through `Senses.Intent.Regions`. `PlayerMotionEvidence` went with it and its properties are in the owning folder's planned work; the physics track the harm forecast uses is `CapturePlayerMotion`, which is a different thing and carries no motion episode. The table is left as the design it is.

Fact access during binding, prediction and comparison goes through a tracked reader over `ObservedBrainFacts` and the hypothetical overlay. Each result returns the exact keys, versions and digest it read, including cached model inputs and scheduler state. A direct `Main.*` read in these pure evaluators violates the boundary and fails a source-boundary check. Snapshots use these manifests rather than guessing which facts were relevant afterwards.

### One native receipt pipeline prevents duplicate strikes and invented ordering

Add `Observation/CollectNativeEffectReceipts.cs` as the receipt adapter; keep tModLoader hook classes where their actual integration belongs. `Diagnostics/ObserveNativeCombatEvents.OnHitByProjectile`, `Firing/TrackLandedHits`, and `Progression/CreditKillsAndFights.cs` already observe overlapping parts of a strike. The latter's `ObservePlayerStrikesForExperience.OnHitNPC` is the player-to-enemy post-strike hook; its `ModifyHitNPC` is pre-strike attribution only. `CompanionPlayer.OnHurt` concerns damage to the player. `EnemyIntegration` supplies targeting facts and does not become a second receipt writer.

Extend the existing before/after strike bracket into a shared strike token, keyed by the actual target object/generation and synchronous strike dispatch, with nested dispatches represented separately. Repeated pre-hook views enrich one token; the first post-strike observation finalises its native amount once; further post-hook views attach provenance to that same token. Companion projectile lineage and the companion swing bracket determine actor attribution before player ownership does. Progression, learning and diagnostics consume that receipt/token; they do not each award a fresh physical hit. Do not deduplicate by tick, target and damage, since two identical same-tick hits are two strikes. Verify actual native callback ordering with item, projectile, swing, mount/dash and nested-hit fixtures; an unsupported/unmatched dispatch gets explicit unknown attribution and cannot certify intrinsic learning or duplicate XP.

Hooks append immutable receipts to a bounded game-thread observation queue carrying `(world epoch, origin tick, observed phase, global observation ordinal, receipt ID)`. The ordinal advances on every hook and brain boundary; phase names describe actual callbacks/brain stages, never inferred engine order. Brain step 1 captures a drain watermark and applies all receipts through it. Later receipts are applied next tick. Before step 9, compare current affected-object versions and the latest observation ordinal with the accepted binding; a late change can veto the use without pretending it was in the earlier comparison snapshot. Same-tick native actions emit their receipts immediately for that late validation.

The gameplay receipt queue and diagnostic queue are separate resources. If observation capacity is exceeded, retain a fixed-size dirty-object/global-gap summary, reobserve affected state, mark affected effects/attribution unresolved and stop certifying dependent next uses until revalidated. The recorder also gets a gap occurrence. Logging being disabled does not disable gameplay receipts, and losing log events does not lose the live course's world state.

### Tick order establishes one authority over the next action

```text
1. Drain native receipts and observe the world; advance shared senses once.
2. Apply downing/recovery lifecycle; release grants, retain only history if taken over.
3. Validate current binding cheaply; match receipts; dirty dependent forecasts.
4. If admission failed, stop the invalid next use and request the smallest repair.
5. Establish a cheap legal continuation from the actual pose before deeper search.
6. Spend the shared remaining budget on discovery, required repair, comparison and refinement.
7. Publish one accepted current binding and one atomic course revision.
8. Grant body/hands; position -> route -> evade -> motor through the existing movement boundary.
9. Revalidate native-use preconditions against the latest available facts; perform the accepted use.
10. Record projection and occurrences; effects arriving later feed the next observation tick.
```

A late fact between steps 7 and 9 can veto that use. It cannot start an unaccepted alternative shot. A from-here weapon substitution is accepted as a revised combat binding before firing, with new predicted effects and dependent suffix invalidation. No tactical fallback may secretly fire and leave the course believing a different action happened.

The cheap continuation is current valid movement/use, or a proven safe prefix towards a useful region, or normal companionship. On cold start with an available direct native-valid attack/tool interaction, construct that one-step binding first. If even its admission is unanswered, move/hover lawfully and record the unresolved cause; “always doing something” never licenses an unproved edit or shot.

## 4. Prediction stays conditional, and corrections propagate through dependencies

The future is a sparse overlay over the observed snapshot: changed positions/velocities, life/debuffs, cargo/mana, terrain and permanent light. Every prediction names the facts and effects it read. A shot's effect may enable a later shot; a torch may make another placement redundant; a broken tile may reveal a new site. A forecast cannot consume a predicted successor without retaining that causal dependency.

Maintain a reverse index from entity generation, terrain chunk, light patch, cargo slot/capacity, capability, policy, encounter and motion episode to dependent bindings/effects. Each change first dirties relevant estimates, then validates their meaning. A revision difference alone is not proof that a binding became unusable. Spatially remote edits do not restart local combat/navigation; edits in a read footprint do. Ordinary threat movement dirties that threat's trajectory, not every light/loot purpose.

Repair operations are remove satisfied step, remove invalid step, rebind a target/pose/method, reforecast an effect, insert a useful new step, reorder a bounded suffix or replace a course. Invalidate descendants transitively, including resource and time dependencies, then stop at an unaffected dependency boundary. A change near the front can legitimately affect the entire remaining course; the cost remains resumable. A repaired tail is published atomically only after its current executable binding is valid. Partial work stays private and cannot half-update the executor.

### A shot that did not do what was predicted changes the next decision

1. The accepted use creates a registered in-flight effect with projectile lineage and target generation. Its mana/cooldown costs are actual at use; its damage and shove remain predicted.
2. The player's hit or the enemy's autonomous motion changes observed pose/velocity/life. Native player-to-enemy damage attribution must be added through the real damage hooks; `CompanionPlayer.OnHurt` is damage **to the player**, not evidence of this example. When no supported hook identifies the cause, record changed-state/unknown actor.
3. Predictions reading the old target path become stale. The existing projectile continues naturally. Its disappearance/hit/window expiry produces its own receipt; it cannot be recalled or made to hit by changing the plan.
4. Rebind the next attack from the observed pose. Retain the firing region if it still offers useful attacks; change it if the target teleported elsewhere. The old expected shove never gets applied to the real enemy a second time.
5. Compare the revised combat continuation against the rest of the useful course. If the enemy is gone, a real drop may become a collect opportunity; if no drop appeared, do not fly towards invented loot.

Learning has two channels. Tactical outcome reliability learns that shots in this context can fail under external interference. Intrinsic projectile/weapon dynamics learn only from attributable, uncontaminated trajectory/hit observations. A miss after an unexplained target discontinuity is not proof of bad projectile gravity; a missing target does not teach a weapon miss. Keep contaminated/censored/unknown observations and their reason visible. No claim of causal blame is derived merely from a before/after difference.

### Random motion narrows useful prediction, rather than stopping all action

V1 uses the existing enemy forecast and learned weapon model, extended with per-entity forecast residuals and discontinuity detection. A residual is observed pose/velocity minus the prior forecast at the same tick. Keep bounded running residual summaries by relevant movement context and prediction horizon. Known velocity/acceleration/collision constraints produce reachable envelopes; empirical residual ranges are labelled empirical, not guaranteed bounds. No observations means unknown uncertainty, not zero variance. A teleport-like change beyond the reachable envelope starts a new motion episode and discards that trajectory.

The concrete V1 error key is entity generation, movement-model revision and prediction age in native ticks. Keep a bounded ring of issued forecasts and matched observations; update min/max residual and running squared error for each supported age. Do not pool different enemy types or infer an unobserved AI phase. On model change or discontinuity, end the episode and retain old data for diagnostics only. Eviction produces unknown coverage at that age, never a zero-error default. Ordinary kinematic correction remains within the same episode when inside the model's reachable envelope; an observation outside it dirties all dependent trajectories immediately. If the model has no finite reachable envelope, record a model mismatch against its nominal path without claiming a statistically significant teleport.

`SufficientlyModelled` has a concrete meaning: current-use preconditions proven, finite route/use/cancellation times for the executable prefix, native resource effects defined, and every credited future effect supported by an explicit model and dependency manifest. Nominal damage uses the existing trajectory/hit estimator; unknown effects earn no **certified** benefit and remain candidates for refinement. Empirical residual min/max can describe observed error but cannot become a confidence interval without calibration. Certified comparison bounds come only from native constraints or a validated model domain. With no such bounds, optional mid-action replacement waits for a semantic boundary; cold start and completed actions still select a legal nominal continuation. No probability sample-count threshold is invented.

Urgency is not a separate score or a new timeout. An observed new threat dirties the harm forecast. A currently predicted contact along the active control prefix invokes existing immediate avoidance and invalidates an incompatible next use; a newly worthwhile attack otherwise competes through J with its current effect/time and harm consequences. A forecast miss by itself does not revoke all optional work. G06/G11 exercise empty error history, evicted ages, continuous correction, model change and a discontinuity during a cut search, so these cases cannot be implemented as arbitrary private thresholds.

For known bodies, simulate realised knockback through the same enemy-motion approximation the local forecast uses, including tile contact, into successor position and velocity. Apply learned impulse after the predicted hit time, then roll the successor forward. An unsupported enemy motion model exports an uncertain successor; precise shove-combination credit cannot be certified from it. Reactive exploitation of the observed shove still works. The model must not run arbitrary live NPC AI against a hypothetical Terraria world or mutate the real game to test a guess.

Use region membership to retain firing positions: a pose remains acceptable while a current legal attack and shared spatial/product constraints support it. Re-solving aim does not require moving. A useful current shot plus movement to a better region is a valid segment, with its effects included before scoring the move. Preserve incomparable successor states in tactical search; do not prune an enabling shove solely because its immediate damage is smaller.

Longer horizons widen uncertainty. V1 retains one nominal continuation with effect dependencies and a generic repair path, rather than enumerating every random branch. Optional modules can add several predictive models or sampled branches later. Basic uncertainty, current-shot execution, external interference, missed effects and discontinuity repair are mandatory core.

## 5. The player's actual movement matters more than a momentary heading

Preserve `InferPlayerActivity`'s net-displacement/path-length coherence and `PlayerIntentRegionSense`'s immediate held-direction reversal at the clamp. Add observation/episode identity rather than replacing these with a new private predictor. A cave descent is continuous observed travel unless it violates the movement model; it must not wait for a “teleport” classification to affect relevance.

The shared intent sense publishes two related regions from the same observed geometry: **admission**, the existing forward-led region for proposing new work, and **continuation**, the union of that region with its same-sized zero-lead translation centred on the live player. This extra region is for retaining locally admitted purpose under ambiguous heading; it is not extra physical reach or a larger maximum leash. Both move with the actual player every tick. All domains consume these same regions. Their shape comes from the existing player-scale region, with no new hold radius or timeout.

Forward projection uses coherent observed travel, not the sign of the latest key alone. Incoherent weaving collapses the projected journey toward the local region; it never becomes simultaneous confident travel left and right. Heading edits dirty route/rejoin estimates. They do not retire opportunity IDs or erase a valid current local action. Candidate discovery still rotates across both sides, so confidence loss does not blind the drone to nearby work.

Actual departure moves the continuation region. At each control boundary, compare remaining effect-plus-rejoin against companionship from the current body state. Leaving the continuation region alone is not cancellation. It updates the shared companionship cost and the uncertainty of reunion. A proven obsolete purpose, forbidden next use, failed required return contract or a companionship continuation that wins the sufficiently evaluated future comparison releases it. The drone may continue lighting a useful nearby cave while the player walks past its mouth, as README expects; a long surface fight that has ceased to be useful loses to joining the player's new cave journey. G07 must contain both controls. When a previously required return proof is invalid and the new answer is `Unresolved`, initiate no additional discretionary use depending on that proof; retain a proven safe motion prefix and repair. This is a proof-lifecycle condition, not a raw distance veto. Ordinary reach uncertainty must not erase unrelated locally proven work or turn the whole brain off. A legal current native use already in progress can finish only if its cancellation contract and immediate safety permit it. An in-flight projectile remains in flight regardless.

For a brief finish still locally admitted, the same future-value comparison includes its effect, execution time, player motion and reunion. One nearly dead slime can be worth finishing. A long surface fight after the player has descended cannot preserve itself by claiming its whole enemy-kill bundle is one indivisible action: each weapon use is a boundary and the travel prefix is interruptible.

If the player becomes **proven unreachable**, ordinary recovery/local-help behaviour remains available under the existing lifecycle contract; this is different from unresolved return during an active departure. Nearby proven work may still compete without inventing a reachable player route. Continuous recovery flight is initiated only by ordinary following and never teleports. On downing, world change or respawn, discard control grants and revalidate opportunities from new facts; old courses never resurrect as orders.

## 6. Discovery, scheduling and tactical search share a bounded computation owner

Retain the current execution spine and replace one-representative-per-family selection with incremental concrete opportunity sources. Keep family labels only for product controls, policy and reporting. They cannot hide all but one torch, drop or enemy before course comparison. Candidates have deterministic keys; producers expose scanned bounds, counts, cursor and incompleteness.

The coarse scheduler seeds from the retained continuation and cheapest proven one-step actions. It tries removal, insertion, binding substitution, adjacent exchange and a whole-region alternative generated from actual nearby opportunities. It does not enumerate every permutation. A continuation may be partial beyond its executable prefix; unknown suffixes are not assigned certain value. A fair round-robin cursor across sources and unexplored regions guarantees eventual examination of a finite stable set under positive budget. Retained/current/in-flight objects are pinned; other cached objects may be evicted with a resumable source cursor and explicit coverage loss.

One `DecisionWorkBudget` covers observation-side discretionary search, candidate setup, route/refinement requests, held-combat reevaluation, cheap opener construction, suffix repair and tactical search. Existing independently minted deadlines and unbounded fallback simulations are removed. Native observation/effect capture and motor execution remain compulsory work, separately timed and bounded by their finite input domain; they are not hidden inside a claimed planning ceiling. A planning allowance is not a hard real-time guarantee for the whole .NET frame.

The implementation uses the existing shared `BrainPlanningBudgetMs` setting as the initial global planning allowance, with its current value recorded rather than inventing a second competing combat limit. Every consumer borrows the remaining deadline. Deterministic test mode uses operation counts from the same budget API; production also checks elapsed time. Break expensive operations into resumable slices and test cuts before/after each suspension point. Deadline overrun is measured and reported (since 23 September 2026 the budget reads the real clock on every sixteenth check, so up to fifteen further checks pass after the deadline; the route, flood and travel searches read the clock on every expansion themselves); a purported four-millisecond search must not conceal forty-millisecond setup or fallback.

Schedule mandatory validation first, then cheap useful action, required repair, and fair optional refinement. A flood or combat search keeps its frontier after a slice; ordinary movement does not restart it. Material dependency changes can restart only the affected frontier. When a dependency changes continuously, the cheap executable path must still make progress without ever certifying stale future uses. Suffix depth, candidate cache and diagnostics buffers are computational resource limits in one budget/config owner, not behaviour probabilities. Record their truncation and compare quality under smaller/larger limits before calling them harmless.

## 7. Target folders preserve the physical and native boundaries

This is the target layout after the full implementation. Existing product surfaces remain named for their work; a mass rename of inventory, drawing or progression would add migration cost without improving decision ownership. New folders below hold actual V1 code, not extension placeholders. Every shown folder receives or updates its existing `CLAUDE.md` in the implementation.

```text
Companion/
├─ CharacterBody/                         existing NPC, mana, drawing and lifecycle
├─ EnemyIntegration/                      existing targeting bridge and hostile facts
├─ Brain/
│  ├─ CoordinateBrainTick.cs               the only tick coordinator
│  ├─ Activities/                         concrete opportunity sources and domain executors
│  │  ├─ CompanionAction.cs               domain contract; no private global score
│  │  ├─ ClassifyOffersAndAttempts.cs      typed admission and execution outcomes
│  │  ├─ RecordCandidateFunnel.cs          source coverage; shared diagnostic adapter
│  │  ├─ WorkPolicies.cs                   actual user permissions
│  │  ├─ Combat/
│  │  │  ├─ FightEnemies.cs                combat opportunity/binding adapter
│  │  │  └─ Planning/                     bounded precise attack search, existing owners
│  │  │     ├─ AttackPlan.cs               interruptible tactical segment contract
│  │  │     ├─ SearchAttackPlans.cs        consumes borrowed decision budget
│  │  │     ├─ ReevaluateAttackPlan.cs     resumable fresh consequence evaluation
│  │  │     └─ [other existing files]      aim/stand generation, outcomes and descriptions
│  │  ├─ Gathering/                       MineOre, ChopTree and native work conclusions
│  │  └─ NearbyAssistance/                light, collect, pot and company sources
│  ├─ SharedBehaviours/
│  │  ├─ Safety/                          all-job immediate threat avoidance
│  │  └─ Recovery/                        body takeover and continuous return
│  └─ Infrastructure/
│     ├─ Observation/
│     │  ├─ ObservePlayerMotionEvidence.cs observation episode and coherent travel summary
│     │  ├─ ObserveDecisionCapabilities.cs gear, body, cargo and permission revisions
│     │  ├─ CollectObservedEffects.cs     typed receipt stream and watermark
│     │  ├─ CollectNativeEffectReceipts.cs native hook adapter and once-per-strike identity
│     │  ├─ ObserveForecastErrors.cs      residuals, discontinuities and attribution status
│     │  └─ [existing senses]             light, reach, intent, threat and terrain facts
│     ├─ Selection/
│     │  ├─ ChooseBehaviour.cs            thin composition facade; no second chooser
│     │  ├─ OwnCurrentActivity.cs         sole live attempt/executor lifecycle
│     │  ├─ BehaviourWeights.cs           remaining existing product/geometric settings
│     │  ├─ Opportunities/
│     │  │  ├─ DescribeOpportunity.cs     stable keys, useful needs and coverage contracts
│     │  │  ├─ DiscoverOpportunities.cs   fair incremental source registry/cursors
│     │  │  └─ BindOpportunity.cs         shared precondition and live-binding gate
│     │  ├─ Courses/
│     │  │  ├─ DescribeCourse.cs          course, binding, effects and revision records
│     │  │  ├─ RetainCourse.cs            sole current-course owner and atomic publication
│     │  │  ├─ RepairCourse.cs            dependency dirtiness and suffix repair
│     │  │  ├─ SearchCourseOrders.cs      bounded insertion, exchange and region alternatives
│     │  │  ├─ CompareCourseOutcomes.cs   shared V1 value and tie policy
│     │  │  ├─ ProjectCourseEffects.cs    sparse hypothetical overlay; no live mutation
│     │  │  └─ TrackCourseDependencies.cs reverse dependency footprints
│     │  └─ Computation/
│     │     ├─ AllocateDecisionWork.cs    single borrowed budget and fair scheduler
│     │     ├─ ResumeDecisionWork.cs      cursors, epochs and cancellation ownership
│     │     └─ LimitDecisionStorage.cs    bounded caches; pin/evict/coverage semantics
│     ├─ Grants/                         accepted binding -> body/hand grants -> receipt
│     ├─ Position/                       useful regions, shared clearance and live fit
│     ├─ Movement/                       preserved one movement and one body boundary
│     │  ├─ Contact/                     the actual circle contact
│     │  ├─ FreeSpace/                   clearance, corners and resumable routes
│     │  ├─ Steering/                    motor requests, routes and hover
│     │  ├─ TerrainModel/                spatial edit versions
│     │  └─ TerrariaIntegration/         actual tiles and NPC writes
│     ├─ Interactions/                   actual side effects and native validation
│     │  ├─ Firing/                      accepted tactical use, lineage and hit receipts
│     │  ├─ Mining/                      native persistent tile work
│     │  ├─ Chopping/                    native trunk work
│     │  ├─ Torch/                       placement and permanent-light effect receipts
│     │  ├─ Doors/                       existing door execution; route gap stays explicit
│     │  └─ WorldProtection/             shared edit permission before use
│     ├─ Aiming/                         trajectory solutions and realised successor geometry
│     ├─ WeaponKnowledge/                intrinsic learning separated from interference outcomes
│     └─ Diagnostics/
│        ├─ RecordCourseTrace.cs         typed events through existing God's-eye writer
│        ├─ CaptureDecisionSnapshot.cs   bounded replay inputs and omitted coverage
│        ├─ QueueDiagnosticRecords.cs    bounded immutable event queue and loss accounting
│        ├─ FlushDiagnosticRecords.cs   sole recording worker, stream ownership and teardown
│        └─ [existing diagnostics]      TSV, God's-eye, overlay, timing and scenario capture
├─ Inventory/                            four slots/cargo; capability/effect adapters
├─ PlayerIntegration/                    observation hooks and controls
├─ Progression/                          real effect credits; no predicted XP
├─ ProfileCard/                          native policy controls remain
├─ DiagnosticsConfiguration/             recording controls and cap disclosure
├─ MapIntegration/                       unchanged product surface
└─ HeadsUpDisplay/                       unchanged product surface
```

> **Divergence note, 22 September 2026.** Three files in that `Observation/` list no longer exist: `ObservePlayerMotionEvidence.cs`, `CollectObservedEffects.cs` and `ObserveForecastErrors.cs` were deleted because each had a fixture caller and **no production caller at all** — the tree reached this part of the layout as classes nobody ran, which is the failure section 9's own title names. `CollectObservedEffects` and its `ObservedBrainFacts` were a second packaging of what `DecisionFactSnapshot` already carries; the other two are capability the plan still wants and whose *properties* are recorded in `Companion/Brain/Infrastructure/Observation/CLAUDE.md`'s planned work, to be rebuilt from their consumer rather than from the store. The plan is left as written, because it is the design rather than a description of the tree, and because the step that would need them has not been reached. `Diagnostics/CaptureDecisionSnapshot.cs` is still here and is half-alive: its `CourseTraceContext` has five production callers and its `CourseDecisionSnapshot` has no producer anywhere, so the exact-replay input the file is named for is never captured.

“Other existing files” means preserve their implementation responsibility and names unless the migration table explicitly retires them; it is not a licence for a second global selector. Namespace changes follow actual moves, and every caller, fixture, reflection reference, diagnostic producer locator and folder guide moves in the same batch.

| Current owner | Target change and removal obligation |
|---|---|
| `ChooseBehaviour`, `NominateFamilyActivities`, `EvaluatePreparedActivities` | Replace family-winner selection with course composition; delete nomination and prepared-score ranking once consumers migrate. Preserve category labels only for controls/reporting. |
| `ScheduleOpportunityQueries` | Move incremental scheduling into `Opportunities/DiscoverOpportunities`; expose more than one concrete candidate. Remove the old scheduler entry point. |
| `ValidatePreparedActivity` | Move shared admission into `BindOpportunity`; domain checks remain at their real owners and native activation rechecks them. Remove compatibility wrapper after all callers migrate. |
| `OrderNearbyTasks` | Replace discard-after-first permutation scoring with `SearchCourseOrders`; delete factorial enumeration and close-score/tie nudges. |
| `AssessReunionCost`, `EvaluateConsiderations` | Extract the still-used region/relevance curves into shared comparison/observation ownership. Delete retired time/commitment/family factors; keep a named curve helper only if real callers remain. |
| `OwnCurrentActivity` | Keep one live activity/attempt lifecycle. Accept `StepBinding`; add course/effect links. Remove any selector-created resume obligation. |
| Activity `Prepare` methods | Enumerate specific sites incrementally, bind against hypothetical/current facts, execute through the existing action. Do not reproduce six private course schedulers. |
| Combat `PlanningBudget`, hold/release logic, from-here fallback | Remove private deadline/unbounded mode in production; borrow `DecisionWorkBudget`. Course owns global retention; combat owns tactical validity and current use. Replace time-stall/urgency slack retention with common boundary/repair contracts where equivalent; no hidden second commitment rule survives. |
| Forecast/hit/shot outcome learning | Propagate predicted and realised position/velocity, keep actual projectile lineage; split intrinsic versus interference-conditioned learning. |
| Grants and incidental interactions | Require accepted binding/phase compatibility. Every incidental pot break, torch placement or tool use needs an accepted one-step binding before the native call, including resource/capacity and predicted effects. `ConsiderIncidentalInteractions` becomes a proposal source consumed before grants, never a post-grant second chooser. Passive unavoidable contact pickup remains an observed receipt and repairs reservations; it is not a new autonomous action. No unpriced contradictory shot. |
| Observation, position, movement | Publish one shared intent/clearance/capability answer and consume it at discovery, binding, destination and execution. Preserve route/contact algorithms except changes needed to satisfy that contract. |
| Diagnostics and tools | Extend the existing format and readers below; remove obsolete live family-score fields or version them as historical, never silently reinterpret them as course values. |

## 8. God's View records the decision chain and its coverage

Extend the existing TSV plus God's-eye JSONL sidecar; no second planner log. Diagnostics consume typed decision events from the owners and never participate in choosing. Current telemetry is schema 0.40.0 at source baseline `d315bf2`; choose the next schema once when implementation lands and update writers/readers/fixtures together. Older captures remain readable with explicit missing-course coverage.

### Identities and events distinguish intention from execution

Every course occurrence carries capture/world epoch, sequence, source tick and native phase, course ID/revision, step/binding/attempt IDs where relevant, producer symbol, source revision and policy/config fingerprint. IDs are monotonic session-local values, never mutable pose hashes. Reset records define world unload/load, companion despawn/spawn and recording start/stop. Tick alone cannot order a native hit against a brain decision in the same frame.

The TSV keeps compact current fields: `course_id`, `course_revision`, `course_episode`, `course_step_id`, `binding_id`, `course_phase`, `course_prefix_status`, `course_suffix_status`, `course_fact_revision`, `course_last_change`, `course_claims`, `course_freshness`, `decision_work_used`, `decision_work_cut`. Existing attempt/grant/native-action and `brain_fresh` fields remain distinct. Retained state is not a newly computed decision.

Use typed JSON payloads on the existing event envelope: add `payload_kind`, `payload_version` and a JSON-object `payload`, plus `phase`, `observation_ordinal` and `receipt_watermark`. Preserve `detail` for historical event kinds; new course events use the typed payload, never a second hand-built string protocol. One serialization helper owns the envelope. Readers validate each kind/version; unknown or malformed payloads yield missing/partial coverage, never an empty candidate list. Preserve old-schema score readers explicitly when retiring live family-score output. Round-trip and unknown-version mutations belong in `VerifyGodsEyeEvents` and SessionReport's own tests.

| Occurrence | Payload required to answer the diagnostic question |
|---|---|
| Opportunity examined / coverage closed | Concrete generation/key, domain/cursor/scan bounds, admission facts and verdict, known omissions with reason, source exhausted versus budget cut. |
| Binding evaluated / comparison decided | Both alternative IDs; same snapshot/episode; natural effects, value terms, bounds/status, remaining/switch cost, omitted outcomes, tie/retain/replace reason. |
| Course created / repaired / released | Trigger event; predecessor ID/revision; retained/removed/added bindings; first dirty dependency; pending repair frontier; completed versus abandoned purpose. |
| Step started / granted / used / ended | Resource intervals and cancellation boundary; requested versus granted controls; native use identity; completion/partial/external satisfaction/failure reason. |
| Effect expected / receipt observed / prediction reconciled | Parent bindings, target generation, expected state/time, real state/time, native actor attribution, confirmed/partial/contradicted/expired/censored/unobservable status. |
| Fact changed / dependency invalidated | Old/new version and value summaries, exact read footprint and spatial relation, directly affected and transitive bindings; ordinary estimate dirty versus admission failure. |
| Motion evidence changed | Player/target episode, live pose, observed input, coherent travel summary, discontinuity evidence, admission/continuation regions, projected return and reach status. |
| Work slice yielded / budget closed | Deadline and operation allowance; costs by setup/validation/discovery/repair/combat/route; first executable-prefix tick; frontier retained/restarted and why. |
| Decision snapshot / recording gap | Input IDs and digest, bounds and model inputs, ring prehistory range, missing/evicted fields, dropped/coalesced counts, writer error or unclean close. |

The why-not answer has three honest forms: examined and rejected for a recorded reason; discovered but unresolved/not expanded; outside recorded discovery coverage. It never says “the drone decided against that torch” when that torch was not examined. Log the best examined challenger and all selected/invalidated bindings; exhaustive candidate payloads are a bounded diagnostic mode with omitted counts, not an unlimited default.

### A bounded snapshot makes a decision reproducible only when its dependencies are present

Keep a bounded in-memory prehistory ring of observation deltas, bindings and receipts. Freeze it on explicit mark and invariant failure, and capture compact decision snapshots on creation, material repair and periodic checkpoint. Store current source/config/policy/knowledge revisions, deterministic scheduler state/cursors, random state if used, entity generations/poses, relevant native item numbers, sense verdicts/ages, player motion summary, sparse hypothetical overlays and dependency footprints. Terrain reuses existing chunk snapshots with versions/digests; missing chunks mark reconstruction partial.

All bounds live in diagnostics configuration with units: event count, bytes per snapshot, ring bytes, queue bytes and file bytes. Implement finite defaults by measuring the representative recorded scenes before accepting the implementation; document the chosen resource envelope and sensitivity. `QueueDiagnosticRecords` owns three bounded partitions: required decisions/native receipts, optional candidate/snapshot detail, and fixed gap summaries. Producers enqueue bounded immutable values with a nonblocking try operation; they never serialize or write to disk. Oversize payloads become bounded summaries plus omitted counts. Failure to enqueue required evidence sets a sticky incomplete flag. Logging remains optional; observation and gameplay receipts remain independent.

`FlushDiagnosticRecords` owns one diagnostics worker per recording, both file streams and their serialization. It reads immutable records only, never Terraria or the planner. TSV and sidecar records carry producer ordinals so worker scheduling cannot invent event order. On stop/unload, stop new enqueue, signal drain/cancellation, wait only the configured teardown allowance, and dispose streams on the worker. If a write cannot finish, retain incomplete status and report the undrained count; never block the game thread indefinitely or publish a clean footer. Do not launch a replacement writer until the previous one has exited; report its task state and eventually observe completion. Normal close joins the task and confirms disposal. Test slow/full storage, cancellation, repeated reload and an I/O exception; no orphan worker or silent complete marker is acceptable. A pathological OS write may outlive cancellation, so this is bounded game-thread waiting, not a promise of forcibly terminating managed I/O.

Count per-kind offered, enqueued, written, coalesced and dropped records, queue high-water bytes and source-copy/enqueue/serialization/flush times. Current `record_ms` alone cannot prove the new boundary's cost. File headers start incomplete; only a drained, loss-free close supplies a complete footer. Readers distinguish `exact-input-complete`, `explanatory-partial` and `historical-unavailable`. Exact replay requires every tracked read plus matching source/model/policy/scheduler inputs; otherwise the driver refuses exact mode and lists the missing keys. Partial timelines remain useful but do not certify omitted decisions.

Causal attribution has explicit strength: native identity match, compatible observed state change, inferred association, unknown. The reader never upgrades inference to proof. One native receipt may satisfy several predicted needs, but its physical amount is allocated once: record the deterministic target/need allocation map, unallocated remainder and policy revision, assigning compatible effect claims in stable effect-ID order up to each claim's remaining physical amount. Distinct consequences such as damage and reduced future harm retain separate units. Lighting completion requires coverage evidence, not merely a placed-torch occurrence. Mining partial damage may expire/reset and then ceases to be remaining progress. XP and cargo use native receipts only.

`Tools/SessionReport` adds `Read/ReadCourseChronicle.cs`, `Checks/CheckCourseContracts.cs`, `Measures/MeasureCourseWork.cs`, and `Write/WriteCourseTimeline.cs`. The chronicle joins current TSV projection, event lineage, attempts, grants and receipts. The timeline filters by course/binding/effect, shows before/after plans, player-intent regions, predicted/realised outcomes, value comparison and why-not coverage. Existing inspector rendering gains course/current-step/affected-suffix/coverage views through its existing diagnostic surface; it does not introduce a new gameplay UI or modify the player's screen uninvited.

`Tools/EngineReplay/DecisionMaking/` adds native-integrated course cases and a pure deterministic decision snapshot driver using the production course classes. The driver accepts only fully captured dependencies for exact decision replay; otherwise it returns partial coverage. Replaying a saved world and player track through `Tools/WorldRun` remains a counterfactual run, **not** reconstruction of the original NPC/projectile/item chronology. Name the mode on every result. `Tools/Ledger` receives verdict rows and ungraded measures, never the raw trace.

## 9. Headless gates test behaviour, not just the presence of new classes

Gate names below become fixture/ledger identities in the implementation. The first full pass starts from source baseline and named capture evidence, with current tree status and unrelated work recorded. The [reproducible probes](<../Evaluation and Observability/Probes/CLAUDE.md>) retain the existing capture counts and mathematical counterexamples, including eight arithmetic checks of this objective; they are evidence seeds, not substitutes for running the production brain.

| Gate | Production-path fixture and pass condition | Mutation that must fail |
|---|---|---|
| G01a — historical swap evidence | Reproduce the 0.30.6 ticks 7773–11125 counts/signature with the existing reader/probe. All unavailable course/binding/footprint/watermark fields are `historical-unavailable`. | Interpret missing course fields as no course changes; rewrite the captured swap count. |
| G01b — capture-shaped production case | Build a native fixture from the recoverable geometry/identities, declaring synthetic additions. Run the new brain and writer→chronicle. With finite stable feasible work, finish both effects; no same-snapshot A→B→A with zero changed admission/effects. This is a capture-shaped fixture, not exact replay of the old live run. | Recreate identity on score change; discard selected suffix; credit attempted torch as light. |
| G02 — concrete order | Seven torch sites, multiple drops, two regions and a profitable detour. Complete all admitted effects in the stable case; compare route/time against identical-candidate reactive reference and exact small-instance enumeration. | Hide all but nearest site; permanent first-five cutoff; multiply overlapping coverage. |
| G03 — relative choice | Equal job sets in opposite orders; same now-state with different past travel; split/merge loot and vein identities; empty worthwhile set; a lone finite job; added candidates already in the census; discounted timing trade-offs. Same future yields same choice independent of past spend or artificial partition; finite completion beats omission at equal costs; extra candidate discovery cannot change T or the harm charge. | Add sunk travel or incumbent percentage; count split stacks as new full reward. |
| G04 — useful attack now | Useful native-valid opener exists while a better stand search is cut. Accepted shot fires on its earliest mechanically available use, while valid movement continues. A blocked/no-weapon variant must report why it cannot fire. | Remove opener; allow fallback to bypass course acceptance; wait for full suffix before use. |
| G05 — external hit | Player-to-enemy native hit during our projectile flight changes target geometry; next use reads realised state; projectile resolves separately; causal learning is censored correctly when attribution is insufficient. | Apply predicted shove after an external displacement; label `OnHurt` as player attack; train intrinsic gravity on contaminated miss. |
| G06 — unpredictable motion | Seeded jump/dash/teleport sequences plus adversarial event order. No stale aim on a new motion episode; no whole-course identity churn from ordinary movement; legal useful attacks still occur. | Forecast with zero unknown variance; retain teleport trajectory; re-create course for every pose update. |
| G07 — departure and weaving | Replay bounded left/right motion inside the continuation region, then a real cave descent. Local work retains through reversals; no obsolete optional use starts after departure invalidates it; legal following continues while reach is unresolved. Paired control: the player passes a cave entrance and the still-worthwhile local lighting continues. Subdivide the same route into different waypoint counts: choices must remain identical. | Treat any held-key reversal as abandonment; require a long heading timer before actual descent counts; keep attacking until enemy death. |
| G08 — repairs across domains | Player takes drop/mines tile/lights pocket/fells tree; slot reuse; new hostile; changed pick power/cargo/permission; remote and local terrain edits. Only affected dependencies change, native credit is correct, and obsolete bindings never execute. | Global edit invalidates all plans; ignore local edit; reuse dead object slot; count external effect as companion XP. |
| G09 — interleaving | Travel plus legal incidental work; projectile in flight plus movement; mining approach and native use quiet; accepted combat interruption and still-relevant vein resumption; expired vein dropped. A pot-breaking world-work binding can use its weapon mechanism; an unrelated hostile is never substituted. Reserve the last cargo slot for a drop, then propose a pot whose contents could occupy it: account before use and repair only genuinely unexpected contact pickup. | Attack a hostile outside accepted combat; perform pot/torch/tool use with no accepted binding; restore the post-grant incidental chooser; reserve hand for a hypothetical future use; force resume stack. |
| G10 — global spatial contract | Light/loot/mine/chop/combat/company all inspect the same clearance/intent/capability facts at discovery, binding, destination and use; surface/cave hover plus two-by-two horizontal/vertical/diagonal passage controls. | Remove a destination consumer; turn soft clearance into collision exclusion; apply clearance cost twice. |
| G11 — budget and lifecycle | Cut before first expansion, during validation/refinement/repair, after valid prefix; sustained fact changes; downing/recovery/world reset. No unbounded secondary solve, no stale grant, eventual finite-source discovery. | Mint separate combat budget; restart unchanged frontier; publish half-repaired course; preserve grant across world epoch. |
| G12 — policy contrasts | README danger versus trivial work, nearly finished versus long job, ordinary loot versus no loot, local versus remote darkness, harmless dummy versus real enemy, safe damage tie, empty-work companionship. Document all value terms and expected alternative before running. | Restore category priority, hide legacy time/commitment factor, use raw effect counts, make every uncertain harm veto all work. |
| G13 — encounter conduct | Boss/event suppresses optional work; survival-first among feasible meaningful fight continuations; player death preserves encounter; unarmed/unsupported target gives correct safe conduct rather than fake attack. | Player-dead global refusal; idle artificially wins by zero damage; ordinary utility purchases forbidden optional work. |
| G14 — observability | Writer → parser → chronicle → checks → rendered report for all transitions, censored effects, malformed/truncated/missing sequences, event overflow and old schemas. Correctly grades missing evidence as missing. | Delete binding/receipt link; reuse ID; stale TSV masquerades as fresh; truncate sidecar while reporting complete. |
| G15 — model fidelity | Compare native effects with predicted projectile/knockback/cargo/mana/light/work successors, including interference. Contradicted models trigger repair and cannot certify enabling chains. | Omit successor position/velocity; double debit mana; duplicate in-flight damage; predict permanent light from carried illumination. |

Add generated **sequences** of the above events with reproducible seeds, not just isolated states. Invariants include one current course owner, at most one incompatible grant per resource, no completed effect without sufficient receipt, no invalid next use, no stale generation, no unknown-as-absent and no hidden live-state mutation in hypothetical evaluation. Include scheduler-order variations and deliberately tiny budgets. Each test declares whether time allowances are deterministic or real; a lifted allowance suite cannot prove the live deadline.

### Comparison and performance acceptance are explicit

Use a headless reactive reference with the **same** concrete candidates, models, policy, executor, observation and budget. Its difference is that it retains no mixed-work suffix. Keep this reference in tools/fixtures only; production has one brain. Small finite fully known cases also have an exact enumerated oracle in tools, never the runtime. Compare completed unique effects, weighted delivery times, route length, reunion delay, received harm, valid-use latency, idle-with-executable-work, repairs without productive effect, discovery coverage, memory and work cost. Lower delay/harm/travel is better; more unique useful effects at equal time/resources is better.

For every named deterministic regression, the candidate must meet the stated behavioural invariant. For matched mixed-work scenes, it must have no loss in required effects/reunion/safety and show a strict completion-time or route improvement in at least the chaining and consequential-order scenes; otherwise the architecture's extra machinery has not earned its claim. Do not pool dissimilar live sessions into a causal before/after. Record all losing scenes rather than average them out.

For performance, use paired warmed runs on the same fixtures and allowance mode; separately report cold-start first-use latency. The planning slice must honour its borrowed deadline plus the maximum measured atomic slice, with every overrun visible. Whole-brain p50/p95/p99/max, allocations, peak retained bytes and diagnostic-on/off cost must be reported. The implementation does not pass by moving work out of the measured planner timer. If comparison loses under the unchanged global allowance, simplify/refine work allocation inside this run; do not silently enlarge the allowance or call a slower unbounded suite the success. A heavy timing batch runs only at an owner-authorised machine-use window; correctness fixtures and other independent implementation continue beforehand.

## 10. Implement the complete core in dependency order, then request one live acceptance

These are internal construction gates within one delivery. None ends by asking the owner to launch the game. Failures are repaired before dependent integration; completed prerequisites do not get reported as the completed brain.

1. **Freeze evidence and executable contracts.** Record HEAD/status, enumerate chooser/candidate/control/effect callers and existing tests, create G01–G15 fixture specifications with expected alternatives and source/capture provenance. Pin baseline policy and performance inputs. Establish the future-code ledger without marking anything green for being written.
2. **Build facts, receipts and instrumentation first.** Add generation/episode/capability revisions, typed native receipts, forecast error attribution and the event/schema reader contracts. Pass G08/G14 identity and corruption cases. No predicted effect earns native work credit.
3. **Expose concrete opportunities and bindings.** Migrate all six domains with fair cursors, capability/permission/region contracts and native-use rechecks. Pass source-coverage, same-input and domain admission tests. Preserve current executable behaviour while internal callers migrate; no compatibility path survives final integration.
4. **Build the course model, objective and repair engine.** Implement the fixed V1 policy, sparse effects/resources, boundary retention, dependency index and atomic publication. Pass G02/G03/G07/G09/G12, including split/merge conservation and unresolved repair. A policy contradiction is a design defect to report with its counterexample; do not silently redefine the objective to make fixtures green.
5. **Integrate precise combat and one computation budget.** Useful opener, accepted substitution, realised knockback successors, interference learning, fair/resumable search and dependency-aware target refresh. Pass G04/G05/G06/G11/G15 under forced cuts. No unbounded held-plan reevaluation or clock-free escape survives.
6. **Switch the complete live brain and remove obsolete ownership.** Wire tick order, all grants, spatial consumers, encounter/death semantics, UI diagnostic projection and native effect adapters. Delete the migration-table retired paths/knobs. Pass G01/G08/G09/G10/G13 and navigation boundaries. This is the only production decision path, enabled by default.
7. **Verify the whole delivered tree.** Run targeted native checks then `sh Tools/verify.sh`; inspect the ledger's closing row counts, missing/skipped/sealed cases and every red. Run exact small-instance, sequence/mutation, snapshot replay and matched-reactive comparisons. Run the authorised performance batch and record its limits. Fix findings, repeat only affected checks plus required integration, then independent review of the final tree and each cross-boundary contract.
8. **Deliver the completed implementation for one final play.** Update README System In Place from source, Current Behaviour only from named recordings, all affected folder guides, build version and schema. Package only with the game closed. Provide the final acceptance route and telemetry mark procedure. The live run checks perceived responsiveness, random encounters, cave-following, weaving, multi-job completion and all-job hovering; its outcome can reveal new defects, but it is not a prerequisite for building the next missing third.

The implementation command will be `sh Tools/verify.sh` from the repository root for the full gate, with `sh Tools/verify.sh --case '<fixture fragment>'` for affected cases and `sh Tools/check-navigation-boundary.sh` when movement boundaries change. The proposed fixture names and new files above do not exist yet. A game-free replay or green suite does not establish live feel, arbitrary mod compatibility or that every possible issue has been observed.

### Completion means a whole brain, with other product work still named honestly

V1 owns mixed-work ordering, concrete sites, conditional consequences, repair, random/external-event handling, player weaving/departure, global policy/fact consumption, computation and diagnostics. It preserves the existing one-body movement and native mechanisms. Mastery effects, a new gear onboarding UI, unsupported modded weapon mechanisms, additional allowed world edits, and the known closed-door route representation are separate product work; none is quietly claimed solved by planning. All 32 README rows retain their explicit disposition in the rationale, with the core rows above gaining implementation gates.

If a model cannot support an intended advanced combination, the core still executes and repairs useful immediate actions; the model limitation is visible and that combination's acceptance remains unmet. Do not report the full consequential-combat gate complete from reactive exploitation alone. Conversely, no module is needed just to recover from an ordinary miss, interference or direction change.

## 11. Optional modules have evidence triggers, not placeholder code

[Modules](Modules/CLAUDE.md) contains conditional extensions: multiple predictive models, wider course search, reusable demonstrated methods and sampled uncertain futures. They attach to the existing prediction/search contracts; they do not add another owner of the next action. Their documents give the symptom that reopens them, cost, comparator and removal condition. No empty runtime module folder or feature toggle is created for them in V1.

## 12. Evidence and design limitations remain visible after consolidation

The [implementation-plan attack](<../Decision Architecture/Attack Six Complete Brain Architectures/08 Attack the Complete Implementation Plan.md>) and [evidence-contract review](<../Decision Architecture/Attack Six Complete Brain Architectures/09 Audit the Complete Plan Evidence Contracts.md>) retain initial failures and final bounded rechecks. Both reviews reused existing worker contexts; they are independent evaluations of the artefact, not fresh-context isolation. The final verdict accepts this implementation plan's contracts and gates, not an unbuilt brain. The arithmetic probe passes eight stated examples; it does not establish native performance or the general suitability of the policy.

The source refresh at `d315bf2` confirms existing bounded player-intent history, concrete combat bindings, per-use live aiming, typed specialised shot/mining outcomes and one shared movement boundary. It also confirms that the nominal combat search budget excludes some preparation/reevaluation/fallback work, that current successor modelling lacks knockback-driven trajectory propagation, and that current family selection cannot retain a mixed sequence. The rationale supplies exact source locators and historical commits. The two source maps from this design pass are preserved under [Implementation Evidence](<../Implementation Evidence/CLAUDE.md>).

The most consequential design risks are the equal-normalised-need policy, uncertainty broad enough to discourage profitable interruption, horizon/set changes that alter comparisons, large invalidated suffixes, moving-world search starvation and incomplete model attribution. G03/G06/G07/G11/G12/G15 are written to expose them. This plan is a concrete implementation hypothesis with acceptance gates, not a guarantee that no later module or policy correction will ever be needed.
