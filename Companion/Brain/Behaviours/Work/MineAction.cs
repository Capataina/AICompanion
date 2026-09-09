#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;
using AICompanion.Companion.Brain.WorldObservation;
using AICompanion.Companion.Brain.WorldInteractions.Mining;

namespace AICompanion.Companion.Brain.Behaviours.Work;

/// <summary>
/// The player is really mining an ore: find the same ore outside the player's vein, or
/// failing that any ore, walk to a spot in reach, and clear the whole patch with the
/// player's pickaxe. The job survives a while past the player's last ore hit so a slow
/// swing does not drop it, and a finished patch is followed by the next one while the
/// player is still at it. The search for a new target is the expensive part (a vein
/// flood and a scan of the area with a reachability check per candidate), so it runs
/// only when the player starts on a new ore or the last target is gone, and never more
/// often than a short cooldown, while the score itself stays cheap.
/// </summary>
public sealed class MineAction : CompanionAction
{
    public override string Name => "mine";

    private const int KeepJobTicks = 600;
    private const int SearchRadiusTiles = 45;
    private const int SearchEveryTicks = 60;

    private OreFinder.OreTarget? target;
    private HashSet<Point> patch = new();
    private Point? lastSearchedFor;
    private int sinceSearch = SearchEveryTicks;

    public override float Score(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsDead)
            return 0f;
        sinceSearch++;

        bool playerMining = p.MinedOre != null || TileDamageWatcher.TicksSinceOreHit <= KeepJobTicks;
        if (!playerMining)
        {
            target = null;
            lastSearchedFor = null;
            return 0f;
        }
        if (target is OreFinder.OreTarget t && !OreFinder.IsOre(t.Tile.X, t.Tile.Y))
        {
            patch.Remove(t.Tile);
            target = NextInPatch(ctx, t);
        }
        if (target == null && p.MinedOre is (Point hit, int type))
        {
            bool newOre = lastSearchedFor != hit;
            if (newOre || sinceSearch >= SearchEveryTicks)
                Search(ctx, hit, type);
        }
        if (target == null)
            return 0f;

        float safe = Consideration.AtLeast(1f - ctx.Senses.Threats.PlayerDanger, 0.1f);
        return 0.7f * safe;
    }

    private void Search(in ActionContext ctx, Point playersHit, int type)
    {
        sinceSearch = 0;
        lastSearchedFor = playersHit;
        int pick = TileMiner.PickaxeFor(ctx.Player).pick;
        var playersVein = OreFinder.Vein(playersHit, type);
        var found = OreFinder.FindNearest(ctx.Npc.Bottom, SearchRadiusTiles, type, playersVein);
        if (found is OreFinder.OreTarget f && ctx.Companion.Miner.CanMine(f.Tile, pick))
        {
            target = f;
            patch = OreFinder.Vein(f.Tile, f.Type);
        }
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

        if (Vector2.Distance(ctx.Npc.Bottom, t.StandPosition) > 20f)
            return PositionRequest.ExactAt(t.StandPosition);

        if (!OreFinder.InReach(ctx.Npc.Bottom, t.Tile))
        {
            // Arrived, but the tile is not swingable from here (the stand was approximate, or
            // the world changed): never swing at what cannot be reached; pick the next tile.
            patch.Remove(t.Tile);
            target = NextInPatch(ctx, t);
            return PositionRequest.Hold;
        }
        ctx.Companion.HoldItem(pickaxe.type);
        ctx.Companion.Motor.Face(t.Tile.X * 16f + 8f);
        if (ctx.Companion.Miner.Swing(t.Tile, pickaxe))
            ctx.Companion.StartAnimation(pickaxe.type, pickaxe.useAnimation);
        return PositionRequest.Hold;
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
            if (OreFinder.Approach(p, ctx.Npc.Bottom) is Vector2 stand)
                return new OreFinder.OreTarget(p, current.Type, stand);
        }
        return null;
    }
}
