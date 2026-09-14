#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Aiming;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using Senses = AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Infrastructure.Position;

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

    // The feet tiles a walker can reach are the reach sense's, not this resolver's. A spot the walker
    // cannot reach is not a spot: the fourth run of 2026-09-08 parked the companion above a sealed
    // cavity the scorer had picked. This resolver drives the sense's cadence — the flood's lava and
    // one-way rules are set per request, so it has to run inside a resolve — and every other consumer
    // reads the same region instead of flooding its own.
    private ReachSense reachSense = null!;
    public int CandidateCount { get; private set; }
    public int ReachableCandidateCount { get; private set; }
    public int RejectedCandidateCount { get; private set; }
    public string ChoiceReason { get; private set; } = "none";
    /// <summary>Following is complete only inside its two-axis player region, separate from route waypoint arrival.</summary>
    public bool FollowObjectiveSatisfied { get; private set; }
    public float FollowHorizontalGap { get; private set; }
    public float FollowVerticalGap { get; private set; }
    public string FollowObjectiveReason { get; private set; } = "not-following";

    /// <summary>Whether the flood from the companion's feet ran out of region before its budget, so a tile outside it is truly unreachable.</summary>
    /// <summary>The scored region's completeness, not the two-way region's: this pairs with
    /// <see cref="InReach"/>, which tests the scored set, and on a player-only-one-way tick that set is the
    /// raw one. Pairing a verdict with another set's exhaustion is how a tile the flood simply has not
    /// reached gets graded absent.</summary>
    public bool ReachComplete => reachSense?.ScoredComplete ?? false;

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
        reachSense = senses.Reach;
        if (lastTerrainRevision != TerrainChanges.Revision)
        {
            // Cadence may retain unchanged observations, never evidence from an edited world. The
            // flood's own reset for the same edit lives in the sense, so every consumer gets it.
            Chosen = null;
            lastTerrainRevision = TerrainChanges.Revision;
            // Remembered arc refusals are evidence about the world the solve ran in, so an edit throws them away
            // here rather than leaving each lookup to compare revisions. Comparing is not enough: the revision is
            // reset to a previous value when a world is rebuilt, and a stale refusal whose stamp happens to match
            // reads as a fact about terrain that no longer exists, writing off stands nothing ever solved against.
            refused.Clear();
            refusalOrder.Clear();
        }
        // New interference evidence reconsiders a held destination once. Waiting out the rescore cadence would let the
        // placement or the walk that produced it finish first, and rescoring every tick while it lasts would buy nothing.
        if (senses.Player.InterferenceRevision != lastInterferenceRevision)
        {
            lastInterferenceRevision = senses.Player.InterferenceRevision;
            sinceScore = RescoreInterval;
        }
        // The region ages once per resolve, whatever the request does this tick: counted inside the
        // rescore it multiplied the two cadences and refloods came every 144 ticks.
        senses.Reach.Age();
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
                senses.Reach.Refresh(senses);
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
                senses.Reach.Refresh(senses);
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
            senses.Reach.Refresh(senses);
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
        senses.Reach.Refresh(senses);
        // Retention is a rule at the rescore, not a bonus inside the scoring. A destination that still belongs to
        // the region it was admitted against is kept and nothing else is searched, so the revision names one
        // journey instead of a lattice resampled around a moving anchor every twelve ticks. Replacement happens
        // only where this test fails, which is the only moment a different place is actually needed.
        if (!kindChanged && RetainsHeldDestination(request, senses, fireProfile))
            return Chosen;
        Chosen = Best(request, senses, fireProfile);
        // Every non-null answer from Best passed acceptance against this call's player feet and anchor (the
        // incumbent and every sampled candidate are gated alike), so these are the references it was admitted
        // against even when the value did not change and the revision did not advance.
        Region = Chosen == null ? SuccessRegion.None
            // A partial-progress answer is the one destination that is deliberately outside the objective, so it
            // cannot declare the follow region: that would publish a contract the navigator cannot meet. It does
            // declare its own, the arrival radius around the tile it named, because being there is the whole of
            // what it claimed — and a destination declaring nothing is one an arrival can never be judged against.
            : ChoiceReason == "partial-progress-candidate"
                ? SuccessRegion.Partial(Chosen.Value, senses.Tick, TerrainChanges.Revision)
            : request.Kind == RequestKind.WithPlayer
                ? SuccessRegion.Follow(senses.Intent.Objective.At(request.Anchor), senses.Tick, TerrainChanges.Revision)
                : SuccessRegion.Unscored(SuccessRegionKind.FiringPosition, request.Anchor, senses.Tick, TerrainChanges.Revision);
        return Chosen;
    }

    /// <summary>Where the target stood when the held firing destination was admitted, so the hold can tell a target that
    /// has drifted a little from one that has moved somewhere the arc was never proved against.</summary>
    private Vector2 admittedTargetCentre;

    /// <summary>
    /// Whether the destination already held still belongs to the region it was admitted against, in which case it is
    /// kept and no search runs. Each kind's membership is its own: a follow spot is inside the comfort box the
    /// objective admits against now, a firing stand has an arc against a target that has not moved past the hold
    /// slack, and a partial-progress tile is still somewhere the body has not reached and still closes the gap.
    /// Every kind additionally needs the tile to be standable, un-banned and not proven out of the reachable region,
    /// because a destination the body cannot get to is not a destination however well it once served the purpose.
    /// </summary>
    private bool RetainsHeldDestination(in PositionRequest request, Senses.Senses senses, WeaponProfile? fireProfile)
    {
        if (Chosen is not Vector2 spot) return false;
        Point tile = MovementQueries.FeetTile(spot);
        if (!Allowed(tile) || !MovementQueries.IsStandable(tile.X, tile.Y) || ProvenUnreachable(tile))
            return false;
        switch (Region.Kind)
        {
            case SuccessRegionKind.FollowComfort:
                if (request.Kind != RequestKind.WithPlayer) return false;
                var objective = senses.Intent.Objective.At(request.Anchor);
                if (!objective.AcceptsDestination(spot, CanSeePlayer(spot + new Vector2(0f, -30f), senses)))
                    return false;
                if (StandsInPlayersWay(spot, senses)) return false;
                ChoiceReason = PositionReasons.Retained;
                Region = SuccessRegion.Follow(objective, senses.Tick, TerrainChanges.Revision);
                return true;
            case SuccessRegionKind.FiringPosition:
                // The kind declares no box because the arc belongs to a moving target, so membership is the arc
                // itself. Re-proving it costs one solve and it is taken outside the shortlist budget, which is
                // what makes a budget cut incapable of dropping a stand that still works.
                if (request.Target is not { active: true, life: > 0 } enemy || fireProfile is not { } profile)
                    return false;
                if (Vector2.DistanceSquared(enemy.Center, admittedTargetCentre)
                    > Weights.FiringHoldTargetSlackPx * Weights.FiringHoldTargetSlackPx)
                    return false;
                var held = SolveShotAtArrival(spot + new Vector2(0f, -30f), spot, enemy, profile, senses);
                if (!held.Solved)
                {
                    RememberRefusal(tile, enemy, BodyBucket(senses.Companion.Bottom),
                        held.Reason.StartsWith(PositionReasons.ShotWindowShorterThanTrip, StringComparison.Ordinal));
                    return false;
                }
                ChoiceReason = PositionReasons.Retained;
                admittedTargetCentre = enemy.Center;
                Region = SuccessRegion.Unscored(SuccessRegionKind.FiringPosition, request.Anchor, senses.Tick, TerrainChanges.Revision);
                return true;
            case SuccessRegionKind.PartialProgress:
                if (request.Kind != RequestKind.WithPlayer) return false;
                // The tile exists only because nothing satisfied the objective, so a body that now satisfies it has
                // no reason to walk anywhere: retaining on the gap alone would send it on to a tile whose only
                // merit is a smaller gap than one already small enough. This flag is computed above, on this tick.
                if (FollowObjectiveSatisfied) return false;
                if (!ImprovesOnStandingHere(senses.Intent.Objective.At(request.Anchor), senses.Companion.Bottom, spot))
                    return false;
                if (StandsInPlayersWay(spot, senses)) return false;
                ChoiceReason = "partial-progress-candidate";
                Region = SuccessRegion.Partial(spot, senses.Tick, TerrainChanges.Revision);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Whether a fallback tile is worth walking to from where the body actually stands: further than the navigator
    /// will call arrived, and strictly closer on the objective's own two axes. Both halves are measured from the live
    /// feet rather than from their tile, which is the arithmetic that produced the fixed point — the quantised feet
    /// can sit most of a tile from the body, so a tile that "improves" on them can be one the navigator is already
    /// arrived at, and the fallback then hands back the tile the body is standing on for as long as the gap lasts.
    /// </summary>
    private static bool ImprovesOnStandingHere(in FollowPlayerObjective objective, Vector2 feet, Vector2 destination)
        => Vector2.Distance(destination, feet) > Navigator.ArriveDistance
            && objective.HorizontalGap(destination) + objective.VerticalGap(destination)
                < objective.HorizontalGap(feet) + objective.VerticalGap(feet);

    private bool InReach(Point tile) => reachSense != null && reachSense.InScoredRegion(tile);

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
        var objective = senses.Intent.Objective.At(request.Anchor);
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
        senses.Reach.Refresh(senses);
        return senses.Reach.Returnable(tile);
    }
    public float? EstimatedTravelTicks(Point from, Point tile) => reachSense?.EstimatedTravelTicks(from, tile);

    /// <summary>
    /// The refusing flood did not hold the player, so the region being scored is the raw one and
    /// the companion is willing to go somewhere it cannot come back from. True is not a fault: it
    /// is the companion following the player into a place he chose to be. It is worth recording
    /// because it is the one state where prevention is deliberately switched off.
    /// </summary>
    public bool PlayerOnlyOneWay => reachSense?.PlayerOnlyOneWay ?? false;

    /// <summary>How many tiles the body can reach at all, and how many of those it can come home from.</summary>
    public int ReachCount => reachSense?.AnyCount ?? 0;
    public int ReturnableCount => reachSense?.TwoWayCount ?? 0;

    /// <summary>Whether the spot last chosen is one the body can come home from; true when nothing is chosen.</summary>
    public bool ChosenReturnable
        => Chosen is not Vector2 c || reachSense == null || reachSense.Returnable(MovementQueries.FeetTile(c));

    /// <summary>Wall-clock of the last reach flood, for the telemetry.</summary>
    public double LastFloodMs => reachSense?.LastFloodMs ?? 0d;

    /// <summary>
    /// The farthest of a handful of reachable tiles drawn at random, which is far without being
    /// the same far corner every time: a pocket is walked end to end over a few picks rather
    /// than paced between its two ends. Banned tiles and the tile underfoot are not offered.
    /// </summary>
    private Vector2? RoamSpot(Point feet)
    {
        if (reachSense == null || reachSense.AnyCount < 2)
            return null;
        var tiles = new List<Point>(reachSense.ScoredTiles);
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

    /// <summary>
    /// The reachable standable tile that most reduces the follow objective's own two gaps, or nothing when
    /// none improves on where the body already stands. Measured as horizontal plus vertical gap rather than
    /// straight-line distance, because that is what the objective is satisfied by: a tile on another floor
    /// can be nearer as the crow flies and further from being with the player. Only tiles the flood has
    /// actually claimed qualify, so this is a proven destination and not a hopeful direction.
    ///
    /// Both tests are taken from the body's live feet rather than from their tile, and that is the fix rather
    /// than a tidy-up. The tile's own feet position can sit most of a tile from the body, so a tile that beat
    /// the quantised position could be one the navigator was already arrived at: the 13:27 capture holds a
    /// 328-row stretch at one spot with the navigator Arrived, the follow gap open and the flood complete, and
    /// twenty-two more of the same shape. An answer must therefore be further away than the navigator's own
    /// arrival radius as well as strictly closer on the objective, or there is no answer and the unresolved
    /// follow intent goes to the shared state search, which is where it was always meant to go.
    /// </summary>
    private Vector2? PartialProgress(FollowPlayerObjective objective, Vector2 feetNow)
    {
        float best = objective.HorizontalGap(feetNow) + objective.VerticalGap(feetNow);
        Vector2? found = null;
        foreach (Point tile in reachSense?.ScoredTiles ?? (IReadOnlyCollection<Point>)System.Array.Empty<Point>())
        {
            if (!Allowed(tile) || !MovementQueries.IsStandable(tile.X, tile.Y)) continue;
            Vector2 feet = MovementQueries.FeetWorld(tile);
            if (Vector2.Distance(feet, feetNow) <= Navigator.ArriveDistance) continue;
            float gap = objective.HorizontalGap(feet) + objective.VerticalGap(feet);
            if (gap >= best) continue;
            best = gap;
            found = feet;
        }
        return found;
    }

    /// <summary>What one stand's trajectory solve established, including whether the refusal is a fact about the
    /// geometry or only about the wait: a stand with no arc now has none whatever the trip, while a stand whose arc
    /// closes before the body could arrive is refused for this trip and may be fine from closer.</summary>
    private readonly record struct ShotVerdict(bool Solved, string Reason, int TripTicks);

    /// <summary>
    /// Whether this stand can shoot the target once the body has walked to it. The stand's arc used to be solved
    /// against the target's current centre, which is a question about a slime that will not be there: the shot was
    /// proved at the moment of choosing and the body arrived a trip later. It is now solved against the forecast at
    /// the estimated arrival, and then at samples across a short window after it, and a stand whose shot holds for
    /// less than the trip is refused — because a shot that closes before the body gets there was never a reason to go.
    ///
    /// Where the forecast carries too little measured confidence to be evidence, the current position is asked
    /// instead and the reason says so, since refusing a stand on an unmeasured guess is worse than the staleness it
    /// replaces. The arrival sample is taken first and the window samples only on a pass, because three solves per
    /// candidate under an unchanged millisecond budget would otherwise cut the shortlist to a third of its depth.
    /// </summary>
    private ShotVerdict SolveShotAtArrival(Vector2 eye, Vector2 feet, NPC target, WeaponProfile profile, Senses.Senses senses)
    {
        Point from = MovementQueries.FeetTile(senses.Companion.Bottom);
        float trip = EstimatedTravelTicks(from, MovementQueries.FeetTile(feet))
            ?? Vector2.Distance(senses.Companion.Bottom, feet) / Companion.CompanionMotor.WalkSpeed;
        int arrival = (int)MathHelper.Clamp(trip, 0f, 180f);
        bool forecastUsable = PredictObservedMotion.ErrorSamples(target) > 0
            && PredictObservedMotion.Confidence(target, arrival) >= Weights.ShotForecastConfidenceFloor;
        if (!forecastUsable || arrival <= 0)
        {
            bool now = TrajectoryAimer.Solve(eye, target, profile) != null;
            return new(now, now ? (forecastUsable ? "clear-arc" : "clear-arc-unforecast") : "no-arc", arrival);
        }
        if (TrajectoryAimer.Solve(eye, target, profile, arrival) == null)
            return new(false, "no-arc", arrival);
        // The window is the trip's own length, bounded above so a walk across the world does not ask
        // for a shot that holds for ever. The rule as it stood read the other way round: it took the
        // trip only to choose between one sample and two, and then required the arc to hold for a
        // fixed twenty or forty ticks *after* arrival however long the trip was — so a long trip was
        // held to the same short window as a middling one, and a trip shorter than one sample interval
        // was never tested at all, because the loop started at the interval and any trip under it made
        // the body never execute. A quick step to a stand was therefore accepted on the arrival solve
        // alone, which is exactly the case the samples exist for: the shot has to survive the walk,
        // and a short walk is still a walk.
        int window = ShotWindow(trip);
        foreach (int held in ShotWindowOffsets(trip))
            if (TrajectoryAimer.Solve(eye, target, profile, Math.Min(180, arrival + held)) == null)
                return new(false, WindowRefusal(window), arrival);
        return new(true, "clear-arc", arrival);
    }

    /// <summary>
    /// How long past the estimated arrival a stand's arc has to hold: the trip's own length, bounded. A shot has to
    /// survive the walk to the stand, and nothing beyond that is the stand's business.
    /// </summary>
    public static int ShotWindow(float trip) => (int)MathF.Min(MathF.Max(0f, trip), Weights.ShotWindowCapTicks);

    /// <summary>
    /// Which ticks past arrival the arc is actually asked about, as a pure function of the trip, so the sampling
    /// rule can be read off directly rather than inferred from a scene that happens to have the right geometry.
    ///
    /// <para>Every sample interval up to the window, and then the window's own end whenever the interval does not
    /// land on it. That last clause is the one the rule was missing and it is not an edge case: the loop it replaces
    /// began at one whole interval, so <b>any trip shorter than an interval produced no samples at all</b> and a
    /// short walk to a stand was accepted on the arrival solve alone — which is the case the samples exist for,
    /// since a short walk is still a walk and the shot still has to survive it. The same clause covers every window
    /// the interval does not divide, where sampling only the multiples leaves the last stretch unasked.</para>
    ///
    /// <para>The old rule also read the trip backwards. It took <c>min(trip, 2 x interval)</c> as a *requirement*
    /// the arc had to hold for after arrival, so a long trip was held to the same short window as a middling one;
    /// the trip only ever chose between one sample and two. The trip is the window now, and the cap only stops a
    /// walk across the world asking for an arc that holds indefinitely — a question the forecast cannot answer at
    /// that range anyway.</para>
    /// </summary>
    public static IEnumerable<int> ShotWindowOffsets(float trip)
    {
        int window = ShotWindow(trip);
        int last = 0;
        for (int held = Weights.ShotWindowSampleTicks; held <= window; held += Weights.ShotWindowSampleTicks)
        {
            last = held;
            yield return held;
        }
        if (window > last) yield return window;
    }

    /// <summary>The refusal, carrying the window it was measured over. A refusal that names only itself cannot be
    /// read back against the trip it was about, and the window is the whole of what the rule now asserts; the token
    /// stays the leading part of the string so anything matching on the name keeps matching.</summary>
    /// <summary>
    /// Whether every candidate from <paramref name="from"/> onward has already been asked about this target, so a
    /// pass that stops here has an answer for the whole shortlist even though it did not reach the end of it.
    ///
    /// <para>This is what lets a search finish while the body is walking. The stop itself is not the question — a
    /// budget always runs out somewhere — and the only thing that matters is whether anything is left unasked
    /// behind it. Reading the stop as an unfinished search regardless is what made an undecided answer permanent
    /// for a moving companion, because the memory stopped counting each time the body changed bucket and the cut
    /// fell in the same place every pass.</para>
    /// </summary>
    private bool EveryRemainingAsked(List<(Vector2 feet, Vector2 eye, float baseScore)> candidates, int from, NPC target)
    {
        for (int i = from; i < candidates.Count; i++)
            if (!EverAsked(MovementQueries.FeetTile(candidates[i].feet), target)) return false;
        return true;
    }

    private static string WindowRefusal(int window)
        => FormattableString.Invariant($"{PositionReasons.ShotWindowShorterThanTrip}={window}");

    // Stands a real solve refused, scoped to the conditions the refusal was a fact under: this terrain, this target
    // generation standing about here, and a body standing about here. A refusal is a fact and may be remembered; an
    // unfinished search is not, which is why nothing records a candidate that was never asked. Without this the
    // shortlist cuts at the same place every rescore while body, target and terrain hold still, so an undecided
    // answer would be permanent in a static scene and the stand past the cut would never be reached.
    //
    // The body's position is part of the scope rather than a refinement of it. A stand can be refused because the
    // shot would close before the body could walk there, which is a fact about the trip and therefore about where
    // the body is standing — and the first version of this memory excluded those refusals for exactly that reason,
    // which turned out to break the property the memory exists for: a handful of trip-refused stands ranked at the
    // top of the shortlist are re-solved every pass for ever, nothing below them is ever reached, and the search
    // stays undecided permanently. Scoping the memory to where the body stands keeps every refusal honest and lets
    // the search finish, at the cost of re-asking once the body has moved, which is when the answer can differ.
    //
    // "Where the body stands" is a coarse bucket rather than its exact tile, and the coarseness is what makes the
    // scope usable at all rather than a refinement of it. The exact tile changes on almost every tick a walking
    // body is asked — `ResolveFiringOpportunity` keys its own cache in the same strides for the same reason — and
    // a hunt is challenged through `PrepareOffer` while some other activity walks the body, so an exact key would
    // discard every refusal the last few passes established and restart the shortlist from the top on each of
    // them. The search would then never exhaust while the body moved, which is the case this memory exists to
    // stop. Whether some stand can shoot an enemy does not change from one tile of travel.
    private readonly Dictionary<(Point tile, int slot, int generation), (int terrain, Vector2 target, Point from, bool aboutTheTrip)> refused = new();

    /// <summary>Where the body is standing, in the strides the refusal memory is scoped by.</summary>
    private static Point BodyBucket(Vector2 bottom)
    {
        Point feet = MovementQueries.FeetTile(bottom);
        return new Point(feet.X >> 2, feet.Y >> 2);
    }

    /// <summary>When each target was first refused anything, so the cap evicts a whole target rather than everybody.</summary>
    private readonly Dictionary<(int slot, int generation), int> refusalOrder = new();
    private readonly List<(Point tile, int slot, int generation)> evicting = new();
    private int refusalStamp;

    /// <summary>
    /// How many refusals are kept before the oldest target's are dropped. A cap is needed because the memory is
    /// keyed per target and a long fight produces targets; what it must not be is a flush.
    /// </summary>
    private const int RefusalCapacity = 512;

    private void RememberRefusal(Point tile, NPC target, Point from, bool aboutTheTrip)
    {
        var owner = (target.whoAmI, HostileAttackSources.Generation(target));
        if (!refusalOrder.ContainsKey(owner)) refusalOrder[owner] = refusalStamp++;
        // Evict the target that has been in the memory longest, entries and all, rather than clearing everything.
        // The cap used to flush the whole dictionary, which is an unscoped reset of the one structure that makes a
        // bounded search terminate: every target's exhaustion restarted at once, at a moment decided by an unrelated
        // target's entries arriving. A blood moon with several unshootable enemies in it reaches the cap easily, and
        // each time it did, every sweep in progress went back to its first stand. Evicting one target's entries costs
        // that target its progress, which is the smallest thing that can be lost and still make room.
        while (refused.Count > RefusalCapacity && refusalOrder.Count > 1)
        {
            (int slot, int generation) oldest = default;
            int oldestStamp = int.MaxValue;
            foreach (var entry in refusalOrder)
                if (entry.Value < oldestStamp) { oldestStamp = entry.Value; oldest = entry.Key; }
            // The target being refused right now is never the one evicted: dropping it would throw away the
            // entry about to be written along with everything the current sweep has established.
            if (oldest == owner) break;
            evicting.Clear();
            foreach (var key in refused.Keys)
                if (key.slot == oldest.slot && key.generation == oldest.generation) evicting.Add(key);
            foreach (var key in evicting) refused.Remove(key);
            refusalOrder.Remove(oldest);
        }
        refused[(tile, owner.Item1, owner.Item2)] = (TerrainChanges.Revision, target.Center, from, aboutTheTrip);
    }

    /// <summary>
    /// Whether a real solve refused this stand against this target in a way that still holds, so the verdict may be
    /// reused instead of paying for the solve again.
    ///
    /// <para>Which refusals survive the body moving is decided by what each one was about, and that distinction is
    /// the whole of it. A stand with no arc is a fact about the line between the stand and the target: the body's
    /// position is not in that question at all, so the refusal holds from anywhere and is reused from anywhere. A
    /// stand refused because the shot would close before the body could walk there is a fact about the trip, so it
    /// holds only from where that trip started and is re-asked once the body has moved, which is when the answer
    /// can differ.</para>
    ///
    /// <para>Scoping every refusal to the body was what stopped a moving companion's search from ever finishing,
    /// and not by the route it looks like. A remembered refusal is skipped without spending a solve, which is what
    /// lets a pass walk deeper than its budget; when the body moved, nothing was skipped, so all eight solves went
    /// to the top of the shortlist and the pass never reached the stands below however many passes ran. The count
    /// was never the binding constraint — the skipping was.</para>
    /// </summary>
    private bool AlreadyRefused(Point tile, NPC target, Point from)
        => RefusalMark(tile, target) is { } mark && (!mark.aboutTheTrip || mark.from == from);

    /// <summary>
    /// Whether this stand has been asked about at all for this target, wherever the body was standing at the time.
    /// This is the exhaustion question and it is deliberately a different question from the one above: "has the
    /// sweep been all the way round" is about the stands and the target, never about the body.
    ///
    /// <para>Keying exhaustion on the body's bucket as well is why a search could not finish while the body walked.
    /// A following body changes bucket every few rescores, and every change made the whole memory stop counting, so
    /// the shortlist re-cut at the same place and the stands past the cut were never reached — the search stayed
    /// permanently unfinished for as long as the companion was moving, which is most of the time a hunt matters.</para>
    ///
    /// <para>What this costs is worth naming rather than discovering. A stand refused because the shot would close
    /// before the body could walk there from somewhere else still counts as asked, so a sweep can complete on a
    /// refusal that a shorter walk might now beat. It is not lost: the stand is re-solved as soon as the budget
    /// reaches it from the new bucket, because the verdict above no longer matches. What the exhaustion flag claims
    /// is that every stand has an answer under this terrain and this target's position, and that is true.</para>
    /// </summary>
    private bool EverAsked(Point tile, NPC target) => RefusalMark(tile, target) != null;

    private (int terrain, Vector2 target, Point from, bool aboutTheTrip)? RefusalMark(Point tile, NPC target)
        => refused.TryGetValue((tile, target.whoAmI, HostileAttackSources.Generation(target)), out var mark)
            && mark.terrain == TerrainChanges.Revision
            && Vector2.DistanceSquared(mark.target, target.Center)
                <= Weights.FiringHoldTargetSlackPx * Weights.FiringHoldTargetSlackPx
            ? mark : null;

    private Vector2? Best(in PositionRequest request, Senses.Senses senses, WeaponProfile? fireProfile)
    {
        EvidenceTick = senses.Tick;
        CandidateEvidence = "";
        EvaluatedCandidates = 0;
        var evidence = new List<(Point tile, float score, string shot)>();
        // Nothing here favours the destination already held. Retention is decided before this runs and keeps a
        // still-valid destination without searching at all, so a search that reaches this point is one where the
        // held destination has just failed its own region — and preferring a place that has stopped working is
        // exactly wrong. The hysteresis this replaces was a 1.15 multiplier fighting a resampled lattice, which
        // is why the chosen spot still changed a thousand times under requests that had not changed.
        FollowPlayerObjective? followObjective = request.Kind == RequestKind.WithPlayer
            ? senses.Intent.Objective.At(request.Anchor) : null;
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
                float score = ScoreSpot(request, feet, eye, playerBottom, senses, bandNear, bandFar, SightToTarget(eye, request.Target), reach);
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
            // Following is the one request that must always produce somewhere to go. Its acceptance is a
            // region around the player, so a companion outside that region with no candidate inside it has
            // nothing accepted and used to answer nothing at all — which left the navigator with no proven
            // destination and the brain reaching for a state search that jumps at the player. A reachable
            // tile that closes the gap is not the destination the request wanted, and it is progress toward
            // it, which is strictly better than standing still or leaping. Other request kinds keep the
            // null: there is no partial credit for a firing position that cannot fire.
            if (followObjective is FollowPlayerObjective partial
                && PartialProgress(partial, senses.Companion.Bottom) is Vector2 step)
            {
                ChosenScore = 0f;
                ChoiceReason = "partial-progress-candidate";
                return step;
            }
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
        // Whether every candidate was actually answered. A candidate that was solved this pass, or that a real
        // solve refused on an earlier one, is answered; a candidate the solve count or the millisecond budget
        // never reached is not, and the difference is the whole of what the offer's third value means.
        bool everyCandidateAnswered = true;
        int solved = 0;
        // Where the body stands scopes every remembered refusal, because a stand refused for the length of the walk
        // to it is refused from here and may be fine from somewhere else; the stride is set by BodyBucket.
        Point bodyTile = BodyBucket(senses.Companion.Bottom);
        var solveClock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < candidates.Count; i++)
        {
            (Vector2 feet, Vector2 eye, float baseScore) = candidates[i];
            float score = baseScore;
            string shot = "not-required";
            if (needsFire)
            {
                Point tile = MovementQueries.FeetTile(feet);
                if (AlreadyRefused(tile, request.Target!, bodyTile))
                {
                    // Counted as evaluated because it is answered: a real solve refused it, under this terrain
                    // revision and against a target that has not moved since. The count means "candidates this
                    // pass has an answer for", which is what both the exhaustion test and the recorder need;
                    // reading it as "solves paid for" is what SolvesThisRescore is.
                    EvaluatedCandidates++;
                    evidence.Add((tile, 0f, "no-arc-remembered"));
                    evidence.Sort((a, b) => b.score.CompareTo(a.score));
                    if (evidence.Count > 4) evidence.RemoveAt(4);
                    continue;
                }
                if (solved > 0 && Infrastructure.Movement.LimitPlanningWork.Spent(solveClock, Weights.PositionAimingMilliseconds))
                { everyCandidateAnswered = EveryRemainingAsked(candidates, i, request.Target!); break; }
                if (solved >= solves)
                { everyCandidateAnswered = EveryRemainingAsked(candidates, i, request.Target!); break; } // unsolved candidates cannot beat a solved one above them
                solved++;
                var verdict = SolveShotAtArrival(eye, feet, request.Target!, fireProfile!.Value, senses);
                if (!verdict.Solved)
                    RememberRefusal(tile, request.Target!, bodyTile,
                        verdict.Reason.StartsWith(PositionReasons.ShotWindowShorterThanTrip, StringComparison.Ordinal));
                shot = verdict.Reason;
                score = ScoreSpot(request, feet, eye, playerBottom, senses, bandNear, bandFar, verdict.Solved ? 1f : 0f, reach);
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
        // The shortlist cap counts solves rather than list positions now, so remembered refusals do not consume the
        // budget they were already paid for. Without that a static scene re-cut at the same eight nearest stands for
        // ever and the ninth, which is the one that works, was never asked.
        SolvesThisRescore = solved;
        ChosenScore = bestScore;
        CandidateEvidence = string.Join("|", evidence.ConvertAll(e => FormattableString.Invariant($"{e.tile.X},{e.tile.Y}:{e.score:0.000}:{e.shot}")));
        ChoiceReason = best != null ? (anyReachable ? "reachable-candidate" : "reachability-unknown")
            // An exhausted bound is not a proven negative. The first of these says every shortlisted candidate was
            // asked and none works, which is a fact about the world; the second says the search stopped early, which
            // is a fact about the budget, and the chooser must not veto an activity on it.
            : everyCandidateAnswered ? PositionReasons.NoUsableDestination
            : PositionReasons.SearchUnfinished;
        if (best is Vector2 stand && needsFire && request.Target is { } chosenTarget)
            admittedTargetCentre = chosenTarget.Center;
        return best;
    }

    /// <summary>How many trajectory solves the last scored pass actually paid for, so the arrival-window cost is a
    /// measured number rather than a guess about how deep the shortlist now goes.</summary>
    public int SolvesThisRescore { get; private set; }

    public int EvidenceTick { get; private set; }
    public int EvaluatedCandidates { get; private set; }
    public string CandidateEvidence { get; private set; } = "";

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
        => StandsInPlayersWay(feet, senses) ? Weights.CourtesyOccupancyShare : 1f;

    /// <summary>
    /// Whether a body standing here would overlap the player's interference footprint. Retention of a follow
    /// destination asks this as well as the objective, because the objective is about being near the player and
    /// says nothing about being in the way: a spot the player is now walking through no longer belongs to the
    /// region it was admitted against, whatever its distance says. Without this the footprint's forced rescore
    /// above becomes a rescore that retains, which is no rescore at all, and the companion holds the tile the
    /// player is trying to walk down. Releasing it is not a veto — the scored pass that follows prices courtesy
    /// as a share, so the spot is chosen again where it is the only usable one.
    /// </summary>
    private static bool StandsInPlayersWay(Vector2 feet, Senses.Senses senses)
        => senses.Player.Interference is Rectangle footprint
            && PlayerSense.BodyTiles(feet, BodyPhysics.Width, BodyPhysics.Height).Intersects(footprint);

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
