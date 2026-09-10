#nullable enable

using Terraria.ID;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;

namespace AICompanion.Companion.Brain.Behaviours.Companionship;

/// <summary>
/// The player is in danger: be next to them with a sight line, and shoot the most
/// urgent threat from there. Outscores everything as danger rises, which is what
/// pulls the companion off a hunt or a loot run when a shooter gets a line on the player.
/// </summary>
public sealed class GuardAction : CompanionAction
{
    public override string Name => "guard";
    public override bool IsExcursion => false;

    public override float Score(in ActionContext ctx)
    {
        var t = ctx.Senses.Threats;
        if (ctx.Senses.Player.IsDead)
            return 0f;
        float danger = t.ProtectionUrgency;
        float away = Consideration.AtLeast(Consideration.Rising(ctx.Senses.DistanceToPlayer, 400f), 0.4f);
        // Scaled past the ordinary 0..1 band because guarding has to be able to interrupt, and an
        // action that tops out at 1 cannot interrupt anything: the running action carries
        // Weights.Commitment, so a following body already at 1 sits at 1.15 and no danger reading
        // could ever displace it. That is not a tuning miss, it is arithmetic, and it is what left
        // the companion holding a torch with the player's danger reading full and slimes on him
        // through the 2026-09-09 underground session. Weights.GuardUrgency owns the ladder and
        // why survive sits above this in turn.
        return danger * away * Weights.GuardUrgency;
    }

    public override PositionRequest Execute(in ActionContext ctx)
    {
        // Standing between him and them is all this does now; the shooting happens in Brain.Engage.
        var target = ctx.Senses.Threats.MostUrgent?.Npc;
        return new PositionRequest(RequestKind.Guard, ctx.Senses.Player.Bottom, target);
    }
}
