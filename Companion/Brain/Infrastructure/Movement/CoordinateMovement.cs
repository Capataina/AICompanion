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
        // A hold's anchor does not survive another owner moving the body: the escape takes the body out of the pool the
        // hold began in, and a hover that kept the anchor would float it straight back in the moment the escape let go.
        holdAnchor = null;
        seek ??= new StateSeek(live, safe, heuristic, throughLiquid);
        bool chosen = seek.Continue(live, workBudget, out controls);
        pending = seek.Pending;
        if (!chosen && !pending) seek = null;
        return chosen;
    }

    public Controls MoveTo(OrbState live, Vector2 goal)
    {
        CancelStateSearch();
        holdAnchor = null;
        return Navigator.MoveTo(live, goal);
    }

    /// <summary>
    /// Stop wanting anything, and ask the motor for nothing. This is for owners that take the body away from
    /// the brain — downing, recovery flight — and for nothing the brain chooses: a brain that holds hovers
    /// (<see cref="HoverHere"/>), because the orb is never strictly standing still.
    /// </summary>
    /// <param name="preemptedBy">Null when the brain released the request itself; otherwise the owner
    /// that took the body (downing, recovery flight), so the interrupted attempt is scored as
    /// pre-empted rather than cancelled.</param>
    public Controls Hold(OrbState live, string? preemptedBy = null)
    {
        CancelStateSearch();
        holdAnchor = null;
        Navigator.Interrupt(live, preemptedBy == null ? AttemptEnding.Cancelled : AttemptEnding.Preempted, preemptedBy ?? "released");
        return Controls.None;
    }

    /// <summary>
    /// The brain's hold: release whatever route was held and drift around the place the hold began. The
    /// place is taken once, when the hold starts, so a body still carrying momentum glides back to where it
    /// was told to stay rather than anchoring wherever the momentum has taken it by now.
    /// </summary>
    public Controls HoverHere(OrbState live)
    {
        CancelStateSearch();
        Navigator.Interrupt(live, AttemptEnding.Cancelled, "released");
        holdAnchor ??= live.Centre;
        return Navigator.Hover.Around(live, holdAnchor.Value, MovementQueries.World);
    }

    /// <summary>A missing chosen place does not cancel a travel intention: aim at the anchor itself until <paramref name="arrived"/> says the body is there, and hover once it is.</summary>
    public Controls SeekDestination(OrbState live, Vector2 anchor, Func<Vector2, bool> arrived)
    {
        seek = null;
        unresolvedGoal = anchor;
        if (arrived(live.Centre))
        {
            Navigator.Interrupt(live, AttemptEnding.Completed, "objective-satisfied");
            // Anchored once, where the objective was first met: an anchor taken from the body every tick moves with the
            // body, and the drift around it becomes a slow wander away from the place it arrived.
            holdAnchor ??= live.Centre;
            return Navigator.Hover.Around(live, holdAnchor.Value, MovementQueries.World);
        }
        holdAnchor = null;
        return Navigator.MoveTo(live, anchor);
    }

    /// <summary>
    /// Safety on top of the job: the tick's controls, bent away from a predicted hit when following them would
    /// meet one. <paramref name="bent"/> says whether they were, so the record can name the tick.
    /// </summary>
    public Controls Evade(OrbState live, Controls wanted, Func<OrbState, int, bool>? unsafeAtTick, out bool bent)
    {
        bent = false;
        if (unsafeAtTick == null) return wanted;
        return EvadeWhileMoving.Bend(live, wanted, unsafeAtTick, MovementQueries.World, out bent);
    }

    private Vector2? holdAnchor;

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
            controls = SteerAlongRoute.Steer(live, route, OrbPace.MaxSpeed, OrbPace.SpeedChange, out _);
            return true;
        }
    }
}
