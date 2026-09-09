#nullable enable

using System;
using System.Collections.Generic;
using AICompanion.Brain.Behaviours;
using AICompanion.Brain.Behaviours.Combat;
using AICompanion.Brain.Behaviours.Companionship;
using AICompanion.Brain.Behaviours.Gathering;
using AICompanion.Brain.Behaviours.Survival;
using AICompanion.Brain.Behaviours.Work;

namespace AICompanion.Brain.BehaviourSelection;

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

    public CompanionAction Choose(in ActionContext ctx)
    {
        LastScores.Clear();
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
