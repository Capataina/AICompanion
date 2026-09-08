#nullable enable

using System;
using System.Collections.Generic;
using AICompanion.Brain.Decision.Actions;

namespace AICompanion.Brain.Decision;

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
        new GuardAction(),
        new KiteAction(),
        new HuntAction(),
        new LootAction(),
        new ChopAction(),
        new WalkWithPlayerAction(),
        new WanderAction(),
    };

    public readonly List<Scored> LastScores = new();
    public CompanionAction? Current { get; private set; }

    public CompanionAction Choose(in ActionContext ctx)
    {
        LastScores.Clear();
        float horizon = ctx.Senses.Threats.Horizon;
        CompanionAction? best = null;
        float bestScore = -1f;

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

        best ??= Actions[^1];
        if (best != Current)
        {
            Current?.Exit(ctx);
            best.Enter(ctx);
            Current = best;
        }
        return best;
    }
}
