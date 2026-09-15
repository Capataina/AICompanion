# 6 Repository Layout and File Fates — the target tree and what happens to every file

## The organising rule

Combat is behaviour, so it lives in the brain, and it is split by kind exactly as mining is: the decision is an activity, the thing the hands do is an interaction, and what the companion knows about the world is infrastructure beside the senses. A reader looking for "why did it stand there" opens the activity; "why did the shot go there" opens the weapon model; "why did the projectile spawn like that" opens the firing interaction. `Companion/Weapons/` is dissolved.

Why not one combat folder holding everything: it would be the one family organised differently from every other, and its knowledge layer is a model of the world that later readers (the threat sense predicting hostile projectiles, for one) can use without depending on an activity. Why not keep `Weapons/` outside the brain: it was put there by the rule that the brain grants a free hand and the arsenal chooses, which held only while shooting was independent of behaviour.

## The target tree

```
Companion/
├─ CharacterBody/
│  └─ CompanionMana.cs                       moved from Weapons/: a body stat mirrored from the player, like life
├─ Inventory/
│  └─ CompanionGear.cs                       slot acceptance; loses the unfittable branch, gains the owner-anchored property
└─ Brain/
   ├─ CoordinateBrainTick.cs                 Engage fires only on a Combat tick
   ├─ Activities/
   │  └─ Combat/
   │     ├─ CLAUDE.md                        the stance, the offer, commitment, how planning is used
   │     ├─ FightEnemies.cs                  the one Combat activity
   │     └─ Planning/
   │        ├─ CLAUDE.md
   │        ├─ AttackPlan.cs                 AttackPlan, AttackSegment, PlannedUse, StandProposal, PlanValidity
   │        ├─ CombatOutcome.cs              the objective vector, its units and tolerances
   │        ├─ ProposeFiringStands.cs        the seven generators
   │        ├─ EvaluateAttackOutcomes.cs     moved from Weapons/: the plan's state rolled forward, returning the vector
   │        ├─ KeepOnlyUndominated.cs
   │        ├─ WeighCombatObjectives.cs      built-in weights from the senses
   │        ├─ SearchAttackPlans.cs          the beam over timed segments
   │        ├─ CommitAttackPlan.cs           validity, advancing, stalls
   │        └─ PlanningBudget.cs             the millisecond budget and the cut flag
   └─ Infrastructure/
      ├─ Observation/
      │  ├─ ForecastEnemies.cs               the per-decision enemy forecast (file 3)
      │  └─ PredictObservedMotion.cs         unchanged
      ├─ WeaponKnowledge/                    replaces Aiming/ and the learners in Weapons/
      │  ├─ CLAUDE.md                        what is learned, from which hooks, keyed how; the hook-order probe's findings
      │  ├─ Recording/
      │  │  ├─ RecordProjectileFlights.cs    FlightTrace from the global projectile hooks
      │  │  └─ GroupSpawnsIntoUses.cs        player and companion uses from spawn sources
      │  ├─ Learning/
      │  │  ├─ FlightLaw.cs                  the term library and one update under a law
      │  │  ├─ FitFlightLaws.cs              stagewise selection; replaces LearnProjectileArcs.cs
      │  │  ├─ LearnVolleyShapes.cs
      │  │  ├─ LearnWallResponses.cs
      │  │  ├─ LearnHitResponses.cs
      │  │  ├─ LearnChildSpawns.cs
      │  │  ├─ LearnWeaponEffectsOnEnemies.cs moved from Weapons/, unchanged
      │  │  └─ LearnAttackOutcomes.cs        moved from Weapons/; the residual on the simulator
      │  ├─ Simulation/
      │  │  ├─ SimulateUse.cs
      │  │  ├─ SolveAims.cs                  replaces SolveProjectileTrajectory.cs
      │  │  ├─ ApplyCompanionModifiers.cs    the one seam mastery's weapon nodes enter by
      │  │  └─ CacheSimulatedUses.cs
      │  ├─ WeaponIdentity.cs                full-name keys for items and projectiles
      │  └─ PersistWeaponKnowledge.cs        save and load on CompanionPlayer
      ├─ Interactions/
      │  └─ Firing/
      │     ├─ CLAUDE.md
      │     ├─ CompanionCombat.cs            the per-companion combat surface; replaces Arsenal.cs
      │     ├─ CompanionWeapon.cs            moved from Weapons/: the contract
      │     ├─ ItemWeapon.cs                 moved from Weapons/: facts, and spawning a volley or a swing
      │     ├─ FireDueUse.cs                 the plan's due use or the best from where the body is
      │     ├─ SpoofOwnerInputForShots.cs    the cursor at the aim point during companion projectile AI
      │     ├─ TrackLandedHits.cs            moved from Weapons/; gains wall-contact and death events
      │     └─ ObserveShotOutcomes.cs        moved from Weapons/
      ├─ Position/
      │  ├─ ChooseUsefulPosition.cs          AssessStands and FireFrom; firing-stand scoring removed
      │  └─ PositionRequest.cs               FireFrom replaces Guard and LineOfFire
      ├─ Selection/
      │  ├─ ChooseBehaviour.cs               six activities
      │  └─ BehaviourWeights.cs              Guard*/Hunt* become Combat*, planning budget, objective weight constants
      └─ Diagnostics/                        file 7's records, columns, snapshot writer and plan overlay

Tools/
├─ CombatAudit/                              new: replays combat snapshots with an unbounded search, sweeps weights, grades knowledge
├─ EngineReplay/Combat/
│  ├─ Knowledge/                             K rows
│  ├─ Simulation/                            S rows
│  ├─ Planning/                              P rows and C1
│  └─ Activity/                              F rows, and the rewritten hunt and guard fixtures
├─ SessionReport/Checks/CheckTheWeaponKnowledge.cs   new
└─ check-navigation-boundary.sh              WeaponKnowledge/ and Interactions/Firing/ join the no-route-search set; Activities/ is already in it
```

Namespaces follow folders: `AICompanion.Companion.Brain.Activities.Combat`, `.Combat.Planning`, `AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.{Recording,Learning,Simulation}`, `.Interactions.Firing`.

**Dependency direction** is one way and is checked by the boundary script: `Activities/Combat` may reference `WeaponKnowledge`, `Interactions/Firing`, `Position`'s verdict query and `Observation`; `WeaponKnowledge` may reference `Observation` and Terraria's collision; `Interactions/Firing` may reference `WeaponKnowledge`; nothing in `WeaponKnowledge` or `Firing` references `Activities`, except `FireDueUse` reading the committed plan it is handed as a parameter.

## Every file that exists today, and its fate

| File (today) | Fate | Phase |
|---|---|---|
| `Companion/Weapons/Arsenal.cs` | split: gear enumeration and weapon list → `CompanionCombat.cs`; target, weapon and aim choice → planner; `InducedDanger` → `EvaluateAttackOutcomes`; intervention and removal estimates → planner; cooldown and fire → `FireDueUse`; then deleted | A keeps it, D splits it |
| `Companion/Weapons/CompanionMana.cs` | moved to `CharacterBody/` | 0 |
| `Companion/Weapons/CompanionWeapon.cs` | moved to `Interactions/Firing/`; `Hits` and `Model` removed in C | 0, C |
| `Companion/Weapons/ItemWeapon.cs` | moved; `Pierce`'s cap removed in C; `Fire` spawns a volley in B | 0, B, C |
| `Companion/Weapons/EvaluateAttackOutcomes.cs` | moved to `Planning/`; returns the vector in D | 0, D |
| `Companion/Weapons/LearnAttackOutcomes.cs` | moved to `WeaponKnowledge/Learning/`; context loses the `/8` lane denominator in C | 0, C |
| `Companion/Weapons/LearnWeaponEffectsOnEnemies.cs` | moved to `WeaponKnowledge/Learning/` | 0 |
| `Companion/Weapons/ObserveShotOutcomes.cs` | moved to `Interactions/Firing/` | 0 |
| `Companion/Weapons/TrackLandedHits.cs` | moved to `Interactions/Firing/`; the arc-learner hooks move to `RecordProjectileFlights` in B | 0, B |
| `Companion/Weapons/CLAUDE.md` | content distributed to the three new folder guides; deleted | 0 |
| `Companion/Brain/Infrastructure/Aiming/LearnProjectileArcs.cs` | replaced by `FitFlightLaws.cs`; its priors kept as the default law | C |
| `Companion/Brain/Infrastructure/Aiming/SolveProjectileTrajectory.cs` | replaced by `SolveAims.cs` and `FlightLaw` | C |
| `Companion/Brain/Infrastructure/Aiming/CLAUDE.md` | folded into `WeaponKnowledge/CLAUDE.md`; folder deleted | C |
| `Companion/Brain/Activities/Combat/ProtectPlayer.cs` | merged into `FightEnemies.cs`; deleted | A |
| `Companion/Brain/Activities/Combat/PursueAttackOpportunity.cs` | merged into `FightEnemies.cs`; deleted | A |
| `Companion/Brain/Activities/Combat/ResolveFiringOpportunity.cs` | kept inside phase A's `FightEnemies` as its reachability test, deleted in E when stand verdicts replace it | A, E |
| `Companion/Brain/Activities/Combat/CLAUDE.md` | rewritten for the one activity | A, E |
| `Companion/Brain/CoordinateBrainTick.cs` | `Engage` gated on Combat; `CountStranded` reads `FireFrom` | A, E |
| `Companion/Brain/Infrastructure/Position/ChooseUsefulPosition.cs` | `AssessStands`, `FireFrom`; firing-stand scoring removed | E |
| `Companion/Brain/Infrastructure/Position/PositionRequest.cs` | `FireFrom` replaces `Guard` and `LineOfFire` | A adds `FireFrom` beside them, E removes them |
| `Companion/Brain/Infrastructure/Selection/ChooseBehaviour.cs` | six activities | A |
| `Companion/Brain/Infrastructure/Selection/BehaviourWeights.cs` | combat weights | A, D, E |
| `Companion/Brain/Infrastructure/Diagnostics/RecordBrainTelemetry.cs` | combat labels, then plan columns | A, D |
| `Companion/Brain/Infrastructure/Diagnostics/RecordGodsEyeEvents.cs` | new records | B, D |
| `Companion/Brain/Infrastructure/Diagnostics/DrawBrainOverlay.cs` | simulated traces, plan layer | C, E |
| `Companion/Inventory/CompanionGear.cs` | unfittable branch removed; owner-anchored property | C |
| `Companion/PlayerIntegration/ConfigureCompanionPreferences.cs` | `Hunting` → `Combat` with the old key read | A |
| `Companion/ProfileCard/ControlWorkPreferences.cs` | comment names combat | A |
| `Companion/CharacterBody/CompanionNPC.cs` | holds `CompanionCombat` instead of `Arsenal` | D |
| `Tools/EngineReplay/*.csproj` | the compile item that copies `Aiming/` becomes `WeaponKnowledge/` | 0, C |
| `Tools/EngineReplay/Combat/VerifyHuntProgress.cs`, `VerifyHuntAdmissibility.cs`, `VerifyFiringPosition.cs`, `VerifyCombatPurpose.cs`, `VerifyCombatActorMatrix.cs`, `VerifyEncounterContext.cs`, `VerifySafetyAftermath.cs`, `VerifyKnockbackAwareness.cs`, `VerifyOfferValidity.cs` | rewritten against `FightEnemies`, each row keeping the behaviour it proved | A, E |
| `Tools/EngineReplay/Combat/VerifyArcLearning.cs`, `VerifyWeaponLearning.cs`, `VerifyItemWeapon.cs`, `VerifyAttackOutcomes.cs`, `VerifyHandedGear.cs` | move to `Knowledge/` and `Simulation/`; rows kept, extended | 0, B, C |
| `Tools/EngineReplay/Movement/VerifyFollowRecoveryAndProtection.cs`, `Assistance/VerifyCompanyIsTheFallback.cs`, `Assistance/VerifyCompanionActivities.cs`, `Lifecycle/VerifyCompanionPreferences.cs`, `Observation/VerifyFamilyOffers.cs`, `VerifyPreparedActivities.cs`, `VerifyObservationLifecycle.cs` | label and registration changes | A |
| `Tools/SessionReport/Checks/CheckTheFight.cs`, `CheckTheChoices.cs`, `CheckDecisionContracts.cs`, `CheckThePlayersReference.cs`, `CheckTheRecord.cs`, `Write/MultiRunReport.cs`, `Measures/MeasureCommitmentAndChoice.cs`, `Program.cs`, `Tests/ChronicleTests.cs` | read `combat` labels and still read `hunt` and `guard` in captures before the schema bump | A |

## Folder guides written in the phase that creates their folder

`Activities/Combat/CLAUDE.md`, `Activities/Combat/Planning/CLAUDE.md`, `WeaponKnowledge/CLAUDE.md`, `Interactions/Firing/CLAUDE.md` and `Tools/CombatAudit/CLAUDE.md` are each written as a mechanism walkthrough in the commit that creates the folder, and `Companion/Brain/CLAUDE.md`, `Companion/CLAUDE.md` and the root `CLAUDE.md` lose the sentence that weapons stay outside the brain in phase 0.
