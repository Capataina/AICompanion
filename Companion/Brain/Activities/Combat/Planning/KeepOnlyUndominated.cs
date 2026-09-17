#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

/// <summary>
/// The Pareto filter: a plan is dropped when another is at least as good on every objective within that
/// objective's tolerance and better on one. The tolerances are each objective's measurement noise, so two
/// plans that differ by rounding are not treated as different.
///
/// What the filter changes is the front, not the argmax: with positive weights a strictly dominated plan
/// cannot win a weighted sum with or without the filter. The front is what the recorder writes, what the
/// search audit replays against, and what the beam keeps its diversity in — a front that held dominated
/// plans would call a ragged search exhaustive.
/// </summary>
public static class KeepOnlyUndominated
{
    /// <summary>The survivors, in their original order.</summary>
    public static List<T> Filter<T>(IReadOnlyList<T> plans, Func<T, CombatOutcome> outcome)
    {
        var survivors = new List<T>(plans.Count);
        CombatOutcome tolerances = CombatOutcome.Tolerances;
        for (int i = 0; i < plans.Count; i++)
        {
            bool dominated = false;
            for (int j = 0; j < plans.Count; j++)
            {
                if (i != j && Dominates(outcome(plans[j]), outcome(plans[i]), tolerances))
                {
                    dominated = true;
                    break;
                }
            }
            if (!dominated)
                survivors.Add(plans[i]);
        }
        return survivors;
    }

    /// <summary>
    /// The survivors with the drops beside them: each drop's dominator and the objective it lost on — the
    /// beyond-tolerance win with the widest tolerance-normalised gap, which is where the two plans most differ.
    /// The record's rejected plans come from here, so a decision names what it turned down and where.
    /// </summary>
    public static (List<T> Survivors, List<(T Plan, T Dominator, int LostOn)> Drops) FilterWithDrops<T>(
        IReadOnlyList<T> plans, Func<T, CombatOutcome> outcome)
    {
        var survivors = new List<T>(plans.Count);
        var drops = new List<(T Plan, T Dominator, int LostOn)>();
        CombatOutcome tolerances = CombatOutcome.Tolerances;
        for (int i = 0; i < plans.Count; i++)
        {
            bool dominated = false;
            for (int j = 0; j < plans.Count; j++)
            {
                if (i != j && Dominates(outcome(plans[j]), outcome(plans[i]), tolerances))
                {
                    dominated = true;
                    drops.Add((plans[i], plans[j], LostObjective(outcome(plans[j]), outcome(plans[i]), tolerances)));
                    break;
                }
            }
            if (!dominated)
                survivors.Add(plans[i]);
        }
        return (survivors, drops);
    }

    private static int LostObjective(CombatOutcome dominator, CombatOutcome dropped, CombatOutcome tolerances)
    {
        int lost = 0;
        float widest = float.NegativeInfinity;
        for (int i = 0; i < CombatOutcome.Count; i++)
        {
            float tol = MathF.Max(float.Epsilon, tolerances[i]);
            float gap = CombatOutcome.HigherIsBetter(i)
                ? (dominator[i] - dropped[i]) / tol
                : (dropped[i] - dominator[i]) / tol;
            if (gap > widest)
            {
                widest = gap;
                lost = i;
            }
        }
        return lost;
    }

    /// <summary>Whether <paramref name="a"/> dominates <paramref name="b"/>: at least as good within tolerance everywhere, better beyond it somewhere.</summary>
    public static bool Dominates(CombatOutcome a, CombatOutcome b, CombatOutcome tolerances)
    {
        bool better = false;
        for (int i = 0; i < CombatOutcome.Count; i++)
        {
            float tol = tolerances[i];
            if (CombatOutcome.HigherIsBetter(i))
            {
                if (a[i] < b[i] - tol) return false;
                if (a[i] > b[i] + tol) better = true;
            }
            else
            {
                if (a[i] > b[i] + tol) return false;
                if (a[i] < b[i] - tol) better = true;
            }
        }
        return better;
    }
}
