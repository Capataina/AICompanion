#nullable enable

using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Movement;

namespace AICompanion.Companion.Brain.SharedBehaviours.Safety;

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
        return ctx.Senses.Threats.CompanionInTrouble && Connecting(ctx) && Exposure > Weights.CombatSpaceExposure
            && Terraria.Main.GameUpdateCount >= retryAfter;
    }

    /// <summary>Spacing is for something that will land, not for a hopper ten tiles away. A predicted overlap, or a proven reach arriving inside CombatSpaceConnectTicks, counts; mere Inverse(160px) proximity does not.</summary>
    private static bool Connecting(in ActionContext ctx)
    {
        foreach (var threat in ctx.Senses.Threats.Threats)
        {
            for (int tick = 0; tick <= (int)Weights.CombatSpaceConnectTicks; tick += 10)
                if (threat.PredictedHitbox(tick).Intersects(ctx.Npc.Hitbox))
                    return true;
            if (threat.CanReachCompanion && threat.TicksToCompanion <= Weights.CombatSpaceConnectTicks)
                return true;
        }
        return false;
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
