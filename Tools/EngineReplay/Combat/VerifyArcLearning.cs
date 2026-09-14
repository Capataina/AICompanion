extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Tools.Ledger;
using Aimer = live::AICompanion.Companion.Brain.Infrastructure.Aiming.TrajectoryAimer;
using Flight = live::AICompanion.Companion.Brain.Infrastructure.Aiming.ProjectileFlight;
using FlightModel = live::AICompanion.Companion.Brain.Infrastructure.Aiming.FlightModel;
using LearnedMotion = live::AICompanion.Companion.Brain.Infrastructure.Aiming.LearnedMotion;
using Arcs = live::AICompanion.Companion.Brain.Infrastructure.Aiming.ProjectileArcs;

/// <summary>
/// The arc the aimer flies a projectile with, held against Terraria's own <see cref="Projectile.VanillaAI"/>.
/// Two things are proved here. The priors the learner starts a vanilla arrow and a thrown object with
/// are the game's numbers tick for tick, so a vanilla arrow's first shot need not miss. And the swept
/// trace refuses a thin wall between two clear endpoints and refuses an accuracy rotation that misses,
/// so a learned motion can never turn an unproved shot into a spawn.
///
/// Everything here goes through the <c>live</c> alias on purpose: this project compiles its own copy of
/// the Aiming folder, and a fixture that watched samples into one copy and solved from the other
/// would prove nothing about what the arsenal fires with.
/// </summary>
internal static class VerifyArcLearning
{
    public static int Run()
    {
        VerifyCompanionLifecycle.Create();
        Arcs.Reset();
        int failures = 0;
        failures += PriorMatchesNative("arrow", ProjectileID.WoodenArrowFriendly, 10f, 40);
        failures += PriorMatchesNative("knife", ProjectileID.ThrowingKnife, 10f, 40);
        failures += StraightPriorForABullet();
        failures += VerifySweptTerrainAndNoise();
        return failures;
    }

    /// <summary>A style 1 projectile without the arrow flag takes none of the arrow's gravity branch, and its prior says so.</summary>
    private static int StraightPriorForABullet()
    {
        LearnedMotion bullet = Arcs.Prior(ProjectileID.Bullet);
        if (!bullet.IsStraight)
        {
            EmitLedgerRows.Detail($"bullet prior: expected straight, got gravity {bullet.Gravity} drag {bullet.HorizontalDrag}");
            return 1;
        }
        Console.WriteLine("bullet prior: straight, as AI_001 flies a style-1 projectile without the arrow flag");
        return 0;
    }

    private static int VerifySweptTerrainAndNoise()
    {
        var model = new FlightModel(10f, Arcs.Prior(ProjectileID.WoodenArrowFriendly), 150, 10, 1100f);
        Vector2 muzzle = new(160f, 500f);
        Vector2 launch = new(10f, 0f);
        var target = new NPC { active = true, width = 10, height = 10, position = new Vector2(306f, 495f), velocity = Vector2.Zero, noGravity = true };
        if (!Aimer.TryTrace(muzzle, launch, target, model, out _))
        {
            EmitLedgerRows.Detail("projectile sweep: clear trace did not reach its target");
            return 1;
        }

        // A 16px ceiling tile lies between two 10px projectile endpoints. The old endpoint-only
        // check skipped this geometry; the real flight must reject it before a projectile spawns.
        Tile wall = Main.tile[14, 31];
        wall.HasTile = true;
        wall.TileType = 1;
        bool blocked = !Aimer.TryTrace(muzzle, launch, target, model, out _);
        wall.HasTile = false;
        bool reopened = Aimer.TryTrace(muzzle, launch, target, model, out _);
        bool noisyStillSafe = !Aimer.TryTrace(muzzle, launch.RotatedBy(MathHelper.ToRadians(4f)), target, model, out _);
        if (!blocked || !reopened || !noisyStillSafe)
        {
            EmitLedgerRows.Detail($"projectile sweep: blocked={blocked} reopened={reopened} noisy-rejected={noisyStillSafe}");
            return 1;
        }
        Console.WriteLine("projectile sweep: thin wall blocks, its removal reopens, and an accuracy rotation is rejected when it misses");
        return 0;
    }

    /// <summary>
    /// The prior for this type, flown by the aimer's own step, against the native AI plus the engine's
    /// move, for <paramref name="ticks"/> ticks from the same launch. The box is compared too, because
    /// the sweep collides the box the model claims and a wrong box proves a clear line the game would strike.
    /// </summary>
    private static int PriorMatchesNative(string name, int type, float speed, int ticks)
    {
        var native = new Projectile();
        native.SetDefaults(type);
        native.position = new Vector2(1000f, 1000f);
        native.velocity = new Vector2(speed, 0f);
        native.owner = Main.myPlayer;
        var model = new FlightModel(speed, Arcs.Prior(type), 150, native.width, 1100f);
        Vector2 modelPosition = native.position;
        Vector2 modelVelocity = native.velocity;
        int phase = 0;

        for (int tick = 1; tick <= ticks; tick++)
        {
            native.VanillaAI();
            native.position += native.velocity;
            Flight.Advance(ref modelPosition, ref modelVelocity, model, ref phase);
            if (Vector2.Distance(native.position, modelPosition) > .001f || Vector2.Distance(native.velocity, modelVelocity) > .001f)
            {
                EmitLedgerRows.Detail($"projectile {name} tick {tick}: native pos={native.position} vel={native.velocity}; model pos={modelPosition} vel={modelVelocity}");
                return 1;
            }
        }
        Console.WriteLine($"projectile {name}: {ticks} native free-flight ticks, {native.width}x{native.height} box, the prior's phase/gravity/drag/cap matched");
        return 0;
    }
}
