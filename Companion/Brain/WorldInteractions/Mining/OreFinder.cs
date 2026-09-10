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

    /// <summary>The player's own reach (the game keeps it as a static set from the local player each frame), so accessories that extend it extend the companion's.</summary>
    private static int ReachX => Player.tileRangeX;
    private static int ReachY => Player.tileRangeY;

    public readonly record struct OreTarget(Point Tile, int Type, Vector2 StandPosition);
    public readonly record struct SearchResult(OreTarget? Target, bool ApproachUnknown);

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
    /// </summary>
    public static SearchResult FindNearest(Vector2 fromFeet, Vector2 near, int radiusTiles, int preferredType = -1,
        System.Func<Point, bool>? accept = null)
    {
        bool unknown = false;
        OreTarget? preferred = Nearest(fromFeet, near, radiusTiles, preferredType, accept, ref unknown);
        if (preferred != null || preferredType < 0)
            return new SearchResult(preferred, unknown);
        OreTarget? any = Nearest(fromFeet, near, radiusTiles, -1, accept, ref unknown);
        return new SearchResult(any, unknown);
    }

    private static OreTarget? Nearest(Vector2 fromFeet, Vector2 near, int radiusTiles, int type, System.Func<Point, bool>? accept,
        ref bool approachUnknown)
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
                Reachability.Reach approach = Approach(tile, fromFeet, out Vector2 stand);
                if (approach == Reachability.Reach.Unknown)
                    approachUnknown = true;
                if (approach != Reachability.Reach.Yes)
                    continue;
                bestDist = d;
                best = new OreTarget(tile, Main.tile[x, y].TileType, stand);
            }
        }
        return best;
    }

    /// <summary>
    /// A standable feet tile within reach of the ore whose eye has a line to it and that the
    /// walker can reach from <paramref name="fromFeet"/>, nearest to the ore first.
    /// </summary>
    public static Reachability.Reach Approach(Point ore, Vector2 fromFeet, out Vector2 stand)
    {
        Vector2 oreCentre = ore.ToWorldCoordinates(8f, 8f);
        Point from = MovementQueries.FeetTile(fromFeet);
        Vector2? best = null;
        bool unknown = false;
        float bestDist = float.MaxValue;
        for (int dx = -ReachX; dx <= ReachX; dx++)
        {
            for (int dy = -ReachY; dy <= ReachY + 2; dy++)
            {
                int x = ore.X + dx, y = ore.Y + dy;
                if (!MovementQueries.IsStandable(x, y))
                    continue;
                Vector2 feet = MovementQueries.FeetWorld(new Point(x, y));
                if (!InReach(feet, ore) || !InReach(feet + new Vector2(-8, 0), ore) || !InReach(feet + new Vector2(8, 0), ore))
                    continue;
                float d = Vector2.DistanceSquared(feet + Eye, oreCentre);
                Reachability.Reach reach = MovementQueries.WalkerReach(from, new Point(x, y));
                if (reach == Reachability.Reach.Unknown)
                    unknown = true;
                if (d < bestDist && reach == Reachability.Reach.Yes)
                {
                    bestDist = d;
                    best = feet;
                }
            }
        }
        stand = best ?? default;
        return best != null ? Reachability.Reach.Yes : unknown ? Reachability.Reach.Unknown : Reachability.Reach.No;
    }

    private static readonly Vector2 Eye = new(0f, -30f);

    /// <summary>Whether a swing from <paramref name="feet"/> can reach <paramref name="ore"/>: inside the player's native reach box and with a line to one exposed face.</summary>
    public static bool InReach(Vector2 feet, Point ore)
    {
        Vector2 eye = feet + Eye;
        Vector2 oreCentre = ore.ToWorldCoordinates(8f, 8f);
        return System.MathF.Abs(eye.X - oreCentre.X) <= ReachX * 16f + 8f
            && System.MathF.Abs(eye.Y - oreCentre.Y) <= ReachY * 16f + 8f
            && HasLineToExposedFace(eye, ore);
    }

    private static bool HasLineToExposedFace(Vector2 eye, Point ore)
    {
        // PickTile itself checks only range. The companion adds occlusion so its closed-set
        // ability does not mine through a wall. CanHitLine includes a solid destination tile,
        // therefore the actual target is an adjacent open tile on an exposed ore face.
        foreach (Point side in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
        {
            Point face = ore + side;
            if (!WorldGen.InWorld(face.X, face.Y, 5) || WorldGen.SolidTile(face.X, face.Y))
                continue;
            if (Collision.CanHitLine(eye, 1, 1, face.ToWorldCoordinates(8f, 8f), 1, 1))
                return true;
        }
        return false;
    }
}
