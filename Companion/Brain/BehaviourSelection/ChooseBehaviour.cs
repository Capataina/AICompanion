#nullable enable

using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.Behaviours.Combat;
using AICompanion.Companion.Brain.Behaviours.Companionship;
using AICompanion.Companion.Brain.Behaviours.Gathering;
using AICompanion.Companion.Brain.Behaviours.Survival;
using AICompanion.Companion.Brain.Behaviours.Work;

namespace AICompanion.Companion.Brain.BehaviourSelection;

/// <summary>
/// The utility scorer: every action rates itself, the incumbent keeps a small bonus,
/// anything that would outlast the safety horizon is charged, and the best runs. The
/// per-tick scores are kept so the overlay and the telemetry can show why.
/// </summary>
public sealed class Chooser
{
    public readonly record struct Scored(CompanionAction Action, float Raw, float Final);

    public readonly List<CompanionAction> Actions = new()
    {
        new SurviveAction(),
        new GuardAction(),
        new KiteAction(),
        new HuntAction(),
        new LootAction(),
        new ChopAction(),
        new MineAction(),
        new WalkWithPlayerAction(),
        new WanderAction(),
    };

    public readonly List<Scored> LastScores = new();
    public CompanionAction? Current { get; private set; }
    public float RegroupUrgency { get; private set; }
    public float EstimatedReturnTicks { get; private set; }

    public CompanionAction Choose(in ActionContext ctx)
    {
        LastScores.Clear();
        var delta = ctx.Senses.Player.Bottom - ctx.Npc.Bottom;
        EstimatedReturnTicks = (MathF.Abs(delta.X) + MathF.Abs(delta.Y)) / SharedMovementSystem.BodyPhysics.WalkSpeed;
        var navigator = ctx.Companion.Brain.Navigator;
        if (ctx.Companion.Brain.Positioner.EstimatedTravelTicks(SharedMovementSystem.NavGrid.FeetTile(ctx.Npc.Bottom),
            SharedMovementSystem.NavGrid.FeetTile(ctx.Senses.Player.Bottom)) is float knownTravel)
            EstimatedReturnTicks = MathF.Max(EstimatedReturnTicks, knownTravel);
        if (ctx.Companion.Brain.LastRequest.Kind is PositionSelection.RequestKind.WithPlayer or PositionSelection.RequestKind.Guard
            && navigator.Path is { Finished: false } route)
        {
            float routeTicks = 0f;
            for (int i = route.Index; i < route.Steps.Count; i++) routeTicks += route.Steps[i].Ticks;
            EstimatedReturnTicks = MathF.Max(EstimatedReturnTicks, routeTicks);
        }
        float movingAway = delta.LengthSquared() > 1f ? Microsoft.Xna.Framework.Vector2.Dot(ctx.Senses.Player.Velocity, Microsoft.Xna.Framework.Vector2.Normalize(delta)) : 0f;
        RegroupUrgency = ctx.Senses.Player.IsDead ? 0f : WorldObservation.CalculateRegroupUrgency.Evaluate(
            ctx.Senses.DistanceToPlayer, EstimatedReturnTicks, movingAway, navigator.StuckTicks,
            Weights.CalmBandFar, Weights.RegroupFullDistance, Weights.RegroupFreeReturnTicks, Weights.RegroupFullReturnTicks);
        float horizon = ctx.Senses.Threats.Horizon;
        // An all-zero board (the player is dead, nothing to do) falls to the last action, wander,
        // which holds still in that case; starting below zero would hand the tick to whichever
        // action happens to be listed first.
        CompanionAction? best = Actions[^1];
        float bestScore = 0f;

        foreach (CompanionAction action in Actions)
        {
            float raw = action.Score(ctx);
            float final = raw;
            if (action.IsExcursion && !ctx.Stranded) final *= 1f - Math.Max(RegroupUrgency, ctx.Senses.Threats.ProtectionUrgency);
            if (raw > 0f)
            {
                if (action == Current)
                    final *= Weights.Commitment;
                float forecast = action.ForecastTicks(ctx);
                if (forecast > horizon)
                {
                    float overrun = forecast - horizon;
                    final *= Math.Max(0f, 1f - overrun / Weights.HorizonOverrunToZero);
                }
            }
            LastScores.Add(new Scored(action, raw, final));
            if (final > bestScore)
            {
                bestScore = final;
                best = action;
            }
        }

        if (best != Current)
        {
            Current?.Exit(ctx);
            best.Enter(ctx);
            Current = best;
        }
        return best;
    }
}
