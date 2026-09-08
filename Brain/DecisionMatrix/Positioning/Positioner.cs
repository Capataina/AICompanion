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

    public Vector2? Resolve(in PositionRequest request, Senses.Senses senses, WeaponProfile? fireProfile)
    {
        switch (request.Kind)
        {
            case RequestKind.Hold:
                Chosen = null;
                return null;
            case RequestKind.Exact:
                // Exact still means a real place to stand: the nearest standable tile, which
                // NavGrid refuses when it is in or over lava.
                Point? tile = NavGrid.NearestStandable(NavGrid.FeetTile(request.Anchor), 3);
                Chosen = tile is Point t ? NavGrid.FeetWorld(t) : null;
                return Chosen;
        }

        sinceScore++;
        bool kindChanged = request.Kind != lastRequest.Kind || request.Target != lastRequest.Target;
        if (Chosen != null && !kindChanged && sinceScore < RescoreInterval)
            return Chosen;

        lastRequest = request;
        sinceScore = 0;
        Chosen = Best(request, senses, fireProfile);
        return Chosen;
    }

    private Vector2? Best(in PositionRequest request, Senses.Senses senses, WeaponProfile? fireProfile)
    {
        Point centre = NavGrid.FeetTile(request.Anchor);
        Vector2 playerBottom = senses.Player.Bottom;
        bool threatened = !senses.Threats.PlayerIsSafe;
        float bandNear = threatened ? Weights.ThreatBandNear : Weights.CalmBandNear;
        float bandFar = threatened ? Weights.ThreatBandFar : Weights.CalmBandFar;

        // Two passes: every candidate gets the cheap factors; only the best few then pay for an
        // aimer solve, which is the expensive one (up to 48 arcs × 150 ticks of tile checks).
        var candidates = new List<(Vector2 feet, Vector2 eye, float baseScore)>();
        for (int dx = -SampleRadiusTiles; dx <= SampleRadiusTiles; dx += SampleStride)
        {
            for (int dy = -SampleRadiusTiles; dy <= SampleRadiusTiles; dy += SampleStride)
            {
                int x = centre.X + dx, y = centre.Y + dy;
                if (!NavGrid.IsStandable(x, y))
                    continue;
                Vector2 feet = NavGrid.FeetWorld(new Point(x, y));
                Vector2 eye = feet + new Vector2(0f, -30f);
                float score = ScoreSpot(request, feet, eye, playerBottom, senses, bandNear, bandFar, fire: 1f);
                if (score > 0f)
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
