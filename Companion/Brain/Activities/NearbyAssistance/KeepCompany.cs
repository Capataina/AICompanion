#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Activities.NearbyAssistance;

/// <summary>
/// Accompany the player's activity through reunion, resting or nearby movement.
/// These methods share one ordinary purpose rather than competing as separate jobs.
/// </summary>
public sealed class KeepCompany : CompanionAction
{
    public override string Name => "keep-company";
    public override PurposeFamily Family => PurposeFamily.NearbyAssistance;
    public override bool IsExcursion => false;

    private float preparedValue;
    private bool reunite;

    public override void Enter(in ActionContext ctx)
    {
        // A freshly entered activity has no method in flight to protect, so its first preparation
        // adopts whatever the geometry asks for rather than holding the last run's method for a
        // rescore first.
        pendingTicks = Weights.PositionRescoreTicks;
    }

    /// <summary>A meeting place belongs to an ongoing reunion; leaving company must not leave its flood
    /// and reason standing as if another activity were still heading for the player.</summary>
    public override void Exit(in ActionContext ctx)
    {
        ctx.Companion.Brain.Meeting.Release();
        base.Exit(ctx);
    }

    public override void Prepare(in ActionContext ctx)
    {
        float reunionValue = CalculateReunionValue(ctx);
        float localValue = ctx.Senses.Player.IsDead ? 0f : ctx.Stranded ? Weights.StrandedWander : Weights.WanderFloor;
        reunite = ChooseMethod(ctx, reunionValue > localValue);
        preparedValue = MathF.Max(reunionValue, localValue);
        if (ctx.Senses.Player.IsDead) Classify(OfferEligibility.NoOpportunity, "player-dead");
        else Classify(OfferEligibility.Usable, reunite ? "reunion-method" : ctx.Stranded ? "sealed-pocket-local-method" : "local-company-method");
    }
    public override float Score() => preparedValue;

    /// <summary>Company is a way of being with the player rather than a job with a finish line, so
    /// its attempts claim execution and never completion.</summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
        => new(AttemptStatus.Executed, reunite ? "reunion-method-executed" : "local-company-method-executed");

    /// <summary>
    /// Whether the method changes, as opposed to whether it would. A method change is a change of
    /// request kind, and a change of request kind cancels whatever the body is doing — at tick 8128
    /// of the 2026-09-14 capture the flip from reunion to local turned a WithPlayer into a Hold, and
    /// the Hold reached the navigator one tick after take-off and cut the jump. So a regime that
    /// wants the other method has to hold for a rescore. The walker also refused the change while
    /// its body was in the air; the orb is always in the air and drifts even when holding, so that
    /// test went on 15 September 2026 — kept, it would have frozen the method for as long as the
    /// body hovered. Entering the new method costs the wait; there is no wait on the wanted regime
    /// going back to the one in force, because then nothing changes.
    /// </summary>
    private bool ChooseMethod(in ActionContext ctx, bool wantsReunion)
    {
        if (ctx.Senses.Player.IsDead) { pendingTicks = 0; return reunite = wantsReunion; }
        if (wantsReunion == reunite) { pendingTicks = 0; return reunite; }
        // The wait guards a body that is already where it is meant to be. Outside the region there
        // is nothing to protect — a companion that has lost the player, or has just spawned and is
        // still falling, has no business waiting a rescore per tick it stays in the air before it
        // is allowed to go after him — so a change takes effect at once there. Inside, and only
        // inside, the airborne tick is refused: that is where tick 8127's false arrival happened.
        if (!ctx.Senses.Intent.Region.Contains(ctx.Npc.Bottom)) { pendingTicks = 0; return reunite = wantsReunion; }
        // Standing in the tiles the player is asking for is the other case with nothing to protect:
        // the body is in his way now, and a wait measured in rescores is a wait he spends walking
        // into it. The wait exists to keep a move in flight from being cancelled, and a body being
        // asked to move is not a move in flight.
        if (ctx.Senses.Player.Interference is Rectangle asked
            && PlayerSense.BodyTiles(ctx.Npc.Bottom, ctx.Npc.width, ctx.Npc.height).Intersects(asked))
        { pendingTicks = 0; return reunite = wantsReunion; }
        if (++pendingTicks < Weights.PositionRescoreTicks) return reunite;
        pendingTicks = 0;
        return reunite = wantsReunion;
    }

    /// <summary>Starts satisfied, because before the first preparation there is no method in flight to
    /// protect: an activity whose very first tick had to wait a rescore would hold a body that had not
    /// yet been told to do anything. Enter restores it for the same reason.</summary>
    private int pendingTicks = Weights.PositionRescoreTicks;

    /// <summary>
    /// How strongly the geometry alone asks for reunion, as one continuous curve from the region's
    /// centre outward. Pure and internal so the contract can be sampled either side of the region's
    /// edge directly, rather than inferred from a whole-brain walk that would also have to arrange
    /// sight, regrouping and a stranded body to see it.
    ///
    /// <para>Inside, the pull scales with the region's own normalised distance and reaches the
    /// central-pull weight at the edge. A flat zero inside was the whole of the never-overtakes
    /// defect: the box had no gradient, so a moving player was not itself a reason to move, the body
    /// coasted to whichever edge it entered by, and keeping company scored its wander floor where any
    /// rival offer beat it.</para>
    ///
    /// <para>Outside, the slope rises over the same span it always did, but it is measured on how far
    /// beyond the region's edge the body is rather than on how far it is from the player's body. That
    /// is what removes the step. Measured body-to-body the slope was already partway up at the
    /// region's own leading edge, because a companion standing exactly where the region asks is a
    /// lead plus a half-width from the player — so the curve jumped at the one boundary it had to be
    /// continuous across, and the size of the jump grew with the lead, which is to say with the
    /// screen. The two halves now meet at zero-beyond-the-edge, where the inside gradient is at its
    /// largest, so <c>max</c> of the two is continuous rather than a choice between two regimes; the
    /// ternary that used to pick between them is gone because there is nothing left to pick.</para>
    /// </summary>
    public static float GeometricPull(in FollowPlayerObjective objective, Vector2 feet, bool travelling)
    {
        float inner = MathF.Max(objective.HorizontalComfort, objective.VerticalComfort);
        float outer = MathF.Max(inner + 1f, PlayerIntegration.CompanionPreferences.Current.RecoveryRadius);
        float central = travelling ? Weights.IntentRegionCentralPull * MathF.Min(1f, objective.Pull(feet)) : 0f;
        float far = Consideration.Rising(objective.GapBeyond(feet), outer - inner);
        return MathF.Max(central, far);
    }

    private float CalculateReunionValue(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsDead)
            return 0f;
        var objective = ctx.Senses.Intent.Objective;
        float hardLeash = Consideration.Step(ctx.Senses.DistanceToPlayer > Weights.LeashHard, 1f, 0f);
        float stranded = ctx.Stranded ? Weights.StrandedFollowDiscount : 1f;
        float regroup = ctx.Companion.Brain.Chooser.RegroupUrgency;
        bool seen = global::AICompanion.Companion.Brain.Infrastructure.Observation.LineOfSight.Between(ctx.Npc, ctx.Player);
        bool blocking = p.Interference is Rectangle footprint
            && PlayerSense.BodyTiles(ctx.Npc.Bottom, ctx.Npc.width, ctx.Npc.height).Intersects(footprint);
        float pull = GeometricPull(objective, ctx.Npc.Bottom, p.IsTravelling);
        if (!seen)
            pull = MathF.Max(pull, 0.3f);
        // Occupying a passage the player is walking is not "already with them": the slope from the
        // comfort box stays at zero while they overlap, and resting would park in the way. A still
        // player aiming a block is the other courtesy, and that one steps aside without a reunion.
        if (blocking && p.IsTravelling)
            pull = MathF.Max(pull, 0.3f);
        float demand = MathF.Min(Weights.KeepCompanyFarCap, MathF.Max(pull, regroup));
        return MathF.Max(demand, hardLeash) * stranded;
    }

    public override PositionRequest Execute(in ActionContext ctx)
    {
        ctx.Companion.HoldItem(ItemID.None);
        var p = ctx.Senses.Player;
        if (p.IsDead) return PositionRequest.Hold;
        if (reunite)
        {
            // Reunion aims at a place on the player's apparent journey that the companion's own
            // routes reach, not at a point extrapolated from velocity; a paused or working player
            // is met where they stand.
            var meeting = ctx.Companion.Brain.Meeting;
            // The centre, because that is what the parameter is: the resolver roots its own free-space flood at
            // `NearestUsableCorner` of the point handed in. `Bottom` on this body is the centre plus a radius,
            // which for a companion resting one radius clear of a floor is the floor line itself — so the root
            // was chosen around a point on or inside terrain, the flood grew from wherever that landed, and it
            // finished having reached none of the tiles along the player's walk. A finished flood with no
            // candidate reached is reported as a proven absence, so the refusal read `no-reachable-meeting-place`
            // on open ground with the player walking straight at the companion, and reunion fell back to the
            // region's leading edge on every tick of the journey it exists to price.
            meeting.Resolve(ctx.Npc.Center, p, ctx.Senses.Intent.Region, Main.GameUpdateCount);
            return new PositionRequest(RequestKind.WithPlayer, meeting.Destination, MeetingPlace: meeting.HasPlace);
        }
        ctx.Companion.Brain.Meeting.Release();
        if (ctx.Stranded) return new PositionRequest(RequestKind.Roam, ctx.Npc.Bottom);
        // Courtesy. A body over the tiles the player is building on or walking down hands the choice of spot to ordinary follow
        // selection near the player, which prices spots overlapping that footprint down. Only company yields: work and protection
        // keep their spot, which is what pricing courtesy against them means, and a body that overlaps nothing is left where it is.
        if (p.Interference is Rectangle footprint
            && PlayerSense.BodyTiles(ctx.Npc.Bottom, ctx.Npc.width, ctx.Npc.height).Intersects(footprint))
            return new PositionRequest(RequestKind.WithPlayer, p.Bottom);
        // Local company is a hold, and the brain's hold is a hover: the body drifts around where it is. A stroll picker lived here
        // until 15 September 2026 — a random safe cell every one to four seconds, a third of the picks a rest, every arrival a
        // brake to zero — and it went with the owner's ruling that the orb is never strictly standing still, because the hover the
        // movement system now gives every held spot is the motion the strolls were for, without a destination to reach and stop on.
        return PositionRequest.Hold;
    }
}
