#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>One retained travel query from a physical pose and momentum, including hypothetical
/// job-to-job legs. Observation runs the existing route, steering and contact implementation;
/// pure course evaluation consumes the frozen result and never reads live terrain.</summary>
public sealed class CaptureCourseTravel
{
    private readonly CoursePoint from, initialVelocity, to;
    private readonly ReadTravelTerrain world;
    private readonly float speed, turn, acceleration;
    private readonly long capabilityRevision;
    private FreeSpaceSearch? search;
    private Route? route;
    private Vector2 position, velocity;
    private int checkedRevision, simulationTicks;
    private DecisionFact? result;
    private bool initialised;

    public CaptureCourseTravel(ITileWorld terrain, CoursePoint from, CoursePoint initialVelocity, CoursePoint to,
        long capabilityRevision)
    {
        this.from = from; this.initialVelocity = initialVelocity; this.to = to;
        this.capabilityRevision = capabilityRevision;
        world = new(terrain); checkedRevision = terrain.Revision;
        speed = OrbPace.MaxSpeed; turn = OrbPace.Turn; acceleration = OrbPace.SpeedChange;
        position = Vector(from); velocity = Vector(initialVelocity);
    }

    public FactKey Key => ReadCourseTravel.Key(from, initialVelocity, to);
    public bool Complete => result != null;
    public int SimulationTicks => simulationTicks;
    public int ExpandedCorners => search?.Expansions ?? 0;
    public bool Stale { get; private set; }

    /// <summary>Null means the shared allowance cut an unfinished query. An unresolved result
    /// names a concluded model limitation; neither answer means the route is impossible.</summary>
    public DecisionFact? Continue(DecisionWorkBudget budget)
    {
        int now = world.Revision;
        if (speed != OrbPace.MaxSpeed || turn != OrbPace.Turn || acceleration != OrbPace.SpeedChange
            || world.ChangedSince(checkedRevision, world.ReadContains) != TerrainEditVerdict.Unchanged)
        {
            Stale = true;
            return Finish(OpportunityAdmission.Unresolved, "travel-input-changed");
        }
        checkedRevision = now;
        if (result != null) return result;
        if (!initialised)
        {
            if (!budget.TrySpend("course-travel-admission")) return null;
            initialised = true;
            if (!(speed > 0) || !(acceleration > 0) || !(turn > 0))
                return Finish(OpportunityAdmission.Unresolved, "movement-capability-unavailable");
            if (CircleContact.Overlaps(world, position) || CircleContact.Overlaps(world, Vector(to)))
                return Finish(OpportunityAdmission.KnownUnusable, "travel-endpoint-overlaps-terrain");
            Point? start = CornerGraph.NearestUsable(world, position, 2);
            Point? goal = CornerGraph.NearestUsable(world, Vector(to), 2);
            if (start == null || goal == null)
                return Finish(OpportunityAdmission.Unresolved, "endpoint-corner-not-found-in-local-bound");
            search = new(world, start.Value, goal.Value);
        }
        while (search is { Finished: false })
        {
            // Live callers already have this same borrowed budget installed. Headless
            // direct callers spend here; neither path creates another allowance.
            if (LimitPlanningWork.IsActive)
            {
                if (!ReferenceEquals(LimitPlanningWork.Current, budget))
                    throw new InvalidOperationException("Travel must borrow the active decision allowance.");
                if (budget.Exhausted) return null;
            }
            else if (!budget.TrySpend("course-travel-search")) return null;
            search.Advance(1);
        }
        if (route == null)
        {
            if (!budget.TrySpend("course-travel-route")) return null;
            if (search?.Stop != FreeSpaceSearch.StopReason.Found)
                return Finish(search?.Stop == FreeSpaceSearch.StopReason.Exhausted
                    ? OpportunityAdmission.KnownUnusable : OpportunityAdmission.Unresolved,
                    "route-" + search?.Stop);
            var points = new List<Vector2> { position };
            points.AddRange(search.RouteCorners()!.Select(CornerGraph.ToWorld));
            points.Add(Vector(to));
            route = new(Route.Smooth(world, points), CourseIdentity.Next(), world.Revision);
        }
        while (Vector2.Distance(position, Vector(to)) > Navigator.ArriveDistance)
        {
            if (!budget.TrySpend("course-travel-body")) return null;
            var controls = SteerAlongRoute.Steer(new(position, velocity), route, speed, acceleration, out _);
            Vector2 before = position;
            velocity = OrbPace.Step(velocity, controls.Desired, speed, turn, acceleration, controls.Burst);
            position += velocity;
            CircleContact.Resolve(world, ref position, ref velocity);
            simulationTicks++;
            if (position == before && velocity == Vector2.Zero)
                return Finish(OpportunityAdmission.Unresolved, "travel-model-pinned");
        }
        return Finish(OpportunityAdmission.KnownUsable, "native-motion-model-arrival");
    }

    private DecisionFact Finish(OpportunityAdmission admission, string reason)
    {
        var travel = new CapturedCourseTravel(from, initialVelocity, to, Point(velocity), simulationTicks,
            admission, reason, Array.AsReadOnly(route?.Points.Select(Point).ToArray() ?? Array.Empty<CoursePoint>()), capabilityRevision);
        // Input geometry is tracked spatially above. Distant edit counters never change
        // an identical captured estimate or manufacture a dependency change.
        return result = new(Key, capabilityRevision, new(Text: JsonSerializer.Serialize(travel)),
            admission == OpportunityAdmission.Unresolved ? FactEvidence.Unresolved : FactEvidence.Modelled);
    }

    private static Vector2 Vector(CoursePoint point) => new((float)point.X, (float)point.Y);
    private static CoursePoint Point(Vector2 point) => new(point.X, point.Y);

    private sealed class ReadTravelTerrain : ITileWorld
    {
        private readonly ITileWorld source;
        private int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        public ReadTravelTerrain(ITileWorld source) => this.source = source;
        public int Revision => source.Revision;
        public TerrainEditVerdict ChangedSince(int since, Func<int, int, bool> sensitive) => source.ChangedSince(since, sensitive);
        public bool ReadContains(int x, int y) => x >= left && x <= right && y >= top && y <= bottom;
        private void Touch(int x, int y)
        { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        public bool InWorld(int x, int y) { Touch(x, y); return source.InWorld(x, y); }
        public TileShape Shape(int x, int y) { Touch(x, y); return source.Shape(x, y); }
        public bool PassThrough(int x, int y) { Touch(x, y); return source.PassThrough(x, y); }
        public bool Water(int x, int y) { Touch(x, y); return source.Water(x, y); }
        public bool Lava(int x, int y) { Touch(x, y); return source.Lava(x, y); }
    }
}
