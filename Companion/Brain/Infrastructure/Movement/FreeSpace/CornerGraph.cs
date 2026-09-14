#nullable enable

using System;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The graph the flood and the route search walk: nodes are tile corners, not tile centres, and
/// they are eight-connected. A corner is the reason a two-wide corridor is open: a twenty-pixel
/// body centred on a sixteen-pixel tile in a two-tile corridor overhangs each wall by two pixels,
/// so a centre grid would report every two-wide corridor closed, while the corner between the
/// two tiles sits in the corridor's middle with six pixels either side. A corner is usable when
/// the four tiles around it are free, which is exactly the set of tiles a circle of the body's
/// radius at that corner overlaps.
///
/// <para>Each edge is validated by the contact's own swept-circle test against the tiles it
/// crosses, never by the grid alone, because the diagonal between two corners passes a one-tile
/// step with 11.3 pixels of clearance at its midpoint and that is where the margin lives. An edge
/// costs its length times one plus a tunable over the clearance at its far end, so the search
/// prefers a corridor's middle over its walls.</para>
/// </summary>
public static class CornerGraph
{
    public static readonly Point[] Neighbours =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    public static Vector2 ToWorld(Point corner) => new(corner.X * 16f, corner.Y * 16f);

    /// <summary>The corner nearest a world point.</summary>
    public static Point NearestCorner(Vector2 point) => new((int)MathF.Round(point.X / 16f), (int)MathF.Round(point.Y / 16f));

    /// <summary>The four tiles a corner touches, which are the tiles the body overlaps when centred on it.</summary>
    public static bool Usable(ITileWorld world, Point corner)
        => OrbTerrain.Free(world, corner.X - 1, corner.Y - 1) && OrbTerrain.Free(world, corner.X, corner.Y - 1)
        && OrbTerrain.Free(world, corner.X - 1, corner.Y) && OrbTerrain.Free(world, corner.X, corner.Y);

    /// <summary>The corners of a tile: a body at any of them sits within the tile's own footprint.</summary>
    public static bool AnyCornerOf(Point tile, Func<Point, bool> test)
        => test(tile) || test(new Point(tile.X + 1, tile.Y)) || test(new Point(tile.X, tile.Y + 1)) || test(new Point(tile.X + 1, tile.Y + 1));

    /// <summary>
    /// The nearest usable corner to a point within a ring radius, preferring one the body can
    /// reach in a straight line from where it is: a start corner the body cannot slide to is a
    /// route that begins with a wall.
    /// </summary>
    public static Point? NearestUsable(ITileWorld world, Vector2 point, int radius, bool requireSweep = true)
    {
        Point centre = NearestCorner(point);
        Point? fallback = null;
        float fallbackDistance = float.PositiveInfinity;
        for (int ring = 0; ring <= radius; ring++)
        {
            Point? best = null;
            float bestDistance = float.PositiveInfinity;
            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                    var corner = new Point(centre.X + dx, centre.Y + dy);
                    if (!Usable(world, corner)) continue;
                    float distance = Vector2.DistanceSquared(point, ToWorld(corner));
                    if (requireSweep && !CircleContact.SweptClear(world, point, ToWorld(corner), OrbTerrain.Wall))
                    {
                        if (distance < fallbackDistance) { fallback = corner; fallbackDistance = distance; }
                        continue;
                    }
                    if (distance < bestDistance) { best = corner; bestDistance = distance; }
                }
            }
            if (best is Point found) return found;
        }
        return fallback;
    }

    public static float Clearance(ITileWorld world, Point corner) => ClearanceField.Shared.AtCorner(world, corner);

    /// <summary>
    /// Whether the body can travel from one usable corner to a neighbouring one in a straight line.
    /// Usability of both ends is the whole test, and that is a theorem rather than a shortcut: a
    /// cardinal edge's swept circle of radius ten lies entirely inside the six tiles the two
    /// corners' footprints cover, and a diagonal edge's swept circle reaches only two tiles outside
    /// the seven the footprints cover — the step tiles either side of it — and passes each at a
    /// tile's half-diagonal, 11.3 pixels, which is more than the radius. So every edge between two
    /// usable corners is swept-clear by construction, and running the swept test here was the
    /// cost that made a screen-sized flood take eighteen milliseconds a slice. The swept test is
    /// kept for what needs it: the long segments of smoothing and the joins from the body to its
    /// first corner and from the last corner to the goal.
    /// </summary>
    public static bool EdgeClear(ITileWorld world, Point from, Point to) => Usable(world, to);

    /// <summary>Length times one plus the corridor-middle preference over the far corner's clearance.</summary>
    public static float EdgeCost(ITileWorld world, Point from, Point to)
    {
        float length = Vector2.Distance(ToWorld(from), ToWorld(to));
        float clearance = MathF.Max(0.5f, Clearance(world, to));
        return length * (1f + Weights.CorridorMiddlePreference / clearance);
    }
}
