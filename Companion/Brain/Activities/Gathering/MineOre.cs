#nullable enable

using FindToolAccess = AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Activities;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Mining;

namespace AICompanion.Companion.Brain.Activities.Gathering;

/// <summary>
/// Retains one ore-only job. Opportunistic mode can start from a vein near either body;
/// mimic mode uses the player's recent ore contact as its trigger. Both clear every
/// reachable tile of the selected vein, including the player's vein, and never excavate
/// terrain merely to make an approach. The body hovers beside the ore within tool reach;
/// there is no pose to stand in and no jump to prove, so a tile is workable from any cell
/// the flood reaches whose centre sees an exposed face of it.
/// </summary>
public sealed class MineOre : CompanionAction
{
    public override string Name => "mine";
    public override PurposeFamily Family => PurposeFamily.Gathering;
    public override Vector2? ActivityTarget => preparedTarget;
    private Vector2? preparedTarget;
    private float preparedValue, preparedTrip;
    private Infrastructure.Interactions.BindTileTarget? preparedTile;
    public override string PreparedTargetRejection => WorkPolicies.Mining == WorkPolicy.Disabled ? "work-disabled" : preparedTile is { } bound
        ? target?.Tile != bound.Tile ? "prepared-target-changed" : bound.Rejection : "";

    private const int KeepJobTicks = 600;
    private const int SearchRadiusTiles = 45;
    private const int SearchEveryTicks = 60;

    private OreFinder.OreTarget? target;
    private HashSet<Point> patch = new();
    private HashSet<Point> jobTiles = new();
    private readonly HashSet<Point> ownRemovals = new();
    private int jobType;
    private int nextJobId = 1;
    private int jobId;
    private string status = "idle";
    private int sinceSearch = SearchEveryTicks;
    private Point? approachOrigin;
    private int approachRevision;
    private int approachPickPower;
    private (int X, int Y) approachReach;

    private bool swinging;

    public int JobId => jobId;
    public DescribeOreJobEnd? LastConclusion { get; private set; }
    /// <summary>True only while the pickaxe is actually out; the whole flight to the vein is empty-handed.</summary>
    public override bool HandsBusy => swinging;
    public override object? ActivityIdentity => jobId > 0 ? jobId : null;
    public WorkPolicy Policy => WorkPolicies.Mining;
    public string Status => status;
    public int RemainingTiles => patch.Count;
    public Point? TargetTile => target?.Tile;
    public Vector2? TargetStandPosition => target?.StandPosition;
    public Infrastructure.Interactions.RemainingToolWork? RemainingWork { get; private set; }

    public override void Prepare(in ActionContext ctx)
    {
        preparedValue = DiscoverValue(ctx);
        preparedTarget = target?.Tile.ToWorldCoordinates();
        RemainingWork = target is { } workTarget
            ? ctx.Companion.Miner.EstimateRemaining(workTarget.Tile, TileMiner.PickaxeFor(ctx.Player)) : null;
        if (target != null && RemainingWork == null)
        {
            preparedValue = 0;
            Classify(OfferEligibility.KnownUnusable, "tool-cannot-damage-target");
        }
        preparedTrip = target is { } found
            ? Vector2.Distance(ctx.Npc.Center, found.StandPosition) / OrbPace.MaxSpeed + (RemainingWork?.Ticks ?? 0f)
            : 0f;
        preparedTile = preparedValue <= 0 || target is not { } boundTarget
            ? null
            : new Infrastructure.Interactions.BindTileTarget(boundTarget.Tile, boundTarget.Type);
    }

    public override float Score() => preparedValue;

    /// <summary>
    /// A cleared tracked vein completes the attempt only when this attempt itself produced an
    /// observed effect: the same empty coordinates reached through the player's pickaxe change the
    /// remaining work and earn the companion nothing. A job ended by lost permission or changed
    /// material is invalid rather than failed, because the method never got to prove itself.
    /// </summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
    {
        // A job conclusion counts only if it was captured after this attempt opened.
        if (conclusionSequence > attemptConclusionBase && LastConclusion is { } end)
        {
            if (end.ObservedClear)
            {
                if (productiveEffects == 0) return new(AttemptStatus.Invalid, "tracked-vein-cleared-without-companion-effect");
                // Every tracked site is empty but ore of the same kind still touches one: the vein continues past what the
                // job tracked, so the work is a finished portion. Calling it complete is a completion the world contradicts.
                if (!end.VeinObservedClear) return new(AttemptStatus.Partial, "tracked-portion-clear-vein-continues");
                // Every tracked site removed by the companion's own strikes finished the job alone;
                // any site that vanished some other way means it was finished together.
                return new(AttemptStatus.Complete, "tracked-vein-observed-clear",
                    end.CompanionRemovals == end.Tracked ? AttemptAttribution.Companion : AttemptAttribution.Shared);
            }
            if (productiveEffects > 0) return new(AttemptStatus.Partial, end.Reason);
            return new(end.Reason is NoProvenPoseReason ? AttemptStatus.Failed : AttemptStatus.Invalid, end.Reason);
        }
        if (attemptSetback is { } setback)
            return productiveEffects > 0 ? new(AttemptStatus.Partial, setback.Cause) : new(setback.Status, setback.Cause);
        return productiveEffects > 0
            ? new(AttemptStatus.Partial, "replaced-with-vein-remaining")
            : new(AttemptStatus.Attempted, "replaced-before-productive-effect");
    }

    public override void BeginAttempt()
    {
        attemptConclusionBase = conclusionSequence;
        attemptSetback = null;
    }

    private const string NoProvenPoseReason = "remaining ore has no reachable working cell";
    // Incremented each time a job conclusion is captured; an attempt reads a conclusion only if the
    // sequence moved after it opened, which is what a same-tick clear-then-rediscover needs.
    private int conclusionSequence, attemptConclusionBase;
    // A method-level setback this attempt suffered, recorded where it happens rather than inferred
    // later from a discovery status string that the next preparation may already have replaced.
    private (AttemptStatus Status, string Cause)? attemptSetback;

    private float DiscoverValue(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsDead)
        {
            Classify(OfferEligibility.NoOpportunity, "player-dead");
            return 0f;
        }
        sinceSearch++;

        if (WorkPolicies.Mining == WorkPolicy.Disabled)
        {
            ClearJob("disabled");
            Classify(OfferEligibility.PolicyForbidden, "mining-disabled");
            return 0f;
        }
        bool playerMining = p.MinedOre != null || TileDamageWatcher.TicksSinceOreHit <= KeepJobTicks;
        if (WorkPolicies.Mining == WorkPolicy.Mimic && !playerMining)
        {
            ClearJob("mimic trigger expired");
            Classify(OfferEligibility.PolicyForbidden, "mimic-awaiting-player-ore-contact");
            return 0f;
        }
        int pick = TileMiner.PickaxeFor(ctx.Player).pick;
        if (patch.Count > 0)
        {
            // Ore removed or transformed by anyone leaves the working set on the preparation that observes it, so the
            // remaining count stays true, and a job whose ore is all gone ends as that rather than as lost permission.
            patch.RemoveWhere(tile => !OreFinder.IsOreOfType(tile.X, tile.Y, jobType));
            if (patch.Count == 0)
            {
                ClearJob("no eligible ore remains");
                if (LastConclusion is { ObservedClear: true }) status = "tracked ore cleared";
                sinceSearch = SearchEveryTicks;
            }
        }
        if (patch.Count > 0)
        {
            var context = ctx;
            patch.RemoveWhere(tile => !AllowsTarget(context, tile.ToWorldCoordinates())
                || Infrastructure.Interactions.WorldProtection.ProtectCompanionHomes.IsProtected(tile));
            if (target is { } selected && !patch.Contains(selected.Tile)) target = null;
            if (patch.Count == 0) { ClearJob("outside activity range or protected home"); sinceSearch = SearchEveryTicks; }
        }
        Point origin = MovementQueries.Tile(ctx.Npc.Center);
        if (origin != approachOrigin || TerrainChanges.Revision != approachRevision || pick != approachPickPower
            || FindToolAccess.Reach != approachReach)
        {
            // A route computed from the old cell is stale the moment the body moves, so the hover
            // has to be re-derived. The ore does not go stale, and discarding it here was the defect:
            // travelling is what invalidated the target, and travelling is the only thing a mining
            // job ever does before it swings, so a vein more than one tile away could never be
            // reached. The 2026-09-11 session sat in "approach unknown" for 9,123 of 26,716 ticks
            // against 267 ticks of actual mining, and ore stayed in the ground beside the player.
            // The tile is kept and only its approach is recomputed; when the recomputation declines
            // to answer, the hover is dropped and mining offers nothing until a later preparation
            // proves a cell from the new position.
            if (target is OreFinder.OreTarget held)
            {
                if (FindToolAccess.InReach(ctx.Npc.Center, held.Tile))
                    target = held with { StandPosition = ctx.Npc.Center };
                else if (FindToolAccess.Approach(held.Tile, ctx.Npc.Center, ctx.Senses.Reach, out Vector2 hover) == Reachability.Reach.Yes)
                    target = held with { StandPosition = hover };
                else
                    target = null;
            }
            if (patch.Count > 0 && target == null) sinceSearch = SearchEveryTicks;
            // A different pick is new evidence about every ore, not only the held one: a weaker tool
            // should be reported as unable to mine now rather than as nothing found until the next
            // cadence, and a stronger one should start work now rather than a second later.
            if (pick != approachPickPower) sinceSearch = SearchEveryTicks;
            // A different reach is the same kind of evidence: a larger one can reach ore the last search ruled out, and a
            // smaller one strands the hover just re-derived above.
            if (FindToolAccess.Reach != approachReach) sinceSearch = SearchEveryTicks;
            approachOrigin = origin;
            approachRevision = TerrainChanges.Revision;
            approachPickPower = pick;
            approachReach = FindToolAccess.Reach;
        }
        if (target is OreFinder.OreTarget t && (!OreFinder.IsOreOfType(t.Tile.X, t.Tile.Y, jobType) || !ctx.Companion.Miner.CanMine(t.Tile, pick)))
        {
            patch.Remove(t.Tile);
            target = NextInPatch(ctx, pick);
        }
        else if (target == null && patch.Count > 0 && (status != "approach unknown" || sinceSearch >= SearchEveryTicks))
        {
            target = NextInPatch(ctx, pick);
            // Retrying a retained approach consumes its cadence. Ending that job must
            // preserve a requested fresh discovery for the replacement world material.
            if (patch.Count > 0) sinceSearch = 0;
        }
        if (patch.Count == 0 && sinceSearch >= SearchEveryTicks)
        {
            Search(ctx, p.MinedOre);
        }
        if (patch.Count == 0 || (target == null && status == "approach unknown"))
        {
            ClassifyWithoutProvenTarget(0f);
            return 0f;
        }
        Classify(OfferEligibility.Usable, "vein-target-established");
        return 0.7f;
    }

    /// <summary>Without a proven working cell the offer is either an undecided approach, a vein this
    /// tool or search has ruled out, or nothing found. The status string is the discovery's own
    /// account, so the classification reads it rather than re-deriving the search.</summary>
    private void ClassifyWithoutProvenTarget(float value)
    {
        if (value > 0) { Classify(OfferEligibility.Unresolved, "ore-approach-undecided"); return; }
        OfferEligibility eligibility = status switch
        {
            "approach unknown" or "approaching unproven ore" or "no eligible approach" => OfferEligibility.Unresolved,
            "no mineable ore" or NoProvenPoseReason => OfferEligibility.KnownUnusable,
            _ => OfferEligibility.NoOpportunity,
        };
        Classify(eligibility, status.Replace(' ', '-'));
    }

    private Point? unresolvedCandidate;

    private void Search(in ActionContext ctx, (Point Tile, int Type)? playerHit)
    {
        sinceSearch = 0;
        int pick = TileMiner.PickaxeFor(ctx.Player).pick;
        var miner = ctx.Companion.Miner;
        var context = ctx;
        bool Mineable(Point tile) => miner.CanMine(tile, pick) && AllowsTarget(context, tile.ToWorldCoordinates())
            && !Infrastructure.Interactions.WorldProtection.ProtectCompanionHomes.IsProtected(tile);
        OreFinder.SearchResult result = default;
        // "Ore near the player" is measured from his intent region rather than his feet, for the
        // reason every work radius now is: a vein a few tiles ahead of a walking player is behind
        // the search centre the moment he starts walking towards it.
        Vector2 nearPlayer = ctx.Senses.Intent.Region.Heading;
        Vector2 body = ctx.Npc.Center;
        if (WorkPolicies.Mining == WorkPolicy.Mimic)
        {
            if (playerHit is (Point hit, int type))
                result = OreFinder.FindNearest(body, nearPlayer, ctx.Senses.Reach, SearchRadiusTiles, type, Mineable);
        }
        else
        {
            OreFinder.SearchResult byPlayer = OreFinder.FindNearest(body, nearPlayer, ctx.Senses.Reach, SearchRadiusTiles, accept: Mineable);
            OreFinder.SearchResult byCompanion = OreFinder.FindNearest(body, body, ctx.Senses.Reach, SearchRadiusTiles, accept: Mineable);
            result = new OreFinder.SearchResult(Nearest(body, byPlayer.Target, byCompanion.Target),
                NearestTile(body, byPlayer.UnresolvedTile, byCompanion.UnresolvedTile));
        }
        unresolvedCandidate = result.UnresolvedTile;
        OreFinder.OreTarget? found = result.Target;
        if (found is OreFinder.OreTarget f)
        {
            target = f;
            patch = OreFinder.Vein(f.Tile, f.Type);
            jobTiles = new HashSet<Point>(patch);
            ownRemovals.Clear();
            jobType = f.Type;
            jobId = nextJobId++;
            status = "approaching";
        }
        else if (result.ApproachUnknown)
            status = unresolvedCandidate == null ? "no reachable ore" : "approach unknown";
        else
        {
            // Search once without the tool predicate only after every mineable candidate was
            // rejected, so a closer weak-pick ore cannot mask a farther usable one. This pass only
            // names why nothing was offered.
            OreFinder.SearchResult anyOre = WorkPolicies.Mining == WorkPolicy.Mimic && playerHit is (Point _, int anyType)
                ? OreFinder.FindNearest(body, nearPlayer, ctx.Senses.Reach, SearchRadiusTiles, anyType)
                : OreFinder.FindNearest(body, body, ctx.Senses.Reach, SearchRadiusTiles);
            status = anyOre.Target != null ? "no mineable ore" : anyOre.ApproachUnknown ? "no eligible approach" : "no reachable ore";
        }
    }

    public override float ForecastTicks()
        => preparedTrip;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        Item pickaxe = TileMiner.PickaxeFor(ctx.Player);
        // The pickaxe comes out only in position; on the flight there the hand stays empty, so
        // the torch can hold it in the dark and the other arm can still throw.
        ctx.Companion.HoldItem(ItemID.None);
        swinging = false;
        if (WorkPolicies.Mining == WorkPolicy.Disabled)
        {
            ClearJob("disabled before execution");
            sinceSearch = SearchEveryTicks;
            return PositionRequest.Hold;
        }
        if (preparedTile == null || PreparedTargetRejection.Length > 0)
        {
            target = null;
            sinceSearch = SearchEveryTicks;
            status = "prepared tile invalidated";
            attemptSetback = (AttemptStatus.Invalid, "prepared-tile-invalidated");
            return PositionRequest.Hold;
        }
        if (target is not OreFinder.OreTarget t)
            return PositionRequest.Hold;

        if (!FindToolAccess.InReach(ctx.Npc.Center, t.Tile))
        {
            // A waypoint tolerance is not tool reach. Keep approaching the proven cell until
            // the actual body can swing; returning Hold here made approximate arrival permanent.
            return PositionRequest.ExactAt(t.StandPosition, t.Tile);
        }
        ctx.Companion.HoldItem(pickaxe.type);
        swinging = true;
        ctx.Companion.Motor.Face(t.Tile.X * 16f + 8f);
        ctx.Companion.ShowBeam(t.Tile.ToWorldCoordinates(8f, 8f));
        if (ctx.Companion.Miner.Swing(t.Tile, pickaxe))
        {
            ctx.Companion.StartAnimation(pickaxe.type, pickaxe.useAnimation);
            if (ctx.Companion.Miner.LastOutcome is { } outcome)
            {
                var owner = ctx.Companion.Brain.Chooser.Activity;
                Infrastructure.Diagnostics.GodsEyeEvents.RecordToolEffect(ctx.Npc, "pickaxe", outcome, ctx.Companion.Brain.Chooser.EvaluationId, owner.Id, owner.AttemptOpen ? owner.AttemptId : 0);
                if (outcome.Effect == Infrastructure.Interactions.TileToolEffect.Removed
                    && outcome.Before.Type == jobType && jobTiles.Contains(outcome.Target))
                    ownRemovals.Add(outcome.Target);
                if (outcome.Productive) ctx.Companion.Brain.Chooser.RecordWork(t.Tile.ToWorldCoordinates());
            }
        }
        return PositionRequest.Hold;
    }

    /// <summary>The next tile of the patch: one still in reach from where the body hovers, else the nearest with a new cell.</summary>
    private OreFinder.OreTarget? NextInPatch(in ActionContext ctx, int pickPower)
    {
        var miner = ctx.Companion.Miner;
        patch.RemoveWhere(p => !OreFinder.IsOreOfType(p.X, p.Y, jobType) || !miner.CanMine(p, pickPower)
            || Infrastructure.Interactions.WorldProtection.ProtectCompanionHomes.IsProtected(p));
        if (patch.Count == 0)
        {
            ClearJob("no eligible ore remains");
            if (LastConclusion is { ObservedClear: true }) status = "tracked ore cleared";
            return null;
        }
        Point? inReach = null;
        float best = float.MaxValue;
        foreach (Point p in patch)
        {
            float d = Vector2.DistanceSquared(p.ToWorldCoordinates(), ctx.Npc.Center);
            if (d < best && FindToolAccess.InReach(ctx.Npc.Center, p))
            {
                best = d;
                inReach = p;
            }
        }
        if (inReach is Point r)
        {
            status = "mining";
            return new OreFinder.OreTarget(r, jobType, ctx.Npc.Center);
        }
        bool unknown = false;
        unresolvedCandidate = null;
        foreach (Point p in patch)
        {
            var approach = FindToolAccess.Approach(p, ctx.Npc.Center, ctx.Senses.Reach, out Vector2 hover);
            if (approach == Reachability.Reach.Yes)
            {
                status = "relocating";
                return new OreFinder.OreTarget(p, jobType, hover);
            }
            unknown |= approach == Reachability.Reach.Unknown;
            if (approach == Reachability.Reach.Unknown)
                unresolvedCandidate = NearestTile(ctx.Npc.Center, unresolvedCandidate, p);
        }
        if (unknown)
        {
            status = "approach unknown";
            return null;
        }
        // A complete bounded search established that none of the remaining tiles has a
        // reachable working cell. End this job's reachable portion; a later discovery starts fresh.
        ClearJob(NoProvenPoseReason);
        return null;
    }

    private static OreFinder.OreTarget? Nearest(Vector2 from, OreFinder.OreTarget? a, OreFinder.OreTarget? b)
        => a == null ? b : b == null ? a
            : Vector2.DistanceSquared(from, a.Value.Tile.ToWorldCoordinates()) <= Vector2.DistanceSquared(from, b.Value.Tile.ToWorldCoordinates()) ? a : b;

    private static Point? NearestTile(Vector2 from, Point? a, Point? b)
        => a == null ? b : b == null ? a
            : Vector2.DistanceSquared(from, a.Value.ToWorldCoordinates()) <= Vector2.DistanceSquared(from, b.Value.ToWorldCoordinates()) ? a : b;

    private void ClearJob(string reason)
    {
        if (jobId > 0)
        {
            LastConclusion = DescribeOreJobEnd.Capture(jobId, reason, jobTiles, jobType, ownRemovals.Count);
            conclusionSequence++;
        }
        jobTiles.Clear();
        ownRemovals.Clear();
        target = null;
        unresolvedCandidate = null;
        patch.Clear();
        jobId = 0;
        status = reason;
        ReleaseActivity();
    }
    public override void Exit(in ActionContext ctx)
    {
        // The vein survives interruption, but a route from the old body position does not.
        swinging = false;
        target = null;
        status = "resume requires approach";
        sinceSearch = SearchEveryTicks;
    }
}
