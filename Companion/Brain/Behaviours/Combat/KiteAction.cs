#nullable enable

using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;
using AICompanion.Companion.Brain.WorldObservation;

namespace AICompanion.Companion.Brain.Behaviours.Combat;

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
        // Backing off is all this does now; the shooting happens in Brain.Engage whatever is
        // chosen, which is what stops kiting being a mode you have to be in to fight back.
        var target = ctx.Senses.Threats.MostUrgent?.Npc;
        return new PositionRequest(RequestKind.Retreat, ctx.Senses.Player.Bottom, target);
    }
}
