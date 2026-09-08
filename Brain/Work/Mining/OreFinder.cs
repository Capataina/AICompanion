#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Brain.DecisionMatrix.Navigation;

namespace AICompanion.Brain.Work.Mining;

/// <summary>
/// Finds ore the companion can mine. A vein is the 8-connected patch of one ore type
/// around a tile, bounded so a giant vein cannot stall a tick. A target is an ore tile
/// with a standing spot inside pickaxe reach that has a line to it, so the miner can
/// swing from where the navigator delivers the body.
/// </summary>
public static class OreFinder
{
    /// <summary>The player's pickaxe reach, in tiles, as Player.tileRangeX.</summary>
    public const int ReachTiles = 5;
    private const int MaxVeinTiles = 400;

    public readonly record struct OreTarget(Point Tile, int Type, Vector2 StandPosition);

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
    /// Nearest ore tile to <paramref name="from"/> within <paramref name="radiusTiles"/> that is
    /// of <paramref name="preferredType"/> and outside <paramref name="exclude"/>; failing that,
    /// the nearest ore of any type outside it. Null when nothing has a standing spot.
    /// </summary>
    public static OreTarget? FindNearest(Vector2 from, int radiusTiles, int preferredType, HashSet<Point> exclude)
        => Nearest(from, radiusTiles, preferredType, exclude) ?? Nearest(from, radiusTiles, -1, exclude);

    private static OreTarget? Nearest(Vector2 from, int radiusTiles, int type, HashSet<Point> exclude)
    {
        int cx = (int)(from.X / 16f), cy = (int)(from.Y / 16f);
        OreTarget? best = null;
        float bestDist = float.MaxValue;
        for (int x = cx - radiusTiles; x <= cx + radiusTiles; x++)
        {
            for (int y = cy - radiusTiles; y <= cy + radiusTiles; y++)
            {
                if (!IsOre(x, y) || (type >= 0 && Main.tile[x, y].TileType != type))
                    continue;
                var tile = new Point(x, y);
                if (exclude.Contains(tile))
                    continue;
                float d = Vector2.DistanceSquared(from, tile.ToWorldCoordinates());
                if (d >= bestDist)
                    continue;
                if (Approach(tile) is not Vector2 stand)
                    continue;
                bestDist = d;
                best = new OreTarget(tile, Main.tile[x, y].TileType, stand);
            }
        }
        return best;
    }

    /// <summary>A standable feet tile within reach of the ore whose eye has a line to it, nearest first.</summary>
    public static Vector2? Approach(Point ore)
    {
        Vector2 oreCentre = ore.ToWorldCoordinates();
        Vector2? best = null;
        float bestDist = float.MaxValue;
        for (int dx = -ReachTiles; dx <= ReachTiles; dx++)
        {
            for (int dy = -ReachTiles; dy <= ReachTiles + 2; dy++)
            {
                int x = ore.X + dx, y = ore.Y + dy;
                if (!NavGrid.IsStandable(x, y))
                    continue;
                Vector2 feet = NavGrid.FeetWorld(new Point(x, y));
                Vector2 eye = feet + new Vector2(0f, -30f);
                if (System.MathF.Abs(eye.X - oreCentre.X) > ReachTiles * 16f + 8f || System.MathF.Abs(eye.Y - oreCentre.Y) > ReachTiles * 16f + 8f)
                    continue;
                if (!Collision.CanHitLine(eye, 1, 1, oreCentre, 1, 1))
                    continue;
                float d = Vector2.DistanceSquared(eye, oreCentre);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = feet;
                }
            }
        }
        return best;
    }

    /// <summary>Whether a swing from <paramref name="feet"/> can reach <paramref name="ore"/>: inside reach with a line to it.</summary>
    public static bool InReach(Vector2 feet, Point ore)
    {
        Vector2 eye = feet + new Vector2(0f, -30f);
        Vector2 oreCentre = ore.ToWorldCoordinates();
        return System.MathF.Abs(eye.X - oreCentre.X) <= ReachTiles * 16f + 8f
            && System.MathF.Abs(eye.Y - oreCentre.Y) <= ReachTiles * 16f + 8f
            && Collision.CanHitLine(eye, 1, 1, oreCentre, 1, 1);
    }
}
