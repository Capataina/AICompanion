#nullable enable

extern alias live;

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using live::AICompanion.Companion.Brain.Activities.Combat.Planning;
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
        float radius = restored.Snapshot.AllowanceRadius;
        Vector2 heading = RestoreSnapshot.Shift(restored, restored.Snapshot.Player.Region.Heading);
        Vector2 feet = ctx.Npc.Bottom;
        bool Allows(Vector2 point) => Vector2.DistanceSquared(point, heading) <= radius * radius
            && Vector2.DistanceSquared(feet, heading) <= radius * radius;
        PlanningBudget budget = Budget(restored.Snapshot.AllowanceMs);
        // No forced means: the sampler draws deterministically at the restored tick, so the replay draws what
        // the live search drew. Forcing means here would price the replay at the posterior mean against live
        // samples and diverge on every calm snapshot; the sweep keeps its own forcing, where noise-free
        // weight comparison is the point.
        SearchAttackPlans.SearchResult result = SearchAttackPlans.SearchDepthOne(ctx, combat, positioner, Allows, restored.Weights,
            restored.Snapshot.Plan?.Id ?? combat.NextPlanId++,
            ref budget, new SearchAttackPlans.SearchOptions(restored.Proposals, restored.Verdicts));
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
    /// front. Depth stays the live depth: the plus-one level arrives with phase E's deeper search.
    /// </summary>
    public static ExhaustiveVerdict Exhaustive(RestoredDecision restored, bool liveProposalsOnly = false)
    {
        var combat = restored.Companion.Combat;
        var positioner = restored.Companion.Brain.Positioner;
        var ctx = restored.Ctx;
        float radius = restored.Snapshot.AllowanceRadius;
        Vector2 heading = RestoreSnapshot.Shift(restored, restored.Snapshot.Player.Region.Heading);
        Vector2 feet = ctx.Npc.Bottom;
        bool Allows(Vector2 point) => Vector2.DistanceSquared(point, heading) <= radius * radius
            && Vector2.DistanceSquared(feet, heading) <= radius * radius;
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
        PlanningBudget budget = PlanningBudget.Unbounded();
        AttackLearning.ForceMeans = true;
        SearchAttackPlans.SearchResult result;
        try
        {
            result = SearchAttackPlans.SearchDepthOne(ctx, combat, positioner, Allows, restored.Weights,
                combat.NextPlanId++, ref budget, new SearchAttackPlans.SearchOptions(grid));
        }
        finally
        {
            AttackLearning.ForceMeans = false;
        }
        if (restored.CommittedShifted == null || result.Plan == null)
            return new ExhaustiveVerdict(result.Plan != null, 0f, "none", result.Plan?.Segments[0].Stand.Stand,
                result.Plan?.Weighted ?? 0f, capped, grid.Count, result.FrontSize);
        CombatOutcome? repriced = ReevaluateAttackPlan.Reevaluate(ctx, combat, restored.Enemies,
            restored.CommittedShifted, restored.Weights);
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

    private static PlanningBudget Budget(float allowanceMs)
        => !float.IsFinite(allowanceMs) || allowanceMs >= float.MaxValue
            ? PlanningBudget.Unbounded()
            : PlanningBudget.FromMilliseconds(allowanceMs);

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
