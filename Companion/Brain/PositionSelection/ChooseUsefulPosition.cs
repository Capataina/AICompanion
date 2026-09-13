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
    // A standable row can be only one tile high. Skipping alternate rows makes the same
    // floor disappear whenever the moving anchor changes parity, especially at pool rims.
    private const int SampleStride = 1;
    private const int MaxSolvesPerRescore = 8;

    private Vector2? chosen;
    /// <summary>The destination this resolver holds. Every change of value advances <see cref="ChosenRevision"/>.</summary>
    public Vector2? Chosen
    {
        get => chosen;
        private set
        {
            if (value != chosen) ChosenRevision++;
            chosen = value;
        }
    }
    /// <summary>The destination revision: advances whenever <see cref="Chosen"/> takes a different value, including
    /// clearing it, so every row naming one revision names one admitted destination.</summary>
    public long ChosenRevision { get; private set; }
    /// <summary>The success region the held destination was admitted against, snapshotted by the resolve that admitted it.</summary>
    public SuccessRegion Region { get; private set; } = SuccessRegion.None;
    public float ChosenScore { get; private set; }
    private PositionRequest lastRequest;
    private WeaponProfile? lastFireProfile;
    private int lastTerrainRevision = -1;
    private int lastInterferenceRevision;
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

    /// <summary>Refine a nominated attack method through the ordinary resolver. A rejected
    /// nomination must not erase another activity's held destination or its explanation.
    /// Reach-search work survives rejection so yielding cannot starve refinement.</summary>
    public PositionOffer PrepareOffer(in PositionRequest request, Senses.Senses senses, WeaponProfile? profile)
    {
        if (request.Kind is not (RequestKind.Guard or RequestKind.LineOfFire))
            throw new ArgumentException("Only attack-position requests require this admission query.", nameof(request));
        var held = (Chosen, ChosenScore, lastRequest, lastFireProfile, lastTerrainRevision, sinceScore,
            ChoiceReason, FollowObjectiveSatisfied, FollowHorizontalGap, FollowVerticalGap,
            FollowObjectiveReason, CandidateCount, ReachableCandidateCount, RejectedCandidateCount,
            EvidenceTick, EvaluatedCandidates, CandidateEvidence, ChosenRevision, Region);
        int previousClock = clock;
        bool admitted = false;
        try
        {
            if (request.Target is not { } enemy || !enemy.CanBeChasedBy())
                return new(null, "attack-target-not-attackable", "", senses.Tick);
            Vector2? destination = Resolve(request, senses, profile);
            admitted = destination != null;
            return new(destination, ChoiceReason, CandidateEvidence, EvidenceTick);
        }
        finally
        {
            // Querying candidates must not age temporary bans as extra executed ticks.
            clock = previousClock;
            if (!admitted)
                (Chosen, ChosenScore, lastRequest, lastFireProfile, lastTerrainRevision, sinceScore,
                    ChoiceReason, FollowObjectiveSatisfied, FollowHorizontalGap, FollowVerticalGap,
                    FollowObjectiveReason, CandidateCount, ReachableCandidateCount, RejectedCandidateCount,
                    // The revision and region come last: restoring Chosen above advances the revision, and a
                    // rejected query must leave the held destination's identity exactly as it found it.
                    EvidenceTick, EvaluatedCandidates, CandidateEvidence, ChosenRevision, Region) = held;
        }
    }

    public Vector2? Resolve(in PositionRequest request, Senses.Senses senses, WeaponProfile? fireProfile)
    {
        if (lastTerrainRevision != TerrainChanges.Revision)
        {
            // Cadence may retain unchanged observations, never evidence from an edited world.
            Chosen = null;
            sinceFlood = RescoreInterval;
            lastTerrainRevision = TerrainChanges.Revision;
        }
        // New interference evidence reconsiders a held destination once. Waiting out the rescore cadence would let the
        // placement or the walk that produced it finish first, and rescoring every tick while it lasts would buy nothing.
        if (senses.Player.InterferenceRevision != lastInterferenceRevision)
        {
            lastInterferenceRevision = senses.Player.InterferenceRevision;
            sinceScore = RescoreInterval;
        }
        // The region ages in ticks, whatever the request does this tick: counted inside the
        // rescore it multiplied the two cadences and refloods came every 144 ticks.
        sinceFlood++;
        clock++;
        UpdateFollowObjective(request, senses);
        bool attackPosition = request.Kind is RequestKind.LineOfFire or RequestKind.Guard;
        if (attackPosition && (fireProfile == null || request.Target is not { active: true, life: > 0 }))
        {
            lastRequest = request;
            Chosen = null;
            ChosenScore = 0f;
            ChoiceReason = "attack-input-unavailable";
            CandidateEvidence = "";
            EvaluatedCandidates = CandidateCount = ReachableCandidateCount = RejectedCandidateCount = 0;
            EvidenceTick = senses.Tick;
            Region = SuccessRegion.None;
            return null;
        }
        switch (request.Kind)
        {
            case RequestKind.Hold:
                // A hold or an exact ends whatever was being held (a roam's spot, a scored spot),
                // so the next scored or roam request picks afresh instead of reading the spot an
                // interruption left in Chosen for the rest of a hold (Codex review of 2303802).
                lastRequest = request;
                Chosen = null;
                Region = SuccessRegion.None;
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
                // Declared against the request's own stand rather than the substituted tile: the proof
                // chose the stand, and the tile nearest it is where the walk happens to aim.
                Region = Chosen == null ? SuccessRegion.None
                    : request.WorkTile is Point work ? SuccessRegion.ToolStand(request.Anchor, work, senses.Tick, TerrainChanges.Revision)
                    : SuccessRegion.Unscored(SuccessRegionKind.Undeclared, request.Anchor, senses.Tick, TerrainChanges.Revision);
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
                Region = Chosen == null ? SuccessRegion.None
                    : SuccessRegion.Unscored(SuccessRegionKind.Undeclared, request.Anchor, senses.Tick, TerrainChanges.Revision);
                return Chosen;
        }

        if (request.Kind == RequestKind.WithPlayer && request.MeetingPlace)
        {
            // A priced meeting place is the destination itself. Scoring a region around it picked a
            // tile nearer the player whose best route went the other way, so the companion walked back
            // along the route the meeting place had been chosen to avoid. It still has to be a place to
            // stand that this region has not proven unreachable; otherwise ordinary scoring applies.
            RefreshReach(senses);
            Point place = MovementQueries.FeetTile(request.Anchor);
            if (MovementQueries.IsStandable(place.X, place.Y) && Allowed(place) && !ProvenUnreachable(place)
                && CourtesyShare(MovementQueries.FeetWorld(place), senses) == 1f)
            {
                lastRequest = request;
                sinceScore = 0;
                Chosen = MovementQueries.FeetWorld(place);
                ChoiceReason = "priced-meeting-place";
                Region = SuccessRegion.Unscored(SuccessRegionKind.MeetingPlace, request.Anchor, senses.Tick, TerrainChanges.Revision);
                return Chosen;
            }
        }

        sinceScore++;
        // A dropped meeting place is a changed request: without this the scored path kept walking to the
        // dropped tile for the rest of the rescore cadence, even once it lay behind the player.
        bool kindChanged = request.Kind != lastRequest.Kind || request.Target != lastRequest.Target
            || request.MeetingPlace != lastRequest.MeetingPlace || fireProfile != lastFireProfile;
        if (Chosen != null && !kindChanged && sinceScore < RescoreInterval)
            return Chosen;

        lastRequest = request;
        lastFireProfile = fireProfile;
        sinceScore = 0;
        RefreshReach(senses);
        Chosen = Best(request, senses, fireProfile);
        // Every non-null answer from Best passed acceptance against this call's player feet and anchor (the
        // incumbent and every sampled candidate are gated alike), so these are the references it was admitted
        // against even when the value did not change and the revision did not advance.
        Region = Chosen == null ? SuccessRegion.None
            : request.Kind == RequestKind.WithPlayer
                ? SuccessRegion.Follow(new FollowPlayerObjective(senses.Player.Bottom, request.Anchor), senses.Tick, TerrainChanges.Revision)
                : SuccessRegion.Unscored(SuccessRegionKind.FiringPosition, request.Anchor, senses.Tick, TerrainChanges.Revision);
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

    /// <summary>
    /// Whether the body can walk to this feet tile and come home from it: membership of the region flooded from the feet with the
    /// edges that have no way back refused, which is the region every other kind is scored against unless the player stands only
    /// beyond a drop. The flood is refreshed on its own cadence first, so a caller deciding during a hold still reads a current
    /// region. An unfinished flood answers only for the tiles it has reached, so a tile beyond its frontier reads false rather
    /// than unknown; a caller that treats false as "not here" stays inside what has been proven.
    /// </summary>
    public bool IsReturnable(Senses.Senses senses, Point tile)
    {
        RefreshReach(senses);
        return returnable != null && returnable.Contains(tile);
    }
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
            float score = ScoreSpot(request, incumbent, eye, playerBottom, senses, bandNear, bandFar, SightToTarget(eye, request.Target), reach) * Weights.IncumbentSpotBonus;
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
                float score = ScoreSpot(request, feet, eye, playerBottom, senses, bandNear, bandFar, SightToTarget(eye, request.Target), reach) * Incumbency(feet, held);
                if (score <= 0f)
                    continue;
                // The tier opens only on a reachable candidate the action accepts: a reachable
                // tile the score vetoes must not empty the list and
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

        // Attack destinations cannot be admitted on a reduced score after the trajectory
        // solver refuses them. Shared safety searches body states independently of a shot.
        bool needsFire = request.Kind is RequestKind.LineOfFire or RequestKind.Guard;
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
                if (i > 0 && SharedMovementSystem.LimitPlanningWork.Spent(solveClock, Weights.PositionAimingMilliseconds)) break;
                if (i >= solves)
                    break; // unsolved candidates cannot beat a solved one above them
                bool solved = TrajectoryAimer.Solve(eye, request.Target!, fireProfile!.Value) != null;
                float fire = solved ? 1f : 0f;
                shot = solved ? "clear-arc" : "no-arc";
                score = ScoreSpot(request, feet, eye, playerBottom, senses, bandNear, bandFar, fire, reach)
                    * (solved ? Incumbency(feet, held) : 1f);
            }
            EvaluatedCandidates++;
            evidence.Add((MovementQueries.FeetTile(feet), score, shot));
            evidence.Sort((a, b) => b.score.CompareTo(a.score));
            if (evidence.Count > 4) evidence.RemoveAt(4);
            if (score > 0f && score > bestScore)
            {
                bestScore = score;
                best = feet;
            }
        }
        ChosenScore = bestScore;
        CandidateEvidence = string.Join("|", evidence.ConvertAll(e => FormattableString.Invariant($"{e.tile.X},{e.tile.Y}:{e.score:0.000}:{e.shot}")));
        ChoiceReason = best == null ? "no-usable-destination-established"
            : best == held ? "retained-position" : anyReachable ? "reachable-candidate" : "reachability-unknown";
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
        float danger = PredictedExposureAt(feet, senses);
        float open = Openness(feet);
        float travel = TravelBias(feet, senses);

        return request.Kind switch
        {
            // Getting back to him is a disengage, not a charge: the walk home gained the clear-way
            // test the firing requests already had, so a route that passes through a zombie is
            // discounted and the body goes round rather than paying for the shortest line. The
            // planner already prices reachable enemies on the route; this is the same idea applied
            // to choosing the destination, so the two agree instead of one undoing the other.
            RequestKind.WithPlayer => band * sight * (1f - 0.8f * danger) * open * travel * ClearWayTo(feet, senses) * CourtesyShare(feet, senses),
            // Guarding him is being able to shoot what is attacking him, which is not the same as
            // standing where he stands. It carried neither a standoff from the target nor the
            // clear-way test, so the only thing pulling the body anywhere was a band measured to
            // the player and the threats are on the player: every guard spot worth having was
            // inside the melee. It now scores the same two factors the line-of-fire request does.
            RequestKind.Guard => Consideration.Band(toPlayer, Weights.GuardBandNear, Weights.GuardBandFar, 260f) * sight * fire * (1f - 0.7f * danger) * open * StandoffFromTarget(feet, request.Target, reach) * ClearWayTo(feet, senses, request.Target),
            RequestKind.LineOfFire => fire * Consideration.AtLeast(band, 0.3f) * (1f - 0.7f * danger) * open * StandoffFromTarget(feet, request.Target, reach) * ClearWayTo(feet, senses, request.Target),
            _ => 0f,
        };
    }

    /// <summary>
    /// The share of its score a follow spot keeps when a body standing there would overlap the player's interference footprint
    /// (a block or wall aimed at the companion, or a passage the player is walking down). It is a factor rather than a veto:
    /// among useful spots, one out of the player's way wins, and a spot that is the only usable one stays usable. Attack
    /// positions do not read it, because protection is not priced against courtesy.
    /// </summary>
    private static float CourtesyShare(Vector2 feet, Senses.Senses senses)
        => senses.Player.Interference is Rectangle footprint
            && PlayerSense.BodyTiles(feet, BodyPhysics.Width, BodyPhysics.Height).Intersects(footprint)
            ? Weights.CourtesyOccupancyShare : 1f;

    private static bool CanSeePlayer(Vector2 eye, Senses.Senses senses)
        => Collision.CanHitLine(eye, 1, 1, senses.PlayerEntity.position, senses.PlayerEntity.width, senses.PlayerEntity.height);

    /// <summary>
    /// A cheap stand-in for "could this spot shoot the target", used to decide which candidates are
    /// worth an aimer solve. The cheap pass used to pass a literal 1 for every candidate, which made
    /// the ranking that selects the shortlist contain no line-of-fire information at all: the eight
    /// candidates that then paid for a real solve were the eight that happened to win on band,
    /// danger and openness, and nothing connected them to whether a shot existed. On 810 hunt ticks
    /// of the 2026-09-11 session the companion stood on a spot it had itself scored as having no arc.
    ///
    /// It ranks and never vetoes. Terraria's decompiled <c>Collision.CanHitLine</c> walks the tile
    /// line between the centres of the two boxes and fails on any active, non-actuated tile that is
    /// <c>tileSolid</c> and not <c>tileSolidTop</c>, so platforms do not block it and the test is a
    /// straight line. A projectile arcs, so a blocked ray is a lower bound rather than a refusal:
    /// such a candidate keeps a reduced rank and can still reach the shortlist and be solved properly.
    /// </summary>
    private static float SightToTarget(Vector2 eye, NPC? target)
        => target == null || Collision.CanHitLine(eye, 1, 1, target.position, target.width, target.height)
            ? 1f
            : Weights.BlockedSightRank;

    /// <summary>0..1: how much of the next second's predicted threat paths pass through this spot.</summary>
    public static float PredictedExposureAt(Vector2 feet, Senses.Senses senses)
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
        // Standoff is bounded by the available weapon's reach. Keeping a little inside it
        // leaves room for target movement; the trajectory solve separately admits the shot.
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
    ///
    /// The thing being shot at is excluded, because it is already priced twice over — by
    /// <see cref="PredictedExposureAt"/> at the destination and by <see cref="StandoffFromTarget"/>, which is
    /// the factor that owns how close to the target the companion should stand. Counting it here as
    /// well made a hunt self-defeating: the target is a member of the threat list, so every spot on
    /// its far side was cut to a sixth of its score and demoted out of the shortlist before anyone
    /// asked whether it could shoot from there, leaving only near-side spots to choose between.
    /// Every other enemy on the way is still charged, which is the hazard this factor exists for.
    /// </summary>
    private static float ClearWayTo(Vector2 feet, Senses.Senses senses, NPC? target = null)
    {
        const float Clearance = 40f;
        Vector2 from = senses.Companion.Bottom;
        Vector2 way = feet - from;
        float length = way.Length();
        if (length < 1f)
            return 1f;
        foreach (ThreatRecord t in senses.Threats.Threats)
        {
            if (target != null && ReferenceEquals(t.Npc, target))
                continue;
            float along = MathHelper.Clamp(Vector2.Dot(t.Npc.Center - from, way) / (length * length), 0f, 1f);
            Vector2 closest = from + way * along;
            if (Vector2.Distance(closest, t.Npc.Center) < Clearance)
                return 0.15f;
        }
        return 1f;
    }
}
