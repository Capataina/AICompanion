# 4 Attack Planning — stands, timed plans across weapons, the objective vector, commitment

The planner lives in `Companion/Brain/Activities/Combat/Planning/` and is called only by the Combat activity and the firing interaction. It turns the enemy forecast, the positioner's stand verdicts and the simulator into one committed plan.

## What a plan is

A plan is a short timeline of segments. A segment is a stand held while uses are fired from it; the next segment begins when the body arrives at its stand, or later if waiting for an effect of the previous segment is worth more.

```csharp
public sealed record AttackPlan(int Id, AttackSegment[] Segments, CombatOutcome Outcome, float Weighted,
    PlanValidity Validity, bool BudgetCut);

public sealed record AttackSegment(StandProposal Stand, StandVerdict Verdict, int ArriveTick, int StartTick, int EndTick,
    PlannedUse[] Uses, SegmentEnd EndsWhen);        // TargetsDead | NextSegmentWorthMore | Horizon

public readonly record struct PlannedUse(WeaponId Weapon, Vector2 Muzzle, Vector2 AimPoint, int FireTick, int SimulationKey);

public readonly record struct StandProposal(Vector2 Stand, StandReason Reason, WeaponId? ForWeapon, int[] TargetSlots);
```

The first segment's stand may be where the body already is. Uses during travel between stands are fired from points sampled along the straight line at the weapon's use cadence; the positioner's route is not searched per plan, so this is an approximation named in the recorder as such.

## Stand proposals come from what the weapons do

`ProposeFiringStands.cs` runs a set of generators. Each reads knowledge and the enemy forecast, and none names a weapon category. Proposals are deduplicated to half a tile and sent to the positioner in one batch for verdicts (file 5). How many each generator may emit is taken from the decision's budget, never a constant.

| Generator | Proposes | Serves |
|---|---|---|
| `HereAndCompany` | the body's current point, and points inside the player's predicted intent region from which a use reaches a target | B1 |
| `BestRange` | for each weapon and each priority target, the distance at which the weapon's simulated yield per use peaks (flown along the line from the target toward the body and toward the player's side), at a few bearings biased to open air | B2, B3 |
| `PierceLines` | for any weapon whose simulated pierce exceeds one, points on the extension of lines through chains of predicted bodies — worm segments are a chain — at that weapon's best range, both ends | B4 |
| `FloorFlanks` | for a law with gravity and a reflecting or stopping floor response, points at a group's two sides at a height from which the simulated shot meets the floor before the group | B6 |
| `AboveArea` | for a law with learned area, points above a group from which the simulated drop lands at the group's centre when its trigger fires | B7 |
| `BankShots` | for a law with a reflecting wall response and a target with no line, points in the reachable region from which `SolveAims`' bank sweep reaches it | B8 |
| `SafeRange` | when the companion's harm weight is high, points at the far edge of the longest-reaching weapon's useful range | self-preservation |

## Inside a segment: which use next

Once a stand is fixed, uses are chosen greedily at each tick the hands are ready, which is today's `EvaluateAttackOutcomes` continuation generalised. The companion keeps one shared cooldown across weapons, one weapon per use, as now. The candidate uses at a ready tick are every weapon × every aim `SolveAims` offers against the targets still alive; the one with the largest weighted marginal gain given the plan's state so far is taken. This is where "shotgun the goons, sniper the boss" is chosen at a fixed stand.

## Rolling the plan's state forward

`EvaluateAttackOutcomes.cs` keeps the plan's own copy of the enemies and applies every simulated hit in tick order:

- **life** is reduced and capped at zero, so overkill and duplicate pellets earn nothing twice;
- **a kill** removes the body from later uses' targets and records the kill tick;
- **a push** displaces the body's later predicted positions by the weapon-effects table's expected push, held over the settle window, as `Arsenal.InducedDanger` does today; the danger it adds is charged;
- **a debuff** marks the body for the other weapons' `DamageIfDebuffed` by the learned chance and length, as today;
- **delayed hits** — a grenade's fuse, a child spawned on a timer — land at their own simulated ticks, so a later segment's uses see the group already wounded. This is what lets B7's pierce be worth most when timed to the explosion.

## The objective vector

```csharp
public readonly record struct CombatOutcome(
    float DamagePerSecond,       // higher: health-capped damage over the plan, per second, in units of the encounter's life at risk
    float ThreatRemoved,         // higher: Σ over bodies of danger × fraction of life removed, kills counted in full
    float PlayerHarmPrevented,   // higher: expected hits on the player that removed or pushed-away bodies no longer land, in units of his life
    float CompanionHarmTaken,    // lower: predicted damage to the body at its stands and along its travel, in units of its life
    float PushDangerAdded,       // lower: danger the plan's pushes add toward either body
    float CompanyGap,            // lower: time-integrated distance outside the player's predicted intent region, in units of the region's size × horizon
    float TimeToFirstDamage,     // lower: travel + cooldown + flight to the first landed hit, in units of the horizon
    float ManaSpent);            // lower: in units of the pool
```

Every objective is in a natural unit so that weights compare like with like and a decision's numbers mean the same thing in every fight; min–max normalisation across a decision's candidates was rejected because it makes a weight's meaning change with whichever candidates happened to be generated. **Finishing low-health enemies** is not a separate objective: a wound that kills removes all of that body's threat, so it is already worth more per damage.

`CompanyGap` reads `PlayerIntentRegion`: the region is carried forward over the plan's horizon by its own lead and velocity, and the body's planned positions are measured with `GapBeyond`. A plan that kills a slime from inside the region pays nothing; one that flies back to it pays for every tick outside.

`CompanionHarmTaken` uses `ChooseUsefulPosition.PredictedExposureAt`'s question, moved into the verdict (file 5), plus the threat sense's hostile projectile predictions along the travel line.

## Undominated plans, then built-in weights

`KeepOnlyUndominated.cs` drops a plan when another is at least as good on every objective within that objective's tolerance and better on one; the tolerances are each objective's measurement noise, so two plans that differ by rounding are not treated as different. `WeighCombatObjectives.cs` computes the weights from the senses and returns the weighted sum:

| Weight | Rises with |
|---|---|
| player harm prevented | `ThreatSense.PlayerDanger` |
| companion harm taken | the companion's missing life share and `CompanionDanger` |
| threat removed and damage | the share of the encounter's danger still alive |
| company gap | the player travelling, and falls as `PlayerDanger` rises (defending him is company) |
| push danger | the player's and companion's proximity to pushed bodies |
| time to first damage | `PlayerDanger`, because a late save is no save |
| mana | the pool's emptiness, since magic already tires at the gradient |

The shapes are fixed here; the constants live in `BehaviourWeights` beside every other tunable and are tuned offline by the audit tool's weight sweeps (file 7), never by a player setting.

## Searching: a beam over timed segments

`SearchAttackPlans.cs`:

```
level 1  proposals (all generators) → verdicts → for each reachable stand, the greedy segment from it
         → outcome → drop dominated → keep the best B by weighted value
level 2  for each kept plan: proposals re-run against the enemies its segment leaves alive, and
         start ticks at arrival and at each delayed-effect tick of the previous segment
         → greedy segment → outcome of the whole plan → drop dominated → keep B
level 3  the same, only when budget remains
commit   the best weighted plan among the undominated survivors
```

Candidates at each level are evaluated best upper bound first — the value of a segment's uses with no geometry in the way, today's `IdealShotValue` generalised — and the search stops when the rescore's millisecond budget (`PlanningBudget`, from `BehaviourWeights`) is spent. A cut search is recorded as cut and the Combat offer is **unresolved**, never known-unusable, which is the repository's rule for every bounded search.

## Commitment: kept by validity, never by a bonus

`CommitAttackPlan.cs` keeps one `AttackPlan`. It stays committed while all of these hold:

- its current segment's stand still has a reachable verdict and still lies in the region it was admitted against;
- every body it targets is still alive, or was killed by the plan;
- no hostile has appeared whose urgency to either body exceeds the highest urgency the plan was admitted against;
- the player's intent region has not moved so far that the plan's company gap has doubled;
- the hands have made progress: a planned use fired or a planned hit landed within the stall window.

When any fails, the next rescore searches afresh; when a segment ends, the plan advances. A plan is never replaced merely because a rival scores higher on one rescore, which is the walker's lesson that a destination is kept by membership rather than by a bonus, and what keeps B11 from flickering. A stall defers every body the stalled plan targeted, not only the primary, because changing target is not progress.

## The worked examples, as the planner sees them

- **B1 slime behind a travelling player.** `HereAndCompany` proposes points inside the moving region with a bow line to the slime; `BestRange` proposes a point near the slime. The near point wins on time to first damage and loses heavily on company gap while the player travels; the in-region point is undominated and wins on weights.
- **B2, B3 sniper, bow, shotgun.** `BestRange` flies each weapon's volley at a lone target from several distances: the sniper's yield is flat to its reach, so its best range is the far one with the least exposure; the shotgun's yield rises as the spread tightens on the box, so its best range is close. On a lone boss the shotgun's close plan wins on damage per second and loses on harm taken; the weights decide by the companion's life.
- **B4 worm.** `PierceLines` proposes the extension of the segment chain; the simulated pierce crosses many segments, so damage dominates the perpendicular stands.
- **B5 goons then boss.** Level 1's best segment is the far stand: the sniper on the boss while the shotgun takes goons that come close, because the close stand's harm taken is high while goons live. Level 2, proposed against the enemies left alive, finds the close stand now cheap, and the two-segment plan beats both single segments.
- **B6 roller, B7 grenade then pierce.** `AboveArea` proposes the drop point; level 2 proposes `FloorFlanks` and `PierceLines` against the wounded group with a start tick at the grenade's explosion, and the timed pierce through a group already on low life is worth more than either use alone.
- **B8 bank shot.** With no direct aim, `BankShots` proposes points from which the reflecting law reaches the target; a straight weapon offers none, so only the bouncing weapon has a plan.
- **B9 danger.** Player harm prevented dominates the weights when `PlayerDanger` is high; the Combat offer's value rises above work (file 5). An idle enemy far off leaves the weights on company and the value below the vein's.
