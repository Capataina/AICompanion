using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.PurposeFamilies.Gathering;

/// <summary>The bounded discovered vein when its job ends. World disappearance and
/// attributed native removals are separate; neither establishes item yield or pickup.</summary>
public readonly record struct DescribeOreJobEnd(ulong Tick, int JobId, string Reason,
    int Tracked, int Present, int Changed, int Missing, int Unobserved, int CompanionRemovals)
{
    public bool ObservedClear => Tracked > 0 && Missing == Tracked;

    public static DescribeOreJobEnd Capture(int jobId, string reason, HashSet<Point> tiles,
        int type, int companionRemovals)
    {
        int present = 0, changed = 0, missing = 0, unobserved = 0;
        foreach (Point point in tiles)
        {
            if (!WorldGen.InWorld(point.X, point.Y, 5)) { unobserved++; continue; }
            Tile tile = Main.tile[point.X, point.Y];
            if (!tile.HasTile) missing++;
            else if (tile.TileType == type) present++;
            else changed++;
        }
        return new(Main.GameUpdateCount, jobId, reason, tiles.Count, present, changed,
            missing, unobserved, companionRemovals);
    }
}
