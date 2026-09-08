#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Brain.Aiming;

/// <summary>
/// How a projectile flies, in the terms the game's own projectile AI uses: a launch
/// speed in pixels per tick, a number of ticks it flies straight before gravity
/// applies, and the gravity added to its vertical speed on every tick after that.
/// A vanilla arrow (aiStyle 1) flies straight for 15 ticks then gains 0.1 per tick.
/// </summary>
public readonly record struct WeaponProfile(float Speed, int StraightTicks, float Gravity, float MaxFallSpeed, int MaxFlightTicks, int HitboxSize)
{
    public static readonly WeaponProfile Arrow = new(Speed: 9.6f, StraightTicks: 15, Gravity: 0.1f, MaxFallSpeed: 16f, MaxFlightTicks: 150, HitboxSize: 10);

    public WeaponProfile WithSpeed(float speed) => this with { Speed = speed };
}

/// <summary>
/// Trajectory-aware aiming. Instead of pointing at the target, the aimer simulates
/// the weapon's own arc tick by tick against the world's solid tiles, so it lobs over
/// a hill the arrow can clear, refuses a target the arc cannot reach (underground, or
/// behind a wall), and leads a moving target by advancing the target's hitbox along
/// its velocity for every simulated tick.
/// </summary>
public static class TrajectoryAimer
{
    private const float AngleStepRadians = MathHelper.Pi / 60f; // 3 degrees
    private const float MaxDepressionRadians = MathHelper.Pi / 3f;   // 60 degrees below the direct line
    private const float MaxElevationRadians = MathHelper.Pi * 0.45f; // 81 degrees above it

    /// <summary>
    /// The launch velocity that hits <paramref name="target"/> from <paramref name="muzzle"/>,
    /// or null if no arc within the weapon's reach lands. Tries the flattest arcs first, so
    /// a clear shot is a straight shot and a lob is only chosen when the straight line fails.
    /// </summary>
    public static Vector2? Solve(Vector2 muzzle, NPC target, WeaponProfile weapon)
    {
        Vector2 toTarget = target.Center - muzzle;
        float direct = MathF.Atan2(toTarget.Y, toTarget.X);

        // Candidate angles ordered by distance from the direct line, alternating above and below.
        for (float offset = 0f; offset <= MaxElevationRadians; offset += AngleStepRadians)
        {
            if (TryAngle(direct - offset, muzzle, target, weapon, out Vector2 v))
                return v;
            if (offset > 0f && offset <= MaxDepressionRadians && TryAngle(direct + offset, muzzle, target, weapon, out v))
                return v;
        }
        return null;
    }

    private static bool TryAngle(float angle, Vector2 muzzle, NPC target, WeaponProfile weapon, out Vector2 launch)
    {
        launch = angle.ToRotationVector2() * weapon.Speed;
        Vector2 position = muzzle;
        Vector2 velocity = launch;
        int half = weapon.HitboxSize / 2;

        for (int tick = 0; tick < weapon.MaxFlightTicks; tick++)
        {
            if (tick >= weapon.StraightTicks)
            {
                velocity.Y += weapon.Gravity;
                if (velocity.Y > weapon.MaxFallSpeed)
                    velocity.Y = weapon.MaxFallSpeed;
            }
            position += velocity;

            if (!WorldGen.InWorld((int)(position.X / 16f), (int)(position.Y / 16f), 5))
                return false;
            if (Collision.SolidCollision(position - new Vector2(half), weapon.HitboxSize, weapon.HitboxSize))
                return false;

            Rectangle projectileBox = new((int)position.X - half, (int)position.Y - half, weapon.HitboxSize, weapon.HitboxSize);
            Rectangle targetBox = target.Hitbox;
            Vector2 lead = target.velocity * tick;
            targetBox.Offset((int)lead.X, (int)lead.Y);
            if (projectileBox.Intersects(targetBox))
                return true;
        }
        return false;
    }
}
