#nullable enable

using System;

namespace AICompanion.Brain.SharedMovementSystem;

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
    /// <summary>
    /// The body's box in world pixels. These are the player's own hitbox
    /// (<c>Player.defaultWidth</c> and <c>defaultHeight</c>), not an NPC size and not a
    /// sprite size, because the companion is drawn as a player and is meant to fit
    /// wherever the player fits. <c>CompanionNPC.SetDefaults</c> declares the same two
    /// numbers on the real NPC, and the two declarations have to agree: the planner
    /// proves every move against this box while the game moves the other one, so a change
    /// made in one place and not the other plans routes the body cannot walk. Neither is
    /// a tunable.
    /// </summary>
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
    public static float StepFall(float vy, bool wet = false, int liquidKind = 0)
    {
        if (!wet)
            return MathF.Min(vy + Gravity, MaxFallSpeed);
        // Terraria's liquid paths slow vertical movement as well as horizontal movement. The
        // integer is TerrariaLiquidID at the adapter boundary (0 water, 1 lava, 2 honey, 3
        // shimmer), kept numeric here so the replay core remains game-free.
        return liquidKind switch
        {
            2 => MathF.Min(vy + 0.1f, 4f),
            3 => MathF.Min(vy + 0.15f, 5.5f),
            _ => MathF.Min(vy + 0.2f, 7f),
        };
    }

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

    /// <summary>
    /// The speed to ask the motor for when steering the body's centre onto <paramref name="targetX"/>
    /// in the air: full walk speed toward it, and nothing once the remaining distance is inside the
    /// stopping distance at the current speed (v² over twice the slowdown), so the body coasts onto
    /// the column instead of hunting across it at full speed and landing a tile beyond. The path
    /// follower and the planner's jump simulation share this rule, which is what lets the planner
    /// promise a landing tile the follower then reaches.
    /// </summary>
    public static float SteerToward(float targetX, float centreX, float vx, float tolerance = 2f)
    {
        float d = targetX - centreX;
        if (MathF.Abs(d) <= tolerance)
            return 0f;
        float stopping = vx * vx / (2f * Slowdown);
        if (MathF.Sign(d) == MathF.Sign(vx) && MathF.Abs(d) <= stopping)
            return 0f;
        return MathF.Sign(d) * WalkSpeed;
    }

    /// <summary>
    /// The jump the path follower makes, tick by tick: from a standing pose, a jump at
    /// <paramref name="scale"/> of the full velocity, steering toward the centre of
    /// <paramref name="steerColumn"/> every tick with SteerToward and the motor's own acceleration
    /// rule, under NPC gravity, with the displacement halved while the feet are in liquid the way the
    /// game halves a wet NPC's movement. Sideways the body is stopped by any shape it meets; upward it
    /// stops under a ceiling and falls; downward it lands on the first surface it crosses, checked in
    /// short steps so a fast fall cannot pass through a thin ledge. Returns the pose it lands in, or
    /// null if it never lands inside <paramref name="maxTicks"/>; <paramref name="ticks"/> is the
    /// flight time, which is what the edge costs. <paramref name="startVx"/> is the horizontal
    /// speed the body carries into the jump: zero from standing, the walk speed from a run-up,
    /// and the difference is about three tiles against seven, because the motor takes most of a
    /// second to reach walk speed from rest.
    /// </summary>
    public static Pose? SimulateJump(ITileWorld world, Pose from, float scale, float startVx, int steerColumn, int landingRow, int maxTicks, out int ticks)
        => SimulateJump(world, from, scale, startVx, steerColumn, landingRow, maxTicks, out ticks, null);

    /// <summary>One tick of a simulated jump after it has moved, handed to a trace callback by the replay tool.</summary>
    public readonly record struct JumpTick(int Tick, float Left, float Bottom, float Vx, float Vy, int FeetColumn, int FeetRow);

    /// <param name="landingRow">The feet row the caller wants; once the body is falling past it the flight is abandoned, because it can no longer land there.</param>
    public static Pose? SimulateJump(ITileWorld world, Pose from, float scale, float startVx, int steerColumn, int landingRow, int maxTicks, out int ticks, System.Action<JumpTick>? trace)
    {
        // The jump is the body's own tick rule driven by the jump's steering: an impulse on the
        // first tick, then the in-air steer toward the landing column every tick, and the body
        // is wherever BodyMotion says it is. A jump never asks to fall through a platform.
        var state = new BodyState(from.Left, from.Bottom, startVx, 0f, true);
        float targetCentre = steerColumn * 16f + 8f;
        float giveUpBelow = (landingRow + 2) * 16f;
        for (ticks = 1; ticks <= maxTicks; ticks++)
        {
            var controls = new Controls(SteerToward(targetCentre, state.CentreX, state.Vx), Jump: ticks == 1, JumpScale: scale);
            state = BodyMotion.Step(world, state, controls);
            trace?.Invoke(new JumpTick(ticks, state.Left, state.Bottom, state.Vx, state.Vy, state.FeetTile.X, state.FeetTile.Y));
            if (state.Stuck)
                return null;
            if (state.OnGround)
                return state.Pose;
            // Only a falling body has given up on the row: a jump upward starts below the line.
            if (state.Vy >= 0f && state.Bottom > giveUpBelow)
                return null;
        }
        return null;
    }

    /// <summary>
    /// The top of a platform under the span that lies in (<paramref name="from"/>, <paramref name="to"/>], else null.
    /// A platform is not in <see cref="Fits"/> (the body passes it from below and stands on it from above), so
    /// a falling body crossing a platform's top lands on it through this, the way the game's collision clips
    /// a downward move at a platform unless the body asked to fall through.
    /// </summary>
    public static float? PlatformTopCrossed(ITileWorld world, float left, float from, float to)
    {
        int x0 = (int)MathF.Floor(left / 16f), x1 = (int)MathF.Floor((left + Width - Touch) / 16f);
        int y0 = (int)MathF.Floor(from / 16f), y1 = (int)MathF.Floor(to / 16f);
        for (int y = y0; y <= y1; y++)
        {
            float top = y * 16f;
            if (top <= from || top > to)
                continue;
            for (int x = x0; x <= x1; x++)
                if (world.Shape(x, y) == TileShape.Platform || world.PassThrough(x, y))
                    return top;
        }
        return null;
    }

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
    public const float Touch = 0.02f;

    /// <summary>
    /// Where the body stands with its feet in tile <paramref name="column"/>, <paramref name="row"/>,
    /// or null when nothing there holds it: the first sideways offset whose rectangle rests on a
    /// surface inside that feet row and overlaps no solid. A body on a slope rests partway down
    /// the slope's own tile, which is why the feet row of a slope is the slope tile itself. The
    /// surface is looked for below the row's top edge only, because a platform whose top is that
    /// edge passes through the body's shins in the game while a lower tread under the body's
    /// other edge holds it, which is the body one step down a platform staircase; the highest
    /// surface under the span would name that platform and refuse the tile.
    /// </summary>
    public static Pose? Stand(ITileWorld world, int column, int row)
    {
        // The feet cannot be inside a full block: every offset overlaps this column, so RestBottom
        // would land on the block's top and the feet row above. Skip the probe on every rock tile.
        if (world.Shape(column, row) == TileShape.Solid)
            return null;
        foreach (float off in Offsets)
        {
            float left = column * 16f + 8f + off - Width / 2f;
            float? bottom = RestBottom(world, left, row, row * 16f + 2f * Touch);
            if (bottom is not float b || FeetRow(b) != row)
                continue;
            if (Fits(world, left, b))
                return new Pose(left, b);
        }
        return null;
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
    public static float? RestBottom(ITileWorld world, float left, int row, bool fallThrough = false) => RestBottom(world, left, row, float.NegativeInfinity, fallThrough);

    /// <summary>
    /// The same, counting no surface above <paramref name="noHigherThan"/>: a platform whose top
    /// is at the body's knee is passed through sideways in the game, and a body standing beside
    /// one is resting on its own floor and not on the platform, which the unbounded query would
    /// name as the highest surface under the span (the follow harness on run 7, 2026-09-08,
    /// stuck the body beside a platform staircase this way).
    /// </summary>
    public static float? RestBottom(ITileWorld world, float left, int row, float noHigherThan, bool fallThrough = false)
    {
        float right = left + Width;
        int x0 = (int)MathF.Floor(left / 16f), x1 = (int)MathF.Floor((right - Touch) / 16f);
        float? best = null;
        for (int y = row; y <= row + 1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float top = y * 16f;
                float? surface = fallThrough && world.PassThrough(x, y) ? null : world.Shape(x, y) switch
                {
                    TileShape.Air => null,
                    TileShape.Half => top + 8f,
                    TileShape.SolidLowerLeft => top + MathF.Max(0f, left - x * 16f),
                    TileShape.SolidLowerRight => top + MathF.Max(0f, x * 16f + 16f - right),
                    _ => top,
                };
                if (surface is float s && s >= noHigherThan - Touch && (best == null || s < best))
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
    public static bool Fits(ITileWorld world, float left, float bottom, bool fallThrough = false)
    {
        float right = left + Width, top = bottom - Height;
        int x0 = (int)MathF.Floor(left / 16f), x1 = (int)MathF.Floor((right - Touch) / 16f);
        int y0 = (int)MathF.Floor(top / 16f), y1 = (int)MathF.Floor((bottom - Touch) / 16f);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                TileShape shape = world.Shape(x, y);
                if (shape is TileShape.Air or TileShape.Platform || (fallThrough && world.PassThrough(x, y)))
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
