#nullable enable

using System;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// One tick of the body under the game's own order, as geometry against the tile shapes: the
/// controls set the horizontal speed and the jump impulse, the kerb rules lift the body over a
/// one-tile rise and keep its feet on a one-tile fall, gravity is added, the sideways move is
/// clipped by any shape it meets, and the downward move is walked in short steps so a fast fall
/// cannot pass a thin ledge, landing on a platform's top unless the controls asked to fall
/// through it and on any other surface always. This is the single body every traversal is
/// simulated with by the planner and driven by in the replay, and the motor in the game is
/// the same rule with the NPC's collision in place of the shape tests; where the two disagree,
/// the executed-edge trace in the telemetry is where it shows.
/// </summary>
public static class BodyMotion
{
    /// <summary>The longest downward move taken in one piece; a platform is one pixel row, so a fall at the cap crosses it inside a step.</summary>
    private const float SubStep = 4f;

    public static BodyState Step(ITileWorld world, BodyState s, Controls c)
    {
        float left = s.Left, bottom = s.Bottom;
        float vx = BodyPhysics.StepVelocity(s.Vx, c.MoveX);
        float vy = s.Vy;
        bool grounded = s.OnGround;
        if (c.Jump && grounded)
        {
            vy = BodyPhysics.JumpVelocity * c.JumpScale;
            grounded = false;
        }
        // The game adds gravity after the AI has set the velocity and before the collision clips
        // the move; a standing body's small downward step is clipped by its own floor below.
        vy = BodyPhysics.StepFall(vy);

        int feetColumn = (int)MathF.Floor((left + BodyPhysics.Width / 2f) / 16f);
        int feetRow = BodyPhysics.FeetRow(bottom);
        // A wet NPC moves at half its velocity, the game's own rule.
        float move = world.Water(feetColumn, feetRow) || world.Lava(feetColumn, feetRow) ? 0.5f : 1f;

        // Sideways: the move, or the same move lifted over a one-tile kerb when standing (the
        // game's StepUp), or nothing and the speed lost against the shape.
        bool collideX = false;
        float nextLeft = left + vx * move;
        if (BodyPhysics.Fits(world, nextLeft, bottom))
            left = nextLeft;
        else if (grounded && StepUp(world, nextLeft, bottom) is float lifted)
        {
            left = nextLeft;
            bottom = lifted;
        }
        else
        {
            vx = 0f;
            collideX = true;
        }

        // Upward: rise while the body fits, stop under a ceiling.
        if (vy < 0f)
        {
            float risen = bottom + vy * move;
            if (BodyPhysics.Fits(world, left, risen))
                bottom = risen;
            else
                vy = 0f;
            return new BodyState(left, bottom, vx, vy, false, collideX, false, s.Mobility);
        }

        // Standing: the surface the feet rest on keeps holding them, a platform included unless
        // asked to pass it, and a one-tile fall ahead is stepped down onto (the game's StepDown)
        // rather than fallen.
        if (grounded)
        {
            if (!c.FallThrough && SurfaceAt(world, left, bottom))
                return new BodyState(left, bottom, vx, 0f, true, collideX, false, s.Mobility);
            if (!c.FallThrough && StepDown(world, left, bottom) is float lowered)
                return new BodyState(left, lowered, vx, 0f, true, collideX, false, s.Mobility);
        }

        // Falling: in short steps, landing on the first surface crossed. A platform's top counts
        // unless the controls asked to fall through it this tick, which is how the follower
        // presses "down" and how the game's own collision reads the hook.
        float remaining = vy * move;
        while (remaining > 0f)
        {
            float step = MathF.Min(SubStep, remaining);
            if (!c.FallThrough && BodyPhysics.PlatformTopCrossed(world, left, bottom, bottom + step) is float top)
                return BodyPhysics.Fits(world, left, top)
                    ? new BodyState(left, top, vx, 0f, true, collideX, false, s.Mobility)
                    : new BodyState(left, bottom, vx, 0f, true, collideX, true, s.Mobility);
            if (BodyPhysics.Fits(world, left, bottom + step))
            {
                bottom += step;
                remaining -= step;
                continue;
            }
            float? rest = BodyPhysics.RestBottom(world, left, BodyPhysics.FeetRow(bottom));
            return rest is float r && r >= bottom - 1f && BodyPhysics.Fits(world, left, r)
                ? new BodyState(left, r, vx, 0f, true, collideX, false, s.Mobility)
                : new BodyState(left, bottom, vx, 0f, true, collideX, true, s.Mobility);
        }
        return new BodyState(left, bottom, vx, vy, false, collideX, false, s.Mobility);
    }

    /// <summary>A surface under the span sits exactly at the bottom: the body is standing on it.</summary>
    private static bool SurfaceAt(ITileWorld world, float left, float bottom)
        => BodyPhysics.RestBottom(world, left, BodyPhysics.FeetRow(bottom)) is float s && MathF.Abs(s - bottom) <= 0.5f;

    /// <summary>
    /// The bottom the body rests on when lifted over a one-tile kerb at <paramref name="nextLeft"/>,
    /// or null when nothing within a tile above holds it there.
    /// </summary>
    private static float? StepUp(ITileWorld world, float nextLeft, float bottom)
    {
        float? rest = BodyPhysics.RestBottom(world, nextLeft, BodyPhysics.FeetRow(bottom) - 1);
        return rest is float r && r < bottom && r >= bottom - 16f && BodyPhysics.Fits(world, nextLeft, r) ? r : null;
    }

    /// <summary>The bottom one tile or less below that a surface holds the body at, or null when the fall ahead is deeper than a step.</summary>
    private static float? StepDown(ITileWorld world, float left, float bottom)
    {
        float? rest = BodyPhysics.RestBottom(world, left, BodyPhysics.FeetRow(bottom) + 1);
        return rest is float r && r > bottom && r <= bottom + 16f && BodyPhysics.Fits(world, left, r) ? r : null;
    }
}
