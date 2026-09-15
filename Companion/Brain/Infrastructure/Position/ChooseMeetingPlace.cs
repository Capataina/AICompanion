#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Infrastructure.Position;

/// <summary>
/// Where reunion aims while the player is travelling: a standable place on the player's apparent
/// journey that the companion's own routes reach, priced by how long until the two are together,
/// rather than one point extrapolated from the player's velocity.
///
/// Candidates are the hoverable cells nearest the player's line of travel at a ladder of horizons,
/// plus the place already chosen while it still lies ahead on that journey. Each is priced from a
/// free-space flood rooted at the companion's body. Arriving before the player costs the wait until
/// the player gets there. Arriving after the player has passed costs the arrival plus the chase that
/// follows at the difference in pace, which is why, along one shared floor, every point ahead ties
/// and the uncertainty charge keeps the anchor from leading further than the travel evidence
/// supports. What the comparison changes is the case a single extrapolated point gets wrong: a
/// parallel route that climbs to the player's journey ahead is met there, a route that ends is
/// abandoned for the way back, and a companion already ahead waits on the journey instead of walking
/// back to where the player stands now.
///
/// A candidate the flood has not reached is undecided rather than cheap: while the flood is unfinished
/// the previous place is kept, and with none the bounded continuation of the player's travel stands in.
/// A player working or paused in place is not on a journey, so company is where they are.
/// </summary>
public sealed class ChooseMeetingPlace
{
    public readonly record struct Candidate(Point Tile, float PlayerTicks, float? CompanionTicks, float Cost);

    private readonly List<Candidate> candidates = new();
    private FreeSpaceSearch? search;
    private ulong rootedAt, resolvedAt;
    private Point? chosen;

    public Vector2 Anchor { get; private set; }
    /// <summary>The hoverable place reunion is flying to this tick, without the overlay lerp. Flying at the published box would trail a moving meeting place.</summary>
    public Vector2 Destination { get; private set; }
    /// <summary>The anchor is a priced place on the journey rather than the player's current feet.</summary>
    public bool HasPlace => chosen != null;
    public string Reason { get; private set; } = "not-reuniting";
    /// <summary>Ticks until the player reaches the chosen place, or NaN when no place was priced.</summary>
    public float PlayerTicks { get; private set; } = float.NaN;
    /// <summary>Route ticks for the companion from where its flood was rooted, or NaN when unknown.</summary>
    public float CompanionTicks { get; private set; } = float.NaN;
    public int Priced { get; private set; }
    /// <summary>How the companion's flood stands — unfinished, exhausted, or capped at its node limit, which
    /// bounds the answer without proving that an unreached place is unreachable — and, after the @, how many
    /// ticks ago it was rooted, which is how old the route times behind a decision are.</summary>
    public string FloodState => search == null ? "none"
        : (!search.Finished ? "advancing"
            : search.Stop == FreeSpaceSearch.StopReason.Exhausted ? "exhausted" : "node-limit:" + search.Reached.Count)
          + "@" + (resolvedAt >= rootedAt ? resolvedAt - rootedAt : 0);
    public IReadOnlyList<Candidate> Candidates => candidates;

    /// <summary>Reunion is not the method in use: nothing is retained and no flood advances.</summary>
    public void Release()
    {
        search = null;
        chosen = null;
        candidates.Clear();
        Priced = 0;
        PlayerTicks = CompanionTicks = float.NaN;
        Reason = "not-reuniting";
    }

    /// <summary>
    /// Where reunion aims. <paramref name="region"/> is the player's intent region, and it is now the
    /// anchor whenever nothing has been priced — which, measured on the 2026-09-14 capture, is very
    /// nearly always: a place is decided only on a finished flood, the flood does not finish while
    /// the player moves, and the priced place therefore decided 3 rows out of 22,473. So the
    /// region's leading edge is the anchor in the ordinary case and the priced place is the
    /// route-aware refinement of it when one exists, rather than the other way round.
    /// </summary>
    public Vector2 Resolve(Vector2 companionCentre, PlayerSense player, PlayerIntentRegion region, ulong tick)
    {
        resolvedAt = tick;
        candidates.Clear();
        Priced = 0;
        PlayerTicks = CompanionTicks = float.NaN;
        if (Anchor == Vector2.Zero)
            Anchor = player.Bottom;
        if (player.IsDead || !player.IsTravelling)
        {
            search = null;
            chosen = null;
            candidates.Clear();
            Priced = 0;
            PlayerTicks = CompanionTicks = float.NaN;
            Reason = player.IsDead ? "player-dead" : "player-not-travelling";
            // A player who has stopped is met at the region's centre, which is his feet plus whatever
            // lead has not yet drifted out of the filter. Aiming at his feet directly would undo the
            // drift the filter exists for and snap the destination back in one tick.
            Vector2 home = region.Centre;
            Destination = home;
            Anchor = player.IsDead || Vector2.DistanceSquared(Anchor, home) < 64f
                ? home
                : Vector2.Lerp(Anchor, home, Weights.MeetingAnchorLerp);
            return Destination;
        }

        // Route times are measured from the flood's root. An unfinished flood keeps expanding while the body
        // stays inside the region it has already flooded, because restarting it on every cadence meant a
        // cave-sized flood never finished and reunion never got a priced place. It re-roots when the terrain
        // it read changed, when the body left the flooded region, or when it finished with prices older than
        // the cadence. The flood prices plain length, not clearance: a meeting time is a distance at pace, and
        // the corridor preference belongs to the route the navigator then plans.
        if (MovementQueries.NearestUsableCorner(companionCentre, 2, requireSweep: false) is Point root)
        {
            bool stale = search is { Finished: true } && search.Start != root && tick - rootedAt >= (ulong)Weights.MeetingRerootTicks;
            bool left = search != null && search.Start != root && !search.Reached.Contains(root);
            if (search == null || !search.Valid || stale || left)
            {
                search = new FreeSpaceSearch(MovementQueries.World, root, null, priceClearance: false);
                rootedAt = tick;
            }
        }
        search?.Advance(Weights.ReachFloodBudget, Weights.MeetingSearchMilliseconds);
        // A best-first flood finalises a tile's travel time only when it stops expanding: before that a
        // reached tile carries an upper bound that a later, shorter route can still lower, and deciding
        // on those bounds priced a reconnection ahead as dearer than the long way round. A finished flood
        // bounds the answer, and a tile it never reached is unavailable within that bound.
        bool settled = search?.Finished == true;
        bool undecided = !settled;

        // Intent is already scaled by confidence, so the player covers a projected distance sooner
        // than its horizon says: at the true pace the arrival is horizon × confidence.
        float confidence = Math.Clamp(player.Activity.Confidence, 0.01f, 1f);
        float playerPace = player.Intent.Length() / confidence;
        float latePenalty = MathF.Min(Weights.MeetingChaseCeiling,
            playerPace / MathF.Max(0.05f, OrbPace.MaxSpeed - playerPace));
        Candidate? best = null;
        var seen = new HashSet<Point>();
        for (int step = 0; step <= Weights.MeetingHorizonSteps; step++)
        {
            float horizon = step * Weights.MeetingHorizonStepTicks;
            if (Snap(player.Bottom + player.Intent * horizon) is not Point tile || !seen.Add(tile)) continue;
            Candidate candidate = Price(tile, horizon * confidence, confidence, latePenalty);
            candidates.Add(candidate);
            if (candidate.CompanionTicks == null) continue;
            Priced++;
            if (best is not Candidate leader || Cheaper(candidate, leader)) best = candidate;
        }

        Candidate? incumbent = null;
        if (chosen is Point held && !seen.Contains(held) && StillAhead(held, player, confidence, playerPace) is float heldTicks)
        {
            incumbent = Price(held, heldTicks, confidence, latePenalty);
            candidates.Add(incumbent.Value);
        }
        else if (chosen is Point same && seen.Contains(same))
            incumbent = candidates.Find(c => c.Tile == same);

        Candidate? decision = null;
        if (!undecided && best is Candidate winner)
        {
            decision = incumbent is Candidate kept && kept.CompanionTicks != null
                && kept.Cost <= winner.Cost * (1f + Weights.MeetingSwitchMargin) ? kept : winner;
            Reason = decision.Value.Tile == chosen ? "retained-meeting-place"
                : decision.Value.PlayerTicks <= 0f ? "player-position-priced" : "meeting-ahead-priced";
        }
        else if (incumbent is Candidate kept)
        {
            decision = kept;
            Reason = "retained-while-undecided";
        }

        Vector2 target;
        if (decision is Candidate place)
        {
            chosen = place.Tile;
            target = MovementQueries.HoverPoint(place.Tile);
            PlayerTicks = place.PlayerTicks;
            CompanionTicks = place.CompanionTicks ?? float.NaN;
            // A priced place refines the region; it does not compete with it. The pricing minimises
            // the cost of meeting, and the cheapest meeting with a walking player is the one that
            // happens where he already is — so on open ground the ladder reliably names a tile at
            // about his feet, which is outside the region a lead has carried forward, and aiming
            // there is what kept the companion level with him at his own speed and never in front.
            // Measured on the authored straight walk with the priced place as the anchor: a steady
            // 42 px behind and ahead on 1.9% of moving rows. So the region decides the
            // neighbourhood and the flood decides the tile inside it; a place the region does not
            // contain is not a refinement of it and loses to its leading edge. The route-awareness
            // the pricing buys is kept exactly where it points somewhere the companion is going,
            // which is the parallel-routes case it was built for.
            if (region.IsTravelling && !region.Contains(target))
            {
                target = region.LeadingEdge;
                chosen = null;
                Reason = "meeting-place-outside-intent-region";
            }
        }
        else
        {
            // With no priced place the anchor is the bounded continuation reunion used before meeting places:
            // aiming at the player's current feet instead trailed a travelling player for as long as a large
            // cave's flood took to finish, which was most of the time.
            chosen = null;
            // The region's leading edge, not a fresh extrapolation of its own. The old fallback ran
            // a second, shorter lead beside the one the region already carries, so two places in the
            // brain claimed to be "where the player is going" and they disagreed by the difference
            // between their two lead times; the region is the one that is filtered, clamped and
            // grown, and a second copy of it could only ever be a worse version.
            target = region.LeadingEdge;
            Reason = settled && best == null ? "no-reachable-meeting-place" : "meeting-undecided";
        }
        Destination = target;
        Anchor = Vector2.Lerp(Anchor, target, Weights.MeetingAnchorLerp);
        return Destination;
    }

    /// <summary>Equal cost prefers the place that still sits ahead of the player, so the published box does not rest on their toes when a later step on the same floor is just as cheap.</summary>
    private static bool Cheaper(Candidate candidate, Candidate leader)
    {
        if (candidate.Cost < leader.Cost) return true;
        if (candidate.Cost > leader.Cost) return false;
        bool candidateAhead = candidate.PlayerTicks >= Weights.MeetingMinLeadTicks;
        bool leaderAhead = leader.PlayerTicks >= Weights.MeetingMinLeadTicks;
        return candidateAhead && !leaderAhead;
    }

    private Candidate Price(Point tile, float playerTicks, float confidence, float latePenalty)
    {
        // A cell is priced through the nearest of its corners the flood has settled: a finished flood's
        // cost is the route length in pixels, and at the orb's top pace that is the time.
        float? length = null;
        if (search != null)
            CornerGraph.AnyCornerOf(tile, corner =>
            {
                if (search.CostTo(corner) is float c && (length == null || c < length)) length = c;
                return false;
            });
        if (length is not float routeLength)
            return new Candidate(tile, playerTicks, null, float.PositiveInfinity);
        float companionTicks = routeLength / OrbPace.MaxSpeed;
        float late = MathF.Max(0f, companionTicks - playerTicks);
        float horizon = playerTicks / confidence;
        float cost = MathF.Max(companionTicks, playerTicks) + late * latePenalty
            + horizon * (1f - confidence) * Weights.MeetingUncertaintyCost;
        return new Candidate(tile, playerTicks, companionTicks, cost);
    }

    /// <summary>Ticks until the player reaches a previously chosen place, or null once the player has
    /// passed it, the journey has turned away from it, or it lies beyond the horizon ladder.</summary>
    private static float? StillAhead(Point tile, PlayerSense player, float confidence, float playerPace)
    {
        if (!MovementQueries.IsHoverable(tile) || playerPace <= 0f) return null;
        Vector2 direction = Vector2.Normalize(player.Intent);
        Vector2 offset = MovementQueries.HoverPoint(tile) - player.Bottom;
        float along = Vector2.Dot(offset, direction);
        float across = MathF.Abs(offset.X * direction.Y - offset.Y * direction.X);
        float ticks = along / playerPace;
        // A tile is sixteen pixels.
        if (along < -16f || across > (Weights.MeetingSnapTiles + 1) * 16f
            || ticks > Weights.MeetingHorizonSteps * Weights.MeetingHorizonStepTicks * confidence) return null;
        return MathF.Max(0f, ticks);
    }

    /// <summary>The hoverable cell nearest a projected point of the player's journey, within a few rows of it,
    /// nearest row first. The window is deliberately small so a candidate stays beside the player's apparent
    /// path rather than drifting to an open pocket above or below it: the body hovers, but the meeting is his.</summary>
    private static Point? Snap(Vector2 feet)
    {
        Point centre = MovementQueries.Tile(feet);
        for (int i = 0; i <= Weights.MeetingSnapTiles * 2; i++)
        {
            int dy = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1);
            Point cell = new(centre.X, centre.Y + dy);
            if (MovementQueries.IsHoverable(cell)) return cell;
        }
        return null;
    }
}
