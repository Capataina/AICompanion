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
/// Three things are proved here. The priors the learner starts a vanilla arrow and a thrown object with
/// are the game's numbers tick for tick, so a vanilla arrow's first shot need not miss. A type the
/// learner believes flies straight recovers the game's onset, gravity and drag from its own recorded
/// flights, and the measure that matters — hits per shot at a stationary body — goes from the straight
/// guess's figure to the learned figure, both filed as ledger measures. And the swept trace refuses a
/// thin wall between two clear endpoints and refuses an accuracy rotation that misses, so no learned
/// motion can turn an unproved shot into a spawn.
///
/// Everything here goes through the <c>live</c> alias on purpose: this project compiles its own copy of
/// the Aiming folder, and a fixture that watched samples into one copy and solved from the other
/// would prove nothing about what the arsenal fires with.
/// </summary>
internal static class VerifyArcLearning
{
    /// <summary>How many measured shots each phase of the hits-per-shot measure fires.</summary>
    private const int MeasuredShots = 20;

    /// <summary>The pass line, declared before the first run: a learned arc lands at least nine shots in ten on a body that does not move.</summary>
    private const float LearnedHitsPerShotFloor = .9f;

    public static int Run()
    {
        VerifyCompanionLifecycle.Create();
        Arcs.Reset();
        int failures = 0;
        failures += PriorMatchesNative("arrow", ProjectileID.WoodenArrowFriendly, 10f, 40);
        failures += PriorMatchesNative("knife", ProjectileID.ThrowingKnife, 10f, 40);
        failures += StraightPriorForABullet();
        failures += VerifySweptTerrainAndNoise();
        failures += APriorArrowHitsFromTheFirstShot();
        // Thirty tiles rather than twenty for the arrow: at 9.6 px/tick the arrow drops about 17 px over
        // twenty tiles, inside a zombie's 20 px half-height, so the straight guess would hit and the
        // measure would not discriminate; over thirty it drops about 63 px.
        failures += AnUnknownArcIsLearnedFromItsOwnShots("arrow", ProjectileID.WoodenArrowFriendly, 9.6f, 30,
            Arcs.Prior(ProjectileID.WoodenArrowFriendly), onsetTolerance: 0, gravityTolerance: .005f, dragTolerance: .005f);
        // Twenty tiles for the knife: its 0.4 gravity from the twentieth tick drops it about 31 px over
        // that distance, and a lob still reaches it.
        failures += AnUnknownArcIsLearnedFromItsOwnShots("knife", ProjectileID.ThrowingKnife, 10f, 20,
            Arcs.Prior(ProjectileID.ThrowingKnife), onsetTolerance: 0, gravityTolerance: .005f, dragTolerance: .005f);
        failures += AFitOutsideTheModelKeepsThePriorAndTheRingForgets();
        Arcs.Reset();
        return failures;
    }

    /// <summary>
    /// Flights fed to the learner by hand, through the same register-observe path the projectile hooks
    /// use. A rising flight fits a negative gravity, which the aimer cannot fly: it must not be installed,
    /// the type is named unfittable, and a falling flight afterwards installs a fit and clears the name.
    /// Then the ring: after more flights with a later onset than the ring holds, the learned onset is the
    /// later one, because an onset kept as the earliest ever seen would pin the type to its first flight.
    /// </summary>
    private static int AFitOutsideTheModelKeepsThePriorAndTheRingForgets()
    {
        int failures = 0;
        void Require(bool condition, string message)
        {
            if (condition) return;
            failures++;
            EmitLedgerRows.Detail("arc domain: " + message);
        }
        Arcs.Reset();
        const int type = ProjectileID.WoodenArrowFriendly;
        const int slot = 5;
        var shot = new Projectile { whoAmI = slot, type = type, active = true };

        void Fly(Func<int, Vector2> velocityAt)
        {
            Arcs.Register(slot, type, velocityAt(0));
            for (int k = 1; k <= Arcs.SamplesPerShot + 1; k++)
            {
                shot.velocity = velocityAt(k);
                Arcs.Observe(shot);
            }
        }

        Fly(k => new Vector2(8f, -0.05f * Math.Max(0, k - 10)));
        Require(Arcs.Learned(type) == null, $"a rising flight must not install a motion; learned {Arcs.Learned(type)}");
        Require(Arcs.Unfittable(type), "a rising flight names its type unfittable");
        Require(Arcs.MotionFor(type) == Arcs.Prior(type), "an unfittable type flies its prior");

        // The rising flight stays in the ring as evidence, so falling flights have to outnumber it under
        // the median and outlast it in the ring before the type is fittable again: a ring's worth.
        for (int i = 0; i < Arcs.MaxFlightsKept; i++)
            Fly(k => new Vector2(8f, 0.1f * Math.Max(0, k - 15)));
        LearnedMotion? fitted = Arcs.Learned(type);
        Require(fitted is { } f && Arcs.Flyable(f) && MathF.Abs(f.Gravity - 0.1f) < 0.005f && f.GravityStartsAtPhase == 16,
            $"falling flights afterwards install a flyable fit once the rising one has left the ring; learned {fitted}");
        Require(!Arcs.Unfittable(type), "and the name is cleared");

        for (int i = 0; i < Arcs.MaxFlightsKept; i++)
            Fly(k => new Vector2(8f, 0.1f * Math.Max(0, k - 25)));
        Require(Arcs.Learned(type) is { GravityStartsAtPhase: 26 },
            $"after a ring's worth of flights with a later onset the learned onset is the later one; learned {Arcs.Learned(type)}");
        Arcs.Reset();
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

    /// <summary>
    /// The range: a zombie standing still in open air, a number of tiles to the right of the muzzle, in a
    /// box of rows this fixture clears itself because the per-case reset leaves the tile map as earlier
    /// cases left it. Row 75 is well under the suite's shared floor at row 60, so a compensating lob
    /// never meets it.
    /// </summary>
    private static (Vector2 Muzzle, NPC Target) Range(int tilesAway)
    {
        for (int x = 55; x < 100; x++)
            for (int y = 65; y < 86; y++) { Tile air = Main.tile[x, y]; air.HasTile = false; air.LiquidAmount = 0; }
        Vector2 muzzle = new(60 * 16f, 75 * 16f);
        var target = new NPC();
        target.SetDefaults(NPCID.Zombie);
        target.whoAmI = 26; target.active = true; target.velocity = Vector2.Zero; target.noGravity = true;
        target.Center = muzzle + new Vector2(tilesAway * 16f, 0f);
        Main.npc[26] = target;
        return (muzzle, target);
    }

    /// <summary>
    /// One shot: solve with whatever the learner currently believes, spawn the native projectile at the
    /// solved launch, fly it with the game's own AI until it meets the target's box, a solid or the
    /// world's edge, feeding the learner when asked. Returns whether it hit.
    /// </summary>
    private static bool Shoot(int type, float speed, Vector2 muzzle, NPC target, bool learn)
    {
        Projectile sample = ContentSamples.ProjectilesByType[type];
        var model = new FlightModel(speed, Arcs.MotionFor(type), 150, sample.width, 1100f);
        if (!Aimer.TrySolve(muzzle, target, model, out var solution))
            return false;
        var native = new Projectile();
        native.SetDefaults(type);
        native.whoAmI = 0; native.active = true; native.owner = Main.myPlayer;
        native.Center = muzzle;
        native.velocity = solution.LaunchVelocity;
        if (learn) Arcs.Register(0, type, solution.LaunchVelocity);
        bool hit = false;
        for (int tick = 1; tick <= 150; tick++)
        {
            native.VanillaAI();
            native.position += native.velocity;
            if (learn) Arcs.Observe(native);
            if (native.Hitbox.Intersects(target.Hitbox)) { hit = true; break; }
            if (!WorldGen.InWorld((int)(native.position.X / 16f), (int)(native.position.Y / 16f), 5)
                || Collision.SolidCollision(native.position, native.width, native.height))
                break;
        }
        if (learn) Arcs.Retire(0);
        return hit;
    }

    private static float HitsPerShot(int type, float speed, Vector2 muzzle, NPC target)
    {
        int hits = 0;
        for (int shot = 0; shot < MeasuredShots; shot++)
            if (Shoot(type, speed, muzzle, target, learn: false)) hits++;
        return hits / (float)MeasuredShots;
    }

    /// <summary>A vanilla arrow, which has a prior, lands from its first shot at thirty tiles.</summary>
    private static int APriorArrowHitsFromTheFirstShot()
    {
        Arcs.Reset();
        var (muzzle, target) = Range(30);
        float ratio = HitsPerShot(ProjectileID.WoodenArrowFriendly, 9.6f, muzzle, target);
        EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "EngineReplay", "arc learning: arrow with its prior, hits per shot at thirty tiles", ratio, "hits/shot", "up");
        if (ratio < 1f)
        {
            EmitLedgerRows.Detail($"arrow with prior: {ratio:0.00} hits per shot at thirty tiles; a vanilla arrow's first shot should not miss");
            return 1;
        }
        Console.WriteLine($"arc learning: arrow with its prior lands {ratio:0.00} per shot at thirty tiles from the first shot");
        return 0;
    }

    /// <summary>
    /// The measurement the lane is judged by. The learner is told the type flies straight — which is what
    /// any modded projectile starts as — and hits per shot are taken at that guess, then calibration shots
    /// are fired with the learner watching until it has a fit, then hits per shot are taken again with the
    /// learner frozen. The pass line, declared before the first run: after is above before, and after is at
    /// least <see cref="LearnedHitsPerShotFloor"/>. The learned numbers are then held against the game's,
    /// which the prior carries, so the fit is shown to recover the mechanism and not merely to land.
    /// </summary>
    private static int AnUnknownArcIsLearnedFromItsOwnShots(string name, int type, float speed, int tilesAway, LearnedMotion truth,
        int onsetTolerance, float gravityTolerance, float dragTolerance)
    {
        Arcs.Reset();
        Arcs.Assume(type, LearnedMotion.Straight);
        var (muzzle, target) = Range(tilesAway);
        float before = HitsPerShot(type, speed, muzzle, target);

        int calibration = 0;
        while (calibration < 5 && (Arcs.Learned(type) is not { } fitted || fitted.IsStraight))
        {
            Shoot(type, speed, muzzle, target, learn: true);
            calibration++;
        }
        LearnedMotion? learned = Arcs.Learned(type);
        float after = HitsPerShot(type, speed, muzzle, target);

        EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "EngineReplay", $"arc learning: {name} believed straight, hits per shot before learning at {tilesAway} tiles", before, "hits/shot", "up");
        EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "EngineReplay", $"arc learning: {name} after learning, hits per shot at {tilesAway} tiles", after, "hits/shot", "up");
        EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "EngineReplay", $"arc learning: {name} calibration shots until the first fit", calibration, "shots", "down");
        Console.WriteLine($"arc learning: {name} at {tilesAway} tiles: {before:0.00} hits per shot believed straight, {calibration} calibration shot(s), {after:0.00} after learning; learned={learned}; truth={truth}; pairs={Arcs.Evidence(type)}");

        if (learned == null || learned.Value.IsStraight)
        {
            EmitLedgerRows.Detail($"{name}: no fit after {calibration} calibration shots; pairs={Arcs.Evidence(type)}");
            return 1;
        }
        LearnedMotion fit = learned.Value;
        if (Math.Abs(fit.GravityStartsAtPhase - truth.GravityStartsAtPhase) > onsetTolerance
            || MathF.Abs(fit.Gravity - truth.Gravity) > gravityTolerance
            || MathF.Abs(fit.HorizontalDrag - truth.HorizontalDrag) > dragTolerance
            || MathF.Abs(fit.MaxFallSpeed - truth.MaxFallSpeed) > .001f)
        {
            EmitLedgerRows.Detail($"{name}: the fit does not recover the game's numbers; learned={fit} truth={truth}");
            return 1;
        }
        if (!(before < after) || after < LearnedHitsPerShotFloor)
        {
            EmitLedgerRows.Detail($"{name}: hits per shot before={before:0.00} after={after:0.00}; the pass line is after above before and at least {LearnedHitsPerShotFloor:0.00}");
            return 1;
        }
        return 0;
    }
}
