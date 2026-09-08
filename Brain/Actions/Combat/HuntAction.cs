#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Brain.DecisionMatrix.Decision;
using AICompanion.Brain.DecisionMatrix.Senses;

namespace AICompanion.Brain.Actions.Combat;

/// <summary>
/// Go and kill a reachable hostile. Scores by having a target the weapons can engage
/// and by the player being safe enough to leave; the forecast is the trip to a firing
/// spot, which the chooser charges against the horizon. A second enemy appearing does
/// not end a hunt; only the danger and horizon it changes can.
/// </summary>
public sealed class HuntAction : CompanionAction
{
    public override string Name => "hunt";

    public ThreatRecord? Target { get; private set; }

    public override float Score(in ActionContext ctx)
    {
        Target = PickTarget(ctx);
        if (Target == null || ctx.Senses.Player.IsDead)
            return 0f;
        float safe = Consideration.AtLeast(1f - ctx.Senses.Threats.PlayerDanger, 0.1f);
        float near = Consideration.AtLeast(Consideration.Inverse(Target.DistanceToCompanion, Weights.HuntReach), 0.2f);
        float worth = Target.IsBoss ? 1f : 0.85f;
        return safe * near * worth;
    }

    public override float ForecastTicks(in ActionContext ctx)
        => Target == null ? 0f : MathF.Max(0f, Target.DistanceToCompanion - 200f) / Companion.CompanionMotor.WalkSpeed + 60f;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        if (Target == null)
            return PositionRequest.Hold;
        ctx.Companion.Arsenal.TryFire(ctx, Target.Npc);
        return new PositionRequest(RequestKind.LineOfFire, Target.Npc.Center, Target.Npc);
    }

    /// <summary>Threats endangering the player first, then the nearest reachable one a weapon can reach.</summary>
    private static ThreatRecord? PickTarget(in ActionContext ctx)
    {
        ThreatRecord? best = null;
        float bestScore = 0f;
        Rectangle screen = new((int)Main.screenPosition.X - 200, (int)Main.screenPosition.Y - 200, Main.screenWidth + 400, Main.screenHeight + 400);
        foreach (ThreatRecord t in ctx.Senses.Threats.Threats)
        {
            if (!t.Reachable && !t.Npc.Hitbox.Intersects(screen))
                continue;
            if (!t.Reachable && t.Npc.Hitbox.Intersects(screen) && t.Class != MovementClass.Phaser && !ctx.Companion.Arsenal.CanEngage(ctx, t.Npc))
                continue; // sealed off and no shot: not worth a thought
            float score = 0.4f * t.Urgency + 0.6f * Consideration.Inverse(t.DistanceToCompanion, Weights.HuntReach);
            if (t.IsBoss) score += 0.3f;
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }
        return best;
    }
}
