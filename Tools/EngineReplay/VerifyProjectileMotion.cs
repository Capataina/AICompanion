#nullable enable

using AICompanion.Companion.Brain.ProjectileAiming;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

/// <summary>
/// Compares the exact free-flight phase of every projectile currently in the companion kit with
/// Terraria's own <see cref="Projectile.VanillaAI"/>. This is deliberately a native fixture: the
/// solver is trusted only when its phase, gravity, drag, cap and hitbox stay equal to the game.
/// </summary>
internal static class VerifyProjectileMotion
{
    public static int Run()
    {
        int failures = 0;
        failures += Verify("arrow", ProjectileID.WoodenArrowFriendly, WeaponProfile.Arrow.WithSpeed(10f), 40);
        failures += Verify("knife", ProjectileID.ThrowingKnife, new WeaponProfile(10f, ProjectileMotion.ThrowingKnife, 90, 12, 380f), 40);
        failures += VerifySweptTerrainAndNoise();
        return failures;
    }

    private static int VerifySweptTerrainAndNoise()
    {
        WeaponProfile profile = WeaponProfile.Arrow.WithSpeed(10f);
        Vector2 muzzle = new(160f, 500f);
        Vector2 launch = new(10f, 0f);
        var target = new NPC { active = true, width = 10, height = 10, position = new Vector2(306f, 495f), velocity = Vector2.Zero, noGravity = true };
        if (!TrajectoryAimer.TryTrace(muzzle, launch, target, profile, out _))
        {
            Console.WriteLine("FAIL projectile sweep: clear trace did not reach its target");
            return 1;
        }

        // A 16px ceiling tile lies between two 10px projectile endpoints. The old endpoint-only
        // check skipped this geometry; the real flight must reject it before a projectile spawns.
        Tile wall = Main.tile[14, 31];
        wall.HasTile = true;
        wall.TileType = 1;
        bool blocked = !TrajectoryAimer.TryTrace(muzzle, launch, target, profile, out _);
        wall.HasTile = false;
        bool reopened = TrajectoryAimer.TryTrace(muzzle, launch, target, profile, out _);
        bool noisyStillSafe = !TrajectoryAimer.TryTrace(muzzle, launch.RotatedBy(MathHelper.ToRadians(4f)), target, profile, out _);
        if (!blocked || !reopened || !noisyStillSafe)
        {
            Console.WriteLine($"FAIL projectile sweep: blocked={blocked} reopened={reopened} noisy-rejected={noisyStillSafe}");
            return 1;
        }
        Console.WriteLine("PASS projectile sweep: thin wall blocks, its removal reopens, and an accuracy rotation is rejected when it misses");
        return 0;
    }

    private static int Verify(string name, int type, WeaponProfile profile, int ticks)
    {
        var native = new Projectile();
        native.SetDefaults(type);
        native.position = new Vector2(1000f, 1000f);
        native.velocity = new Vector2(profile.Speed, 0f);
        native.owner = Main.myPlayer;
        Vector2 modelPosition = native.position;
        Vector2 modelVelocity = native.velocity;
        int phase = 0;

        if (native.width != profile.HitboxSize || native.height != profile.HitboxSize)
        {
            Console.WriteLine($"FAIL projectile {name}: native box {native.width}x{native.height}, profile {profile.HitboxSize}x{profile.HitboxSize}");
            return 1;
        }

        for (int tick = 1; tick <= ticks; tick++)
        {
            native.VanillaAI();
            native.position += native.velocity;
            ProjectileFlight.Advance(ref modelPosition, ref modelVelocity, profile, ref phase);
            if (Vector2.Distance(native.position, modelPosition) > .001f || Vector2.Distance(native.velocity, modelVelocity) > .001f)
            {
                Console.WriteLine($"FAIL projectile {name} tick {tick}: native pos={native.position} vel={native.velocity}; model pos={modelPosition} vel={modelVelocity}");
                return 1;
            }
        }
        Console.WriteLine($"PASS projectile {name}: {ticks} native free-flight ticks, {profile.HitboxSize}x{profile.HitboxSize} box, phase/gravity/drag/cap matched");
        return 0;
    }
}
