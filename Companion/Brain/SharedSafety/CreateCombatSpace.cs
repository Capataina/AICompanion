#nullable enable

using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;
using AICompanion.Companion.Brain.SharedMovementSystem;

namespace AICompanion.Companion.Brain.SharedSafety;

/// <summary>Seek lower enemy exposure through the shared body-state search. The ordinary
/// activity remains suspended until a stable result, replacement or explicit failed attempt.</summary>
public sealed class CreateCombatSpace
{
    private ulong retryAfter;
    public float Exposure { get; private set; }
    public bool Pending { get; private set; }

    public bool NeedsResponse(in ActionContext ctx)
    {
        Exposure = Positioner.PredictedExposureAt(ctx.Npc.Bottom, ctx.Senses);
        return ctx.Senses.Threats.CompanionInTrouble && Exposure > Weights.CombatSpaceExposure
            && Terraria.Main.GameUpdateCount >= retryAfter;
    }

    public bool IsSatisfied(in ActionContext ctx)
        => SafeLanding(ctx.Companion.Motor.State)
            && Positioner.PredictedExposureAt(ctx.Npc.Bottom, ctx.Senses) <= Weights.CombatSpaceExposure;

    public bool TryMove(in ActionContext ctx, out Controls controls)
    {
        var senses = ctx.Senses;
        bool chosen = ctx.Companion.Brain.Movement.SeekState(ctx.Companion.Motor.State,
            state => SafeLanding(state) && Positioner.PredictedExposureAt(state.Feet, senses) <= Weights.CombatSpaceExposure,
            state => Positioner.PredictedExposureAt(state.Feet, senses) * Weights.CombatSpaceHeuristicPixels,
            Weights.EscapeSearchWork, out controls, out bool pending);
        Pending = pending;
        if (!chosen && !pending) retryAfter = Terraria.Main.GameUpdateCount + Weights.CombatSpaceRetryTicks;
        return chosen;
    }

    private static bool SafeLanding(BodyState state)
        => state.OnGround && state.LiquidKind != 1 && ReachEnvironmentalSafety.HeadIsDry(state);
}
