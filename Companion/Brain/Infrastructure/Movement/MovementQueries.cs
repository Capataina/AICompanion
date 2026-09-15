#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The movement core's public read surface, and the one place the live world is plugged in.
/// Brain systems ask this façade about geometry and free space instead of coupling to the
/// graph or the search; the game sets <see cref="World"/> at load, a headless tool sets it to
/// the world it draws.
/// </summary>
public static class MovementQueries
{
    private static ITileWorld? world;
    public static ITileWorld World
    {
        get => world ?? throw new InvalidOperationException("no tile world is plugged in; the mod sets it at load and a tool sets it for its scene");
        set => world = value;
    }

    /// <summary>Whether a tile world is plugged in, for a sense that also runs in tools which update it with no world behind it.</summary>
    public static bool HasWorld => world != null;

    /// <summary>The tile a world point is in.</summary>
    public static Point Tile(Vector2 point) => new((int)MathF.Floor(point.X / 16f), (int)MathF.Floor(point.Y / 16f));
    /// <summary>
    /// The tile a standing body occupies, from its feet: a body resting on a floor has its feet exactly on
    /// the tile boundary, so <see cref="Tile"/> of that point is the solid row under it, which no body is in
    /// and no flood ever holds. One pixel up is the lowest row the body fills. The player and every walking
    /// enemy still stand, and the orb resting beside a standing player sits in this same cell, so it is the
    /// cell every "where he is" question about a grounded body means.
    /// </summary>
    public static Point FeetTile(Vector2 bottom) => Tile(new Vector2(bottom.X, bottom.Y - 1f));
    public static Vector2 TileCentre(Point tile) => new(tile.X * 16f + 8f, tile.Y * 16f + 8f);
    public static Vector2 CornerWorld(Point corner) => CornerGraph.ToWorld(corner);

    /// <summary>A wall to the orb's contact: a full block, a half block, a slope or a closed door; never a platform.</summary>
    public static bool IsSolidForOrb(int x, int y) => OrbTerrain.Solid(World, x, y);
    /// <summary>The name the map reveal reads: a tile light stops at.</summary>
    public static bool IsBlock(int x, int y) => IsSolidForOrb(x, y);
    /// <summary>Free for the orb under this tick's immunities: not solid and not a forbidden liquid.</summary>
    public static bool IsFreeForOrb(int x, int y) => OrbTerrain.Free(World, x, y);
    public static bool IsWet(int x, int y) => World.InWorld(x, y) && World.LiquidAmount(x, y) > 0;
    /// <summary>A tile a dropped item comes to rest on: anything with a shape, platforms included, because an item lands on a platform.</summary>
    public static bool IsSupport(int x, int y) => World.InWorld(x, y) && World.Shape(x, y) != TileShape.Air;
    public static bool IsLiquid(int x, int y) => IsWet(x, y);
    public static bool IsLava(int x, int y) => World.Lava(x, y);

    /// <summary>Distance from a free tile to the nearest wall, in tiles, capped; zero for a wall.</summary>
    public static float Clearance(int x, int y) => ClearanceField.Shared.At(World, x, y);
    /// <summary>The clearance under a world point: the least of the four tiles around its nearest corner.</summary>
    public static float ClearanceAt(Vector2 point) => ClearanceField.Shared.AtCorner(World, CornerGraph.NearestCorner(point));

    /// <summary>Whether the body fits centred on this corner: its four tiles are free.</summary>
    public static bool IsUsableCorner(Point corner) => CornerGraph.Usable(World, corner);
    /// <summary>Whether the body can hover inside this tile: one of its corners is usable.</summary>
    public static bool IsHoverable(Point tile) => CornerGraph.AnyCornerOf(tile, c => CornerGraph.Usable(World, c));
    /// <summary>The nearest corner the body fits at, within a ring radius of the point, or null.</summary>
    public static Point? NearestUsableCorner(Vector2 point, int radius = 2, bool requireSweep = true) => CornerGraph.NearestUsable(World, point, radius, requireSweep);

    /// <summary>The nearest hoverable tile to a tile within a ring radius, or null; the accept test narrows it.</summary>
    public static Point? NearestHoverable(Point around, int radius = 3, Func<Point, bool>? accept = null)
    {
        for (int ring = 0; ring <= radius; ring++)
        {
            Point? best = null;
            int bestDistance = int.MaxValue;
            for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                    var tile = new Point(around.X + dx, around.Y + dy);
                    if (!IsHoverable(tile) || (accept != null && !accept(tile))) continue;
                    int distance = dx * dx + dy * dy;
                    if (distance < bestDistance) { best = tile; bestDistance = distance; }
                }
            if (best != null) return best;
        }
        return null;
    }

    /// <summary>The point the body hovers at inside a tile: the tile's usable corner nearest its centre.</summary>
    public static Vector2 HoverPoint(Point tile)
    {
        Vector2 centre = TileCentre(tile);
        Vector2 best = centre;
        float bestDistance = float.PositiveInfinity;
        foreach (Point corner in new[] { tile, new Point(tile.X + 1, tile.Y), new Point(tile.X, tile.Y + 1), new Point(tile.X + 1, tile.Y + 1) })
        {
            if (!IsUsableCorner(corner)) continue;
            float distance = Vector2.DistanceSquared(centre, CornerGraph.ToWorld(corner));
            if (distance < bestDistance) { best = CornerGraph.ToWorld(corner); bestDistance = distance; }
        }
        return best;
    }

    /// <summary>Estimates of what an enemy body can reach, for the threat sense; never proofs.</summary>
    public static bool WalkerCanReach(Point from, Point to) => EstimateEnemyReach.WalkerCanReach(World, from, to);
    public static bool FlyerCanReach(Point from, Point to) => EstimateEnemyReach.FlyerCanReach(World, from, to);
}
