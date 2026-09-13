#nullable enable
using FindToolAccess = AICompanion.Companion.Brain.WorldInteractions.FindToolAccess;
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.PositionSelection;
using AICompanion.Companion.Brain.SharedMovementSystem;

using AICompanion.Companion.Brain.Behaviours;

namespace AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance;

/// <summary>One bounded interaction search shared by pot breaking and permanent lighting.</summary>
public abstract class PerformNearbyWorldWork : CompanionAction
{
    public override BehaviourSelection.PurposeFamily Family => BehaviourSelection.PurposeFamily.NearbyAssistance;
    protected Point? target;
    private Vector2 stand;
    private ulong nextSearch, retryAfter;
    private bool eligibleLastObservation;
    private bool needsJump, jumped;
    private ulong jumpStarted;
    // Tiles whose approach was tried and did not arrive, with the tick they may be offered again.
    private readonly System.Collections.Generic.Dictionary<Point, ulong> deferred = new();
    private Vector2 approachOrigin;
    private int approachTicks;
    private Vector2? preparedTarget;
    private float preparedValue;
    private bool enabledAtPreparation;
    // The last way this method gave up on a target during the current attempt, so the attempt is
    // concluded from what happened to it rather than from the absence of a target afterwards.
    private string? release;
    public override Vector2? ActivityTarget => preparedTarget;
    public override object? ActivityIdentity => target;
    protected abstract bool Enabled(in ActionContext ctx);
    protected abstract bool Candidate(in ActionContext ctx, Point tile);
    protected abstract bool Perform(in ActionContext ctx, Point tile);
    protected abstract float Utility { get; }
    protected virtual bool AllowJump => false;
    /// <summary>The classification for a method its enabling conditions currently refuse; the
    /// subclass knows whether that refusal is a player setting, missing supplies or the world.</summary>
    protected virtual (OfferEligibility Eligibility, string Reason) DisabledOffer(in ActionContext ctx)
        => ctx.Player.dead ? (OfferEligibility.NoOpportunity, "player-dead") : (OfferEligibility.PolicyForbidden, "interaction-disabled");
    /// <summary>What one observed productive interaction means for this purpose.</summary>
    protected virtual string CompletedEffect => "interaction-effect-observed";
    /// <summary>How long a tile whose approach never arrived stays out of the candidate set. Long
    /// enough that the companion leaves the area and does something else, short enough that a tile
    /// made reachable by the player digging through becomes available again in the same visit.</summary>
    private const int DeferFailedApproachTicks = 1800;
    protected virtual float CandidateCost(Vector2 feet, Point tile) => Vector2.DistanceSquared(feet, tile.ToWorldCoordinates());

    public override void Prepare(in ActionContext ctx)
    {
        preparedValue = DiscoverValue(ctx);
        preparedTarget = target?.ToWorldCoordinates();
        if (!enabledAtPreparation) { var (eligibility, reason) = DisabledOffer(ctx); Classify(eligibility, reason); }
        else if (target == null) Classify(OfferEligibility.NoOpportunity, "no-candidate-in-search-window");
        else Classify(OfferEligibility.Usable, "reachable-interaction");
    }

    public override float Score() => preparedValue;

    /// <summary>One interaction is this purpose's whole job, so an observed productive effect
    /// completes it; a named give-up is a failed method unless the target itself stopped qualifying.</summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
    {
        // The companion's own native call produced the credited effect, so the completion is its own.
        if (productiveEffects > 0) return new(AttemptStatus.Complete, CompletedEffect, AttemptAttribution.Companion);
        if (release is string reason)
            return new(reason == "target-no-longer-candidate" ? AttemptStatus.Invalid : AttemptStatus.Failed, reason);
        return new(AttemptStatus.Attempted, "replaced-before-interaction");
    }

    public override void BeginAttempt() => release = null;

    private void Release(string reason) => release = reason;

    private bool RefreshEligibility(in ActionContext ctx)
    {
        if (!Enabled(ctx) || ctx.Player.dead)
        {
            target = null;
            eligibleLastObservation = false;
            return false;
        }
        // Changed policy or supplies invalidate a previously unavailable discovery result.
        // An unchanged eligible method still observes the normal bounded search cadence.
        if (!eligibleLastObservation) nextSearch = 0;
        eligibleLastObservation = true;
        return true;
    }

    private float DiscoverValue(in ActionContext ctx)
    {
        enabledAtPreparation = RefreshEligibility(ctx);
        if (!enabledAtPreparation) return 0f;
        if (target is Point old && (!Candidate(ctx, old) || !AllowsTarget(ctx, old.ToWorldCoordinates())))
        { target = null; }
        if (target == null && Main.GameUpdateCount >= nextSearch)
        {
            nextSearch = Main.GameUpdateCount + 90;
            float best = float.MaxValue;
            Point centre = ctx.Npc.Center.ToTileCoordinates();
            for (int x = centre.X - 18; x <= centre.X + 18; x++)
                for (int y = centre.Y - 14; y <= centre.Y + 14; y++)
                {
                    Point p = new(x, y);
                    float distance = CandidateCost(ctx.Npc.Bottom, p);
                    if (distance >= best) continue;
                    if (deferred.TryGetValue(p, out ulong until) && Main.GameUpdateCount < until) continue;
                    if (!AllowsTarget(ctx, p.ToWorldCoordinates(), p) || !Candidate(ctx, p)) continue;
                    Vector2 candidateStand;
                    bool jump = false;
                    if (FindToolAccess.InReach(ctx.Npc.Bottom, p)) candidateStand = ctx.Npc.Bottom;
                    else if (AllowJump && ProveInteractionJump.CanReach(NavGrid.World, ctx.Companion.Motor.State, body => FindToolAccess.InReach(body.Feet, p)))
                    { candidateStand = ctx.Npc.Bottom; jump = true; }
                    else if (FindToolAccess.Approach(p, ctx.Npc.Bottom, out candidateStand) != Reachability.Reach.Yes) continue;
                    target = p; best = distance; stand = candidateStand; needsJump = jump; jumped = false;
                    approachOrigin = ctx.Npc.Bottom; approachTicks = 0;
                }
        }
        if (deferred.Count > 0)
        {
            ulong now = Main.GameUpdateCount;
            foreach (Point expired in new System.Collections.Generic.List<Point>(deferred.Keys))
                if (now >= deferred[expired]) deferred.Remove(expired);
        }
        return target == null ? 0f : Utility;
    }
    public override PositionRequest Execute(in ActionContext ctx)
    {
        ctx.Companion.HoldItem(ItemID.None);
        if (!RefreshEligibility(ctx)) return PositionRequest.Hold;
        if (target is not Point tile) return PositionRequest.Hold;
        if (!Candidate(ctx, tile)) { target = null; Release("target-no-longer-candidate"); return PositionRequest.Hold; }
        if (!FindToolAccess.InReach(ctx.Npc.Bottom, tile))
        {
            if (!needsJump)
            {
                // The approach can fail, and until this existed nothing said so. Score() only ever
                // dropped a target that vanished or left the activity envelope, so a cached stand
                // the body could not walk to was held for ever: on 2026-09-11 that was ticks 18,501
                // to 21,531 on one pot, 3,031 unbroken ticks with the movement system reporting
                // itself stalled on 1,826 of them, which was 97% of every stalled tick in the run.
                // An intent that cannot fail is an intent that cannot be given up, so covering no
                // ground for a full progress window defers this tile and hands the tick back.
                if (Vector2.DistanceSquared(approachOrigin, ctx.Npc.Bottom)
                    >= BehaviourSelection.Weights.ObjectiveProgressPixels * BehaviourSelection.Weights.ObjectiveProgressPixels)
                { approachOrigin = ctx.Npc.Bottom; approachTicks = 0; }
                else if (++approachTicks >= BehaviourSelection.Weights.ObjectiveProgressWindowTicks)
                {
                    deferred[tile] = Main.GameUpdateCount + DeferFailedApproachTicks;
                    BehaviourDiagnostics.GodsEyeEvents.RecordWorldInteraction(ctx.Npc, tile, "approach-abandoned",
                        $"no ground covered in {BehaviourSelection.Weights.ObjectiveProgressWindowTicks} ticks");
                    target = null; nextSearch = Main.GameUpdateCount + 60;
                    Release("approach-made-no-progress");
                    return PositionRequest.Hold;
                }
                return PositionRequest.ExactAt(stand);
            }
            if (!jumped)
            {
                // Validate against the live pose again: another behaviour may have moved us
                // after candidate discovery. The shared movement controller owns the impulse.
                if (!ProveInteractionJump.CanReach(NavGrid.World, ctx.Companion.Motor.State, body => FindToolAccess.InReach(body.Feet, tile)))
                { target = null; Release("interaction-jump-lost-take-off"); return PositionRequest.Hold; }
                jumped = true; jumpStarted = Main.GameUpdateCount;
                return PositionRequest.Hold with { JumpScale = 1f };
            }
            if (Main.GameUpdateCount - jumpStarted > 90) { target = null; Release("interaction-jump-did-not-deliver"); }
            return PositionRequest.Hold;
        }
        if (Main.GameUpdateCount >= retryAfter)
        {
            retryAfter = Main.GameUpdateCount + 30;
            if (Perform(ctx, tile)) ctx.Companion.Brain.Chooser.RecordWork(tile.ToWorldCoordinates());
            else Release("native-interaction-refused");
            target = null; nextSearch = Main.GameUpdateCount + 60;
        }
        return PositionRequest.Hold;
    }

    public override void Exit(in ActionContext ctx)
    {
        // A reflex or protective action can change a take-off pose; never resume its old jump.
        if (needsJump) { target = null; }
    }
}
