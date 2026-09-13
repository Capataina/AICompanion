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
/// Candidates are the standable tiles nearest the player's line of travel at a ladder of horizons,
/// plus the place already chosen while it still lies ahead on that journey. Each is priced from a
/// returnable flood rooted at the companion's feet. Arriving before the player costs the wait until
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
    private ContinueRouteSearch? search;
    private ulong rootedAt, resolvedAt;
    private Point? chosen;

    public Vector2 Anchor { get; private set; }
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
            : search.Stop == AStar.SearchStopReason.Exhausted ? "exhausted" : "node-limit:" + search.TravelTicks.Count)
          + "@" + (resolvedAt >= rootedAt ? resolvedAt - rootedAt : 0);
    public IReadOnlyList<Candidate> Candidates => candidates;

    /// <summary>Reunion is not the method in use: nothing is retained and no flood advances.</summary>
    public void Release()
    {
        search?.Dispose();
        search = null;
        chosen = null;
        candidates.Clear();
        Priced = 0;
        PlayerTicks = CompanionTicks = float.NaN;
        Reason = "not-reuniting";
    }

    public Vector2 Resolve(Vector2 companionFeet, PlayerSense player, ulong tick)
    {
        resolvedAt = tick;
        candidates.Clear();
        Priced = 0;
        PlayerTicks = CompanionTicks = float.NaN;
        if (player.IsDead || !player.IsTravelling)
        {
            string reason = player.IsDead ? "player-dead" : "player-not-travelling";
            Release();
            Reason = reason;
            Anchor = player.Bottom;
            return Anchor;
        }

        // Route times are measured from the flood's root. An unfinished flood keeps expanding while the body
        // stays in the region it can return from, because restarting it on every cadence meant a cave-sized
        // flood never finished and reunion never got a priced place. It re-roots when the terrain changed, when
        // the body left that region, or when it finished with prices older than the cadence.
        if (MovementQueries.NearestStandable(MovementQueries.FeetTile(companionFeet), 2) is Point root)
        {
            bool stale = search is { Finished: true } && search.Start != root && tick - rootedAt >= (ulong)Weights.MeetingRerootTicks;
            if (search == null || !search.Valid || stale || !search.CanReuseFrom(root))
            {
                search?.Dispose();
                search = new ContinueRouteSearch(root, null, AStar.AllowLava, false);
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
            playerPace / MathF.Max(0.05f, BodyPhysics.WalkSpeed - playerPace));
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
            if (best is not Candidate leader || candidate.Cost < leader.Cost) best = candidate;
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

        if (decision is Candidate place)
        {
            chosen = place.Tile;
            Anchor = MovementQueries.FeetWorld(place.Tile);
            PlayerTicks = place.PlayerTicks;
            CompanionTicks = place.CompanionTicks ?? float.NaN;
        }
        else
        {
            // With no priced place the anchor is the bounded continuation reunion used before meeting places:
            // aiming at the player's current feet instead trailed a travelling player for as long as a large
            // cave's flood took to finish, which was most of the time.
            chosen = null;
            Anchor = player.Predict(Weights.MeetingFallbackLeadTicks);
            Reason = settled && best == null ? "no-reachable-meeting-place" : "meeting-undecided";
        }
        return Anchor;
    }

    private Candidate Price(Point tile, float playerTicks, float confidence, float latePenalty)
    {
        if (search == null || !search.TravelTicks.TryGetValue(tile, out float companionTicks))
            return new Candidate(tile, playerTicks, null, float.PositiveInfinity);
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
        if (!MovementQueries.IsStandable(tile.X, tile.Y) || playerPace <= 0f) return null;
        Vector2 direction = Vector2.Normalize(player.Intent);
        Vector2 offset = MovementQueries.FeetWorld(tile) - player.Bottom;
        float along = Vector2.Dot(offset, direction);
        float across = MathF.Abs(offset.X * direction.Y - offset.Y * direction.X);
        float ticks = along / playerPace;
        // A tile is sixteen pixels.
        if (along < -16f || across > (Weights.MeetingSnapTiles + 1) * 16f
            || ticks > Weights.MeetingHorizonSteps * Weights.MeetingHorizonStepTicks * confidence) return null;
        return MathF.Max(0f, ticks);
    }

    /// <summary>The standable tile nearest a projected point within a few rows of it, nearest row first.
    /// The window is deliberately smaller than a floor's thickness plus a body, so a candidate stays on
    /// the player's apparent floor rather than jumping to the one below.</summary>
    private static Point? Snap(Vector2 feet)
    {
        Point centre = MovementQueries.FeetTile(feet);
        for (int i = 0; i <= Weights.MeetingSnapTiles * 2; i++)
        {
            int dy = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1);
            if (MovementQueries.IsStandable(centre.X, centre.Y + dy)) return new Point(centre.X, centre.Y + dy);
        }
        return null;
    }
}
