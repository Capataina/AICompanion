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
    // Each purpose family's share of that total for preparing its optional children. Three
    // families at a third each would leave nothing for position and navigation on a worst tick, so
    // the share is smaller. It bounds optional siblings only: the incumbent, non-excursion children
    // and one optional child always prepare, and the incumbent keeps the whole total because its
    // approach searches retain no progress. The 2026-09-13 brain-cost scene measured an incumbent
    // mining approach recomputation consuming the full 12 ms; that cost was the approach query
    // asking every pose, and asking nearest first brought the same scene's maximum to about 4 ms.
    public const double FamilyPreparationMilliseconds = 3d;
    public const float ProtectionLeadTicks = 60f;
    public const float GuardReleasePressure = 0.08f;
    public const int GuardClearTicks = 90;
    // History spans local back-and-forth motion; evidence support delays confidence in
    // a new journey without discarding its vertical component.
    public const int PlayerIntentHistoryTicks = 120;
    public const int PlayerIntentEvidenceTicks = 24;
    public const float PlayerIntentCorrectionSlack = 32f;
    public const float PlayerIntentWorkDiscount = .75f;
    public const float PlayerIntentTravelSpeed = 1.2f;
    // Meeting places: candidates sit on the player's line of travel at this ladder of horizons,
    // snapped to a standable tile within a window smaller than a floor plus a body, so a candidate
    // never jumps to the floor below. A late arrival pays the chase at the difference in pace, capped
    // where the player is at least as fast as the companion. A new place must beat the held one by
    // the switch margin, so equal-cost points on one floor do not replan the route every tick.
    public const float MeetingHorizonStepTicks = 30f;
    public const int MeetingHorizonSteps = 8;
    public const int MeetingSnapTiles = 3;
    public const float MeetingChaseCeiling = 20f;
    public const float MeetingUncertaintyCost = 1f;
    public const float MeetingSwitchMargin = .15f;
    public const int MeetingRerootTicks = 30;
    public const double MeetingSearchMilliseconds = 1d;
    // Until a finished flood prices a place, reunion aims at the player's travel continued this far: the
    // bounded continuation it used before meeting places existed, clamped by the intent history.
    public const int MeetingFallbackLeadTicks = 45;
    // Lighting reads only light the engine computed. An area is a lighting opportunity when enough of its
    // samples are measured and their mean is below the dark level, which deliberately equals the held
    // torch's raise level (TorchBearer.RaiseBelow) so carrying and placing light agree about what dark
    // means; if the two drift, the companion holds a torch where it will not place one, or the reverse.
    // Carried light is left out within its radius, which matches the ambient reading's exclusion disc.
    // A site's own neighbourhood vetoes it only on enough measured reads, so a sparse read cannot.
    public const int LightAreaRadiusTiles = 18;
    public const int LightAreaStrideTiles = 3;
    public const float LightMeasuredFractionRequired = .6f;
    public const float LightDarkBelow = .22f;
    public const int LightSiteRadiusTiles = 5;
    public const int LightSiteMinimumSamples = 4;
    public const float CarriedLightRadiusTiles = 10f;
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
    /// Guard must be able to interrupt an ordinary action at its maximum committed value.
    /// A ceiling of one cannot displace that incumbent, regardless of observed player danger.
    /// Shared safety can suspend either activity independently of this utility comparison.
    /// </summary>
    public const float GuardUrgency = 1.25f;   // > 1.00 × Commitment
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
    public const float ReunionDelayToleranceTicks = 30f;
    public const float ReunionAbsenceScaleTicks = 1800f;
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
    /// <summary>Prior value for unknown pot contents; actual drops are reconsidered independently.</summary>
    public const float PotContentsValue = .62f;
    /// <summary>Provisional handling allowance for exposing and inspecting unknown contents, excluding approach.</summary>
    public const float PotContentsHandlingTicks = 30f;

    /// <summary>Hunt: how far beyond the screen a target is still worth chasing.</summary>
    public const float HuntReach = 1100f;

    /// <summary>Residual geometric enemy exposure accepted at a stable retreat landing.</summary>
    public const float CombatSpaceExposure = .1f;
    /// <summary>Converts geometric exposure into distance-like body-search guidance.</summary>
    public const float CombatSpaceHeuristicPixels = 320f;
    /// <summary>A completed unsuccessful spacing search yields ordinary work before retrying.</summary>
    public const uint CombatSpaceRetryTicks = 30;

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

    // ---- P09: gathering cooperation and truthful completion ----

    /// <summary>
    /// Mining: how long ceiling ore stays out of discovery after its take-off, with the body at rest on
    /// it, stopped proving a jump while the terrain has not changed. Any terrain change ends the wait at
    /// once, because a changed world is the condition under which the same take-off can be worth asking
    /// again; without a wait the next preparation re-proves the same take-off from rest and re-offers it.
    /// </summary>
    public const int HopTakeOffRetryTicks = 600;

    /// <summary>
    /// Collection: how many nearby drops one preparation may put to the walker search, nearest first. Each question is a
    /// fresh bounded search from the companion's feet plus, when it says yes, the return questions, so a floor strewn with
    /// unreachable drops costs at most this many per preparation and the rest wait for a later one.
    /// </summary>
    public const int CollectionReachCandidates = 3;

    /// <summary>
    /// Collection: how long a drop's reach and return verdicts are reused while its contact pose and the terrain revision are
    /// unchanged. A companion walking toward a drop would otherwise ask the same searches on every tile it crosses; a terrain
    /// edit or a drop that moved to another pose asks again at once.
    /// </summary>
    public const int CollectionReachRecheckTicks = 60;

    // ---- P10: useful assistance without endless detours ----

    /// <summary>
    /// Lighting and pots: how many remote working poses one discovery search may put to the round-trip query, nearest first.
    /// Each question is two fresh route searches, outward and back, so a window full of sites with no way back costs at most
    /// this many per search and the rest wait for the next one.
    /// </summary>
    public const int NearbyWorkTripChecks = 3;

    /// <summary>
    /// Lighting and pots: how long a site whose trip was proven to have no way back, or not enough breath, stays out of
    /// discovery while the terrain is unchanged. Any terrain change ends the wait, because a new staircase or a drained pool
    /// is exactly what makes the same site worth asking again.
    /// </summary>
    public const int NearbyWorkNoReturnRetryTicks = 600;

    /// <summary>Keeping company: the nearest a stroll goal may be to the feet, in tiles, so a stroll is a walk rather than a shuffle on the spot.</summary>
    public const int StrollMinimumTiles = 3;

    /// <summary>Keeping company: how many rows above or below the player's feet a stroll goal may sit, so strolls stay on the floor
    /// the player is on or a step away from it rather than wandering to another level.</summary>
    public const int StrollRowsFromPlayer = 4;

    /// <summary>Keeping company: how many random columns of the player's neighbourhood one pick examines for a safe standing tile.
    /// Each column reads a handful of rows, so this bounds the cost of a pick; a pick that finds nothing rests instead.</summary>
    public const int StrollColumnSamples = 12;

    /// <summary>Keeping company: the most predicted enemy exposure a stroll goal may carry, on PredictedExposureAt's scale, where a
    /// forecast hit is 1 and mere proximity peaks at .6. A stroll exists to be company, never to stand where something is about to arrive.</summary>
    public const float StrollExposureLimit = .3f;
}
