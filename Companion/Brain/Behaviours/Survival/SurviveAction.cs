#nullable enable

using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;
using AICompanion.Companion.Brain.SharedMovementSystem;
using AICompanion.Companion.Brain.WorldObservation;

namespace AICompanion.Companion.Brain.Behaviours.Survival;

/// <summary>
/// Preserve the companion's own body independently of danger to the player. Submersion
/// scores against remaining breath and estimated escape time, while fire and lava use
/// the body's hazard pressure. A standable dry refuge feeds ordinary navigation; a
/// separate, retained air target feeds the shared control-sequence search when the
/// submerged body needs clearance before it can reach a shore. Keeping that target
/// stable lets the search finish instead of restarting at each nearer air pocket.
/// Controls are simulated with the current body and terrain; this grants no swimming
/// ability and cannot promise escape from a physically sealed pool.
/// </summary>
public sealed class SurviveAction : CompanionAction
{
    public override string Name => "survive";
    public override bool IsExcursion => false;

    private Point? refuge;
    private Point? airTarget;
    private int airRevision = -1;
    private bool escapeActive;
    private bool seekingLanding;
    private Point? nearbyAir;
    private Point airSearchOrigin;
    private int nextAirSearch;
    private int nearbyAirRevision = -1;
    private System.Collections.Generic.Dictionary<Point, int> airDistances = new();
    private static readonly Point[] Neighbours = { new(0, -1), new(-1, 0), new(1, 0), new(0, 1) };
    public bool EscapeActive => escapeActive;
    public string EscapeStage => !escapeActive ? "inactive" : seekingLanding ? "dry-landing" : "breathing-air";
    public Point? AirTarget => airTarget;
    public override void Exit(in ActionContext ctx)
    {
        airTarget = null;
        escapeActive = false;
        seekingLanding = false;
        ctx.Companion.Brain.Movement.CancelStateSearch();
    }

    /// <summary>
    /// Ticks until the held refuge is asked again whether it can still be reached. A refuge is
    /// otherwise dropped only when the tile stops being a refuge, which is a question about the
    /// world and not about the route: a body that took a one-way drop, was cut off by settling
    /// sand, or was simply promised a route the search never found stays committed to a place it
    /// cannot arrive at until it drowns there. Asked on a cadence rather than every tick because
    /// the check is a bounded A* and this action runs sixty times a second while it runs at all;
    /// half a second is short against a breath bar and long against a search.
    /// </summary>
    private int sinceRecheck;
    private int searchCooldown;

    public override float Score(in ActionContext ctx)
    {
        CompanionSense self = ctx.Senses.Self;
        float danger = self.SelfDanger;
        // Breaking the surface halfway through a jump has not completed the escape.
        // Keep control until a dry landing, so a one-tick breath cannot cancel the
        // lateral movement that the body still needs to get onto the bank.
        if (escapeActive && !self.HeadUnderwater && !ctx.Companion.Motor.State.OnGround)
            return Weights.SurviveUrgency;
        if (danger <= 0f && !self.HeadUnderwater)
        {
            escapeActive = false;
            refuge = null;
            return 0f;
        }
        float escapePressure = 0f;
        if (self.HeadUnderwater)
        {
            Point from = MovementQueries.FeetTile(ctx.Npc.Bottom);
            Point? dry = NearbyAir(from, ctx.Senses.Tick);
            // Wet movement is at most half ordinary walking speed.  This deliberately optimistic
            // geometric time is still a lower bound: if even it consumes the remaining breath,
            // the action must win before the old half-breath threshold.
            float eta = dry is Point target
                ? Microsoft.Xna.Framework.Vector2.Distance(MovementQueries.FeetWorld(from), MovementQueries.FeetWorld(target)) / (BodyPhysics.WalkSpeed * .5f)
                : self.BreathTicksLeft;
            escapePressure = System.Math.Clamp(eta / System.Math.Max(1, self.BreathTicksLeft), 0f, 1f);
        }
        // Pulls from a modest score as danger appears to the top of the scale near the end, above
        // a *committed* guard rather than merely above guard's raw ceiling, which is the whole
        // point of the ladder in Weights: guarding is now scaled to interrupt an ordinary action,
        // so surviving has to be scaled to interrupt guarding or a drowning companion would stand
        // and shoot. The two constants move together.
        return System.Math.Max(Consideration.Rising(danger, 1f), escapePressure) * Weights.SurviveUrgency;
    }

    public override PositionRequest Execute(in ActionContext ctx)
    {
        ctx.Companion.HoldItem(Terraria.ID.ItemID.None);
        Point feet = MovementQueries.FeetTile(ctx.Npc.Bottom);
        if (refuge is Point r && !IsRefuge(r.X, r.Y))
            refuge = null;
        if (refuge is Point held && ++sinceRecheck >= Weights.RefugeRecheckTicks)
        {
            sinceRecheck = 0;
            if (!MovementQueries.WalkerProvenReach(feet, held))
                refuge = null;
        }
        if (searchCooldown > 0) searchCooldown--;
        if (refuge == null && searchCooldown == 0)
        {
            refuge = FindRefuge(feet);
            searchCooldown = Weights.RefugeRecheckTicks;
        }
        if (refuge is Point spot)
            return PositionRequest.ExactAt(MovementQueries.FeetWorld(spot));
        // The coordinator asks TryEscape for a retained, collision-validated control prefix
        // before resolving this fallback.  A stationary Hold+Jump was only a repeated vertical
        // attempt: beneath an awning it never creates the side clearance needed to leave water.
        return PositionRequest.Hold;
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
        if (!ctx.Senses.Self.HeadUnderwater && (!escapeActive || ctx.Companion.Motor.State.OnGround))
        {
            airTarget = null;
            escapeActive = false;
            return false;
        }
        escapeActive = true;
        bool landing = !ctx.Senses.Self.HeadUnderwater;
        if (landing != seekingLanding)
        {
            seekingLanding = landing;
            ctx.Companion.Brain.Movement.CancelStateSearch();
            airTarget = landing ? FindRefuge(ctx.Companion.Motor.State.FeetTile) : null;
        }

        // Breathing needs air, not a standable shore. A geometrically nearby dry floor can
        // be below the pool behind rock and pull every short rollout away from its surface.
        // Keep the objective stable while a prefix is being searched and executed. Selecting
        // the nearest air cell again after every step can alternate between separate pockets
        // and invalidate the progress that made the previous direction useful.
        if (airRevision != MovementQueries.World.Revision || airTarget is Point heldAir && !IsDryHeadTarget(heldAir))
        {
            airRevision = MovementQueries.World.Revision;
            airTarget = null;
            ctx.Companion.Brain.Movement.CancelStateSearch();
        }
        airTarget ??= NearbyAir(ctx.Companion.Motor.State.FeetTile, ctx.Senses.Tick);
        Point? target = airTarget ?? refuge;
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

    private static bool HeadIsDry(BodyState state)
    {
        // Use the same head rectangle and partial-liquid surface as CompanionBreath.
        // A whole wet tile is not necessarily submerged at this body's head height.
        return !state.Wet || state.LiquidKind != 0 || !Terraria.Collision.DrownCollision(
            new Vector2(state.Left, state.Bottom - BodyPhysics.Height), BodyPhysics.Width, BodyPhysics.Height, 1f);
    }

    private float EscapeHeuristic(BodyState state, Point? refuge)
    {
        // Distance through occupiable terrain preserves progress around an awning.
        // Direct distance to the air rewards a jump into its underside instead.
        if (!seekingLanding && airDistances.Count > 0)
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

    private static Point? FindRefuge(Point from)
    {
        // This is geometry only.  Reachability is established by the retained body-state search,
        // rather than spending a synchronous flood on every survival tick or trusting a grid
        // route that cannot start from the submerged live pose.
        for (int radius = 0; radius <= Weights.RefugeSearchRadiusTiles; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != radius)
                    continue;
                Point candidate = new(from.X + dx, from.Y + dy);
                if (IsRefuge(candidate.X, candidate.Y))
                    return candidate;
            }
        }
        return null;
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
        // This flood only supplies search guidance. It excludes air behind solid rock;
        // the body-state search still has to prove every control through the passage.
        var open = new System.Collections.Generic.Queue<Point>();
        var visited = new System.Collections.Generic.HashSet<Point>();
        open.Enqueue(from);
        visited.Add(from);
        var passable = new System.Collections.Generic.HashSet<Point> { from };
        while (open.TryDequeue(out Point candidate))
        {
            if (nearbyAir == null && IsDryHeadTarget(candidate)) nearbyAir = candidate;
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
