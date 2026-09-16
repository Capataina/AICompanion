extern alias live;

using System;
using System.Collections.Generic;
using AICompanion.Tools.Ledger;
using CombatOutcome = live::AICompanion.Companion.Brain.Activities.Combat.Planning.CombatOutcome;
using KeepOnlyUndominated = live::AICompanion.Companion.Brain.Activities.Combat.Planning.KeepOnlyUndominated;
using WeighCombatObjectives = live::AICompanion.Companion.Brain.Activities.Combat.Planning.WeighCombatObjectives;
using CombatWeights = live::AICompanion.Companion.Brain.Activities.Combat.Planning.CombatWeights;

/// <summary>
/// Planning rows (P): knowledge and forecasts planted, so they test planning and not learning.
/// </summary>
internal static class VerifyAttackPlanning
{
    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    /// <summary>
    /// P12: a plan worse on every objective than another never survives, whatever the weights. Two halves:
    /// the filter drops the strictly dominated plan and keeps a tradeoff, and near-equals within tolerance
    /// both survive while a plan worse beyond tolerance on one objective falls; and across weight sweeps
    /// the weighted choice over the unfiltered set never lands on the dominated plan either, which is
    /// arithmetic rather than filtering — with positive weights a strictly dominated plan cannot win a sum.
    /// The filter's observable is the front: the recorder writes it and the audit replays against it, so a
    /// front that held dominated plans would call a ragged search exhaustive. Skipping the filter is the
    /// mutation this row kills.
    /// </summary>
    public static int ADominatedPlanNeverSurvivesTheFront()
    {
        var good = new CombatOutcome(0.5f, 0.5f, 0.5f, 0.1f, 0.1f, 0.1f, 0.2f, 0.1f);
        var dominated = new CombatOutcome(0.4f, 0.4f, 0.4f, 0.2f, 0.2f, 0.2f, 0.3f, 0.2f);
        var tradeoff = new CombatOutcome(0.4f, 0.5f, 0.5f, 0.1f, 0.1f, 0.1f, 0.05f, 0.1f);
        var plans = new (string Name, CombatOutcome Outcome)[] { ("good", good), ("dominated", dominated), ("tradeoff", tradeoff) };
        List<(string Name, CombatOutcome Outcome)> front =
            KeepOnlyUndominated.Filter(plans, p => p.Outcome);
        Require(front.Count == 2, $"the front holds the good plan and the tradeoff; got {front.Count}");
        Require(front[0].Name == "good" && front[1].Name == "tradeoff",
            "the front keeps its original order minus the dominated plan");

        CombatOutcome tolerances = CombatOutcome.Tolerances;
        var nearEqual = new CombatOutcome(
            good.DamagePerSecond - tolerances.DamagePerSecond / 2f,
            good.ThreatRemoved - tolerances.ThreatRemoved / 2f,
            good.PlayerHarmPrevented - tolerances.PlayerHarmPrevented / 2f,
            good.CompanionHarmTaken + tolerances.CompanionHarmTaken / 2f,
            good.PushDangerAdded + tolerances.PushDangerAdded / 2f,
            good.CompanyGap + tolerances.CompanyGap / 2f,
            good.TimeToFirstDamage + tolerances.TimeToFirstDamage / 2f,
            good.ManaSpent + tolerances.ManaSpent / 2f);
        Require(KeepOnlyUndominated.Filter(new[] { good, nearEqual }, o => o).Count == 2,
            "plans that differ by rounding are not different plans; both survive");
        var worseOne = good with { DamagePerSecond = good.DamagePerSecond - tolerances.DamagePerSecond * 2f };
        Require(KeepOnlyUndominated.Filter(new[] { good, worseOne }, o => o).Count == 1,
            "a plan worse beyond tolerance on one objective and no better anywhere falls");

        CombatWeights baseWeights = WeighCombatObjectives.For(0.5f, 0.2f, 0.1f, true, 0.8f, 0.9f);
        var sweeps = new List<CombatWeights> { baseWeights };
        for (int i = 0; i < CombatOutcome.Count; i++)
            sweeps.Add(baseWeights.Scaled(i, 2f));
        foreach (CombatWeights weights in sweeps)
        {
            float best = float.NegativeInfinity;
            string winner = "";
            foreach ((string name, CombatOutcome outcome) in plans)
            {
                float value = weights.Weighted(outcome);
                if (value > best) { best = value; winner = name; }
            }
            Require(winner != "dominated", "no weight sweep lands the weighted choice on the dominated plan");
        }
        Console.WriteLine($"attack planning: the front holds 2 of 3, near-equals survive, and {sweeps.Count} weight sweeps never choose the dominated plan");
        return 0;
    }
}
