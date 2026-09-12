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
    public readonly record struct Scored(CompanionAction Action, float Raw, float Final,
        float Protection = 1f, float Commitment = 1f, float Horizon = 1f, float UsefulWork = 1f, string Error = "");

    public readonly List<CompanionAction> Actions = new()
    {
        new SurviveAction(),
        new GuardAction(),
        new KiteAction(),
        new HuntAction(),
        new LootAction(),
        new ChopAction(),
        new MineAction(),
        new BreakNearbyPots(),
        new PlaceNearbyTorches(),
        new WalkWithPlayerAction(),
        new WanderAction(),
    };

    public readonly List<Scored> LastScores = new();
    public readonly OwnCurrentActivity Activity = new();
    public CompanionAction? Current => Activity.Current;
    /// <summary>Identity and source tick of a completed comparison, not of the latest brain update.</summary>
    public long EvaluationId { get; private set; }
    public ulong? EvaluationTick { get; private set; }
    public float RegroupUrgency { get; private set; }
    public float EstimatedReturnTicks { get; private set; }
    private Microsoft.Xna.Framework.Vector2? workSite;
    private ulong workSiteTick;
    public bool IsCollectingWork(Microsoft.Xna.Framework.Vector2 target)
        => workSite is { } site && Terraria.Main.GameUpdateCount - workSiteTick <= Weights.WorkCollectionTicks
            && Microsoft.Xna.Framework.Vector2.DistanceSquared(site, target) <= Weights.WorkSiteRadius * Weights.WorkSiteRadius;

    public void RecordWork(Microsoft.Xna.Framework.Vector2 site)
    {
        workSite = site;
        workSiteTick = Terraria.Main.GameUpdateCount;
    }

    public CompanionAction? Choose(in ActionContext ctx)
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
            Weights.FollowHorizontalComfort * PlayerIntegration.CompanionPreferences.Current.FollowComfortScale,
            Weights.RegroupFullDistance, Weights.RegroupFreeReturnTicks, Weights.RegroupFullReturnTicks);
        var follow = new PositionSelection.FollowPlayerObjective(ctx.Senses.Player.Bottom, ctx.Senses.Player.Bottom);
        bool arrived = follow.IsSatisfied(ctx.Npc.Bottom, WorldObservation.LineOfSight.Between(ctx.Npc, ctx.Player));
        if (arrived) RegroupUrgency = 0f;
        if (!ctx.Senses.Player.IsDead && !arrived)
        {
            // A nearby player behind a floor can have a long route. Geometric closeness must
            // not suppress measured return pressure when companionship is still unsatisfied.
            float travelPressure = Math.Clamp((EstimatedReturnTicks + navigator.StuckTicks - Weights.RegroupFreeReturnTicks)
                / Math.Max(1f, Weights.RegroupFullReturnTicks - Weights.RegroupFreeReturnTicks), 0f, 1f);
            RegroupUrgency = Math.Max(RegroupUrgency, travelPressure);
        }
        float horizon = ctx.Senses.Threats.Horizon;
        // An all-zero board (the player is dead, nothing to do) falls to the last action, wander,
        // which holds still in that case; starting below zero would hand the tick to whichever
        // action happens to be listed first.
        CompanionAction? best = null, fallback = null;
        float bestScore = 0f;

        // Discovery runs once per adapter. Score and forecast read the captured candidate;
        // neither receives live context or advances the job during comparison.
        var prepared = new PreparedActivity[Actions.Count];
        for (int i = 0; i < Actions.Count; i++)
        {
            CompanionAction action = Actions[i];
            action.Prepare(ctx);
            float raw = action.Score();
            prepared[i] = new(i, action.Name, raw, raw > 0 ? action.ForecastTicks() : 0,
                action.IsExcursion, action.ActivityTarget != null, action is WalkWithPlayerAction, action == Current);
        }
        var comparison = new ActivityComparisonContext(ctx.Senses.Threats.ProtectionUrgency, ctx.Stranded,
            horizon, Weights.InterruptibleActionTicks, Weights.HorizonOverrunToZero, Weights.Commitment,
            ctx.Senses.DistanceToPlayer <= PlayerIntegration.CompanionPreferences.Current.ActiveActivityRadius,
            Weights.FollowDuringUsefulWork);
        foreach (EvaluatedActivity score in EvaluatePreparedActivities.Evaluate(prepared, comparison))
        {
            CompanionAction action = Actions[score.Index];
            LastScores.Add(new(action, score.Raw, score.Final, score.Protection, score.Commitment, score.Horizon, score.UsefulWork, score.Error));
            if (score.Error.Length != 0) continue;
            fallback = action;
            if (score.Final > bestScore)
            {
                bestScore = score.Final;
                best = action;
            }
        }

        best ??= fallback;
        Activity.Select(best, ctx);
        EvaluationId++;
        EvaluationTick = Terraria.Main.GameUpdateCount;
        return best;
    }
}
