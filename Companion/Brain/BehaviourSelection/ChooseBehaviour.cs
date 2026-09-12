#nullable enable

using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.PurposeFamilies.Combat;
using AICompanion.Companion.Brain.Behaviours.Work;
using AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance;

namespace AICompanion.Companion.Brain.BehaviourSelection;

/// <summary>
/// The utility scorer: every action rates itself, the incumbent keeps a small bonus,
/// anything that would outlast the safety horizon is charged, and the best runs. The
/// per-tick scores are kept so the overlay and the telemetry can show why.
/// </summary>
public sealed class Chooser
{
    public readonly record struct Scored(CompanionAction Action, float Raw, float Final,
        float Protection = 1f, float Commitment = 1f, float Horizon = 1f, float UsefulWork = 1f, string Error = "", float Reunion = 1f);

    public readonly List<CompanionAction> Actions = new()
    {
        new ProtectPlayer(),
        new PursueAttackOpportunity(),
        new CollectNearbyItems(),
        new ChopAction(),
        new MineAction(),
        new PlaceNearbyTorches(),
        new KeepCompany(),
    };

    public readonly List<Scored> LastScores = new();
    public FamilyNomination[] LastNominations { get; private set; } = Array.Empty<FamilyNomination>();
    public readonly OwnCurrentActivity Activity = new();
    public CompanionAction? Current => Activity.Current;
    /// <summary>Identity and source tick of a completed comparison, not of the latest brain update.</summary>
    public long EvaluationId { get; private set; }
    public ulong? EvaluationTick { get; private set; }
    public float RegroupUrgency { get; private set; }
    public float EstimatedReturnTicks { get; private set; }
    public AssessReunionCost Reunion { get; } = new();

    public void ObserveCompanionship(in ActionContext ctx)
    {
        var objective = new PositionSelection.FollowPlayerObjective(ctx.Senses.Player.Bottom, ctx.Senses.Player.Bottom);
        Reunion.Observe(Terraria.Main.GameUpdateCount,
            objective.IsSatisfied(ctx.Npc.Bottom, ctx.Senses.Player.CompanionCanSeePlayer), ctx.Senses.Player.IsDead);
    }
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
        ObserveCompanionship(ctx);
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
        float movingAway = delta.LengthSquared() > 1f ? Microsoft.Xna.Framework.Vector2.Dot(ctx.Senses.Player.Intent, Microsoft.Xna.Framework.Vector2.Normalize(delta)) : 0f;
        Reunion.Evaluate(movingAway, EstimatedReturnTicks, ctx.Senses.Player.IsDead, ctx.Stranded);
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
        // Discovery runs once per adapter. Score and forecast read the captured candidate;
        // neither receives live context or advances the job during comparison.
        var prepared = new PreparedActivity[Actions.Count];
        var bindings = new ValidatePreparedActivity[Actions.Count];
        for (int i = 0; i < Actions.Count; i++)
        {
            CompanionAction action = Actions[i];
            action.Prepare(ctx);
            float raw = action.Score();
            prepared[i] = new(i, action.Name, raw, raw > 0 ? action.ForecastTicks() : 0,
                action.IsExcursion, action.ActivityTarget != null, action is KeepCompany, action == Current);
            bindings[i] = ValidatePreparedActivity.Capture(action);
        }
        var comparison = ComparisonContext(ctx);
        var available = (PreparedActivity[])prepared.Clone();
        var rejections = new string?[prepared.Length];
        CompanionAction? best = null;
        // Every rejection removes one prepared candidate. Reconsideration is bounded by the
        // board size and never reruns discovery. Recompute shared opportunity costs because an
        // invalidated work offer must no longer suppress companionship.
        for (int attempt = 0; attempt <= prepared.Length; attempt++)
        {
            var evaluated = EvaluatePreparedActivities.Evaluate(available, comparison);
            var candidates = new FamilyCandidate[evaluated.Length];
            LastScores.Clear();
            foreach (EvaluatedActivity evaluatedScore in evaluated)
            {
                var score = rejections[evaluatedScore.Index] is { } rejection
                    ? evaluatedScore with { Raw = prepared[evaluatedScore.Index].RawValue, Error = rejection }
                    : evaluatedScore;
                CompanionAction action = Actions[score.Index];
                LastScores.Add(new(action, score.Raw, score.Final, score.Protection, score.Commitment, score.Horizon, score.UsefulWork, score.Error, score.Reunion));
                candidates[score.Index] = new(action.Family, score);
            }
            LastNominations = NominateFamilyActivities.Nominate(candidates);
            if (NominateFamilyActivities.Select(LastNominations) is not { } winner) break;
            string reason = bindings[winner.Index].Rejection(Actions[winner.Index]);
            if (reason.Length == 0) { best = Actions[winner.Index]; break; }
            rejections[winner.Index] = reason;
            available[winner.Index] = available[winner.Index] with { RawValue = 0 };
        }
        Activity.Select(best, ctx);
        EvaluationId++;
        EvaluationTick = Terraria.Main.GameUpdateCount;
        return best;
    }

    public ActivityComparisonContext ComparisonContext(in ActionContext ctx)
        => new(ctx.Senses.Threats.ProtectionUrgency, ctx.Stranded,
            ctx.Senses.Threats.Horizon, Weights.InterruptibleActionTicks, Weights.HorizonOverrunToZero, Weights.Commitment,
            ctx.Senses.DistanceToPlayer <= PlayerIntegration.CompanionPreferences.Current.ActiveActivityRadius,
            Weights.FollowDuringUsefulWork, Reunion.DelayCostPerTick);
}
