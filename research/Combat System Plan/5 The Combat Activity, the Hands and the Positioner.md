# 5 The Combat Activity, the Hands and the Positioner — the brain seams

This file specifies every place the new combat touches the brain that exists today: the activity contract, the family chooser, the hands in `CoordinateBrainTick`, the positioner, the threat sense, preferences and the grants. Names are today's, read from main at `c15c140`.

## One activity: `FightEnemies`

`Companion/Brain/Activities/Combat/FightEnemies.cs` replaces `ProtectPlayer.cs` (246 lines), `PursueAttackOpportunity.cs` (445 lines) and `ResolveFiringOpportunity.cs`. It implements `CompanionAction` as every activity does.

```csharp
public sealed class FightEnemies : CompanionAction
{
    public override string Name => "combat";
    public override PurposeFamily Family => PurposeFamily.Combat;

    public override void Prepare(in ActionContext ctx);      // below
    public override float Score();                           // the prepared value, no world reads
    public override float ForecastTicks();                   // the committed plan's remaining duration
    public override float TaskTicks();                       // the same, so the chooser's horizon discount prices it
    public override void Enter(in ActionContext ctx);        // commits the prepared plan
    public override void Exit(in ActionContext ctx);         // releases the plan and its admission
    public override void Suspend(in ActionContext ctx);      // keeps the plan, marks it suspended by safety
    public override void ObserveOutcome(in ActionContext ctx); // progress: planned uses fired, planned hits landed
    public override AttemptConclusion ConcludeAttempt(int productiveEffects);
    public override PositionRequest Execute(in ActionContext ctx); // FireFrom the current segment's stand
}
```

**Prepare**, in order:

1. No weapon in a slot (`CompanionGear.Accepts` on each weapon slot) → `NoOpportunity: no-weapon`, value zero. This answers the unarmed-orb card by construction.
2. No damageable hostile inside the allowance (`AllowsTarget`, the intent-region envelope every activity uses, and Terraria's chase predicate for damageability) → `NoOpportunity: no-eligible-target`.
3. Build the enemy forecast (file 3). If a plan is committed and valid (file 4), re-evaluate its outcome against this forecast; otherwise run the search.
4. Classify: a plan with a solved first use → `Usable`; search cut by budget or the reach sense answering `NotYet` for every proposal → `Unresolved`, value zero; every proposal proven unreachable or no use reaching any target from any reachable stand → `KnownUnusable` with the reason.
5. Capture the value, the plan and its target generations together, so repeated `Score` reads cannot retarget.

**The value** offered to the chooser is the committed plan's weighted outcome mapped into the utility band: `CombatValueScale × saturate(weighted)`, lifted by `(1 + CombatPlayerDangerLift × PlayerDanger)`. The lift is guarding's property kept: `GuardUrgency` let a threat on the player exceed a committed ordinary action, and Combat must be able to take the body from a vein when something is on the player (row F3). The companion's own danger is not a separate skin on the score any more — it is `CompanionHarmTaken` inside the vector — so there is one place self-preservation is priced.

**Eagerness is tuned, not forced.** Keeping company is already capped (`KeepCompanyFarCap`) and priced as the fallback; a usable Combat plan whose company gap is small is nearly free, so it beats keeping company whenever it is worth anything. Phase A sets `CombatValueScale` against today's hunting and guarding scores, then a play session tunes it with the owner so Combat wins over keeping company almost whenever an enemy it can hurt is near, and sits below mining and chopping unless danger lifts it. No priority list is written.

**Execute** returns `new PositionRequest(RequestKind.FireFrom, segment.Stand.Stand, primaryTarget)`. A null resolution from the positioner invalidates the plan for the next rescore.

**Progress and stalls** generalise hunting's rule: an attempt renews when a planned use fires or a planned hit lands on a planned target generation; a stall over the stall window defers every body the plan targeted; an attempt concludes when the plan's targets are gone after a planned hit.

## The family chooser

`ChooseBehaviour` registers `new FightEnemies()` in place of `new ProtectPlayer(firingAccess)` and `new PursueAttackOpportunity(firingAccess)`, and the shared `ResolveFiringOpportunity` instance goes. `PurposeFamily` keeps its three values. `EvaluatePreparedActivities`' shared factors — the commitment multiplier for the incumbent and the threat-horizon discount on long tasks — apply to Combat unchanged.

## The hands fire only on a Combat tick

`CoordinateBrainTick.Engage` is today's always-on hands. It becomes:

```csharp
private bool Engage(CompanionNPC companion, in ActionContext ctx, HandGrant hand)
{
    if (hand != HandGrant.Available) { companion.Combat.NoteHands("hands-busy"); return false; }
    if (ActivityOwner.Current is not FightEnemies fight) { companion.Combat.NoteHands("not-fighting"); return false; }
    return companion.Combat.FireDueUse(ctx, fight.CommittedPlan);
}
```

`FireDueUse` (in `Interactions/Firing/`) fires the plan's due use when the weapon is ready and the body is within the stand's arrival tolerance, re-simulating that one use from the actual muzzle so the aim is the live one; while the body is travelling it fires the best use from where the body is, chosen by the same evaluator with the plan's targets. The fire outcome vocabulary the recorder writes becomes: `fired`, `cooldown`, `no-use-worth-firing`, `hands-busy`, `not-fighting`, `no-weapon`. `not-fighting` is separate from `no-target` on purpose, so a quiet weapon during mining is never read as a missing target.

Downed and recovery-flight ticks never reach `Engage` with Combat current, so neither fires. The torch keeps filling an otherwise free hand; during Combat the hand is the weapon's.

## The positioner returns verdicts and routes to a chosen stand

`ChooseUsefulPosition` gains one query and one request kind, and loses the firing-stand scoring.

```csharp
public readonly record struct StandVerdict(Vector2 Stand, ReachVerdict Reach, float TravelTicks,
    float ExposureAtStand, float ExposureAlongTravel, bool InAllowance, string Reason);

public void AssessStands(ReadOnlySpan<StandProposal> stands, Senses.Senses senses, Span<StandVerdict> into, ref PlanningBudget budget);
```

`AssessStands` reads only what the positioner already owns: the reach sense's three-valued `Reachable` and `EstimatedTravelTicks` from the body, `PredictedExposureAt` at the stand and sampled along the travel line, and membership of the Combat allowance. It runs no route search, so the navigation boundary is unchanged.

`RequestKind.FireFrom` resolves like `Exact` — the named point, hovered at, with the evade layer on top — and refuses with a reason when the stand is no longer reachable, which the activity reads as plan invalidity.

**Removed from `ChooseUsefulPosition`**: `FiringStandShare`, `StandoffFromTarget`, `SolveShotAtArrivalWithAnyWeapon`, `HandedModels`, the `Guard` and `LineOfFire` arms of the spot score, the `FlightModel? profile` parameters of `PrepareOffer` and `Resolve`, and `ShotWindow`/`ShotWindowOffsets` once no caller remains. **Removed from `PositionRequest.cs`**: `RequestKind.Guard` and `RequestKind.LineOfFire`. `CoordinateBrainTick.CountStranded`'s test for `Guard` reads `FireFrom`.

## Other consumers of today's combat

| Consumer | Today | After |
|---|---|---|
| `ThreatSense.SetInterventionEstimate` | `Arsenal.EstimateInterventionTicks` (10 references), arc-free, every hit assumed to land | the Combat planner's predicted kill tick of the most urgent threat, from its best plan; infinite with no weapon or no plan |
| `Arsenal.EstimateRemovalTicks`, `EstimateDelayedAttackValue` | pursuit's valuation | gone with pursuit |
| `Arsenal.BestTarget`, `CanEngage`, `ProfileFor`, `Choose` | hands and activities | gone: the plan names targets and uses |
| `Arsenal.Weapons`, `MaxReach`, `GearSignature` | enumeration | `CompanionCombat.Weapons`, `MaxReach`, `GearSignature` |
| `RecordBrainTelemetry` arsenal columns | last chosen weapon, both weapons' expected, target evidence | file 7's plan columns |
| `ConfigureCompanionPreferences.Hunting` | saved as `"hunting"` | `Combat`, saved as `"combat"`, reading `"hunting"` when `"combat"` is absent so an old save keeps the player's choice |
| `ProfileCard/ControlWorkPreferences` | notes hunting is not a toggle | names combat |

`CompanionCombat` is the one object per companion that replaces `Arsenal` on `CompanionNPC`: it holds the gear enumeration, the knowledge handle, the planner and the firing interaction, and is the only combat surface the rest of the companion references.

## What stays exactly as it is

The evade layer and its threat prediction (every liquid is air to the body since the liquids build, so nothing else takes the body for safety); recovery flight; the reach and light senses; the intent region; the stand-in player that lets hostiles target the companion; experience attribution through player ownership; gear acceptance apart from the unfittable branch and the owner-anchored property; the mana gradient.
