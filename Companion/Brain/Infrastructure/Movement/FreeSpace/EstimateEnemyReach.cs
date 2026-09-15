#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// Whether an enemy can get from where it is to a tile, estimated cheaply and never proved: the
/// threat sense discounts a slime sealed under a ledge and counts a bat that can fly straight in,
/// and "sealed" is a question about the enemy's body rather than the orb's. A flyer is answered by
/// a flood over non-solid tiles. A walker is answered by a flood over tiles it can stand in, moving
/// sideways along a floor, climbing a few rows where the column above is open, and dropping any
/// distance. It over-approximates on purpose — a walker that can climb three rows here may only
/// climb two in the game — because the cost of a wrong "sealed" is an enemy discounted while it
/// arrives, and the cost of a wrong "reachable" is a slime that was never coming being watched.
/// </summary>
public static class EstimateEnemyReach
{
    private const int Budget = 3000;
    private const int ClimbRows = 3;
    private static readonly Point[] Sides = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    public static bool FlyerCanReach(ITileWorld world, Point from, Point to)
    {
        if (from == to) return true;
        var seen = new HashSet<Point>(CornerKey.Comparer) { from };
        var open = new Queue<Point>();
        open.Enqueue(from);
        while (open.Count > 0 && seen.Count < Budget)
        {
            Point at = open.Dequeue();
            foreach (Point side in Sides)
            {
                var next = new Point(at.X + side.X, at.Y + side.Y);
                if (!world.InWorld(next.X, next.Y) || OrbTerrain.Solid(world, next.X, next.Y) || !seen.Add(next)) continue;
                if (next == to) return true;
                open.Enqueue(next);
            }
        }
        return false;
    }

    /// <summary>A tile a walking body can occupy: open, with support under it (a block or a platform).</summary>
    private static bool Standable(ITileWorld world, Point tile)
        => world.InWorld(tile.X, tile.Y) && !OrbTerrain.Solid(world, tile.X, tile.Y) && world.Shape(tile.X, tile.Y + 1) != TileShape.Air;

    public static bool WalkerCanReach(ITileWorld world, Point from, Point to)
    {
        Point start = Land(world, from);
        Point goal = Land(world, to);
        if (start == goal) return true;
        var seen = new HashSet<Point>(CornerKey.Comparer) { start };
        var open = new Queue<Point>();
        open.Enqueue(start);
        while (open.Count > 0 && seen.Count < Budget)
        {
            Point at = open.Dequeue();
            foreach (int dx in new[] { -1, 1 })
            {
                // Step sideways onto a floor, climb up to ClimbRows where the way up is open, or step off and drop.
                for (int rise = 0; rise <= ClimbRows; rise++)
                {
                    var candidate = new Point(at.X + dx, at.Y - rise);
                    if (rise > 0 && OrbTerrain.Solid(world, at.X, at.Y - rise)) break;
                    if (!world.InWorld(candidate.X, candidate.Y) || OrbTerrain.Solid(world, candidate.X, candidate.Y)) continue;
                    Point landed = Land(world, candidate);
                    if (!seen.Add(landed)) continue;
                    if (landed == goal) return true;
                    open.Enqueue(landed);
                }
            }
        }
        return false;
    }

    /// <summary>Where a body released at a tile comes to rest: the first standable tile down its column, within a bound.</summary>
    private static Point Land(ITileWorld world, Point tile)
    {
        for (int drop = 0; drop < 64; drop++)
        {
            var at = new Point(tile.X, tile.Y + drop);
            if (Standable(world, at)) return at;
            if (OrbTerrain.Solid(world, at.X, at.Y)) return new Point(tile.X, tile.Y + drop - 1);
        }
        return tile;
    }
}
