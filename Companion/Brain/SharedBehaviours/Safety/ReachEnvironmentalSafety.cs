#nullable enable

using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.SharedBehaviours.Safety;

/// <summary>
/// Preserve the companion's own body independently of ordinary utility. Breath reserve,
/// estimated time to air and lava exposure can start a retained control-sequence search.
/// Air and landing targets use body-sized passage guidance; keeping each target stable
/// lets the search finish instead of restarting at every nearer pocket.
/// Controls are simulated with the current body and terrain; this grants no swimming
/// ability and cannot promise escape from a physically sealed pool.
/// </summary>
public sealed class ReachEnvironmentalSafety
{
    public bool NeedsResponse { get; private set; }
    public float EstimatedEscapeTicks { get; private set; }

    private Point? airTarget;
    private int airRevision = -1;
    private bool escapeActive;
    private bool seekingLanding;
    private Point? nearbyAir;
    private Point airSearchOrigin;
    private int nextAirSearch;
    private int nearbyAirRevision = -1;
    private bool nearbyAirNeedsLanding;
    private System.Collections.Generic.Dictionary<Point, int> airDistances = new();
    private static readonly Point[] Neighbours = { new(0, -1), new(-1, 0), new(1, 0), new(0, 1) };
    public bool EscapeActive => escapeActive;
    public string EscapeStage => !escapeActive ? "inactive" : seekingLanding ? "dry-landing" : "breathing-air";
    public Point? AirTarget => airTarget;
    public void Cancel(in ActionContext ctx)
    {
        NeedsResponse = false;
        airTarget = null;
        escapeActive = false;
        seekingLanding = false;
        ctx.Companion.Brain.Movement.CancelStateSearch();
    }

    public void Refresh(in ActionContext ctx)
    {
        CompanionSense self = ctx.Senses.Self;
        bool exposed = self.HeadUnderwater || self.InLava;
        if (escapeActive && !exposed && ctx.Companion.Motor.State.OnGround)
        {
            Cancel(ctx);
            return;
        }
        if (escapeActive)
        {
            NeedsResponse = true;
            return;
        }
        NeedsResponse = self.InLava;
        EstimatedEscapeTicks = 0f;
        if (!self.HeadUnderwater) return;
        Point from = MovementQueries.FeetTile(ctx.Npc.Bottom);
        Point? dry = NearbyAir(from, ctx.Senses.Tick);
        // This is an optimistic geometric bound, not a proven breath-feasible route. Ordinary
        // water travel remains possible while breath reserve is healthy and this bound fits.
        EstimatedEscapeTicks = dry is Point target
            ? Vector2.Distance(MovementQueries.FeetWorld(from), MovementQueries.FeetWorld(target)) / (BodyPhysics.WalkSpeed * .5f)
            : self.BreathTicksLeft;
        NeedsResponse |= self.SelfDanger > 0f || EstimatedEscapeTicks >= self.BreathTicksLeft;
    }

    /// <summary>
    /// Finds the next verified clearance control while the breathing sense says the head is
    /// underwater.  The search belongs to <see cref="CoordinateMovement"/>: survival supplies
    /// only why a state is safe and which of the legal states is useful.
    /// </summary>
    public bool TryEscape(in ActionContext ctx, out Controls controls, out bool pending)
    {
        controls = Controls.None;
        pending = false;
        bool exposed = ctx.Senses.Self.HeadUnderwater || ctx.Senses.Self.InLava;
        if (!exposed && (!escapeActive || ctx.Companion.Motor.State.OnGround))
        {
            airTarget = null;
            escapeActive = false;
            return false;
        }
        escapeActive = true;
        // First air changes the objective to a sustainable landing. Falling back through the
        // surface must not discard that shore search and restart the same breathing jump.
        bool landing = seekingLanding || !exposed;
        if (landing != seekingLanding)
        {
            seekingLanding = landing;
            ctx.Companion.Brain.Movement.CancelStateSearch();
            airTarget = null;
        }

        // Breathing needs air, not a standable shore. A geometrically nearby dry floor can
        // be below the pool behind rock and pull every short rollout away from its surface.
        // Keep the objective stable while a prefix is being searched and executed. Selecting
        // the nearest air cell again after every step can alternate between separate pockets
        // and invalidate the progress that made the previous direction useful.
        if (airRevision != MovementQueries.World.Revision || airTarget is Point heldAir
            && (!IsDryHeadTarget(heldAir) || seekingLanding && !IsRefuge(heldAir.X, heldAir.Y)))
        {
            airRevision = MovementQueries.World.Revision;
            airTarget = null;
            ctx.Companion.Brain.Movement.CancelStateSearch();
        }
        airTarget ??= NearbyAir(ctx.Companion.Motor.State.FeetTile, ctx.Senses.Tick, seekingLanding);
        Point? target = airTarget;
        bool chosen = ctx.Companion.Brain.Movement.SeekState(
            ctx.Companion.Motor.State,
            state => HeadIsDry(state) && (!seekingLanding || state.OnGround) && state.LiquidKind != 1,
            state => EscapeHeuristic(state, target),
            Weights.EscapeSearchWork,
            out controls,
            out pending);
        if (!chosen && !pending) airTarget = null;
        return chosen;
    }

    internal static bool HeadIsDry(BodyState state)
    {
        // Use the same head rectangle and partial-liquid surface as CompanionBreath.
        // A whole wet tile is not necessarily submerged at this body's head height.
        return !state.Wet || state.LiquidKind != 0 || !Terraria.Collision.DrownCollision(
            new Vector2(state.Left, state.Bottom - BodyPhysics.Height), BodyPhysics.Width, BodyPhysics.Height, 1f);
    }

    private float EscapeHeuristic(BodyState state, Point? refuge)
    {
        // Both breathing and landing use passage distance. A geometrically near dry floor
        // behind a wall is not a useful guide for the controls that must get around it.
        if (airDistances.Count > 0)
        {
            Point cell = state.FeetTile;
            if (airDistances.TryGetValue(cell, out int distance)) return distance * 16f;
            float nearest = float.PositiveInfinity, score = float.PositiveInfinity;
            foreach (var entry in airDistances)
            {
                float gap = Vector2.DistanceSquared(state.Feet, MovementQueries.FeetWorld(entry.Key));
                if (gap < nearest) { nearest = gap; score = entry.Value * 16f + System.MathF.Sqrt(gap); }
            }
            return score;
        }
        if (refuge is Point spot)
            return Microsoft.Xna.Framework.Vector2.Distance(state.Feet, MovementQueries.FeetWorld(spot));
        // Terraria's world Y grows downward.  In a pocket with no identified shore, an upward
        // state is the only general progress signal; the sequence search is still free to step
        // away from a wall first because this is an ordering value, never a movement constraint.
        return state.Bottom / 16f;
    }

    /// <summary>Standable, head row dry, nothing in the body column or under the feet is lava.</summary>
    private static bool IsRefuge(int x, int y)
    {
        if (!MovementQueries.IsStandable(x, y))
            return false;
        for (int i = 0; i < MovementQueries.BodyHeightTiles; i++)
            if (MovementQueries.IsLava(x, y - i))
                return false;
        return !MovementQueries.IsLiquid(x, y - 2) && !MovementQueries.IsLava(x, y + 1);
    }

    private Point? NearbyAir(Point from, int tick, bool needsLanding = false)
    {
        if (tick < nextAirSearch && nearbyAirRevision == MovementQueries.World.Revision
            && nearbyAirNeedsLanding == needsLanding
            && System.Math.Abs(from.X - airSearchOrigin.X) + System.Math.Abs(from.Y - airSearchOrigin.Y) < 8)
            return nearbyAir;
        nextAirSearch = tick + Weights.RefugeRecheckTicks;
        nearbyAirRevision = MovementQueries.World.Revision;
        nearbyAirNeedsLanding = needsLanding;
        airSearchOrigin = from;
        nearbyAir = null;
        airDistances = new();
        // This flood only supplies search guidance. It excludes air behind solid rock;
        // the body-state search still has to prove every control through the passage.
        var open = new System.Collections.Generic.Queue<Point>();
        var visited = new System.Collections.Generic.HashSet<Point>();
        open.Enqueue(from);
        visited.Add(from);
        var passable = new System.Collections.Generic.HashSet<Point> { from };
        while (open.TryDequeue(out Point candidate))
        {
            if (nearbyAir == null && IsDryHeadTarget(candidate)
                && (!needsLanding || IsRefuge(candidate.X, candidate.Y))) nearbyAir = candidate;
            foreach (Point offset in Neighbours)
            {
                Point next = candidate + offset;
                if (System.Math.Max(System.Math.Abs(next.X - from.X), System.Math.Abs(next.Y - from.Y)) > Weights.RefugeSearchRadiusTiles
                    || !MovementQueries.World.InWorld(next.X, next.Y) || !visited.Add(next)) continue;
                Vector2 feet = MovementQueries.FeetWorld(next);
                if (BodyPhysics.Fits(MovementQueries.World, feet.X - BodyPhysics.Width / 2f, feet.Y))
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

    private static bool IsDryHeadTarget(Point candidate)
    {
        Vector2 feet = MovementQueries.FeetWorld(candidate);
        Vector2 position = feet - new Vector2(BodyPhysics.Width / 2f, BodyPhysics.Height);
        return MovementQueries.World.InWorld(candidate.X, candidate.Y)
            && BodyPhysics.Fits(MovementQueries.World, position.X, feet.Y)
            && !Terraria.Collision.LavaCollision(position, BodyPhysics.Width, BodyPhysics.Height)
            && !Terraria.Collision.DrownCollision(position, BodyPhysics.Width, BodyPhysics.Height, 1f);
    }
}
