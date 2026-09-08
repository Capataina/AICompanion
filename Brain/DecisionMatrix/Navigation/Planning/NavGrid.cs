#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// The world as the navigator sees it: a tile is a node when the real body (20 px wide,
/// 42 tall) has a place to stand with its feet in that tile, tested against the tiles'
/// shapes the way the game tests the player, sitting a little off-centre when a wall at
/// head height on one side and foot height on the other leaves room only there. Edges
/// come from what the companion can do with its own legs, walk, step, jump, drop and fall
/// through a platform, and never from changing tiles: no digging, no building.
/// </summary>
public static class NavGrid
{
    /// <summary>
    /// How many rows up the jump box looks. The full jump's apex from JumpVelocity -8.5 and NPC
    /// gravity 0.3 is v²/2g ≈ 120 px, seven and a half tiles, and the box reaches all of it; the
    /// arc simulation decides which of those tiles are landings, so the box being generous costs
    /// candidates and never invents an edge. At five it hid a platform the lowest real arc landed on.
    /// </summary>
    public const int JumpHeightTiles = 7;

    /// <summary>How many tiles across a running jump clears.</summary>
    public const int JumpGapTiles = 4;

    /// <summary>Furthest a drop is planned. NPCs take no fall damage; the limit keeps the search bounded.</summary>
    public const int MaxDropTiles = 40;

    public const int BodyHeightTiles = 3;

    /// <summary>
    /// Where the tiles come from: the game sets the live world at load, the replay tool sets a
    /// text scenario. Every tile question below goes through it and nothing else.
    /// </summary>
    public static ITileWorld World = null!;

    /// <summary>A full block: collided with from every side. A slope or half block is not this; ask <see cref="IsBlock"/> for anything the body cannot pass.</summary>
    public static bool IsSolid(int x, int y) => World.Shape(x, y) == TileShape.Solid;

    /// <summary>Anything but air and platforms: a full block, a half block or a slope, which a falling body lands on and a flood of air stops at.</summary>
    public static bool IsBlock(int x, int y) => World.Shape(x, y) is not (TileShape.Air or TileShape.Platform);

    /// <summary>Something feet rest on: any shape but air.</summary>
    public static bool IsSupport(int x, int y) => World.Shape(x, y) != TileShape.Air;

    /// <summary>The support under feet at (x, y) is a platform: the body can drop through it on purpose.</summary>
    public static bool IsPlatformUnder(int x, int y) => World.Shape(x, y + 1) == TileShape.Platform;

    /// <summary>
    /// A platform lies under some part of a body standing in <paramref name="pose"/> with its
    /// feet in <paramref name="feetRow"/>: the body is wider than a tile, so it stands on a
    /// platform whose column is not the one its centre is in, at the end of a platform
    /// staircase's tread, and pressing down there passes that platform all the same.
    /// </summary>
    public static bool IsPlatformUnder(BodyPhysics.Pose pose, int feetRow)
    {
        for (int c = (int)MathF.Floor(pose.Left / 16f); c <= (int)MathF.Floor((pose.Left + BodyPhysics.Width - 0.02f) / 16f); c++)
            if (IsPlatformUnder(c, feetRow))
                return true;
        return false;
    }

    /// <summary>
    /// Any liquid but lava in this tile. Liquid is walkable but slow: the game halves an NPC's
    /// movement while wet, so a jump from inside it reaches about half as far, and the edge
    /// generator shrinks the jump envelope and raises the cost of every move that starts here.
    /// </summary>
    public static bool IsLiquid(int x, int y) => World.Water(x, y);

    /// <summary>Lava in this tile. The companion takes damage and is not lava-immune, so a node holding it is priced, never free.</summary>
    public static bool IsLava(int x, int y) => World.Lava(x, y);

    /// <summary>Feet in tile (x, y): the body has a place to stand there and nothing in its column is lava.</summary>
    public static bool IsStandable(int x, int y) => IsStandable(x, y, allowLava: false);

    /// <summary>
    /// Feet in tile (x, y) with lava allowed in the column: the same test with the lava rule
    /// lifted, for a search that prices lava rather than refusing it.
    /// </summary>
    public static bool IsStandable(int x, int y, bool allowLava) => StandAt(x, y, allowLava) != null;

    /// <summary>
    /// Where the body stands with its feet in tile (x, y), or null: the physics test in
    /// <see cref="BodyPhysics.Stand"/>, plus the lava rule. On a slope the feet rest partway
    /// down the slope's own tile, so the slope tile is the node and its pose is below the
    /// tile's top; a walk step onto it is what the motor's own slope handling makes smooth.
    /// </summary>
    public static BodyPhysics.Pose? StandAt(int x, int y, bool allowLava)
    {
        if (!allowLava && LavaTilesAt(x, y) > 0)
            return null;
        return BodyPhysics.Stand(World, x, y);
    }

    /// <summary>
    /// The X, in world pixels, of the middle of the open run of columns around
    /// <paramref name="column"/> at the row the feet are on: columns the body is clear in
    /// with nothing solid beneath them, a few each way at most. A body dropping into a hole
    /// steers here rather than to a tile centre, because centred on one column of a two-wide
    /// shaft it still overhangs the lip by a couple of pixels and the game keeps it standing.
    /// </summary>
    public static float OpenSpanCentreX(int column, int feetRow, bool throughPlatform)
    {
        (int left, int right) = OpenSpan(column, feetRow, throughPlatform);
        return (left * 16f + (right + 1) * 16f) / 2f;
    }

    /// <summary>How many columns either way the open span beside a lip is read; the edge cache's invalidation box is sized from it.</summary>
    public const int OpenSpanReach = 3;

    /// <summary>
    /// The run of open columns around <paramref name="column"/> at the row the feet are on, as
    /// its first and last column: columns the body is clear in with nothing solid beneath them
    /// (or a platform beneath them, for a fall-through), <see cref="OpenSpanReach"/> each way at
    /// most. Where in this span the body falls decides what it lands on, so the planner tries
    /// its edges and its middle and the follower steers to the one the plan chose.
    /// </summary>
    public static (int left, int right) OpenSpan(int column, int feetRow, bool throughPlatform)
    {
        const int Reach = OpenSpanReach;
        // A drop wants the open air beside the lip; a fall-through wants the platform the body
        // is passing, because steering off a one-tile platform into open air loses the landing.
        bool Open(int c) => IsBodyClear(c, feetRow) && (throughPlatform ? IsPlatformUnder(c, feetRow) : !IsSupport(c, feetRow + 1));
        int left = column, right = column;
        while (left > column - Reach && Open(left - 1)) left--;
        while (right < column + Reach && Open(right + 1)) right++;
        return (left, right);
    }

    /// <summary>How many tiles of the body column and the support under it hold lava.</summary>
    public static int LavaTilesAt(int x, int y)
    {
        int n = IsLava(x, y + 1) ? 1 : 0;
        for (int i = 0; i < BodyHeightTiles; i++)
            if (IsLava(x, y - i))
                n++;
        return n;
    }

    /// <summary>The head row of a body standing at (x, y) is in liquid: it is drowning there.</summary>
    public static bool HeadSubmergedAt(int x, int y) => IsLiquid(x, y - BodyHeightTiles + 1);

    /// <summary>The body can occupy column x with its feet at the bottom of row y, at some sideways offset; used for flight and jump arcs.</summary>
    public static bool IsBodyClear(int x, int y) => BodyPhysics.FitsInColumn(World, x, y);

    /// <summary>The feet tile of an entity: the tile containing the bottom-centre point, nudged up if inside ground.</summary>
    public static Point FeetTile(Vector2 bottom)
    {
        int x = (int)(bottom.X / 16f);
        int y = (int)((bottom.Y - 1f) / 16f);
        return new Point(x, y);
    }

    /// <summary>The nearest standable tile to a point, searched in a small box; null if the area is solid or air.</summary>
    public static Point? NearestStandable(Point around, int radius = 3)
        => NearestStandable(around, radius, null);

    /// <summary>
    /// The nearest standable tile that also passes <paramref name="accept"/>, so a caller holding
    /// a reachable region can ask for the nearest tile it can actually get to.
    /// </summary>
    public static Point? NearestStandable(Point around, int radius, System.Func<Point, bool>? accept)
    {
        if (IsStandable(around.X, around.Y) && (accept == null || accept(around)))
            return around;
        Point? best = null;
        int bestD = int.MaxValue;
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                int x = around.X + dx, y = around.Y + dy;
                if (!IsStandable(x, y) || (accept != null && !accept(new Point(x, y))))
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

    /// <summary>
    /// World position of the feet for a tile: where the body actually rests there (off-centre
    /// in a tight column, partway down a slope), or the tile's bottom-centre when nothing
    /// stands there, so a target inside a slope steers to the slope's surface and not to a
    /// point under it.
    /// </summary>
    public static Vector2 FeetWorld(Point tile)
    {
        return BodyPhysics.Stand(World, tile.X, tile.Y) is BodyPhysics.Pose p
            ? new Vector2(p.CentreX, p.Bottom)
            : new Vector2(tile.X * 16f + 8f, (tile.Y + 1) * 16f);
    }
}
