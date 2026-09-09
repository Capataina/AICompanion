#nullable enable

using Microsoft.Xna.Framework;
using AICompanion.Brain.BehaviourSelection;
using AICompanion.Brain.PositionSelection;
using AICompanion.Brain.SharedMovementSystem;
using AICompanion.Brain.WorldObservation;

namespace AICompanion.Brain.Behaviours.Survival;

/// <summary>
/// Save the companion's own body: get the head out of the water before the breath runs
/// out, get out of lava and away from what is burning it. Scores on the self sense's
/// danger alone, so it is nothing while the body is fine, starts to pull once breath is
/// half gone or fire has caught, and outranks every other action near the end, because
/// nothing the companion could do for the player is worth drowning for. It does not
/// keep the companion out of water: crossing a pool is the navigator's business and
/// priced there; this is the backstop when a crossing turns out longer than the breath.
///
/// The spot it asks for is the nearest standable tile whose head row is out of liquid
/// and whose column touches no lava, found by widening rings around the feet; a wrong
/// guess costs one replan, and a tile that stops being safe is dropped. Where no such
/// tile is reachable at all the body treads water rather than holding still, because a
/// flooded pocket with no shore is survivable by bobbing and was not survived by standing.
/// </summary>
public sealed class SurviveAction : CompanionAction
{
    public override string Name => "survive";

    private Point? refuge;

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
        if (danger <= 0f)
        {
            refuge = null;
            return 0f;
        }
        // Pulls from a modest score as danger appears to the top of the scale near the end, above
        // a *committed* guard rather than merely above guard's raw ceiling, which is the whole
        // point of the ladder in Weights: guarding is now scaled to interrupt an ordinary action,
        // so surviving has to be scaled to interrupt guarding or a drowning companion would stand
        // and shoot. The two constants move together.
        return Consideration.Rising(danger, 1f) * Weights.SurviveUrgency;
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
        // Breaking the surface refills the breath, so it is worth asking for whether or not a
        // refuge was found. The shared movement coordinator admits this ground jump while the
        // body is uncommitted; an active route keeps its own jump and preparation controls.
        //
        // This sat below the refuge return until 2026-09-09, which made it the alternative to
        // having a plan rather than a floor underneath one, so it could not fire in the case that
        // actually drowns the companion: a refuge is found, the route to it finishes short, and
        // the body stands on the bottom of the pool with a finished path and an empty breath bar.
        // That is what killed it in the underground session at tick 20,679 — action survive,
        // request Exact, path 1 step of 1, velocity zero, press zero, breath zero, for a hundred
        // and twenty ticks. Whether a refuge exists says nothing about whether the head is out of
        // the water, and only the second question has anything to do with drowning.
        float jump = ctx.Senses.Self.HeadUnderwater ? 1f : 0f;
        if (refuge is Point spot)
            return PositionRequest.ExactAt(MovementQueries.FeetWorld(spot)) with { JumpScale = jump };
        // No refuge is reachable, which in water means a flooded pocket: tread water instead of
        // standing in it. The bob above saves the body wherever the surface is inside a wet jump's
        // rise; a shaft deeper than that still drowns, which is what a player without a Flipper
        // also suffers and what the swim traversal fixes (AIC-187).
        return PositionRequest.Hold with { JumpScale = jump };
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
        // One bounded expansion provides candidates for the whole request. Running a
        // separate A* for every dry tile makes a sealed wet pocket the most expensive case.
        var reached = MovementQueries.Region(from, Weights.ReachFloodBudget, out _);
        Point? nearest = null;
        int nearestDistance = int.MaxValue;
        foreach (Point candidate in reached)
        {
            int dx = candidate.X - from.X, dy = candidate.Y - from.Y;
            int distance = dx * dx + dy * dy;
            if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) > Weights.RefugeSearchRadiusTiles
                || distance >= nearestDistance || !IsRefuge(candidate.X, candidate.Y)) continue;
            nearest = candidate;
            nearestDistance = distance;
        }
        return nearest;
    }
}
