#nullable enable

extern alias live;

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using live::AICompanion.Companion.Brain.Activities.Combat.Planning;
using live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;

namespace AICompanion.Tools.CombatAudit;

/// <summary>
/// The weight sweep: each objective halved and doubled in turn, the decision replayed at the scaled
/// weights, and whatever about the winner moved reported — stand, weapons, targets. Run against the
/// snapshot's own proposals and verdicts, so a move is the weights' doing and nothing else's. The
/// sweep is how an owner sees what a weight is for: the row that plants a scene where doubling damage
/// moves the fight is A3, and a sweep that reported moves at factor 1.0 would fail it by construction.
/// </summary>
internal static class AuditWeights
{
    public sealed record SweepMove(int Objective, string Name, float Factor, bool Changed, Vector2 Stand,
        string Weapons, string Targets, float Weighted);

    public static List<SweepMove> Sweep(RestoredDecision restored, float[]? factors = null)
    {
        factors ??= new[] { 0.5f, 1.0f, 2.0f };
        var moves = new List<SweepMove>();
        foreach (float factor in factors)
            for (int objective = 0; objective < CombatOutcome.Count; objective++)
                moves.Add(RunAt(restored, objective, factor));
        return moves;
    }

    private static SweepMove RunAt(RestoredDecision restored, int objective, float factor)
    {
        var combat = restored.Companion.Combat;
        var positioner = restored.Companion.Brain.Positioner;
        var ctx = restored.Ctx;
        float radius = restored.Snapshot.AllowanceRadius;
        Vector2 heading = RestoreSnapshot.Shift(restored, restored.Snapshot.Player.Region.Heading);
        Vector2 feet = ctx.Npc.Bottom;
        bool Allows(Vector2 point) => Vector2.DistanceSquared(point, heading) <= radius * radius
            && Vector2.DistanceSquared(feet, heading) <= radius * radius;
        CombatWeights weights = factor == 1f ? restored.Weights : restored.Weights.Scaled(objective, factor);
        PlanningBudget budget = PlanningBudget.Unbounded();
        AttackLearning.ForceMeans = true;
        SearchAttackPlans.SearchResult result;
        try
        {
            result = SearchAttackPlans.SearchDepthOne(ctx, combat, positioner, Allows, weights,
                combat.NextPlanId++, ref budget,
                new SearchAttackPlans.SearchOptions(restored.Proposals, restored.Verdicts));
        }
        finally
        {
            AttackLearning.ForceMeans = false;
        }
        if (result.Plan == null)
            return new SweepMove(objective, CombatOutcome.Name(objective), factor, true, Vector2.Zero,
                "none", "none", 0f);
        AttackSegment segment = result.Plan.Segments[0];
        var weapons = new SortedSet<int>();
        var targets = new SortedSet<int>();
        foreach (PlannedUse use in segment.Uses)
        {
            weapons.Add(use.WeaponSlot);
            targets.Add(use.TargetSlot);
        }
        bool changed = false;
        if (restored.CommittedShifted != null)
        {
            AttackSegment committed = restored.CommittedShifted.Segments[0];
            changed = segment.Stand.Stand != committed.Stand.Stand || weapons.Count != WeaponsOf(committed).Count
                || !weapons.SetEquals(WeaponsOf(committed)) || !targets.SetEquals(TargetsOf(committed));
        }
        return new SweepMove(objective, CombatOutcome.Name(objective), factor, changed, segment.Stand.Stand,
            string.Join("+", weapons), string.Join("+", targets), result.Plan.Weighted);
    }

    private static SortedSet<int> WeaponsOf(AttackSegment segment)
    {
        var weapons = new SortedSet<int>();
        foreach (PlannedUse use in segment.Uses)
            weapons.Add(use.WeaponSlot);
        return weapons;
    }

    private static SortedSet<int> TargetsOf(AttackSegment segment)
    {
        var targets = new SortedSet<int>();
        foreach (PlannedUse use in segment.Uses)
            targets.Add(use.TargetSlot);
        return targets;
    }
}
