#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.SharedMovementSystem;

/// <summary>
/// Can a thing get from here to there? Walkers ask the same A* the companion uses.
/// Flyers ask a flood fill over connected air. Phasers are always reachable.
///
/// The walker's answer is three-valued, and that is the whole design of this file rather
/// than a nicety. A bounded search that runs out of budget, and a start or goal with no
/// standable tile near it, are not evidence of a route and not evidence against one — they
/// are the search declining to answer. Collapsing that into a bool forces one conservatism
/// on every caller, and the callers want opposite ones: a threat wrongly ignored costs a
    /// hit the player takes, so the threat sense reads unknown as reachable, while a refuge
/// wrongly believed reachable costs the companion its life, so self-rescue reads unknown
/// as no. Both readings are correct and neither is the default; <see cref="WalkerReach"/>
/// reports what the search actually established and the caller decides what to do with it.
/// </summary>
public static class Reachability
{
    public const int WalkerBudget = 400;
    public const int FlyerBudget = 1500;

    /// <summary>What a bounded search established: a route, no route, or that it could not tell.</summary>
    public enum Reach
    {
        /// <summary>A whole path exists.</summary>
        Yes,
        /// <summary>The search exhausted the region it could get to and never arrived.</summary>
        No,
        /// <summary>The budget ran out, or one end has no standable tile: nothing was established either way.</summary>
        Unknown,
    }

    /// <summary>
    /// What the walker search established between two feet tiles, unrounded.
    /// <paramref name="asAnyCreature"/> asks the enemy question — can a body that takes any drop
    /// get from here to there — and the default asks the companion's own question under whatever
    /// one-way rule its planner is currently working to. The distinction is the whole point of the
    /// parameter: this search forced one-way drops on for every caller because it was written for
    /// threat observation, so mining and chopping certified approaches that require a drop the
    /// route planner then refuses to plan, and the job waited for a route that could not exist.
    /// A certificate issued under different rules from the execution is not a certificate.
    /// </summary>
    public static Reach WalkerReach(Point fromFeet, Point toFeet, bool asAnyCreature = false)
    {
        Point? start = NavGrid.NearestStandable(fromFeet, 2);
        Point? goal = NavGrid.NearestStandable(toFeet, 2);
        if (start == null || goal == null)
            return Reach.Unknown;
        // The brain sets the one-way rule for the companion's own plans at the end of its tick and
        // it is still set when the senses run at the start of the next, so the enemy question
        // forces it on around this search and puts it back after; left alone, a hunt read a zombie
        // that reaches the player by dropping into a cave as unreachable.
        bool oneWay = AStar.AllowOneWayDrops;
        if (asAnyCreature) AStar.AllowOneWayDrops = true;
        NavPath? path;
        int used;
        AStar.SearchStopReason stop;
        try
        {
            path = AStar.Find(start.Value, goal.Value, WalkerBudget, out used, out stop);
        }
        finally
        {
            AStar.AllowOneWayDrops = oneWay;
        }
        // A partial path is the search saying it could get closer, not that it arrived, so only a
        // whole path is a yes. A budget that ran out is the search declining to answer and is
        // reported as such rather than folded into either verdict.
        if (path != null && !path.Partial)
            return Reach.Yes;
        return stop is AStar.SearchStopReason.ExpansionBudget or AStar.SearchStopReason.Deadline ? Reach.Unknown : Reach.No;
    }

    /// <summary>
    /// The threat sense's reading: unknown counts as reachable, because a threat wrongly ignored
    /// costs the player a hit and one wrongly feared costs a little caution. Work selection reads
    /// the three-valued result directly: an unfinished approach yields without discarding its job.
    /// </summary>
    public static bool WalkerCanReach(Point fromFeet, Point toFeet) => WalkerReach(fromFeet, toFeet, asAnyCreature: true) != Reach.No;

    /// <summary>
    /// Self-rescue's reading: only a route the search actually found counts. A drowning companion
    /// that treats an unknown as a promise walks at a refuge it cannot arrive at, holds it (a held
    /// refuge is dropped only when it stops being a refuge, never when the navigator fails to
    /// reach it), and stands with a finished path and an empty breath bar, which is how it died at
    /// tick 20,679 of the 2026-09-09 underground session. With no proven refuge the action asks
    /// for nothing and treads water instead, which is a worse plan and a survivable one.
    /// </summary>
    public static bool WalkerProvenReach(Point fromFeet, Point toFeet) => WalkerReach(fromFeet, toFeet) == Reach.Yes;

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
