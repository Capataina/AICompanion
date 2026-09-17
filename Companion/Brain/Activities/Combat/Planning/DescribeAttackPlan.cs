#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

/// <summary>
/// The combat-plan record's detail, built from the plan and the search that chose it: every segment's
/// stand, reason, weapons, targets and ticks, the objective vector, the weighted value, the front size, the
/// three best rejected plans with dominated-or-weights and the objective each lost on, the budget cut, and
/// the invalidation reason when the plan ended. Semicolons between fields and commas inside them, the way
/// every other god's-eye record reads, so the report parses it with the same split.
/// </summary>
public static class DescribeAttackPlan
{
    public static string Detail(AttackPlan plan, IReadOnlyList<RejectedPlan> rejected, int frontSize, string invalidation)
    {
        var detail = new StringBuilder();
        detail.Append(FormattableString.Invariant($"segments={plan.Segments.Length};"));
        for (int s = 0; s < plan.Segments.Length; s++)
        {
            AttackSegment segment = plan.Segments[s];
            var weapons = new List<int>(segment.Uses.Length);
            var targets = new List<int>(segment.Uses.Length);
            foreach (PlannedUse use in segment.Uses)
            {
                if (!weapons.Contains(use.WeaponSlot))
                    weapons.Add(use.WeaponSlot);
                if (!targets.Contains(use.TargetSlot))
                    targets.Add(use.TargetSlot);
            }
            detail.Append(FormattableString.Invariant($"seg{s}=stand={segment.Stand.Stand.X:0.0},{segment.Stand.Stand.Y:0.0}:reason={segment.Stand.Reason}:weapons={string.Join("+", weapons)}:targets={string.Join("+", targets)}:arrive={segment.ArriveTick}:start={segment.StartTick}:end={segment.EndTick}:ends={segment.EndsWhen}:uses={segment.Uses.Length};"));
        }
        CombatOutcome outcome = plan.Outcome;
        detail.Append(FormattableString.Invariant($"outcome={outcome.DamagePerSecond:0.000},{outcome.ThreatRemoved:0.000},{outcome.PlayerHarmPrevented:0.000},{outcome.CompanionHarmTaken:0.000},{outcome.PushDangerAdded:0.000},{outcome.CompanyGap:0.000},{outcome.TimeToFirstDamage:0.000},{outcome.ManaSpent:0.000};"));
        detail.Append(FormattableString.Invariant($"weighted={plan.Weighted:0.000};front={frontSize};cut={(plan.BudgetCut ? 1 : 0)};invalid={invalidation};"));
        for (int r = 0; r < rejected.Count; r++)
        {
            AttackPlan turnedDown = rejected[r].Plan;
            Vector2 stand = turnedDown.Segments.Length > 0 ? turnedDown.Segments[0].Stand.Stand : Vector2.Zero;
            detail.Append(FormattableString.Invariant($"rejected{r}={rejected[r].Reason}:{rejected[r].LostOn}:stand={stand.X:0.0},{stand.Y:0.0}:weighted={turnedDown.Weighted:0.000};"));
        }
        if (plan.TargetKillTicks is { Length: > 0 } kills)
        {
            var parts = new List<string>(kills.Length);
            foreach ((int slot, int tick) in kills)
                parts.Add(FormattableString.Invariant($"{slot}@{tick}"));
            detail.Append($"kills={string.Join("+", parts)};");
        }
        detail.Append(FormattableString.Invariant($"targets={Targets(plan)}"));
        return detail.ToString();
    }

    private static string Targets(AttackPlan plan)
    {
        var parts = new List<string>(plan.Validity.Targets.Length);
        foreach ((int slot, int generation) in plan.Validity.Targets)
            parts.Add(FormattableString.Invariant($"{slot}g{generation}"));
        return string.Join("+", parts);
    }
}
