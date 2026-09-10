using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.SharedMovementSystem;

namespace AICompanion.Companion.Brain.WorldInteractions.WorldProtection;

/// <summary>
/// Bed-anchored protection for autonomous edits. Terraria's housing check also requires
/// walls and a minimum room size, so its final housing verdict cannot protect unfinished
/// homes. This bounded room walk uses its solid/door boundary semantics without mutating
/// WorldGen's global housing scratch state or requiring a light in an intentionally dark room.
/// </summary>
public static class ProtectCompanionHomes
{
    private static readonly List<Rectangle> rooms = new();
    private static int revision = -1;
    private static ulong refreshed;
    private static Point lastPlayer, lastCompanion;
    private static int world;

    public static void Refresh(Vector2 player, Vector2 companion)
    {
        Point p = player.ToTileCoordinates(), c = companion.ToTileCoordinates();
        if (world == Main.worldID && revision == TerrainChanges.Revision && Main.GameUpdateCount - refreshed < 60
            && Math.Abs(p.X - lastPlayer.X) + Math.Abs(p.Y - lastPlayer.Y) < 16
            && Math.Abs(c.X - lastCompanion.X) + Math.Abs(c.Y - lastCompanion.Y) < 16) return;
        rooms.Clear();
        world = Main.worldID;
        revision = TerrainChanges.Revision;
        refreshed = Main.GameUpdateCount;
        lastPlayer = p; lastCompanion = c;
        var beds = new HashSet<Point>();
        Scan(p, beds); Scan(c, beds);
        foreach (Point bed in beds) rooms.Add(RoomAround(bed));
    }

    private static void Scan(Point centre, HashSet<Point> beds)
    {
        // Covers the largest configured activity neighbourhood; only active actors' vicinity
        // is scanned, never the whole world, and multi-tile beds are deduplicated by their room.
        const int radius = 144;
        for (int x = Math.Max(5, centre.X - radius); x <= Math.Min(Main.maxTilesX - 6, centre.X + radius); x++)
            for (int y = Math.Max(5, centre.Y - radius); y <= Math.Min(Main.maxTilesY - 6, centre.Y + radius); y++)
            {
                Tile tile = Main.tile[x, y];
                if (tile.HasTile && tile.TileType == TileID.Beds)
                    beds.Add(new Point(x - tile.TileFrameX / 18 % 4, y - tile.TileFrameY / 18 % 2));
            }
    }

    private static Rectangle RoomAround(Point bed)
    {
        var seen = new HashSet<Point>();
        var queue = new Queue<Point>();
        queue.Enqueue(bed); seen.Add(bed);
        int left = bed.X, right = bed.X + 3, top = bed.Y, bottom = bed.Y + 1;
        bool bounded = true;
        while (queue.Count > 0 && seen.Count < 4096)
        {
            Point p = queue.Dequeue();
            left = Math.Min(left, p.X); right = Math.Max(right, p.X);
            top = Math.Min(top, p.Y); bottom = Math.Max(bottom, p.Y);
            foreach (Point d in directions)
            {
                Point q = p + d;
                if (!WorldGen.InWorld(q.X, q.Y, 5) || Math.Abs(q.X - bed.X) > 80 || Math.Abs(q.Y - bed.Y) > 60)
                { bounded = false; continue; }
                if (!seen.Add(q)) continue;
                Tile t = Main.tile[q.X, q.Y];
                bool door = t.HasTile && (t.TileType == TileID.ClosedDoor || t.TileType == TileID.OpenDoor);
                if (t.HasTile && !t.IsActuated && (door || Main.tileSolid[t.TileType] && !Main.tileSolidTop[t.TileType]))
                {
                    left = Math.Min(left, q.X); right = Math.Max(right, q.X);
                    top = Math.Min(top, q.Y); bottom = Math.Max(bottom, q.Y);
                    continue;
                }
                queue.Enqueue(q);
            }
        }
        // An open/oversized structure has no provable enclosing room. Protect its bed vicinity
        // conservatively instead of interpreting the whole connected cave as a house.
        if (!bounded || queue.Count > 0) return new Rectangle(bed.X - 16, bed.Y - 12, 36, 26);
        return new Rectangle(left - 1, top - 1, right - left + 3, bottom - top + 3);
    }

    private static readonly Point[] directions = { new(-1, 0), new(1, 0), new(0, -1), new(0, 1) };
    public static bool IsProtected(Point tile)
    {
        foreach (Rectangle room in rooms) if (room.Contains(tile)) return true;
        return false;
    }
    public static void Reset() { rooms.Clear(); revision = -1; refreshed = 0; }
}
