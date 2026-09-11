#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;
using AICompanion.Companion.Brain.SharedMovementSystem;
using AICompanion.Companion.Brain.WorldObservation;
using AICompanion.Companion.Brain.WorldInteractions.Mining;

namespace AICompanion.Companion.Brain.Behaviours.Work;

/// <summary>
/// Retains one ore-only job. Opportunistic mode can start from a vein near either body;
/// mimic mode uses the player's recent ore contact as its trigger. Both clear every
/// reachable tile of the selected vein, including the player's vein, and never excavate
/// terrain merely to make an approach.
/// </summary>
public sealed class MineAction : CompanionAction
{
    public override string Name => "mine";
    public override Vector2? ActivityTarget => target?.Tile.ToWorldCoordinates();

    private const int KeepJobTicks = 600;
    private const int SearchRadiusTiles = 45;
    private const int SearchEveryTicks = 60;

    private OreFinder.OreTarget? target;
    private HashSet<Point> patch = new();
    private int nextJobId = 1;
    private int jobId;
    private string status = "idle";
    private int sinceSearch = SearchEveryTicks;
    private Point? approachOrigin;
    private int approachRevision;
    private int approachPickPower;

    private bool swinging;

    public int JobId => jobId;
    /// <summary>True only while the pickaxe is actually out; the whole walk to the vein is empty-handed.</summary>
    public override bool HandsBusy => swinging;
    public override object? ActivityIdentity => jobId > 0 ? jobId : null;
    public WorkPolicy Policy => WorkPolicies.Mining;
    public string Status => status;
    public int RemainingTiles => patch.Count;
    public Point? TargetTile => target?.Tile;
    public Vector2? TargetStandPosition => target?.StandPosition;

    public override float Score(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsDead)
            return 0f;
        sinceSearch++;

        if (WorkPolicies.Mining == WorkPolicy.Disabled)
        {
            ClearJob("disabled");
            return 0f;
        }
        bool playerMining = p.MinedOre != null || TileDamageWatcher.TicksSinceOreHit <= KeepJobTicks;
        if (WorkPolicies.Mining == WorkPolicy.Mimic && !playerMining)
        {
            ClearJob("mimic trigger expired");
            return 0f;
        }
        int pick = TileMiner.PickaxeFor(ctx.Player).pick;
        if (patch.Count > 0)
        {
            var context = ctx;
            patch.RemoveWhere(tile => !AllowsTarget(context, tile.ToWorldCoordinates())
                || WorldInteractions.WorldProtection.ProtectCompanionHomes.IsProtected(tile));
            if (target is { } selected && !patch.Contains(selected.Tile)) target = null;
            if (patch.Count == 0) { ClearJob("outside activity range or protected home"); sinceSearch = SearchEveryTicks; }
        }
        Point origin = MovementQueries.FeetTile(ctx.Npc.Bottom);
        if (origin != approachOrigin || TerrainChanges.Revision != approachRevision || pick != approachPickPower)
        {
            // A route computed from the old feet tile is stale the moment the feet move, so the
            // stand has to be re-derived. The ore does not go stale, and discarding it here was
            // the defect: walking is what invalidated the target, and walking is the only thing a
            // mining job ever does before it swings, so a vein more than one tile away could never
            // be reached. The 2026-09-11 session sat in "approach unknown" for 9,123 of 26,716
            // ticks against 267 ticks of actual mining, and ore stayed in the ground beside the
            // player. The tile is kept and only its approach is recomputed; when the recomputation
            // declines to answer, the tile becomes the walk-at-it target rather than nothing.
            if (target is OreFinder.OreTarget held)
            {
                if (OreFinder.InReach(ctx.Npc.Bottom, held.Tile))
                    target = held with { StandPosition = ctx.Npc.Bottom };
                else if (OreFinder.Approach(held.Tile, ctx.Npc.Bottom, out Vector2 restand) == Reachability.Reach.Yes)
                    target = held with { StandPosition = restand };
                else
                    target = null; // the walk-at-unproven-ore path below re-finds it
            }
            if (patch.Count > 0 && target == null) sinceSearch = SearchEveryTicks;
            approachOrigin = origin;
            approachRevision = TerrainChanges.Revision;
            approachPickPower = pick;
        }
        if (target is OreFinder.OreTarget t && (!OreFinder.IsOre(t.Tile.X, t.Tile.Y) || !ctx.Companion.Miner.CanMine(t.Tile, pick)))
        {
            patch.Remove(t.Tile);
            target = NextInPatch(ctx, t, pick);
        }
        else if (target == null && patch.Count > 0 && (status != "approach unknown" || sinceSearch >= SearchEveryTicks))
        {
            target = NextInPatch(ctx, new OreFinder.OreTarget(default, 0, ctx.Npc.Bottom), pick);
            sinceSearch = 0;
        }
        if (patch.Count == 0 && sinceSearch >= SearchEveryTicks)
        {
            Search(ctx, p.MinedOre);
        }
        float safe = Consideration.AtLeast(1f - ctx.Senses.Threats.PlayerDanger, 0.1f);
        if (patch.Count == 0 || (target == null && status == "approach unknown"))
            return UnprovenApproach(ctx, safe);
        unproven = null;
        return 0.7f * safe;
    }

    private Point? unproven;
    private Vector2 unprovenOrigin;
    private int unprovenTicks;

    /// <summary>
    /// What mining is worth while the approach search has declined to answer. It used to be worth
    /// nothing, and that zero was self-fulfilling: the reachability question is a bounded search run
    /// fresh from the companion's feet each time, so for a body that does not move it returns the
    /// same "could not tell" for ever, and the only thing that would shorten the search — walking
    /// closer — is the thing a zero score prevents. A quarter of the 2026-09-11 session sat in that
    /// state: 3,444 ticks where mining had found ore, wanted it, and contributed nothing.
    ///
    /// So the companion walks at the ore instead, and the approach is re-asked from each new
    /// position until it resolves one way or the other. The score is discounted well below a proven
    /// job, so a vein it can actually reach always wins, and it is renewed only while the body is
    /// covering ground: standing still stops earning it, which preserves the property the original
    /// zero was protecting — that the chooser is never held by mining that is not going anywhere.
    /// </summary>
    private float UnprovenApproach(in ActionContext ctx, float safe)
    {
        // Both spellings are the same state: the second is what the walk toward an unproven ore
        // reports so a session can be read for it, and it must not read as a different state here
        // or the attempt would end on the tick after it started.
        if (status is not ("approach unknown" or "approaching unproven ore"))
        {
            unproven = null;
            return 0f;
        }
        if (unproven is not Point held || !OreFinder.IsOre(held.X, held.Y) || sinceSearch >= SearchEveryTicks)
        {
            unproven = NearestUnprovenOre(ctx);
            unprovenOrigin = ctx.Npc.Bottom;
            unprovenTicks = 0;
        }
        if (unproven == null)
            return 0f;
        if (Vector2.DistanceSquared(unprovenOrigin, ctx.Npc.Bottom) >= Weights.ObjectiveProgressPixels * Weights.ObjectiveProgressPixels)
        {
            unprovenOrigin = ctx.Npc.Bottom;
            unprovenTicks = 0;
        }
        else if (++unprovenTicks >= Weights.ObjectiveProgressWindowTicks)
        {
            // Walking has stopped resolving it. Give the tick back rather than lean on the ore.
            unproven = null;
            return 0f;
        }
        return 0.7f * safe * Weights.MineUnprovenApproach;
    }

    /// <summary>
    /// The nearest ore this pickaxe may break that the activity envelope and home protection allow,
    /// asked without any reachability question at all. The ordinary search discards exactly these
    /// candidates, so when it reports that some approach was unknown it has already thrown away the
    /// tile that would say where to walk.
    /// </summary>
    private Point? NearestUnprovenOre(in ActionContext ctx)
    {
        int pick = TileMiner.PickaxeFor(ctx.Player).pick;
        var miner = ctx.Companion.Miner;
        Point from = MovementQueries.FeetTile(ctx.Npc.Bottom);
        Point? best = null;
        float bestDistance = float.MaxValue;
        for (int x = from.X - SearchRadiusTiles; x <= from.X + SearchRadiusTiles; x++)
        {
            for (int y = from.Y - SearchRadiusTiles; y <= from.Y + SearchRadiusTiles; y++)
            {
                if (!OreFinder.IsOre(x, y))
                    continue;
                var tile = new Point(x, y);
                if (!miner.CanMine(tile, pick) || !AllowsTarget(ctx, tile.ToWorldCoordinates())
                    || WorldInteractions.WorldProtection.ProtectCompanionHomes.IsProtected(tile))
                    continue;
                float d = Vector2.DistanceSquared(ctx.Npc.Bottom, tile.ToWorldCoordinates());
                if (d >= bestDistance)
                    continue;
                bestDistance = d;
                best = tile;
            }
        }
        return best;
    }

    private void Search(in ActionContext ctx, (Point Tile, int Type)? playerHit)
    {
        sinceSearch = 0;
        int pick = TileMiner.PickaxeFor(ctx.Player).pick;
        var miner = ctx.Companion.Miner;
        var context = ctx;
        bool Mineable(Point tile) => miner.CanMine(tile, pick) && AllowsTarget(context, tile.ToWorldCoordinates())
            && !WorldInteractions.WorldProtection.ProtectCompanionHomes.IsProtected(tile);
        OreFinder.SearchResult result = default;
        if (WorkPolicies.Mining == WorkPolicy.Mimic)
        {
            if (playerHit is (Point hit, int type))
                result = OreFinder.FindNearest(ctx.Npc.Bottom, ctx.Player.Bottom, SearchRadiusTiles, type, Mineable);
        }
        else
        {
            OreFinder.SearchResult byPlayer = OreFinder.FindNearest(ctx.Npc.Bottom, ctx.Player.Bottom, SearchRadiusTiles, accept: Mineable);
            OreFinder.SearchResult byCompanion = OreFinder.FindNearest(ctx.Npc.Bottom, ctx.Npc.Bottom, SearchRadiusTiles, accept: Mineable);
            result = new OreFinder.SearchResult(Nearest(ctx.Npc.Bottom, byPlayer.Target, byCompanion.Target),
                byPlayer.ApproachUnknown || byCompanion.ApproachUnknown);
        }
        OreFinder.OreTarget? found = result.Target;
        if (found is OreFinder.OreTarget f)
        {
            target = f;
            patch = OreFinder.Vein(f.Tile, f.Type);
            jobId = nextJobId++;
            status = "approaching";
        }
        else if (result.ApproachUnknown)
            status = "approach unknown";
        else
        {
            // Search once without the tool predicate only after every mineable candidate was
            // rejected, so a closer weak-pick ore cannot mask a farther usable one.
            OreFinder.SearchResult anyOre = WorkPolicies.Mining == WorkPolicy.Mimic && playerHit is (Point _, int anyType)
                ? OreFinder.FindNearest(ctx.Npc.Bottom, ctx.Player.Bottom, SearchRadiusTiles, anyType)
                : OreFinder.FindNearest(ctx.Npc.Bottom, ctx.Npc.Bottom, SearchRadiusTiles);
            status = anyOre.Target != null ? "no mineable ore" : anyOre.ApproachUnknown ? "approach unknown" : "no reachable ore";
        }
    }

    public override float ForecastTicks(in ActionContext ctx)
        => target == null ? 0f : Vector2.Distance(ctx.Npc.Bottom, target.Value.StandPosition) / Companion.CompanionMotor.WalkSpeed + 180f;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        Item pickaxe = TileMiner.PickaxeFor(ctx.Player);
        // The pickaxe comes out only in position; on the walk there the hand stays empty, so
        // the torch can hold it in the dark and the other arm can still throw.
        ctx.Companion.HoldItem(ItemID.None);
        swinging = false;
        if (target == null && unproven is Point approach)
        {
            // No proven stand exists yet, so walk at the ore itself. Exact resolves to the nearest
            // standable tile it can actually reach, and the partial route walks as close as it can —
            // which is what re-asks the approach question from somewhere new.
            status = "approaching unproven ore";
            return PositionRequest.ExactAt(approach.ToWorldCoordinates());
        }
        if (target is not OreFinder.OreTarget t)
            return PositionRequest.Hold;

        if (!OreFinder.InReach(ctx.Npc.Bottom, t.Tile))
        {
            // A waypoint tolerance is not tool reach. Keep approaching the proven stand until
            // the actual body can swing; returning Hold here made approximate arrival permanent.
            return PositionRequest.ExactAt(t.StandPosition);
        }
        ctx.Companion.HoldItem(pickaxe.type);
        swinging = true;
        ctx.Companion.Motor.Face(t.Tile.X * 16f + 8f);
        if (ctx.Companion.Miner.Swing(t.Tile, pickaxe))
        {
            ctx.Companion.StartAnimation(pickaxe.type, pickaxe.useAnimation);
            ctx.Companion.Brain.Chooser.RecordWork(t.Tile.ToWorldCoordinates());
        }
        return PositionRequest.Hold;
    }

    /// <summary>The next tile of the patch: one still in reach of the current stand, else the nearest with a new stand.</summary>
    private OreFinder.OreTarget? NextInPatch(in ActionContext ctx, OreFinder.OreTarget current, int pickPower)
    {
        var miner = ctx.Companion.Miner;
        patch.RemoveWhere(p => !OreFinder.IsOre(p.X, p.Y) || !miner.CanMine(p, pickPower)
            || WorldInteractions.WorldProtection.ProtectCompanionHomes.IsProtected(p));
        if (patch.Count == 0)
        {
            jobId = 0;
            ReleaseActivity();
            status = "completed reachable ore";
            return null;
        }
        Point? inReach = null;
        float best = float.MaxValue;
        foreach (Point p in patch)
        {
            float d = Vector2.DistanceSquared(p.ToWorldCoordinates(), ctx.Npc.Center);
            if (d < best && OreFinder.InReach(ctx.Npc.Bottom, p))
            {
                best = d;
                inReach = p;
            }
        }
        if (inReach is Point r)
        {
            status = "mining";
            return current with { Tile = r, StandPosition = ctx.Npc.Bottom };
        }
        bool unknown = false;
        foreach (Point p in patch)
        {
            var approach = OreFinder.Approach(p, ctx.Npc.Bottom, out Vector2 stand);
            if (approach == Reachability.Reach.Yes)
            {
                status = "relocating";
                return new OreFinder.OreTarget(p, current.Type, stand);
            }
            unknown |= approach == Reachability.Reach.Unknown;
        }
        if (unknown)
        {
            status = "approach unknown";
            return null;
        }
        // A complete bounded search established that none of the remaining tiles has a
        // legal approach. End this job's reachable portion; a later discovery starts fresh.
        patch.Clear();
        jobId = 0;
        ReleaseActivity();
        status = "completed reachable portion";
        return null;
    }

    private static OreFinder.OreTarget? Nearest(Vector2 from, OreFinder.OreTarget? a, OreFinder.OreTarget? b)
        => a == null ? b : b == null ? a
            : Vector2.DistanceSquared(from, a.Value.Tile.ToWorldCoordinates()) <= Vector2.DistanceSquared(from, b.Value.Tile.ToWorldCoordinates()) ? a : b;

    private void ClearJob(string reason)
    {
        target = null;
        patch.Clear();
        jobId = 0;
        status = reason;
        ReleaseActivity();
    }
    public override void Exit(in ActionContext ctx)
    {
        // The vein survives interruption, but a route from the old body position does not.
        target = null;
        status = "resume requires approach";
        sinceSearch = SearchEveryTicks;
    }
}
