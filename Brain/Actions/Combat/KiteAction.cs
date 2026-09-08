#nullable enable

using AICompanion.Brain.DecisionMatrix.Decision;
using AICompanion.Brain.DecisionMatrix.Senses;

namespace AICompanion.Brain.Actions.Combat;

/// <summary>
/// Something is on top of the companion: back off while keeping a line of fire. The
/// positioner's retreat request does the geometry; this action only says when.
/// </summary>
public sealed class KiteAction : CompanionAction
{
    public override string Name => "kite";

    public override float Score(in ActionContext ctx)
    {
        float closest = float.MaxValue;
        foreach (ThreatRecord t in ctx.Senses.Threats.Threats)
            if (t.Reachable && t.DistanceToCompanion < closest)
                closest = t.DistanceToCompanion;
        return Consideration.Inverse(closest, Weights.KiteTrigger * 1.5f);
    }

    public override PositionRequest Execute(in ActionContext ctx)
    {
        var target = ctx.Senses.Threats.MostUrgent?.Npc;
        ctx.Companion.Arsenal.TryFire(ctx, target);
        return new PositionRequest(RequestKind.Retreat, ctx.Senses.Player.Bottom, target);
    }
}
