#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria.ID;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;

namespace AICompanion.Companion.Brain.Behaviours.Companionship;

/// <summary>
/// Keep up with an exploring player: go where they are going, not where they are.
/// Scores by how far the companion is from the player's predicted position, so it
/// walks alongside rather than trailing at the edge of the band, and keeps going for
/// a moment when the player pauses because the intent outlives the pause.
/// </summary>
public sealed class WalkWithPlayerAction : CompanionAction
{
    public override string Name => "walk-with";
    public override bool IsExcursion => false;

    public override float Score(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsDead)
            return 0f;
        Vector2 ahead = p.Predict(45);
        float gap = Vector2.Distance(ctx.Npc.Bottom, ahead);
        float hardLeash = Consideration.Step(ctx.Senses.DistanceToPlayer > Weights.LeashHard, 1f, 0f);
        // Stranded, the follow yields: every plan to the player returns nothing and the body
        // pressed at the wall nearest them is a body doing nothing, so wander's roam outscores
        // this while the brain says so and this wins back for the retry window between roams.
        float stranded = ctx.Stranded ? Weights.StrandedFollowDiscount : 1f;
        float regroup = ctx.Companion.Brain.Chooser.RegroupUrgency;
        var objective = new FollowPlayerObjective(p.Bottom, ahead);
        float horizontalRatio = objective.HorizontalGap(ctx.Npc.Bottom) / objective.HorizontalComfort;
        float verticalRatio = objective.VerticalGap(ctx.Npc.Bottom) / objective.VerticalComfort;
        float objectiveRatio = MathF.Max(horizontalRatio, verticalRatio);
        float objectiveMiss = objectiveRatio > 1f
            ? Consideration.AtLeast(Consideration.Rising(objectiveRatio - 1f, 3f), 0.3f)
            : 0f;
        if (!global::AICompanion.Companion.Brain.WorldObservation.LineOfSight.Between(ctx.Npc, ctx.Player))
            objectiveMiss = MathF.Max(objectiveMiss, 0.3f);
        if (p.IsTravelling)
            return MathF.Max(objectiveMiss, MathF.Max(regroup, MathF.Max(Consideration.AtLeast(Consideration.Rising(gap, Weights.FollowIntentDistance * 2f), 0.3f), hardLeash))) * stranded;

        // Standing player: only worth acting on when the companion has drifted well out of the
        // comfort region. Local occlusion still requires following even inside its axis limits.
        float drifted = Consideration.Rising(ctx.Senses.DistanceToPlayer - Weights.CalmBandFar, 400f) * 0.6f;
        return MathF.Max(objectiveMiss, MathF.Max(regroup, MathF.Max(drifted, hardLeash))) * stranded;
    }

    public override PositionRequest Execute(in ActionContext ctx)
    {
        ctx.Companion.HoldItem(ItemID.None);
        var p = ctx.Senses.Player;
        Vector2 anchor = p.IsTravelling ? p.Predict(45) : p.Bottom;
        return new PositionRequest(RequestKind.WithPlayer, anchor);
    }
}
