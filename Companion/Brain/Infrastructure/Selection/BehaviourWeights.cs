#nullable enable

namespace AICompanion.Companion.Brain.Infrastructure.Selection;

/// <summary>
/// Every tunable number of the brain in one place, so a playtest note ("it hugs too
/// close", "it gives up on loot too soon") is one edit here and nowhere else.
/// </summary>
public static class Weights
{
    // Wall-clock budgets are live-brain policy. Headless core tests leave them disabled so
    // machine load cannot change a fixture's reachability verdict.
    public const double RouteSearchMilliseconds = 8d;
    public const double PositionReachMilliseconds = 2d;
    public const double PositionAimingMilliseconds = 2d;
    // The orb's pace is the player's, read live, times these: the cap is three times his maximum run
    // speed after accessories (the owner's ruling, so a speed accessory carries over and a
    // companion at the cap overtakes a running player; raised from two times on 15 September 2026
    // after the second orb play, where the gap to the player opened while the companion finished work
    // and the flight back took too long). The velocity law in Movement/Steering/
    // OrbPace has two accelerations, each a multiple of his run acceleration. The turn authority
    // bends the velocity: at three times the run acceleration a body at the cap turned with a
    // radius of nine tiles and sailed past a corridor's last bend into the wall (Tools/NavReplay
    // --self-test, corridor middle); at six the radius is half that and the bend slowdown brings
    // it inside the two-tile gaps the body is meant to fit. The speed change eases the body into
    // and out of a move, and is two times the run acceleration so the body takes a little over half
    // a second from rest to the cap, where the single shared acceleration it replaced took a fifth
    // and read as a snap (the owner's ruling of 15 September 2026: slower acceleration, smoother
    // slowdown, visible momentum, unchanged top speed). The fallbacks are for a player whose
    // numbers are not finite, which a fixture can produce, and mirror a plain player's run
    // acceleration of 0.08 times each multiple.
    public const float OrbSpeedPerRunSpeed = 3f;
    public const float OrbTurnPerRunAcceleration = 6f;
    public const float OrbSpeedChangePerRunAcceleration = 2f;
    public const float OrbFallbackSpeed = 9f;
    public const float OrbFallbackTurn = 0.48f;
    public const float OrbFallbackSpeedChange = 0.16f;
    // Arriving glides: the speed asked for near a goal is what lets the body slow at this share of
    // its easing rate, so the motor is never asked for its whole easing rate and the slowdown reads
    // as a glide rather than a stop at the last moment.
    public const float OrbArrivalEasingShare = 0.6f;
    // The drift around a held or reached spot (Movement/Steering/HoverAroundSpot). The radius is
    // about a tile, the owner's default; the flattening keeps the drift wider than it is tall so
    // it reads as floating beside a place rather than bobbing on a spring. The hover speed is a
    // drift's speed, a little over a pixel a tick, so a body held at a tool stand or a firing spot
    // floats about it rather than darting. The turn-rate floor keeps the target moving, so the body is
    // never exactly still: at the floor and the radius the target moves about half a pixel a tick
    // even at the flat of the ellipse, which clears the session reader's still threshold with room
    // for the body's lag behind it. The jitter is the wander's random walk per tick, and the reverse
    // chance flips the direction of circling now and then.
    public const float HoverRadiusPixels = 16f;
    public const float HoverVerticalShare = 0.6f;
    public const float HoverSpeedPx = 1.2f;
    public const float HoverGain = 0.08f;
    public const float HoverTurnRateMinimum = 0.05f;
    public const float HoverTurnRateMaximum = 0.08f;
    public const float HoverTurnJitter = 0.004f;
    public const double HoverReverseChance = 0.004;

    // Moving about the player's region while keeping him company (Movement/Steering/HoverAroundSpot.Across). The target
    // walks the region at about half a walking player's pace, so an idle companion crosses the box in a few seconds and a
    // travelling one's pursuit is the region's own motion plus this; the vertical part is flattened by HoverVerticalShare
    // for the reason the hover's is. The turn rate is far slower than the hover's and has no floor, for the reason at
    // TurnRateMaximum below: circling on the spot is not crossing the box. The gain pulls
    // the body onto the target on top of the target's own motion. While the region leads by more than LeadPixels, the
    // target may only go RearShare of the way from the centre toward the region's rear, which is what keeps a travelling
    // player's companion level or ahead rather than trailing as a policy. A region whose centre moved further than the
    // body could fly in JumpTicks has jumped, and the walk starts again from where the body is.
    public const float AccompanyWanderSpeedPx = 1.5f;
    // Signed and floorless, so the heading can run straight: at this cap and the wander speed the tightest loop is about five
    // hundred pixels across, wider than the box, which is what makes the walk cross it and reflect rather than circle.
    public const float AccompanyTurnRateMaximum = 0.006f;
    public const float AccompanyTurnJitter = 0.001f;
    public const float AccompanyGain = 0.1f;
    public const float AccompanyLeadPixels = 8f;
    public const float AccompanyRearShare = 0.2f;
    public const float AccompanyJumpTicks = 30f;
    // The route search prices an edge at its length times one plus this over the clearance at its
    // far corner, in tiles, so a corridor's middle is cheaper than its walls without a wall ever
    // being refused: at one, a corner touching a wall costs twice its length and one three tiles
    // clear a third more. Zero is the shortest path and the wall-hugging the owner refused.
    public const float CorridorMiddlePreference = 1f;
    // A corner inside a threat's inflated body costs this many times more, so a route goes around
    // an enemy where a way around exists and through it only where none does.
    public const float ThreatBodyRoutePenalty = 6f;
    // How many corners the reach flood may close per resolve. Corner expansions are cheap — eight
    // swept tests each — and a screen-sized window is a few thousand corners, so this closes one
    // in a handful of resolves rather than the walker's several seconds.
    public const int ReachFloodExpansions = 1500;
    // How many corners, and how many milliseconds, the intent sense's way-to-the-player flood may spend in one brain tick. It runs
    // inside the senses, ahead of the positioner and the navigator on the same shared deadline, so it has its own slice and resumes
    // on later ticks; its flood is unpriced and bounded by the player's grown region, a few thousand corners in open air.
    public const int PlayerSideFloodExpansions = 1500;
    public const double PlayerSideFloodMilliseconds = 1d;
    /// <summary>
    /// How far from the reach flood's root, in straight-line tiles, the sense gives a verdict at all. Inside
    /// it a tile the finished flood never claimed is proven unreachable; beyond it the answer is not yet,
    /// never unreachable, because the flood was not asked to go that far. The flood itself is a disc of twice
    /// this distance around its root, so "unreachable within the radius" means every route to the tile is a
    /// detour of about three times its straight line, which the reunion charge would refuse anyway. It sits above every
    /// distance a consumer asks about from the body: the hunt reach and an on-screen enemy beyond it, the
    /// work radius around the intent region, the loot reach. It is measured from the flood's root and not
    /// from the body: a replacement flood is grown once the body is half this distance from the root and
    /// advanced every brain tick so the root trails the body by little, but a body that outruns it reads
    /// not yet near itself until the replacement has caught up, never a false absence.
    /// </summary>
    public const int ReachKnownRadiusTiles = 72;
    // How many corners a route search may close per tick; a search that runs out keeps its
    // frontier and continues next tick while the body follows what it already had.
    public const int RouteSearchExpansions = 2500;
    // How far from a goal, in tiles, the navigator looks for a free corner to plan to when none is within two. A follow
    // anchor is a point the player's heading projects, and on 15 September 2026 it sat 256 px inside the rock under the
    // floor he was walking on; the planner found no corner within two tiles and the orb hovered in open air. Sixteen
    // covers that anchor's depth; a goal further into rock than this is still not planned to.
    public const int RouteGoalCornerTiles = 16;
    // How far a goal may drift before its route is thrown away and planned afresh: a following
    // anchor moves every tick, and a route re-aimed at a nearby goal is the same route.
    public const float ReplanGoalPixels = 24f;
    // The steering aims at a point ahead of the body's projection on its route, as far as the body
    // travels in this many ticks at its current speed, within a floor and a ceiling. Longer cuts
    // corners more and settles faster on straights; shorter tracks a winding route tighter. Scaled
    // by speed so a fast body leans into a bend early and a slow one hugs a winding route, where
    // the fixed distance it replaced looked seven ticks ahead at the cap and a whole route ahead
    // when hovering.
    public const float OrbLookaheadTicks = 8f;
    public const float OrbLookaheadMinimumPixels = 24f;
    public const float OrbLookaheadMaximumPixels = 64f;
    // Into a bend the speed cap falls by this share of the turn's fraction of a half-turn, never
    // below the minimum share of the cap, so a hairpin is taken slowly and a gentle curve barely
    // slows the body at all.
    public const float OrbBendSlowdown = 0.85f;
    public const float OrbBendMinimumShare = 0.25f;
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
    // Courtesy: evidence that the player is placing a block or wall where the companion stands, or walking a one-body-tall
    // passage it stands in, is held this long after it was last seen, so it outlasts a placement swing and the positioner's
    // rescore cadence. A follow spot a body would overlap that footprint in keeps this share of its score: moving aside wins
    // among useful spots, and the only usable spot is never made unusable. The passage is read this many tiles ahead.
    public const int CourtesyEvidenceTicks = 60;
    public const float CourtesyOccupancyShare = .25f;
    public const int CourtesyPassageTiles = 8;
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
    /// <summary>How quickly the published meeting place moves toward its target, as a fraction of the remaining gap per tick. Small enough that a brief reverse does not teleport the ahead-box.</summary>
    public const float MeetingAnchorLerp = .08f;
    /// <summary>While travelling, prefer a meeting place at least this far ahead so the ahead-box sits off the player's toes.</summary>
    public const float MeetingMinLeadTicks = 20f;
    public const int MeetingRerootTicks = 30;
    public const double MeetingSearchMilliseconds = 1d;
    // Lighting reads only light the engine computed. One tile of open air is dark below this brightness,
    // and that single threshold is what both carrying and placing light mean by the word, so the companion
    // cannot hold a torch where it would not place one or the reverse. The player's smart cursor does not
    // read brightness at all — only whether another torch already sits within eight tiles — and 0.22 left
    // dim cave air the cursor still offered (0.24 to 0.47 on the 16 September play) counting as lit. 0.5 is
    // the playtest bar after 0.4 felt right but still a little shy: a fully lit cave sits around here, unlit
    // caverns still under 0.1, and the eight-tile spacing plus the sky veto still refuse a room already
    // torched and the surface at night.
    public const float LightDarkBelow = .5f;
    // The smallest the light field's window may be, in tiles either side of the companion, whatever the
    // screen says. These are the half-extents the lighting search used around the body before the field
    // existed, kept because a screen-derived window is not always real: with no screen at all the width is
    // zero and the window becomes the one column the companion stands in, which reads as a world where no
    // light has been measured anywhere and offers no lighting work in any fixture.
    public const int LightWindowMinimumHalfWidthTiles = 48;
    public const int LightWindowMinimumHalfHeightTiles = 28;
    // How far around a body the torch decision looks. A torch lights roughly ten tiles, so a radius near
    // that asks "is the space this torch would light actually dark" rather than "is the region dark", which
    // is the question a screen-wide read answered and answered wrongly in a lit chamber inside a dark cave.
    public const int TorchHoldRadiusTiles = 12;
    // The share of measured open air that must read dark before the torch goes up, and the lower share it
    // must fall back to before it comes down. A share beat a mean brightness, which is what this replaced:
    // a mean over a window holding one lit chamber and three dark wings sits in the middle and is wrong
    // about every part of it. The gap between the two is the hysteresis, and it is wide because the
    // alternative — one threshold with only the minimum hold to damp it — flickers at a doorway.
    public const float TorchRaiseDarkShare = .40f;
    public const float TorchLowerDarkShare = .15f;
    // How far ahead of the player the torch also looks, in ticks of his observed travel. It is the
    // minimum hold in ticks, so the torch is raised about as far ahead as it is committed to staying
    // raised: looking only one hold's worth ahead beat looking further, which lit a torch for a dark
    // place the player turned away from before either of them reached it.
    public const int TorchHeadingLeadTicks = 180;
    // How far from the companion's own feet a dark region may sit and still be nominated, in tiles. It is
    // the work radius in tiles, so lighting reaches exactly as far as every other nearby job and no
    // further; a larger number was rejected because it lets the companion walk out of the player's area
    // chasing darkness, which is the behaviour recovery flight exists to undo.
    public const int LightRegionSearchTiles = (int)(FollowWorkRadius / 16f);
    // How long a nearby-work search waits before asking again when it could not answer, as opposed to when
    // it answered that there is nothing. It is a rescore or two, which is what the reach region needs to
    // settle after a world change; longer and the body has wandered somewhere else before the evidence it
    // was waiting for arrives, so the site it then proves is a different and worse one.
    public const int NearbyWorkUnresolvedRetryTicks = 15;
    // What one dark sample in the nominated region is worth, and the ceiling that stops a cavern from
    // outbidding everything. A count rather than a flat value because a torch in the larger dark space is
    // worth more, and the ceiling because without it a big enough cave beats protecting the player.
    public const float LightRegionSampleValue = .04f;
    public const float LightRegionValueCap = .20f;
    // What lighting is worth before the region's size is added, and the multiplier when the player is
    // carrying no light of his own. Placing a torch matters more when he cannot see either; it beat
    // treating his held torch as a reason not to light at all, which left permanent darkness behind him
    // everywhere he had walked holding one.
    //
    // Base plus the cap is deliberately the flat value lighting carried before it read a region at all, so
    // with the player lit the biggest cavern is worth exactly what one proven site used to be and lighting
    // keeps losing to mining and chopping. The first version let base and cap sum to .80 and then took the
    // unlit factor on top, which reached 1.12: above the band every ordinary raw value lives in, so a dark
    // cave with ore in it would have been lit rather than mined, and lighting could have interrupted a
    // committed job that an expression capped at 1 cannot touch. Only the unlit factor rises past the old
    // value, which is the one case the player is actually worse off without the torch.
    public const float LightBaseValue = .40f;
    public const float LightPlayerUnlitFactor = 1.4f;
    // How far a new job may sit from the player. Kept independent of fly-home so raising recovery
    // does not silently enlarge every work allowance past the worlds the fixtures fit in. A started job keeps
    // this times ActivityContinuationFactor. Lowered on 15 September 2026 (the owner's ruling after the second orb
    // play: the companion was doing too much too far from the player), with the continuation factor unchanged.
    public const float FollowWorkRadius = 1000f;
    // Recovery is a following fallback, not a traversal available to route search or mastery.
    // 100 tiles: room to hunt and work nearby without the far-follow flight cutting it short, and lowered from 120
    // with the work radius so that the companion stays closer.
    public const float FollowRecoveryDistance = 1600f;
    public const float FollowRecoveryArrival = 80f;
    public const float FollowRecoverySpeed = 12f;
    public const float FollowRecoveryAcceleration = 0.45f;
    public const float ActivityContinuationFactor = 1.25f;
    public const float WorkSiteRadius = 160f;
    public const int WorkCollectionTicks = 600;
    public const float InterruptibleActionTicks = 12f;
    public const float FollowDuringUsefulWork = .2f;

    // What "soon" means when jobs are compared by time. A job's worth is multiplied by window / (window + the ticks
    // until it is done, travel included), so a job a whole window away is worth half of the same job at hand, a
    // nearly finished job is worth nearly all of its value, and a long job is never zero. It is a half-life, not a
    // cutoff. Five seconds because the owner's examples are about "the best thing to do in the next couple of
    // seconds"; a longer window blurs the job on the way with the job across the room, a shorter one makes every job
    // but the nearest nearly worthless.
    public const float TaskWindowTicks = 300f;
    // Which jobs are close enough in worth to be put in an order together: every job scoring at least this share
    // below the best job is left out, because no order makes a clearly worse job the right first step.
    public const float TaskOrderShare = .4f;
    // The most jobs ordered together. 5! orders is 120 short sums on a rescore, and the board holds one job per
    // activity, so five is every job activity the companion has.
    public const int TaskOrderMaximum = 5;
    // The time to use a nearby site once there — a torch placed, a pot broken — which the shared executor adds to its
    // trip. Small on purpose: it only has to stop an interaction at the body's own feet reading as free.
    public const float NearbyInteractionTicks = 20f;
    // The kill time charged when no weapon in the slots can say how fast it hurts: three seconds, a middling fight,
    // so an unarmed or unreadable hunt is neither free nor ruled out by the time term alone.
    public const float TaskUnknownWorkTicks = 180f;
    // How far ahead the player's observed travel is projected when asking whether a job will still be near him when
    // it is done. Capped because a travel estimate stretched over a long job invents a destination he never chose.
    public const float PlayerProjectionCapTicks = 300f;
    public const int ObjectiveProgressWindowTicks = 180;
    public const float ObjectiveProgressPixels = 32f;
    /// <summary>Bonus multiplier the running action keeps, so scores do not flicker.</summary>
    public const float Commitment = 1.15f;

    /// <summary>
    /// Guard must be able to interrupt an ordinary action at its maximum committed value.
    /// A ceiling of one cannot displace that incumbent, regardless of observed player danger.
    /// </summary>
    public const float GuardUrgency = 1.25f;   // > 1.00 × Commitment

    /// <summary>
    /// The one combat activity offers the larger of the old guarding and hunting values under their
    /// existing terms, times this. The stance must be eager enough that a damageable hostile in reach
    /// takes the body off keeping company, without lifting a distant, unthreatening enemy above the
    /// work beside the player. Measured on the combat-activity rows with the screen centred on the
    /// player as the live game centres it: shared-eagerness is red at 0.4 (combat 0.076, under the
    /// 0.1 usefulness that discounts company) and green at 0.6 (combat 0.131), and mining still holds
    /// the vein against it at 4.0, so one sits with nearly twice the floor beneath it and more than
    /// four times the ceiling above on those rows. Guarding's own interruption ladder and hunting's
    /// product keep their meanings; this is the stance's, not theirs.
    /// </summary>
    public const float CombatValueScale = 1.0f;

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
    public const float FollowHorizontalComfort = 240f;
    public const float FollowVerticalComfort = 96f;

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
    public const float LeashHard = FollowRecoveryDistance;

    /// <summary>
    /// How far the companion may stray before hunting starts losing value, and how much further
    /// takes it to nothing. Hunting is the opportunistic behaviour — something to do when there is
    /// little else on — so it is the one that yields to staying with the player, and the free
    /// distance is about a screen's half-width because the standing goal is that he can see it.
    /// </summary>
    public const float HuntLeashFree = 700f;
    public const float HuntLeashToZero = 900f;

    public const float WanderFloor = 0.05f;
    /// <summary>
    /// The most rejoining the player can be worth short of the hard leash, and the most a job's separation can take from it.
    /// The owner ruled 0.5 after the third orb play of 15 September 2026, where rejoining at the full value any job can have
    /// out-bid a slime hunt beside an idle player: keeping company is the fallback for when nothing is worth doing, so on its
    /// own it never outweighs a job genuinely worth doing, and only the hard leash at fly-home distance, a separate step,
    /// takes everything.
    /// </summary>
    public const float KeepCompanyFarCap = .5f;
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

    /// <summary>Loot: value of the nearest pickup fades with distance over this many px.</summary>
    public const float LootReach = 900f;
    /// <summary>Prior value for unknown pot contents; actual drops are reconsidered independently.</summary>
    public const float PotContentsValue = .62f;
    /// <summary>Provisional handling allowance for exposing and inspecting unknown contents, excluding approach.</summary>
    public const float PotContentsHandlingTicks = 30f;

    /// <summary>Hunt: how far beyond the screen a target is still worth chasing.</summary>
    public const float HuntReach = 1100f;

    /// <summary>How far ahead a predicted hit is looked for: the reflex names an imminent collision inside this many ticks, and
    /// the evade step runs every candidate heading forward this far through the body's own law and contact.</summary>
    public const int DodgeLookaheadTicks = 20;

    /// <summary>How many evenly spaced headings the evade step scores besides the job's own heading and a stop. Sixteen is
    /// fine enough that the chosen heading is never more than eleven degrees from the ideal one, which the motor's easing then
    /// smooths into a curve, and each heading is a twenty-tick simulation, so the count is the evade's whole cost.</summary>
    public const int EvadeHeadings = 16;

    /// <summary>How much more danger than the least dangerous heading a heading may carry and still be chosen for agreeing with
    /// the job, as a share of the horizon. Zero would pick by danger alone and break every tie by angle; a little slack lets the
    /// heading that keeps doing the job win among ones that are clear for nearly as long.</summary>
    public const float EvadeDangerTolerance = 0.1f;

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

    /// <summary>A hunt whose remaining trip is this short is local work, not an outing: it does not pay the reunion charge a long chase does.</summary>
    public const float HuntLocalTripTicks = 120f;

    // ---- P09: gathering cooperation and truthful completion ----


    // P08 — purposeful combat and combined safety. Proposal 1's P08 tunables sit together at the end of
    // the class so parallel lanes adding their own blocks collide on nothing but position.

    /// <summary>
    /// Guard: the longest a protective fight can take and still be worth guarding over, in ticks. A
    /// threat the weapons could remove sooner keeps its full protection value; one needing longer keeps
    /// value in proportion — half at twice this — because standing between the player and something that
    /// outlasts the companion's weapons for that long protects nobody. Thirty seconds is a judgement about
    /// what a player would still call helping, not a measurement: on starting weapons a zombie or a swarm
    /// of small eyes sits well inside it and a boss's life bar far outside it, and that separation is
    /// what the number has to preserve when weapons or enemies change.
    /// </summary>
    public const float GuardUsefulRemovalTicks = 1800f;

    /// <summary>
    /// Encounter: ticks of unbroken pressure — reachable hostiles weighing more than the game's own spawn cap
    /// allows — before an unrecognised encounter reaches full intensity. There is no count threshold beside
    /// it: the cap is the game's, so it moves with depth, events and every spawn-rate change on its own.
    /// Intensity ramps linearly over this window so a brief overshoot does not stop all optional work, and
    /// drops to nothing the first tick the weight is back at or under the cap, because the return to
    /// ordinary is meant to be immediate.
    /// </summary>
    public const float EncounterPressureTicks = 300f;

    // ---- P10: useful assistance without endless detours ----

    /// <summary>
    /// Lighting and pots: how long a site whose trip was proven to have no way back, or not enough breath, stays out of
    /// discovery while the terrain is unchanged. Any terrain change ends the wait, because a new staircase or a drained pool
    /// is exactly what makes the same site worth asking again.
    /// </summary>
    public const int NearbyWorkNoReturnRetryTicks = 600;


    /// <summary>Incidental interactions: how often the grant boundary scans the tiles in reach for a permitted pot or dark torch site. Each
    /// scan runs every tile in reach through the methods' candidate rules, lighting's among them a light measurement and the native torch
    /// selector, so it is spaced out; a body walking past a pot at walking speed stays in reach for many times this interval.</summary>
    public const int IncidentalScanTicks = 15;

    // ---- P07: movement failures and retained route searches ----

    /// <summary>
    /// Navigation: how long a body with no route to walk may stand still waiting for the answer to its current goal before
    /// standing still counts as a stall again. The wait is summed across every search restarted for that goal, and it
    /// restarts only when the goal moves, the body arrives or a route is being walked. Exempting a body from the stall only
    /// while one search instance is running let terrain churn anywhere in the world restart the search for ever, so the
    /// two-strike spot ban and regroup's travel pressure never came. The authority for the value is the slowest legitimate
    /// answer any fixture measures (a search starved to one work unit per tick answering a sealed corridor); it must stay
    /// above that, or a slow answer is struck as a stall and its retained frontier is thrown away.
    /// </summary>
    public const int RouteAnswerWaitTicks = 300;

    // ---- Lane C: the player's intent region ----

    /// <summary>
    /// How far ahead in time the region is carried. The lead is a duration rather than a distance
    /// on purpose: multiplied by the player's own observed pace it lengthens for free when he puts
    /// on Hermes boots and shortens when he picks his way across a cave, where a fixed pixel lead
    /// would be a guess tuned to one walking speed. Two seconds is the property being approximated
    /// — about how far ahead a person walking beside someone looks — and it is what decides how far
    /// in front of a travelling player the companion tries to be.
    /// </summary>
    public const int IntentRegionLeadTicks = 120;

    /// <summary>
    /// The time constant of the one-pole filter on the lead, so the region glides rather than snaps.
    /// The hold is what starts a move — a reverse at the clamp has to begin on the first opposite
    /// key, not after a second of leftover walk-history — and this is only how long the glide takes.
    /// Twenty-four ticks still read as a snap on a one-block drop: the pose was already the floor,
    /// and a third of a second of falling took the box most of the way there. Ninety-six is a
    /// quarter of that rate, about two thirds of the way in a second and a half, so a tap eases
    /// and a held cave-drop still fills the clamp.
    /// </summary>
    public const int IntentRegionFilterTicks = 96;

    /// <summary>
    /// How much larger the region is than the follow comfort it is built from, with no lead. 1 is the follow comfort itself.
    /// A quarter larger was the 15 September 2026 ruling — the region had become a place to live in rather than arrive at —
    /// and the 16 September play found that box too big, so the scale went back to one.
    /// </summary>
    public const float IntentRegionBaseScale = 1f;

    /// <summary>
    /// The most the region grows with its own lead, as a share, reached exactly when the lead is at the clamp that keeps the
    /// player inside. A leading region is also a larger one — a player crossing broken ground is somewhere in a band rather
    /// than at a point — and growing with the lead's share of its own limit, rather than with a fixed distance, is what makes
    /// "fully grown" and "led as far as it may" the same moment. Fifteen percent is the original cap; twenty-five rode in with
    /// the 1.25 scale and left with it.
    /// </summary>
    public const float IntentRegionGrowthCap = .15f;

    /// <summary>
    /// Pace, in pixels per tick, given to a held direction that is not producing displacement: holding right into a wall,
    /// or down into the floor, still feeds the region's lead filter. Ordinary walking is about this fast, so the filter
    /// has a real target to chase and the clamp still binds; a smaller number would leave the box barely sliding.
    /// </summary>
    public const float IntentRegionHeldPace = 3f;

    /// <summary>
    /// How much of the player's own vertical displacement the region takes as lead, on top of a held
    /// up or down. One fifth is the 16 September play: walking off a single block moved the box all
    /// the way to the floor because any downward velocity saturated the pose; at this gain a tile of
    /// his is a fifth of a tile of the box, and a long fall still fills the clamp because the
    /// displacement adds up. A held key is a separate input and is not replaced by this.
    /// </summary>
    public const float IntentRegionVerticalTravelGain = .2f;

    /// <summary>
    /// How long the body must be at rest inside the region before following reads as satisfied, and
    /// how long a new keep-company regime must hold before the method changes. Leaving is immediate
    /// both times: this is a floor on entering a state, never a delay on leaving one. The authority
    /// is <c>ChooseUsefulPosition.RescoreInterval</c>, which is one rescore of the positioner; if the
    /// two drift the hold is no longer one rescore and the method can change inside a single scoring
    /// pass, which is the flicker it exists to stop.
    /// </summary>
    public const int PositionRescoreTicks = 12;

    /// <summary>
    /// The speed, in pixels per tick, under which the orb counts as at rest for the settled streak.
    /// A body that flies has no ground to stand on, so "has stopped" is the only arrival a streak can
    /// count, and a body crossing the region at pace must never read as arrived. It sits above the
    /// motor's braking residue: the steering brakes to the arrival radius and the motor then decays
    /// the last of the velocity at the acceleration per tick, so a body that has arrived is under
    /// this within a few ticks and a body tracking a walking player is never under it.
    /// </summary>
    public const float SettledSpeedPx = 1.5f;

    /// <summary>
    /// A positioning rule and never a wall: no scored candidate sits higher than this many tiles above
    /// the player's feet. The orb can fly anywhere the flood reaches, so without this the openest
    /// spot in a cavern is its roof and the companion hovers out of the player's sight. Following is
    /// already bound tighter by the intent region's own vertical half-size; this binds the attack
    /// requests, whose band is measured to the player and says nothing about height.
    /// </summary>
    public const int HoverCeilingTiles = 10;

    /// <summary>
    /// The clearance, in tiles, at which a candidate's openness factor reaches its full value. The
    /// factor is the clearance field the route search prices — the same reading, so a spot the scorer
    /// likes is one the route can reach the middle of — and it saturates here because a body two
    /// tiles from every wall is as open as it needs to be, and preferring the exact middle of every
    /// cavern would walk the companion further from the player for nothing.
    /// </summary>
    public const float OpennessFullClearanceTiles = 2f;

    /// <summary>Item physics, from the game's own <c>Item.UpdateItem</c>: gravity per tick and the fall
    /// speed it is capped at, dry and wet. A drop is forecast to its landing with these, so a falling
    /// item is priced where it will be rather than where it is; if the game changes them the forecast
    /// lands short and collection walks to the wrong tile.</summary>
    public const float DropGravity = .1f;
    public const float DropMaxFallSpeed = 7f;
    public const float DropWetGravity = .08f;
    public const float DropWetMaxFallSpeed = 5f;

    /// <summary>
    /// Honey's and shimmer's own figures, from the same ladder in <c>Item.UpdateItem</c> that supplies the water
    /// pair above. They are separate constants rather than water's reused because the game separates them, and an
    /// item in honey falls at half water's acceleration to a third of its cap.
    /// </summary>
    public const float DropHoneyGravity = .05f;
    public const float DropHoneyMaxFallSpeed = 3f;
    public const float DropShimmerGravity = .065f;
    public const float DropShimmerMaxFallSpeed = 4f;

    /// <summary>
    /// The share of its velocity a submerged item actually moves by each tick. <c>Item.UpdateItem</c> keeps a
    /// separate <c>wetVelocity</c> and steps the position by that instead of by the velocity whenever the item is
    /// wet, so a liquid slows an item twice over: once through the gravity and cap above, and again here. Omitting
    /// this is not a small error — it doubles the distance covered in water and quadruples it in honey.
    /// </summary>
    public const float DropWaterVelocityShare = .5f;
    public const float DropHoneyVelocityShare = .25f;
    public const float DropShimmerVelocityShare = .375f;
    /// <summary>Item horizontal damping per tick, and the speed below which the game zeroes it.</summary>
    public const float DropHorizontalDamping = .95f;
    public const float DropHorizontalFloor = .1f;
    /// <summary>How far ahead a drop's fall is forecast before the answer is given up as unknown. A drop
    /// still falling after this long is going somewhere the companion should not be committing to.</summary>
    public const int DropForecastTicks = 240;

    // Lane B — offer validity and destination retention.

    /// <summary>
    /// How many ticks apart the arrival-window samples of a firing stand's shot are taken. Two samples this far past the
    /// estimated arrival are asked, so the window a stand must hold for is bounded by twice this however long the trip is;
    /// the trip itself is the requirement below that bound. It is a sampling rate, not a promise about the ticks between:
    /// a target that leaves the arc and returns inside one interval reads as a shot that held.
    /// </summary>
    public const int ShotWindowSampleTicks = 20;

    /// <summary>
    /// The measured continuation confidence a forecast must carry before a stand's shot is judged at the arrival tick
    /// rather than at the target's current position. Below it the forecast is not evidence about where the thing will be,
    /// so refusing a stand on it would be refusing on a guess; the solve falls back to the current position and the reason
    /// says which of the two was asked.
    /// </summary>
    public const float ShotForecastConfidenceFloor = 0.35f;

    /// <summary>
    /// How far a target may drift from where it stood when a firing destination was admitted before the stand's arc is
    /// re-proved rather than retained. It is the firing position's success region: the kind declares no box precisely
    /// because its arc belongs to a moving target, so "still the region it was admitted against" can only mean "the
    /// target has not moved enough to have changed the answer".
    /// </summary>
    public const float FiringHoldTargetSlackPx = 48f;

    /// <summary>
    /// The longest window a firing stand's arc is required to hold for, past the estimated arrival. The requirement
    /// itself is the trip's own length — a shot has to survive the walk to the stand and no longer — and this only
    /// stops a walk across the world from asking for an arc that holds indefinitely, which is a question the
    /// forecast cannot answer anyway at that range. Raising it asks for more solves on long trips; lowering it stops
    /// distinguishing a middling trip from a long one.
    /// </summary>
    public const int ShotWindowCapTicks = 2 * ShotWindowSampleTicks;

    // Lane D — knockback, the danger a hit's push adds, and credit for wounding a dangerous enemy.

    /// <summary>
    /// How many ticks a hit's added horizontal speed is held when weighing where the push leaves an enemy. A walker keeps
    /// the speed a hit gave it until its own AI turns it round, which takes on the order of this many ticks, so the push is
    /// a speed held for a window rather than a flight. Integrating the airtime under gravity was the first shape considered
    /// and was rejected on paper before it was built: a zombie's pop from an ordinary bow is airborne for a couple of ticks,
    /// so every push would read as a pixel and the preference it drives would be blind to the pushes the owner watched
    /// carry enemies into him.
    /// </summary>
    public const int KnockbackSettleTicks = 12;

    /// <summary>
    /// How much a hit's value is charged per unit of danger its push adds, where danger is the added urgency to a body
    /// times what one of that enemy's hits takes off that body. It is on the scale of the prevented-harm credit on
    /// purpose, because the charge and that credit measure the same thing in opposite directions: harm to the player or
    /// the orb that the shot makes likelier rather than less likely. Before this there was no charge at all, and a shot
    /// that pushed a zombie into the player won on its damage alone, which is the behaviour the owner reported; no other
    /// value of this weight has been played.
    /// </summary>
    public const float KnockbackInducedDangerWeight = AttackPreventedHarmWeight;

    /// <summary>
    /// The share of prevented-harm credit a non-lethal hit earns, per unit of the enemy's remaining life it removes. Below
    /// one so the kill still stands out: two half-life hits earn this share on the first and the full kill credit on the
    /// second. Crediting harm only on a kill, which it replaced, valued wounding a zombie beside the player exactly like
    /// wounding a harmless slime.
    /// </summary>
    public const float AttackPartialHarmShare = .5f;

    // Lane D — weapon, target, stand and aim valued by what the companion's own shots achieved.

    /// <summary>
    /// The share of its score a firing stand keeps when the best attack any weapon in hand could make from there is worth
    /// nothing, rising to the whole score as that attack approaches the best the weapons could do with no geometry in the
    /// way. A floor rather than a veto, like every other factor: a stand whose only shot pushes the target into the player
    /// is still the answer when it is the only stand with a shot. It replaced a side preference read off the weapon chosen
    /// last, which priced a stand by one weapon's push and ignored the other weapon entirely.
    /// </summary>
    public const float FiringStandValueFloor = .5f;

    /// <summary>
    /// The prior variance of each learned coefficient, on the context's unit scale and the outcome ratio's scale. One means
    /// a coefficient is expected to move a forecast by up to about its whole value across an input's range — a straight
    /// arrow aimed at the widest offset losing its whole damage is exactly that size — and a weapon's first few outcomes
    /// move the posterior a long way, which is wanted because the learner starts every session empty.
    /// </summary>
    public const float WeaponLearningPriorVariance = 1f;

    /// <summary>
    /// The outcome noise the learner assumes, as a variance of the outcome ratio. A shot either lands or misses, so a ratio
    /// swings between about zero and about one from shot to shot even for a weapon whose average is well known; a quarter
    /// is that swing's variance for a weapon that lands half the time, which is the noisiest ordinary case.
    /// </summary>
    public const float WeaponLearningNoiseVariance = .25f;

    /// <summary>
    /// The prior variance of an enemy type's bias away from its weapon's average. Smaller than the coefficients' because a
    /// new enemy type should start at the weapon's average and move off it only with evidence of its own.
    /// </summary>
    public const float WeaponLearningEnemyTypeVariance = .25f;

    /// <summary>
    /// What one enemy struck is worth in an outcome's yield, as damage: a starter weapon's hit, so a second body struck
    /// counts on the scale of damage and a piercing or splitting weapon earns for its crowd without a count swamping what
    /// its hits actually took off.
    /// </summary>
    public const float ShotOutcomeStruckEnemyValue = 5f;

    /// <summary>
    /// The longest a shot's outcome window stays open, ticks: past the arsenal's own evaluation horizon, so a slow or
    /// lingering projectile is still counted for the damage the horizon credited it with, and bounded so a projectile that
    /// never dies cannot hold a window open for the session.
    /// </summary>
    public const int ShotOutcomeWindowTicks = 240;

    /// <summary>
    /// The threat urgency above which decisions stop exploring and use the posterior mean. Urgency is the threat sense's
    /// zero-to-one scale; half is a threat that will reach a body soon with a hit that matters, which is the point at which a
    /// sampled worse attack costs the player rather than a moment.
    /// </summary>
    public const float WeaponExploreDangerCeiling = .5f;

}
