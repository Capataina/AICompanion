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
/// <para>A search too large for one tick keeps its frontier and continues next tick, and while it is
/// unfinished the body flies a partial route to the nearest place the search has already reached,
/// unless the straight line to the goal is clear, when it steers straight. A search that stops at its
/// node limit leaves that partial route behind it. Hovering in place until the search came back was
/// the first shape, and in the second play of 0.27.0 it held the orb in open air for two seconds at a
/// time while the search ran; a partial route is how Recast/Detour's path query answers the same case.</para>
///
/// <para>A search that exhausts the free space without finding the goal is a proven absence, reported
/// as <see cref="ExecutionStatus.Unreachable"/>, and the difference between that and unfinished is what
/// every "not yet is not no" rule upstream stands on. A proven absence is not flown toward: the body
/// hovers where it waited and the goal is published as <see cref="GoalProvenUnreachable"/>, because another place is the
/// positioner's answer. A search that ended without a route is kept for the goal it answered and is
/// not asked again while the goal and the terrain it read stand — a proven absence while the body stays
/// inside what it explored, a node limit until the partial route has brought the body materially
/// nearer — because asking the same question every tick was the other half of that stall.</para>
/// </summary>
public sealed class Navigator
{
    public enum ExecutionStatus { Idle, Arrived, Executable, Pending, Unreachable, Direct }

    // Profiler sections: planning a route, and turning the route into this tick's controls. Inert in NavReplay, where
    // nothing claims a recording thread.
    private static readonly int RouteSearchSection = Diagnostics.BrainSections.Register("route-search");
    private static readonly int SteerSection = Diagnostics.BrainSections.Register("steer");

    /// <summary>How close to the goal counts as having arrived, in pixels.</summary>
    public const float ArriveDistance = 12f;

    /// <summary>How far from its goal an arrived body may drift before it has left: the arrival radius plus
    /// the hover's reach and a margin for the momentum the drift carries, so hovering around a spot never
    /// undoes the arrival and restarts the attempt every orbit. Positioning reserves this inside its regions,
    /// so a body drifting around a spot on a region's boundary is still inside the region.</summary>
    public const float SettleRadius = ArriveDistance + Weights.HoverRadiusPixels + 4f;

    /// <summary>How close to the body a route corner must be to join the route there rather than at its start, in pixels. A
    /// route found after the body has flown a partial route begins where the search began, and joining at its first corner
    /// would fly the body back; four tiles is where the swept test per corner stays cheap.</summary>
    private const float JoinPixels = 64f;

    /// <summary>The drift every reached or held spot gets. One instance, so arriving and holding share one
    /// wander and a hold that follows an arrival carries on circling rather than starting over.</summary>
    public HoverAroundSpot Hover { get; } = new();

    /// <summary>The telemetry's plan dump, attached by the brain; the replay tool leaves it unset.
    /// Arguments: the body's tile, the goal's tile, the held goal's tile if any, the expansions spent, and why.</summary>
    public static Action<Point, Point, Point?, int, string>? PlanFailed;
    public static double PlanMsBudget { get; set; }

    public ExecutionStatus Status { get; private set; }
    public Route? Path { get; private set; }
    /// <summary>Whether <see cref="Path"/> ends short of the goal: at the nearest place a search has reached, or at the free
    /// corner nearest a goal inside rock.</summary>
    public bool PathIsPartial { get; private set; }
    public Vector2? Goal { get; private set; }
    public Point? GoalTile => Goal is Vector2 goal ? MovementQueries.Tile(goal) : null;
    public Vector2 Lookahead { get; private set; }

    /// <summary>What produced the controls <see cref="MoveTo"/> last returned, so the evade layer can forecast the same steering.</summary>
    public enum Steering { Hover, Route, Direct }

    /// <summary>The branch <see cref="MoveTo"/> steered by on its last call: a route, a direct line to the goal, or a hover.</summary>
    public Steering LastSteering { get; private set; }
    public bool Arrived { get; private set; }
    public bool LastPlanFailed { get; private set; }
    public bool LastPlanEmpty { get; private set; }
    public bool PlannedThisTick { get; private set; }
    public FreeSpaceSearch.StopReason LastSearchStop { get; private set; }
    public int LastExpansions { get; private set; }
    // Wall-clock spent planning since the recorder last asked, accumulated over every plan call in between.
    private double planMsUnread;

    /// <summary>
    /// What planning has cost since the last call, and zero when nothing planned; the recorder calls it once per row, so a
    /// row carries its own tick's cost. It replaced a last-plan figure that was repeated on every row until the next plan,
    /// so a sum over a stretch of rows counted one plan once per row and a tick that planned nothing looked like one that did.
    /// </summary>
    public double TakePlanMs()
    {
        double spent = planMsUnread;
        planMsUnread = 0;
        return spent;
    }
    public long SearchId { get; private set; }
    public long AttemptId { get; private set; }
    public int SearchExpansions => search?.Expansions ?? 0;
    public bool SearchPending => search is { Finished: false };
    public string ProgressReason { get; private set; } = "idle";
    public int StuckTicks { get; private set; }
    public int StuckStrikes { get; private set; }
    /// <summary>Whether the held goal's last search exhausted the free space without reaching it, and that answer still stands:
    /// the goal is proven unreachable from where the body is, so a spot the positioner chose is the positioner's to replace.</summary>
    public bool GoalProvenUnreachable => spent is { Stop: FreeSpaceSearch.StopReason.Exhausted };
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
    // The last search that finished without a route, kept for the goal it answered, and how far the body was from the
    // goal when that search began, which is what a node-limited search must beat before it is asked again.
    private FreeSpaceSearch? spent;
    private float spentFromDistance, searchFromDistance;
    // The end of a completed route to the free corner nearest a goal the body cannot slide into: the body is as close as it
    // can get, and planning it again from there finds the same corner.
    private Vector2? settledShort;
    // The goal each kept conclusion was reached about. A conclusion is forgotten once the goal is further than the replan
    // distance from this, never from last tick's goal: the drift branch re-aims the held goal a little every tick, so a goal
    // compared tick to tick never moves far enough to count, and a conclusion kept that way outlived any distance of drift.
    private Vector2 spentGoal, settledShortGoal;
    // Whether the route being flown ends where a finished search chose to end it — the free corner beside a goal the body
    // cannot slide into — as opposed to a partial route lent by a search that was still running or stopped at its node limit.
    // Only the first is an answer about the goal. It used to be inferred from `spent == null`, which a tile edit invalidating
    // a node-limit answer also produces, and that parked the body short of a goal it had never finished searching for.
    private bool routeEndsWhereSearchChose;
    private Vector2? lastCentre;
    private Vector2 progressOrigin;
    private int progressTicks;
    private bool attemptOpen;
    // Where a body with no route and no clear line began waiting, so its drift circles one place instead of following itself.
    private Vector2? waitAnchor;

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
            DropRoute();
            // A wait anchor belongs to the goal that could not be reached, and dies with it.
            waitAnchor = null;
            StuckTicks = 0;
            progressOrigin = live.Centre;
            progressTicks = 0;
            attemptOpen = true;
            AttemptId++;
        }
        else if (Goal is Vector2 g && g != goal)
        {
            // A goal that drifted a little keeps its route; the last segment is re-aimed at it, unless the route never
            // reached the goal, when its end is a place of its own.
            Goal = goal;
            if (Path != null && !PathIsPartial) Path.Points[^1] = goal;
        }

        float distanceToGoal = Vector2.Distance(live.Centre, goal);
        // Arriving is reaching the arrival radius; staying arrived is staying inside the settle radius,
        // which is wider by the hover's reach. Inside it the body drifts around the goal rather than
        // stopping on it, because the orb is never strictly standing still.
        if (distanceToGoal <= (Arrived ? SettleRadius : ArriveDistance))
        {
            if (!Arrived)
            {
                Arrived = true;
                EndAttempt(AttemptEnding.Completed);
                BehaviourCensus.RequestReached();
            }
            Status = ExecutionStatus.Arrived;
            ProgressReason = "arrived";
            DropRoute();
            LastSteering = Steering.Hover;
            return Hover.Around(live, goal, world);
        }
        Arrived = false;

        // Is the route still the route? Edited under, or the segment ahead blocked.
        if (Path != null && (!Path.StillValid(world) || !SegmentAheadClear(world, live, Path)))
        {
            Path = null;
            PathIsPartial = false;
            // A partial route is lent by a search that may still be running; only the route died, not the question.
            if (search is not { Finished: false }) search = null;
            BehaviourCensus.Planned(replan: true);
        }
        // A kept answer stops standing for the goal once the world changed where it read, or, for a proven absence, once
        // the body is somewhere that search never explored — carried across a wall, the question is a new one.
        // Either kind is also forgotten once the goal has moved past the replan distance from the goal it was about.
        float forget = Weights.ReplanGoalPixels * Weights.ReplanGoalPixels;
        if (spent != null && (!spent.Valid || Vector2.DistanceSquared(goal, spentGoal) > forget
            || (spent.Stop == FreeSpaceSearch.StopReason.Exhausted && CornerGraph.NearestUsable(world, live.Centre, 2) is Point here && !spent.Reached.Contains(here))))
            spent = null;
        if (settledShort is Vector2 shortEnd && (Vector2.Distance(live.Centre, shortEnd) > SettleRadius || Vector2.DistanceSquared(goal, settledShortGoal) > forget))
            settledShort = null;

        if (Path == null || search is { Finished: false })
            using (Diagnostics.BrainSections.Enter(RouteSearchSection)) Plan(live, goal, world);

        using var steering = Diagnostics.BrainSections.Enter(SteerSection);
        Controls controls;
        if (Path != null && PathIsPartial && Vector2.Distance(live.Centre, Path.Goal) <= ArriveDistance)
        {
            // At the nearest place known: drift there while a search still runs, and once none does, let the next tick
            // decide from here whether asking again has earned itself.
            Vector2 end = Path.Goal;
            Status = search is { Finished: false } ? ExecutionStatus.Pending : ExecutionStatus.Unreachable;
            controls = Hover.Around(live, end, world);
            LastSteering = Steering.Hover;
            Lookahead = end;
            ProgressReason = "at-nearest-known-place";
            if (search is not { Finished: false })
            {
                if (routeEndsWhereSearchChose)
                {
                    settledShort = end;
                    settledShortGoal = goal;
                }
                Path = null;
                PathIsPartial = false;
                routeEndsWhereSearchChose = false;
                waitAnchor = end;
            }
        }
        else if (Path != null)
        {
            Status = search is { Finished: false } ? ExecutionStatus.Pending
                : PathIsPartial ? ExecutionStatus.Unreachable : ExecutionStatus.Executable;
            controls = SteerAlongRoute.Steer(live, Path, OrbPace.MaxSpeed, OrbPace.SpeedChange, out Vector2 ahead);
            LastSteering = Steering.Route;
            Lookahead = ahead;
            ProgressReason = PathIsPartial ? "following-partial-route" : Status == ExecutionStatus.Pending ? "following-while-replanning" : "following";
        }
        else if (CircleContact.SweptClear(world, live.Centre, goal, OrbTerrain.Wall))
        {
            // No route yet and a clear straight line: go, easing down into the goal.
            Status = search is { Finished: false } ? ExecutionStatus.Pending : ExecutionStatus.Direct;
            Vector2 direction = (goal - live.Centre) / distanceToGoal;
            controls = new Controls(direction * OrbPace.ArrivalSpeed(distanceToGoal));
            LastSteering = Steering.Direct;
            Lookahead = goal;
            ProgressReason = "direct";
        }
        else
        {
            Status = search is { Finished: false } ? ExecutionStatus.Pending : ExecutionStatus.Unreachable;
            // Nothing to fly along and no clear line to the goal: drift around where this began rather than ask for
            // nothing, because the orb is never strictly standing still. Asking for nothing here parked the body for as
            // long as a follow anchor stayed unplannable — measured in VerifySafetyIsALayerOnTheJob's enemy-beside row, fourteen
            // ticks at zero with the player walking away — and the stuck strikes and the positioner's bans, not a stop,
            // are what find another place.
            // An anchor the body has no clear line to is not a place to drift around: hovering at it pins the body against
            // whatever lies between. The first build kept a wait anchor across an interrupted wait, and the next unreachable
            // goal pulled the body 230 px back to it and held it at zero against a wall.
            if (waitAnchor is Vector2 w && !CircleContact.SweptClear(world, live.Centre, w, OrbTerrain.Wall)) waitAnchor = null;
            waitAnchor ??= live.Centre;
            controls = Hover.Around(live, waitAnchor.Value, world);
            LastSteering = Steering.Hover;
            Lookahead = live.Centre;
            ProgressReason = Status == ExecutionStatus.Pending ? "waiting-for-route"
                : settledShort != null ? "as-close-as-it-can-get" : "no-route";
        }
        if (Status is not (ExecutionStatus.Pending or ExecutionStatus.Unreachable)) waitAnchor = null;
        WatchProgress(live);
        return controls;
    }

    private void Plan(OrbState live, Vector2 goal, ITileWorld world)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        LastPlanFailed = LastPlanEmpty = false;
        if (search == null || !search.Valid)
        {
            search = null;
            if (settledShort != null)
            {
                planMsUnread += clock.Elapsed.TotalMilliseconds;
                return;
            }
            if (spent != null)
            {
                // The last search already answered this goal from about here. A proven absence is kept while the body
                // stays inside what it explored; a node limit is asked again only once the partial route it lent has
                // brought the body materially nearer, since from the same place it would stop at the same limit.
                bool earned = spent.Stop != FreeSpaceSearch.StopReason.Exhausted && Path == null
                    && Vector2.Distance(live.Centre, goal) <= spentFromDistance - Weights.ObjectiveProgressPixels;
                if (!earned)
                {
                    LastPlanFailed = LastPlanEmpty = true;
                    LastSearchStop = spent.Stop;
                    LastExpansions = spent.Expansions;
                    planMsUnread += clock.Elapsed.TotalMilliseconds;
                    return;
                }
                spent = null;
            }
            PlannedThisTick = true;
            Point? start = CornerGraph.NearestUsable(world, live.Centre, 2);
            // A goal with no free corner beside it — a follow anchor projected into the rock under a floor — is planned to
            // the free corner nearest it, so the body goes as close as it can instead of asking about a wall every tick.
            Point? end = CornerGraph.NearestUsable(world, goal, 2, requireSweep: false)
                ?? CornerGraph.NearestUsable(world, goal, Weights.RouteGoalCornerTiles, requireSweep: false);
            if (start == null || end == null)
            {
                // Inside something, or aiming at a goal buried deeper than the corner search looks: nothing to plan over. The direct steer or a hover answers this tick, and next tick asks again.
                LastPlanFailed = true;
                LastSearchStop = FreeSpaceSearch.StopReason.Exhausted;
                LastExpansions = 0;
                planMsUnread += clock.Elapsed.TotalMilliseconds;
                if (start == null) ProgressReason = "no-corner-under-body";
                return;
            }
            search = new FreeSpaceSearch(world, start.Value, end.Value) { Avoid = Avoid };
            searchFromDistance = Vector2.Distance(live.Centre, goal);
            SearchId++;
            BehaviourCensus.Planned(replan: false);
        }
        PlannedThisTick = true;
        bool finished = search.Advance(Weights.RouteSearchExpansions, PlanMsBudget);
        LastSearchStop = search.Stop;
        LastExpansions = search.Expansions;
        if (finished && search.Stop == FreeSpaceSearch.StopReason.Found)
        {
            var raw = Joined(live, search.RouteCorners()!, world);
            // The goal itself is the last point when the body can slide to it from the last corner; otherwise the route
            // ends at that corner, which is the nearest free place to a goal inside rock or a goal just behind a thin wall.
            bool reachesGoal = CircleContact.SweptClear(world, raw[^1], goal, OrbTerrain.Wall);
            if (reachesGoal) raw.Add(goal);
            Path = new Route(Route.Smooth(world, raw), SearchId, world.Revision);
            PathIsPartial = !reachesGoal;
            routeEndsWhereSearchChose = !reachesGoal;
            search = null;
        }
        else if (finished)
        {
            LastPlanEmpty = true;
            LastPlanFailed = true;
            BehaviourCensus.PlanFailed();
            PlanFailed?.Invoke(live.Tile, MovementQueries.Tile(goal), null, search.Expansions, search.Stop.ToString());
            // A proven absence is published through GoalProvenUnreachable, which the brain bans a spot on; a node limit is
            // not, because it proves nothing and the partial route is still making progress. Neither touches the stuck
            // strikes, which count windows without progress and mean exactly that to the route-endings fixture.
            spent = search;
            spentGoal = goal;
            spentFromDistance = searchFromDistance;
            if (search.Stop == FreeSpaceSearch.StopReason.Exhausted)
            {
                Path = null;
                PathIsPartial = false;
            }
            else if (Path == null)
            {
                TakePartialRoute(live, goal, world, search);
            }
            search = null;
        }
        else
        {
            BehaviourCensus.PlanPending();
            if (Path == null || (PathIsPartial && Vector2.Distance(live.Centre, Path.Goal) <= SettleRadius))
                TakePartialRoute(live, goal, world, search);
        }
        planMsUnread += clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// A route to the nearest place <paramref name="from"/> has reached, when that place is materially nearer the goal than
    /// the body already is and the straight line to the goal is not clear, which the direct steer answers better.
    /// </summary>
    private void TakePartialRoute(OrbState live, Vector2 goal, ITileWorld world, FreeSpaceSearch from)
    {
        if (from.Closest is not Point nearest) return;
        if (Vector2.Distance(CornerGraph.ToWorld(nearest), goal) > Vector2.Distance(live.Centre, goal) - Weights.ObjectiveProgressPixels) return;
        if (CircleContact.SweptClear(world, live.Centre, goal, OrbTerrain.Wall)) return;
        Path = new Route(Route.Smooth(world, Joined(live, from.PathTo(nearest), world)), SearchId, world.Revision);
        PathIsPartial = true;
        routeEndsWhereSearchChose = false;
        settledShort = null;
    }

    /// <summary>The body's centre, then the corners from the one nearest the body that it can fly straight to, so a route a
    /// search began somewhere else does not send the body back to where the search started.</summary>
    private static List<Vector2> Joined(OrbState live, List<Point> corners, ITileWorld world)
    {
        int from = 0;
        float best = JoinPixels * JoinPixels;
        for (int i = 0; i < corners.Count; i++)
        {
            Vector2 at = CornerGraph.ToWorld(corners[i]);
            float d = Vector2.DistanceSquared(live.Centre, at);
            if (d < best && CircleContact.SweptClear(world, live.Centre, at, OrbTerrain.Wall)) { best = d; from = i; }
        }
        var raw = new List<Vector2>(corners.Count - from + 2) { live.Centre };
        for (int i = from; i < corners.Count; i++) raw.Add(CornerGraph.ToWorld(corners[i]));
        return raw;
    }

    private void DropRoute()
    {
        Path = null;
        PathIsPartial = false;
        routeEndsWhereSearchChose = false;
        search = null;
        spent = null;
        settledShort = null;
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
        PathIsPartial = false;
        search = null;
    }

    public void ResetStrikes() => StuckStrikes = 0;

    /// <summary>End the held goal and its route, scoring the attempt by who ended it.</summary>
    public void Interrupt(OrbState live, AttemptEnding ending = AttemptEnding.Cancelled, string cause = "released")
    {
        waitAnchor = null;
        if (Goal == null && Path == null && search == null && spent == null) return;
        if (attemptOpen && !Arrived) EndAttempt(ending);
        Goal = null;
        DropRoute();
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
        Goal = null; DropRoute(); Arrived = false; attemptOpen = false; waitAnchor = null;
        Status = ExecutionStatus.Idle; ProgressReason = "idle"; StuckStrikes = 0; StuckTicks = 0;
    }
}
