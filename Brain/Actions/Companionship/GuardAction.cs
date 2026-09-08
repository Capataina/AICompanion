#nullable enable

using Terraria.ID;
using AICompanion.Brain.DecisionMatrix.Decision;

namespace AICompanion.Brain.Actions.Companionship;

/// <summary>
/// The player is in danger: be next to them with a sight line, and shoot the most
/// urgent threat from there. Outscores everything as danger rises, which is what
/// pulls the companion off a hunt or a loot run when a shooter gets a line on the player.
/// </summary>
public sealed class GuardAction : CompanionAction
{
    public override string Name => "guard";

    public override float Score(in ActionContext ctx)
    {
        var t = ctx.Senses.Threats;
        if (ctx.Senses.Player.IsDead)
            return 0f;
        float danger = t.PlayerDanger;
        float away = Consideration.AtLeast(Consideration.Rising(ctx.Senses.DistanceToPlayer, 400f), 0.4f);
        return danger * away;
    }

    public override PositionRequest Execute(in ActionContext ctx)
    {
        var target = ctx.Senses.Threats.MostUrgent?.Npc;
        ctx.Companion.Arsenal.TryFire(ctx, target);
        return new PositionRequest(RequestKind.Guard, ctx.Senses.Player.Bottom, target);
    }
}
