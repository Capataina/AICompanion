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
        float Protection = 1f, float Commitment = 1f, float Horizon = 1f, float UsefulWork = 1f);

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
    public CompanionAction? Current { get; private set; }
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
        CompanionAction? best = Actions[^1];
        float bestScore = 0f;

        foreach (CompanionAction action in Actions)
        {
            float raw = action.Score(ctx);
            float final = raw;
            float protection = 1f, commitment = 1f, horizonFactor = 1f;
            // Optional jobs validate their target against the activity envelope themselves.
            // The short follow comfort band must not veto an admitted, useful excursion.
            if (action.IsExcursion && !ctx.Stranded) protection = 1f - ctx.Senses.Threats.ProtectionUrgency;
            if (raw > 0f)
            {
                if (action == Current)
                    // Earned, not granted. A task whose body has stopped covering ground toward
                    // what it asked for is not owed the benefit of already having started; that is
                    // how one unreachable pot outscored reachable ore for fifty unbroken seconds.
                    commitment = ctx.Companion.Brain.MovementStalled ? Weights.CommitmentWhileStalled : Weights.Commitment;
                // Work is interruptible at the next decision tick. Charging the whole vein
                // against a momentary safety horizon made safe, resumable work impossible.
                float forecast = action.IsExcursion ? Math.Min(Weights.InterruptibleActionTicks, action.ForecastTicks(ctx)) : action.ForecastTicks(ctx);
                if (forecast > horizon)
                {
                    float overrun = forecast - horizon;
                    horizonFactor = Math.Max(0f, 1f - overrun / Weights.HorizonOverrunToZero);
                }
            }
            final = raw * protection * commitment * horizonFactor;
            LastScores.Add(new Scored(action, raw, final, protection, commitment, horizonFactor));
        }

        bool useful = LastScores.Exists(s => s.Action.IsExcursion && s.Action.ActivityTarget != null && s.Final > .1f);
        for (int i = 0; i < LastScores.Count; i++)
        {
            var scored = LastScores[i];
            float final = scored.Final;
            CompanionAction action = scored.Action;
            float usefulWork = 1f;
            if (useful && action is WalkWithPlayerAction
                && ctx.Senses.DistanceToPlayer <= PlayerIntegration.CompanionPreferences.Current.ActiveActivityRadius)
                usefulWork = Weights.FollowDuringUsefulWork;
            final *= usefulWork;
            LastScores[i] = scored with { Final = final, UsefulWork = usefulWork };
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
        // A behaviour can finish one target and select another without losing the tick.
        // Score validates acquisition first; only the selected identity earns continuation.
        best.AdmitActivity();
        return best;
    }
}
