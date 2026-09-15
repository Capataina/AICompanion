# 9 Build Phases — ordered work packages, their acceptance, lanes and reviews

Each phase lands on main behind its rows with `sh Tools/verify.sh` reading no red row, a sentinel review before merge (and a Codex review beside it whenever Codex is available), its folder guides written in the same commit, and `build.txt` moved by the rule in the root guide. A phase is struck here in the commit that lands it.

```
0  probe and move ──► A  one Combat ──► play: tune eagerness
                  └─► B  recording and volleys ──► C  laws, responses, simulation ──► D  vector, planner, audit ──► E  stands, sequences, commitment ──► play protocol
                                                                                                             └─► G  saving and modifier seam
```

A and B are independent after phase 0 and can run as parallel lanes in separate worktrees; C needs B; D needs A and C; E needs D; G needs C and the mastery session's bonuses record.

## Phase 0 — probe the engine, move the files, keep behaviour identical

**Goal.** Establish the facts the learners depend on and put every file in its final folder before any behaviour changes, so later phases are diffs in place rather than moves tangled with logic.

- Decompile `Terraria.Projectile` and write down, in `WeaponKnowledge/CLAUDE.md`, the order of `AI`, `PostAI`, `HandleMovement`, `OnTileCollide`, `extraUpdates` sub-steps and `ModSystem.PostUpdateProjectiles`, and whether a full `Projectile.Update` runs headless on the EngineReplay host.
- Move, with no logic change: `CompanionMana.cs` → `CharacterBody/`; `CompanionWeapon.cs`, `ItemWeapon.cs`, `TrackLandedHits.cs`, `ObserveShotOutcomes.cs` → `Interactions/Firing/`; `LearnWeaponEffectsOnEnemies.cs`, `LearnAttackOutcomes.cs` → `WeaponKnowledge/Learning/`; `EvaluateAttackOutcomes.cs` → `Activities/Combat/Planning/`; `Arsenal.cs` → `Interactions/Firing/` pending its split. Namespaces follow. Fixture folders under `Tools/EngineReplay/Combat/` created and existing rows moved.
- Rewrite `Companion/Weapons/CLAUDE.md`'s content into the new folder guides; delete `Weapons/`; remove "weapons stay outside the brain" from `Companion/CLAUDE.md`, `Companion/Brain/CLAUDE.md` and the root guide.
- Update the EngineReplay project's copied-folder compile items and `check-navigation-boundary.sh`'s folder sets.

**Acceptance.** The suite's row set and results are identical before and after (the ledger's scoreboard against the parent shows no changed row), the boundary check passes, and `git log --follow` traces each moved file. **Review**: a sentinel checks that no logic moved with the files. **Version**: patch.

## Phase A — one Combat activity, and only Combat fires

**Goal.** The owner's ruling, on today's arsenal and stand pricing, so eagerness can be tuned in play before the planner changes how fights look.

- `FightEnemies.cs` merging `ProtectPlayer` and `PursueAttackOpportunity`: guarding's threat binding and urgency lift, hunting's admissibility, firing-position access (still `ResolveFiringOpportunity`), progress and stall deferral, one offer. Its value is the larger of the two old values under their existing terms, scaled by `CombatValueScale`, so behaviour before tuning is recognisably today's.
- `ChooseBehaviour` registers six activities; `RequestKind.FireFrom` is added beside `Guard` and `LineOfFire`, which the activity still uses in this phase.
- `CoordinateBrainTick.Engage` gated on Combat; fire outcome `not-fighting`.
- Preferences `Hunting` → `Combat` reading the old key; profile card comment.
- The telemetry schema's next minor version; SessionReport reads both label sets; `CheckTheFight` gains eagerness and `not-fighting` checks.
- Rows F1–F6; the nine combat fixtures and seven label fixtures rewritten (file 6).
- README's System In Place, the Slate architecture law's "until that build lands" clause, and `Activities/Combat/CLAUDE.md` rewritten for what is now built.

**Acceptance.** F1–F6 green with their mutations red; every rewritten fixture keeps the behaviour it proved (each commit body names the row and its old name); a world run with a hostile shows Combat winning. Then **a play session with the owner** to tune `CombatValueScale` and the danger lift, recorded and read before phase D starts. **Review**: sentinel on the merge's lost behaviours — for each of the old activities' responsibilities, where it now lives. **Version**: minor, because the brain's activity set changed.

## Phase B — recording flights and learning volleys

**Goal.** Watch every projectile use, the player's included, and reproduce learned volleys.

- `RecordProjectileFlights.cs` on the hooks phase 0 established; `GroupSpawnsIntoUses.cs`; `LearnVolleyShapes.cs`.
- `ItemWeapon.Fire` spawns the learned volley (expanded at fixed quantiles, types substituted from ammo where the slot names none), falling back to one projectile for an unobserved item.
- `SpoofOwnerInputForShots.cs`.
- God's-eye `volley-observed` and extended `shot-event` records.
- Rows K0, K6, K8; S3.

**Acceptance.** Rows green with mutations red; a capture where the player fires a vanilla shotgun and then hands it over shows the companion's volley matching his in count and spread. **Version**: patch.

## Phase C — flight laws, responses and the simulator

**Goal.** Replace the four-number arc and the swept trace with learned laws, responses and `SimulateUse`, and remove every cap.

- `FlightLaw.cs`, `FitFlightLaws.cs` with today's priors as the default law; `LearnWallResponses.cs`, `LearnHitResponses.cs`, `LearnChildSpawns.cs`; area.
- `ForecastEnemies.cs`, `SimulateUse.cs`, `SolveAims.cs`, `CacheSimulatedUses.cs`, `ApplyCompanionModifiers.cs` with no modifiers yet.
- Today's `Arsenal.Forecast` calls `SimulateUse`, so the existing chooser runs on the new model before the planner exists — the model is proved under the decision everyone already understands.
- `MaxPierceCounted`, `SamplesPerShot`, `MaxFlightsKept` and `MinPairsToFit`, and the aim-offset steps removed as file 1's number table says; `CompanionGear`'s unfittable branch removed and the owner-anchored property added; `Aiming/` deleted.
- God's-eye `flight-law`; the overlay's simulated-uses layer; `CheckTheWeaponKnowledge.cs`.
- Rows K1–K5, K7, K9; S1, S2, S4, S6; the existing arc and weapon-learning rows kept green.

**Acceptance.** Rows green with mutations red; the hits-per-shot measure `VerifyArcLearning` files for a stationary target does not fall; the knowledge audit's calibration on a vanilla play capture is inside tolerance for every weapon in the play protocol's first group. **Review**: sentinel plus a second opinion on the fitting algorithm, since it is the component most likely to be right on fixtures and wrong in play. **Version**: minor.

## Phase D — the objective vector, the planner's first form, and the audit

**Goal.** One-segment plans chosen on the vector, with the audit landing beside the planner so every later tuning is measured.

- `CombatOutcome.cs`, `EvaluateAttackOutcomes` returning the vector, `KeepOnlyUndominated.cs`, `WeighCombatObjectives.cs` with weights set to reproduce today's blend first, `SearchAttackPlans.cs` at depth one over today's stand candidates, `CommitAttackPlan.cs`, `PlanningBudget.cs`.
- `Arsenal.cs` split into `CompanionCombat.cs`, `FireDueUse.cs` and the planner; `CompanionNPC` holds `CompanionCombat`; `ThreatSense`'s intervention estimate from the plan.
- `combat-plan` and `combat-snapshot` records; the schema's next minor version; plan columns.
- `Tools/CombatAudit/` with the search audit, weight sweep and knowledge audit; SessionReport "Combat decisions" section; ledger measures.
- Rows P9, P10, P11, P12; A1–A4; S5 once the seam exists.

**Acceptance.** With today's weights the committed decisions on the existing combat fixtures match phase C's choices (the vector changes representation, not behaviour); A1 reproduces live decisions exactly; the audit runs on a play capture and files its measures. **Version**: minor.

## Phase E — stands from the weapons, timed sequences, verdicts

**Goal.** The behaviours B1–B8.

- `ProposeFiringStands.cs` with all seven generators; `AssessStands` and `RequestKind.FireFrom` routing in the positioner; `FiringStandShare`, `StandoffFromTarget`, `SolveShotAtArrivalWithAnyWeapon`, `HandedModels`, the `Guard` and `LineOfFire` arms and kinds removed; `ResolveFiringOpportunity.cs` deleted.
- Search depth two and three with event-aligned segment starts; the company gap objective; weights moved from today's blend to the shapes in file 4, tuned by weight sweeps over the phase D play captures.
- The overlay's plan layer; `CheckTheFight`'s plan performed and churn checks.
- Rows P1–P8; C1.

**Acceptance.** Rows green with mutations red; C1's 99th percentile inside the frame budget; the search audit on the play protocol's captures shows the committed plan on the exhaustive front for the stated share of uncut decisions, and every off-front decision names its missing generator or its cut. Then **the full play protocol** with the owner. **Review**: sentinel on the positioner removal's radius — every caller of the removed members and request kinds. **Version**: minor.

## Phase G — keeping knowledge and the mastery seam

**Goal.** Knowledge survives sessions; mastery's weapon nodes change fights at once.

- `WeaponIdentity.cs`, `PersistWeaponKnowledge.cs` on `CompanionPlayer`, knowledge schema version.
- `ApplyCompanionModifiers.cs` reading the mastery bonuses record from the mastery session's build.
- Rows K10; S5 with real nodes.

**Acceptance.** Rows green; a save made in one session loads the same laws in the next with a different mod load order; levelling Piercing mid-fight changes the next plan's predicted hits with no relearning, seen in the capture. **Version**: patch.

## Lanes

| Lane | Seat | Isolation | Reads first |
|---|---|---|---|
| 0 | one android | main tree, mechanical, one commit per move group | files 6 and 9 |
| A | one android | worktree off phase 0 | files 1, 5, 8 |
| B | one android | worktree off phase 0 | files 2, 8 |
| C | one android after B merges | worktree | files 2, 3, 8 |
| D | one android | worktree after A and C | files 4, 5, 7, 8 |
| E | one android | worktree after D | files 4, 5, 8 |
| G | one android | worktree after C and mastery | files 2, 3 |

Every brief carries the worktree traps named in the root guide, the headless contract (never launch the game), and the rule that a lane returns proposed README text for the owner's sections rather than editing Expected Behaviour itself.
