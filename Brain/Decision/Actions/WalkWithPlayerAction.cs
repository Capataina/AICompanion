#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria.ID;

namespace AICompanion.Brain.Decision.Actions;

/// <summary>
/// Keep up with an exploring player: go where they are going, not where they are.
/// Scores by how far the companion is from the player's predicted position, so it
/// walks alongside rather than trailing at the edge of the band, and keeps going for
/// a moment when the player pauses because the intent outlives the pause.
/// </summary>
public sealed class WalkWithPlayerAction : CompanionAction
{
    public override string Name => "walk-with";

    public override float Score(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsDead)
            return 0f;
        Vector2 ahead = p.Predict(45);
        float gap = Vector2.Distance(ctx.Npc.Bottom, ahead);
        float travelling = Consideration.Step(p.IsTravelling, 1f, 0.35f);
        float far = Consideration.AtLeast(Consideration.Rising(gap, Weights.FollowIntentDistance * 2f), 0.15f);
        float hardLeash = Consideration.Step(ctx.Senses.DistanceToPlayer > Weights.LeashHard, 1f, 0f);
        return MathF.Max(travelling * far, hardLeash);
    }

    public override PositionRequest Execute(in ActionContext ctx)
    {
        ctx.Companion.HoldItem(ItemID.None);
        var p = ctx.Senses.Player;
        Vector2 anchor = p.IsTravelling ? p.Predict(45) : p.Bottom;
        return new PositionRequest(RequestKind.WithPlayer, anchor);
    }
}
