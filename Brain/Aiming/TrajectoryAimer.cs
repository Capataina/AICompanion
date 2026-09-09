#nullable enable

using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Every hostile the shot passes through, in the order the projectile reaches them, written
    /// into <paramref name="into"/> and counted in the return. A piercing weapon is worth what its
    /// real arc crosses, so this asks the flight rather than counting enemies near the target: two
    /// zombies abreast of the muzzle are one shot and two abreast of the target may be none. The
    /// walk stops at the same wall and the same world edge the solve stops at, and it runs over the
    /// caller's own hostile list rather than every slot in the world, because it is asked per weapon
    /// per target while the brain is deciding.
    /// </summary>
    public static int PathHits(Vector2 muzzle, Vector2 launch, WeaponProfile weapon, IReadOnlyList<NPC> hostiles, NPC[] into)
    {
        Vector2 position = muzzle;
        Vector2 velocity = launch;
        int half = weapon.HitboxSize / 2;
        int found = 0;

        for (int tick = 0; tick < weapon.MaxFlightTicks && found < into.Length; tick++)
        {
            Advance(ref position, ref velocity, weapon, tick);
            if (!WorldGen.InWorld((int)(position.X / 16f), (int)(position.Y / 16f), 5))
                break;
            if (Collision.SolidCollision(position - new Vector2(half), weapon.HitboxSize, weapon.HitboxSize))
                break;

            Rectangle projectileBox = new((int)position.X - half, (int)position.Y - half, weapon.HitboxSize, weapon.HitboxSize);
            for (int i = 0; i < hostiles.Count && found < into.Length; i++)
            {
                NPC npc = hostiles[i];
                if (npc == null || !npc.active || npc.life <= 0 || Seen(into, found, npc))
                    continue;
                Rectangle box = npc.Hitbox;
                Vector2 lead = npc.velocity * tick;
                box.Offset((int)lead.X, (int)lead.Y);
                if (projectileBox.Intersects(box))
                    into[found++] = npc;
            }
        }
        return found;
    }

    private static bool Seen(NPC[] into, int count, NPC npc)
    {
        for (int i = 0; i < count; i++)
            if (into[i] == npc)
                return true;
        return false;
    }

    private static bool TryAngle(float angle, Vector2 muzzle, NPC target, WeaponProfile weapon, out Vector2 launch)
    {
        launch = angle.ToRotationVector2() * weapon.Speed;
        Vector2 position = muzzle;
        Vector2 velocity = launch;
        int half = weapon.HitboxSize / 2;

        for (int tick = 0; tick < weapon.MaxFlightTicks; tick++)
        {
            Advance(ref position, ref velocity, weapon, tick);

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

    /// <summary>
    /// One tick of flight, in the terms the game's projectile AI uses. Both the solve and the
    /// pierce walk step through this, so the arc that is proven and the arc that is counted can
    /// never be two different arcs.
    /// </summary>
    private static void Advance(ref Vector2 position, ref Vector2 velocity, WeaponProfile weapon, int tick)
    {
        if (tick >= weapon.StraightTicks)
        {
            velocity.Y += weapon.Gravity;
            if (velocity.Y > weapon.MaxFallSpeed)
                velocity.Y = weapon.MaxFallSpeed;
        }
        position += velocity;
    }
}
