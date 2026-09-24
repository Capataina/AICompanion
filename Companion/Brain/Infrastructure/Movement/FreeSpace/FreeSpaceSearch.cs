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

    /// <summary>
    /// The closed corner nearest the goal so far by the straight line the heuristic measures, or null for a search with no
    /// goal. It is Recast/Detour's <c>lastBestNode</c>: a route search that has not reached its goal, or never will, still
    /// knows the nearest place it has proven the body can get to, and a route to that place is the partial result the body
    /// flies while the rest of the question is still being asked.
    /// </summary>
    public Point? Closest { get; private set; }
    private float closestDistance = float.PositiveInfinity;

    /// <summary>The corners closed so far; for the flood this is the reachable region.</summary>
    public IReadOnlySet<Point> Reached => closed;

    /// <summary>The same corners in the order they were closed, so a consumer deriving something from the
    /// region can extend it by what was added since it last looked rather than rebuilding it whole. The
    /// reach sense's tile set is that consumer: rebuilt whole every time a growing flood advanced, it was 5%
    /// of the brain thread on the replay of the 22 September 2026 capture (profiled 23 September 2026).</summary>
    public IReadOnlyList<Point> ReachedInOrder => closedInOrder;
    private readonly List<Point> closedInOrder = new();

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

    /// <summary>
    /// A rectangle in world pixels outside which a corner is never queued, or null for none. It bounds the way the disc does, so
    /// <c>Exhausted</c> means every corner reachable without leaving the rectangle is closed. The intent sense asks it which
    /// places inside the player's region join him without leaving the region, which the reach disc cannot answer: a way round
    /// outside the region joins both sides of a wall inside it, so the disc holds the player from either side.
    /// </summary>
    public Rectangle? Bounds { get; init; }

    /// <param name="priceClearance">Whether edges carry the corridor-middle price; a flood that answers only which
    /// places join, as the meeting place and the intent region's side flood ask, prices by length alone.</param>
    /// <param name="radius">See <see cref="Radius"/>; infinite unless the caller bounds a flood.</param>
    public FreeSpaceSearch(ITileWorld world, Point start, Point? goal, bool priceClearance = true,
        float radius = float.PositiveInfinity)
    {
        if (goal != null && !float.IsPositiveInfinity(radius))
            throw new ArgumentException("a radius bounds a flood; a route search has a goal and no disc", nameof(radius));
        this.world = world;
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
    /// Whether the search still describes the world: the same world object, and no edit since it
    /// began inside what it read. Answered from the edit record's ring, so a world edited only far
    /// away keeps the search.
    /// </summary>
    public bool Valid
    {
        get
        {
            if (!ReferenceEquals(world, MovementQueries.World) && !ReferenceEquals(world, WorldOverride)) return false;
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
        while (open.Count > 0)
        {
            // All callers borrow the brain's allowance. Checking before removal keeps
            // the frontier intact when a deterministic or wall-clock slice runs out.
            if (LimitPlanningWork.IsActive && !LimitPlanningWork.Current.TrySpend("free-space"))
            { Stop = StopReason.Deadline; return false; }
            open.TryDequeue(out Point node, out _);
            if (closed.Contains(node)) continue;
            closed.Add(node);
            closedInOrder.Add(node);
            Expansions++;
            spent++;
            if (Goal != null && Heuristic(node) is float toGoal && toGoal < closestDistance) { closestDistance = toGoal; Closest = node; }
            if (Goal is Point goal && node == goal) return Finish(StopReason.Found);
            float here = cost[node];
            foreach (Point step in CornerGraph.Neighbours)
            {
                var next = new Point(node.X + step.X, node.Y + step.Y);
                if (closed.Contains(next)) continue;
                if (!usable.TryGetValue(next, out bool fits))
                    usable[next] = fits = CornerGraph.Usable(world, next);
                if (!fits) continue;
                float edge = priceClearance ? CornerGraph.EdgeCost(world, node, next) : Vector2.Distance(CornerGraph.ToWorld(node), CornerGraph.ToWorld(next));
                if (Avoid.Count > 0) edge *= AvoidancePenalty(next);
                float tentative = here + edge;
                if (cost.TryGetValue(next, out float known) && known <= tentative) continue;
                // Outside the disc is never queued, so the queue empties exactly when every corner
                // reachable inside the disc is closed and Exhausted is proven for the disc rather than the world.
                if (!float.IsPositiveInfinity(Radius) && Vector2.Distance(CornerGraph.ToWorld(next), CornerGraph.ToWorld(Start)) > Radius) continue;
                if (Bounds is Rectangle box && !box.Contains(next.X * 16, next.Y * 16)) continue;
                cost[next] = tentative;
                parent[next] = node;
                Touch(next);
                open.Enqueue(next, tentative + Heuristic(next));
            }
            if (closed.Count >= NodeLimit) return Finish(StopReason.NodeLimit);
            if (spent >= expansionBudget) { Stop = StopReason.ExpansionBudget; return false; }
            // Asked through the decision clock, so a replay of a recorded session cuts this search at the same expansion.
            if (deadline != 0 && Selection.Computation.DecisionClock.Passed(deadline)) { Stop = StopReason.Deadline; return false; }
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
        => Goal is Point goal ? Vector2.Distance(CornerGraph.ToWorld(corner), CornerGraph.ToWorld(goal)) : 0f;

    private float AvoidancePenalty(Point corner)
        => ClearanceHeat.Penalty(ClearanceHeat.ToBoxes(CornerGraph.ToWorld(corner), Avoid));

    private void Touch(Point corner)
    {
        minX = Math.Min(minX, corner.X - 1); maxX = Math.Max(maxX, corner.X);
        minY = Math.Min(minY, corner.Y - 1); maxY = Math.Max(maxY, corner.Y);
    }
}
