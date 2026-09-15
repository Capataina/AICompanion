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

    /// <summary>
    /// The most corners one search may close, a backstop rather than a bound the design relies on. A flood
    /// that stops here has not exhausted anything: it finishes without proving an absence, and a reach sense
    /// that sat on such a flood could never say "unreachable" again. The reach flood is bounded by
    /// <see cref="Radius"/> instead, and this limit only has to sit above the corners a disc of that
    /// radius can hold in fully open air, which for the sense's radius is about sixty-five thousand.
    /// </summary>
    public const int NodeLimit = 80000;

    private readonly ITileWorld world;
    private readonly LiquidImmunity immunity;
    private readonly bool immunityOverridden;
    /// <summary>
    /// The revision this query's answer is known to be current as of — the record's revision when the search
    /// began, and then the record's revision at every later moment the query was asked and found clean.
    ///
    /// <para>It moves, and that is the whole point. The edit record keeps a fixed-size ring, so a revision older
    /// than the ring can no longer be compared against and <c>ChangedSince</c> can only answer <c>TooOld</c>. If
    /// this stayed at the revision the search started on, a query asked continuously while the world was edited
    /// far away would still fall off the back of the ring after a window's worth of edits and throw its answer
    /// away for no reason at all — measured here at exactly edit 256 of 256, with every edit fifty rows outside
    /// the region the query read. Re-basing on a clean answer is what makes the ring a window on "since you last
    /// asked" rather than a countdown on the query's whole life.</para>
    /// </summary>
    private int revisionAtStart;
    private readonly PriorityQueue<Point, float> open = new();
    private readonly Dictionary<Point, float> cost = new(CornerKey.Comparer);
    private readonly Dictionary<Point, Point> parent = new(CornerKey.Comparer);
    private readonly HashSet<Point> closed = new(CornerKey.Comparer);
    // A corner's usability is asked by up to eight neighbours; four tile reads once beats thirty-two.
    private readonly Dictionary<Point, bool> usable = new(CornerKey.Comparer);
    private int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

    private readonly bool priceClearance;

    public Point Start { get; }
    public Point? Goal { get; }
    public bool Finished { get; private set; }
    public StopReason Stop { get; private set; } = StopReason.ExpansionBudget;
    public int Expansions { get; private set; }
    /// <summary>Extra cost for corners inside these rectangles (world pixels): threat bodies a route should go around rather than through.</summary>
    public IReadOnlyList<Rectangle> Avoid { get; set; } = Array.Empty<Rectangle>();
    /// <summary>A goal given as a predicate rather than a corner: the search finishes Found at the first closed corner it accepts.</summary>
    public Func<Point, bool>? Accept { get; set; }
    /// <summary>An ordering value for a predicate goal, used as the heuristic so the flood leans toward what the caller prefers.</summary>
    public Func<Point, float>? Prefer
    {
        get => prefer;
        set
        {
            // A preference reorders the queue away from cost order, and a disc-bounded flood's "exhausted
            // inside the disc" is argued from closing every reachable corner inside it, which a preference
            // does not break — but a bounded flood is the reach sense's and answers "where can I get to";
            // leaning it toward a caller's goal would make one consumer's preference everyone's region.
            if (value != null && !float.IsPositiveInfinity(Radius))
                throw new InvalidOperationException("a bounded flood is a region, not a search; it takes no preference");
            prefer = value;
        }
    }
    private Func<Point, float>? prefer;
    /// <summary>The corner a predicate goal accepted, once one has.</summary>
    public Point? FoundCorner { get; private set; }

    /// <summary>The corners closed so far; for the flood this is the reachable region.</summary>
    public IReadOnlySet<Point> Reached => closed;

    /// <summary>
    /// The straight-line distance from the start, in world pixels, beyond which a flood does not reach:
    /// a corner outside the disc is never queued, so <c>Exhausted</c> means every corner reachable
    /// inside the disc is closed. A disc and not a travel-cost ball, on purpose: a cost ball is only
    /// sound when costs are lengths, and the reach flood prices its edges by clearance so the travel
    /// estimates read off it stay the ones every reunion weight was tuned against — a priced ball
    /// shrinks in a tight tunnel to a third of its nominal reach, which is exactly where the size rule
    /// sends the body. The disc's guarantee is weaker than the ball's and is stated where it is
    /// consumed: a corner inside the reach sense's known radius that an exhausted flood never claimed
    /// has no route shorter than about three times its straight line, since any route to it that
    /// leaves the disc goes out past twice the known radius and comes back.
    /// </summary>
    public float Radius { get; }

    /// <param name="rules">The immunities this search runs under; the process-wide ones unless a caller
    /// needs to search through liquid the body is already in.</param>
    /// <param name="priceClearance">Whether edges carry the corridor-middle price; a search for the nearest
    /// safe corner wants the nearest and not the widest.</param>
    /// <param name="radius">See <see cref="Radius"/>; infinite unless the caller bounds a flood.</param>
    public FreeSpaceSearch(ITileWorld world, Point start, Point? goal, LiquidImmunity? rules = null, bool priceClearance = true,
        float radius = float.PositiveInfinity)
    {
        if (goal != null && !float.IsPositiveInfinity(radius))
            throw new ArgumentException("a radius bounds a flood; a route search has a goal and no disc", nameof(radius));
        this.world = world;
        immunity = rules ?? OrbTerrain.Immunity;
        immunityOverridden = rules != null;
        this.priceClearance = priceClearance;
        Radius = radius;
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
            if (!ReferenceEquals(world, MovementQueries.World) && !ReferenceEquals(world, WorldOverride)) return false;
            if (immunity != (immunityOverridden ? immunity : OrbTerrain.Immunity)) return false;
            Rectangle bounds = ExploredBounds;
            // Read once: the record moves under us, and re-basing to a revision later than the one the verdict
            // was computed against would swallow an edit that landed between the two reads.
            int now = world.Revision;
            if (world.ChangedSince(revisionAtStart, (x, y) => bounds.Contains(x, y)) != TerrainEditVerdict.Unchanged)
                return false;
            revisionAtStart = now;
            return true;
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
            if (Accept != null && Accept(node)) { FoundCorner = node; return Finish(StopReason.Found); }
            float here = cost[node];
            foreach (Point step in CornerGraph.Neighbours)
            {
                var next = new Point(node.X + step.X, node.Y + step.Y);
                if (closed.Contains(next)) continue;
                if (!usable.TryGetValue(next, out bool fits))
                    usable[next] = fits = CornerGraph.Usable(world, next, immunity);
                if (!fits) continue;
                float edge = priceClearance ? CornerGraph.EdgeCost(world, node, next) : Vector2.Distance(CornerGraph.ToWorld(node), CornerGraph.ToWorld(next));
                if (Avoid.Count > 0) edge *= AvoidancePenalty(next);
                float tentative = here + edge;
                if (cost.TryGetValue(next, out float known) && known <= tentative) continue;
                // Outside the disc is never queued, so the queue empties exactly when every corner
                // reachable inside the disc is closed and Exhausted is proven for the disc rather than the world.
                if (!float.IsPositiveInfinity(Radius) && Vector2.Distance(CornerGraph.ToWorld(next), CornerGraph.ToWorld(Start)) > Radius) continue;
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
        => Goal is Point goal && closed.Contains(goal) ? PathTo(goal) : null;

    /// <summary>The corners from the start to any closed corner.</summary>
    public List<Point> PathTo(Point corner)
    {
        var route = new List<Point>();
        for (Point at = corner; ; at = parent[at])
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
        => Goal is Point goal ? Vector2.Distance(CornerGraph.ToWorld(corner), CornerGraph.ToWorld(goal))
            : Prefer != null ? Prefer(corner) : 0f;

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
