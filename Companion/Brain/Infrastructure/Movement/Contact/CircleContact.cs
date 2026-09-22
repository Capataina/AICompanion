#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The orb's one contact with terrain: a circle of <see cref="Radius"/> against the rectangle of
/// every solid tile its bounding box overlaps. This runs in the motor after the tick's velocity has
/// been decided and nowhere else, and it runs identically in the mod and in every headless tool,
/// which is what leaves the companion with one body rather than a native one and a portable one
/// whose disagreement had to be measured every tick.
///
/// <para>Why a circle and not the engine's box. A twenty-pixel box centred on a tile corner cannot
/// pass a one-tile diagonal step because its corner catches on the step's corner; a twenty-pixel
/// circle passes the same step with 2.6 pixels to spare, and only a box of sixteen or fewer would,
/// which is exactly the one-tile gap the orb must not fit. The size rule the circle satisfies is
/// therefore the acceptance test for the body: it fits every two-by-two gap in every direction and
/// no one-by-one gap in any.</para>
///
/// <para>What is a wall to the circle is decided by <see cref="Solid"/>: a full block, a half block
/// and every slope are solid, because the owner ruled that slopes are full tiles to this body; a
/// platform is passable; a closed door is a solid tile until the door interaction opens it. Liquid
/// is not a wall here at all — it is a wall to the planner and a hurt to the motor — so a body
/// knocked into water is pushed out of nothing and simply takes the damage.</para>
/// </summary>
public static class CircleContact
{
    /// <summary>Half the body's diameter, in pixels. <c>NPC.width</c> and <c>NPC.height</c> are
    /// <see cref="Diameter"/> and must stay equal to it: the engine's box is what enemies and
    /// projectiles hit, and a box a different size from the circle is a body that is hit where it
    /// is not and missed where it is.</summary>
    public const float Radius = 10f;
    public const float Diameter = Radius * 2f;

    /// <summary>The push-out iterates because resolving one tile can move the circle into another;
    /// this is the cap that keeps a body wedged between three tiles from spinning for ever, and a
    /// body still overlapping after it is reported through <see cref="Result.Wedged"/>.</summary>
    private const int MaxPasses = 6;

    /// <summary>A tile the circle collides with, under the owner's rules for this body.</summary>
    public static bool Solid(ITileWorld world, int x, int y)
    {
        if (!world.InWorld(x, y)) return true;
        TileShape shape = world.Shape(x, y);
        if (shape == TileShape.Air) return false;
        // Two readings, and the body needs both to say "passable" for the same reason. A platform is
        // passable to the orb by the owner's ruling, whatever else a world says about it, so the shape
        // alone settles it; and a platform keeps its fall-through behaviour after hammering gives it a
        // slope or half shape, which no shape compare can see, so the world's own flag settles that.
        //
        // The shape clause exists so the ruling cannot be lost through a world that reports a platform
        // without the flag. No world in this tree produces that pair — `GameTileWorld.Shape` returns
        // Platform only where `PassThrough` is true, `TextTileWorld` maps '=' to Platform with
        // PassThrough true, and the capture decorator forwards both answers to what it wraps — so the
        // clause changes no behaviour today. It is here because the
        // combination is representable through `ITileWorld` and is already named elsewhere in the tree
        // (the recorder writes "solid-platform" for exactly it), and a body that collides with a
        // platform is the ruling silently reversed by a world nobody was looking at.
        return shape != TileShape.Platform && !world.PassThrough(x, y);
    }

    /// <summary>What one resolution did to the body.</summary>
    public readonly record struct Result(bool Touched, bool Wedged, Vector2 Normal);

    /// <summary>
    /// Move the circle out of every solid tile it overlaps and kill the part of the velocity that
    /// points into each one, leaving the part along the wall so the body slides. <paramref
    /// name="centre"/> and <paramref name="velocity"/> are both rewritten.
    /// </summary>
    public static Result Resolve(ITileWorld world, ref Vector2 centre, ref Vector2 velocity)
        => Resolve(world, ref centre, ref velocity, static (w, x, y) => Solid(w, x, y));

    /// <summary>The same resolution against a caller's own wall rule, which is how a headless
    /// instrument asks the contact about a world it is drawing rather than the game's.</summary>
    public static Result Resolve(ITileWorld world, ref Vector2 centre, ref Vector2 velocity, Func<ITileWorld, int, int, bool> wall)
    {
        bool touched = false;
        Vector2 lastNormal = Vector2.Zero;
        for (int pass = 0; pass < MaxPasses; pass++)
        {
            bool moved = false;
            int x0 = (int)MathF.Floor((centre.X - Radius) / 16f), x1 = (int)MathF.Floor((centre.X + Radius) / 16f);
            int y0 = (int)MathF.Floor((centre.Y - Radius) / 16f), y1 = (int)MathF.Floor((centre.Y + Radius) / 16f);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (!wall(world, x, y)) continue;
                    if (!PushOut(world, wall, x, y, ref centre, out Vector2 normal, out float depth)) continue;
                    touched = moved = true;
                    lastNormal = normal;
                    // Only the component into the wall dies; the component along it is the slide.
                    float into = Vector2.Dot(velocity, normal);
                    if (into < 0f) velocity -= normal * into;
                }
            }
            if (!moved) return new Result(touched, false, lastNormal);
        }
        return new Result(touched, Overlaps(world, centre, wall), lastNormal);
    }

    /// <summary>
    /// Push the circle out of one tile's rectangle if it overlaps it. The normal is the direction
    /// from the rectangle's closest point to the centre. When the centre is inside the rectangle
    /// there is no closest-point normal, so the push is along the least penetration among the
    /// sides whose neighbouring tile is open; that is the case of a block placed on the orb or a
    /// spawn inside terrain, and it is why the push cannot be written as the closest-point rule
    /// alone. Sides with a wall behind them are considered only when every side has one, because
    /// a push into the next wall tile is resolved by that tile in turn and a column of them walks
    /// the body along the wall rather than out of it.
    /// </summary>
    private static bool PushOut(ITileWorld world, Func<ITileWorld, int, int, bool> wall, int tx, int ty, ref Vector2 centre, out Vector2 normal, out float depth)
    {
        float left = tx * 16f, top = ty * 16f, right = left + 16f, bottom = top + 16f;
        float cx = Math.Clamp(centre.X, left, right), cy = Math.Clamp(centre.Y, top, bottom);
        float dx = centre.X - cx, dy = centre.Y - cy;
        float distanceSquared = dx * dx + dy * dy;
        if (distanceSquared >= Radius * Radius) { normal = Vector2.Zero; depth = 0f; return false; }
        if (distanceSquared > 1e-6f)
        {
            float distance = MathF.Sqrt(distanceSquared);
            normal = new Vector2(dx / distance, dy / distance);
            depth = Radius - distance;
        }
        else
        {
            Span<float> penetration = stackalloc float[] { centre.X - left, right - centre.X, centre.Y - top, bottom - centre.Y };
            Span<bool> open = stackalloc bool[] { !wall(world, tx - 1, ty), !wall(world, tx + 1, ty), !wall(world, tx, ty - 1), !wall(world, tx, ty + 1) };
            int chosen = -1;
            for (int pass = 0; pass < 2 && chosen < 0; pass++)
                for (int side = 0; side < 4; side++)
                    if ((pass == 1 || open[side]) && (chosen < 0 || penetration[side] < penetration[chosen]))
                        chosen = side;
            normal = chosen switch { 0 => new Vector2(-1f, 0f), 1 => new Vector2(1f, 0f), 2 => new Vector2(0f, -1f), _ => new Vector2(0f, 1f) };
            depth = penetration[chosen] + Radius;
        }
        centre += normal * depth;
        return true;
    }

    /// <summary>Whether the circle at <paramref name="centre"/> overlaps any solid tile.</summary>
    public static bool Overlaps(ITileWorld world, Vector2 centre)
        => Overlaps(world, centre, static (w, x, y) => Solid(w, x, y));

    public static bool Overlaps(ITileWorld world, Vector2 centre, Func<ITileWorld, int, int, bool> wall)
    {
        int x0 = (int)MathF.Floor((centre.X - Radius) / 16f), x1 = (int)MathF.Floor((centre.X + Radius) / 16f);
        int y0 = (int)MathF.Floor((centre.Y - Radius) / 16f), y1 = (int)MathF.Floor((centre.Y + Radius) / 16f);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (wall(world, x, y) && DistanceSquaredToTile(x, y, centre) < Radius * Radius)
                    return true;
        return false;
    }

    /// <summary>
    /// How far the circle's edge is from the nearest solid tile, in pixels: positive is free
    /// space, zero is touching, negative is overlap by that much. Searched a tile beyond the
    /// circle's own footprint, so a body one tile from a wall reads its distance rather than the
    /// cap; further than that the answer is the cap, which is all a consumer needs to know.
    /// </summary>
    public static float Clearance(ITileWorld world, Vector2 centre)
    {
        const float Reach = 32f;
        float best = Reach;
        int x0 = (int)MathF.Floor((centre.X - Radius - Reach) / 16f), x1 = (int)MathF.Floor((centre.X + Radius + Reach) / 16f);
        int y0 = (int)MathF.Floor((centre.Y - Radius - Reach) / 16f), y1 = (int)MathF.Floor((centre.Y + Radius + Reach) / 16f);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (Solid(world, x, y))
                    best = MathF.Min(best, MathF.Sqrt(DistanceSquaredToTile(x, y, centre)) - Radius);
        return best;
    }

    /// <summary>Whether the circle overlaps a tile the predicate accepts at all, counting a touch
    /// at the boundary; this is the liquid test, where a wet tile is a region and not a wall.</summary>
    public static bool Touches(Vector2 centre, Func<int, int, bool> tile)
    {
        int x0 = (int)MathF.Floor((centre.X - Radius) / 16f), x1 = (int)MathF.Floor((centre.X + Radius) / 16f);
        int y0 = (int)MathF.Floor((centre.Y - Radius) / 16f), y1 = (int)MathF.Floor((centre.Y + Radius) / 16f);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (tile(x, y) && DistanceSquaredToTile(x, y, centre) < Radius * Radius)
                    return true;
        return false;
    }

    /// <summary>
    /// Whether the circle can travel from <paramref name="from"/> to <paramref name="to"/> in a
    /// straight line without meeting a wall: the distance from the segment to every wall tile's
    /// rectangle along the way stays at least the radius. This is the edge test the corner graph
    /// runs and the line-of-sight test the smoother runs, and it is continuous on purpose: the
    /// diagonal between two corners passes a one-tile step with 11.3 pixels of clearance at its
    /// midpoint, and a test that only looked at the tiles under the endpoints would never see it.
    /// </summary>
    public static bool SweptClear(ITileWorld world, Vector2 from, Vector2 to, Func<ITileWorld, int, int, bool> wall)
    {
        float minX = MathF.Min(from.X, to.X) - Radius, maxX = MathF.Max(from.X, to.X) + Radius;
        float minY = MathF.Min(from.Y, to.Y) - Radius, maxY = MathF.Max(from.Y, to.Y) + Radius;
        int x0 = (int)MathF.Floor(minX / 16f), x1 = (int)MathF.Floor(maxX / 16f);
        int y0 = (int)MathF.Floor(minY / 16f), y1 = (int)MathF.Floor(maxY / 16f);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (wall(world, x, y) && SegmentDistanceSquaredToTile(x, y, from, to) < Radius * Radius)
                    return false;
        return true;
    }

    public static bool SweptClear(ITileWorld world, Vector2 from, Vector2 to)
        => SweptClear(world, from, to, static (w, x, y) => Solid(w, x, y));

    private static float DistanceSquaredToTile(int tx, int ty, Vector2 p)
    {
        float left = tx * 16f, top = ty * 16f;
        float cx = Math.Clamp(p.X, left, left + 16f), cy = Math.Clamp(p.Y, top, top + 16f);
        float dx = p.X - cx, dy = p.Y - cy;
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// The squared distance between a segment and a tile's rectangle: zero when the segment enters
    /// the rectangle, otherwise the least of the distances from the segment to the four edges,
    /// which is the standard segment-to-segment distance taken four times.
    /// </summary>
    private static float SegmentDistanceSquaredToTile(int tx, int ty, Vector2 a, Vector2 b)
    {
        float left = tx * 16f, top = ty * 16f, right = left + 16f, bottom = top + 16f;
        if (SegmentEntersRectangle(a, b, left, top, right, bottom)) return 0f;
        Vector2 tl = new(left, top), tr = new(right, top), bl = new(left, bottom), br = new(right, bottom);
        float best = SegmentDistanceSquared(a, b, tl, tr);
        best = MathF.Min(best, SegmentDistanceSquared(a, b, tr, br));
        best = MathF.Min(best, SegmentDistanceSquared(a, b, br, bl));
        best = MathF.Min(best, SegmentDistanceSquared(a, b, bl, tl));
        return best;
    }

    /// <summary>Liang–Barsky clipping of the segment against the rectangle: any surviving parameter
    /// range means the segment passes through the rectangle's interior or boundary.</summary>
    private static bool SegmentEntersRectangle(Vector2 a, Vector2 b, float left, float top, float right, float bottom)
    {
        float t0 = 0f, t1 = 1f;
        float dx = b.X - a.X, dy = b.Y - a.Y;
        if (!Clip(-dx, a.X - left, ref t0, ref t1)) return false;
        if (!Clip(dx, right - a.X, ref t0, ref t1)) return false;
        if (!Clip(-dy, a.Y - top, ref t0, ref t1)) return false;
        if (!Clip(dy, bottom - a.Y, ref t0, ref t1)) return false;
        return true;
    }

    private static bool Clip(float p, float q, ref float t0, ref float t1)
    {
        if (p == 0f) return q >= 0f;
        float r = q / p;
        if (p < 0f)
        {
            if (r > t1) return false;
            if (r > t0) t0 = r;
        }
        else
        {
            if (r < t0) return false;
            if (r < t1) t1 = r;
        }
        return true;
    }

    /// <summary>The squared distance between two segments in the plane.</summary>
    private static float SegmentDistanceSquared(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
    {
        if (SegmentsIntersect(p1, q1, p2, q2)) return 0f;
        float best = PointSegmentDistanceSquared(p1, p2, q2);
        best = MathF.Min(best, PointSegmentDistanceSquared(q1, p2, q2));
        best = MathF.Min(best, PointSegmentDistanceSquared(p2, p1, q1));
        best = MathF.Min(best, PointSegmentDistanceSquared(q2, p1, q1));
        return best;
    }

    private static float PointSegmentDistanceSquared(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared <= 1e-9f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f);
        Vector2 closest = a + ab * t;
        return Vector2.DistanceSquared(p, closest);
    }

    private static bool SegmentsIntersect(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
    {
        float d1 = Cross(q2 - p2, p1 - p2), d2 = Cross(q2 - p2, q1 - p2);
        float d3 = Cross(q1 - p1, p2 - p1), d4 = Cross(q1 - p1, q2 - p1);
        if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f))) return true;
        return (d1 == 0f && OnSegment(p2, q2, p1)) || (d2 == 0f && OnSegment(p2, q2, q1))
            || (d3 == 0f && OnSegment(p1, q1, p2)) || (d4 == 0f && OnSegment(p1, q1, q2));
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
        => p.X >= MathF.Min(a.X, b.X) && p.X <= MathF.Max(a.X, b.X) && p.Y >= MathF.Min(a.Y, b.Y) && p.Y <= MathF.Max(a.Y, b.Y);
}
