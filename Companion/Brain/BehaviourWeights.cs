#nullable enable

namespace AICompanion.Companion.Brain.BehaviourSelection;

/// <summary>
/// Every tunable number of the brain in one place, so a playtest note ("it hugs too
/// close", "it gives up on loot too soon") is one edit here and nowhere else.
/// </summary>
public static class Weights
{
    // Wall-clock budgets are live-brain policy. Headless core tests leave them disabled so
    // machine load cannot change a fixture's reachability verdict.
    public const double RouteSearchMilliseconds = 8d;
    public const double MovementPreparationMilliseconds = 2d;
    public const double PositionReachMilliseconds = 2d;
    public const double PositionAimingMilliseconds = 2d;
    public const double EscapeSearchMilliseconds = 2d;
    public const int EscapeSearchWork = 120;
    public const int HuntRetryTicks = 180;
    // Useful damage remains valuable across the forecast window. Timely threat
    // removal earns extra value without letting kill count dominate healthy targets.
    public const float AttackDelayedDamageFraction = .4f;
    public const float AttackPreventedHarmWeight = 2f;
    public const float AttackFinishingValue = 4f;
    public const double TotalPlanningMilliseconds = 12d;
    public const float ProtectionLeadTicks = 60f;
    public const float GuardReleasePressure = 0.08f;
    public const int GuardClearTicks = 90;
    // Recovery is a following fallback, not a traversal available to route search or mastery.
    public const float FollowRecoveryDistance = CalmBandFar * 2f;
    public const float FollowRecoveryArrival = 80f;
    public const float FollowRecoverySpeed = 12f;
    public const float FollowRecoveryAcceleration = 0.45f;
    public const float ActivityContinuationFactor = 1.25f;
    public const float WorkSiteRadius = 160f;
    public const int WorkCollectionTicks = 600;
    public const float InterruptibleActionTicks = 12f;
    public const float FollowDuringUsefulWork = .2f;
    public const int ObjectiveProgressWindowTicks = 180;
    public const float ObjectiveProgressPixels = 32f;
    /// <summary>Bonus multiplier the running action keeps, so scores do not flicker.</summary>
    public const float Commitment = 1.15f;

    /// <summary>
    /// What the running action's bonus becomes once the body has stopped making ground toward what
    /// that action asked for. A flat incumbency bonus is a claim that continuing is worth more than
    /// starting over, and that claim is false for a task whose approach has failed — it inverts
    /// into the thing that keeps the failure selected. On 2026-09-11 the companion spent ticks
    /// 18,501 to 21,531 unbroken on one pot it never reached, holding 0.62 x 1.15 = 0.713 against
    /// mining's 0.70, while its own mining system reported ore in actual swing reach on 979 of
    /// those ticks. Below one, so a stalled task is easier to displace than a fresh one rather
    /// than harder: 0.62 x 0.6 = 0.372 loses to that ore, which is the outcome the player watched
    /// not happen. Progress restores the ordinary bonus within one window.
    /// </summary>
    public const float CommitmentWhileStalled = .6f;

    /// <summary>
    /// The urgency ladder, and the reason it is a ladder rather than three independent numbers.
    /// An ordinary action scores in 0..1, so a running one carries up to <see cref="Commitment"/>
    /// and anything that must be able to *interrupt* it has to clear that product, not merely
    /// reach 1. Guard could not: it topped out at 1 against a following body already at 1 × 1.15,
    /// so on every high-danger tick of the 2026-09-09 underground session where following was
    /// running, guarding lost by construction and no amount of danger could change it. The
    /// companion stood holding a torch with the player's danger reading full.
    ///
    /// So each tier is scaled to clear the tier below it *including* the bonus that tier carries
    /// as the incumbent. Guard beats a committed ordinary action; survive beats a committed
    /// guard, because nothing the companion could do for the player is worth drowning for. Change
    /// one of these and the ones above it move with it, or the ladder silently loses a rung.
    /// </summary>
    public const float GuardUrgency = 1.25f;   // > 1.00 × Commitment
    public const float SurviveUrgency = 1.5f;  // > GuardUrgency × Commitment
    public const int RefugeSearchRadiusTiles = 24;
    public const int RefugeRecheckTicks = 30;

    /// <summary>How fast the horizon charge falls once an action would outlast the horizon, in ticks of overrun to zero.</summary>
    public const float HorizonOverrunToZero = 240f;

    /// <summary>Distance band to the player when calm, in px: closer than Near or further than Far scores worse.</summary>
    public const float CalmBandNear = 96f;
    public const float CalmBandFar = 560f;

    /// <summary>
    /// Following ends in this close two-axis region. CalmBandFar remains the wider boundary for
    /// wandering and excursions; using it as ordinary follow comfort kept the companion a screen
    /// away even on an unobstructed floor.
    /// </summary>
    public const float FollowHorizontalComfort = 192f;
    public const float FollowVerticalComfort = 64f;

    /// <summary>
    /// The band when the player is in danger. It is wider than the calm band's near edge rather
    /// than tighter, because the threats are on the player: a band that closes as danger rises
    /// scores a ranged companion into the melee that is already hitting him, and what should
    /// decide the distance is whether the shot solves from further out.
    /// </summary>
    public const float ThreatBandNear = 32f;
    public const float ThreatBandFar = 384f;

    /// <summary>
    /// Distance band to the player while guarding him. Guarding means being able to shoot what is
    /// attacking him, not standing on him, so this is wide and the line of fire does the rest.
    /// </summary>
    public const float GuardBandNear = 48f;
    public const float GuardBandFar = 360f;

    /// <summary>
    /// Distance band from the thing being shot at. Distance is preferred across the band rather
    /// than merely permitted, because the line-of-fire factor already refuses a spot that cannot
    /// reach the target, so anything the shot still solves from is free to be further away.
    /// </summary>
    public const float StandoffNear = 120f;
    public const float StandoffFar = 520f;

    /// <summary>Beyond this the companion drops everything and comes back, whatever else is going on.</summary>
    public const float LeashHard = 1400f;

    /// <summary>
    /// How far the companion may stray before hunting starts losing value, and how much further
    /// takes it to nothing. Hunting is the opportunistic behaviour — something to do when there is
    /// little else on — so it is the one that yields to staying with the player, and the free
    /// distance is about a screen's half-width because the standing goal is that he can see it.
    /// </summary>
    public const float HuntLeashFree = 700f;
    public const float HuntLeashToZero = 900f;

    public const float WanderFloor = 0.05f;
    public const float FollowIntentDistance = 140f;
    public const float RegroupFullDistance = 640f;
    public const float RegroupFreeReturnTicks = 60f;
    public const float RegroupFullReturnTicks = 240f;

    /// <summary>
    /// Stranded: the companion's plans to the player return nothing and the flood from its feet
    /// closes without spending its budget, so it is in a pocket the world seals. After this many
    /// ticks of that it stops pressing the wall nearest the player and walks the pocket instead
    /// (Caner, 2026-09-08: "rather than standing perfectly still"), which is also how a way out
    /// the first start tile could not see gets found, because each new start gets its own plan.
    /// </summary>
    public const int StrandedAfterTicks = 180;

    /// <summary>Stranded: how long each walk of the pocket lasts before the follow gets a window to try the player again, and how long that window is.</summary>
    public const int RoamTicks = 300;
    public const int RoamRetryTicks = 30;

    /// <summary>
    /// Stranded: what wander scores while roaming. Under every combat action's ceiling even
    /// after the commitment bonus a running roam gets, so an urgent threat in the pocket still
    /// takes the body (the Codex review of 2303802: at 0.9 an incumbent roam scored 1.035 and
    /// nothing but survive could win), and above the stranded follow with the same bonus.
    /// </summary>
    public const float StrandedWander = 0.6f;

    /// <summary>Stranded: what walk-with's score is multiplied by while roaming, so the follow yields the body it cannot use.</summary>
    public const float StrandedFollowDiscount = 0.3f;

    /// <summary>Positioner: how long a roam spot is kept before another is picked, in ticks.</summary>
    public const int RoamHoldTicks = 180;

    /// <summary>
    /// Positioner: how much the spot already being walked to is favoured over an equal one, so a
    /// rescore that finds two spots worth the same keeps the one the body is already on its way
    /// to. The chooser has had exactly this at the action layer since it was written — the running
    /// action keeps a small bonus so near-equal scores do not flicker — and the spot layer had
    /// nothing, so the goal tile changed every seventeen ticks on average while the request behind
    /// it never changed at all. A bonus rather than a hold, because a genuinely better spot must
    /// still win at once: a threat arriving is exactly when the companion has to move.
    /// </summary>
    public const float IncumbentSpotBonus = 1.15f;

    /// <summary>Positioner: how near a candidate must be to the held spot to count as the same place, in px.</summary>
    public const float IncumbentSlackPx = 24f;

    /// <summary>Loot: value of the nearest pickup fades with distance over this many px.</summary>
    public const float LootReach = 900f;
    public const int LootTripTicksPerPx = 1; // approximates 1 px per tick allowing for jumps

    /// <summary>Hunt: how far beyond the screen a target is still worth chasing.</summary>
    public const float HuntReach = 1100f;

    /// <summary>Kite: a walker inside this many px is "on top of me".</summary>
    public const float KiteTrigger = 64f;

    /// <summary>Reflex: a threat whose predicted hitbox meets the companion inside this many ticks triggers a dodge.</summary>
    public const int DodgeLookaheadTicks = 20;

    /// <summary>
    /// Positioner: how many feet tiles the flood from the companion's feet may visit when it asks
    /// which candidate spots are reachable. Spent once per rescore, not per candidate.
    /// </summary>
    public const int ReachFloodBudget = 400;

    /// <summary>
    /// Positioner: how long a spot stays refused after the navigator was stuck twice on the way
    /// to it, in ticks. Long enough that the companion goes somewhere else and does something
    /// there, short enough that a spot blocked by an enemy that has since moved comes back.
    /// </summary>
    public const int StuckSpotBanTicks = 600;

    /// <summary>
    /// Positioner: how much a candidate keeps in the cheap ranking pass when the straight line from
    /// its eye to the target is blocked. It ranks, it never vetoes: a straight ray is a lower bound
    /// on an arcing projectile, which clears a lip the ray hits, so a blocked candidate must still
    /// be able to reach the shortlist and pay for a real trajectory solve. It therefore sits above
    /// the 0.15 a *solved* failure scores and below the 1 a clear ray scores.
    /// </summary>
    public const float BlockedSightRank = .35f;

    /// <summary>
    /// Hunt: what a target is worth when no weapon can hit it from here but a reachable standing
    /// position has a line to it, so the hunt is the walk to that position. Below 1 because a fight
    /// that has to be walked to is worth less than one already in hand, which is also what lets
    /// ordinary work outscore a hunt that would first have to cross the room.
    /// </summary>
    public const float HuntRepositionShot = .7f;

    /// <summary>
    /// Hunt: what a target is worth while nothing has established whether a firing position exists —
    /// sighted standing spots near it, and a reachable region that has not finished expanding. It is
    /// deliberately not a veto: refusing a target because the flood is young would refuse every
    /// enemy at the moment it is first noticed, which is when hunting it is most useful.
    /// </summary>
    public const float HuntUnprovenShot = .45f;

    /// <summary>
    /// Mining: what an ore job is worth while its approach search has declined to answer and the
    /// companion is walking closer to make it answerable. Well under a proven job, so ore it can
    /// actually reach always wins, and under a hunt it can already shoot, so walking at a maybe
    /// never outranks doing something certain.
    /// </summary>
    public const float MineUnprovenApproach = .55f;
}
