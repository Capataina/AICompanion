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
        return Verdict(path, stop);
    }

    /// <summary>
    /// What one bounded search established. A partial path is the search saying it could get
    /// closer, not that it arrived, so only a whole path is a yes. A budget or deadline that ran out
    /// is the search declining to answer and is reported as such rather than folded into either
    /// verdict; only an exhausted region is a no.
    /// </summary>
    private static Reach Verdict(NavPath? path, AStar.SearchStopReason stop)
        => path is { Partial: false } ? Reach.Yes
        : stop is AStar.SearchStopReason.ExpansionBudget or AStar.SearchStopReason.Deadline ? Reach.Unknown : Reach.No;

    /// <summary>
    /// The breath a round trip may spend, in ticks: how long until drowning damage from now, the
    /// most the bar holds in the same unit, and how many of those ticks one tick with the head clear
    /// gives back. They are the character's own breathing rule expressed as integers and passed in,
    /// because the movement core compiles without the game. For the companion that is
    /// <c>CompanionBreath.TicksLeft</c>, <c>BreathMax × BreathCDMax</c> and
    /// <c>RecoverPerTick × BreathCDMax</c>; a mismatch between these and that rule makes every
    /// verdict below wrong in the same direction, so they belong at the call site next to the breath
    /// they read.
    /// </summary>
    public readonly record struct BreathEnvelope(int TicksLeft, int CapacityTicks, int RecoveryPerDryTick);

    /// <summary>
    /// Going there and coming back, kept as separate answers. <see cref="Breath"/> is unknown
    /// unless both directions are a whole route, because breath cannot be judged over a trip nobody
    /// has found. The tick counts are the routes' proven step durations, so they are estimates of
    /// travel time and not measurements.
    /// </summary>
    public readonly record struct RoundTripEvidence(Reach Outward, Reach Return, int OutwardTicks, int ReturnTicks,
        int OutwardSubmergedTicks, int ReturnSubmergedTicks, int LowestBreathTicks, Reach Breath);

    /// <summary>
    /// Whether the body can go from <paramref name="fromFeet"/> to <paramref name="toFeet"/> and get
    /// back, and whether its breath lasts the trip. Reaching a place is not a certificate of leaving
    /// it: an outward route may take a drop the return cannot climb, so the return is asked of its
    /// own search, and a return search that runs out of budget is unknown, never a return. Both
    /// searches take any drop, because it is the return search, not a one-way filter, that judges
    /// whether a drop was one-way.
    ///
    /// Breath is walked step by step over the outward route and then the return: a step whose head
    /// is under water at either end drains its duration, and any other step restores its duration
    /// times the recovery rate, capped at capacity. The error has a direction, which is the reason
    /// for that rule. It over-charges a step that surfaces part way and water that does not drown
    /// (honey and shimmer read as water here), and it misses an arc that dips under water between
    /// two dry endpoints. It includes no time spent at the destination, so a caller that means to
    /// work there adds that work to the outward leg itself.
    /// </summary>
    public static RoundTripEvidence RoundTrip(Point fromFeet, Point toFeet, BreathEnvelope breath, int budget = WalkerBudget)
    {
        Point? start = NavGrid.NearestStandable(fromFeet, 2);
        Point? goal = NavGrid.NearestStandable(toFeet, 2);
        if (start == null || goal == null)
            return new RoundTripEvidence(Reach.Unknown, Reach.Unknown, 0, 0, 0, 0, breath.TicksLeft, Reach.Unknown);
        bool oneWay = AStar.AllowOneWayDrops;
        double allowance = AStar.MsBudget;
        AStar.AllowOneWayDrops = true;
        // The per-search millisecond allowance is static, and the navigator's plan sets it to its own
        // planning allowance and leaves it there, so a round trip asked afterwards inherited whatever that
        // was: after a plan starved to one work unit it stopped each leg after its first node and answered
        // unknown both ways for a pit it can walk. Each leg is bounded here by its expansion budget and by
        // the shared planning deadline the caller runs under, which a caller narrows with
        // LimitPlanningWork.Narrow, and the caller's allowance is put back afterwards.
        AStar.MsBudget = 0;
        NavPath? outward, back;
        AStar.SearchStopReason outwardStop, backStop;
        try
        {
            outward = AStar.Find(start.Value, goal.Value, budget, out _, out outwardStop);
            back = AStar.Find(goal.Value, start.Value, budget, out _, out backStop);
        }
        finally
        {
            AStar.AllowOneWayDrops = oneWay;
            AStar.MsBudget = allowance;
        }
        Reach there = Verdict(outward, outwardStop), home = Verdict(back, backStop);
        int outwardTicks = DurationOf(outward), returnTicks = DurationOf(back);
        if (there != Reach.Yes || home != Reach.Yes)
            return new RoundTripEvidence(there, home, outwardTicks, returnTicks, 0, 0, breath.TicksLeft, Reach.Unknown);

        int left = breath.TicksLeft, lowest = left;
        int SpendOver(NavPath leg)
        {
            int submerged = 0;
            foreach (NavStep step in leg.Steps)
            {
                int ticks = System.Math.Max(1, step.Ticks);
                if (NavGrid.HeadSubmergedAt(step.From.X, step.From.Y) || NavGrid.HeadSubmergedAt(step.Tile.X, step.Tile.Y))
                {
                    left -= ticks;
                    submerged += ticks;
                    lowest = System.Math.Min(lowest, left);
                }
                else
                    left = System.Math.Min(breath.CapacityTicks, left + ticks * breath.RecoveryPerDryTick);
            }
            return submerged;
        }
        int outwardSubmerged = SpendOver(outward!);
        int returnSubmerged = SpendOver(back!);
        return new RoundTripEvidence(there, home, outwardTicks, returnTicks, outwardSubmerged, returnSubmerged, lowest,
            lowest >= 0 ? Reach.Yes : Reach.No);
    }

    private static int DurationOf(NavPath? path)
    {
        if (path == null) return 0;
        int ticks = 0;
        foreach (NavStep step in path.Steps) ticks += System.Math.Max(1, step.Ticks);
        return ticks;
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
