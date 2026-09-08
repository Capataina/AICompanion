#nullable enable

using System;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// The companion body's movement numbers and the one-tick rules that use them, in the
/// navigation core so the motor, the reflex simulation, the planner's simulated jumps and
/// the replay tool all move the body by the same arithmetic. The horizontal rule is the
/// player's own shape (a small fixed gain per tick up to the walk speed, a larger fixed
/// loss when stopping or reversing); the vertical numbers are the game's NPC defaults.
/// The shape tests at the bottom are the game's collision read as geometry: a body is a
/// rectangle, a tile is a box, a half box or a triangle, and the body fits where the two
/// do not overlap, which is what lets a worldgen staircase of slopes be walked.
/// </summary>
public static class BodyPhysics
{
    public const int Width = 20;
    public const int Height = 42;

    public const float WalkSpeed = 3.5f;
    public const float JumpVelocity = -8.5f;

    /// <summary>Speed gained per tick toward the target; the player's runAcceleration scaled to this walk speed.</summary>
    public const float Acceleration = 0.08f * WalkSpeed / 3f;

    /// <summary>Speed lost per tick when stopping or reversing; the player's runSlowdown.</summary>
    public const float Slowdown = 0.2f;

    /// <summary>The game's NPC gravity and fall-speed cap (NPC.UpdateGravity defaults).</summary>
    public const float Gravity = 0.3f;
    public const float MaxFallSpeed = 10f;

    /// <summary>
    /// One tick of horizontal physics: toward <paramref name="target"/> by the acceleration when
    /// the current speed is on the target's side, by the slowdown when it is against it or the
    /// target is zero, never overshooting.
    /// </summary>
    public static float StepVelocity(float v, float target)
    {
        if (target == 0f || MathF.Sign(v) == -MathF.Sign(target))
        {
            float slowed = v - MathF.Sign(v) * Slowdown;
            v = MathF.Sign(slowed) != MathF.Sign(v) ? 0f : slowed;
            if (target == 0f)
                return v;
        }
        float next = v + MathF.Sign(target) * Acceleration;
        // Clamp to the target only once the speed is on the target's side: a magnitude test
        // alone snapped -3.2 straight to +1.75 on a reversal, which is the instant turn the
        // slowdown above exists to prevent.
        return MathF.Sign(next) == MathF.Sign(target) && MathF.Abs(next) > MathF.Abs(target) ? target : next;
    }

    /// <summary>One tick of vertical physics in the air.</summary>
    public static float StepFall(float vy) => MathF.Min(vy + Gravity, MaxFallSpeed);

    /// <summary>
    /// Jump velocity scale for a rise of so many tiles, the heights the fighter AI uses (-6 for
    /// two tiles, -7 for three, -8 for four) and the full jump above that; one tile is a step.
    /// </summary>
    public static float JumpScaleForTiles(int tiles) => tiles switch
    {
        <= 2 => 6f / -JumpVelocity,
        3 => 7f / -JumpVelocity,
        4 => 8f / -JumpVelocity,
        _ => 1f,
    };

    /// <summary>Vertical offset of a full jump from standing after so many ticks, from the jump velocity and gravity (negative is up).</summary>
    public static float JumpOffsetAt(int ticks) => JumpVelocity * ticks + Gravity / 2f * ticks * ticks;

    /// <summary>A place the body stands: its left edge and the pixel row its feet rest on, both in world pixels.</summary>
    public readonly record struct Pose(float Left, float Bottom)
    {
        public float CentreX => Left + Width / 2f;
    }

    /// <summary>
    /// The sideways positions tried for a body asked to stand in a tile column, centre first
    /// and then a little further off-centre each way, because the body is wider than a tile
    /// and a column with a wall at head height on one side and at foot height on the other
    /// still holds it shifted a few pixels; the game only cares that the rectangle fits.
    /// </summary>
    private static readonly float[] Offsets = { 0f, -2f, 2f, -4f, 4f, -6f, 6f, -8f, 8f };

    /// <summary>Touching counts as fitting, the way the game's own pushes of 0.01 px leave a body resting against a face.</summary>
    private const float Touch = 0.02f;

    /// <summary>
    /// Where the body stands with its feet in tile <paramref name="column"/>, <paramref name="row"/>,
    /// or null when nothing there holds it: the first sideways offset whose rectangle rests on a
    /// surface inside that feet row and overlaps no solid. A body on a slope rests partway down
    /// the slope's own tile, which is why the feet row of a slope is the slope tile itself.
    /// </summary>
    public static Pose? Stand(ITileWorld world, int column, int row)
    {
        foreach (float off in Offsets)
        {
            float left = column * 16f + 8f + off - Width / 2f;
            float? bottom = RestBottom(world, left, row);
            if (bottom is not float b || FeetRow(b) != row)
                continue;
            if (Fits(world, left, b))
                return new Pose(left, b);
        }
        return null;
    }

    /// <summary>
    /// The body can move from one standing pose to a neighbouring one along the straight line
    /// between them: it fits at every few pixels of the way. Along a slope the line is the
    /// slope's own surface, so a walk down a staircase of slopes passes under a ceiling slope
    /// that a body held at the upper pose's height would hit; for a one-tile step the line is
    /// the diagonal the step-up or step-down sweeps.
    /// </summary>
    public static bool CanSlide(ITileWorld world, Pose from, Pose to)
    {
        float dx = to.Left - from.Left, dy = to.Bottom - from.Bottom;
        int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Max(MathF.Abs(dx), MathF.Abs(dy)) / 4f));
        int rowLo = FeetRow(MathF.Min(from.Bottom, to.Bottom)), rowHi = FeetRow(MathF.Max(from.Bottom, to.Bottom));
        for (int i = 1; i < steps; i++)
        {
            float left = from.Left + dx * i / steps;
            float bottom = from.Bottom + dy * i / steps;
            // The body never sinks below the ground on the way: where the surface between the
            // two poses is higher than the straight line (a slope steeper than the line, the
            // lip of a step), it rides the surface; where it is lower it is in the air.
            for (int row = rowLo; row <= rowHi; row++)
                if (RestBottom(world, left, row) is float surface && surface < bottom)
                    bottom = surface;
            if (!Fits(world, left, bottom))
                return false;
        }
        return true;
    }

    /// <summary>The tile row a body with this bottom has its feet in: the row containing the last pixel above the bottom.</summary>
    public static int FeetRow(float bottom) => (int)MathF.Floor((bottom - 1f) / 16f);

    /// <summary>The body can occupy tile column <paramref name="column"/> with its feet at the bottom of row <paramref name="row"/>, at some sideways offset; the arc and drop checks ask this.</summary>
    public static bool FitsInColumn(ITileWorld world, int column, int row)
    {
        float bottom = (row + 1) * 16f;
        foreach (float off in Offsets)
            if (Fits(world, column * 16f + 8f + off - Width / 2f, bottom))
                return true;
        return false;
    }

    /// <summary>
    /// The highest surface under a body whose left edge is <paramref name="left"/>, among the
    /// tiles of <paramref name="row"/> and the row below it: a block or platform top, the middle
    /// of a half block, or the point of a floor slope's diagonal under the body's near edge,
    /// which is the game's own rule (SlopeCollision rests the body at the tile's top plus the
    /// edge's inset, and at the tile's top when the edge is outside the tile).
    /// </summary>
    public static float? RestBottom(ITileWorld world, float left, int row)
    {
        float right = left + Width;
        int x0 = (int)MathF.Floor(left / 16f), x1 = (int)MathF.Floor((right - Touch) / 16f);
        float? best = null;
        for (int y = row; y <= row + 1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float top = y * 16f;
                float? surface = world.Shape(x, y) switch
                {
                    TileShape.Air => null,
                    TileShape.Half => top + 8f,
                    TileShape.SolidLowerLeft => top + MathF.Max(0f, left - x * 16f),
                    TileShape.SolidLowerRight => top + MathF.Max(0f, x * 16f + 16f - right),
                    _ => top,
                };
                if (surface is float s && (best == null || s < best))
                    best = s;
            }
        }
        return best;
    }

    /// <summary>
    /// The body rectangle with this left edge and this bottom overlaps nothing solid: a full
    /// block anywhere in it, the lower half of a half block, the solid triangle of a slope.
    /// Platforms never block. This is the standing test; movement through a shape (a platform
    /// from below, a slope from its open side) is the game's collision's business, not this.
    /// </summary>
    public static bool Fits(ITileWorld world, float left, float bottom)
    {
        float right = left + Width, top = bottom - Height;
        int x0 = (int)MathF.Floor(left / 16f), x1 = (int)MathF.Floor((right - Touch) / 16f);
        int y0 = (int)MathF.Floor(top / 16f), y1 = (int)MathF.Floor((bottom - Touch) / 16f);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                TileShape shape = world.Shape(x, y);
                if (shape is TileShape.Air or TileShape.Platform)
                    continue;
                if (shape == TileShape.Solid)
                    return false;
                // The overlap of the body with this tile, in the tile's own 0..16 coordinates.
                float lx0 = MathF.Max(left, x * 16f) - x * 16f, lx1 = MathF.Min(right, x * 16f + 16f) - x * 16f;
                float ly0 = MathF.Max(top, y * 16f) - y * 16f, ly1 = MathF.Min(bottom, y * 16f + 16f) - y * 16f;
                bool hit = shape switch
                {
                    TileShape.Half => ly1 > 8f + Touch,
                    TileShape.SolidLowerLeft => ly1 > lx0 + Touch,
                    TileShape.SolidLowerRight => ly1 > 16f - lx1 + Touch,
                    TileShape.SolidUpperLeft => ly0 < 16f - lx0 - Touch,
                    TileShape.SolidUpperRight => ly0 < lx1 - Touch,
                    _ => true,
                };
                if (hit)
                    return false;
            }
        }
        return true;
    }
}
