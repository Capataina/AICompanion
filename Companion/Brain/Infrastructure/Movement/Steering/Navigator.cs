#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>How an attempt to reach a place ended, scored apart so the record can tell a body that
/// got there from one that was told to stop, one that was taken by another owner, and one that failed.</summary>
public enum AttemptEnding { Completed, Failed, Preempted, Cancelled }

/// <summary>
/// Owns the route the body is following and the search that produced it. A request names a goal
/// point; the navigator plans a route to it over the free-space graph, smooths it, and steers the
/// body along it every tick, re-planning when the goal moves materially, when the world is edited
/// under the route, when the segment ahead is no longer clear, or when the body has stopped making
/// progress. There is no proof step between route and performance, because the body is
/// holonomic: any route with clearance is followable at some speed, and the steering's bend cap
/// is what makes that true in practice.
///
/// <para>A search too large for one tick keeps its frontier and continues next tick; while it is
/// unfinished the body keeps following the route it already had, steers straight at the goal when
/// the straight line is clear, and otherwise holds. A search that exhausts the free space without
/// finding the goal is a proven absence, reported as <see cref="ExecutionStatus.Unreachable"/>,
/// and the difference between that and unfinished is what every "not yet is not no" rule upstream
/// stands on.</para>
/// </summary>
public sealed class Navigator
{
    public enum ExecutionStatus { Idle, Arrived, Executable, Pending, Unreachable, Direct }

    /// <summary>How close to the goal counts as there, in pixels; positioning reserves this inside its regions.</summary>
    public const float ArriveDistance = 12f;

    /// <summary>The telemetry's plan dump, attached by the brain; the replay tool leaves it unset.
    /// Arguments: the body's tile, the goal's tile, the held goal's tile if any, the expansions spent, and why.</summary>
    public static Action<Point, Point, Point?, int, string>? PlanFailed;
    public static double PlanMsBudget { get; set; }

    public ExecutionStatus Status { get; private set; }
    public Route? Path { get; private set; }
    public Vector2? Goal { get; private set; }
    public Point? GoalTile => Goal is Vector2 goal ? MovementQueries.Tile(goal) : null;
    public Vector2 Lookahead { get; private set; }
    public bool Arrived { get; private set; }
    public bool LastPlanFailed { get; private set; }
    public bool LastPlanEmpty { get; private set; }
    public bool PlannedThisTick { get; private set; }
    public FreeSpaceSearch.StopReason LastSearchStop { get; private set; }
    public int LastExpansions { get; private set; }
    public double LastPlanMs { get; private set; }
    public long SearchId { get; private set; }
    public long AttemptId { get; private set; }
    public int SearchExpansions => search?.Expansions ?? 0;
    public bool SearchPending => search is { Finished: false };
    public string ProgressReason { get; private set; } = "idle";
    public int StuckTicks { get; private set; }
    public int StuckStrikes { get; private set; }
    public AttemptEnding? LastEnding { get; private set; }
    public int CompletedAttempts { get; private set; }
    public int FailedAttempts { get; private set; }
    public int PreemptedAttempts { get; private set; }
    public int CancelledAttempts { get; private set; }
    /// <summary>The predicted-collision predicate the reflexes supplied this tick, or null when nothing threatens.</summary>
    public Func<OrbState, int, bool>? UnsafeAtTick { get; set; }
    /// <summary>Threat bodies a route should go around; set per tick by the coordinator.</summary>
    public IReadOnlyList<Rectangle> Avoid { get; set; } = Array.Empty<Rectangle>();

    public float RemainingEstimatedRouteTicks => Path is { } route && lastCentre is Vector2 c
        ? route.RemainingLength(c) / MathF.Max(0.1f, OrbPace.MaxSpeed) : 0f;
    public int RemainingRouteSteps => Path is { } route ? Math.Max(0, route.Count - 1 - route.Index) : 0;

    private FreeSpaceSearch? search;
    private Vector2? lastCentre;
    private Vector2 progressOrigin;
    private int progressTicks;
    private bool attemptOpen;

    /// <summary>Steer toward a goal, planning or re-planning as needed.</summary>
    public Controls MoveTo(OrbState live, Vector2 goal)
    {
        PlannedThisTick = false;
        lastCentre = live.Centre;
        ITileWorld world = MovementQueries.World;
        bool newGoal = Goal is not Vector2 held || Vector2.DistanceSquared(held, goal) > Weights.ReplanGoalPixels * Weights.ReplanGoalPixels;
        if (newGoal)
        {
            if (attemptOpen && Goal != null) EndAttempt(AttemptEnding.Cancelled);
            Goal = goal;
            Path = null;
            search = null;
            Arrived = false;
            StuckTicks = 0;
            progressOrigin = live.Centre;
            progressTicks = 0;
            attemptOpen = true;
            AttemptId++;
        }
        else if (Goal is Vector2 g && g != goal)
        {
            // A goal that drifted a little keeps its route; the last segment is re-aimed at it.
            Goal = goal;
            if (Path != null) Path.Points[^1] = goal;
        }

        float distanceToGoal = Vector2.Distance(live.Centre, goal);
        if (distanceToGoal <= ArriveDistance)
        {
            if (!Arrived)
            {
                Arrived = true;
                EndAttempt(AttemptEnding.Completed);
                BehaviourCensus.RequestReached();
            }
            Status = ExecutionStatus.Arrived;
            ProgressReason = "arrived";
            Path = null;
            search = null;
            return Controls.None;
        }
        Arrived = false;

        // Is the route still the route? Edited under, immunity changed, or the segment ahead blocked.
        if (Path != null && (!Path.StillValid(world) || !SegmentAheadClear(world, live, Path)))
        {
            Path = null;
            search = null;
            BehaviourCensus.Planned(replan: true);
        }
        if (Path == null)
            Plan(live, goal, world);

        Controls controls;
        if (Path != null)
        {
            Status = search is { Finished: false } ? ExecutionStatus.Pending : ExecutionStatus.Executable;
            controls = SteerAlongRoute.Steer(live, Path, OrbPace.MaxSpeed, OrbPace.Acceleration, out Vector2 ahead);
            Lookahead = ahead;
            ProgressReason = Status == ExecutionStatus.Pending ? "following-while-replanning" : "following";
        }
        else if (CircleContact.SweptClear(world, live.Centre, goal, OrbTerrain.Wall))
        {
            // No route yet and a clear straight line: go, braking for the goal.
            Status = search is { Finished: false } ? ExecutionStatus.Pending : ExecutionStatus.Direct;
            Vector2 direction = (goal - live.Centre) / distanceToGoal;
            float brake = MathF.Sqrt(2f * OrbPace.Acceleration * MathF.Max(0f, distanceToGoal - ArriveDistance * 0.5f));
            controls = new Controls(direction * MathF.Min(OrbPace.MaxSpeed, brake));
            Lookahead = goal;
            ProgressReason = "direct";
        }
        else
        {
            Status = search is { Finished: false } ? ExecutionStatus.Pending : ExecutionStatus.Unreachable;
            controls = Controls.None;
            Lookahead = live.Centre;
            ProgressReason = Status == ExecutionStatus.Pending ? "waiting-for-route" : "no-route";
        }
        WatchProgress(live);
        return controls;
    }

    private void Plan(OrbState live, Vector2 goal, ITileWorld world)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        PlannedThisTick = true;
        LastPlanFailed = LastPlanEmpty = false;
        if (search == null || !search.Valid)
        {
            Point? start = CornerGraph.NearestUsable(world, live.Centre, 2);
            Point? end = CornerGraph.NearestUsable(world, goal, 2, requireSweep: false);
            if (start == null || end == null)
            {
                // Inside something, in a liquid that is a wall, or aiming at one: nothing to plan over.
                // The direct steer or a hold answers this tick, and next tick asks again.
                LastPlanFailed = true;
                LastSearchStop = FreeSpaceSearch.StopReason.Exhausted;
                LastExpansions = 0;
                LastPlanMs = clock.Elapsed.TotalMilliseconds;
                search = null;
                if (start == null) ProgressReason = "no-corner-under-body";
                return;
            }
            search = new FreeSpaceSearch(world, start.Value, end.Value) { Avoid = Avoid };
            SearchId++;
            BehaviourCensus.Planned(replan: false);
        }
        bool finished = search.Advance(Weights.RouteSearchExpansions, PlanMsBudget);
        LastSearchStop = search.Stop;
        LastExpansions = search.Expansions;
        if (finished && search.Stop == FreeSpaceSearch.StopReason.Found)
        {
            var corners = search.RouteCorners()!;
            var raw = new List<Vector2>(corners.Count + 2) { live.Centre };
            foreach (Point corner in corners) raw.Add(CornerGraph.ToWorld(corner));
            // The goal itself is the last point when the body can slide to it from the last corner;
            // otherwise the route ends at the corner and the goal drifts within arrival distance.
            if (CircleContact.SweptClear(world, raw[^1], goal, OrbTerrain.Wall)) raw.Add(goal);
            Path = new Route(Route.Smooth(world, raw), SearchId, world.Revision, OrbTerrain.Immunity);
            search = null;
        }
        else if (finished)
        {
            LastPlanEmpty = true;
            LastPlanFailed = true;
            BehaviourCensus.PlanFailed();
            PlanFailed?.Invoke(live.Tile, MovementQueries.Tile(goal), null, search.Expansions, search.Stop.ToString());
            search = null;
        }
        else
        {
            BehaviourCensus.PlanPending();
        }
        LastPlanMs = clock.Elapsed.TotalMilliseconds;
    }

    private static bool SegmentAheadClear(ITileWorld world, OrbState live, Route route)
    {
        int i = Math.Clamp(route.Index, 0, route.Points.Count - 2);
        return CircleContact.SweptClear(world, live.Centre, route.Points[i + 1], OrbTerrain.Wall);
    }

    /// <summary>
    /// Net displacement over a window catches a body pinned against a wall the sweep cannot see
    /// or a route the steering cannot keep; two strikes on one goal hand the goal back to the
    /// positioner, which bans the spot for a while.
    /// </summary>
    private void WatchProgress(OrbState live)
    {
        if (++progressTicks < Weights.ObjectiveProgressWindowTicks) return;
        bool moved = Vector2.DistanceSquared(progressOrigin, live.Centre) >= Weights.ObjectiveProgressPixels * Weights.ObjectiveProgressPixels;
        progressOrigin = live.Centre;
        progressTicks = 0;
        if (moved) { StuckTicks = 0; return; }
        StuckTicks += Weights.ObjectiveProgressWindowTicks;
        StuckStrikes++;
        ProgressReason = "no-progress";
        Path = null;
        search = null;
    }

    public void ResetStrikes() => StuckStrikes = 0;

    /// <summary>
    /// Choose a velocity that keeps the body out of predicted collisions for the next few ticks:
    /// each of eight headings and a stop is run forward through the contact, and the one that
    /// stays safe longest wins, ties broken by progress toward the goal. Off the line, for a thing
    /// that flies, is usually up or down rather than back.
    /// </summary>
    public Controls AvoidThreats(OrbState live, Func<OrbState, int, bool> unsafeAtTick, Vector2 goal)
    {
        Interrupt(live, AttemptEnding.Preempted, "avoid-threats");
        ITileWorld world = MovementQueries.World;
        float speed = OrbPace.MaxSpeed;
        Vector2 bestVelocity = Vector2.Zero;
        int bestSafeTicks = -1;
        float bestProgress = float.NegativeInfinity;
        var candidates = new List<Vector2> { Vector2.Zero };
        for (int i = 0; i < 8; i++)
        {
            float angle = i * MathF.PI / 4f;
            candidates.Add(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed);
        }
        foreach (Vector2 candidate in candidates)
        {
            Vector2 centre = live.Centre, velocity = live.Velocity;
            int safeTicks = Weights.DodgeLookaheadTicks;
            for (int tick = 1; tick <= Weights.DodgeLookaheadTicks; tick++)
            {
                Vector2 change = candidate - velocity;
                if (change.LengthSquared() > OrbPace.Acceleration * OrbPace.Acceleration) change = Vector2.Normalize(change) * OrbPace.Acceleration;
                velocity += change;
                Vector2 next = centre + velocity;
                CircleContact.Resolve(world, ref next, ref velocity);
                centre = next;
                if (unsafeAtTick(new OrbState(centre, velocity), tick)) { safeTicks = tick - 1; break; }
            }
            float progress = -Vector2.Distance(centre, goal);
            if (safeTicks > bestSafeTicks || (safeTicks == bestSafeTicks && progress > bestProgress))
            {
                bestSafeTicks = safeTicks;
                bestProgress = progress;
                bestVelocity = candidate;
            }
        }
        Status = ExecutionStatus.Direct;
        ProgressReason = "avoiding";
        return new Controls(bestVelocity);
    }

    /// <summary>End the held goal and its route, scoring the attempt by who ended it.</summary>
    public void Interrupt(OrbState live, AttemptEnding ending = AttemptEnding.Cancelled, string cause = "released")
    {
        if (Goal == null && Path == null && search == null) return;
        if (attemptOpen && !Arrived) EndAttempt(ending);
        Goal = null;
        Path = null;
        search = null;
        Arrived = false;
        Status = ExecutionStatus.Idle;
        ProgressReason = cause;
    }

    private void EndAttempt(AttemptEnding ending)
    {
        if (!attemptOpen) return;
        attemptOpen = false;
        LastEnding = ending;
        switch (ending)
        {
            case AttemptEnding.Completed: CompletedAttempts++; break;
            case AttemptEnding.Failed: FailedAttempts++; break;
            case AttemptEnding.Preempted: PreemptedAttempts++; break;
            default: CancelledAttempts++; break;
        }
    }

    public void Clear()
    {
        Goal = null; Path = null; search = null; Arrived = false; attemptOpen = false;
        Status = ExecutionStatus.Idle; ProgressReason = "idle"; StuckStrikes = 0; StuckTicks = 0;
    }
}
