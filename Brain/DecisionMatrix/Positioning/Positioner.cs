#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Brain.Aiming;
using AICompanion.Brain.DecisionMatrix.Decision;
using AICompanion.Brain.DecisionMatrix.Navigation;
using AICompanion.Brain.DecisionMatrix.Senses;

namespace AICompanion.Brain.DecisionMatrix.Positioning;

/// <summary>
/// Turns a position request into a feet position by scoring candidate standing spots.
/// Candidates are standable tiles sampled around the request's anchor; each scores on
/// distance band to the player (tight under threat), sight line to the player, line of
/// fire to the target through the aimer, danger from predicted threat paths, openness
/// and cliff safety, with the weights the request kind sets. Re-scored every few ticks
/// so the companion does not twitch between two equal spots.
/// </summary>
public sealed class Positioner
{
    private const int RescoreInterval = 12;
    private const int SampleRadiusTiles = 14;
    private const int SampleStride = 2;
    private const int MaxSolvesPerRescore = 8;

    public Vector2? Chosen { get; private set; }
    public float ChosenScore { get; private set; }
    private PositionRequest lastRequest;
    private int sinceScore = RescoreInterval;

    // The feet tiles a walker can reach from where the companion stands, flooded once per
    // rescore and read for every candidate. A spot the walker cannot reach is not a spot: the
    // fourth run of 2026-09-08 parked the companion above a sealed cavity the scorer had picked.
    private HashSet<Point>? reach;
    private int sinceFlood = RescoreInterval;

    /// <summary>Whether the flood from the companion's feet ran out of region before its budget, so a tile outside it is truly unreachable.</summary>
    public bool ReachComplete { get; private set; }

    // Spots the navigator could not reach however it planned, each with the tick it is allowed
    // back; skipped by every resolve until then, so the next answer is a different place.
    private readonly Dictionary<Point, int> banned = new();
    private int clock;

    /// <summary>
    /// Refuse this feet tile for a while and pick again: the navigator stood still on the way to it
    /// twice over, so the grid's opinion that it is reachable is wrong for the body in fact, and
    /// the companion is better off somewhere else than standing.
    /// </summary>
    public void Ban(Point tile, int ticks)
    {
        banned[tile] = clock + ticks;
        Chosen = null;
        sinceScore = RescoreInterval;
    }

    private bool Allowed(Point tile) => !banned.TryGetValue(tile, out int until) || until < clock;

    public Vector2? Resolve(in PositionRequest request, Senses.Senses senses, WeaponProfile? fireProfile)
    {
        // The region ages in ticks, whatever the request does this tick: counted inside the
        // rescore it multiplied the two cadences and refloods came every 144 ticks.
        sinceFlood++;
        clock++;
        switch (request.Kind)
        {
            case RequestKind.Hold:
                Chosen = null;
                return null;
            case RequestKind.Exact:
                // Exact still means a real place to stand: the nearest standable tile the walker can
                // reach, which NavGrid refuses when it is in or over lava; when nothing reachable is
                // near, the nearest standable tile at all, and the partial path walks as close as it can.
                RefreshReach(senses);
                Point around = NavGrid.FeetTile(request.Anchor);
                Point? tile = NavGrid.NearestStandable(around, 3, t => InReach(t) && Allowed(t)) ?? NavGrid.NearestStandable(around, 3, Allowed);
                Chosen = tile is Point t ? NavGrid.FeetWorld(t) : null;
                return Chosen;
        }

        sinceScore++;
        bool kindChanged = request.Kind != lastRequest.Kind || request.Target != lastRequest.Target;
        if (Chosen != null && !kindChanged && sinceScore < RescoreInterval)
            return Chosen;

        lastRequest = request;
        sinceScore = 0;
        RefreshReach(senses);
        Chosen = Best(request, senses, fireProfile);
        return Chosen;
    }

    private bool InReach(Point tile) => reach != null && reach.Contains(tile);

    private void RefreshReach(Senses.Senses senses)
    {
        if (reach != null && sinceFlood < RescoreInterval)
            return;
        sinceFlood = 0;
        Point? feet = NavGrid.NearestStandable(NavGrid.FeetTile(senses.Companion.Bottom), 2);
        if (feet == null)
        {
            // In the air or inside something: keep the last flood, which is a tick or two stale
            // and still the best answer to "where can I get to" until the body lands.
            return;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        reach = AStar.Region(feet.Value, Weights.ReachFloodBudget, out bool complete);
        LastFloodMs = clock.Elapsed.TotalMilliseconds;
        ReachComplete = complete;
    }

    /// <summary>Wall-clock of the last reach flood, for the telemetry.</summary>
    public double LastFloodMs { get; private set; }

    private Vector2? Best(in PositionRequest request, Senses.Senses senses, WeaponProfile? fireProfile)
    {
        Point centre = NavGrid.FeetTile(request.Anchor);
        Vector2 playerBottom = senses.Player.Bottom;
        bool threatened = !senses.Threats.PlayerIsSafe;
        float bandNear = threatened ? Weights.ThreatBandNear : Weights.CalmBandNear;
        float bandFar = threatened ? Weights.ThreatBandFar : Weights.CalmBandFar;

        // Two passes: every candidate gets the cheap factors; only the best few then pay for an
        // aimer solve, which is the expensive one (up to 48 arcs × 150 ticks of tile checks).
        // Reachability is a tier, not a factor: while any candidate is inside the flooded region,
        // only those are scored, because a spot the walker cannot reach is not a worse spot but no
        // spot. When none is (the flood ran out before it got here), every candidate stays, and the
        // partial path walks the companion as close as it can, which is what it did before.
        var candidates = new List<(Vector2 feet, Vector2 eye, float baseScore)>();
        bool anyReachable = false;
        for (int dx = -SampleRadiusTiles; dx <= SampleRadiusTiles; dx += SampleStride)
        {
            for (int dy = -SampleRadiusTiles; dy <= SampleRadiusTiles; dy += SampleStride)
            {
                int x = centre.X + dx, y = centre.Y + dy;
                if (!NavGrid.IsStandable(x, y) || !Allowed(new Point(x, y)))
                    continue;
                bool reachable = InReach(new Point(x, y));
                if (!reachable && anyReachable)
                    continue;
                Vector2 feet = NavGrid.FeetWorld(new Point(x, y));
                Vector2 eye = feet + new Vector2(0f, -30f);
                float score = ScoreSpot(request, feet, eye, playerBottom, senses, bandNear, bandFar, fire: 1f);
                if (score <= 0f)
                    continue;
                // The tier opens only on a reachable candidate the action accepts: a reachable
                // tile the score vetoes (a retreat spot under an enemy) must not empty the list and
                // turn a movement request into a hold.
                if (reachable && !anyReachable)
                {
                    anyReachable = true;
                    candidates.Clear();
                }
                candidates.Add((feet, eye, score));
            }
        }
        if (candidates.Count == 0)
        {
            ChosenScore = -1f;
            return null;
        }

        bool needsFire = request.Target is NPC && fireProfile != null;
        candidates.Sort((a, b) => b.baseScore.CompareTo(a.baseScore));
        int solves = needsFire ? Math.Min(MaxSolvesPerRescore, candidates.Count) : 0;

        Vector2? best = null;
        float bestScore = -1f;
        for (int i = 0; i < candidates.Count; i++)
        {
            (Vector2 feet, Vector2 eye, float baseScore) = candidates[i];
            float score = baseScore;
            if (needsFire)
            {
                if (i >= solves)
                    break; // unsolved candidates cannot beat a solved one above them
                float fire = TrajectoryAimer.Solve(eye, request.Target!, fireProfile!.Value) != null ? 1f : 0.15f;
                score = ScoreSpot(request, feet, eye, playerBottom, senses, bandNear, bandFar, fire);
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = feet;
            }
        }
        ChosenScore = bestScore;
        return best;
    }

    private static float ScoreSpot(in PositionRequest request, Vector2 feet, Vector2 eye, Vector2 playerBottom, Senses.Senses senses, float bandNear, float bandFar, float fire)
    {
        // The band is measured to the request's anchor (the player's predicted position when
        // walking with them), with a gentle pull toward its centre so equal-band spots are not tied.
        float toAnchor = Vector2.Distance(feet, request.Anchor);
        float toPlayer = Vector2.Distance(feet, playerBottom);
        float band = Consideration.Band(toAnchor, bandNear, bandFar, 400f) * (0.6f + 0.4f * Consideration.Inverse(toAnchor, bandFar + 200f));
        bool seesPlayer = Collision.CanHitLine(eye, 1, 1, senses.PlayerEntity.position, senses.PlayerEntity.width, senses.PlayerEntity.height);
        float sight = seesPlayer ? 1f : 0.35f;
        float danger = DangerAt(feet, senses);
        float open = Openness(feet);
        float travel = TravelBias(feet, senses);

        return request.Kind switch
        {
            RequestKind.WithPlayer => band * sight * (1f - 0.8f * danger) * open * travel,
            RequestKind.Guard => Consideration.Band(toPlayer, 24f, 120f, 200f) * sight * fire * (1f - 0.5f * danger) * open,
            RequestKind.LineOfFire => fire * Consideration.AtLeast(band, 0.3f) * (1f - 0.7f * danger) * open * StandoffFromTarget(feet, request.Target) * ClearWayTo(feet, senses),
            RequestKind.Retreat => (1f - danger) * Consideration.AtLeast(band, 0.3f) * fire * open * ClearWayTo(feet, senses),
            _ => 0f,
        };
    }

    /// <summary>0..1: how much of the next second's predicted threat paths pass through this spot.</summary>
    private static float DangerAt(Vector2 feet, Senses.Senses senses)
    {
        float worst = 0f;
        Rectangle body = new((int)feet.X - 10, (int)feet.Y - 42, 20, 42);
        foreach (ThreatRecord t in senses.Threats.Threats)
        {
            if (!t.Reachable)
                continue;
            float d = Vector2.Distance(t.Npc.Center, feet);
            float proximity = Consideration.Inverse(d, 160f) * 0.6f;
            for (int tick = 0; tick <= 40; tick += 10)
                if (t.PredictedHitbox(tick).Intersects(body))
                {
                    proximity = 1f;
                    break;
                }
            worst = MathF.Max(worst, proximity);
        }
        return worst;
    }

    /// <summary>Penalise crevices: count solid tiles in the ring two tiles out at eye height.</summary>
    private static float Openness(Vector2 feet)
    {
        Point p = NavGrid.FeetTile(feet);
        int solid = 0;
        for (int dx = -2; dx <= 2; dx++)
            for (int dy = -3; dy <= -1; dy++)
                if (NavGrid.IsBlock(p.X + dx, p.Y + dy))
                    solid++;
        return Consideration.Inverse(solid, 12f) * 0.7f + 0.3f;
    }

    private static float TravelBias(Vector2 feet, Senses.Senses senses)
    {
        var p = senses.Player;
        if (!p.IsTravelling)
            return 1f;
        float along = (feet.X - p.Bottom.X) * p.TravelDirection;
        return along >= 0f ? 1f : 0.6f;
    }

    private static float StandoffFromTarget(Vector2 feet, NPC? target)
    {
        if (target == null)
            return 1f;
        float d = Vector2.Distance(feet, target.Center);
        return Consideration.Band(d, 120f, 520f, 300f);
    }

    /// <summary>
    /// Near 1 when the straight line from where the companion stands to this spot passes no
    /// reachable enemy closely, low when it runs through one, because a firing spot on the
    /// far side of a zombie is reached by walking into the zombie.
    /// </summary>
    private static float ClearWayTo(Vector2 feet, Senses.Senses senses)
    {
        const float Clearance = 40f;
        Vector2 from = senses.Companion.Bottom;
        Vector2 way = feet - from;
        float length = way.Length();
        if (length < 1f)
            return 1f;
        foreach (ThreatRecord t in senses.Threats.Threats)
        {
            if (!t.Reachable)
                continue;
            float along = MathHelper.Clamp(Vector2.Dot(t.Npc.Center - from, way) / (length * length), 0f, 1f);
            Vector2 closest = from + way * along;
            if (Vector2.Distance(closest, t.Npc.Center) < Clearance)
                return 0.15f;
        }
        return 1f;
    }
}
