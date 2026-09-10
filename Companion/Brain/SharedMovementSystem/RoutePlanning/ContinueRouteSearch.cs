#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.SharedMovementSystem;

/// <summary>A query owns its frontier and the suspended edge generator. Yielding spends no
/// search knowledge; terrain changes invalidate the query explicitly. Only the main thread
/// advances it because the terrain adapter may call native collision.</summary>
public sealed class ContinueRouteSearch : IDisposable
{
    private readonly ITileWorld world;
    private readonly int revision;
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
    public bool Valid => world == NavGrid.World && revision == world.Revision;
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
        world = NavGrid.World; revision = world.Revision; lava = allowLava; oneWay = allowOneWay;
        start = NavNode.At(from); Goal = goal; pose = actualPose; acceptFirst = acceptFirstStep;
        suffixes = goal is Point destination ? RememberExecutedRoutes.World.Suffixes(world, destination, allowLava,
            AStar.PriceRememberedStep, allowOneWay ? null : AStar.RememberedStepHasReturn) : new();
        best = start; bestH = Distance(from);
        avoidance = AStar.Avoid.ToArray();
        costs[start] = 0; Enqueue(start, Distance(from)); Reached.Add(from); TravelCosts[from] = 0;
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
                Reached.Add(next.Tile);
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
