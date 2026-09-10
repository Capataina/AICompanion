#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.ProjectileAiming;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.SharedMovementSystem;
using AICompanion.Companion.Brain.WorldObservation;
using Senses = AICompanion.Companion.Brain.WorldObservation;

namespace AICompanion.Companion.Brain.PositionSelection;

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
    private ContinueRouteSearch? returnSearch, rawSearch;
    private bool reachLava;
    public int CandidateCount { get; private set; }
    public int ReachableCandidateCount { get; private set; }
    public int RejectedCandidateCount { get; private set; }
    public string ChoiceReason { get; private set; } = "none";
    /// <summary>Following is complete only inside its two-axis player region, separate from route waypoint arrival.</summary>
    public bool FollowObjectiveSatisfied { get; private set; }
    public float FollowHorizontalGap { get; private set; }
    public float FollowVerticalGap { get; private set; }
    public string FollowObjectiveReason { get; private set; } = "not-following";
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
        UpdateFollowObjective(request, senses);
        switch (request.Kind)
        {
            case RequestKind.Hold:
                // A hold or an exact ends whatever was being held (a roam's spot, a scored spot),
                // so the next scored or roam request picks afresh instead of reading the spot an
                // interruption left in Chosen for the rest of a hold (Codex review of 2303802).
                lastRequest = request;
                Chosen = null;
                return null;
            case RequestKind.Exact:
                // Exact still means a real place to stand: the nearest standable tile the walker can
                // reach, which NavGrid refuses when it is in or over lava; when nothing reachable is
                // near, the nearest standable tile at all, and the partial path walks as close as it can.
                lastRequest = request;
                RefreshReach(senses);
                Point around = MovementQueries.FeetTile(request.Anchor);
                Point? tile = MovementQueries.NearestStandable(around, 3, t => InReach(t) && Allowed(t)) ?? MovementQueries.NearestStandable(around, 3, Allowed);
                Chosen = tile is Point t ? MovementQueries.FeetWorld(t) : null;
                return Chosen;
            case RequestKind.Roam:
                // Anywhere in the region the body can reach, the further from its feet the better,
                // kept for a while so the walk is a walk and not a twitch between picks. The region
                // is the one every other kind reads, so a roam never leaves what the flood found,
                // and the flood ran under the brain's one-way rule for a roam (off), so a pocket is
                // walked and never deepened. Nothing reachable but the tile underfoot is a hold.
                sinceScore++;
                if (Chosen != null && lastRequest.Kind == RequestKind.Roam && sinceScore < Weights.RoamHoldTicks)
                    return Chosen;
                lastRequest = request;
                sinceScore = 0;
                RefreshReach(senses);
                Chosen = RoamSpot(MovementQueries.FeetTile(request.Anchor));
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

    /// <summary>
    /// A missing tile in an unfinished flood is unknown, not unreachable. The following tier may
    /// therefore begin toward a useful lower floor while the bounded flood is still expanding;
    /// only an exhausted region can reject it as physically absent.
    /// </summary>
    private bool ProvenUnreachable(Point tile) => ReachComplete && !InReach(tile);

    private void UpdateFollowObjective(in PositionRequest request, Senses.Senses senses)
    {
        if (request.Kind != RequestKind.WithPlayer)
        {
            FollowObjectiveSatisfied = false;
            FollowHorizontalGap = FollowVerticalGap = 0f;
            FollowObjectiveReason = "not-following";
            return;
        }
        var objective = new FollowPlayerObjective(senses.Player.Bottom, request.Anchor);
        bool connected = CanSeePlayer(senses.Companion.Bottom + new Vector2(0f, -30f), senses);
        FollowObjectiveSatisfied = objective.IsSatisfied(senses.Companion.Bottom, connected);
        FollowHorizontalGap = objective.HorizontalGap(senses.Companion.Bottom);
        FollowVerticalGap = objective.VerticalGap(senses.Companion.Bottom);
        FollowObjectiveReason = objective.Reason(senses.Companion.Bottom, connected);
    }

    /// <summary>The last flood from the companion's feet holds this tile: the brain reads the player's feet against it to end a stranded count.</summary>
    public bool Reaches(Point tile) => InReach(tile);
    public float? EstimatedTravelTicks(Point from, Point tile) => rawSearch?.EstimatedTicks(from, tile)
        ?? returnSearch?.EstimatedTicks(from, tile);

    private void RefreshReach(Senses.Senses senses)
    {
        if (reach != null && sinceFlood < RescoreInterval)
            return;
        sinceFlood = 0;
        Point? feet = MovementQueries.NearestStandable(MovementQueries.FeetTile(senses.Companion.Bottom), 2);
        if (feet == null)
        {
            // In the air or inside something: keep the last flood, which is a tick or two stale
            // and still the best answer to "where can I get to" until the body lands.
            return;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // Without the edges that have no way back, so this region means "everywhere the body can go
        // and come home from". That is what turns the tier below into the decision about whether to
        // enter somewhere: while any candidate spot is in here only those are scored, and when none
        // is, the tier opens and the unrecoverable ones are scored instead. So the companion shoots
        // into a pit from its rim while a rim spot exists and drops in when none does, with nothing
        // in the code naming an enemy or a pit.
        // Reuse needs generated connectivity in both directions, not a distance allowance.
        // A body can cross a one-way boundary while moving only one tile.
        if (returnSearch == null || !returnSearch.Valid || reachLava != AStar.AllowLava || !returnSearch.CanReuseFrom(feet.Value))
        {
            reachLava = AStar.AllowLava;
            returnSearch?.Dispose(); rawSearch?.Dispose();
            returnSearch = new ContinueRouteSearch(feet.Value, null, AStar.AllowLava, false);
            rawSearch = new ContinueRouteSearch(feet.Value, null, AStar.AllowLava, true);
        }
        returnSearch.Advance(Weights.ReachFloodBudget, Weights.PositionReachMilliseconds / 2d);
        returnable = returnSearch.Reached;
        bool complete = returnSearch.Finished && returnSearch.Stop == AStar.SearchStopReason.Exhausted;
        reach = returnable;

        // Unless that map does not hold the player, in which case it is the wrong map. Being stuck
        // is not having a way back to the take-off; it is not having a way to the player (Caner's
        // own definition, 2026-09-08), so a place he is standing in is by definition not somewhere
        // the companion strands itself, and refusing the only edges that reach him would leave the
        // body on the rim of a shaft he had climbed down. The corpus's two-wide shaft fixture is
        // exactly that shape: eight rows down, seven rim tiles returnable and inside the sample
        // box, so a returnable candidate always survived and the tier never opened for him.
        //
        // The raw region has to be read before the exception is granted, because "he is not in the
        // returnable region" is also true of a player the body cannot reach at all — walled off
        // behind a sand fall, which is a state the mod supports until he digs the body out. Opening
        // the tier there refuses nothing and buys nothing: it hands the roam a region full of drops
        // with no way back, so the pocket gets deeper, and it asserts a one-way route to a player
        // no route reaches. The exception is for the drop that leads to him, so it is granted only
        // where the region without the refusal actually holds him.
        Point? player = MovementQueries.NearestStandable(MovementQueries.FeetTile(senses.Player.Bottom), 2);
        PlayerOnlyOneWay = false;
        if (player is Point p && !returnable.Contains(p))
        {
            rawSearch!.Advance(Weights.ReachFloodBudget, Weights.PositionReachMilliseconds / 2d);
            HashSet<Point> raw = new(rawSearch.Reached);
            bool rawComplete = rawSearch.Finished && rawSearch.Stop == AStar.SearchStopReason.Exhausted;
            // Both bounded searches prove membership, but may spend their work on different
            // branches. Preserve all previously proven reachable tiles when opening the tier.
            raw.UnionWith(returnable);
            if (raw.Contains(p))
            {
                reach = raw;
                complete = rawComplete;
                PlayerOnlyOneWay = true;
            }
        }

        LastFloodMs = clock.Elapsed.TotalMilliseconds;
        ReachComplete = complete;
    }

    // The refusing flood is kept beside the one being scored against, because after the second
    // flood `reach` no longer answers "can I come home from here" and that is the question the
    // telemetry and the scenario capture ask. Nothing in the scoring reads it.
    private HashSet<Point>? returnable;

    /// <summary>
    /// The refusing flood did not hold the player, so the region being scored is the raw one and
    /// the companion is willing to go somewhere it cannot come back from. True is not a fault: it
    /// is the companion following the player into a place he chose to be. It is worth recording
    /// because it is the one state where prevention is deliberately switched off.
    /// </summary>
    public bool PlayerOnlyOneWay { get; private set; }

    /// <summary>How many tiles the body can reach at all, and how many of those it can come home from.</summary>
    public int ReachCount => reach?.Count ?? 0;
    public int ReturnableCount => returnable?.Count ?? 0;

    /// <summary>Whether the spot last chosen is one the body can come home from; true when nothing is chosen.</summary>
    public bool ChosenReturnable
        => Chosen is not Vector2 c || returnable == null || returnable.Contains(MovementQueries.FeetTile(c));

    /// <summary>Wall-clock of the last reach flood, for the telemetry.</summary>
    public double LastFloodMs { get; private set; }

    /// <summary>
    /// The farthest of a handful of reachable tiles drawn at random, which is far without being
    /// the same far corner every time: a pocket is walked end to end over a few picks rather
    /// than paced between its two ends. Banned tiles and the tile underfoot are not offered.
    /// </summary>
    private Vector2? RoamSpot(Point feet)
    {
        if (reach == null || reach.Count < 2)
            return null;
        var tiles = new List<Point>(reach);
        Point? best = null;
        int bestDistance = 0;
        for (int i = 0; i < RoamSamples; i++)
        {
            Point t = tiles[Main.rand.Next(tiles.Count)];
            if (t == feet || !Allowed(t))
                continue;
            int distance = Math.Abs(t.X - feet.X) + Math.Abs(t.Y - feet.Y);
            if (distance > bestDistance)
            {
                best = t;
                bestDistance = distance;
            }
        }
        return best is Point b ? MovementQueries.FeetWorld(b) : null;
    }

    private const int RoamSamples = 12;

    private Vector2? Best(in PositionRequest request, Senses.Senses senses, WeaponProfile? fireProfile)
    {
        EvidenceTick = senses.Tick;
        CandidateEvidence = "";
        EvaluatedCandidates = 0;
        var evidence = new List<(Point tile, float score, string shot)>();
        // The spot being walked to, read before it is overwritten, so it can be favoured over an
        // equal one. Without this the scorer picked afresh every rescore with no memory of its own
        // last answer, and since two standable tiles a couple of pixels apart score within noise
        // of each other, the tile it named wandered continuously under a request that had not
        // changed — which the navigator then read as a new goal and replanned for.
        Vector2? held = Chosen;
        FollowPlayerObjective? followObjective = request.Kind == RequestKind.WithPlayer
            ? new FollowPlayerObjective(senses.Player.Bottom, request.Anchor) : null;
        Point centre = MovementQueries.FeetTile(request.Anchor);
        Vector2 playerBottom = senses.Player.Bottom;
        bool threatened = !senses.Threats.PlayerIsSafe;
        float bandNear = threatened ? Weights.ThreatBandNear : Weights.CalmBandNear;
        float bandFar = threatened ? Weights.ThreatBandFar : Weights.CalmBandFar;
        // How far the weapon in hand can actually shoot, so the standoff never prefers a spot the
        // shot cannot arrive from. Without a profile there is nothing to shoot and the standoff is
        // inert anyway, so the wide default costs nothing.
        float reach = fireProfile?.Reach ?? Weights.StandoffFar;

        // Two passes: every candidate gets the cheap factors; only the best few then pay for an
        // aimer solve, which is the expensive one (up to 48 arcs × 150 ticks of tile checks).
        // Reachability is a tier, not a factor: while any candidate is inside the flooded region,
        // only those are scored, because a spot the walker cannot reach is not a worse spot but no
        // spot. When none is (the flood ran out before it got here), every candidate stays, and the
        // partial path walks the companion as close as it can, which is what it did before.
        var candidates = new List<(Vector2 feet, Vector2 eye, float baseScore)>();
        CandidateCount = ReachableCandidateCount = RejectedCandidateCount = 0;
        bool anyReachable = false;
        // The incumbent participates even when a moving anchor changes sample-grid parity.
        // Otherwise hysteresis cannot retain a position the sampler never offered this tick.
        if (held is Vector2 incumbent && Allowed(MovementQueries.FeetTile(incumbent))
            && MovementQueries.IsStandable(MovementQueries.FeetTile(incumbent).X, MovementQueries.FeetTile(incumbent).Y))
        {
            Vector2 eye = incumbent + new Vector2(0, -30);
            Point tile = MovementQueries.FeetTile(incumbent);
            float score = ScoreSpot(request, incumbent, eye, playerBottom, senses, bandNear, bandFar, 1f, reach) * Weights.IncumbentSpotBonus;
            if ((followObjective?.AcceptsDestination(incumbent, CanSeePlayer(eye, senses)) ?? true) && !ProvenUnreachable(tile) && score > 0)
            { candidates.Add((incumbent, eye, score)); anyReachable = InReach(tile); }
        }
        for (int dx = -SampleRadiusTiles; dx <= SampleRadiusTiles; dx += SampleStride)
        {
            for (int dy = -SampleRadiusTiles; dy <= SampleRadiusTiles; dy += SampleStride)
            {
                int x = centre.X + dx, y = centre.Y + dy;
                CandidateCount++;
                if (!MovementQueries.IsStandable(x, y) || !Allowed(new Point(x, y)))
                {
                    RejectedCandidateCount++;
                    continue;
                }
                bool reachable = InReach(new Point(x, y));
                if (reachable) ReachableCandidateCount++;
                if (ProvenUnreachable(new Point(x, y)))
                    continue;
                Vector2 feet = MovementQueries.FeetWorld(new Point(x, y));
                if (followObjective is FollowPlayerObjective objective && !objective.AcceptsDestination(feet, CanSeePlayer(feet + new Vector2(0f, -30f), senses)))
                    continue;
                if (!reachable && anyReachable)
                    continue;
                Vector2 eye = feet + new Vector2(0f, -30f);
                float score = ScoreSpot(request, feet, eye, playerBottom, senses, bandNear, bandFar, fire: 1f, reach) * Incumbency(feet, held);
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
            ChoiceReason = "no-accepted-candidate";
            return null;
        }

        bool needsFire = request.Target is NPC && fireProfile != null;
        candidates.Sort((a, b) => b.baseScore.CompareTo(a.baseScore));
        int solves = needsFire ? Math.Min(MaxSolvesPerRescore, candidates.Count) : 0;

        Vector2? best = null;
        float bestScore = -1f;
        var solveClock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < candidates.Count; i++)
        {
            (Vector2 feet, Vector2 eye, float baseScore) = candidates[i];
            float score = baseScore;
            string shot = "not-required";
            if (needsFire)
            {
                if (i > 0 && solveClock.Elapsed.TotalMilliseconds >= Weights.PositionAimingMilliseconds) break;
                if (i >= solves)
                    break; // unsolved candidates cannot beat a solved one above them
                float fire = TrajectoryAimer.Solve(eye, request.Target!, fireProfile!.Value) != null ? 1f : 0.15f;
                shot = fire == 1f ? "clear-arc" : "no-arc";
                score = ScoreSpot(request, feet, eye, playerBottom, senses, bandNear, bandFar, fire, reach) * Incumbency(feet, held);
            }
            EvaluatedCandidates++;
            evidence.Add((MovementQueries.FeetTile(feet), score, shot));
            evidence.Sort((a, b) => b.score.CompareTo(a.score));
            if (evidence.Count > 4) evidence.RemoveAt(4);
            if (score > bestScore)
            {
                bestScore = score;
                best = feet;
            }
        }
        ChosenScore = bestScore;
        CandidateEvidence = string.Join("|", evidence.ConvertAll(e => FormattableString.Invariant($"{e.tile.X},{e.tile.Y}:{e.score:0.000}:{e.shot}")));
        ChoiceReason = best == held ? "retained-position" : anyReachable ? "reachable-candidate" : "reachability-unknown";
        return best;
    }

    public int EvidenceTick { get; private set; }
    public int EvaluatedCandidates { get; private set; }
    public string CandidateEvidence { get; private set; } = "";

    /// <summary>
    /// The bonus the spot already held gets over an equal one; 1 for everything else. Applied to
    /// the cheap pass as well as the scored one, because the cheap pass decides which candidates
    /// are worth an aimer solve and an incumbent dropped there never gets to defend itself.
    /// </summary>
    private static float Incumbency(Vector2 feet, Vector2? held)
        => held is Vector2 h && Vector2.DistanceSquared(feet, h) < Weights.IncumbentSlackPx * Weights.IncumbentSlackPx
            ? Weights.IncumbentSpotBonus
            : 1f;

    private static float ScoreSpot(in PositionRequest request, Vector2 feet, Vector2 eye, Vector2 playerBottom, Senses.Senses senses, float bandNear, float bandFar, float fire, float reach)
    {
        // The band is measured to the request's anchor (the player's predicted position when
        // walking with them), with a gentle pull toward its centre so equal-band spots are not tied.
        float toAnchor = Vector2.Distance(feet, request.Anchor);
        float toPlayer = Vector2.Distance(feet, playerBottom);
        float band = Consideration.Band(toAnchor, bandNear, bandFar, 400f) * (0.6f + 0.4f * Consideration.Inverse(toAnchor, bandFar + 200f));
        bool seesPlayer = CanSeePlayer(eye, senses);
        float sight = seesPlayer ? 1f : 0.35f;
        float danger = DangerAt(feet, senses);
        float open = Openness(feet);
        float travel = TravelBias(feet, senses);

        return request.Kind switch
        {
            // Getting back to him is a disengage, not a charge: the walk home gained the clear-way
            // test the firing requests already had, so a route that passes through a zombie is
            // discounted and the body goes round rather than paying for the shortest line. The
            // planner already prices reachable enemies on the route; this is the same idea applied
            // to choosing the destination, so the two agree instead of one undoing the other.
            RequestKind.WithPlayer => band * sight * (1f - 0.8f * danger) * open * travel * ClearWayTo(feet, senses),
            // Guarding him is being able to shoot what is attacking him, which is not the same as
            // standing where he stands. It carried neither a standoff from the target nor the
            // clear-way test, so the only thing pulling the body anywhere was a band measured to
            // the player and the threats are on the player: every guard spot worth having was
            // inside the melee. It now scores the same two factors the line-of-fire request does.
            RequestKind.Guard => Consideration.Band(toPlayer, Weights.GuardBandNear, Weights.GuardBandFar, 260f) * sight * fire * (1f - 0.7f * danger) * open * StandoffFromTarget(feet, request.Target, reach) * ClearWayTo(feet, senses),
            RequestKind.LineOfFire => fire * Consideration.AtLeast(band, 0.3f) * (1f - 0.7f * danger) * open * StandoffFromTarget(feet, request.Target, reach) * ClearWayTo(feet, senses),
            RequestKind.Retreat => (1f - danger) * Consideration.AtLeast(band, 0.3f) * fire * open * ClearWayTo(feet, senses),
            _ => 0f,
        };
    }

    private static bool CanSeePlayer(Vector2 eye, Senses.Senses senses)
        => Collision.CanHitLine(eye, 1, 1, senses.PlayerEntity.position, senses.PlayerEntity.width, senses.PlayerEntity.height);

    /// <summary>0..1: how much of the next second's predicted threat paths pass through this spot.</summary>
    private static float DangerAt(Vector2 feet, Senses.Senses senses)
    {
        float worst = 0f;
        Rectangle body = new((int)feet.X - 10, (int)feet.Y - 42, 20, 42);
        foreach (ThreatRecord t in senses.Threats.Threats)
        {
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
        Point p = MovementQueries.FeetTile(feet);
        int solid = 0;
        for (int dx = -2; dx <= 2; dx++)
            for (int dy = -3; dy <= -1; dy++)
                if (MovementQueries.IsBlock(p.X + dx, p.Y + dy))
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

    /// <summary>
    /// How good this spot's distance from the thing being shot at is. The band alone was a plateau,
    /// so a spot pressed against an enemy scored exactly as well as one across the room and which
    /// one got picked was down to the other factors and the walk; the companion closed on things it
    /// could already hit. Distance now leans outward across the band, because the line-of-fire
    /// factor is a separate multiplier and it is what refuses a spot too far to shoot from — which
    /// means every remaining distance is one the shot solves at, and the further of two is strictly
    /// better for a body that would rather not be reached.
    /// </summary>
    private static float StandoffFromTarget(Vector2 feet, NPC? target, float reach)
    {
        if (target == null)
            return 1f;
        // The far edge is whatever the weapon in hand can actually reach, never a fixed distance.
        // The band's own 520 px outran the knife's 380 px reach, so the outward lean could walk the
        // body to about 500 px from the target and hold it there — where its shot does not arrive
        // and the line-of-fire factor, which scores a failed solve at 0.15 rather than 0, was too
        // weak to veto it. Found by review before it reached a playtest. A little inside the reach,
        // because the target moves and a spot exactly at the limit stops working when it steps back.
        float far = MathF.Min(Weights.StandoffFar, reach * 0.85f);
        float near = MathF.Min(Weights.StandoffNear, far * 0.5f);
        float d = Vector2.Distance(feet, target.Center);
        float across = MathHelper.Clamp((d - near) / MathF.Max(1f, far - near), 0f, 1f);
        return Consideration.Band(d, near, far, 300f) * (0.55f + 0.45f * across);
    }

    /// <summary>
    /// Near 1 when the straight line from where the companion stands to this spot passes no
    /// enemy closely, low when it runs through one, because a firing spot on the
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
            float along = MathHelper.Clamp(Vector2.Dot(t.Npc.Center - from, way) / (length * length), 0f, 1f);
            Vector2 closest = from + way * along;
            if (Vector2.Distance(closest, t.Npc.Center) < Clearance)
                return 0.15f;
        }
        return 1f;
    }
}
