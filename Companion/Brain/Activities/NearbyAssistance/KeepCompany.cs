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
/// Accompany the player: rejoin his region from outside it, and move about it with him from inside it.
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
    /// Which method is in force, decided by where the body is: outside the player's region it rejoins the region, inside it
    /// moves about the region with him. A stranded body rejoins only where rejoining is worth more than roaming its pocket,
    /// because a sealed pocket has no route home to ask for.
    ///
    /// <para>This was a comparison of rejoining's worth against the wander floor, guarded by a rescore's wait, an arrival test
    /// and a check that the region had stopped sliding, and every one of those guards existed because the two methods asked
    /// for different kinds of request and a change of kind cut whatever the body was doing: at tick 8128 of the 2026-09-14
    /// capture a flip from reunion to local turned a follow into a Hold that cut a jump one tick after take-off, and on
    /// 15 September 2026 at three times the player's speed the flip happened thirty-nine times in six hundred ticks. Both
    /// methods now ask for the same kind and differ only in whether the body is inside, so a change cuts nothing and there
    /// is nothing to wait for; the sense's inside latch gives the edge the width the wait used to give it.</para>
    /// </summary>
    private static bool ChooseMethod(in ActionContext ctx, bool rejoiningIsWorthMore)
        => !ctx.Senses.Player.IsDead && !ctx.Senses.Intent.Inside && (!ctx.Stranded || rejoiningIsWorthMore);

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
    public static float PullBeyond(in PlayerIntentRegion region, Vector2 point) => PullAtGap(region, region.GapBeyond(point));

    /// <summary>The same slope read at a distance the caller measured, so a job's separation priced along the route home climbs
    /// the one slope keeping company climbs rather than a second one.</summary>
    public static float PullAtGap(in PlayerIntentRegion region, float gap)
        => Infrastructure.Selection.Courses.MeasureCompanionshipGap.Pull(gap, region.HalfSize.X, region.HalfSize.Y,
            PlayerIntegration.CompanionPreferences.Current.RecoveryRadius);

    /// <summary>
    /// Rejoining's value: the larger of the pull and the regroup urgency, capped, beside the separate hard leash at fly-home
    /// distance that still takes everything. Losing sight of the player and sitting in a passage he walks down each used to
    /// floor this at 0.3; both were hand-written stand-ins for distance, and neither is distance — a companion behind a
    /// pillar inside his region is with him, and courtesy is answered where the companion moves inside the region.
    /// </summary>
    private float CalculateReunionValue(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsDead)
            return 0f;
        float hardLeash = Consideration.Step(ctx.Senses.DistanceToPlayer > Weights.LeashHard, 1f, 0f);
        float stranded = ctx.Stranded ? Weights.StrandedFollowDiscount : 1f;
        float regroup = ctx.Companion.Brain.Companionship.RegroupUrgency;
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
            // region on every tick of the journey it exists to price.
            meeting.Resolve(ctx.Npc.Center, p, ctx.Senses.Intent.Region, Main.GameUpdateCount);
            return new PositionRequest(RequestKind.WithPlayer, meeting.Destination, MeetingPlace: meeting.HasPlace);
        }
        ctx.Companion.Brain.Meeting.Release();
        if (ctx.Stranded) return new PositionRequest(RequestKind.Roam, ctx.Npc.Bottom);
        // Inside the region, with him. The request is the kind rejoining asks for, aimed at the region's centre.
        // Navigate then accompanies across the box; the walk prefers combined wall-and-enemy clearance among its
        // own steps. The positioner still names a clearest park, and that park is not what the body flies.
        return new PositionRequest(RequestKind.WithPlayer, ctx.Senses.Intent.Region.Centre);
    }
}
