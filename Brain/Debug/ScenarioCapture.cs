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
/// memory. Four detectors, each with its own threshold and cooldown, each writing the same
/// tile window the failed-plan dump writes (with the player's trail) under its own reason:
/// a follow failure (walking with the player, far behind, and no nearer than five seconds
/// ago, so a companion catching up is never one), a stuck run (the body has not moved for
/// two seconds while the follower had a path, counted here so a replan cannot reset it), a
/// hit taken just after a reflex approved a dodge (the dodge did not clear what it was
/// simulated against; lava and fire are excluded because the reflex never promised those),
/// and a missed mode (the player has been mining or chopping for five seconds of activity,
/// pauses of up to a second allowed, and the matching action scored zero throughout, so the
/// icon never showed). Every duration is in game ticks. Thresholds live here and not in
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

    private static int followBehind, stuck, mineZero, mineIdle, chopZero, chopIdle, lastLife = -1;
    private static float followDistanceAtStart;
    private static long lastDodgeTick = long.MinValue;
    private static string? lastDodge;
    private static Vector2 lastPosition;
    private static long followCooldown, stuckCooldown, dodgeCooldown, modeCooldown;

    /// <summary>Forget everything: a new session, a new world, a new companion.</summary>
    public static void Reset()
    {
        followBehind = stuck = mineZero = mineIdle = chopZero = chopIdle = 0;
        lastLife = -1;
        lastDodgeTick = long.MinValue;
        lastDodge = null;
        followCooldown = stuckCooldown = dodgeCooldown = modeCooldown = 0;
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
        if (lastLife >= 0 && npc.life < lastLife && !environmental && tick - lastDodgeTick <= DodgeMemoryTicks && tick >= dodgeCooldown)
        {
            dodgeCooldown = tick + CooldownTicks;
            BrainTelemetry.DumpScenario(feet, goal, $"hit through a dodge, {lastDodge} {tick - lastDodgeTick} ticks ago");
        }
        lastLife = npc.life;

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
