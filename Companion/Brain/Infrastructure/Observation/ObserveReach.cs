#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>What the body can get to, as a sense every consumer reads rather than a private
/// answer one component owns.</summary>
public enum ReachVerdict
{
    /// <summary>In the region flooded from the body's own cell.</summary>
    Reachable,
    /// <summary>The flood has not finished and has not claimed this tile: unknown, not absent.</summary>
    NotYet,
    /// <summary>The flood ran out of free space before its budget and never claimed this tile.</summary>
    Unreachable,
}

/// <summary>
/// Where the body can fly to: one free-space flood over corner nodes from the corner nearest the
/// body, read by everyone who needs the answer. There is one flood and not two: every edge of the
/// corner graph is two-way for a body that flies, so "can go" and "can come home from" are the same
/// region, and the walker's one-way exception for a player standing at the bottom of a drop has no
/// meaning.
///
/// <para>The flood is a disc, not a world. It is bounded at twice
/// <see cref="Weights.ReachKnownRadiusTiles"/> of straight-line distance from its root, so in an open
/// world it finishes rather than growing until the search's node limit stops it unfinished — which is
/// what the first orb tree did, and it left <see cref="Complete"/> false for the life of every
/// session, so nothing could ever be proven unreachable and hunting could never prove an absence. A
/// tile within the known radius of the root that a finished flood never claimed is proven
/// unreachable, which means it has no route shorter than about three times its straight line, since
/// a route that leaves the disc goes out past twice the radius and back; a tile beyond the known
/// radius answers not yet, because the flood was never asked about it. Underground the disc is mostly
/// rock and closes in a few resolves; in open sky it is the whole disc and takes a few hundred ticks,
/// which is the one place "not yet" stays the common answer for long.</para>
///
/// <para>Once the body has travelled half the known radius from the root, a second flood is grown
/// there while the first keeps answering, and it replaces the first only when it has finished, so
/// ordinary travel never empties the region the way a terrain edit does. The flood keeps the route
/// search's clearance pricing on its edges, and the travel estimates read off it are therefore
/// priced rather than plain distances: the reunion charge takes the larger of the straight line and
/// that estimate, and every reunion weight was tuned against the priced number — an unpriced flood
/// was tried and had keeping company stroll beside a player fifteen tiles off. Which is why the
/// bound is a disc and not a cost ball: a ball is sound only in unpriced units.</para>
///
/// <para>The sense is refreshed by the positioner's resolve rather than by <see cref="Senses.Update"/>,
/// and that is deliberate: where the flood is rooted and when a replacement takes over is decided on
/// the rescore, because the region a candidate is scored against has to hold still for the rescore
/// that scores it. Consumers that read between resolves read the flood the last resolve left, which
/// is a tick or two stale and still the best answer to "where can I get to".</para>
///
/// <para>The tri-state is the whole point of exposing it. A tile missing from an unfinished flood is
/// unknown rather than absent, so an activity that refuses <see cref="ReachVerdict.NotYet"/> is
/// declining to start on an unanswered search rather than asserting the place does not exist.</para>
/// </summary>
public sealed class ReachSense
{
    /// <summary>How many resolves a flood is reused for. It mirrors the positioner's rescore interval,
    /// because the region a candidate is scored against and the region that candidate came from have
    /// to be the same one; if the two drift a spot is admitted against a region it was never in.</summary>
    private const int RefloodTicks = 12;

    /// <summary>How many resolves the body may sit at a corner the flood has not closed before the
    /// flood is rooted afresh there: a body carried into another pocket by the recovery flight is
    /// not connected to the region it left, and an unfinished flood would never say so.</summary>
    private const int MissingRootPatience = 3;

    /// <summary>The flood's disc, in pixels of straight-line distance from its root: twice the known radius, so a
    /// tile inside the known radius is proven absent only when every route to it is a detour of about three times its straight line.</summary>
    private static float DiscRadius => Weights.ReachKnownRadiusTiles * 2f * 16f;
    /// <summary>How far, in pixels of straight-line distance, the body may be from the root before a replacement flood is grown there.</summary>
    private static float RerootDistance => Weights.ReachKnownRadiusTiles * 16f / 2f;

    private FreeSpaceSearch? flood;
    /// <summary>The replacement flood growing under a body that has travelled far from the root; answers come
    /// from <see cref="flood"/> until this one has finished, then it becomes the flood.</summary>
    private FreeSpaceSearch? pending;
    private HashSet<Point>? tiles;
    private int tilesBuiltAt = -1;
    private int sinceFlood = RefloodTicks;
    private int rootMissing;

    /// <summary>How many times the flood has been thrown away and started again this session. It
    /// counts the discard rather than the advance, so a cadence that keeps growing one flood does
    /// not register: this is the number a terrain edit used to drive from anywhere in the world.</summary>
    public int Refloods { get; private set; }

    /// <summary>How many times a replacement flood was grown under a travelling body while the old one kept
    /// answering. Counted apart from <see cref="Refloods"/> because a reroot empties nothing.</summary>
    public int Reroots { get; private set; }

    /// <summary>Which flood is answering, advanced every time a different flood object starts answering: a reflood, and a
    /// replacement taking over from a reroot. A refusal proved from one finished flood stays true while that flood answers,
    /// because a finished flood does not grow and an edit it read would have replaced it; so this, not the world's terrain
    /// revision, is what a remembered proof is keyed on. The terrain revision moves on every edit anywhere, including the
    /// torch the companion just placed, and a proof keyed on it was thrown away by the companion's own work.</summary>
    public int FloodGeneration { get; private set; }

    /// <summary>Whether the flood ran out of free space inside its travel radius, so a tile inside the known
    /// radius that it never claimed is truly absent. Says nothing about a tile beyond the known radius.</summary>
    public bool Complete { get; private set; }

    /// <summary>Whether a tile is close enough to the flood's root for the sense to give a verdict about it.</summary>
    public bool WithinKnownRadius(Point tile)
        => flood != null && Vector2.Distance(CornerGraph.ToWorld(flood.Start), new Vector2(tile.X * 16f + 8f, tile.Y * 16f + 8f))
            <= Weights.ReachKnownRadiusTiles * 16f;

    /// <summary>A corner the finished flood never claimed, inside the known radius: proven absent rather than not yet.</summary>
    public bool ProvenUnreachableCorner(Point corner)
        => Complete && flood != null && !flood.Reached.Contains(corner)
            && Vector2.Distance(CornerGraph.ToWorld(flood.Start), CornerGraph.ToWorld(corner)) <= Weights.ReachKnownRadiusTiles * 16f;

    /// <summary>The same fact under the name the positioner reads; one flood means one completeness.</summary>
    public bool ScoredComplete => Complete;

    /// <summary>Wall-clock of the last flood slice, for the telemetry.</summary>
    public double LastFloodMs { get; private set; }

    /// <summary>How many corners the flood has closed, and how many tiles those corners cover.</summary>
    public int CornerCount => flood?.Reached.Count ?? 0;
    public int AnyCount => Tiles.Count;
    public int TwoWayCount => AnyCount;

    /// <summary>The region the positioner scores candidates against.</summary>
    public bool InScoredRegion(Point tile) => Returnable(tile);

    /// <summary>Every tile in the region, for a caller that draws from it rather than asking about
    /// one tile. Empty rather than null before the first flood, so no caller has to know the difference.</summary>
    public IReadOnlyCollection<Point> ScoredTiles => Tiles;

    /// <summary>Whether the body can fly to this tile: a corner of it is in the flood.</summary>
    public bool Returnable(Point tile) => flood != null && CornerGraph.AnyCornerOf(tile, flood.Reached.Contains);

    /// <summary>Whether a corner node itself is in the flood.</summary>
    public bool ReachesCorner(Point corner) => flood != null && flood.Reached.Contains(corner);

    // The walker's one-way exception, kept as constants only until the positioner and the recorder
    // that name it are rewritten for the orb: a flying body has no edge it cannot come back along.
    public bool PlayerOnlyOneWay => false;
    public bool ReachableOneWay(Point tile) => Returnable(tile);

    /// <summary>Reachable, not yet known, or proven absent; a tile beyond the known radius is never proven absent.</summary>
    public ReachVerdict Reachable(Point tile)
        => Returnable(tile) ? ReachVerdict.Reachable
            : Complete && WithinKnownRadius(tile) ? ReachVerdict.Unreachable
            : ReachVerdict.NotYet;

    /// <summary>
    /// The flood's travel cost to the nearest reached corner of a tile, in ticks at the body's cap;
    /// null while unreached. Root-relative: the cost counts from the flood's root, and <paramref name="from"/>
    /// selects nothing — a caller pricing a leg between two other points differs two costs itself.
    /// </summary>
    public float? EstimatedTravelTicks(Point from, Point tile)
    {
        if (flood == null) return null;
        float? best = null;
        foreach (Point corner in new[] { tile, new Point(tile.X + 1, tile.Y), new Point(tile.X, tile.Y + 1), new Point(tile.X + 1, tile.Y + 1) })
            if (flood.CostTo(corner) is float cost && (best == null || cost < best)) best = cost;
        return best is float found ? found / System.MathF.Max(0.1f, OrbPace.MaxSpeed) : null;
    }

    /// <summary>The flood's travel cost to one corner, in ticks at the body's cap; null while the flood has not closed it.</summary>
    public float? TravelTicksToCorner(Point corner)
        => flood?.CostTo(corner) is float cost ? cost / System.MathF.Max(0.1f, OrbPace.MaxSpeed) : null;

    private HashSet<Point> Tiles
    {
        get
        {
            if (flood == null) return tiles ??= new HashSet<Point>();
            if (tiles != null && tilesBuiltAt == flood.Reached.Count) return tiles;
            tiles = new HashSet<Point>();
            foreach (Point corner in flood.Reached)
            {
                tiles.Add(new Point(corner.X - 1, corner.Y - 1));
                tiles.Add(new Point(corner.X, corner.Y - 1));
                tiles.Add(new Point(corner.X - 1, corner.Y));
                tiles.Add(corner);
            }
            tilesBuiltAt = flood.Reached.Count;
            return tiles;
        }
    }

    /// <summary>
    /// Age the region by one resolve. The cadence counts resolves rather than game ticks, and that is
    /// load-bearing rather than incidental: the flood is bounded per advance and grows across successive
    /// resolves, so a caller that resolves repeatedly inside one tick is asking for the region to keep
    /// expanding, and a cadence keyed to the tick would advance it once and never finish.
    /// </summary>
    public void Age() => sinceFlood++;

    /// <summary>
    /// Advance the flood if the cadence has passed, or start it again where the world has been edited
    /// inside what it read, or where the body has left its region. The
    /// terrain check lives here rather than in the caller so every consumer of the sense reads a
    /// region that survived the dig, and it expires the cadence rather than waiting for the reuse
    /// test, because an edit the flood read has to land on the next resolve and not up to a cadence later.
    /// </summary>
    public void Refresh(Senses senses)
    {
        if (flood?.Valid == false)
            sinceFlood = RefloodTicks;
        if (pending?.Valid == false)
            pending = null;
        if (pending?.Finished == true)
            TakeOver();
        if (flood != null && sinceFlood < RefloodTicks)
            return;
        sinceFlood = 0;

        ITileWorld world = MovementQueries.World;
        Point? root = CornerGraph.NearestUsable(world, senses.Companion.Center, 2, requireSweep: false);
        if (root == null)
        {
            // Inside something: keep the last flood, which is a tick or two stale and still the best
            // answer to "where can I get to" until the body is out.
            return;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool rooted = flood != null && flood.Valid && flood.Reached.Contains(root.Value);
        // A body that has outrun the flood answering for it but is already inside the replacement
        // takes the replacement now, unfinished: its answers are not yet rather than nothing.
        if (!rooted && pending != null && pending.Reached.Contains(root.Value))
        {
            TakeOver();
            rooted = true;
        }
        if (flood != null && flood.Valid && !rooted && !flood.Finished && ++rootMissing < MissingRootPatience)
            rooted = true;
        if (!rooted)
        {
            // The body is outside the region, carried there by recovery or by a flood that never
            // held it: the old answers are about somewhere else, so they go, and the region is
            // empty until the new flood has grown — the one case where travel empties it.
            rootMissing = 0;
            Refloods++;
            FloodGeneration++;
            flood = Flood(world, root.Value);
            pending = null;
            tiles = null;
        }
        else
        {
            if (flood!.Reached.Contains(root.Value)) rootMissing = 0;
            // The body has travelled far from the root: grow the replacement here while the old flood
            // keeps answering, so ordinary travel never reads as an empty region.
            if (pending == null
                && Vector2.Distance(CornerGraph.ToWorld(root.Value), CornerGraph.ToWorld(flood.Start)) > RerootDistance)
            {
                pending = Flood(world, root.Value);
                Reroots++;
            }
        }
        flood.Advance(Weights.ReachFloodExpansions, Weights.PositionReachMilliseconds);
        Complete = flood.Finished && flood.Stop == FreeSpaceSearch.StopReason.Exhausted;
        LastFloodMs = clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// Grow whichever flood is unfinished by one slice, once per brain tick. Refresh runs on the rescore
    /// cadence and decides where the flood is rooted and when a replacement takes over, because the
    /// region a candidate is scored against has to hold still for a rescore; growth only adds corners
    /// to a region and breaks no admission, so it need not wait. Growing only on the cadence took the
    /// first flood of the recorded route three hundred ticks to complete and its replacement a hundred
    /// and fifty to catch the body, during which nothing near the body could be proven absent.
    /// </summary>
    public void Grow()
    {
        FreeSpaceSearch? growing = pending ?? flood;
        if (growing == null || growing.Finished || !growing.Valid) return;
        growing.Advance(Weights.ReachFloodExpansions, Weights.PositionReachMilliseconds);
        if (ReferenceEquals(growing, flood))
            Complete = flood.Finished && flood.Stop == FreeSpaceSearch.StopReason.Exhausted;
    }

    /// <summary>The replacement flood becomes the flood; the tile set derived from the old one goes with it.</summary>
    private void TakeOver()
    {
        flood = pending;
        FloodGeneration++;
        pending = null;
        tiles = null;
        Complete = flood!.Finished && flood.Stop == FreeSpaceSearch.StopReason.Exhausted;
    }

    /// <summary>A reach flood: goalless, priced like the route search so its estimates stay calibrated, and bounded to the disc.</summary>
    private static FreeSpaceSearch Flood(ITileWorld world, Point root)
        => new(world, root, null, radius: DiscRadius);
}
