#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// How a corner is hashed in every set and dictionary the search keeps. The framework's own
/// <c>Point.GetHashCode</c> is <c>X ^ Y</c>, which on a grid collapses thousands of corners onto a
/// few hundred hashes and turns every lookup into a walk down a chain; mixing the two coordinates
/// is the difference between a flood that finishes in a slice and one that spends it hashing.
/// </summary>
public static class CornerKey
{
    private sealed class Mixed : IEqualityComparer<Point>
    {
        public bool Equals(Point a, Point b) => a.X == b.X && a.Y == b.Y;
        public int GetHashCode(Point p) => HashCode.Combine(p.X, p.Y);
    }

    public static readonly IEqualityComparer<Point> Comparer = new Mixed();
}

/// <summary>
/// One resumable best-first search over the corner graph, serving two questions: with no goal it
/// is the reach flood, a Dijkstra expansion outward from the body that records the travel cost
/// of every corner it closes and finishes when the free space runs out; with a goal it is the
/// route search, A* toward one corner with the straight-line distance as the heuristic, which is
/// admissible because every edge costs at least its length.
///
/// <para>It advances in slices, bounded by an expansion budget and the shared planning deadline,
/// and keeps its frontier between slices so a search too large for one tick finishes over
/// several. Every slice records the corners it read, and <see cref="Valid"/> asks the world's
/// edit record whether anything landed inside that box since the search began: the same spatial
/// rule the walker's searches used, kept because it is the right shape — a search is thrown away
/// only where the world changed under what it read.</para>
/// </summary>
public sealed class FreeSpaceSearch
{
    public enum StopReason { ExpansionBudget, Deadline, Exhausted, Found, NodeLimit }

    /// <summary>The most corners one search may close; a flood over a whole loaded world would otherwise grow without bound.</summary>
    public const int NodeLimit = 40000;

    private readonly ITileWorld world;
    private readonly LiquidImmunity immunity;
    private readonly int revisionAtStart;
    private readonly PriorityQueue<Point, float> open = new();
    private readonly Dictionary<Point, float> cost = new(CornerKey.Comparer);
    private readonly Dictionary<Point, Point> parent = new(CornerKey.Comparer);
    private readonly HashSet<Point> closed = new(CornerKey.Comparer);
    // A corner's usability is asked by up to eight neighbours; four tile reads once beats thirty-two.
    private readonly Dictionary<Point, bool> usable = new(CornerKey.Comparer);
    private int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

    public Point Start { get; }
    public Point? Goal { get; }
    public bool Finished { get; private set; }
    public StopReason Stop { get; private set; } = StopReason.ExpansionBudget;
    public int Expansions { get; private set; }
    /// <summary>Extra cost for corners inside these rectangles (world pixels): threat bodies a route should go around rather than through.</summary>
    public IReadOnlyList<Rectangle> Avoid { get; set; } = Array.Empty<Rectangle>();

    /// <summary>The corners closed so far; for the flood this is the reachable region.</summary>
    public IReadOnlySet<Point> Reached => closed;

    public FreeSpaceSearch(ITileWorld world, Point start, Point? goal)
    {
        this.world = world;
        immunity = OrbTerrain.Immunity;
        revisionAtStart = world.Revision;
        Start = start;
        Goal = goal;
        cost[start] = 0f;
        open.Enqueue(start, Heuristic(start));
        Touch(start);
    }

    /// <summary>The tiles the search has read, inflated by the tile the swept edges look at.</summary>
    public Rectangle ExploredBounds => closed.Count == 0 && cost.Count == 0
        ? Rectangle.Empty
        : new Rectangle(minX - 2, minY - 2, maxX - minX + 4, maxY - minY + 4);

    /// <summary>
    /// Whether the search still describes the world: the same world object, the same immunities,
    /// and no edit since it began inside what it read. Answered from the edit record's ring, so
    /// a world edited only far away keeps the search.
    /// </summary>
    public bool Valid
    {
        get
        {
            if (!ReferenceEquals(world, NavGrid.World) && !ReferenceEquals(world, WorldOverride)) return false;
            if (immunity != OrbTerrain.Immunity) return false;
            Rectangle bounds = ExploredBounds;
            return world.ChangedSince(revisionAtStart, (x, y) => bounds.Contains(x, y)) == TerrainEditVerdict.Unchanged;
        }
    }

    /// <summary>A headless tool searching a world that is not the process's live one names it here so validity does not refuse it.</summary>
    public static ITileWorld? WorldOverride { get; set; }

    public float? CostTo(Point corner) => closed.Contains(corner) ? cost[corner] : null;

    /// <summary>
    /// Expand until the budget, the deadline, the goal or the free space runs out. Returns whether
    /// the search finished on this call.
    /// </summary>
    public bool Advance(int expansionBudget, double milliseconds = 0)
    {
        if (Finished) return true;
        long deadline = LimitPlanningWork.Deadline(milliseconds);
        int spent = 0;
        while (open.TryDequeue(out Point node, out _))
        {
            if (closed.Contains(node)) continue;
            closed.Add(node);
            Expansions++;
            spent++;
            if (Goal is Point goal && node == goal) return Finish(StopReason.Found);
            float here = cost[node];
            foreach (Point step in CornerGraph.Neighbours)
            {
                var next = new Point(node.X + step.X, node.Y + step.Y);
                if (closed.Contains(next)) continue;
                if (!usable.TryGetValue(next, out bool fits))
                    usable[next] = fits = CornerGraph.EdgeClear(world, node, next);
                if (!fits) continue;
                float edge = CornerGraph.EdgeCost(world, node, next);
                if (Avoid.Count > 0) edge *= AvoidancePenalty(next);
                float tentative = here + edge;
                if (cost.TryGetValue(next, out float known) && known <= tentative) continue;
                cost[next] = tentative;
                parent[next] = node;
                Touch(next);
                open.Enqueue(next, tentative + Heuristic(next));
            }
            if (closed.Count >= NodeLimit) return Finish(StopReason.NodeLimit);
            if (spent >= expansionBudget) { Stop = StopReason.ExpansionBudget; return false; }
            if (deadline != 0 && System.Diagnostics.Stopwatch.GetTimestamp() >= deadline) { Stop = StopReason.Deadline; return false; }
        }
        return Finish(StopReason.Exhausted);
    }

    /// <summary>The corners from the start to the goal, or null while unfound.</summary>
    public List<Point>? RouteCorners()
    {
        if (Goal is not Point goal || !closed.Contains(goal)) return null;
        var route = new List<Point>();
        for (Point at = goal; ; at = parent[at])
        {
            route.Add(at);
            if (at == Start) break;
        }
        route.Reverse();
        return route;
    }

    private bool Finish(StopReason reason)
    {
        Finished = true;
        Stop = reason;
        return true;
    }

    private float Heuristic(Point corner)
        => Goal is Point goal ? Vector2.Distance(CornerGraph.ToWorld(corner), CornerGraph.ToWorld(goal)) : 0f;

    private float AvoidancePenalty(Point corner)
    {
        Vector2 at = CornerGraph.ToWorld(corner);
        foreach (Rectangle box in Avoid)
            if (box.Contains((int)at.X, (int)at.Y)) return Selection.Weights.ThreatBodyRoutePenalty;
        return 1f;
    }

    private void Touch(Point corner)
    {
        minX = Math.Min(minX, corner.X - 1); maxX = Math.Max(maxX, corner.X);
        minY = Math.Min(minY, corner.Y - 1); maxY = Math.Max(maxY, corner.Y);
    }
}
