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
        var objective = ctx.Senses.Intent.Objective;
        Reunion.Observe(Terraria.Main.GameUpdateCount,
            objective.IsSatisfied(ctx.Npc.Center, ctx.Senses.Player.CompanionCanSeePlayer), ctx.Senses.Player.IsDead);
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
        // Reunion, excursion and return cost are all "how far from the player", and they measure to
        // the intent region's centre: a companion pricing its way back to where the player was
        // standing prices a trip that is already out of date on a player who is walking.
        var region = ctx.Senses.Intent.Region;
        var delta = region.Centre - ctx.Npc.Center;
        EstimatedReturnTicks = delta.Length() / Infrastructure.Movement.OrbPace.MaxSpeed;
        var navigator = ctx.Companion.Brain.Navigator;
        // The route home is priced to the cell at the region's centre, which is air a third of the box
        // above the player's centre rather than his feet, so it is the plain tile. It was `FeetTile` while
        // the centre was his feet, because the tile feet floor into is the solid floor row the flood never
        // holds, and priced to that the estimate was null and the straight line silently won every tick.
        if (ctx.Companion.Brain.Positioner.EstimatedTravelTicks(Infrastructure.Movement.MovementQueries.Tile(ctx.Npc.Center),
            Infrastructure.Movement.MovementQueries.Tile(region.Centre)) is float knownTravel)
            EstimatedReturnTicks = MathF.Max(EstimatedReturnTicks, knownTravel);
        if (ctx.Companion.Brain.LastRequest.Kind is Infrastructure.Position.RequestKind.WithPlayer or Infrastructure.Position.RequestKind.Guard
            && navigator.Path != null)
            EstimatedReturnTicks = MathF.Max(EstimatedReturnTicks, navigator.RemainingEstimatedRouteTicks);
        float movingAway = delta.LengthSquared() > 1f ? Microsoft.Xna.Framework.Vector2.Dot(ctx.Senses.Player.Intent, Microsoft.Xna.Framework.Vector2.Normalize(delta)) : 0f;
        Reunion.Evaluate(movingAway, EstimatedReturnTicks, ctx.Senses.Player.IsDead, ctx.Stranded);
        // Regrouping is measured on the gap beyond the region's rectangle, like every other separation, so it is exactly
        // zero anywhere inside the region: the owner ruled there is no pull there, whatever the body is doing and whatever a
        // route round a thin wall would cost. The travel pressure below is gated the same way for the same reason.
        float gapBeyond = region.GapBeyond(ctx.Npc.Center);
        bool insideRegion = gapBeyond <= 0f;
        RegroupUrgency = ctx.Senses.Player.IsDead ? 0f : Infrastructure.Observation.CalculateRegroupUrgency.Evaluate(
            gapBeyond, EstimatedReturnTicks, movingAway, navigator.StuckTicks, MathF.Max(region.HalfSize.X, region.HalfSize.Y),
            Weights.RegroupFullDistance, Weights.RegroupFreeReturnTicks, Weights.RegroupFullReturnTicks);
        if (!ctx.Senses.Player.IsDead && !insideRegion)
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
            float forecast = raw > 0 ? action.ForecastTicks() : 0;
            float taskTicks = raw > 0 ? action.TaskTicks() : 0;
            var site = action.ActivityTarget;
            float duration = MathF.Max(forecast, taskTicks);
            float fit = raw > 0 && site is { } place ? PlayerFit(context, place, duration) : 1f;
            // Where the job keeps the body: an excursion's site, or where the body already is for a job done from here —
            // a hunt shooting from where the orb hovers pays for being outside the region only if the orb is.
            float separation = raw > 0 && site is { } at
                ? Separation(context, action.IsExcursion ? at : context.Npc.Center, duration) : 1f;
            prepared[i] = new(i, action.Name, raw, forecast,
                action.IsExcursion, site != null, action is KeepCompany, action == Current, action.Eligibility,
                action.Family == PurposeFamily.Combat, taskTicks, site, fit, action.ServesPlayerDirectly, separation);
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
            var ordered = OrderNearbyTasks.Apply(EvaluatePreparedActivities.Evaluate(available, comparison), available,
                ctx.Npc.Center, Infrastructure.Movement.OrbPace.MaxSpeed, comparison.TaskWindowTicks,
                Weights.TaskOrderShare, Weights.TaskOrderMaximum);
            var evaluated = ordered.Evaluated;
            LastTaskOrder = ordered.Order;
            LastTaskOrderRunnerUp = ordered.RunnerUp;
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
                // The query answers what preparation left unresolved: a destination makes the offer usable for this
                // comparison, none makes it known-unusable here without blacklisting it — except where the reason
                // says the bounded search was cut rather than answered. A budget that ran out establishes nothing
                // about the candidates it never reached, and branding that impossible is what let a hunt be vetoed
                // by its own solve count and the companion flip to keeping company on the tick the budget expired.
                offers[winner.Index] = method.Destination == null
                    ? (method.Undecided ? OfferEligibility.Unresolved : OfferEligibility.KnownUnusable, "method-" + method.Reason)
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
            // To the region's centre, not the player's body: a radius anchored on where he is
            // standing shrinks the ground in front of him as he walks into it.
            Microsoft.Xna.Framework.Vector2.Distance(ctx.Npc.Bottom, ctx.Senses.Intent.Region.Centre)
                <= PlayerIntegration.CompanionPreferences.Current.ActiveActivityRadius,
            Weights.FollowDuringUsefulWork, ctx.Senses.Encounter.Intensity,
            Weights.TaskWindowTicks);

    /// <summary>The order the last comparison put its close jobs in, and the best order that started differently,
    /// each as names joined by '>' with the order's score; empty when fewer than two jobs were close.</summary>
    public string LastTaskOrder { get; private set; } = "";
    public string LastTaskOrderRunnerUp { get; private set; } = "";

    /// <summary>
    /// Whether a job at <paramref name="site"/> will still be inside the player's work allowance by the time it is
    /// done: one inside the new-job radius of where his region is heading, nothing beyond the started-job radius, and a
    /// straight fall between. Measured to the region's centre carried forward by his observed travel, the same anchor
    /// the allowance itself reads, so a job the allowance admits now scores one until his heading takes it away.
    /// </summary>
    public static float PlayerFit(in ActionContext ctx, Microsoft.Xna.Framework.Vector2 site, float ticks)
    {
        if (ctx.Senses.Player.IsDead) return 1f;
        var preferences = PlayerIntegration.CompanionPreferences.Current;
        var projected = ctx.Senses.Intent.Region.Heading + ctx.Senses.Player.Intent * MathF.Min(ticks, Weights.PlayerProjectionCapTicks);
        return FitAt(Microsoft.Xna.Framework.Vector2.Distance(site, projected), preferences.NewActivityRadius, preferences.ActiveActivityRadius);
    }

    /// <summary>
    /// The share of a job's worth left after paying for keeping the companion apart: one minus the pull beyond the player's
    /// region at the job's stand — keeping company's own slope, uncapped — measured against the region carried along his
    /// observed travel for the job's duration. So a stand inside the region pays nothing, a stand beyond it pays by the gap,
    /// a job whose stand the player's travel will leave at fly-home distance is worth nothing, and a player leaving makes a
    /// long job pay more than a quick one. It is the one separation cost; the reunion delay charge it replaced priced the
    /// same separation a second way and is still computed only because the recorder writes it.
    /// </summary>
    public static float Separation(in ActionContext ctx, Microsoft.Xna.Framework.Vector2 stand, float ticks)
    {
        if (ctx.Senses.Player.IsDead || ctx.Stranded) return 1f;
        return SeparationAt(ctx.Senses.Intent.Region, ctx.Senses.Player.Intent, stand, ticks);
    }

    /// <summary>The same share from the numbers it reads, so a comparison can be checked against a region built by hand.</summary>
    public static float SeparationAt(in Infrastructure.Observation.PlayerIntentRegion region, Microsoft.Xna.Framework.Vector2 playerTravel,
        Microsoft.Xna.Framework.Vector2 stand, float ticks)
    {
        var shift = playerTravel * MathF.Min(ticks, Weights.PlayerProjectionCapTicks);
        var projected = region with { Centre = region.Centre + shift, Heading = region.Heading + shift };
        return 1f - KeepCompany.PullBeyond(projected, stand);
    }

    public static float FitAt(float distance, float near, float far)
        => distance <= near ? 1f : distance >= far ? 0f : 1f - (distance - near) / MathF.Max(1f, far - near);
}
