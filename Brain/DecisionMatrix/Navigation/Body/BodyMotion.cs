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

        // Sideways: the move, or the same move lifted onto a one-tile kerb or a platform at the
        // knee when standing (the game's StepUp, as the player holding up runs it), or nothing
        // and the speed lost against the shape.
        bool collideX = false;
        float nextLeft = left + vx * move;
        // The platform lift is refused while the move in hand is going down. Without that clause
        // this rule and the game's disagreed about *when* rather than about what: here the lift
        // needs a grounded body, so a falling body never took it and a descent simulated clean,
        // while the motor in the game ran the same lift on every tick with a non-negative vertical
        // velocity — true throughout a fall — and climbed the platform it was descending. The
        // shaft that replayed in 264 ticks and froze in play was that one word of difference.
        if (grounded && !c.Descend && vx != 0f && PlatformStepUp(world, nextLeft, bottom, MathF.Sign(vx)) is float onto)
        {
            left = nextLeft;
            bottom = onto;
        }
        else if (BodyPhysics.Fits(world, nextLeft, bottom))
            left = nextLeft;
        else if (grounded && StepUp(world, nextLeft, bottom) is float lifted)
        {
            left = nextLeft;
            bottom = lifted;
        }
        else if (CeilingPush(world, nextLeft, bottom) is float pushed)
        {
            left = nextLeft;
            bottom = pushed;
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
            float? rest = BodyPhysics.RestBottom(world, left, BodyPhysics.FeetRow(bottom), bottom - 1f);
            return rest is float r && BodyPhysics.Fits(world, left, r)
                ? new BodyState(left, r, vx, 0f, true, collideX, false, s.Mobility)
                : new BodyState(left, bottom, vx, 0f, true, collideX, true, s.Mobility);
        }
        return new BodyState(left, bottom, vx, vy, false, collideX, false, s.Mobility);
    }

    /// <summary>A surface under the span sits exactly at the bottom: the body is standing on it.</summary>
    private static bool SurfaceAt(ITileWorld world, float left, float bottom)
        => BodyPhysics.RestBottom(world, left, BodyPhysics.FeetRow(bottom), bottom - 1f) is float s && MathF.Abs(s - bottom) <= 0.5f;

    /// <summary>
    /// The bottom the body rests on when lifted over a one-tile kerb at <paramref name="nextLeft"/>
    /// (a block, a half block or a floor slope it would otherwise walk into), or null when nothing
    /// within a tile above holds it there. The rows above the feet row hold a kerb; the row below
    /// holds the slope a body is riding whose bottom sits a fraction of a pixel into that row,
    /// where the diagonal rises ahead of it by less than the fraction, and a lift that never
    /// looked there left the body wedged against a slope's last half pixel (the follow harness
    /// on the run-5 pool route, 2026-09-08).
    /// </summary>
    private static float? StepUp(ITileWorld world, float nextLeft, float bottom)
    {
        int row = BodyPhysics.FeetRow(bottom);
        foreach (int from in new[] { row - 1, row + 1 })
        {
            if (BodyPhysics.RestBottom(world, nextLeft, from, bottom - 16f) is float r && r < bottom && BodyPhysics.Fits(world, nextLeft, r))
                return r;
        }
        return null;
    }

    /// <summary>
    /// The platform top the body steps onto when the column its leading edge enters holds a
    /// platform in the feet row with the body's height clear above it, or null. A platform never
    /// blocks, so the plain move would pass through it at the knee; the game's StepUp lifts the
    /// body onto it only while the player holds up, and the motor asks for that, so the companion
    /// climbs a platform staircase the way a player does. The lift waits until the lifted body
    /// fits, which is where the game would let a corner clip rock and push it out later.
    /// </summary>
    private static float? PlatformStepUp(ITileWorld world, float nextLeft, float bottom, int dir)
    {
        int ahead = (int)MathF.Floor((nextLeft + BodyPhysics.Width / 2f + (BodyPhysics.Width / 2f + 1f) * dir) / 16f);
        int feetRow = BodyPhysics.FeetRow(bottom);
        if (world.Shape(ahead, feetRow) != TileShape.Platform || world.Shape(ahead, feetRow - 1) == TileShape.Platform)
            return null;
        for (int r = 1; r <= NavGrid.BodyHeightTiles; r++)
            if (world.Shape(ahead, feetRow - r) is not (TileShape.Air or TileShape.Platform))
                return null;
        float top = feetRow * 16f;
        if (top >= bottom || bottom - top > 16.1f || !BodyPhysics.Fits(world, nextLeft, top))
            return null;
        return top;
    }

    /// <summary>
    /// The bottom the body is pushed down to by the ceiling slopes over its head at
    /// <paramref name="nextLeft"/>, or null when no push under a tile lets it fit there. The
    /// game's slope collision moves a body under a ceiling slope's diagonal in the tick it
    /// meets it, so a body hopping down a staircase of floor slopes under a matching staircase
    /// of ceiling slopes passes with its head a fraction of a pixel under each diagonal; a move
    /// refused for that fraction parked the body on the fourth run's slope staircase.
    /// </summary>
    private static float? CeilingPush(ITileWorld world, float nextLeft, float bottom)
    {
        float right = nextLeft + BodyPhysics.Width, top = bottom - BodyPhysics.Height;
        int x0 = (int)MathF.Floor(nextLeft / 16f), x1 = (int)MathF.Floor((right - BodyPhysics.Touch) / 16f);
        int y0 = (int)MathF.Floor(top / 16f);
        float push = 0f;
        for (int y = y0; y <= y0 + 1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float lx0 = MathF.Max(nextLeft, x * 16f) - x * 16f, lx1 = MathF.Min(right, x * 16f + 16f) - x * 16f;
                float ly0 = MathF.Max(top, y * 16f) - y * 16f;
                // Pushed to the diagonal itself, a touch under where Fits stops counting a hit,
                // because a push computed to Fits' own boundary lands on either side of it in
                // floating point.
                float need = world.Shape(x, y) switch
                {
                    TileShape.SolidUpperRight => lx1 - ly0,
                    TileShape.SolidUpperLeft => 16f - lx0 - ly0,
                    _ => 0f,
                };
                push = MathF.Max(push, need);
            }
        }
        if (push <= 0f || push > 16f)
            return null;
        float pushed = bottom + push;
        return BodyPhysics.Fits(world, nextLeft, pushed) ? pushed : null;
    }

    /// <summary>
    /// The bottom a surface below holds the body at when the fall ahead is between half a tile
    /// and a tile, the game's own StepDown window; a smaller gap is left to gravity, so a body
    /// walking down a slope hops a few air ticks at a time as it does in the game, and a larger
    /// one is a fall. Platforms count, as they do in the game.
    /// </summary>
    private static float? StepDown(ITileWorld world, float left, float bottom)
    {
        int row = (int)MathF.Floor((bottom + 4f) / 16f);
        float? rest = BodyPhysics.RestBottom(world, left, row, bottom + 0.01f);
        return rest is float r && r - bottom > 7f && r - bottom < 17f && BodyPhysics.Fits(world, left, r) ? r : null;
    }
}
