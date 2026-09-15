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
/// body, read by everyone who needs the answer. Because no cell needs a jump proved, the flood
/// finishes within a few resolves on a whole window, and "not yet known" is the rare answer rather
/// than the common one. There is one flood and not two: every edge of the corner graph is
/// two-way for a body that flies, so "can go" and "can come home from" are the same region, and
/// the walker's one-way exception for a player standing at the bottom of a drop has no meaning.
///
/// <para>The sense is refreshed by the positioner's resolve rather than by <see cref="Senses.Update"/>,
/// and that is deliberate: the immunities are copied into the terrain reading by the brain tick, so
/// a flood run at the top of the tick would answer every consumer under the previous tick's rule.
/// Consumers that read between resolves read the flood the last resolve left, which is a tick or
/// two stale and still the best answer to "where can I get to".</para>
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

    private FreeSpaceSearch? flood;
    private HashSet<Point>? tiles;
    private int tilesBuiltAt = -1;
    private int sinceFlood = RefloodTicks;
    private int rootMissing;

    /// <summary>How many times the flood has been thrown away and started again this session. It
    /// counts the discard rather than the advance, so a cadence that keeps growing one flood does
    /// not register: this is the number a terrain edit used to drive from anywhere in the world.</summary>
    public int Refloods { get; private set; }

    /// <summary>Whether the flood ran out of free space before its budget, so a tile outside it is truly absent.</summary>
    public bool Complete { get; private set; }

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

    /// <summary>Reachable, not yet known, or proven absent.</summary>
    public ReachVerdict Reachable(Point tile)
        => Returnable(tile) ? ReachVerdict.Reachable
            : Complete ? ReachVerdict.Unreachable
            : ReachVerdict.NotYet;

    /// <summary>The flood's travel cost to the nearest reached corner of a tile, in ticks at the body's cap; null while unreached.</summary>
    public float? EstimatedTravelTicks(Point from, Point tile)
    {
        if (flood == null) return null;
        float? best = null;
        foreach (Point corner in new[] { tile, new Point(tile.X + 1, tile.Y), new Point(tile.X, tile.Y + 1), new Point(tile.X + 1, tile.Y + 1) })
            if (flood.CostTo(corner) is float cost && (best == null || cost < best)) best = cost;
        return best is float found ? found / System.MathF.Max(0.1f, OrbPace.MaxSpeed) : null;
    }

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
    /// inside what it read, where the immunities changed, or where the body has left its region. The
    /// terrain check lives here rather than in the caller so every consumer of the sense reads a
    /// region that survived the dig, and it expires the cadence rather than waiting for the reuse
    /// test, because an edit the flood read has to land on the next resolve and not up to a cadence later.
    /// </summary>
    public void Refresh(Senses senses)
    {
        if (flood?.Valid == false)
            sinceFlood = RefloodTicks;
        if (flood != null && sinceFlood < RefloodTicks)
            return;
        sinceFlood = 0;

        ITileWorld world = MovementQueries.World;
        Point? root = CornerGraph.NearestUsable(world, senses.Companion.Center, 2, requireSweep: false);
        if (root == null)
        {
            // Inside something, or in a liquid that is a wall: keep the last flood, which is a tick
            // or two stale and still the best answer to "where can I get to" until the body is out.
            return;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool rooted = flood != null && flood.Valid && flood.Reached.Contains(root.Value);
        if (flood != null && flood.Valid && !rooted && !flood.Finished && ++rootMissing < MissingRootPatience)
            rooted = true;
        if (!rooted)
        {
            rootMissing = 0;
            Refloods++;
            flood = new FreeSpaceSearch(world, root.Value, null);
        }
        else if (flood!.Reached.Contains(root.Value)) rootMissing = 0;
        flood.Advance(Weights.ReachFloodExpansions, Weights.PositionReachMilliseconds);
        Complete = flood.Finished && flood.Stop == FreeSpaceSearch.StopReason.Exhausted;
        LastFloodMs = clock.Elapsed.TotalMilliseconds;
    }
}
