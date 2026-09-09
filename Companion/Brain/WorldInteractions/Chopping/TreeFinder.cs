#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AICompanion.Companion.Brain.WorldInteractions.Chopping;

/// <summary>
/// Finds trees the companion can chop. A tree is identified by its bottom trunk
/// tile (the one the game routes every axe hit to via WorldGen.GetTreeBottom), so
/// "the tree the player is hitting" and "a tree the companion found" compare as
/// one tile coordinate. Walkability is judged at the trunk's foot: a standing
/// spot two tiles to one side must have air for a body and ground under it.
/// </summary>
public static class TreeFinder
{
    /// <summary>A tree the companion can walk to: its bottom trunk tile and where to stand.</summary>
    public readonly record struct ChoppableTree(Point Bottom, Vector2 StandPosition, int FacingDirection);

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
    /// Nearest choppable tree to <paramref name="from"/> within <paramref name="radiusTiles"/>,
    /// excluding the tree whose bottom is <paramref name="exclude"/>. Scans columns outward
    /// so the first hit in each column is the closest trunk there.
    /// </summary>
    public static ChoppableTree? FindNearest(Vector2 from, int radiusTiles, Point? exclude)
    {
        int cx = (int)(from.X / 16f), cy = (int)(from.Y / 16f);
        ChoppableTree? best = null;
        float bestDist = float.MaxValue;

        for (int x = cx - radiusTiles; x <= cx + radiusTiles; x++)
        {
            for (int y = cy - radiusTiles; y <= cy + radiusTiles; y++)
            {
                if (!WorldGen.InWorld(x, y, 10) || !IsTreeTile(x, y))
                    continue;
                Point bottom = TrunkBottom(x, y);
                if (exclude is Point ex && ex == bottom)
                    continue;

                ChoppableTree? tree = Approach(bottom);
                if (tree is not ChoppableTree t)
                    continue;
                float d = Vector2.DistanceSquared(from, t.StandPosition);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = t;
                }
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

    /// <summary>Pick a standing spot beside the trunk: left first, then right.</summary>
    private static ChoppableTree? Approach(Point bottom)
    {
        foreach (int side in new[] { -1, 1 })
        {
            int sx = bottom.X + side * 2;
            if (!WorldGen.InWorld(sx, bottom.Y, 10))
                continue;
            bool bodyClear = !WorldGen.SolidTile(sx, bottom.Y) && !WorldGen.SolidTile(sx, bottom.Y - 1) && !WorldGen.SolidTile(sx, bottom.Y - 2);
            bool groundBelow = WorldGen.SolidTile(sx, bottom.Y + 1) || Main.tile[sx, bottom.Y + 1].IsHalfBlock || WorldGen.SolidTile(sx, bottom.Y + 2);
            if (bodyClear && groundBelow)
            {
                Vector2 stand = new(sx * 16f + 8f, (bottom.Y + 1) * 16f);
                return new ChoppableTree(bottom, stand, -side);
            }
        }
        return null;
    }
}
