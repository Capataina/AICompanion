#nullable enable

using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.SharedBehaviours.Safety;

/// <summary>
/// Preserve the companion's own body independently of ordinary utility. The orb has no breath:
/// water and lava hurt it on contact, so touching either is the exposure and leaving it is the
/// whole response. The escape is a retained flood over the corner graph that is allowed to
/// cross the liquid the body is already in, toward the nearest free, dry cell; the target is
/// kept stable while the search runs so it finishes instead of restarting at every nearer pocket.
/// Nothing here grants immunity: a body sealed inside a pool with no dry cell in reach reports
/// that failure and keeps its purpose.
/// </summary>
public sealed class ReachEnvironmentalSafety
{
    public bool NeedsResponse { get; private set; }
    public float EstimatedEscapeTicks { get; private set; }

    private Point? airTarget;
    private int airRevision = -1;
    private bool escapeActive;
    private Point? nearbyAir;
    private Point airSearchOrigin;
    private int nextAirSearch;
    private int nearbyAirRevision = -1;
    private System.Collections.Generic.Dictionary<Point, int> airDistances = new();
    private static readonly Point[] Neighbours = { new(0, -1), new(-1, 0), new(1, 0), new(0, 1) };
    public bool EscapeActive => escapeActive;
    public string EscapeStage => escapeActive ? "leaving-liquid" : "inactive";
    public Point? AirTarget => airTarget;
    public void Cancel(in ActionContext ctx)
    {
        NeedsResponse = false;
        airTarget = null;
        escapeActive = false;
        ctx.Companion.Brain.Movement.CancelStateSearch();
    }

    public void Refresh(in ActionContext ctx)
    {
        bool exposed = ctx.Companion.Motor.InHurtingLiquid;
        if (escapeActive && !exposed && Dry(ctx.Npc.Center))
        {
            Cancel(ctx);
            return;
        }
        if (escapeActive)
        {
            NeedsResponse = true;
            return;
        }
        NeedsResponse = exposed;
        EstimatedEscapeTicks = 0f;
        if (!exposed) return;
        Point from = MovementQueries.Tile(ctx.Npc.Center);
        Point? dry = NearbyAir(from, ctx.Senses.Tick);
        // An optimistic geometric bound, not a proven route: the passage distance to the nearest dry
        // cell at full pace.
        EstimatedEscapeTicks = dry is Point target
            ? Vector2.Distance(ctx.Npc.Center, MovementQueries.HoverPoint(target)) / OrbPace.MaxSpeed
            : float.PositiveInfinity;
    }

    /// <summary>
    /// Finds the next step out of the liquid while the motor says the body is in one that hurts. The
    /// search belongs to <see cref="CoordinateMovement"/>: safety supplies only why a place is safe
    /// and which of the reachable places is useful.
    /// </summary>
    public bool TryEscape(in ActionContext ctx, out Controls controls, out bool pending)
    {
        controls = Controls.None;
        pending = false;
        bool exposed = ctx.Companion.Motor.InHurtingLiquid;
        if (!exposed && (!escapeActive || Dry(ctx.Npc.Center)))
        {
            airTarget = null;
            escapeActive = false;
            return false;
        }
        escapeActive = true;
        // Keep the objective stable while the flood runs. Selecting the nearest dry cell again on
        // every tick can alternate between separate pockets and discard the flood that was
        // already most of the way to one of them.
        if (airRevision != MovementQueries.World.Revision || airTarget is Point heldAir && !MovementQueries.IsHoverable(heldAir))
        {
            airRevision = MovementQueries.World.Revision;
            airTarget = null;
            ctx.Companion.Brain.Movement.CancelStateSearch();
        }
        airTarget ??= NearbyAir(MovementQueries.Tile(ctx.Npc.Center), ctx.Senses.Tick);
        Point? target = airTarget;
        bool chosen = ctx.Companion.Brain.Movement.SeekState(
            ctx.Companion.Motor.State,
            Dry,
            centre => EscapeHeuristic(centre, target),
            Weights.EscapeSearchWork,
            throughLiquid: true,
            out controls,
            out pending);
        if (!chosen && !pending) airTarget = null;
        return chosen;
    }

    /// <summary>The circle at <paramref name="centre"/> touches no liquid that hurts this body under the
    /// tick's immunity rules. Honey and shimmer only slow, so they are dry for this purpose.</summary>
    internal static bool Dry(Vector2 centre)
    {
        ITileWorld world = MovementQueries.World;
        LiquidImmunity rules = OrbTerrain.Immunity;
        return !CircleContact.Touches(centre, (x, y) => OrbTerrain.WetWall(world, x, y, rules));
    }

    private float EscapeHeuristic(Vector2 centre, Point? refuge)
    {
        // Passage distance rather than straight-line distance: a geometrically near dry cell behind
        // a wall is not a useful guide for the steps that must get around it.
        if (airDistances.Count > 0)
        {
            Point cell = MovementQueries.Tile(centre);
            if (airDistances.TryGetValue(cell, out int distance)) return distance * 16f;
            float nearest = float.PositiveInfinity, score = float.PositiveInfinity;
            foreach (var entry in airDistances)
            {
                float gap = Vector2.DistanceSquared(centre, MovementQueries.TileCentre(entry.Key));
                if (gap < nearest) { nearest = gap; score = entry.Value * 16f + System.MathF.Sqrt(gap); }
            }
            return score;
        }
        if (refuge is Point spot)
            return Vector2.Distance(centre, MovementQueries.HoverPoint(spot));
        // Terraria's world Y grows downward. With no dry cell identified, up is the only general
        // progress signal, because liquid pools have their surface at the top.
        return centre.Y / 16f;
    }

    private Point? NearbyAir(Point from, int tick)
    {
        if (tick < nextAirSearch && nearbyAirRevision == MovementQueries.World.Revision
            && System.Math.Abs(from.X - airSearchOrigin.X) + System.Math.Abs(from.Y - airSearchOrigin.Y) < 8)
            return nearbyAir;
        nextAirSearch = tick + Weights.RefugeRecheckTicks;
        nearbyAirRevision = MovementQueries.World.Revision;
        airSearchOrigin = from;
        nearbyAir = null;
        airDistances = new();
        // This flood only supplies search guidance. It walks open tiles, wet or dry, and excludes
        // air behind solid rock; the corner flood still has to prove every step of the way there.
        ITileWorld world = MovementQueries.World;
        var open = new System.Collections.Generic.Queue<Point>();
        var visited = new System.Collections.Generic.HashSet<Point>();
        open.Enqueue(from);
        visited.Add(from);
        var passable = new System.Collections.Generic.HashSet<Point> { from };
        while (open.TryDequeue(out Point candidate))
        {
            if (nearbyAir == null && MovementQueries.IsHoverable(candidate)) nearbyAir = candidate;
            foreach (Point offset in Neighbours)
            {
                Point next = candidate + offset;
                if (System.Math.Max(System.Math.Abs(next.X - from.X), System.Math.Abs(next.Y - from.Y)) > Weights.RefugeSearchRadiusTiles
                    || !world.InWorld(next.X, next.Y) || !visited.Add(next)) continue;
                if (!OrbTerrain.Solid(world, next.X, next.Y))
                { open.Enqueue(next); passable.Add(next); }
            }
        }
        if (nearbyAir is Point goal)
        {
            open.Enqueue(goal); airDistances[goal] = 0;
            while (open.TryDequeue(out Point cell))
                foreach (Point offset in Neighbours)
                {
                    Point next = cell + offset;
                    if (passable.Contains(next) && !airDistances.ContainsKey(next))
                    { airDistances[next] = airDistances[cell] + 1; open.Enqueue(next); }
                }
        }
        return nearbyAir;
    }
}
