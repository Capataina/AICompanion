#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The movement entry point for the brain. It owns the navigator's retained route and the
/// state search a safety response may hold, so callers hand it a live body and an intent rather
/// than manipulating routes, searches or the terrain rules independently. Every caller supplies
/// intent here; only the motor applies what comes back.
/// </summary>
public sealed class CoordinateMovement
{
    public Navigator Navigator { get; } = new();
    private StateSeek? seek;
    private Vector2? unresolvedGoal;

    public bool StateSearchPending => seek is { Pending: true };
    public int StateSearchRetainedTicks => seek?.RetainedTicks ?? 0;

    public void CancelStateSearch()
    {
        seek = null;
        unresolvedGoal = null;
    }

    /// <summary>
    /// Find and follow a way to any nearby place the caller calls safe: a bounded flood over the
    /// corner graph from the body, stopping at the first corner the predicate accepts and steering
    /// along the way there. <paramref name="throughLiquid"/> lets the flood cross liquid tiles the
    /// body may not normally enter, which is the escape's case: the body is already in the water,
    /// and a flood that refuses wet corners would refuse the one it is standing on. The flood is
    /// kept across ticks while it is unfinished, so a large pocket is searched over several.
    /// </summary>
    public bool SeekState(OrbState live, Func<Vector2, bool> safe, Func<Vector2, float> heuristic,
        int workBudget, bool throughLiquid, out Controls controls, out bool pending)
    {
        Navigator.Interrupt(live, AttemptEnding.Preempted, "state-search");
        seek ??= new StateSeek(live, safe, heuristic, throughLiquid);
        bool chosen = seek.Continue(live, workBudget, out controls);
        pending = seek.Pending;
        if (!chosen && !pending) seek = null;
        return chosen;
    }

    public Controls MoveTo(OrbState live, Vector2 goal)
    {
        CancelStateSearch();
        return Navigator.MoveTo(live, goal);
    }

    /// <param name="preemptedBy">Null when the brain released the request itself; otherwise the owner
    /// that took the body (downing, recovery flight), so the interrupted attempt is scored as
    /// pre-empted rather than cancelled.</param>
    public Controls Hold(OrbState live, string? preemptedBy = null)
    {
        CancelStateSearch();
        Navigator.Interrupt(live, preemptedBy == null ? AttemptEnding.Cancelled : AttemptEnding.Preempted, preemptedBy ?? "released");
        return Controls.None;
    }

    /// <summary>A missing chosen place does not cancel a travel intention: aim at the anchor itself until <paramref name="arrived"/> says the body is there.</summary>
    public Controls SeekDestination(OrbState live, Vector2 anchor, Func<Vector2, bool> arrived)
    {
        seek = null;
        unresolvedGoal = anchor;
        if (arrived(live.Centre))
        {
            Navigator.Interrupt(live, AttemptEnding.Completed, "objective-satisfied");
            return Controls.None;
        }
        return Navigator.MoveTo(live, anchor);
    }

    public Controls AvoidThreats(OrbState live, Func<OrbState, int, bool> unsafeAtTick, Vector2 goal)
    {
        CancelStateSearch();
        return Navigator.AvoidThreats(live, unsafeAtTick, goal);
    }

    /// <summary>The terrain rules every search runs under this tick: which liquids are not walls.</summary>
    public void Configure(LiquidImmunity immunity) => OrbTerrain.Immunity = immunity;

    public void SetObstacles(IEnumerable<Rectangle> obstacles) => Navigator.Avoid = new List<Rectangle>(obstacles);

    /// <summary>A retained flood toward the nearest place a predicate accepts, and the route once one is found.</summary>
    private sealed class StateSeek
    {
        private readonly Func<Vector2, bool> safe;
        private readonly Func<Vector2, float> heuristic;
        private readonly bool throughLiquid;
        private FreeSpaceSearch? flood;
        private Route? route;
        public bool Pending { get; private set; }
        public int RetainedTicks { get; private set; }

        public StateSeek(OrbState live, Func<Vector2, bool> safe, Func<Vector2, float> heuristic, bool throughLiquid)
        {
            this.safe = safe;
            this.heuristic = heuristic;
            this.throughLiquid = throughLiquid;
        }

        public bool Continue(OrbState live, int workBudget, out Controls controls)
        {
            controls = Controls.None;
            RetainedTicks++;
            ITileWorld world = MovementQueries.World;
            if (route != null)
            {
                if (safe(live.Centre) && Vector2.Distance(live.Centre, route.Goal) <= Navigator.ArriveDistance)
                {
                    Pending = false;
                    return true;
                }
                if (!route.StillValid(world)) route = null;
            }
            if (route == null)
            {
                LiquidImmunity rules = throughLiquid ? new LiquidImmunity(true, true) : OrbTerrain.Immunity;
                if (flood == null || !flood.Valid)
                {
                    Point? start = CornerGraph.NearestUsable(world, live.Centre, 2, requireSweep: false, rules);
                    if (start == null) { Pending = false; return false; }
                    flood = new FreeSpaceSearch(world, start.Value, null, rules, priceClearance: false)
                    {
                        Accept = corner => safe(CornerGraph.ToWorld(corner)),
                        Prefer = corner => heuristic(CornerGraph.ToWorld(corner)),
                    };
                }
                bool finished = flood.Advance(workBudget);
                if (flood.FoundCorner is Point found)
                {
                    var corners = flood.PathTo(found);
                    var raw = new List<Vector2>(corners.Count + 1) { live.Centre };
                    foreach (Point corner in corners) raw.Add(CornerGraph.ToWorld(corner));
                    route = new Route(raw, 0, world.Revision, rules);
                    flood = null;
                }
                else if (finished)
                {
                    Pending = false;
                    return false;
                }
                else
                {
                    Pending = true;
                    return false;
                }
            }
            Pending = false;
            controls = SteerAlongRoute.Steer(live, route, OrbPace.MaxSpeed, OrbPace.Acceleration, out _);
            return true;
        }
    }
}
