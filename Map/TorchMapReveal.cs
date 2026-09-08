#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Brain.Navigation;

namespace AICompanion.Map;

/// <summary>
/// Reveals on the map exactly what a torch would show: the air the light travels
/// through and the first solid face it lands on, never what is behind a wall. A
/// breadth-first flood from the torch's tile through non-solid tiles up to its reach,
/// each tile written to the world map with light falling off by distance, then the
/// map asked to redraw. It reads tiles rather than the lighting engine, so it works
/// the same when the companion is off screen, which is when it matters.
/// </summary>
public static class TorchMapReveal
{
    private static readonly Queue<(Point tile, int dist)> queue = new();
    private static readonly HashSet<Point> seen = new();

    public static void Reveal(Point origin, int reachTiles)
    {
        if (!WorldGen.InWorld(origin.X, origin.Y, reachTiles + 2))
            return;
        queue.Clear();
        seen.Clear();
        queue.Enqueue((origin, 0));
        seen.Add(origin);
        bool changed = false;

        while (queue.Count > 0)
        {
            (Point p, int d) = queue.Dequeue();
            byte light = (byte)(255 * (1f - d / (float)(reachTiles + 1)));
            changed |= Main.Map.UpdateLighting(p.X, p.Y, light);
            if (NavGrid.IsSolid(p.X, p.Y) || d >= reachTiles)
                continue; // a solid face is lit but not passed through
            foreach (Point q in new[] { new Point(p.X + 1, p.Y), new Point(p.X - 1, p.Y), new Point(p.X, p.Y + 1), new Point(p.X, p.Y - 1) })
            {
                if (seen.Contains(q))
                    continue;
                seen.Add(q);
                queue.Enqueue((q, d + 1));
            }
        }
        if (changed)
            Main.refreshMap = true;
    }
}
