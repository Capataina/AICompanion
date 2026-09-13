#nullable enable

using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Activities.Combat;
using AICompanion.Companion.Brain.Activities.Gathering;
using AICompanion.Companion.Brain.Activities.NearbyAssistance;

namespace AICompanion.Companion.Brain.Infrastructure.Selection;

/// <summary>
/// The utility scorer: every action rates itself, the incumbent keeps a small bonus,
/// anything that would outlast the safety horizon is charged, and the best runs. The
/// per-tick scores are kept so the overlay and the telemetry can show why.
/// </summary>
public sealed class Chooser
{
    public readonly record struct Scored(CompanionAction Action, float Raw, float Final,
        float Protection = 1f, float Commitment = 1f, float Horizon = 1f, float UsefulWork = 1f, string Error = "", float Reunion = 1f,
        string MethodEvidence = "", OfferEligibility Eligibility = OfferEligibility.NoOpportunity, string EligibilityReason = "");

    public readonly List<CompanionAction> Actions;

    public Chooser()
    {
        // Guarding and hunting ask one firing-opportunity query, so a threat on the player is judged
        // shootable or not by the same terrain scan and cache the hunt reads; guard prepares first and
        // warms that cache for the hunt on the same tick.
        var firingAccess = new ResolveFiringOpportunity();
        Actions = new()
        {
            new ProtectPlayer(firingAccess),
            new PursueAttackOpportunity(firingAccess),
            new CollectNearbyItems(),
            new ChopTree(),
            new MineOre(),
            new LightUsefulArea(),
            new KeepCompany(),
        };
    }

    public readonly List<Scored> LastScores = new();
    /// <summary>Which activities prepared in the last comparison and what each family spent.</summary>
    public readonly ScheduleOpportunityQueries Queries = new();
    /// <summary>Per-family preparation share; a fixture sets zero to force exactly one optional child per family.</summary>
    public double FamilyPreparationMilliseconds { get; set; } = Weights.FamilyPreparationMilliseconds;
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
        var objective = new Infrastructure.Position.FollowPlayerObjective(ctx.Senses.Player.Bottom, ctx.Senses.Player.Bottom);
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
        // Callers record work only for an observed productive native effect, so this is the one
        // place an attempt's effect count grows; an effect outside an executing attempt is uncredited.
        Activity.RecordProductiveEffect();
    }

    public CompanionAction? Choose(in ActionContext ctx)
    {
        ObserveCompanionship(ctx);
        LastScores.Clear();
        var delta = ctx.Senses.Player.Bottom - ctx.Npc.Bottom;
        EstimatedReturnTicks = (MathF.Abs(delta.X) + MathF.Abs(delta.Y)) / Infrastructure.Movement.BodyPhysics.WalkSpeed;
        var navigator = ctx.Companion.Brain.Navigator;
        if (ctx.Companion.Brain.Positioner.EstimatedTravelTicks(Infrastructure.Movement.NavGrid.FeetTile(ctx.Npc.Bottom),
            Infrastructure.Movement.NavGrid.FeetTile(ctx.Senses.Player.Bottom)) is float knownTravel)
            EstimatedReturnTicks = MathF.Max(EstimatedReturnTicks, knownTravel);
        if (ctx.Companion.Brain.LastRequest.Kind is Infrastructure.Position.RequestKind.WithPlayer or Infrastructure.Position.RequestKind.Guard
            && navigator.Path is { Finished: false } route)
        {
            float routeTicks = 0f;
            for (int i = route.Index; i < route.Steps.Count; i++) routeTicks += route.Steps[i].Ticks;
            EstimatedReturnTicks = MathF.Max(EstimatedReturnTicks, routeTicks);
        }
        float movingAway = delta.LengthSquared() > 1f ? Microsoft.Xna.Framework.Vector2.Dot(ctx.Senses.Player.Intent, Microsoft.Xna.Framework.Vector2.Normalize(delta)) : 0f;
        Reunion.Evaluate(movingAway, EstimatedReturnTicks, ctx.Senses.Player.IsDead, ctx.Stranded);
        RegroupUrgency = ctx.Senses.Player.IsDead ? 0f : Infrastructure.Observation.CalculateRegroupUrgency.Evaluate(
            ctx.Senses.DistanceToPlayer, EstimatedReturnTicks, movingAway, navigator.StuckTicks,
            Weights.FollowHorizontalComfort * PlayerIntegration.CompanionPreferences.Current.FollowComfortScale,
            Weights.RegroupFullDistance, Weights.RegroupFreeReturnTicks, Weights.RegroupFullReturnTicks);
        var follow = new Infrastructure.Position.FollowPlayerObjective(ctx.Senses.Player.Bottom, ctx.Senses.Player.Bottom);
        bool arrived = follow.IsSatisfied(ctx.Npc.Bottom, Infrastructure.Observation.LineOfSight.Between(ctx.Npc, ctx.Player));
        if (arrived) RegroupUrgency = 0f;
        if (!ctx.Senses.Player.IsDead && !arrived)
        {
            // A nearby player behind a floor can have a long route. Geometric closeness must
            // not suppress measured return pressure when companionship is still unsatisfied.
            float travelPressure = Math.Clamp((EstimatedReturnTicks + navigator.StuckTicks - Weights.RegroupFreeReturnTicks)
                / Math.Max(1f, Weights.RegroupFullReturnTicks - Weights.RegroupFreeReturnTicks), 0f, 1f);
            RegroupUrgency = Math.Max(RegroupUrgency, travelPressure);
        }
        // Discovery runs once per behaviour. Score and forecast read the captured candidate;
        // neither receives live context or advances the job during comparison.
        var prepared = new PreparedActivity[Actions.Count];
        var bindings = new ValidatePreparedActivity[Actions.Count];
        var offers = new (OfferEligibility Eligibility, string Reason)[Actions.Count];
        var context = ctx;
        bool[] ran = Queries.Prepare(Actions, Current, FamilyPreparationMilliseconds, i =>
        {
            CompanionAction action = Actions[i];
            action.ResetClassification();
            action.Prepare(context);
            float raw = action.Score();
            offers[i] = (action.Eligibility, action.EligibilityReason);
            prepared[i] = new(i, action.Name, raw, raw > 0 ? action.ForecastTicks() : 0,
                action.IsExcursion, action.ActivityTarget != null, action is KeepCompany, action == Current, action.Eligibility,
                action.Family == PurposeFamily.Combat);
            bindings[i] = ValidatePreparedActivity.Capture(action);
        });
        for (int i = 0; i < Actions.Count; i++)
        {
            if (ran[i]) continue;
            // Not prepared: its retained discovery is neither compared nor allowed to carry value,
            // and the row says why rather than reading as an absent opportunity.
            offers[i] = (OfferEligibility.Deferred, "family-preparation-allowance-spent");
            prepared[i] = new(i, Actions[i].Name, 0, 0, Actions[i].IsExcursion, false, false, false, OfferEligibility.Deferred);
        }
        var comparison = ComparisonContext(ctx);
        var available = (PreparedActivity[])prepared.Clone();
        var rejections = new string?[prepared.Length];
        var methods = new string[prepared.Length];
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
                LastScores.Add(new(action, score.Raw, score.Final, score.Protection, score.Commitment, score.Horizon, score.UsefulWork, score.Error, score.Reunion,
                    methods[score.Index] ?? "", offers[score.Index].Eligibility, offers[score.Index].Reason));
                candidates[score.Index] = new(action.Family, score);
            }
            LastNominations = NominateFamilyActivities.Nominate(candidates);
            if (NominateFamilyActivities.Select(LastNominations) is not { } winner) break;
            string reason = bindings[winner.Index].Rejection(Actions[winner.Index]);
            if (reason.Length == 0 && Actions[winner.Index].PreparedPositionRequest is { } request)
            {
                var method = ctx.Companion.Brain.Positioner.PrepareOffer(request, ctx.Senses,
                    ctx.Companion.Arsenal.ProfileFor(ctx, request.Target));
                Infrastructure.Diagnostics.GodsEyeEvents.RecordMethodAssessment(ctx.Npc, request.Target,
                    bindings[winner.Index].Generation, EvaluationId + 1, Actions[winner.Index].Name,
                    Actions[winner.Index].Family.ToString(), request.Kind.ToString(), method.SourceTick,
                    prepared[winner.Index].RawValue, winner.Final, method.Destination, method.Reason, method.Candidates);
                methods[winner.Index] = FormattableString.Invariant(
                    $"tick:{method.SourceTick},kind:{request.Kind},destination:{method.Destination},reason:{method.Reason},candidates:{method.Candidates}");
                // The query answers what preparation left unresolved: a destination makes the offer
                // usable for this comparison, none makes it known-unusable here without blacklisting it.
                offers[winner.Index] = method.Destination == null
                    ? (OfferEligibility.KnownUnusable, "method-" + method.Reason)
                    : (OfferEligibility.Usable, "method-admitted-" + method.Reason);
                int row = LastScores.FindIndex(s => ReferenceEquals(s.Action, Actions[winner.Index]));
                if (row >= 0) LastScores[row] = LastScores[row] with { MethodEvidence = methods[winner.Index],
                    Eligibility = offers[winner.Index].Eligibility, EligibilityReason = offers[winner.Index].Reason };
                if (method.Destination == null) reason = "method-" + method.Reason;
            }
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
            Weights.FollowDuringUsefulWork, Reunion.DelayCostPerTick, ctx.Senses.Encounter.Intensity);
}
