#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Brain.DecisionMatrix.Decision;
using AICompanion.Brain.DecisionMatrix.Senses;
using AICompanion.Brain.Work.Mining;

namespace AICompanion.Brain.Actions.Work;

/// <summary>
/// The player is really mining an ore: find the same ore outside the player's vein, or
/// failing that any ore, walk to a spot in reach, and clear the whole patch with the
/// player's pickaxe. The job survives ten seconds past the player's last ore hit so a
/// slow swing does not drop it, and a finished patch is followed by the next one while
/// the player is still at it.
/// </summary>
public sealed class MineAction : CompanionAction
{
    public override string Name => "mine";

    private const int KeepJobTicks = 600;
    private const int SearchRadiusTiles = 45;

    private OreFinder.OreTarget? target;
    private HashSet<Point> patch = new();
    private Point? swingAt;

    public override float Score(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsDead)
            return 0f;
        int pick = TileMiner.PickaxeFor(ctx.Player).pick;

        bool playerMining = p.MinedOre != null || TileDamageWatcher.TicksSinceOreHit <= KeepJobTicks;
        if (target is OreFinder.OreTarget t && !OreFinder.IsOre(t.Tile.X, t.Tile.Y))
        {
            patch.Remove(t.Tile);
            target = NextInPatch(ctx, t) ;
        }
        if (target == null && playerMining && p.MinedOre is (Point hit, int type))
        {
            var playersVein = OreFinder.Vein(hit, type);
            var found = OreFinder.FindNearest(ctx.Npc.Center, SearchRadiusTiles, type, playersVein);
            if (found is OreFinder.OreTarget f && TileMiner.CanMine(f.Tile, pick))
            {
                target = f;
                patch = OreFinder.Vein(f.Tile, f.Type);
            }
        }
        if (!playerMining)
            target = null;
        if (target == null)
            return 0f;

        float safe = Consideration.AtLeast(1f - ctx.Senses.Threats.PlayerDanger, 0.1f);
        return 0.7f * safe;
    }

    public override float ForecastTicks(in ActionContext ctx)
        => target == null ? 0f : Vector2.Distance(ctx.Npc.Bottom, target.Value.StandPosition) / Companion.CompanionMotor.WalkSpeed + 180f;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        Item pickaxe = TileMiner.PickaxeFor(ctx.Player);
        // The pickaxe comes out only in position; on the walk there the hand stays empty, so
        // the torch can hold it in the dark.
        ctx.Companion.HoldItem(ItemID.None);
        if (target is not OreFinder.OreTarget t)
            return PositionRequest.Hold;

        if (Vector2.Distance(ctx.Npc.Bottom, t.StandPosition) <= 20f)
        {
            ctx.Companion.HoldItem(pickaxe.type);
            Point tile = swingAt ?? t.Tile;
            if (!OreFinder.IsOre(tile.X, tile.Y) || !OreFinder.InReach(ctx.Npc.Bottom, tile))
            {
                swingAt = null;
                tile = t.Tile;
            }
            ctx.Companion.Motor.Face(tile.X * 16f + 8f);
            if (ctx.Companion.Miner.Swing(tile, pickaxe))
                ctx.Companion.StartAnimation(pickaxe.type, pickaxe.useAnimation);
            return PositionRequest.Hold;
        }
        return PositionRequest.ExactAt(t.StandPosition);
    }

    /// <summary>The next tile of the patch: one still in reach of the current stand, else the nearest with a new stand.</summary>
    private OreFinder.OreTarget? NextInPatch(in ActionContext ctx, OreFinder.OreTarget current)
    {
        patch.RemoveWhere(p => !OreFinder.IsOre(p.X, p.Y));
        if (patch.Count == 0)
            return null;
        Point? inReach = null;
        float best = float.MaxValue;
        foreach (Point p in patch)
        {
            float d = Vector2.DistanceSquared(p.ToWorldCoordinates(), ctx.Npc.Center);
            if (d < best && OreFinder.InReach(current.StandPosition, p))
            {
                best = d;
                inReach = p;
            }
        }
        if (inReach is Point r)
            return current with { Tile = r };
        foreach (Point p in patch)
        {
            if (OreFinder.Approach(p) is Vector2 stand)
                return new OreFinder.OreTarget(p, current.Type, stand);
        }
        return null;
    }
}
