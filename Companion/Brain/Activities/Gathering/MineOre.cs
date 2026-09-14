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
/// terrain merely to make an approach.
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
    private readonly List<Point> noStandingPose = new();
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
    /// <summary>True only while the pickaxe is actually out; the whole walk to the vein is empty-handed.</summary>
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
            ? Vector2.Distance(ctx.Npc.Bottom, found.StandPosition) / Companion.CompanionMotor.WalkSpeed + (RemainingWork?.Ticks ?? 0f)
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
            return new(end.Reason is NoProvenPoseReason or LostTakeOffReason ? AttemptStatus.Failed : AttemptStatus.Invalid, end.Reason);
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

    private const string NoProvenPoseReason = "remaining ore has no proven working pose";
    /// <summary>The job ended because the only ore left is reached by hops whose take-offs stopped proving a jump
    /// with the body at rest on them; those tiles are deferred until the terrain changes or the wait passes.</summary>
    private const string LostTakeOffReason = "interaction-jump-lost-take-off";
    /// <summary>A body counts as at rest on its take-off below this horizontal speed. The engine zeroes smaller
    /// speeds and the slowdown snaps to exactly zero, so this is a numerical tolerance, not a tunable.</summary>
    private const float RestSpeed = 0.01f;
    /// <summary>The longest flight the interaction-jump proof simulates; it mirrors the step limit inside
    /// ProveInteractionJump.CanReach, and a jump still airborne past it was never going to deliver the swing.</summary>
    private const int HopFlightTicks = 90;
    // The tick this method last issued its own hop jump, so its flight is told apart from a traversal jump on the walk there.
    private ulong? hopJumpTick;
    // Hop take-offs that stopped proving a jump from rest, with the terrain revision and tick they may be asked again.
    private readonly Dictionary<Point, (int Revision, ulong Until)> hopDeferred = new();
    // The progress window a hop target is held under while the body walks to its take-off.
    private (Point Tile, Vector2 Stand)? hopProgressFor;
    private Vector2 hopProgressFrom;
    private int hopProgressTicks;
    private bool wasAirborne;
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
        Point origin = MovementQueries.FeetTile(ctx.Npc.Bottom);
        BodyState liveBody = ctx.Companion.Motor.State;
        // Asked before wasAirborne is updated, because landing away from the take-off is read from the change.
        bool hopHeld = target is OreFinder.OreTarget { Hop: true } hopTarget
            && TerrainChanges.Revision == approachRevision && pick == approachPickPower && FindToolAccess.Reach == approachReach
            && HopTargetStillHeld(liveBody, hopTarget);
        wasAirborne = !liveBody.OnGround;
        if (origin != approachOrigin || TerrainChanges.Revision != approachRevision || pick != approachPickPower
            || FindToolAccess.Reach != approachReach
            || target is OreFinder.OreTarget { Hop: true } && !hopHeld)
        {
            // A route computed from the old feet tile is stale the moment the feet move, so the
            // stand has to be re-derived. The ore does not go stale, and discarding it here was
            // the defect: walking is what invalidated the target, and walking is the only thing a
            // mining job ever does before it swings, so a vein more than one tile away could never
            // be reached. The 2026-09-11 session sat in "approach unknown" for 9,123 of 26,716
            // ticks against 267 ticks of actual mining, and ore stayed in the ground beside the
            // player. The tile is kept and only its approach is recomputed; when the recomputation
            // declines to answer, the stand is dropped and mining offers nothing until a later
            // preparation proves a pose from new feet.
            if (target is OreFinder.OreTarget held)
            {
                // A hop target's feet move by design twice over — walking to the take-off and the hop
                // itself — so feet movement alone must not cancel it. It is held while the body is in the
                // air, at its take-off, or still closing ground on it; a body that landed anywhere else, or
                // stopped closing in for a progress window, has been displaced, whatever moved it, and its
                // take-off is re-derived from where it now stands. Only a re-derivation restarts the window,
                // so a body jittering between two tiles cannot keep an unreachable take-off alive.
                bool keepHop = held.Hop && hopHeld;
                if (!keepHop) hopProgressFor = null;
                if (keepHop) { }
                else if (FindToolAccess.InReach(ctx.Npc.Bottom, held.Tile))
                    target = held with { StandPosition = ctx.Npc.Bottom, Hop = false };
                else if (FindToolAccess.Approach(held.Tile, ctx.Npc.Bottom, ctx.Senses.Reach, out Vector2 restand) is var standing && standing == Reachability.Reach.Yes)
                    target = held with { StandPosition = restand, Hop = false };
                else if (standing == Reachability.Reach.No
                    && FindToolAccess.HopApproach(held.Tile, ctx.Companion.Motor.State, ctx.Senses.Reach, out Vector2 takeOff) == Reachability.Reach.Yes)
                    target = held with { StandPosition = takeOff, Hop = true };
                else
                    target = null;
            }
            if (patch.Count > 0 && target == null) sinceSearch = SearchEveryTicks;
            // A different pick is new evidence about every ore, not only the held one: a weaker tool
            // should be reported as unable to mine now rather than as nothing found until the next
            // cadence, and a stronger one should start work now rather than a second later.
            if (pick != approachPickPower) sinceSearch = SearchEveryTicks;
            // A different reach is the same kind of evidence: a larger one can reach ore the last search ruled out and
            // take-offs deferred under the smaller one, and a smaller one strands the stand just re-derived above. Before
            // the reach joined this key a body standing still kept a stand the new reach could not swing from and asked
            // for it every tick; a reach that grew waited out the search cadence.
            if (FindToolAccess.Reach != approachReach)
            {
                sinceSearch = SearchEveryTicks;
                hopDeferred.Clear();
            }
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

    /// <summary>Without a proven working pose the offer is either an undecided approach, a vein this
    /// tool or search has ruled out, or nothing found. The status string is the discovery's own
    /// account, so the classification reads it rather than re-deriving the search.</summary>
    private void ClassifyWithoutProvenTarget(float value)
    {
        if (value > 0) { Classify(OfferEligibility.Unresolved, "ore-approach-undecided"); return; }
        OfferEligibility eligibility = status switch
        {
            "approach unknown" or "approaching unproven ore" or "no eligible approach" => OfferEligibility.Unresolved,
            "no mineable ore" or NoProvenPoseReason or LostTakeOffReason => OfferEligibility.KnownUnusable,
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
            && !Infrastructure.Interactions.WorldProtection.ProtectCompanionHomes.IsProtected(tile)
            && !HopDeferred(tile);
        OreFinder.SearchResult result = default;
        BodyState body = ctx.Companion.Motor.State;
        if (WorkPolicies.Mining == WorkPolicy.Mimic)
        {
            if (playerHit is (Point hit, int type))
                result = OreFinder.FindNearest(ctx.Npc.Bottom, ctx.Player.Bottom, ctx.Senses.Reach, SearchRadiusTiles, type, Mineable, body);
        }
        else
        {
            OreFinder.SearchResult byPlayer = OreFinder.FindNearest(ctx.Npc.Bottom, ctx.Player.Bottom, ctx.Senses.Reach, SearchRadiusTiles, accept: Mineable, body: body);
            OreFinder.SearchResult byCompanion = OreFinder.FindNearest(ctx.Npc.Bottom, ctx.Npc.Bottom, ctx.Senses.Reach, SearchRadiusTiles, accept: Mineable, body: body);
            result = new OreFinder.SearchResult(Nearest(ctx.Npc.Bottom, byPlayer.Target, byCompanion.Target),
                NearestTile(ctx.Npc.Bottom, byPlayer.UnresolvedTile, byCompanion.UnresolvedTile));
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
            status = f.Hop ? "approaching take-off" : "approaching";
        }
        else if (result.ApproachUnknown)
            status = unresolvedCandidate == null ? "no reachable ore" : "approach unknown";
        else
        {
            // Search once without the tool predicate only after every mineable candidate was
            // rejected, so a closer weak-pick ore cannot mask a farther usable one. This pass only
            // names why nothing was offered, so it asks standing access and no hops: a hop scan per
            // unmineable ore was paying for a label. Ceiling ore the pick cannot damage therefore
            // reads as no reachable ore rather than no mineable ore.
            OreFinder.SearchResult anyOre = WorkPolicies.Mining == WorkPolicy.Mimic && playerHit is (Point _, int anyType)
                ? OreFinder.FindNearest(ctx.Npc.Bottom, ctx.Player.Bottom, ctx.Senses.Reach, SearchRadiusTiles, anyType)
                : OreFinder.FindNearest(ctx.Npc.Bottom, ctx.Npc.Bottom, ctx.Senses.Reach, SearchRadiusTiles);
            status = anyOre.Target != null ? "no mineable ore" : anyOre.ApproachUnknown ? "no eligible approach" : "no reachable ore";
        }
    }

    public override float ForecastTicks()
        => preparedTrip;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        Item pickaxe = TileMiner.PickaxeFor(ctx.Player);
        // The pickaxe comes out only in position; on the walk there the hand stays empty, so
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

        if (!FindToolAccess.InReach(ctx.Npc.Bottom, t.Tile))
        {
            if (t.Hop)
            {
                BodyState live = ctx.Companion.Motor.State;
                if (!live.OnGround)
                {
                    // The rising body of this method's own jump is what swings, so that flight is held. Any other
                    // flight is the walk to the take-off crossing a ledge or a gap, and holding it cancels the
                    // navigator's jump in mid-air: at the foot of a two-tile ledge the body bounced for 1,200 ticks.
                    return hopJumpTick is ulong jumped && Main.GameUpdateCount - jumped <= HopFlightTicks
                        ? PositionRequest.Hold : PositionRequest.ExactAt(t.StandPosition);
                }
                // Re-proved against the live pose every time: another behaviour may have moved the
                // body since the take-off was chosen, and a hop proved from somewhere else is not a hop.
                if (ProveInteractionJump.CanReach(NavGrid.World, live, body => FindToolAccess.InReach(body.Feet, t.Tile)))
                {
                    status = "jumping to ore";
                    hopJumpTick = Main.GameUpdateCount;
                    return PositionRequest.Hold with { JumpScale = 1f };
                }
                // Within the proof's own landing tolerance of the take-off, so the two agree on "here". A body
                // still sliding there fails the proof because its jump drifts, not because the take-off is gone,
                // so it holds until it is at rest; the arrival proof in FindToolAccess keeps that slide on support.
                if (Vector2.DistanceSquared(ctx.Npc.Bottom, t.StandPosition) <= BodyPhysics.Width * BodyPhysics.Width)
                {
                    if (System.MathF.Abs(live.Vx) > RestSpeed)
                        return PositionRequest.Hold;
                    // At rest on the take-off with no proof left: the hop itself is gone. The tile leaves discovery
                    // until something changes, or the next preparation re-proves this take-off from rest and offers
                    // it again, which is how a lost take-off used to repeat for ever without an attempt ending.
                    // An entry leaves only when its own tile is looked up again, so tiles never revisited would accumulate.
                    if (hopDeferred.Count > 64) hopDeferred.Clear();
                    hopDeferred[t.Tile] = (TerrainChanges.Revision, Main.GameUpdateCount + (ulong)Weights.HopTakeOffRetryTicks);
                    target = null; status = LostTakeOffReason;
                    attemptSetback = (AttemptStatus.Failed, LostTakeOffReason);
                    return PositionRequest.Hold;
                }
            }
            // A waypoint tolerance is not tool reach. Keep approaching the proven stand until
            // the actual body can swing; returning Hold here made approximate arrival permanent.
            // A hop target reaches this line walking to its take-off, which is a pose that does not
            // reach standing, so only a standing stand declares the tile as its success region.
            return t.Hop ? PositionRequest.ExactAt(t.StandPosition) : PositionRequest.ExactAt(t.StandPosition, t.Tile);
        }
        ctx.Companion.HoldItem(pickaxe.type);
        swinging = true;
        ctx.Companion.Motor.Face(t.Tile.X * 16f + 8f);
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

    /// <summary>The next tile of the patch: one still in reach of the current stand, else the nearest with a new stand.</summary>
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
            if (d < best && FindToolAccess.InReach(ctx.Npc.Bottom, p))
            {
                best = d;
                inReach = p;
            }
        }
        if (inReach is Point r)
        {
            status = "mining";
            return new OreFinder.OreTarget(r, jobType, ctx.Npc.Bottom);
        }
        // A vein that runs up into the ceiling has tiles no standable position can swing at, so the
        // approach search rejects them and the top of the vein stays in the rock for ever. The body
        // can reach them the way a player does, by jumping from where it already is, and the torch
        // work has proved that shape since it started placing lights on ledges. Tried before the
        // relocation loop because hopping beats walking away, and proved against the live pose
        // rather than assumed, so a tile that is only reachable from somewhere else falls through
        // to the ordinary approach below.
        // A tile whose take-off was lost at rest is asked no hop until something changes; if only such tiles
        // are left, the job ends with that cause instead of claiming no pose was ever proven.
        bool skippedDeferred = false;
        foreach (Point p in patch)
        {
            if (FindToolAccess.InReach(ctx.Npc.Bottom, p)) continue;
            if (HopDeferred(p)) { skippedDeferred = true; continue; }
            if (!ProveInteractionJump.CanReach(NavGrid.World, ctx.Companion.Motor.State, body => FindToolAccess.InReach(body.Feet, p)))
                continue;
            status = "jumping to ore";
            return new OreFinder.OreTarget(p, jobType, ctx.Npc.Bottom, Hop: true);
        }
        bool unknown = false;
        unresolvedCandidate = null;
        noStandingPose.Clear();
        foreach (Point p in patch)
        {
            var approach = FindToolAccess.Approach(p, ctx.Npc.Bottom, ctx.Senses.Reach, out Vector2 stand);
            if (approach == Reachability.Reach.Yes)
            {
                status = "relocating";
                return new OreFinder.OreTarget(p, jobType, stand);
            }
            unknown |= approach == Reachability.Reach.Unknown;
            if (approach == Reachability.Reach.Unknown)
                unresolvedCandidate = NearestTile(ctx.Npc.Bottom, unresolvedCandidate, p);
            else
                noStandingPose.Add(p);
        }
        // Only when no tile can be worked standing: a proven hop from a reachable take-off elsewhere, asked
        // before an undecided standing approach is waited on, because a proven method beats a maybe. Only
        // tiles the standing search proved unreachable are asked, for the reason OreFinder gives.
        BodyState body = ctx.Companion.Motor.State;
        foreach (Point p in noStandingPose)
        {
            if (HopDeferred(p)) { skippedDeferred = true; continue; }
            var hop = FindToolAccess.HopApproach(p, body, ctx.Senses.Reach, out Vector2 takeOff);
            if (hop == Reachability.Reach.Yes)
            {
                status = "relocating to take-off";
                return new OreFinder.OreTarget(p, jobType, takeOff, Hop: true);
            }
            if (hop == Reachability.Reach.Unknown)
            {
                unknown = true;
                unresolvedCandidate = NearestTile(ctx.Npc.Bottom, unresolvedCandidate, p);
            }
        }
        if (unknown)
        {
            status = "approach unknown";
            return null;
        }
        // A complete bounded search established that none of the remaining tiles has a
        // legal approach. End this job's reachable portion; a later discovery starts fresh.
        ClearJob(skippedDeferred ? LostTakeOffReason : NoProvenPoseReason);
        return null;
    }

    /// <summary>
    /// Whether a hop target is still the method in hand. Airborne, the rising body is what swings. At the
    /// take-off, within the proof's landing tolerance, it is arriving or about to jump. Walking toward it, it is
    /// held while it keeps closing ground within the shared progress window. A body that landed anywhere else, or
    /// stopped closing in, was displaced by something (knockback, a slide, another behaviour) and the take-off
    /// proved from where it used to stand says nothing about where it stands now.
    /// </summary>
    private bool HopTargetStillHeld(in BodyState live, OreFinder.OreTarget hop)
    {
        if (hopProgressFor != (hop.Tile, hop.StandPosition))
        {
            hopProgressFor = (hop.Tile, hop.StandPosition);
            hopProgressFrom = live.Feet;
            hopProgressTicks = 0;
        }
        if (!live.OnGround)
            return true;
        if (Vector2.DistanceSquared(live.Feet, hop.StandPosition) <= BodyPhysics.Width * BodyPhysics.Width)
        {
            hopProgressFrom = live.Feet;
            hopProgressTicks = 0;
            return true;
        }
        if (wasAirborne)
            return false;
        if (Vector2.Distance(live.Feet, hop.StandPosition) <= Vector2.Distance(hopProgressFrom, hop.StandPosition) - Weights.ObjectiveProgressPixels)
        {
            hopProgressFrom = live.Feet;
            hopProgressTicks = 0;
            return true;
        }
        return ++hopProgressTicks < Weights.ObjectiveProgressWindowTicks;
    }

    /// <summary>A tile whose take-off was lost at rest, while the terrain revision it was lost under still holds and its wait has not passed.</summary>
    private bool HopDeferred(Point tile)
    {
        if (!hopDeferred.TryGetValue(tile, out var entry))
            return false;
        if (entry.Revision == TerrainChanges.Revision && Main.GameUpdateCount < entry.Until)
            return true;
        hopDeferred.Remove(tile);
        return false;
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
