#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AICompanion.Companion.Brain.Infrastructure.Interactions.Chopping;

/// <summary>
/// Finds trees the companion can chop. A tree is identified by its bottom trunk
/// tile (the one the game routes every axe hit to via WorldGen.GetTreeBottom), so
/// "the tree the player is hitting" and "a tree the companion found" compare as
/// one tile coordinate. The shared tool query finds actual access or a reachable
/// working position using the companion's feet, independently of the scan origin.
/// </summary>
public static class TreeFinder
{
    /// <summary>A tree the companion can walk to: its bottom trunk tile and where to stand.</summary>
    public readonly record struct ChoppableTree(Point Bottom, Vector2 StandPosition, int FacingDirection);

    /// <summary>One cell of resumable discovery, before admission or nearest-target filtering.</summary>
    public static Point? TrunkAt(Point tile)
    {
        return WorldGen.InWorld(tile.X, tile.Y, 10) && IsTreeTile(tile.X, tile.Y)
            ? TrunkBottom(tile.X, tile.Y) : null;
    }

    /// <summary>
    /// The bottom trunk tile of the tree the player is currently swinging an axe at,
    /// or null. Reads the player's aim tile (Player.tileTargetX/Y), which is what the
    /// game's own axe code hits.
    /// </summary>
    public static Point? TreeUnderPlayerAxe(Player player)
    {
        if (player.itemAnimation <= 0 || player.HeldItem.axe <= 0)
            return null;
        int x = Player.tileTargetX, y = Player.tileTargetY;
        if (!WorldGen.InWorld(x, y) || !IsTreeTile(x, y))
            return null;
        return TrunkBottom(x, y);
    }

    /// <summary>
    /// The lowest trunk tile of the tree containing (<paramref name="x"/>, <paramref name="y"/>).
    /// WorldGen.GetTreeBottom walks down while the tile is trunk and returns the first tile
    /// that is not, which is the ground under the tree; the trunk bottom is one row above it.
    /// </summary>
    public static Point TrunkBottom(int x, int y)
    {
        WorldGen.GetTreeBottom(x, y, out int bx, out int by);
        return IsTreeTile(bx, by - 1) ? new Point(bx, by - 1) : new Point(bx, by);
    }

    /// <summary>
    /// Nearest admitted trunk to <paramref name="from"/> within <paramref name="radiusTiles"/>,
    /// excluding <paramref name="exclude"/> and requiring tool access from <paramref name="actorFeet"/>.
    /// The admission predicate receives the trunk before any route query runs.
    /// </summary>
    public static ChoppableTree? FindNearest(Vector2 from, Vector2 actorFeet, Observation.ReachSense reach, int radiusTiles,
        Point? exclude, System.Func<Point, bool>? accept = null)
    {
        int cx = (int)(from.X / 16f), cy = (int)(from.Y / 16f);
        ChoppableTree? best = null;
        float bestDist = float.MaxValue;
        var seen = new System.Collections.Generic.HashSet<Point>();

        for (int x = cx - radiusTiles; x <= cx + radiusTiles; x++)
        {
            for (int y = cy - radiusTiles; y <= cy + radiusTiles; y++)
            {
                if (!WorldGen.InWorld(x, y, 10) || !IsTreeTile(x, y))
                    continue;
                Point bottom = TrunkBottom(x, y);
                if (!seen.Add(bottom)) continue;
                if (exclude is Point ex && ex == bottom)
                    continue;
                float d = Vector2.DistanceSquared(from, bottom.ToWorldCoordinates());
                if (d >= bestDist) continue;
                if (accept != null && !accept(bottom)) continue;
                if (FindToolAccess.Approach(bottom, actorFeet, reach, out Vector2 stand)
                    != Infrastructure.Movement.Reachability.Reach.Yes) continue;
                bestDist = d;
                best = new ChoppableTree(bottom, stand, bottom.X * 16f + 8f >= stand.X ? 1 : -1);
            }
        }
        return best;
    }

    /// <summary>The same set the vanilla axe code treats as a tree: trunks, plus palm trees and cactus, which IsATreeTrunk leaves out.</summary>
    public static bool IsTreeType(int type)
        => Main.tileAxe[type] && (TileID.Sets.IsATreeTrunk[type] || type == TileID.PalmTree || type == TileID.Cactus);

    private static bool IsTreeTile(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        return tile.HasTile && IsTreeType(tile.TileType);
    }

}
