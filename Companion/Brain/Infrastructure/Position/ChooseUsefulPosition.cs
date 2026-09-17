#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;
using AICompanion.Companion.Brain.Activities.Combat.Planning;
using Senses = AICompanion.Companion.Brain.Infrastructure.Observation;
using FlightModel = AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.FlightModel;

namespace AICompanion.Companion.Brain.Infrastructure.Position;

/// <summary>
/// Turns a position request into a point for the orb to hover at by scoring candidate spots.
/// Candidates are the usable corner nodes of the free-space graph around the request's anchor —
/// the same nodes the route search plans over, so a spot chosen here is one a route can end on —
/// under a ceiling above the player's feet; each scores on distance band to the player (tight
/// under threat), sight line to the player, line of fire to the target through the simulator, danger
/// from predicted threat paths, clearance from the walls and height beside the player, with the
/// weights the request kind sets. Re-scored every few ticks so the companion does not twitch
/// between two equal spots.
/// </summary>
public sealed class Positioner
{
    private const int RescoreInterval = 12;

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
    private int lastTerrainRevision = -1;
    private int lastInterferenceRevision;
    private int sinceScore = RescoreInterval;

    // The corners the orb can reach are the reach sense's, not this resolver's. A spot the body
    // cannot reach is not a spot: the fourth run of 2026-09-08 parked the walker above a sealed
    // cavity the scorer had picked. This resolver drives the sense's cadence — the region a
    // candidate is scored against has to hold still for the rescore, so it runs inside a resolve —
    // and every other consumer reads the same region instead of flooding its own.
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
    /// Refuse the spots in this tile for a while and pick again: the navigator made no progress on
    /// the way to it twice over, so the graph's opinion that it is reachable is wrong for the body
    /// in fact, and the companion is better off somewhere else than pinned. A spot is keyed by the
    /// tile its point floors into, which for a corner node is the tile below and right of it, and
    /// the navigator's ban names the same tile of the same point.
    /// </summary>
    public void Ban(Point tile, int ticks)
    {
        banned[tile] = clock + ticks;
        Chosen = null;
        sinceScore = RescoreInterval;
    }

    private bool Allowed(Point tile) => !banned.TryGetValue(tile, out int until) || until < clock;

    /// <summary>The held destination and everything describing it, so a query that must not disturb
    /// another activity's hold can put it all back. The reach sense's growth is not held: a query
    /// advancing the shared flood is progress every consumer reads, not a hold to restore.</summary>
    private (Vector2? Chosen, float ChosenScore, PositionRequest LastRequest,
        int LastTerrainRevision, int SinceScore, int LastInterferenceRevision,
        string ChoiceReason, bool FollowObjectiveSatisfied, float FollowHorizontalGap, float FollowVerticalGap,
        string FollowObjectiveReason, int CandidateCount, int ReachableCandidateCount, int RejectedCandidateCount,
        int EvidenceTick, int EvaluatedCandidates, string CandidateEvidence, long ChosenRevision, SuccessRegion Region)
        SaveHeld() => (Chosen, ChosenScore, lastRequest, lastTerrainRevision, sinceScore,
            lastInterferenceRevision,
            ChoiceReason, FollowObjectiveSatisfied, FollowHorizontalGap, FollowVerticalGap,
            FollowObjectiveReason, CandidateCount, ReachableCandidateCount, RejectedCandidateCount,
            EvidenceTick, EvaluatedCandidates, CandidateEvidence, ChosenRevision, Region);

    private void RestoreHeld((Vector2? Chosen, float ChosenScore, PositionRequest LastRequest,
        int LastTerrainRevision, int SinceScore, int LastInterferenceRevision,
        string ChoiceReason, bool FollowObjectiveSatisfied, float FollowHorizontalGap, float FollowVerticalGap,
        string FollowObjectiveReason, int CandidateCount, int ReachableCandidateCount, int RejectedCandidateCount,
        int EvidenceTick, int EvaluatedCandidates, string CandidateEvidence, long ChosenRevision, SuccessRegion Region) held)
    {
        // lastInterferenceRevision is restored with the rest, and it is the one piece of this state whose
        // consumption is not idempotent. A new footprint forces exactly one rescore, and Resolve spends that
        // force by stamping the revision as seen; a rejected query that put back sinceScore but not the stamp
        // therefore ate the forced rescore on behalf of whoever asked next. Keeping company resolving on the
        // same tick then saw no change, retained the tile it was standing on, and waited out the cadence in
        // the player's way — courtesy defeated by a combat query that momentarily won nomination and lost.
        (Chosen, ChosenScore, lastRequest, lastTerrainRevision, sinceScore,
            lastInterferenceRevision,
            ChoiceReason, FollowObjectiveSatisfied, FollowHorizontalGap, FollowVerticalGap,
            FollowObjectiveReason, CandidateCount, ReachableCandidateCount, RejectedCandidateCount,
            // The revision and region come last: restoring Chosen above advances the revision, and a
            // rejected query must leave the held destination's identity exactly as it found it.
            EvidenceTick, EvaluatedCandidates, CandidateEvidence, ChosenRevision, Region) = held;
    }

    /// <summary>Refine a nominated FireFrom method through the ordinary resolver. A rejected
    /// nomination must not erase another activity's held destination or its explanation.
    /// Reach-search work survives rejection so yielding cannot starve refinement.</summary>
    public PositionOffer PrepareOffer(in PositionRequest request, Senses.Senses senses)
    {
        if (request.Kind != RequestKind.FireFrom)
            throw new ArgumentException("Only the combat stance's firing stand requires this admission query.", nameof(request));
        reachSense = senses.Reach;
        var held = SaveHeld();
        int previousClock = clock;
        bool admitted = false;
        try
        {
            if (request.Target is not { } enemy || !enemy.CanBeChasedBy())
                return new(null, "attack-target-not-attackable", "", senses.Tick);
            // Where the body already hovers needs no flood to stay: the here-stand is reachable by
            // definition, the same exception AssessStands grants, or the first tick's unrooted flood —
            // and any reflood gap — reads the body's own tile as undecided and rejects a fight from here.
            bool here = Vector2.DistanceSquared(request.Anchor, senses.Companion.Center) <= 64f;
            if (!here && ReachOf(MovementQueries.Tile(request.Anchor)) == ReachVerdict.NotYet)
                return new(null, PositionReasons.FireStandUndecided, "", senses.Tick);
            Vector2? destination = Resolve(request, senses);
            admitted = destination != null;
            return new(destination, ChoiceReason, CandidateEvidence, EvidenceTick);
        }
        finally
        {
            // Querying candidates must not age temporary bans as extra executed ticks.
            clock = previousClock;
            if (!admitted)
                RestoreHeld(held);
        }
    }

    public Vector2? Resolve(in PositionRequest request, Senses.Senses senses)
    {
        reachSense = senses.Reach;
        if (lastTerrainRevision != TerrainChanges.Revision)
        {
            // Cadence may retain unchanged observations, never evidence from an edited world. The
            // flood's own reset for the same edit lives in the sense, so every consumer gets it.
            Chosen = null;
            lastTerrainRevision = TerrainChanges.Revision;
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
        switch (request.Kind)
        {
            case RequestKind.Hold:
                // A hold or an exact ends whatever was being held (a roam's spot, a scored spot),
                // so the next scored or roam request picks afresh instead of reading the spot an
                // interruption left in Chosen for the rest of a hold (Codex review of 2303802).
                lastRequest = request;
                Chosen = null;
                Region = SuccessRegion.None;
                // A hold still roots and replaces the reach flood. Keeping company hovers on a hold, and nothing else runs
                // while it does: a hold that skipped this left a companion with no flood at all asking for holds for ever,
                // because work offers nothing on an unanswered search and keeping company wins by default. The walker only
                // escaped that circle because its stroll's goal test happened to refresh the flood as a side effect.
                senses.Reach.Refresh(senses);
                return null;
            case RequestKind.Exact:
                // Exact means the point itself where the body fits there: a tool stand was proven
                // against the reach box at that point, and snapping it to a lattice node can move it
                // out of the box it was proven in. The point is refused only where the body overlaps
                // terrain there, where it is banned, or where a finished flood proves the corner
                // beside it out of reach; then the nearest hoverable tile the body can reach stands
                // in, and failing that the nearest hoverable tile at all, with the route flying as
                // close as it can.
                lastRequest = request;
                senses.Reach.Refresh(senses);
                Point around = MovementQueries.Tile(request.Anchor);
                Point? beside = MovementQueries.NearestUsableCorner(request.Anchor, 1, requireSweep: false);
                bool fits = !CircleContact.Overlaps(MovementQueries.World, request.Anchor) && Allowed(around)
                    && !(beside is Point b ? ProvenUnreachable(b) : ReachComplete);
                if (fits)
                    Chosen = request.Anchor;
                else
                {
                    Point? tile = MovementQueries.NearestHoverable(around, 3, t => InReach(t) && Allowed(t)) ?? MovementQueries.NearestHoverable(around, 3, Allowed);
                    Chosen = tile is Point t ? MovementQueries.HoverPoint(t) : null;
                }
                // Declared against the request's own stand rather than the substituted tile: the proof
                // chose the stand, and the tile nearest it is where the walk happens to aim.
                Region = Chosen == null ? SuccessRegion.None
                    : request.WorkTile is Point work ? SuccessRegion.ToolStand(request.Anchor, work, senses.Tick, TerrainChanges.Revision)
                    : SuccessRegion.Unscored(SuccessRegionKind.Undeclared, request.Anchor, senses.Tick, TerrainChanges.Revision);
                return Chosen;
            case RequestKind.FireFrom:
                // The combat stance's firing stand: the named point, hovered at, because the plan's uses
                // were priced from exactly there and a substituted tile is a different plan. Refused with
                // a reason when the stand is no longer reachable — the flood proves it out, the tile is
                // banned, or terrain now overlaps it — which the activity reads as plan invalidity.
                lastRequest = request;
                senses.Reach.Refresh(senses);
                Point fireTile = MovementQueries.Tile(request.Anchor);
                bool fireFits = !CircleContact.Overlaps(MovementQueries.World, request.Anchor) && Allowed(fireTile)
                    && ReachOf(fireTile) != ReachVerdict.Unreachable;
                LastResolveFailed = !fireFits;
                if (!fireFits)
                {
                    Chosen = null;
                    ChosenScore = 0f;
                    ChoiceReason = "fire-stand-unreachable";
                    CandidateEvidence = "";
                    EvaluatedCandidates = CandidateCount = ReachableCandidateCount = RejectedCandidateCount = 0;
                    EvidenceTick = senses.Tick;
                    Region = SuccessRegion.None;
                    return null;
                }
                Chosen = request.Anchor;
                ChosenScore = 1f;
                ChoiceReason = "fire-from-stand";
                CandidateEvidence = FormattableString.Invariant($"{fireTile.X},{fireTile.Y}:1.000:fire-stand");
                EvaluatedCandidates = CandidateCount = ReachableCandidateCount = 1;
                RejectedCandidateCount = 0;
                EvidenceTick = senses.Tick;
                Region = SuccessRegion.Unscored(SuccessRegionKind.Undeclared, request.Anchor, senses.Tick, TerrainChanges.Revision);
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
                Chosen = RoamSpot(MovementQueries.Tile(request.Anchor));
                Region = Chosen == null ? SuccessRegion.None
                    : SuccessRegion.Unscored(SuccessRegionKind.Undeclared, request.Anchor, senses.Tick, TerrainChanges.Revision);
                return Chosen;
        }

        if (request.Kind == RequestKind.WithPlayer && senses.Intent.Inside)
        {
            // Inside the player's region there is no place to choose: the brain moves the body about the region itself. The flood
            // is still refreshed, because that motion reads it to refuse places a finished flood proved out of reach and nothing
            // else roots it while the companion keeps him company. A destination held from outside is dropped, so the first tick
            // outside again chooses afresh instead of flying back to a place picked before the body entered.
            lastRequest = request;
            senses.Reach.Refresh(senses);
            Chosen = null;
            ChosenScore = 0f;
            ChoiceReason = "accompanying-inside-region";
            Region = SuccessRegion.None;
            return null;
        }

        if (request.Kind == RequestKind.WithPlayer && request.MeetingPlace)
        {
            // A priced meeting place is the destination itself. Scoring a region around it picked a
            // tile nearer the player whose best route went the other way, so the companion walked back
            // along the route the meeting place had been chosen to avoid. It still has to be a place to
            // stand that this region has not proven unreachable; otherwise ordinary scoring applies.
            senses.Reach.Refresh(senses);
            Point place = MovementQueries.Tile(request.Anchor);
            if (MovementQueries.IsHoverable(place) && Allowed(place) && !ProvenUnreachable(place)
                && CourtesyShare(MovementQueries.HoverPoint(place), senses) == 1f)
            {
                lastRequest = request;
                sinceScore = 0;
                Chosen = MovementQueries.HoverPoint(place);
                ChoiceReason = "priced-meeting-place";
                Region = SuccessRegion.Unscored(SuccessRegionKind.MeetingPlace, request.Anchor, senses.Tick, TerrainChanges.Revision);
                return Chosen;
            }
        }

        sinceScore++;
        // A dropped meeting place is a changed request: without this the scored path kept walking to the
        // dropped tile for the rest of the rescore cadence, even once it lay behind the player.
        bool kindChanged = request.Kind != lastRequest.Kind || request.Target != lastRequest.Target
            || request.MeetingPlace != lastRequest.MeetingPlace;
        if (Chosen != null && !kindChanged && sinceScore < RescoreInterval)
            return Chosen;

        lastRequest = request;
        sinceScore = 0;
        senses.Reach.Refresh(senses);
        // Retention is a rule at the rescore, not a bonus inside the scoring. A destination that still belongs to
        // the region it was admitted against is kept and nothing else is searched, so the revision names one
        // journey instead of a corner resampled around a moving anchor every twelve ticks. Replacement happens
        // only where this test fails, which is the only moment a different place is actually needed.
        if (!kindChanged && RetainsHeldDestination(request, senses))
            return Chosen;
        // Only WithPlayer reaches here: every other kind returns from its own case above. The way back in is
        // the nearest inside corner, and a follow request that accepts nothing answers nothing — the brain
        // hands the follow intent to the state search, because a substitute the navigator can reach without
        // satisfying the objective is a destination that is arrived at and achieves nothing, which is the fixed
        // point the walker's partial-progress fallback produced.
        Chosen = NearestInsideCorner(senses);
        Region = Chosen == null ? SuccessRegion.None
            : SuccessRegion.Follow(senses.Intent.Objective.At(request.Anchor), senses.Tick, TerrainChanges.Revision);
        return Chosen;
    }

    /// <summary>
    /// Where a companion outside the player's region rejoins it: the corner nearest the body, inside the region by the settle
    /// radius, that the flood holds; failing that, the nearest usable corner inside it the flood has not proven out of reach,
    /// so the route flies as close as it can while the flood grows. Tiles the player is building on or walking down are passed
    /// over. Nearest, because inside the region the companion moves about with the region and a place in it is only the way
    /// back in: scoring places there for band, height and openness was choosing where to arrive and stop, which the owner ruled
    /// on 15 September 2026 nothing does any more. Sight of the player is not asked, because losing it is not distance. A corner
    /// the intent sense has proven cut off from the player inside his region is passed over, because arriving there is not
    /// being with him: the nearest inside corner to a body on the wrong side of a wall is otherwise the one it already hovers at.
    /// </summary>
    private Vector2? NearestInsideCorner(Senses.Senses senses)
    {
        EvidenceTick = senses.Tick;
        CandidateEvidence = "";
        EvaluatedCandidates = 0;
        CandidateCount = ReachableCandidateCount = RejectedCandidateCount = 0;
        var region = senses.Intent.Region;
        float inset = Movement.Navigator.SettleRadius;
        Vector2 body = senses.Companion.Center;
        Point low = CornerGraph.NearestCorner(region.Centre - region.HalfSize + new Vector2(inset));
        Point high = CornerGraph.NearestCorner(region.Centre + region.HalfSize - new Vector2(inset));
        Vector2? reached = null, unproven = null;
        float reachedDistance = float.MaxValue, unprovenDistance = float.MaxValue;
        for (int x = low.X; x <= high.X; x++)
        {
            for (int y = low.Y; y <= high.Y; y++)
            {
                var corner = new Point(x, y);
                Vector2 spot = CornerGraph.ToWorld(corner);
                CandidateCount++;
                if (!region.Accepts(spot, inset) || !MovementQueries.IsUsableCorner(corner) || !Allowed(MovementQueries.Tile(spot))
                    || ProvenUnreachable(corner) || senses.Intent.ProvenCutOff(corner) || StandsInPlayersWay(spot, senses))
                {
                    RejectedCandidateCount++;
                    continue;
                }
                float distance = Vector2.DistanceSquared(spot, body);
                if (ReachesCorner(corner))
                {
                    ReachableCandidateCount++;
                    if (distance < reachedDistance) { reachedDistance = distance; reached = spot; }
                }
                else if (distance < unprovenDistance)
                {
                    unprovenDistance = distance;
                    unproven = spot;
                }
            }
        }
        ChosenScore = reached != null || unproven != null ? 1f : -1f;
        ChoiceReason = reached != null ? "nearest-reached-inside-region"
            : unproven != null ? "nearest-unproven-inside-region"
            : "no-accepted-candidate";
        return reached ?? unproven;
    }

    /// <summary>
    /// Whether the destination already held still belongs to the region it was admitted against, in which case it is
    /// kept and no search runs: a follow spot is inside the comfort box the objective admits against now. The
    /// corner must also be usable, un-banned and not proven out of the reachable region, because a destination
    /// the body cannot get to is not a destination however well it once served the purpose. The corner is tested
    /// rather than the tile the point floors into, because a point on a tile boundary floors into one of four
    /// tiles and a hoverable test on that one tile can refuse a spot the body fits at.
    /// </summary>
    private bool RetainsHeldDestination(in PositionRequest request, Senses.Senses senses)
    {
        if (Chosen is not Vector2 spot) return false;
        Point tile = MovementQueries.Tile(spot);
        Point corner = CornerGraph.NearestCorner(spot);
        if (!Allowed(tile) || !MovementQueries.IsUsableCorner(corner) || ProvenUnreachable(corner))
            return false;
        switch (Region.Kind)
        {
            case SuccessRegionKind.FollowComfort:
                if (request.Kind != RequestKind.WithPlayer) return false;
                var objective = senses.Intent.Objective.At(request.Anchor);
                if (!objective.AcceptsDestination(spot, CanSeePlayer(spot, senses)))
                    return false;
                if (StandsInPlayersWay(spot, senses)) return false;
                ChoiceReason = PositionReasons.Retained;
                Region = SuccessRegion.Follow(objective, senses.Tick, TerrainChanges.Revision);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Whether the flood holds any corner of this tile; the tile-shaped question the exact and roam kinds and the brain ask.</summary>
    private bool InReach(Point tile) => reachSense != null && reachSense.InScoredRegion(tile);

    /// <summary>Whether the flood holds this corner node itself. A tile with one reached corner is not the same as
    /// this corner being reached, and a scored candidate is a corner, so it is asked about as one.</summary>
    private bool ReachesCorner(Point corner) => reachSense != null && reachSense.ReachesCorner(corner);

    /// <summary>
    /// A missing corner in an unfinished flood is unknown, not unreachable. Following may therefore begin toward a
    /// useful spot while the bounded flood is still expanding; only an exhausted region can reject it as absent.
    /// </summary>
    /// <summary>A corner the finished flood never claimed inside its known radius; beyond that radius nothing is proven.</summary>
    private bool ProvenUnreachable(Point corner) => reachSense?.ProvenUnreachableCorner(corner) ?? false;

    /// <summary>A tile the reach sense has proven absent, for a caller deciding whether an unreached stand is a proven refusal or a not-yet.</summary>
    public bool ProvenUnreachableTile(Point tile) => reachSense?.Reachable(tile) == ReachVerdict.Unreachable;

    /// <summary>The reach sense's three-valued verdict for a tile: reached, proven absent, or not yet known.</summary>
    public ReachVerdict ReachOf(Point tile) => reachSense?.Reachable(tile) ?? ReachVerdict.NotYet;

    /// <summary>
    /// One verdict per proposed firing stand, in one batch: the reach sense's three-valued answer and
    /// travel estimate from the body, the predicted harm at the stand and sampled along the travel
    /// line, and the allowance. Reads only what the positioner owns plus the body's point and the
    /// activity's allowance, which travel with the call; it runs no route search, so the navigation
    /// boundary is unchanged. Where the body already hovers is reachable with no travel by
    /// definition, whatever the flood has claimed so far — no route is needed to stay.
    /// </summary>
    public void AssessStands(IReadOnlyList<StandProposal> stands, Vector2 body, Senses.Senses senses,
        float companionLife, Func<Vector2, bool> inAllowance, List<StandVerdict> into)
    {
        reachSense = senses.Reach;
        Point feet = MovementQueries.Tile(body);
        foreach (StandProposal proposal in stands)
        {
            Vector2 stand = proposal.Stand;
            bool here = Vector2.DistanceSquared(stand, body) <= 64f;
            Point tile = MovementQueries.Tile(stand);
            ReachVerdict reach = here ? ReachVerdict.Reachable : ReachOf(tile);
            float travel = 0f;
            if (!here)
                travel = EstimatedTravelTicks(feet, tile)
                    ?? Vector2.Distance(body, stand) / OrbPace.MaxSpeed;
            float atStand = PredictedHarmAt(stand, senses, companionLife);
            float alongTravel = 0f;
            for (int sample = 1; sample <= 4; sample++)
            {
                Vector2 point = Vector2.Lerp(body, stand, sample / 5f);
                alongTravel = MathF.Max(alongTravel, PredictedHarmAt(point, senses, companionLife));
            }
            bool allowed = inAllowance(stand);
            string reason = reach switch
            {
                ReachVerdict.Reachable => allowed ? "reachable-stand" : "stand-outside-allowance",
                ReachVerdict.NotYet => "stand-undecided",
                _ => "stand-unreachable",
            };
            into.Add(new StandVerdict(stand, reach, MathF.Max(0f, travel), atStand, alongTravel, allowed, reason));
        }
    }

    /// <summary>Whether the last FireFrom resolution refused its stand; the combat activity reads it as plan invalidity.</summary>
    public bool LastResolveFailed { get; private set; }

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
        Vector2 centre = senses.Companion.Center;
        FollowObjectiveSatisfied = objective.IsSatisfied(centre);
        FollowHorizontalGap = objective.HorizontalGap(centre);
        FollowVerticalGap = objective.VerticalGap(centre);
        FollowObjectiveReason = objective.Reason(centre);
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
        => Chosen is not Vector2 c || reachSense == null || reachSense.Returnable(MovementQueries.Tile(c));

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
        return best is Point b ? MovementQueries.HoverPoint(b) : null;
    }

    private const int RoamSamples = 12;

    public int EvidenceTick { get; private set; }
    public int EvaluatedCandidates { get; private set; }
    public string CandidateEvidence { get; private set; } = "";
    /// <summary>
    /// The share of its score a follow spot keeps when a body standing there would overlap the player's interference footprint
    /// (a block or wall aimed at the companion, or a passage the player is walking down). It is a factor rather than a veto:
    /// among useful spots, one out of the player's way wins, and a spot that is the only usable one stays usable. Attack
    /// positions do not read it, because protection is not priced against courtesy.
    /// </summary>
    private static float CourtesyShare(Vector2 spot, Senses.Senses senses)
        => StandsInPlayersWay(spot, senses) ? Weights.CourtesyOccupancyShare : 1f;

    /// <summary>
    /// Whether a body standing here would overlap the player's interference footprint. Retention of a follow
    /// destination asks this as well as the objective, because the objective is about being near the player and
    /// says nothing about being in the way: a spot the player is now walking through no longer belongs to the
    /// region it was admitted against, whatever its distance says. Without this the footprint's forced rescore
    /// above becomes a rescore that retains, which is no rescore at all, and the companion holds the tile the
    /// player is trying to walk down. Releasing it is not a veto — the scored pass that follows prices courtesy
    /// as a share, so the spot is chosen again where it is the only usable one.
    /// </summary>
    private static bool StandsInPlayersWay(Vector2 spot, Senses.Senses senses)
        => senses.Player.Interference is Rectangle footprint
            && PlayerSense.BodyTiles(spot + new Vector2(0f, CircleContact.Radius), (int)CircleContact.Diameter, (int)CircleContact.Diameter).Intersects(footprint);

    /// <summary>The orb's own box against the player's, which is the line the brain tick tests arrival with.</summary>
    private static bool CanSeePlayer(Vector2 spot, Senses.Senses senses)
        => Collision.CanHitLine(spot - new Vector2(CircleContact.Radius), (int)CircleContact.Diameter, (int)CircleContact.Diameter,
            senses.PlayerEntity.position, senses.PlayerEntity.width, senses.PlayerEntity.height);

    /// <summary>0..1: how much of the next second's predicted threat paths pass through this spot, the body being the
    /// orb's own box centred on it.</summary>
    public static float PredictedExposureAt(Vector2 centre, Senses.Senses senses)
    {
        float worst = 0f;
        Rectangle body = new((int)(centre.X - CircleContact.Radius), (int)(centre.Y - CircleContact.Radius), (int)CircleContact.Diameter, (int)CircleContact.Diameter);
        foreach (ThreatRecord t in senses.Threats.Threats)
            worst = MathF.Max(worst, ThreatProximity(t, centre, body, 1f));
        return worst;
    }

    /// <summary>
    /// The expected hit at this spot as a share of the companion's life: each threat's proximity times the
    /// hit it lands on the body, summed. A sum rather than the exposure times the hardest hit anywhere,
    /// because that product prices a distant lethal into every stand — a damage-100 body thirty tiles off
    /// set the price of hits the mild zombie at the stand would land, and no fight beside any lethal was
    /// ever worth starting. The distance term is gated by the threat's urgency to the companion, because a
    /// body busy with the player is not incoming however near; a predicted hitbox overlap is not gated,
    /// because a forecasted overlap is incoming by construction.
    /// </summary>
    public static float PredictedHarmAt(Vector2 centre, Senses.Senses senses, float companionLife)
    {
        float total = 0f;
        Rectangle body = new((int)(centre.X - CircleContact.Radius), (int)(centre.Y - CircleContact.Radius), (int)CircleContact.Diameter, (int)CircleContact.Diameter);
        foreach (ThreatRecord t in senses.Threats.Threats)
            total += ThreatProximity(t, centre, body, Math.Clamp(t.UrgencyToCompanion, 0f, 1f)) * MathF.Max(0f, t.EffectiveDamageToCompanion);
        return total / MathF.Max(1f, companionLife);
    }

    private static float ThreatProximity(ThreatRecord t, Vector2 centre, Rectangle body, float urgencyGate)
    {
        for (int tick = 0; tick <= 40; tick += 10)
            if (t.PredictedHitbox(tick).Intersects(body))
                return 1f;
        float d = Vector2.Distance(t.Npc.Center, centre);
        return Consideration.Inverse(d, 160f) * 0.6f * urgencyGate;
    }
}
