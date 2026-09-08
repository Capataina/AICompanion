#nullable enable

using Microsoft.Xna.Framework;
using AICompanion.Brain.DecisionMatrix.Decision;
using AICompanion.Brain.DecisionMatrix.Navigation;
using AICompanion.Brain.DecisionMatrix.Senses;

namespace AICompanion.Brain.Actions.Survival;

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

    private const int SearchRadiusTiles = 24;

    private Point? refuge;

    public override float Score(in ActionContext ctx)
    {
        CompanionSense self = ctx.Senses.Self;
        float danger = self.SelfDanger;
        if (danger <= 0f)
        {
            refuge = null;
            return 0f;
        }
        // Pulls from a modest score as danger appears to the top of the scale near the end,
        // above guard's ceiling, so a drowning companion leaves even a fight.
        return Consideration.Rising(danger, 1f) * 1.2f;
    }

    public override PositionRequest Execute(in ActionContext ctx)
    {
        ctx.Companion.HoldItem(Terraria.ID.ItemID.None);
        Point feet = NavGrid.FeetTile(ctx.Npc.Bottom);
        if (refuge is Point r && !IsRefuge(r.X, r.Y))
            refuge = null;
        refuge ??= FindRefuge(feet);
        if (refuge is Point spot)
            return PositionRequest.ExactAt(NavGrid.FeetWorld(spot));
        // No refuge is reachable, which in water means a flooded pocket: tread water instead of
        // standing in it. Holding still here is what drowned the companion in a pit on 2026-09-08
        // while it had the whole breath to spend jumping. The motor takes a jump only from the
        // ground, so asking every tick bobs the body off the floor whenever the feet touch down,
        // and each break of the surface refills the breath. This saves the body wherever the
        // surface is inside a wet jump's rise; a shaft deeper than that still drowns, which is
        // what a player without a Flipper also suffers and what the swim traversal fixes.
        if (ctx.Senses.Self.HeadUnderwater)
            ctx.Companion.Motor.Jump();
        return PositionRequest.Hold;
    }

    /// <summary>Standable, head row dry, nothing in the body column or under the feet is lava.</summary>
    private static bool IsRefuge(int x, int y)
    {
        if (!NavGrid.IsStandable(x, y))
            return false;
        for (int i = 0; i < NavGrid.BodyHeightTiles; i++)
            if (NavGrid.IsLava(x, y - i))
                return false;
        return !NavGrid.IsLiquid(x, y - 2) && !NavGrid.IsLava(x, y + 1);
    }

    private static Point? FindRefuge(Point from)
    {
        for (int ring = 1; ring <= SearchRadiusTiles; ring++)
        {
            for (int dx = -ring; dx <= ring; dx++)
            {
                for (int dy = -ring; dy <= ring; dy++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != ring)
                        continue;
                    int x = from.X + dx, y = from.Y + dy;
                    if (IsRefuge(x, y) && Reachability.WalkerCanReach(from, new Point(x, y)))
                        return new Point(x, y);
                }
            }
        }
        return null;
    }
}
