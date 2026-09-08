#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.Navigation;

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
        NavPath? path = AStar.Find(start.Value, goal.Value, WalkerBudget, out int used);
        return path != null || used > WalkerBudget;
    }

    /// <summary>Flood fill through non-solid tiles from the flyer toward a box around the target.</summary>
    public static bool FlyerCanReach(Point from, Point to, int arriveRadius = 3)
    {
        if (NavGrid.IsSolid(from.X, from.Y))
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
                if (seen.Contains(n) || NavGrid.IsSolid(n.X, n.Y))
                    continue;
                seen.Add(n);
                queue.Enqueue(n);
            }
        }
        return false;
    }
}
