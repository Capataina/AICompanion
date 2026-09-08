#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Map;
using AICompanion.Brain.DecisionMatrix.Navigation;

namespace AICompanion.Map;

/// <summary>
/// Reveals on the map exactly what a torch would show: the air the light travels
/// through and the first solid face it lands on, never what is behind a wall. A
/// breadth-first flood from the torch's tile through non-solid tiles up to its reach,
/// each tile written to the world map with light falling off by distance. A tile whose
/// map entry changed is queued the way the game queues a changed tile of its own, so
/// only those tiles are redrawn on the next map pass; the full-map refresh is reserved
/// for the queue overflowing, which is the game's own fallback. It reads tiles rather
/// than the lighting engine, so it works the same when the companion is off screen,
/// which is when it matters.
/// </summary>
public static class TorchMapReveal
{
    private static readonly Queue<(Point tile, int dist)> queue = new();
    private static readonly HashSet<Point> seen = new();
    private static readonly Point[] Neighbours = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    public static void Reveal(Point origin, int reachTiles)
    {
        if (!WorldGen.InWorld(origin.X, origin.Y, reachTiles + 2))
            return;
        queue.Clear();
        seen.Clear();
        queue.Enqueue((origin, 0));
        seen.Add(origin);

        while (queue.Count > 0)
        {
            (Point p, int d) = queue.Dequeue();
            byte light = (byte)(255 * (1f - d / (float)(reachTiles + 1)));
            if (Main.Map.UpdateLighting(p.X, p.Y, light))
                QueueRedraw(p);
            if (NavGrid.IsSolid(p.X, p.Y) || d >= reachTiles)
                continue; // a solid face is lit but not passed through
            foreach (Point o in Neighbours)
            {
                var q = new Point(p.X + o.X, p.Y + o.Y);
                if (seen.Add(q))
                    queue.Enqueue((q, d + 1));
            }
        }
    }

    /// <summary>The game's own per-tile redraw queue, as WorldGen.UpdateMapTile fills it.</summary>
    private static void QueueRedraw(Point p)
    {
        if (MapHelper.numUpdateTile < MapHelper.maxUpdateTile - 1)
        {
            MapHelper.updateTileX[MapHelper.numUpdateTile] = (short)p.X;
            MapHelper.updateTileY[MapHelper.numUpdateTile] = (short)p.Y;
            MapHelper.numUpdateTile++;
        }
        else
        {
            Main.refreshMap = true;
        }
    }
}
