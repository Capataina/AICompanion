#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// Where the player is going, as a place rather than a point: a box that holds the player, sits mostly above him, and leans
/// the way his own observed pace is carrying him. It is where the companion lives while it keeps him company, and what every
/// "how far apart are we" question measures to.
///
/// <para>The player is not at the box's centre. His centre sits at the centre of the box's bottom third, so with no lead the
/// box's centre is a third of its height above him: a drone idles mostly above the person it goes with, underground as much
/// as on the surface, because above is where it is out of his way and in his sight. The lead then moves the box within the
/// limits that keep him inside it, which is a small allowance upward and a large one downward — the geometry is written out
/// at <see cref="LeadLimits"/> — so however far and fast he travels, falls or climbs, he is always inside his own region.
/// Before 15 September 2026 the lead was clamped to half the screen instead, which deliberately let the box leave him behind:
/// on the 14:16 capture of that day he stood outside it on 14.9% of rows and on 37% of the rows he was falling.</para>
///
/// <para><see cref="Centre"/> is the box's centre and is what the companion lives around. <see cref="Heading"/> is the player's
/// own centre carried by the same lead, and it is what a radius measured "near where he is going" reads: a work radius
/// centred on a point five tiles over his head would favour work above him for no reason the owner gave.</para>
/// </summary>
public readonly record struct PlayerIntentRegion(Vector2 Centre, Vector2 HalfSize, Vector2 Lead, bool IsTravelling)
{
    private readonly Vector2? heading;

    /// <summary>The player's centre carried by the applied lead: where he is going, as a point. A region built without one — a
    /// fixture's literal box — reads its own centre.</summary>
    public Vector2 Heading { get => heading ?? Centre; init => heading = value; }

    /// <summary>How the box moved on its last update, in pixels a tick: the player's smoothed travel plus the change in the
    /// applied lead. It is the frame a companion moving about inside the region moves in, so a body that asks for this
    /// velocity and nothing else stays where it is in the box while the box carries it along.</summary>
    public Vector2 Velocity { get; init; }

    /// <summary>Whether a point is in the region. Both axes, never a radius, because the two axes are deliberately different
    /// sizes and a point on a different cave floor directly below is not being with the player.</summary>
    public bool Contains(Vector2 point) => Pull(point) <= 1f;

    /// <summary>
    /// The normalised Chebyshev distance from the box's centre: zero at the centre, one at the edge, rising beyond. A continuous
    /// scalar for readers that want "how far through the box", which is not a demand to move: the owner ruled that there is no
    /// pull anywhere inside the region, and the reunion pull is measured on <see cref="GapBeyond"/>.
    /// </summary>
    public float Pull(Vector2 point) => MathF.Max(
        MathF.Abs(point.X - Centre.X) / MathF.Max(1f, HalfSize.X),
        MathF.Abs(point.Y - Centre.Y) / MathF.Max(1f, HalfSize.Y));

    /// <summary>
    /// Whether a point is inside the region by at least <paramref name="arrivalSlack"/> on both axes. A destination is admitted
    /// against this rather than <see cref="Contains"/>, because a body hovering around a point on the boundary spends half its
    /// orbit outside the region that admitted it.
    /// </summary>
    public bool Accepts(Vector2 feet, float arrivalSlack)
        => MathF.Abs(feet.X - Centre.X) <= MathF.Max(0f, HalfSize.X - arrivalSlack)
            && MathF.Abs(feet.Y - Centre.Y) <= MathF.Max(0f, HalfSize.Y - arrivalSlack);

    /// <summary>
    /// How far outside the region a point is, in pixels, and exactly zero anywhere inside it: the larger of the two axes'
    /// overshoots, for the same reason <see cref="Pull"/> is Chebyshev. Every separation the brain charges is measured on this,
    /// so it starts at zero where the region ends and there is no step at the edge.
    /// </summary>
    public float GapBeyond(Vector2 point) => MathF.Max(0f, MathF.Max(
        MathF.Abs(point.X - Centre.X) - HalfSize.X,
        MathF.Abs(point.Y - Centre.Y) - HalfSize.Y));

    /// <summary>The nearest point to <paramref name="point"/> that is inside the region by <paramref name="inset"/> on both
    /// axes: the point itself when it already is.</summary>
    public Vector2 NearestInside(Vector2 point, float inset)
    {
        Vector2 room = new(MathF.Max(0f, HalfSize.X - inset), MathF.Max(0f, HalfSize.Y - inset));
        return new Vector2(Math.Clamp(point.X, Centre.X - room.X, Centre.X + room.X),
            Math.Clamp(point.Y, Centre.Y - room.Y, Centre.Y + room.Y));
    }

    /// <summary>The half-size with no lead: the follow comfort, scaled by the player's distance preference and by the base scale.</summary>
    public static Vector2 BaseHalfSize(float comfortScale)
        => new Vector2(Weights.FollowHorizontalComfort, Weights.FollowVerticalComfort) * comfortScale * Weights.IntentRegionBaseScale;

    /// <summary>
    /// How far the player's centre sits below the box's centre: two thirds of the half-height, which puts him at the centre of
    /// the bottom third, unless the box is too short for that to leave him inside by the slack, when he sits as low as the
    /// slack allows. The second case is the Close distance mode, where a third of the half-height is under the slack.
    /// </summary>
    public static float PlayerBelowCentre(Vector2 halfSize, float slack)
        => MathF.Min(halfSize.Y * 2f / 3f, MathF.Max(0f, halfSize.Y - slack));

    /// <summary>
    /// How far the lead may carry the box. Vertically the player stays inside by the slack: with him <c>p</c> below the
    /// centre of a box of half-size <c>h</c>, <c>p − (h.y − s) ≤ lead.y ≤ p + (h.y − s)</c>, which at the bottom third is
    /// a small lift of <c>h.y/3 − s</c> and a long drop of <c>5h.y/3 − s</c>. Horizontally the player clamps to the two
    /// verticals that split the box into three equal strips, not to the left and right edges: walking right puts him on
    /// the left third-line (<c>|lead.x| ≤ h.x/3</c>), walking left on the right one, so two thirds of the box stays ahead
    /// and the box cannot run out to the far edge. Screen y grows downward, so up is negative. A limit that would be
    /// negative is zero: the box cannot lead that way at all.
    /// </summary>
    public static (float Across, float Up, float Down) LeadLimits(Vector2 halfSize, float slack)
    {
        float below = PlayerBelowCentre(halfSize, slack);
        float room = MathF.Max(0f, halfSize.Y - slack);
        return (MathF.Max(0f, halfSize.X / 3f), MathF.Max(0f, room - below), room + below);
    }

    /// <summary>
    /// The region for a player whose centre is at <paramref name="playerCentre"/> and whose filtered lead is
    /// <paramref name="lead"/>. The box grows with its lead — up to the growth cap — by the lead's share of how far the fully
    /// grown box allows it to lead on that axis, the larger of the two axes' shares; the lead is then clamped against the
    /// limits of the box it actually is. So at the clamp the box is fully grown and the player sits on a third-line
    /// horizontally, or at the top or bottom edge less the slack vertically, and short of the clamp the clamp does not bind.
    /// Where the base is itself smaller than the slack allows for — the Close mode's upward allowance — the clamp can bind a
    /// little before full growth, and the player stays inside regardless, which is the property; growth is the preference.
    /// </summary>
    public static PlayerIntentRegion Around(Vector2 playerCentre, Vector2 lead, float comfortScale, bool travelling, float slack)
    {
        Vector2 baseHalf = BaseHalfSize(comfortScale);
        var full = LeadLimits(baseHalf * (1f + Weights.IntentRegionGrowthCap), slack);
        float across = full.Across > 0f ? MathF.Abs(lead.X) / full.Across : 0f;
        float vertical = lead.Y < 0f ? (full.Up > 0f ? -lead.Y / full.Up : 0f) : (full.Down > 0f ? lead.Y / full.Down : 0f);
        float share = MathF.Min(1f, MathF.Max(across, vertical));
        Vector2 half = baseHalf * (1f + Weights.IntentRegionGrowthCap * share);
        var limits = LeadLimits(half, slack);
        Vector2 applied = new(Math.Clamp(lead.X, -limits.Across, limits.Across), Math.Clamp(lead.Y, -limits.Up, limits.Down));
        Vector2 centre = playerCentre + new Vector2(0f, -PlayerBelowCentre(half, slack)) + applied;
        return new PlayerIntentRegion(centre, half, applied, travelling) { Heading = playerCentre + applied };
    }
}

/// <summary>
/// The two regions derived from one player observation. Admission is led by observed coherent travel;
/// local continuation adds the same-sized resting region around the player so a useful admitted purpose
/// is not discarded merely because the player reverses. The continuation is a union, deliberately not
/// a bounding rectangle: its extra diagonal corners were never admitted by either region.
/// </summary>
public readonly record struct PlayerIntentRegions(PlayerIntentRegion Admission, PlayerIntentRegion LocalContinuation)
{
    public bool ContainsContinuation(Vector2 point)
        => Admission.Contains(point) || LocalContinuation.Contains(point);

    public bool AcceptsContinuation(Vector2 point, float arrivalSlack)
        => Admission.Accepts(point, arrivalSlack) || LocalContinuation.Accepts(point, arrivalSlack);

    /// <summary>The distance outside the union. A point is outside only by the smaller component gap.</summary>
    public float GapBeyondContinuation(Vector2 point)
        => MathF.Min(Admission.GapBeyond(point), LocalContinuation.GapBeyond(point));
}

/// <summary>
/// Rebuilds the region once per brain tick, and owns the one piece of state a region cannot carry:
/// whether the body was inside it last tick.
/// </summary>
public sealed class PlayerIntentRegionSense
{
    public PlayerIntentRegion Region { get; private set; }
    /// <summary>The forward-led region used to admit new work.</summary>
    public PlayerIntentRegion Admission => Region;
    /// <summary>The exact union used to retain work through ambiguous heading, never a larger leash.</summary>
    public PlayerIntentRegions Regions { get; private set; }
    /// <summary>The zero-lead resting geometry. Course timing derives from this stable size, not a momentarily grown lead.</summary>
    public Vector2 BaseRestingHalfSize { get; private set; }

    /// <summary>
    /// Whether the body is with the player: inside his region, latched so that a body which entered stays
    /// inside until it is more than the settle radius beyond the edge. This is the one inside-or-outside fact
    /// the brain decides following on — inside, the companion moves about the region with it; outside, it
    /// rejoins it — and the owner ruled on 15 September 2026 that there is nothing else to wait for.
    ///
    /// <para>It replaced a settled streak (at rest inside for a rescore, latched while inside) and a published
    /// speed of the region's centre. Both existed because inside was where a body arrived and stopped: a body
    /// crossing the box at pace, or at rest in a box still sliding back onto a stopped player, was not arrival,
    /// and at three times the player's speed treating it as one flipped keeping company's method thirty-nine
    /// times in six hundred ticks — each flip a Hold that cut the move in flight. Nothing arrives now. Inside and
    /// outside ask for the same kind of request, so there is no Hold at the edge to cut anything, and the width
    /// the latch gives the edge is the whole of the hysteresis the streak was for.</para>
    /// </summary>
    public bool Inside { get; private set; }

    private Vector2 lead;
    private bool hasRegion;

    /// <summary>The follow objective every consumer shares, anchored on the region's own centre.
    /// A caller with an anchor of its own — a priced meeting place, a request's anchor — refines it
    /// with <see cref="FollowPlayerObjective.At"/> rather than building a second objective.</summary>
    public Position.FollowPlayerObjective Objective => new(Region, Region.Centre, Inside, CutOff);

    /// <summary>
    /// Whether the body has a way to the player that stays inside his region, grown by the width the latch gives its edge, in the
    /// three values every sense answers in. The owner ruled on 15 September 2026 that being with the player requires the companion
    /// can reach him, so a body inside the box on the far side of a sealed wall is outside and rejoining sends it round. It is asked
    /// of a flood bounded by the region and not of the reach disc, because a way round outside the region joins both sides of a
    /// wall inside it: the disc holds the player from either side and cannot tell them apart.
    ///
    /// <para><see cref="ReachVerdict.Reachable"/> and <see cref="ReachVerdict.Unreachable"/> are proofs, read only off a flood that
    /// exhausted the bounds it was given, joined the corner the player stands at now, and contains the corner the body is at now in
    /// its bounds. Anything else is <see cref="ReachVerdict.NotYet"/>: no flood has finished, the player has moved to a corner the
    /// finished one never joined, the body stands outside what it was asked about, or no corner the orb fits at roots a proof — the
    /// body pressed into a gap it cannot centre in, the player in a shaft, no world at all. An unfinished flood is never read as
    /// either proof. The first build ran the flood to exhaustion in one call on every tick the region or the player's corner changed,
    /// and a sentinel measured that at four to ten milliseconds of the twelve the whole tick shares, rebuilt on more than half the
    /// ticks of a walking player; under the live deadline the call stopped short, was never resumed, and read connected for as long as
    /// the player stood still.</para>
    ///
    /// <para>Its consumers read the middle value the way the reach sense's consumers read theirs: nothing is refused on it.
    /// <see cref="Inside"/> loses only a proven cut-off, because reading an unanswered question as outside would send rejoining to find
    /// a place its own route search cannot root either, and a companion beside the player would seek for somewhere it already is; and
    /// the positioner passes over only a corner <see cref="ProvenCutOff"/> names.</para>
    /// </summary>
    public ReachVerdict WayToPlayer { get; private set; } = ReachVerdict.NotYet;

    /// <summary>Whether the body is proven to have no way to the player inside his region.</summary>
    public bool CutOff => WayToPlayer == ReachVerdict.Unreachable;

    /// <summary>
    /// Whether a corner inside the grown region is proven not to join the player without leaving it: the finished flood answering
    /// for the player's current corner was bounded to hold it and never reached it. The positioner passes over such a corner as the
    /// way back in, so rejoining does not arrive on the wrong side of a wall; a corner nothing has proven either way is not passed over.
    /// </summary>
    public bool ProvenCutOff(Point corner)
        => Answering is Movement.FreeSpaceSearch found && sideBounds.Contains(corner.X * 16, corner.Y * 16) && !found.Reached.Contains(corner);

    /// <summary>The finished flood, while it still answers for the corner the player stands at; null when nothing is proven.</summary>
    private Movement.FreeSpaceSearch? Answering
        => side is { Stop: Movement.FreeSpaceSearch.StopReason.Exhausted } found && playerCorner is Point at && found.Reached.Contains(at) ? found : null;

    // The finished flood that answers, and the replacement grown under it. A replacement is started only when none is growing and
    // is never abandoned for a newer target: the box slides a tile at a time under a walking player, and restarting on every tile
    // would never let one finish. A flood the world changed under is dropped, because its answer no longer describes the world.
    private Movement.FreeSpaceSearch? side, growing;
    private Point sideRoot, growingRoot;
    private Rectangle sideBounds, growingBounds;
    private Point? playerCorner;

    /// <summary>
    /// Install the region a snapshot carried, in place of whatever the next update would compute: the audit
    /// restores the decision's input rather than re-deriving it from a history it does not have. The
    /// inside latch and the way-to-player floods are untouched — combat neither reads them nor prices them.
    /// </summary>
    public void AssumeRegion(PlayerIntentRegion region)
    {
        Region = region;
        Vector2 playerCentre = region.Heading - region.Lead;
        BaseRestingHalfSize = region.HalfSize;
        Regions = new PlayerIntentRegions(region,
            new PlayerIntentRegion(playerCentre + new Vector2(0f, -PlayerIntentRegion.PlayerBelowCentre(region.HalfSize, 0f)),
                region.HalfSize, Vector2.Zero, false) { Heading = playerCentre });
        lead = region.Lead;
        hasRegion = true;
    }

    public void Update(NPC companion, PlayerSense player)
    {
        Update(companion.Center, player.Position, player.Intent, player.IsTravelling, player.IsDead, player.Activity.Samples,
            player.HeldMove);
        ObserveWayToPlayer(companion.Center, player.Position);
    }

    /// <summary>
    /// One tick of the way-to-the-player sense, after the region has been rebuilt: grow the replacement flood by one slice, let a
    /// finished one take over, and answer about the body. Public so a fixture can drive it over a text world with the numeric update.
    /// </summary>
    public ReachVerdict ObserveWayToPlayer(Vector2 body, Vector2 playerCentre)
    {
        WayToPlayer = Grow(body, playerCentre);
        Inside &= WayToPlayer != ReachVerdict.Unreachable;
        return WayToPlayer;
    }

    /// <summary>How long the last tick's work on the flood took, in milliseconds, for the cost measure.</summary>
    public double LastFloodMs { get; private set; }

    private ReachVerdict Grow(Vector2 body, Vector2 playerCentre)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            if (!Movement.MovementQueries.HasWorld) { side = growing = null; playerCorner = null; return ReachVerdict.NotYet; }
            var world = Movement.MovementQueries.World;
            playerCorner = Movement.CornerGraph.NearestUsable(world, playerCentre, 2);
            if (playerCorner is not Point root) return ReachVerdict.NotYet;
            // Grown by the latch's width and a tile, so a body the latch still counts inside has a corner the flood may hold, and
            // snapped to whole tiles, so a box sliding with a walking player asks a new question once a tile rather than once a tick.
            float grow = Movement.Navigator.SettleRadius + 16f;
            int left = (int)MathF.Floor((Region.Centre.X - Region.HalfSize.X - grow) / 16f) * 16;
            int top = (int)MathF.Floor((Region.Centre.Y - Region.HalfSize.Y - grow) / 16f) * 16;
            int right = (int)MathF.Ceiling((Region.Centre.X + Region.HalfSize.X + grow) / 16f) * 16;
            int bottom = (int)MathF.Ceiling((Region.Centre.Y + Region.HalfSize.Y + grow) / 16f) * 16;
            var bounds = new Rectangle(left, top, right - left + 1, bottom - top + 1);

            if (side != null && !side.Valid) side = null;
            if (growing != null && !growing.Valid) growing = null;
            if (growing == null && (side == null || sideRoot != root || sideBounds != bounds))
            {
                growing = new Movement.FreeSpaceSearch(world, root, null, priceClearance: false) { Bounds = bounds };
                growingRoot = root;
                growingBounds = bounds;
            }
            if (growing != null)
            {
                // Its own slice rather than the tick's whole allowance: this runs inside the senses, ahead of the positioner and the
                // navigator that share the same deadline, and a flood allowed to run until the deadline spends their share first.
                growing.Advance(Weights.PlayerSideFloodExpansions, Weights.PlayerSideFloodMilliseconds);
                if (growing.Finished)
                {
                    side = growing;
                    sideRoot = growingRoot;
                    sideBounds = growingBounds;
                    growing = null;
                }
            }

            if (Answering is not Movement.FreeSpaceSearch found) return ReachVerdict.NotYet;
            if (Movement.CornerGraph.NearestUsable(world, body, 2) is not Point at) return ReachVerdict.NotYet;
            if (!sideBounds.Contains(at.X * 16, at.Y * 16)) return ReachVerdict.NotYet;
            return found.Reached.Contains(at) ? ReachVerdict.Reachable : ReachVerdict.Unreachable;
        }
        finally
        {
            LastFloodMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
    }

    /// <summary>The same update from the numbers it reads, so a recorded player track can be replayed through the real filter
    /// and the real geometry with no game world behind it: a capture's player stands thousands of tiles from anything a
    /// headless tile map holds.</summary>
    public void Update(Vector2 companionCentre, Vector2 playerCentre, Vector2 intent,
        bool travelling, bool dead, int samples, Vector2 held = default)
    {
        // A live interference footprint deliberately does not suppress the lead, and that was
        // measured rather than assumed. Suppressing it was tried, on the hypothesis that a lead
        // carries the companion into the very tiles the player is asking it to vacate; the courtesy
        // contract stayed red with it in and went green without it, because what was actually
        // holding the body in the passage was keeping company waiting a rescore before it could
        // change method. With that wait exempted for an overlapped footprint, the suppression's only
        // measured effect was on the open-floor walk, where it made courtesy worse — 44 stationary
        // ticks in the player's way against 28 — since a region pulled back onto his feet is a region
        // that asks the companion to stand where he is walking. Courtesy is a positioning problem and
        // is answered where the companion's place is chosen, not by blinding the region.
        // The lead chases a pose, not an unclamped number. Rest is the pose with no lead; a hold
        // pulls that pose to the third-line (or the vertical edge). Filtering toward the clamped
        // pose is what makes a reverse at the clamp start on the first opposite key: chasing
        // intent × 120 ticks first piled 360 px of lead behind a 92 px clamp, and a tap left had
        // to unwind the pile before the box moved. Walk-history intent is not the horizontal drive
        // — it is a second late.
        // Vertical is both. A held up or down still slides the box with no tile progress, the
        // same way a held right into a wall does. His own travel also moves it, at a fraction of
        // the displacement, so walking off one block is a nudge and a long cave fall still fills
        // the clamp. Treating any downward velocity as "go to the floor" was the snap: a one-tile
        // drop saturated the pose and the companion followed it down. The internal lead is
        // re-clamped after the travel add, so a long fall cannot pile past the clamp and have to
        // unwind before the box comes back.
        Vector2 drive = new(
            held.X,
            held.Y != 0f ? held.Y : intent.Y);
        bool led = drive.LengthSquared() > 0f || travelling;
        float scale = PlayerIntegration.CompanionPreferences.Current.FollowComfortScale;
        float slack = Movement.Navigator.SettleRadius;
        Vector2 previousLead = Region.Lead;
        if (dead || samples <= 1)
            lead = Vector2.Zero;
        else
        {
            Vector2 wanted = new(
                held.X != 0f ? held.X * 100000f : 0f,
                held.Y != 0f ? held.Y * 100000f : 0f);
            Vector2 pose = PlayerIntentRegion.Around(playerCentre, wanted, scale, led, slack).Lead;
            lead += (pose - lead) / MathF.Max(1f, Weights.IntentRegionFilterTicks);
            lead.Y += intent.Y * Weights.IntentRegionVerticalTravelGain;
            lead = PlayerIntentRegion.Around(playerCentre, lead, scale, led, slack).Lead;
        }
        // The slack is the settle radius, the room every destination inside the region reserves, so the player's own
        // position is always a place the companion could be and count as inside.
        Region = PlayerIntentRegion.Around(playerCentre, lead, scale, led, slack) with
        {
            Velocity = drive + (hasRegion ? Region.Lead - previousLead : Vector2.Zero),
        };
        BaseRestingHalfSize = PlayerIntentRegion.BaseHalfSize(scale);
        PlayerIntentRegion local = PlayerIntentRegion.Around(playerCentre, Vector2.Zero, scale, false, slack) with
        {
            Velocity = Vector2.Zero,
        };
        Regions = new PlayerIntentRegions(Region, local);
        hasRegion = true;

        // Inside is the body's centre, because the orb is its centre. A body that was inside stays inside until it is
        // more than the settle radius beyond the edge: that is the width the edge has, and it is what keeps a body the
        // region carries along its boundary from leaving and re-entering it on alternate ticks.
        Inside = Region.Contains(companionCentre)
            || (Inside && Region.GapBeyond(companionCentre) <= Movement.Navigator.SettleRadius);
    }
}
