#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Brain.DecisionMatrix.Navigation;
using AICompanion.Companion;

namespace AICompanion.Brain.Debug;

/// <summary>
/// Turns the failures a playtest shows into scenario blocks the replay tool can run, so
/// every "it did not follow me there" becomes a deterministic offline test instead of a
/// memory. Seven detectors, each with its own threshold and cooldown, each writing the same
/// tile window the failed-plan dump writes (with the player's trail) under its own reason:
/// a follow failure (walking with the player, far behind, and no nearer than five seconds
/// ago, so a companion catching up is never one), a traversal fault (a step the follower
/// could not complete, named by its traversal and its outcome), a stuck run (the body has
/// not moved for two seconds while the follower had a path, counted here so a replan cannot
/// reset it), a hit taken just after a reflex approved a dodge (the dodge did not clear what
/// it was simulated against; lava and fire are excluded because the reflex never promised
/// those), a missed mode (the player has been mining or chopping for five seconds of
/// activity, pauses of up to a second allowed, and the matching action scored zero
/// throughout, so the icon never showed), and two for the reachability tier, which is the
/// one decision no offline pass can watch because the replay runs the planner and the
/// follower while the positioner needs the game: a one-way place entered (a spot held with
/// no way back, which is legitimate behind the player and is captured because it is the
/// state a companion gets stuck from), and the invariant behind it (the tier holding the
/// body somewhere returnable while the player can only be reached one-way, which should
/// never happen and is what an unconditional refusal did on 2026-09-08).
/// Every duration is in game ticks. Thresholds live here and not in
/// Weights because they tune the instrument, not the brain; Reset runs on every session.
/// </summary>
public static class ScenarioCapture
{
    private const int FollowGapTiles = 20;
    private const int FollowGapTicks = 300;
    private const float FollowProgressPx = 32f;
    private const int StuckTicksToReport = 120;
    private const int DodgeMemoryTicks = 60;
    private const int ModeMissedTicks = 300;
    private const int ModePauseTicks = 60;
    private const int CooldownTicks = 600;

    private const int OneWayHeldTicks = 90;

    /// <summary>How long one goal is held with the body moving and the distance flat before it is reported.</summary>
    private const int IntentTicks = 240;

    /// <summary>How much closer the body must get over that window for it to count as making progress, in px.</summary>
    private const float IntentProgressPx = 32f;

    /// <summary>How far the body must actually travel over the window for "moving but not arriving" to be the finding rather than "stuck".</summary>
    private const float IntentTravelPx = 200f;

    /// <summary>The goal may drift this far and still be the same goal; the navigator's own slack.</summary>
    private const int IntentGoalSlackTiles = 2;

    /// <summary>How long the body may be held with a velocity and no displacement before the window is written.</summary>
    private const int PinnedTicksToReport = 45;

    private static int followBehind, stuck, mineZero, mineIdle, chopZero, chopIdle, lastLife = -1;
    private static int oneWayCommitted, tierHeldOut;
    private static float followDistanceAtStart;
    private static long lastDodgeTick = long.MinValue;
    private static string? lastDodge;
    private static Vector2 lastPosition;
    private static long followCooldown, stuckCooldown, dodgeCooldown, modeCooldown, faultCooldown, oneWayCooldown, tierCooldown, intentCooldown, pinnedCooldown;

    private static Point? intentGoal;
    private static int intentTicks;
    private static float intentBest, intentTravelled;

    /// <summary>Forget everything: a new session, a new world, a new companion.</summary>
    public static void Reset()
    {
        followBehind = stuck = mineZero = mineIdle = chopZero = chopIdle = 0;
        oneWayCommitted = tierHeldOut = 0;
        lastLife = -1;
        lastDodgeTick = long.MinValue;
        lastDodge = null;
        followCooldown = stuckCooldown = dodgeCooldown = modeCooldown = faultCooldown = oneWayCooldown = tierCooldown = intentCooldown = pinnedCooldown = 0;
        intentGoal = null;
        intentTicks = 0;
        intentBest = intentTravelled = 0f;
    }

    public static void Watch(CompanionNPC companion)
    {
        Brain brain = companion.Brain;
        NPC npc = companion.NPC;
        var player = brain.Senses.Player;
        long tick = Main.GameUpdateCount;
        Point feet = NavGrid.FeetTile(npc.Bottom);
        Point playerFeet = NavGrid.FeetTile(player.Bottom);
        Point goal = brain.Navigator.GoalTile ?? playerFeet;

        // Follow failure: walking with the player, far behind, and no nearer than when the
        // count began; a companion closing the gap at the player's own pace is doing its job.
        float distance = Vector2.Distance(npc.Bottom, player.Bottom);
        bool behind = brain.LastAction?.Name == "walk-with" && distance > FollowGapTiles * 16f;
        if (!behind)
            followBehind = 0;
        else if (followBehind++ == 0)
            followDistanceAtStart = distance;
        if (followBehind >= FollowGapTicks && tick >= followCooldown)
        {
            if (distance > followDistanceAtStart - FollowProgressPx)
            {
                followCooldown = tick + CooldownTicks;
                BrainTelemetry.DumpScenario(feet, playerFeet, $"follow failure, more than {FollowGapTiles} tiles behind for {FollowGapTicks} ticks and no nearer");
            }
            followBehind = 0;
        }

        // A step the follower could not complete, named by its traversal: which move, from where
        // to where, and why (stood past its allowance, landed elsewhere, pressed a shape, lost
        // inside a shape). This is the follow class's own detector; the stuck count below is
        // the backstop for a body that stands with no step faulting.
        if (brain.Navigator.LastFault != TraversalFault.None && tick >= faultCooldown && brain.Navigator.LastEdge is EdgeReport fault)
        {
            faultCooldown = tick + CooldownTicks;
            BrainTelemetry.DumpScenario(feet, goal, $"traversal fault, {fault.Outcome} on {fault.Kind} {fault.From.X},{fault.From.Y} -> {fault.Tile.X},{fault.Tile.Y} after {fault.Actual} ticks, proven {fault.Expected}");
        }

        // Wanting to go somewhere and never getting nearer, which is a different failure from
        // standing still and had no instrument at all. The stuck detector below needs the body to
        // stop; this one needs it to move — the case Caner watched on 2026-09-09 was a marked spot
        // right beside him with the companion going back and forth in front of it for seconds,
        // covering real ground and closing no distance. Neither the stuck count nor the follow gap
        // can see that: the body is not still, and it can happen at any distance from him.
        //
        // Three clauses, and each of them is doing work. One goal held, so a companion whose
        // request keeps changing is not reported for failing to reach any of them. Real travel, so
        // this stays distinct from stuck rather than firing alongside it. And the closest approach
        // never improving, which is the actual claim: distance to the goal is the only measure
        // that separates moving from getting there.
        float toGoal = brain.Positioner.Chosen is Vector2 want ? Vector2.Distance(npc.Bottom, want) : float.MaxValue;
        Point? held = brain.Navigator.GoalTile;
        bool sameGoal = held is Point g && intentGoal is Point had
            && Math.Abs(g.X - had.X) <= IntentGoalSlackTiles && Math.Abs(g.Y - had.Y) <= IntentGoalSlackTiles;
        if (!sameGoal || held == null || brain.Navigator.Path == null)
        {
            intentGoal = brain.Navigator.Path != null ? held : null;
            intentTicks = 0;
            intentBest = toGoal;
            intentTravelled = 0f;
        }
        else
        {
            intentGoal = held;
            intentTicks++;
            intentTravelled += Vector2.Distance(npc.position, lastPosition);
            if (toGoal < intentBest)
                intentBest = toGoal;
            if (intentTicks >= IntentTicks && tick >= intentCooldown)
            {
                if (intentTravelled >= IntentTravelPx && intentBest > toGoal - IntentProgressPx && intentBest < float.MaxValue)
                {
                    intentCooldown = tick + CooldownTicks;
                    BrainTelemetry.DumpScenario(feet, goal, $"intent without progress, one spot held {IntentTicks} ticks while the body travelled {intentTravelled:n0} px and never got within {intentBest:n0} px of it");
                }
                intentTicks = 0;
                intentTravelled = 0f;
            }
        }

        // A body held in place with a velocity it is not spending. That pair is impossible for a
        // body the engine is integrating, so it means something wrote the position back during the
        // AI phase — which is what the motor's own step-up did through the whole shaft freeze,
        // where the body read as airborne (`ground` is velocity.Y == 0) and so registered as
        // neither stuck nor grounded, and every escape route in the navigator was gated shut.
        if (companion.Motor.PinnedTicks >= PinnedTicksToReport && tick >= pinnedCooldown)
        {
            pinnedCooldown = tick + CooldownTicks;
            BrainTelemetry.DumpScenario(feet, goal, $"body pinned, {companion.Motor.PinnedTicks} ticks holding velocity {npc.velocity.X:0.00},{npc.velocity.Y:0.00} with no displacement");
        }

        // Stuck: a path to follow and a body that has not moved, counted here across replans.
        stuck = brain.Navigator.Path != null && Vector2.DistanceSquared(npc.position, lastPosition) < 1f ? stuck + 1 : 0;
        lastPosition = npc.position;
        if (stuck >= StuckTicksToReport && tick >= stuckCooldown)
        {
            stuckCooldown = tick + CooldownTicks;
            stuck = 0;
            BrainTelemetry.DumpScenario(feet, goal, $"stuck, body still for {StuckTicksToReport} ticks with a path");
        }

        // A hit just after a reflex approved a dodge: the simulation said it would clear. Lava
        // and fire are the self sense's business and were never part of the promise.
        if (brain.Reflexes.Active is string dodge)
        {
            lastDodgeTick = tick;
            lastDodge = dodge;
        }
        bool environmental = brain.Senses.Self.InLava || brain.Senses.Self.OnFire;
        // The dodge must have happened. Without that first clause the age is `tick - long.MinValue`,
        // which overflows a signed long and wraps to a large negative number, so every hit in a
        // session where no reflex had ever fired was reported as a hit through a dodge — with an age
        // of -9223372036854773868 ticks printed in the window header, which is how it was found.
        if (lastDodge != null && lastLife >= 0 && npc.life < lastLife && !environmental && tick - lastDodgeTick <= DodgeMemoryTicks && tick >= dodgeCooldown)
        {
            dodgeCooldown = tick + CooldownTicks;
            BrainTelemetry.DumpScenario(feet, goal, $"hit through a dodge, {lastDodge} {tick - lastDodgeTick} ticks ago");
        }
        lastLife = npc.life;

        // The reachability tier's own two events, which no offline pass can see: the replay tool
        // runs the planner and the follower, and the positioner needs the game, so the only view of
        // the decision about entering somewhere unrecoverable is from inside a session.
        //
        // First, the body committed to a spot it cannot come home from. That is not a fault by
        // itself: following the player into a pocket he chose to be in is the behaviour, and the
        // dump exists because it is the state a companion gets stuck from and the one with no
        // offline coverage at all. Held for a while first, so a spot re-scored away next tick is
        // not reported.
        bool oneWaySpot = brain.Positioner.Chosen != null && !brain.Positioner.ChosenReturnable;
        oneWayCommitted = oneWaySpot ? oneWayCommitted + 1 : 0;
        if (oneWayCommitted >= OneWayHeldTicks && tick >= oneWayCooldown)
        {
            oneWayCooldown = tick + CooldownTicks;
            oneWayCommitted = 0;
            BrainTelemetry.DumpScenario(feet, goal, $"entered a one-way place, spot held {OneWayHeldTicks} ticks with no way back, {brain.Positioner.ReturnableCount} of {brain.Positioner.ReachCount} tiles returnable");
        }

        // Second, the invariant behind that: while the player is somewhere the body can only reach
        // through an edge with no way back, the tier must be scoring the raw region, so the spot it
        // picks is one it cannot come home from. A returnable spot in that state means the tier
        // stayed closed on a rim tile and left the body above the player, which is what an
        // unconditional refusal did on 2026-09-08 and what the two-wide shaft fixture reproduces.
        // This should never fire; it is here so that if it ever does, the window is on disk.
        // Both actions the drop gate opens for, not only the follow: guarding him at the bottom of a
        // shaft is the same state and the same defect if the tier keeps the body on the lip.
        bool heldOut = brain.LastAction?.Name is "walk-with" or "guard" && brain.Positioner.PlayerOnlyOneWay && brain.Positioner.Chosen != null && brain.Positioner.ChosenReturnable;
        tierHeldOut = heldOut ? tierHeldOut + 1 : 0;
        if (tierHeldOut >= OneWayHeldTicks && tick >= tierCooldown)
        {
            tierCooldown = tick + CooldownTicks;
            tierHeldOut = 0;
            BrainTelemetry.DumpScenario(feet, playerFeet, $"tier held the body out, {OneWayHeldTicks} ticks with the player only reachable one-way while the spot it picked was returnable");
        }

        // Missed mode: the player has been working, with pauses no longer than a swing between
        // hits, and the matching action never scored. The ore-hit marker lives less than a second,
        // so the count survives a pause of one and resets only after a longer one.
        Count(player.MinedOre != null, RawScore(brain, "mine") <= 0f, ref mineZero, ref mineIdle);
        Count(player.IsChoppingTree, RawScore(brain, "chop") <= 0f, ref chopZero, ref chopIdle);
        if (tick >= modeCooldown && (mineZero >= ModeMissedTicks || chopZero >= ModeMissedTicks))
        {
            modeCooldown = tick + CooldownTicks;
            string mode = mineZero >= ModeMissedTicks ? "mine" : "chop";
            BrainTelemetry.DumpScenario(feet, playerFeet, $"mode missed, player {mode} for {ModeMissedTicks} ticks of activity and the {mode} action scored 0 throughout");
            mineZero = chopZero = 0;
        }
    }

    private static void Count(bool active, bool scoredZero, ref int zero, ref int idle)
    {
        if (active)
        {
            idle = 0;
            zero = scoredZero ? zero + 1 : 0;
            return;
        }
        if (++idle > ModePauseTicks)
            zero = 0;
    }

    private static float RawScore(Brain brain, string action)
    {
        foreach (var s in brain.Chooser.LastScores)
            if (s.Action.Name == action)
                return s.Raw;
        return 0f;
    }
}
