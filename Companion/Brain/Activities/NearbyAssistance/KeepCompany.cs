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
    public override bool ServesPlayerDirectly => true;

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
        // Reunion hands over to local company when the body has arrived, not when it is merely inside, and not at once when it
        // is outside: arrived is the region's own settled state, at rest inside for a rescore, which already carries the wait
        // below. It stands ahead of the outside exemption, because that exemption is for going after a player the body has lost,
        // and a player who has stopped leaves a band just past the region's edge where the far slope has barely risen above the
        // wander floor — local company won there, was granted at once for being outside, and the drifting region left the
        // hovering body at its edge. Being inside was the whole test until 15 September 2026, and at three times the player's
        // speed a reunion crossed the edge at two to three pixels a tick, the hold was issued mid-flight and the momentum coasted
        // the body back out: thirty-nine method changes in six hundred ticks in VerifyResponsiveFollowing's settle row, nine
        // with the settled test placed after the exemption, where the row's own trace showed every one outside at a pull of 1.01.
        // A stranded body has no region to arrive in, so waiting for arrival would hold it in reunion for ever; it roams at once.
        // A region still sliding back onto a player who has stopped is not yet a place to arrive in either: a body settled inside
        // it and held still is outside it a second later, which was the last of the settle row's changes once the others were
        // gone. So arrival also needs the region's centre moving slower than a settled body moves.
        if (reunite && !wantsReunion && !ctx.Stranded)
        {
            pendingTicks = 0;
            bool placeHasStopped = ctx.Senses.Intent.CentreSpeed <= Weights.SettledSpeedPx;
            return reunite = !(ctx.Senses.Intent.Settled && placeHasStopped);
        }
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
    /// How strongly a place outside the player's region asks to be with him again: exactly zero anywhere inside the
    /// rectangle, rising with the gap beyond its edge over the span out to fly-home distance, and capped at
    /// <see cref="Weights.KeepCompanyFarCap"/>. Pure and public because it is one pull in two uses — keeping company's
    /// rejoin value at the body, and the separation every job pays at its stand — and a second copy of the curve is how
    /// the two would drift apart.
    ///
    /// <para>There is no pull inside the region, and that is the owner's ruling rather than an absence of design. The inside
    /// gradient that stood here until 15 September 2026 existed because the old box was where a body arrived and stopped, so
    /// a travelling player had to be a reason to move; the region is now where the companion lives and moves about, carried
    /// by the region's own velocity, so a pull inside it would only drag a busy companion toward a centre nobody asked it to
    /// sit at. Measured on the gap beyond the edge, the curve starts at zero where the region ends and has no step there.</para>
    /// </summary>
    public static float RejoinPull(in PlayerIntentRegion region, Vector2 point)
        => MathF.Min(Weights.KeepCompanyFarCap, PullBeyond(region, point));

    /// <summary>
    /// The slope under <see cref="RejoinPull"/> before its cap: zero anywhere inside the region, rising with the gap beyond
    /// its edge, and reaching one at fly-home distance beyond it, where the companion would lose the player altogether. The
    /// cap belongs to rejoining's value and not to the distance: a job's separation reads this uncapped, because the ruling
    /// that rejoining never on its own outweighs a job genuinely worth doing is about what rejoining is worth, while the
    /// ruling that the pull takes all of the companion's attention at the distance where it would lose him is about how far
    /// is far. Capped for the job as well, a fight the player has dropped four hundred pixels away from lost only half its
    /// worth, and the discount on following while useful work exists took four fifths of rejoining's, so the orb stayed.
    /// </summary>
    public static float PullBeyond(in PlayerIntentRegion region, Vector2 point)
    {
        float inner = MathF.Max(region.HalfSize.X, region.HalfSize.Y);
        float span = MathF.Max(1f, PlayerIntegration.CompanionPreferences.Current.RecoveryRadius - inner);
        return Consideration.Rising(region.GapBeyond(point), span);
    }

    /// <summary>
    /// Rejoining's value: the larger of the pull and the regroup urgency, capped, beside the separate hard leash at fly-home
    /// distance that still takes everything. Losing sight of the player and sitting in a passage he walks down each used to
    /// floor this at 0.3; both were hand-written stand-ins for distance, and neither is distance — a companion behind a
    /// pillar inside his region is with him, and courtesy is answered where the companion's place in the region is chosen.
    /// </summary>
    private float CalculateReunionValue(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsDead)
            return 0f;
        float hardLeash = Consideration.Step(ctx.Senses.DistanceToPlayer > Weights.LeashHard, 1f, 0f);
        float stranded = ctx.Stranded ? Weights.StrandedFollowDiscount : 1f;
        float regroup = ctx.Companion.Brain.Chooser.RegroupUrgency;
        // The body's centre, because the orb is its centre; the walker's feet point sat a radius under it.
        float pull = RejoinPull(ctx.Senses.Intent.Region, ctx.Npc.Center);
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
