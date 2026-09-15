# 7 Observability — seeing every combat decision, and proving it was the best one available

The owner will test this system by handing the companion weapons, unlocking upgrades and watching. The instruments must make every issue visible without a guess, and must go one step further: take the state at any moment of a fight, recompute what the best decision was, and say whether the companion made it. Over time that is how the system is shown to be as good as its knowledge allows.

## Two different questions, audited separately

A combat decision can be wrong in two ways that look identical in play, and the audit keeps them apart because they are fixed in different layers:

```
the companion stood in the wrong place with the wrong weapon
├─ the SEARCH was wrong        given what it knew, a better plan existed and was not found or not chosen
│                              → proposal generators, beam width, budget, weights       (search audit)
└─ the KNOWLEDGE was wrong     the plan was the best for what it predicted, and the prediction was wrong
                               → flight laws, volleys, responses, the residual          (knowledge audit)
```

A decision on the Pareto front of a wrong prediction is still a bad decision, so a report that graded only the search could call a broken weapon model optimal. Both audits run on every recorded fight.

## God's-eye records

Written through `RecordGodsEyeEvents`, each with the stable NPC and projectile identities the existing records use.

| Record | When | Carries |
|---|---|---|
| `volley-observed` | a use is grouped | item full name, shooter, projectile count and types, per-slot angle, speed ratio, damage share, origin kind, delay, modifier state |
| `flight-law` | a type's law changes revision | projectile full name, terms kept and parameters, residual, evidence, predictable flag, wall response, hit response |
| `shot` (extended) | a companion use fires | today's fields plus plan id, segment, use index, simulation key, predicted hits with ticks and damage |
| `shot-event` | per companion projectile, bounded per shot | wall contact (in and out velocity), body hit (NPC, tick, damage), child spawn, area hit, death and cause |
| `combat-plan` | a plan is committed, advanced or invalidated | plan id, segments (stand, reason, weapons, targets, ticks), objective vector, weighted value, front size, the three best rejected plans with dominated-or-weights and the objective they lost on, budget cut, invalidation reason |
| `combat-snapshot` | every plan commit, every rescore while a plan is committed at a bounded rate, and on the inspector's mark key | everything the audit needs to re-run the decision (below) |

## The combat snapshot

A snapshot is the decision's complete input, so an offline run is the same computation the live companion made:

- the tick, the companion's centre, velocity, life, mana and modifier state;
- the gear by full names, and the knowledge in force for those weapons — volley shapes, flight laws, wall, hit and child responses, weapon-effects rows for the enemy types present, the residual posteriors' means — at its revision;
- the enemy forecast as built: every hostile's full type name, generation, life, defence, knockback resistance, box, sampled predicted boxes and confidence, urgency to each body, damageability;
- hostile projectiles as the threat sense saw them;
- the player's centre, velocity, life, intent region with lead and velocity, `PlayerDanger`, `CompanionDanger`;
- the terrain as the existing chunk snapshots (`RecordTerrainChunks`) over the area the plan's proposals span;
- the stand verdicts the positioner returned;
- the budget spent, whether it was cut, how many candidates each level evaluated, and the committed plan.

Snapshots are compressed and bounded per session; the mark key (the inspector's existing keybind with a modifier) always writes one, so the owner can say "that moment" during a test and find it in the report.

## `Tools/CombatAudit`

A headless tool on the EngineReplay host (the simulator needs Terraria's collision), which reads a capture's snapshots and follow-up records.

**Search audit.** For each snapshot, re-run the planner on the snapshot's inputs with the budget lifted and the search widened: every reachable half-tile in the proposal region as a stand, the full aim sweep, one more level of depth, and the beam unbounded. Report per decision:

- whether the committed plan is on the exhaustive front, and the weighted regret (best weighted value minus the committed plan's);
- if not, which objective the better plan wins on, and **which generator would have proposed its first stand** — or that none would, which names a missing kind of stand;
- whether the live budget was cut, which separates "the search is too slow" from "the search is shaped wrong";
- whether the live and replayed runs agree when the replay uses the live budget and proposals, which is the snapshot's own integrity check (row A1).

**Weight sweep.** Re-run each snapshot with one objective's weight scaled up and down in turn, and report how the committed stand, weapon and targets change. This is the owner's "if we say DPS, the positioning, weapon and target should change": a weight that changes nothing across a set of fights is doing nothing, and a weight whose small change flips most decisions is unstable. Batched over many captures, it is how the built-in constants are tuned offline rather than by feel.

**Knowledge audit.** Pair each `shot` record's predicted hits with the `shot-event` records that followed: per projectile type, predicted against landed bodies and damage, the trace error at the first wall contact and at the first hit, and the residual learner's factor. A type whose predictions miss by more than the stated tolerance is listed with its law, so a bad law is found from the capture rather than from a feeling in play.

**Outputs** go into the SessionReport as a "Combat decisions" section and into the ledger as measures: the share of decisions on the exhaustive front, mean regret, the share cut by budget, and calibration error per projectile type. Those measures are what "the system is as good as it gets" means in numbers: every decision on the front, zero regret, no cuts, calibration inside tolerance.

## Recorder columns

The telemetry schema moves one minor version in phase A (labels) and another in phase D (plan columns), counted from whatever `RecordBrainTelemetry.Schema` reads when each phase starts, since other lanes move it too. SessionReport reads every version it has seen, and the older labels in older captures.

| Column | Meaning |
|---|---|
| `activity` | `combat` in place of `hunt` and `guard` |
| `fire_outcome` | `fired`, `cooldown`, `no-use-worth-firing`, `hands-busy`, `not-fighting`, `no-weapon` |
| `plan_id`, `plan_segment`, `plan_stand` | the committed plan and where the body is meant to be |
| `plan_value` | the weighted value, and the vector in a compact form |
| `plan_front` | how many plans survived the dominance filter |
| `plan_cut` | whether the budget cut this decision's search |
| `plan_invalid` | the validity condition that ended the last plan |
| `knowledge_residual` | the residual learner's sampled and mean factor for the weapon fired |
| `shot_error` | the last closed shot's predicted-against-landed damage |

## Session report checks

- `CheckTheWeaponKnowledge.cs` (new): calibration per type, laws that never became predictable, items fired with no observed volley, weapons refused at the slot and why.
- `CheckTheFight.cs` gains: plans decided against performed (the stand reached and the planned uses fired from it), plan churn per minute (the flicker detector for B11), time with a hostile on the player while Combat was not current (the eagerness check for F2 and F3), and `not-fighting` rows while a damageable hostile was within reach.
- `CheckTheChoices.cs` keeps its rejected-alternative-scored-higher check, now reading Combat's offer.

## Overlay

`DrawBrainOverlay` gains two layers behind its existing bit flags: **simulated uses** draws each planned use's traces with bounce points, child branches and predicted hit marks, beside the actual projectile path once it flies, so a wrong law is visible as two lines diverging; **plan** draws the proposed stands coloured by weighted value, the committed segments as numbered stands with arrows and timing, and on hover the objective vector and the front size.
