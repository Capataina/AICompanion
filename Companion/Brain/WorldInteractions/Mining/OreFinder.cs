#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.SharedMovementSystem;

namespace AICompanion.Companion.Brain.WorldInteractions.Mining;

/// <summary>
/// Finds ore the companion can mine. A vein is the 8-connected patch of one ore type
/// around a tile, bounded so a giant vein cannot stall a tick. A target is an ore tile
/// with a standing spot inside the player's pickaxe reach that has a line to it and
/// that the walker can actually get to from where it is, so the miner can swing from
/// where the navigator delivers the body.
/// </summary>
public static class OreFinder
{
    private const int MaxVeinTiles = 400;

    /// <summary>An ore and where to work it from. With <paramref name="Hop"/> the stand is a take-off: the
    /// swing happens during a proven ground jump from it, because no standing pose reaches the tile.</summary>
    public readonly record struct OreTarget(Point Tile, int Type, Vector2 StandPosition, bool Hop = false);
    public readonly record struct SearchResult(OreTarget? Target, Point? UnresolvedTile)
    {
        public bool ApproachUnknown => UnresolvedTile != null;
    }

    public static bool IsOre(int x, int y)
    {
        if (!WorldGen.InWorld(x, y, 5))
            return false;
        Tile t = Main.tile[x, y];
        return t.HasTile && t.TileType < TileID.Sets.Ore.Length && TileID.Sets.Ore[t.TileType];
    }

    public static bool IsOreOfType(int x, int y, int type)
        => IsOre(x, y) && Main.tile[x, y].TileType == type;

    /// <summary>Every tile of the same ore type connected to <paramref name="start"/>, up to a bound.</summary>
    public static HashSet<Point> Vein(Point start, int type)
    {
        var seen = new HashSet<Point>();
        if (!IsOreOfType(start.X, start.Y, type))
            return seen;
        var queue = new Queue<Point>();
        queue.Enqueue(start);
        seen.Add(start);
        while (queue.Count > 0 && seen.Count < MaxVeinTiles)
        {
            Point p = queue.Dequeue();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var q = new Point(p.X + dx, p.Y + dy);
                    if (seen.Contains(q) || !IsOreOfType(q.X, q.Y, type))
                        continue;
                    seen.Add(q);
                    queue.Enqueue(q);
                }
        }
        return seen;
    }

    /// <summary>
    /// Nearest ore tile near <paramref name="near"/> that the companion can reach from
    /// <paramref name="fromFeet"/>. The search origin and the route origin differ when the player
    /// sees a vein first: the companion still has to prove its own approach before selecting it.
    /// With a <paramref name="body"/>, a tile no standing pose reaches may still be selected through
    /// a proven hop from a reachable take-off; without one, only standing work is considered.
    /// </summary>
    public static SearchResult FindNearest(Vector2 fromFeet, Vector2 near, int radiusTiles, int preferredType = -1,
        System.Func<Point, bool>? accept = null, BodyState? body = null)
    {
        Point? unknown = null;
        OreTarget? preferred = Nearest(fromFeet, near, radiusTiles, preferredType, accept, body, ref unknown);
        if (preferred != null || preferredType < 0)
            return new SearchResult(preferred, unknown);
        OreTarget? any = Nearest(fromFeet, near, radiusTiles, -1, accept, body, ref unknown);
        return new SearchResult(any, unknown);
    }

    private static OreTarget? Nearest(Vector2 fromFeet, Vector2 near, int radiusTiles, int type, System.Func<Point, bool>? accept,
        BodyState? body, ref Point? unresolvedTile)
    {
        int cx = (int)(near.X / 16f), cy = (int)(near.Y / 16f);
        OreTarget? best = null;
        float bestDist = float.MaxValue;
        for (int x = cx - radiusTiles; x <= cx + radiusTiles; x++)
        {
            for (int y = cy - radiusTiles; y <= cy + radiusTiles; y++)
            {
                if (!IsOre(x, y) || (type >= 0 && Main.tile[x, y].TileType != type))
                    continue;
                var tile = new Point(x, y);
                if (accept != null && !accept(tile))
                    continue;
                float d = Vector2.DistanceSquared(fromFeet, tile.ToWorldCoordinates());
                if (d >= bestDist)
                    continue;
                Reachability.Reach approach = FindToolAccess.Approach(tile, fromFeet, out Vector2 stand);
                bool hop = false;
                // A hop answers "no standing pose exists", so it is asked only after the standing search
                // proved that. An undecided standing search usually stopped at its deadline, and the hop's
                // body simulations do not watch the deadline while its walker question could only come
                // back undecided too; the tile is asked again once the standing answer resolves.
                if (approach == Reachability.Reach.No && body is BodyState template)
                {
                    Reachability.Reach hopReach = FindToolAccess.HopApproach(tile, fromFeet, template, out Vector2 takeOff);
                    if (hopReach == Reachability.Reach.Yes) { approach = hopReach; stand = takeOff; hop = true; }
                    else if (hopReach == Reachability.Reach.Unknown) approach = Reachability.Reach.Unknown;
                }
                if (approach == Reachability.Reach.Unknown && (unresolvedTile is not Point previous
                    || d < Vector2.DistanceSquared(fromFeet, previous.ToWorldCoordinates())))
                    unresolvedTile = tile;
                if (approach != Reachability.Reach.Yes)
                    continue;
                bestDist = d;
                best = new OreTarget(tile, Main.tile[x, y].TileType, stand, hop);
            }
        }
        return best;
    }

}
