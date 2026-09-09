#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Brain.BehaviourSelection;
using AICompanion.Brain.PositionSelection;
using AICompanion.Brain.WorldObservation;

namespace AICompanion.Brain.Behaviours.Combat;

/// <summary>
/// Go and kill a reachable hostile. Scores by having a target the weapons can engage
/// and by the player being safe enough to leave; the forecast is the trip to a firing
/// spot, which the chooser charges against the horizon. Distance is charged there and
/// only there: an enemy anywhere on screen is worth the full hunt, because a companion
/// that keeps chopping while a zombie walks across the screen reads as not having seen
/// it, and only a target beyond the screen loses value with range. A second enemy
/// appearing does not end a hunt; only the danger and horizon it changes can.
/// </summary>
public sealed class HuntAction : CompanionAction
{
    public override string Name => "hunt";

    public ThreatRecord? Target { get; private set; }

    public override float Score(in ActionContext ctx)
    {
        Rectangle screen = ScreenWithMargin();
        Target = PickTarget(ctx, screen);
        if (Target == null || ctx.Senses.Player.IsDead)
            return 0f;
        float safe = Consideration.AtLeast(1f - ctx.Senses.Threats.PlayerDanger, 0.1f);
        float near = Target.Npc.Hitbox.Intersects(screen)
            ? 1f
            : Consideration.AtLeast(Consideration.Inverse(Target.DistanceToCompanion, Weights.HuntReach), 0.2f);
        float worth = Target.IsBoss ? 1f : 0.85f;
        // Two things this used to ignore, both of which killed it.
        //
        // Where the player is. Hunting is opportunistic — something to do when there is little
        // else going on — and it was scored as though the companion stood alone in the world, so
        // a hunt 84 tiles away scored exactly as well as one at his shoulder. Worse, the only
        // safety term was about the *player*, who is safest of all when the companion has wandered
        // off, so straying made hunting score higher. That is a loop with a body at the end of it.
        //
        // Its own skin. Every danger term in the brain read PlayerDanger, so a companion being
        // surrounded 84 tiles out was in a world with no danger in it (2026-09-09, five hits in
        // 330 ticks, danger 0.00 on every one). Hunting now yields as its own danger rises, which
        // is what lets disengaging outscore pressing on.
        float leash = Consideration.Inverse(MathF.Max(0f, ctx.Senses.DistanceToPlayer - Weights.HuntLeashFree), Weights.HuntLeashToZero);
        float ownSkin = Consideration.AtLeast(1f - ctx.Senses.Threats.CompanionDanger, 0.05f);
        return safe * near * worth * leash * ownSkin;
    }

    private static Rectangle ScreenWithMargin()
        => new((int)Main.screenPosition.X - 200, (int)Main.screenPosition.Y - 200, Main.screenWidth + 400, Main.screenHeight + 400);

    public override float ForecastTicks(in ActionContext ctx)
        => Target == null ? 0f : MathF.Max(0f, Target.DistanceToCompanion - 200f) / Companion.CompanionMotor.WalkSpeed + 60f;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        if (Target == null)
            return PositionRequest.Hold;
        // No firing here. Shooting is not something a mode does, it is what the hands do every
        // tick whatever the feet were told, so it lives in the brain's own tick; hunting is now
        // only the decision to walk toward something. See Brain.Engage.
        return new PositionRequest(RequestKind.LineOfFire, Target.Npc.Center, Target.Npc);
    }

    /// <summary>Threats endangering the player first, then the nearest reachable one a weapon can reach.</summary>
    private static ThreatRecord? PickTarget(in ActionContext ctx, Rectangle screen)
    {
        ThreatRecord? best = null;
        float bestScore = 0f;
        foreach (ThreatRecord t in ctx.Senses.Threats.Threats)
        {
            if (!t.Reachable && !t.Npc.Hitbox.Intersects(screen))
                continue;
            if (!t.Reachable && t.Npc.Hitbox.Intersects(screen) && t.Class != MovementClass.Phaser && !ctx.Companion.Arsenal.CanEngage(ctx, t.Npc))
                continue; // sealed off and no shot: not worth a thought
            float score = 0.4f * t.Urgency + 0.6f * Consideration.Inverse(t.DistanceToCompanion, Weights.HuntReach);
            if (t.IsBoss) score += 0.3f;
            // An on-screen enemy is always a candidate: beyond HuntReach the distance term is zero
            // and a calm enemy's urgency is zero too, which vetoed it before the screen rule scored it.
            if (score <= 0f && t.Npc.Hitbox.Intersects(screen)) score = 0.05f;
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }
        return best;
    }
}
