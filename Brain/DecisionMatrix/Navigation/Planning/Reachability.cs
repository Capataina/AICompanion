#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// Can a thing get from here to there? Walkers ask the same A* the companion uses.
/// Flyers ask a flood fill over connected air. Phasers are always reachable. Both
/// searches are bounded, and running out of budget answers "reachable", because a
/// threat wrongly ignored costs more than a threat wrongly feared.
/// </summary>
public static class Reachability
{
    public const int WalkerBudget = 400;
    public const int FlyerBudget = 1500;

    public static bool WalkerCanReach(Point fromFeet, Point toFeet)
    {
        Point? start = NavGrid.NearestStandable(fromFeet, 2);
        Point? goal = NavGrid.NearestStandable(toFeet, 2);
        if (start == null || goal == null)
            return true;
        // An enemy takes any drop. The brain sets the one-way rule for the companion's own plans
        // at the end of its tick and it is still set when the senses run at the start of the
        // next, so it is forced on around this search and put back after; left alone, a hunt
        // read a zombie that reaches the player by dropping into a cave as unreachable.
        bool oneWay = AStar.AllowOneWayDrops;
        AStar.AllowOneWayDrops = true;
        NavPath? path;
        int used;
        try
        {
            path = AStar.Find(start.Value, goal.Value, WalkerBudget, out used);
        }
        finally
        {
            AStar.AllowOneWayDrops = oneWay;
        }
        // A partial path is the search saying it could get closer, not that it arrived; only a
        // whole path or a budget that ran out answers yes.
        return (path != null && !path.Partial) || used > WalkerBudget;
    }

    /// <summary>Flood fill through non-solid tiles from the flyer toward a box around the target.</summary>
    public static bool FlyerCanReach(Point from, Point to, int arriveRadius = 3)
    {
        if (NavGrid.IsBlock(from.X, from.Y))
            return true;
        var seen = new HashSet<Point> { from };
        var queue = new Queue<Point>();
        queue.Enqueue(from);
        int budget = FlyerBudget;
        while (queue.Count > 0)
        {
            Point p = queue.Dequeue();
            if (System.Math.Abs(p.X - to.X) <= arriveRadius && System.Math.Abs(p.Y - to.Y) <= arriveRadius)
                return true;
            if (--budget <= 0)
                return true;
            foreach (Point n in new[] { new Point(p.X + 1, p.Y), new Point(p.X - 1, p.Y), new Point(p.X, p.Y + 1), new Point(p.X, p.Y - 1) })
            {
                if (seen.Contains(n) || NavGrid.IsBlock(n.X, n.Y))
                    continue;
                seen.Add(n);
                queue.Enqueue(n);
            }
        }
        return false;
    }
}
