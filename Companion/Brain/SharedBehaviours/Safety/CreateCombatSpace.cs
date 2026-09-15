#nullable enable

using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Movement;

namespace AICompanion.Companion.Brain.SharedBehaviours.Safety;

/// <summary>Seek lower enemy exposure through the shared free-space search. The ordinary
/// activity remains suspended until a stable result, replacement or explicit failed attempt.</summary>
public sealed class CreateCombatSpace
{
    private ulong retryAfter;
    public float Exposure { get; private set; }
    public bool Pending { get; private set; }

    public bool NeedsResponse(in ActionContext ctx)
    {
        Exposure = Positioner.PredictedExposureAt(ctx.Npc.Center, ctx.Senses);
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
        => Safe(ctx.Npc.Center) && Positioner.PredictedExposureAt(ctx.Npc.Center, ctx.Senses) <= Weights.CombatSpaceExposure;

    public bool TryMove(in ActionContext ctx, out Controls controls)
    {
        var senses = ctx.Senses;
        bool chosen = ctx.Companion.Brain.Movement.SeekState(ctx.Companion.Motor.State,
            centre => Safe(centre) && Positioner.PredictedExposureAt(centre, senses) <= Weights.CombatSpaceExposure,
            centre => Positioner.PredictedExposureAt(centre, senses) * Weights.CombatSpaceHeuristicPixels,
            Weights.EscapeSearchWork, throughLiquid: false, out controls, out bool pending);
        Pending = pending;
        if (!chosen && !pending) retryAfter = Terraria.Main.GameUpdateCount + Weights.CombatSpaceRetryTicks;
        return chosen;
    }

    /// <summary>A place worth stopping at: the flood already admits only free cells, and this adds that none of the
    /// liquid the body would touch there hurts it.</summary>
    private static bool Safe(Vector2 centre) => ReachEnvironmentalSafety.Dry(centre);
}
