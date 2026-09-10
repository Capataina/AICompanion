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
    public Point? AirTarget => airTarget;
    public override void Exit(in ActionContext ctx)
    {
        airTarget = null;
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
        if (danger <= 0f && !self.HeadUnderwater)
        {
            refuge = null;
            return 0f;
        }
        float escapePressure = 0f;
        if (self.HeadUnderwater)
        {
            Point from = MovementQueries.FeetTile(ctx.Npc.Bottom);
            Point? dry = FindDryHeadTarget(from);
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
        if (!ctx.Senses.Self.HeadUnderwater)
        {
            airTarget = null;
            return false;
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
        airTarget ??= FindDryHeadTarget(ctx.Companion.Motor.State.FeetTile);
        Point? target = airTarget ?? refuge;
        bool chosen = ctx.Companion.Brain.Movement.SeekState(
            ctx.Companion.Motor.State,
            state => HeadIsDry(state) && state.LiquidKind != 1,
            state => EscapeHeuristic(state, target),
            Weights.EscapeSearchWork,
            out controls,
            out pending);
        if (!chosen && !pending) airTarget = null;
        return chosen;
    }

    private static bool HeadIsDry(BodyState state)
    {
        int headRow = (int)System.MathF.Floor((state.Bottom - BodyPhysics.Height) / 16f);
        return !MovementQueries.World.Water((int)System.MathF.Floor(state.CentreX / 16f), headRow)
            && !MovementQueries.World.Lava((int)System.MathF.Floor(state.CentreX / 16f), headRow);
    }

    private static float EscapeHeuristic(BodyState state, Point? refuge)
    {
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

    private static Point? FindDryHeadTarget(Point from)
    {
        for (int radius = 0; radius <= Weights.RefugeSearchRadiusTiles; radius++)
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        {
            if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != radius)
                continue;
            Point candidate = new(from.X + dx, from.Y + dy);
            if (IsDryHeadTarget(candidate))
                return candidate;
        }
        return null;
    }

    private static bool IsDryHeadTarget(Point candidate)
    {
        int head = candidate.Y - NavGrid.BodyHeightTiles + 1;
        return !MovementQueries.IsLiquid(candidate.X, head) && !MovementQueries.IsLava(candidate.X, head)
            && !MovementQueries.IsBlock(candidate.X, head);
    }
}
