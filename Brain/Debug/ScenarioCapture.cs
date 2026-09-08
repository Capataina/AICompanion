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
/// a follow failure (walking with the player and far behind for seconds), a stuck run
/// (the body has not moved for seconds while it had a path), a hit taken just after a
/// reflex approved a dodge (the dodge did not clear what it was simulated against), and a
/// missed mode (the player has been mining or chopping for seconds and the matching action
/// scored zero throughout, so the icon never showed). Thresholds live here and not in
/// Weights because they tune the instrument, not the brain.
/// </summary>
public static class ScenarioCapture
{
    private const int FollowGapTiles = 20;
    private const int FollowGapTicks = 300;
    private const int StuckTicksToReport = 120;
    private const int DodgeMemoryTicks = 60;
    private const int ModeMissedTicks = 300;
    private const int CooldownTicks = 600;

    private static int followBehind, sinceDodge = int.MaxValue, mineWanted, mineZero, chopWanted, chopZero, lastLife = -1;
    private static string? lastDodge;
    private static long followCooldown, stuckCooldown, dodgeCooldown, modeCooldown;

    public static void Watch(CompanionNPC companion)
    {
        Brain brain = companion.Brain;
        NPC npc = companion.NPC;
        var player = brain.Senses.Player;
        long tick = Main.GameUpdateCount;
        Point feet = NavGrid.FeetTile(npc.Bottom);
        Point playerFeet = NavGrid.FeetTile(player.Bottom);
        Point goal = brain.Navigator.GoalTile ?? playerFeet;

        // Follow failure: the chooser says walk with the player and the body is far behind.
        bool walking = brain.LastAction?.Name == "walk-with";
        followBehind = walking && Vector2.Distance(npc.Bottom, player.Bottom) > FollowGapTiles * 16f ? followBehind + 1 : 0;
        if (followBehind >= FollowGapTicks && tick >= followCooldown)
        {
            followCooldown = tick + CooldownTicks;
            followBehind = 0;
            BrainTelemetry.DumpScenario(feet, playerFeet, $"follow failure, more than {FollowGapTiles} tiles behind for {FollowGapTicks} ticks");
        }

        // Stuck: the follower had a path and the body did not move.
        if (brain.Navigator.Path != null && brain.Navigator.StuckTicks >= StuckTicksToReport && tick >= stuckCooldown)
        {
            stuckCooldown = tick + CooldownTicks;
            BrainTelemetry.DumpScenario(feet, goal, $"stuck, body still for {brain.Navigator.StuckTicks} ticks with a path");
        }

        // A hit just after a reflex approved a dodge: the simulation said it would clear.
        if (brain.Reflexes.Active is string dodge)
        {
            sinceDodge = 0;
            lastDodge = dodge;
        }
        else if (sinceDodge < int.MaxValue)
            sinceDodge++;
        if (lastLife >= 0 && npc.life < lastLife && sinceDodge <= DodgeMemoryTicks && tick >= dodgeCooldown)
        {
            dodgeCooldown = tick + CooldownTicks;
            BrainTelemetry.DumpScenario(feet, goal, $"hit through a dodge, {lastDodge} {sinceDodge} ticks ago");
        }
        lastLife = npc.life;

        // Missed mode: the player has been working for seconds and the matching action never scored.
        mineWanted = player.MinedOre != null ? mineWanted + 1 : 0;
        mineZero = player.MinedOre != null && RawScore(brain, "mine") <= 0f ? mineZero + 1 : 0;
        chopWanted = player.IsChoppingTree ? chopWanted + 1 : 0;
        chopZero = player.IsChoppingTree && RawScore(brain, "chop") <= 0f ? chopZero + 1 : 0;
        if (tick >= modeCooldown && (mineZero >= ModeMissedTicks || chopZero >= ModeMissedTicks))
        {
            modeCooldown = tick + CooldownTicks;
            string mode = mineZero >= ModeMissedTicks ? "mine" : "chop";
            BrainTelemetry.DumpScenario(feet, playerFeet, $"mode missed, player {mode} for {ModeMissedTicks} ticks and the {mode} action scored 0 throughout");
            mineZero = chopZero = 0;
        }
    }

    private static float RawScore(Brain brain, string action)
    {
        foreach (var s in brain.Chooser.LastScores)
            if (s.Action.Name == action)
                return s.Raw;
        return 0f;
    }
}
