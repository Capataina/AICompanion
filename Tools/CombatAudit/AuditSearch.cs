#nullable enable

extern alias live;

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using live::AICompanion.Companion.Brain.Activities.Combat.Planning;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;

namespace AICompanion.Tools.CombatAudit;

/// <summary>
/// The search audit: replay the snapshot's decision with its live budget and proposals and compare
/// exactly, then price the committed plan against an exhaustive grid with a completed flood and report
/// whether it is on that front, the weighted regret, and which generator would have proposed the better
/// stand — or none. The replay draws what the live search drew: the sampler is seeded by the restored
/// tick, so even a calm live search replays exactly; only the weight sweep still forces means, where
/// noise-free comparison is the point.
/// </summary>
internal static class AuditSearch
{
    public sealed record ReplayVerdict(bool Reproduced, List<string> Diffs, int FrontSize, float Weighted,
        float PlayerDanger, float CompanionDanger);

    public sealed record ExhaustiveVerdict(bool CommittedOnFront, float Regret, string Generator,
        Vector2? BetterStand, float BetterWeighted, bool Capped, int GridStands, int FrontSize);

    /// <summary>The decision replayed: live proposals with live verdicts under the live allowance.</summary>
    public static ReplayVerdict Replay(RestoredDecision restored)
    {
        var combat = restored.Companion.Combat;
        var positioner = restored.Companion.Brain.Positioner;
        var ctx = restored.Ctx;
        Func<Vector2, bool> Allows = RestoreSnapshot.AllowanceQuery(restored);
        DecisionWorkBudget budget = Budget(restored.Snapshot.AllowanceMs, restored.Snapshot.MaxSimulations);
        // No forced means: the sampler draws deterministically at the restored tick, so the replay draws what
        // the live search drew. Forcing means here would price the replay at the posterior mean against live
        // samples and diverge on every calm snapshot; the sweep keeps its own forcing, where noise-free
        // weight comparison is the point.
        SearchAttackPlans.SearchResult result = SearchAttackPlans.Search(ctx, combat, positioner, Allows, restored.Weights,
            restored.Snapshot.Plan?.Id ?? combat.NextPlanId++,
            ref budget, new SearchAttackPlans.SearchOptions(restored.Proposals, restored.Verdicts, restored.Deeper),
            maxDepth: restored.Deeper.Count > 0 ? SearchAttackPlans.MaxSearchDepth : 1);
        var verdict = new ReplayVerdict(false, new List<string>(), result.FrontSize, result.Plan?.Weighted ?? 0f,
            ctx.Senses.Threats.PlayerDanger, ctx.Senses.Threats.CompanionDanger);
        if (restored.CommittedShifted == null)
        {
            verdict.Diffs.Add("snapshot carries no committed plan; replay offers " + result.Reason);
            return verdict;
        }
        if (result.Plan == null)
        {
            verdict.Diffs.Add("replay offers nothing: " + result.Reason);
            return verdict;
        }
        ComparePlans(restored.CommittedShifted, result.Plan, verdict.Diffs);
        if (result.FrontSize != restored.Snapshot.FrontSize)
            verdict.Diffs.Add($"front size {result.FrontSize} != snapshot {restored.Snapshot.FrontSize}");
        return new ReplayVerdict(verdict.Diffs.Count == 0, verdict.Diffs, verdict.FrontSize, verdict.Weighted,
            verdict.PlayerDanger, verdict.CompanionDanger);
    }

    /// <summary>
    /// The exhaustive front: every half-tile of the proposal region with a completed flood, priced at the
    /// snapshot's weights. The grid spirals out from the committed stand and stops at two thousand stands,
    /// which the verdict reports rather than hiding — past the cap the front is a lower bound, not the
    /// front. Depth is the live depth: the grid replaces the proposals, and the beam expands past them.
    /// </summary>
    public static ExhaustiveVerdict Exhaustive(RestoredDecision restored, bool liveProposalsOnly = false)
    {
        var combat = restored.Companion.Combat;
        var positioner = restored.Companion.Brain.Positioner;
        var ctx = restored.Ctx;
        Func<Vector2, bool> Allows = RestoreSnapshot.AllowanceQuery(restored);
        List<StandProposal> grid;
        bool capped;
        if (liveProposalsOnly)
        {
            grid = restored.Proposals;
            capped = false;
        }
        else
        {
            RestoreSnapshot.GrowFlood(restored);
            (grid, capped) = Grid(restored);
        }
        DecisionWorkBudget budget = Unbounded();
        AttackLearning.ForceMeans = true;
        // The grid grades against everything at the live depth; the live proposals alone replay the live
        // decision's own set, so they run at the recorded depth — a depth-one recording replays depth one.
        int depth = !liveProposalsOnly || restored.Deeper.Count > 0 ? SearchAttackPlans.MaxSearchDepth : 1;
        SearchAttackPlans.SearchResult result;
        try
        {
            result = SearchAttackPlans.Search(ctx, combat, positioner, Allows, restored.Weights,
                combat.NextPlanId++, ref budget, new SearchAttackPlans.SearchOptions(grid), maxDepth: depth);
        }
        finally
        {
            AttackLearning.ForceMeans = false;
        }
        if (restored.CommittedShifted == null || result.Plan == null)
            return new ExhaustiveVerdict(result.Plan != null, 0f, "none", result.Plan?.Segments[0].Stand.Stand,
                result.Plan?.Weighted ?? 0f, capped, grid.Count, result.FrontSize);
        // A fresh unbounded allowance rather than the one the grid search just spent: the committed
        // plan and the grid have to be priced under the same conditions for the regret to mean
        // anything, and a reprice charged to an exhausted budget would cut instantly and report the
        // committed plan as unpriceable rather than as worse.
        DecisionWorkBudget repriceBudget = Unbounded();
        CombatOutcome? repriced = ReevaluateAttackPlan.Reevaluate(ctx, combat, restored.Enemies,
            restored.CommittedShifted, restored.Weights, ref repriceBudget).Outcome;
        if (repriced == null)
            return new ExhaustiveVerdict(false, result.Plan.Weighted, "none", result.Plan.Segments[0].Stand.Stand,
                result.Plan.Weighted, capped, grid.Count, result.FrontSize);
        float committedWeighted = restored.Weights.Weighted(repriced.Value);
        var front = new List<CombatOutcome>(result.Front.Count + 1);
        foreach (AttackPlan plan in result.Front)
            front.Add(plan.Outcome);
        front.Add(repriced.Value);
        List<CombatOutcome> survivors = KeepOnlyUndominated.Filter(front, outcome => outcome);
        bool onFront = survivors.Contains(repriced.Value);
        float best = committedWeighted;
        foreach (AttackPlan plan in result.Front)
            best = Math.Max(best, plan.Weighted);
        float regret = Math.Max(0f, best - committedWeighted);
        string generator = "none";
        if (!onFront || regret > 0f)
            generator = AttributeGenerator(restored, result.Plan.Segments[0].Stand.Stand);
        return new ExhaustiveVerdict(onFront, regret, generator, result.Plan.Segments[0].Stand.Stand,
            result.Plan.Weighted, capped, grid.Count, result.FrontSize);
    }

    private static DecisionWorkBudget Budget(float allowanceMs, int maxSimulations)
        => !float.IsFinite(allowanceMs) || allowanceMs >= float.MaxValue
            ? Unbounded()
            : new DecisionWorkBudget(allowanceMs, maxSimulations);

    /// <summary>The audit's own allowance: no deadline and no operation cap, because grading the
    /// committed plan against an exhaustive grid is the point. This is never the live budget — a
    /// verdict drawn under it says what the search could have found, not what it had time to.</summary>
    private static DecisionWorkBudget Unbounded() => new(double.PositiveInfinity);

    private static void ComparePlans(AttackPlan expected, AttackPlan actual, List<string> diffs)
    {
        if (expected.Segments.Length != actual.Segments.Length)
        {
            diffs.Add($"segments {actual.Segments.Length} != {expected.Segments.Length}");
            return;
        }
        for (int s = 0; s < expected.Segments.Length; s++)
        {
            AttackSegment want = expected.Segments[s], got = actual.Segments[s];
            if (want.Stand.Stand != got.Stand.Stand)
                diffs.Add($"seg{s} stand {got.Stand.Stand} != {want.Stand.Stand}");
            if (want.Uses.Length != got.Uses.Length)
            {
                diffs.Add($"seg{s} uses {got.Uses.Length} != {want.Uses.Length}");
                continue;
            }
            for (int u = 0; u < want.Uses.Length; u++)
            {
                PlannedUse wu = want.Uses[u], gu = got.Uses[u];
                if (wu.WeaponSlot != gu.WeaponSlot || wu.TargetSlot != gu.TargetSlot)
                    diffs.Add($"seg{s} use{u} weapon/target {gu.WeaponSlot}/{gu.TargetSlot} != {wu.WeaponSlot}/{wu.TargetSlot}");
                if (wu.AimPoint != gu.AimPoint)
                    diffs.Add($"seg{s} use{u} aim {gu.AimPoint} != {wu.AimPoint}");
                if (wu.Muzzle != gu.Muzzle)
                    diffs.Add($"seg{s} use{u} muzzle {gu.Muzzle} != {wu.Muzzle}");
                if (wu.FireTick != gu.FireTick)
                    diffs.Add($"seg{s} use{u} fireTick {gu.FireTick} != {wu.FireTick}");
            }
        }
        for (int i = 0; i < CombatOutcome.Count; i++)
            if (expected.Outcome[i] != actual.Outcome[i])
                diffs.Add($"outcome {CombatOutcome.Name(i)} {actual.Outcome[i]} != {expected.Outcome[i]}");
        if (expected.Weighted != actual.Weighted)
            diffs.Add($"weighted {actual.Weighted} != {expected.Weighted}");
    }

    private static (List<StandProposal> Grid, bool Capped) Grid(RestoredDecision restored)
    {
        Vector2 root = restored.CommittedShifted?.Segments[0].Stand.Stand ?? restored.Ctx.Npc.Center;
        var cells = new List<(float Distance, Vector2 Stand)>();
        int width = restored.Snapshot.Terrain.Width, height = restored.Snapshot.Terrain.Height;
        float margin = AuditHost.Margin * 16f;
        for (int hx = 0; hx < width * 2; hx++)
            for (int hy = 0; hy < height * 2; hy++)
            {
                var stand = new Vector2(hx * 8f + 4f + margin, hy * 8f + 4f + margin);
                cells.Add((Vector2.DistanceSquared(stand, root), stand));
            }
        cells.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var targets = new List<int>();
        foreach (var threat in restored.Ctx.Senses.Threats.Threats)
            targets.Add(threat.Npc.whoAmI);
        var grid = new List<StandProposal>(Math.Min(2000, cells.Count));
        foreach ((_, Vector2 stand) in cells)
        {
            if (grid.Count >= 2000)
                break;
            grid.Add(new StandProposal(stand, StandReason.AuditGrid, -1, targets.ToArray()));
        }
        return (grid, cells.Count > grid.Count);
    }

    private static string AttributeGenerator(RestoredDecision restored, Vector2 stand)
    {
        foreach (StandProposal proposal in restored.Proposals)
            if (Vector2.DistanceSquared(proposal.Stand, stand) <= 64f)
                return proposal.Reason.ToString();
        return "none";
    }
}
