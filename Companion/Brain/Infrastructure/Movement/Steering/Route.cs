#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// A route the steering follows: world points from where the body was when it was planned to
/// the goal, smoothed so that consecutive points see each other, and the index of the segment
/// the body is currently on. Smoothing is line-of-sight skipping with the contact's own swept
/// test — a waypoint is dropped while the straight segment past it keeps clearance — so the
/// body cuts corners a corner graph would otherwise zigzag around, and it is the one place the
/// swept test still runs on a route, because a long straight segment is what the grid edges'
/// usability theorem does not cover.
/// </summary>
public sealed class Route
{
    public readonly List<Vector2> Points;
    /// <summary>The segment the body is on: from <c>Points[Index]</c> to <c>Points[Index + 1]</c>.</summary>
    public int Index;
    public readonly long SearchId;
    public readonly int TerrainRevision;
    public readonly Rectangle Bounds;

    public Route(List<Vector2> points, long searchId, int terrainRevision)
    {
        Points = points;
        SearchId = searchId;
        TerrainRevision = terrainRevision;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (Vector2 p in points)
        {
            minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X);
            minY = MathF.Min(minY, p.Y); maxY = MathF.Max(maxY, p.Y);
        }
        // Two tiles around every point: the tiles a swept segment of the body's radius can read.
        int x0 = (int)MathF.Floor(minX / 16f) - 2, y0 = (int)MathF.Floor(minY / 16f) - 2;
        int x1 = (int)MathF.Floor(maxX / 16f) + 2, y1 = (int)MathF.Floor(maxY / 16f) + 2;
        Bounds = new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    public Vector2 Goal => Points[^1];
    public Vector2 Start => Points[0];
    public int Count => Points.Count;
    public bool OnLastSegment => Index >= Points.Count - 2;

    /// <summary>The route's length from the body's projection on its current segment to the goal.</summary>
    public float RemainingLength(Vector2 from) => RemainingLength(from, Index);

    /// <summary>The route's length from the body's projection on segment <paramref name="index"/> to the goal, for a caller carrying its own segment.</summary>
    public float RemainingLength(Vector2 from, int index)
    {
        if (Points.Count < 2) return 0f;
        int i = Math.Clamp(index, 0, Points.Count - 2);
        float length = Vector2.Distance(Project(from, Points[i], Points[i + 1]), Points[i + 1]);
        for (int j = i + 1; j < Points.Count - 1; j++) length += Vector2.Distance(Points[j], Points[j + 1]);
        return length;
    }

    /// <summary>Whether the world under the route is what it was planned over.</summary>
    public bool StillValid(ITileWorld world)
    {
        Rectangle bounds = Bounds;
        return world.ChangedSince(TerrainRevision, (x, y) => bounds.Contains(x, y)) == TerrainEditVerdict.Unchanged;
    }

    /// <summary>
    /// Skip waypoints while the straight segment past them keeps the body clear of walls, from the
    /// front, and keeps the clearance the skipped waypoints had. The second condition is what stops
    /// smoothing undoing the search: the search paid for the middle of a corridor through its edge
    /// cost, and a chord from one wall-hugging end of a corridor to the other is clear of walls
    /// while spending its whole length nearer them than the route it replaced. So a skip is
    /// refused where the chord, at the point standing in for a skipped waypoint, has less
    /// clearance than that waypoint had, by more than the half tile the corner lattice cannot
    /// resolve. A chord that merely crosses a corridor to reach the far side of a bend keeps its
    /// skip, because the waypoints it replaces were crossing too.
    /// </summary>
    public static List<Vector2> Smooth(ITileWorld world, IReadOnlyList<Vector2> raw)
    {
        // How far ahead the smoother looks for a skip; a route of hundreds of points would
        // otherwise cost the square of its length in swept tests, and a skip further than this
        // is a skip the next smoothing pass, taken from further along, will find anyway.
        const int Lookahead = 48;
        var smooth = new List<Vector2>();
        if (raw.Count == 0) return smooth;
        smooth.Add(raw[0]);
        int at = 0;
        while (at < raw.Count - 1)
        {
            int furthest = at + 1;
            int limit = Math.Min(raw.Count - 1, at + Lookahead);
            for (int j = limit; j > at + 1; j--)
            {
                if (CircleContact.SweptClear(world, raw[at], raw[j], OrbTerrain.Wall) && KeepsClearance(world, raw, at, j)) { furthest = j; break; }
            }
            smooth.Add(raw[furthest]);
            at = furthest;
        }
        return smooth;
    }

    /// <summary>The chord from <paramref name="from"/> to <paramref name="to"/> is at least as clear, at each skipped waypoint's
    /// share of the way, as that waypoint was, within half a tile.</summary>
    private static bool KeepsClearance(ITileWorld world, IReadOnlyList<Vector2> raw, int from, int to)
    {
        const float Tolerance = 0.5f;
        Vector2 a = raw[from], b = raw[to];
        for (int k = from + 1; k < to; k++)
        {
            float share = (k - from) / (float)(to - from);
            Vector2 sample = a + (b - a) * share;
            float had = ClearanceField.Shared.AtCorner(world, CornerGraph.NearestCorner(raw[k]));
            float has = ClearanceField.Shared.AtCorner(world, CornerGraph.NearestCorner(sample));
            if (has < had - Tolerance) return false;
        }
        return true;
    }

    public static Vector2 Project(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared <= 1e-6f) return a;
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f);
        return a + ab * t;
    }
}
