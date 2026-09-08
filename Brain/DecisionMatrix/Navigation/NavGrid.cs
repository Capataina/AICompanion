#nullable enable

using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// The world as the navigator sees it: a tile is a node when a body one tile wide and
/// three tall can stand on it (feet on the tile, three clear tiles above, solid or a
/// platform beneath). Edges come from what the companion can do with its own legs,
/// walk, step, jump, drop and fall through a platform, and never from changing tiles:
/// no digging, no building.
/// </summary>
public static class NavGrid
{
    /// <summary>How many tiles up a jump reaches. From JumpVelocity -8.5 and NPC gravity 0.3: v²/2g ≈ 120 px, kept conservative.</summary>
    public const int JumpHeightTiles = 5;

    /// <summary>How many tiles across a running jump clears.</summary>
    public const int JumpGapTiles = 4;

    /// <summary>Furthest a drop is planned. NPCs take no fall damage; the limit keeps the search bounded.</summary>
    public const int MaxDropTiles = 40;

    public const int BodyHeightTiles = 3;

    public static bool IsSolid(int x, int y)
    {
        if (!WorldGen.InWorld(x, y, 5))
            return true;
        Tile t = Main.tile[x, y];
        return t.HasTile && Main.tileSolid[t.TileType] && !Main.tileSolidTop[t.TileType];
    }

    /// <summary>A platform or half block: something feet rest on that the body can also pass through.</summary>
    public static bool IsSupport(int x, int y)
    {
        if (!WorldGen.InWorld(x, y, 5))
            return false;
        Tile t = Main.tile[x, y];
        if (!t.HasTile)
            return false;
        return Main.tileSolid[t.TileType] || Main.tileSolidTop[t.TileType];
    }

    /// <summary>The support under feet at (x, y) is a platform or half block: the body can drop through it on purpose.</summary>
    public static bool IsPlatformUnder(int x, int y) => IsSupport(x, y + 1) && !IsSolid(x, y + 1);

    /// <summary>
    /// Any liquid but lava in this tile. Liquid is walkable but slow: the game halves an NPC's
    /// movement while wet, so a jump from inside it reaches about half as far, and the edge
    /// generator shrinks the jump envelope and raises the cost of every move that starts here.
    /// </summary>
    public static bool IsLiquid(int x, int y)
    {
        if (!WorldGen.InWorld(x, y, 5))
            return false;
        Tile t = Main.tile[x, y];
        return t.LiquidAmount > 0 && t.LiquidType != Terraria.ID.LiquidID.Lava;
    }

    /// <summary>Lava in this tile. The companion takes damage and is not lava-immune, so no node may hold it.</summary>
    public static bool IsLava(int x, int y)
    {
        if (!WorldGen.InWorld(x, y, 5))
            return false;
        Tile t = Main.tile[x, y];
        return t.LiquidAmount > 0 && t.LiquidType == Terraria.ID.LiquidID.Lava;
    }

    /// <summary>Feet at (x, y): the tile below supports, the body column is clear, and nothing in it is lava.</summary>
    public static bool IsStandable(int x, int y)
    {
        if (!IsSupport(x, y + 1))
            return false;
        for (int i = 0; i < BodyHeightTiles; i++)
            if (IsSolid(x, y - i) || IsLava(x, y - i))
                return false;
        return !IsLava(x, y + 1);
    }

    /// <summary>The body column at (x, y) is free of solid tiles; used for flight and jump arcs.</summary>
    public static bool IsBodyClear(int x, int y)
    {
        for (int i = 0; i < BodyHeightTiles; i++)
            if (IsSolid(x, y - i))
                return false;
        return true;
    }

    /// <summary>The feet tile of an entity: the tile containing the bottom-centre point, nudged up if inside ground.</summary>
    public static Point FeetTile(Vector2 bottom)
    {
        int x = (int)(bottom.X / 16f);
        int y = (int)((bottom.Y - 1f) / 16f);
        return new Point(x, y);
    }

    /// <summary>The nearest standable tile to a point, searched in a small box; null if the area is solid or air.</summary>
    public static Point? NearestStandable(Point around, int radius = 3)
    {
        if (IsStandable(around.X, around.Y))
            return around;
        Point? best = null;
        int bestD = int.MaxValue;
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                int x = around.X + dx, y = around.Y + dy;
                if (!IsStandable(x, y))
                    continue;
                int d = dx * dx + dy * dy;
                if (d < bestD)
                {
                    bestD = d;
                    best = new Point(x, y);
                }
            }
        return best;
    }

    /// <summary>World position of the feet for a tile: bottom-centre of the tile.</summary>
    public static Vector2 FeetWorld(Point tile) => new(tile.X * 16f + 8f, (tile.Y + 1) * 16f);
}
