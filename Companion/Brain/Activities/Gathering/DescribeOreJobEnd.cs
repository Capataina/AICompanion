using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.Activities.Gathering;

/// <summary>The bounded discovered vein when its job ends. World disappearance and
/// attributed native removals are separate; neither establishes item yield or pickup.
/// <paramref name="UntrackedNeighbours"/> counts ore of the job's type touching a tracked site that the
/// job never tracked: the vein flood stopped at its bound, or ore was placed beside the vein after the
/// job began. It is what separates a tracked portion observed clear from a vein observed clear.</summary>
public readonly record struct DescribeOreJobEnd(ulong Tick, int JobId, string Reason,
    int Tracked, int Present, int Changed, int Missing, int Unobserved, int CompanionRemovals, int UntrackedNeighbours = 0)
{
    public bool ObservedClear => Tracked > 0 && Missing == Tracked;

    /// <summary>Every tracked site is empty and no ore of the job's type touches any of them.</summary>
    public bool VeinObservedClear => ObservedClear && UntrackedNeighbours == 0;

    public static DescribeOreJobEnd Capture(int jobId, string reason, HashSet<Point> tiles,
        int type, int companionRemovals)
    {
        int present = 0, changed = 0, missing = 0, unobserved = 0;
        var untracked = new HashSet<Point>();
        foreach (Point point in tiles)
        {
            if (!WorldGen.InWorld(point.X, point.Y, 5)) { unobserved++; continue; }
            Tile tile = Main.tile[point.X, point.Y];
            if (!tile.HasTile) missing++;
            else if (tile.TileType == type) present++;
            else changed++;
            // The same eight-connected neighbourhood the vein flood walks, so "touching" means what it means there.
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    Point neighbour = new(point.X + dx, point.Y + dy);
                    if (tiles.Contains(neighbour) || !WorldGen.InWorld(neighbour.X, neighbour.Y, 5)) continue;
                    Tile other = Main.tile[neighbour.X, neighbour.Y];
                    if (other.HasTile && other.TileType == type) untracked.Add(neighbour);
                }
        }
        return new(Main.GameUpdateCount, jobId, reason, tiles.Count, present, changed,
            missing, unobserved, companionRemovals, untracked.Count);
    }
}
