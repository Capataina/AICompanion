#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;

namespace AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance;

/// <summary>
/// Accompany the player's activity through reunion, resting or nearby movement.
/// These methods share one ordinary purpose rather than competing as separate jobs.
/// </summary>
public sealed class KeepCompany : CompanionAction
{
    public override string Name => "keep-company";
    public override PurposeFamily Family => PurposeFamily.NearbyAssistance;
    public override bool IsExcursion => false;

    private float preparedValue;
    private bool reunite;
    private bool walking;
    private int ticksLeft = 1;
    private Vector2 goal;
    private bool hop;

    public override void Enter(in ActionContext ctx) => ResetLocalMovement();

    public override void Prepare(in ActionContext ctx)
    {
        float reunionValue = CalculateReunionValue(ctx);
        float localValue = ctx.Senses.Player.IsDead ? 0f : ctx.Stranded ? Weights.StrandedWander : Weights.WanderFloor;
        reunite = reunionValue > localValue;
        preparedValue = MathF.Max(reunionValue, localValue);
    }
    public override float Score() => preparedValue;

    private float CalculateReunionValue(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsDead)
            return 0f;
        Vector2 ahead = p.Predict(45);
        float gap = Vector2.Distance(ctx.Npc.Bottom, ahead);
        float hardLeash = Consideration.Step(ctx.Senses.DistanceToPlayer > Weights.LeashHard, 1f, 0f);
        // A proven sealed pocket changes the available method, not the companion's purpose.
        // The coordinator periodically retries reunion rather than forgetting it forever.
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
        if (p.IsDead) return PositionRequest.Hold;
        if (reunite)
        {
            ResetLocalMovement();
            Vector2 anchor = p.IsTravelling ? p.Predict(45) : p.Bottom;
            return new PositionRequest(RequestKind.WithPlayer, anchor);
        }
        if (ctx.Stranded) return new PositionRequest(RequestKind.Roam, ctx.Npc.Bottom);
        if (walking && Vector2.DistanceSquared(goal, p.Bottom) > Weights.CalmBandFar * Weights.CalmBandFar)
            ResetLocalMovement();
        if (--ticksLeft <= 0) PickLocalMovement(ctx);
        float jump = hop ? 0.7f : 0f;
        hop = false;
        return (walking ? PositionRequest.ExactAt(goal) : PositionRequest.Hold) with { JumpScale = jump };
    }

    private void ResetLocalMovement()
    {
        walking = false;
        ticksLeft = 1;
        hop = false;
    }

    private void PickLocalMovement(in ActionContext ctx)
    {
        if (walking || Main.rand.NextBool(3))
        {
            walking = false;
            ticksLeft = Main.rand.Next(60, 240);
            hop = Main.rand.NextBool(20);
            return;
        }
        walking = true;
        ticksLeft = Main.rand.Next(60, 240);
        float offset = Main.rand.NextFloat(-Weights.CalmBandFar * 0.7f, Weights.CalmBandFar * 0.7f);
        goal = ctx.Senses.Player.Bottom + new Vector2(offset, 0f);
        hop = Main.rand.NextBool(12);
    }
}
