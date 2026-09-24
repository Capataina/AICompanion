#nullable enable

using System;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Computation;

/// <summary>
/// The millisecond allowance one brain tick installs, and the one seam a harness may move it through.
///
/// <para><b>Harness-only.</b> <see cref="OverrideMilliseconds"/> is null in the game and nothing in the mod
/// sets it; <see cref="Milliseconds"/> is then exactly <see cref="Weights.TotalPlanningMilliseconds"/>, so the
/// seam is inert by construction. It exists so the world run's budget curve can ask what a smaller allowance
/// costs in behaviour without editing the tunable, the same kind of offline knob as
/// <c>Brain.PlanningOperationAllowance</c> and <c>LimitPlanningWork.Unbounded</c> beside it.</para>
///
/// <para>Overriding this one value applies to the whole decision, and that is the property the curve rests on.
/// <c>Brain.Tick</c> is the only production owner of an allowance and reads it here; every nested slice — the
/// route search's 8 ms, the reach flood's 2 ms, the meeting and player-side floods' 1 ms — computes its own
/// deadline through <c>LimitPlanningWork.Deadline</c>, which takes the earlier of its own and the tick's, and
/// spends through the tick's <see cref="DecisionWorkBudget"/>. So a 2 ms override caps every slice at 2 ms
/// without any of them being edited. <c>AuditDecisionContracts.DecideCeilingMilliseconds</c> reads this too, so
/// a <c>decide-overran-allowance</c> record under an override is judged against the installed allowance plus the
/// route-search slice rather than against the production figure.</para>
/// </summary>
public static class TickAllowance
{
    private static double? overrideMilliseconds;

    /// <summary>
    /// Harness-only: the allowance every brain tick installs instead of the tunable, or null for the tunable.
    /// Never set in play. A value that is not a positive finite number of milliseconds is refused rather than
    /// clamped, because a curve point taken at a silently different allowance would be filed under the wrong
    /// figure.
    /// </summary>
    public static double? OverrideMilliseconds
    {
        get => overrideMilliseconds;
        set
        {
            if (value is { } ms && !(ms > 0 && double.IsFinite(ms)))
                throw new ArgumentOutOfRangeException(nameof(OverrideMilliseconds),
                    $"expected a positive finite number of milliseconds, got {ms}");
            overrideMilliseconds = value;
        }
    }

    /// <summary>What a brain tick allows its decision, in milliseconds of wall clock.</summary>
    public static double Milliseconds => overrideMilliseconds ?? Weights.TotalPlanningMilliseconds;
}
