#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>A query owns its frontier and the suspended edge generator. Yielding spends no
/// search knowledge; a terrain change inside the region it has explored invalidates the query
/// explicitly, and one outside that region leaves it alone. Only the main thread advances it
/// because the terrain adapter may call native collision.</summary>
public sealed class ContinueRouteSearch : IDisposable
{
    private readonly ITileWorld world;
    private readonly bool lava, oneWay;
    private readonly NavNode start;
    private readonly BodyPhysics.Pose? pose;
    private readonly Func<NavStep, bool>? acceptFirst;
    private readonly Rectangle[] avoidance;
    private readonly PriorityQueue<NavNode, (float Cost, int Reuse, int X, int Y, int Air, bool Latched, int Dash)> open = new();
    private readonly Dictionary<NavNode, float> costs = new();
    private readonly Dictionary<NavNode, (NavNode From, List<NavStep> Steps)> parents = new();
    private readonly Dictionary<Point, List<NavStep>> suffixes;
    private readonly HashSet<NavNode> closed = new();
    private readonly HashSet<NavNode> canReturn = new();
    private readonly Dictionary<NavNode, HashSet<NavNode>> predecessors = new();
    private IEnumerator<(NavStep Step, float Cost)?>? edges;
    private NavNode expanding, best;
    private float bestH;
    public Point? Goal { get; }
    public Point Start => start.Tile;
    public int Expansions { get; private set; }
    public int WorkUnits { get; private set; }
    public int ExperienceRoutesUsed { get; private set; }
    public bool Finished { get; private set; }
    public AStar.SearchStopReason Stop { get; private set; } = AStar.SearchStopReason.ExpansionBudget;
    public HashSet<Point> Reached { get; } = new();
    public Dictionary<Point, float> TravelCosts { get; } = new();
    public Dictionary<Point, float> TravelTicks { get; } = new();
    // What the query has read, as the bounding box of every tile it has reached, and the revision it
    // was last found clean at. The revision advances on every clean answer rather than staying at
    // the query's birth, which is what keeps the record's window covering the gap between two checks
    // instead of the query's whole life: without it a query that survives a window's worth of edits
    // anywhere in the world falls off the back of the record and restarts on the global edit count,
    // which is the cadence this whole mechanism exists to remove.
    private int exploredMinX, exploredMinY, exploredMaxX, exploredMaxY;
    private int checkedRevision;
    private bool invalidated;
    private readonly Func<int, int, bool> readAnythingAt;

    /// <summary>The box of tiles this query has reached, for a diagnostic and for the fixture that
    /// pins the invalidation margin to the scan reach it is derived from.</summary>
    public Rectangle ExploredBounds => new(exploredMinX, exploredMinY,
        exploredMaxX - exploredMinX + 1, exploredMaxY - exploredMinY + 1);

    /// <summary>
    /// Still answering about the world it was started in, and nothing announced since has landed
    /// anywhere it read. The second half used to be a compare of one world-global counter, so a
    /// player breaking a tile on the far side of the loaded world discarded a frontier that had
    /// never looked there; it is now the question the counter was standing in for.
    ///
    /// <para>Cost per call is the number of edits announced since the last call, which is nothing on
    /// the overwhelming majority of them: an unchanged counter returns on one integer compare, and a
    /// clean answer moves this query's own revision up so the same edits are never walked twice.
    /// Each edit walked costs a box test and, only if that box holds it, a scan of the reached set.
    /// Invalidity is remembered, because a query can never become valid again.</para>
    /// </summary>
    public bool Valid
    {
        get
        {
            if (invalidated) return false;
            if (world != NavGrid.World) { invalidated = true; return false; }
            int now = world.Revision;
            if (now == checkedRevision) return true;
            if (world.ChangedSince(checkedRevision, readAnythingAt) != TerrainEditVerdict.Unchanged)
            {
                invalidated = true;
                return false;
            }
            checkedRevision = now;
            return true;
        }
    }

    /// <summary>
    /// Whether an edit here could have changed an edge this query has already priced. The bounding
    /// box is a pre-filter that rejects a distant edit on four compares; a box hit then asks the
    /// reached set itself, because a region is rarely rectangular and the box around a corridor
    /// holds a great deal of world the corridor never read.
    ///
    /// <para>The margin is <see cref="AStar.ScanReaches"/> rather than a tile or two, and that is
    /// load-bearing. A tile's edges are generated by scanning outward — a drop steers sideways for
    /// as long as the deepest fall takes — so an edit two dozen columns from a tile the query has
    /// closed can change what that tile's drop does, and a margin drawn from the body's own size
    /// would leave that stale. It is the same box the edge cache drops entries in, which is the
    /// point: one statement of how far a scan reads, read by the two things that depend on it.</para>
    /// </summary>
    private bool ReadAnythingAt(int x, int y)
    {
        if (x < exploredMinX - AStar.EdgeReachX || x > exploredMaxX + AStar.EdgeReachX) return false;
        if (y < exploredMinY - AStar.EdgeReachUp || y > exploredMaxY + AStar.EdgeReachDown) return false;
        foreach (Point tile in Reached)
            if (AStar.ScanReaches(tile.X, tile.Y, x, y)) return true;
        return false;
    }

    private void Touch(Point tile)
    {
        if (!Reached.Add(tile)) return;
        if (tile.X < exploredMinX) exploredMinX = tile.X;
        if (tile.X > exploredMaxX) exploredMaxX = tile.X;
        if (tile.Y < exploredMinY) exploredMinY = tile.Y;
        if (tile.Y > exploredMaxY) exploredMaxY = tile.Y;
    }
    /// <summary>Both directions have been generated under this query's traversal policy.
    /// A nearby coordinate alone cannot establish this across a one-way drop.</summary>
    public bool CanReuseFrom(Point feet) => Valid && canReturn.Contains(NavNode.At(feet));
    /// <summary>Region membership transfers within a proven two-way component, but an arrival
    /// time measured from its old root does not. A moved-origin estimate remains unknown.</summary>
    public float? EstimatedTicks(Point from, Point to) => Valid && from == start.Tile
        && TravelTicks.TryGetValue(to, out float ticks) ? ticks : null;

    public ContinueRouteSearch(Point from, Point? goal, bool allowLava, bool allowOneWay,
        BodyPhysics.Pose? actualPose = null, Func<NavStep, bool>? acceptFirstStep = null)
    {
        world = NavGrid.World; lava = allowLava; oneWay = allowOneWay;
        start = NavNode.At(from); Goal = goal; pose = actualPose; acceptFirst = acceptFirstStep;
        suffixes = goal is Point destination ? RememberExecutedRoutes.World.Suffixes(world, destination, allowLava,
            AStar.PriceRememberedStep, allowOneWay ? null : AStar.RememberedStepHasReturn) : new();
        best = start; bestH = Distance(from);
        avoidance = AStar.Avoid.ToArray();
        checkedRevision = world.Revision;
        readAnythingAt = ReadAnythingAt;
        exploredMinX = exploredMaxX = from.X; exploredMinY = exploredMaxY = from.Y;
        costs[start] = 0; Enqueue(start, Distance(from)); Touch(from); TravelCosts[from] = 0;
        TravelTicks[from] = 0;
        canReturn.Add(start);
    }

    public void Advance(int workBudget, double milliseconds = 0, int nodeLimit = 12000)
    {
        if (Finished) return;
        if (!Valid) { Finished = true; Stop = AStar.SearchStopReason.InvalidStart; Dispose(); return; }
        long deadline = LimitPlanningWork.Deadline(milliseconds);
        bool oldLava = AStar.AllowLava, oldOneWay = AStar.AllowOneWayDrops;
        double oldBudget = AStar.MsBudget;
        long oldDeadline = AStar.Deadline;
        var oldAvoidance = AStar.Avoid.ToArray();
        AStar.Avoid.Clear(); AStar.Avoid.AddRange(avoidance);
        AStar.Deadline = deadline;
        AStar.AllowLava = lava; AStar.AllowOneWayDrops = oneWay;
        // Nested returnability probes receive only the remaining slice, not an unlimited search.
        try
        {
            Stop = AStar.SearchStopReason.ExpansionBudget;
            for (int work = 0; work < workBudget; work++)
            {
                // One bounded generator operation is reserved even after upstream work spent
                // the shared allowance. Otherwise a retained query can starve indefinitely.
                if (work > 0 && deadline != 0 && Stopwatch.GetTimestamp() >= deadline) { Stop = AStar.SearchStopReason.Deadline; return; }
                if (costs.Count >= nodeLimit) { Finished = true; Dispose(); return; }
                if (edges == null)
                {
                    while (open.Count > 0 && closed.Contains(open.Peek())) open.Dequeue();
                    if (open.Count == 0) { Finished = true; Stop = AStar.SearchStopReason.Exhausted; return; }
                    expanding = open.Dequeue(); closed.Add(expanding); Expansions++;
                    if (Goal is Point goal && expanding.Tile == goal)
                    { best = expanding; Finished = true; Stop = AStar.SearchStopReason.Found; return; }
                    float h = Distance(expanding.Tile);
                    if (h < bestH && NavGrid.LavaTilesAt(expanding.Tile.X, expanding.Tile.Y) == 0)
                    { best = expanding; bestH = h; }
                    if (expanding.Mobility == default && Goal is Point end && suffixes.TryGetValue(expanding.Tile, out var suffix)
                        && (expanding != start || acceptFirst == null || acceptFirst(suffix[0])))
                    {
                        var destination = NavNode.At(end);
                        float routeCost = costs[expanding];
                        bool permitted = true;
                        foreach (NavStep step in suffix)
                        {
                            if (!oneWay && !AStar.RememberedStepHasReturn(step)) { permitted = false; break; }
                            routeCost += AStar.PriceRememberedStep(step);
                        }
                        if (permitted && (!costs.TryGetValue(destination, out float priorRoute) || routeCost < priorRoute))
                        {
                            costs[destination] = routeCost; parents[destination] = (expanding, suffix);
                            Enqueue(destination, routeCost, reused: true); ExperienceRoutesUsed++;
                        }
                    }
                    edges = AStar.NeighbourWork(expanding, !oneWay, expanding == start ? pose : null).GetEnumerator();
                }
                AStar.MsBudget = deadline == 0 ? 0 : Math.Max(.000001, (deadline - Stopwatch.GetTimestamp()) * 1000d / Stopwatch.Frequency);
                WorkUnits++;
                if (!edges.MoveNext()) { edges.Dispose(); edges = null; continue; }
                if (edges.Current is not { } edge) continue;
                if (expanding == start && acceptFirst != null && !acceptFirst(edge.Step)) continue;
                var next = new NavNode(edge.Step.Tile, edge.Step.Mobility);
                ObserveConnection(expanding, next);
                float cost = costs[expanding] + edge.Cost;
                if (costs.TryGetValue(next, out float old) && old <= cost) continue;
                costs[next] = cost; parents[next] = (expanding, new List<NavStep> { edge.Step });
                closed.Remove(next); Enqueue(next, cost + Distance(next.Tile));
                Touch(next.Tile);
                if (!TravelCosts.TryGetValue(next.Tile, out float known) || cost < known) TravelCosts[next.Tile] = cost;
                float ticks = TravelTicks.GetValueOrDefault(expanding.Tile) + Math.Max(1, edge.Step.Ticks);
                if (!TravelTicks.TryGetValue(next.Tile, out float knownTicks) || ticks < knownTicks) TravelTicks[next.Tile] = ticks;
            }
        }
        finally
        {
            AStar.AllowLava = oldLava; AStar.AllowOneWayDrops = oldOneWay; AStar.MsBudget = oldBudget;
            AStar.Deadline = oldDeadline;
            AStar.Avoid.Clear(); AStar.Avoid.AddRange(oldAvoidance);
        }
    }

    public NavPath? Result()
    {
        if (best == start) return null;
        var steps = new List<NavStep>();
        for (NavNode at = best; at != start;)
        {
            var parent = parents[at];
            for (int i = parent.Steps.Count - 1; i >= 0; i--) steps.Add(parent.Steps[i]);
            at = parent.From;
        }
        steps.Reverse();
        return new NavPath(steps, best.Tile, Stop != AStar.SearchStopReason.Found);
    }

    private float Distance(Point point) => Goal is Point goal ? Math.Abs(point.X - goal.X) + Math.Abs(point.Y - goal.Y) * .5f : 0;
    private void ObserveConnection(NavNode from, NavNode to)
    {
        if (!predecessors.TryGetValue(to, out var incoming)) predecessors[to] = incoming = new();
        incoming.Add(from);
        if (!canReturn.Contains(to) || !canReturn.Add(from)) return;
        var newlyConnected = new Queue<NavNode>(); newlyConnected.Enqueue(from);
        while (newlyConnected.TryDequeue(out var at))
            if (predecessors.TryGetValue(at, out var prior))
                foreach (var node in prior)
                    if (canReturn.Add(node)) newlyConnected.Enqueue(node);
    }
    private void Enqueue(NavNode node, float cost, bool reused = false) => open.Enqueue(node,
        (cost, reused ? 0 : 1, node.Tile.X, node.Tile.Y, node.Mobility.AirJumpsLeft, node.Mobility.Latched, node.Mobility.DashCooldown));
    public void Dispose() { edges?.Dispose(); edges = null; }
}
