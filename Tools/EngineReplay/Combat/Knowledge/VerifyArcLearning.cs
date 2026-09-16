extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Tools.Ledger;
using Recording = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording;
using Laws = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.FitFlightLaws;
using FlightLaw = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.FlightLaw;
using SolveAims = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.SolveAims;
using Simulate = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.SimulateUse;
using WeaponId = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.WeaponId;
using CombatWorld = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.CombatWorld;
using ModifierState = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.ModifierState;
using EnemyForecast = live::AICompanion.Companion.Brain.Infrastructure.Observation.EnemyForecast;
using ForecastEnemies = live::AICompanion.Companion.Brain.Infrastructure.Observation.ForecastEnemies;
using PlanningBudget = live::AICompanion.Companion.Brain.Activities.Combat.Planning.PlanningBudget;

/// <summary>
/// The law a use is simulated under, held against Terraria's own <see cref="Projectile.VanillaAI"/>.
/// Three things are proved here. The priors the fitter starts a vanilla arrow and a thrown object with
/// are the game's numbers tick for tick, so a vanilla arrow's first shot need not miss. A type the
/// learner believes flies straight recovers the game's onset, gravity and drag from watched flights —
/// the player's, because the companion's own aimed shots would teach the law its own aim back — and the
/// measure that matters — hits per shot at a stationary body — goes from the straight
/// guess's figure to the learned figure, both filed as ledger measures. And the sweep refuses a thin
/// wall between two clear endpoints and refuses an accuracy rotation that misses, so no learned law
/// can turn an unproved shot into a spawn.
/// </summary>
internal static class VerifyArcLearning
{
    /// <summary>How many measured shots each phase of the hits-per-shot measure fires.</summary>
    private const int MeasuredShots = 20;

    /// <summary>The pass line, declared before the first run: a learned law lands at least nine shots in ten on a body that does not move.</summary>
    private const float LearnedHitsPerShotFloor = .9f;

    public static int Run()
    {
        VerifyCompanionLifecycle.Create();
        Recording.RecordProjectileFlights.Clear();
        Recording.GroupSpawnsIntoUses.Clear();
        Laws.Reset();
        int failures = 0;
        failures += PriorMatchesNative("arrow", ProjectileID.WoodenArrowFriendly, 10f, 40);
        failures += PriorMatchesNative("knife", ProjectileID.ThrowingKnife, 10f, 40);
        failures += StraightPriorForABullet();
        failures += VerifySweptTerrainAndNoise();
        failures += APriorArrowHitsFromTheFirstShot();
        // Thirty tiles rather than twenty for the arrow: at 9.6 px/tick the arrow drops about 17 px over
        // twenty tiles, inside a zombie's 20 px half-height, so the straight guess would hit and the
        // measure would not discriminate; over thirty it drops about 63 px.
        failures += AnUnknownArcIsLearnedFromWatchedShots("arrow", ProjectileID.WoodenArrowFriendly, ItemID.WoodenBow, 9.6f, 30,
            onset: 14, gravity: .1f, drag: 1f, onsetTolerance: 0, gravityTolerance: .005f, dragTolerance: .005f);
        // Twenty tiles for the knife: its 0.4 gravity from the twentieth tick drops it about 31 px over
        // that distance, and a lob still reaches it.
        failures += AnUnknownArcIsLearnedFromWatchedShots("knife", ProjectileID.ThrowingKnife, ItemID.ThrowingKnife, 10f, 20,
            onset: 19, gravity: .4f, drag: .97f, onsetTolerance: 0, gravityTolerance: .005f, dragTolerance: .005f);
        failures += TheRingKeepsTheLongestAndForgetsInOrder();
        Recording.RecordProjectileFlights.Clear();
        Laws.Reset();
        return failures;
    }

    /// <summary>
    /// Flights fed to the learner by hand, through the same spawn-step path the projectile hooks use.
    /// The kept set is the longest flights, not the newest: after a ring's worth of equal-length flights
    /// with a later onset the fitted onset is the later one, because an onset kept as the earliest ever
    /// seen would pin the type to its first flight — and a long flight survives newer short ones.
    /// </summary>
    private static int TheRingKeepsTheLongestAndForgetsInOrder()
    {
        int failures = 0;
        void Require(bool condition, string message)
        {
            if (condition) return;
            failures++;
            EmitLedgerRows.Detail("arc domain: " + message);
        }
        Recording.RecordProjectileFlights.Clear();
        Laws.Reset();
        const int type = ProjectileID.WoodenArrowFriendly;
        const int slot = 5;
        var item = new Item();
        item.SetDefaults(ItemID.WoodenBow);
        var shot = new Projectile();
        shot.SetDefaults(type);
        shot.whoAmI = slot;
        shot.active = true;
        shot.owner = Main.myPlayer;

        void Fly(System.Func<int, Vector2> velocityAt, int steps)
        {
            shot.Center = new Vector2(1000f, 1000f);
            shot.velocity = velocityAt(0);
            Recording.RecordProjectileFlights.NoteSpawn(slot, shot,
                new Terraria.DataStructures.EntitySource_ItemUse(Main.player[Main.myPlayer], item));
            for (int k = 1; k <= steps; k++)
            {
                shot.velocity = velocityAt(k);
                shot.position += shot.velocity;
                Recording.RecordProjectileFlights.NoteStep(shot);
            }
            Recording.RecordProjectileFlights.NoteDeath(shot);
        }

        for (int i = 0; i < Recording.RecordProjectileFlights.MaxTracesPerType; i++)
            Fly(k => new Vector2(8f, 0.1f * System.Math.Max(0, k - 15)), 46);
        for (int i = 0; i < Recording.RecordProjectileFlights.MaxTracesPerType; i++)
            Fly(k => new Vector2(8f, 0.1f * System.Math.Max(0, k - 25)), 46);
        FlightLaw fitted = Laws.LawFor(type);
        Require(fitted.Gravity is { OnsetUpdate: 25 },
            $"after a ring's worth of flights with a later onset the fitted onset is the later one; fitted {fitted.Gravity}");

        Fly(k => new Vector2(8f, 0.1f * System.Math.Max(0, k - 25)), 100);
        for (int i = 0; i < 3; i++)
            Fly(k => new Vector2(8f, 0.1f * System.Math.Max(0, k - 25)), 46);
        int longest = 0;
        foreach (Recording.FlightTrace trace in Recording.RecordProjectileFlights.ClosedFor(type))
            longest = System.Math.Max(longest, trace.Steps.Count);
        Require(longest == 100, $"the long flight survives newer short ones; longest kept has {longest} steps");
        Recording.RecordProjectileFlights.Clear();
        Laws.Reset();
        return failures;
    }

    /// <summary>A style 1 projectile without the arrow flag takes none of the arrow's gravity branch, and its prior says so.</summary>
    private static int StraightPriorForABullet()
    {
        FlightLaw bullet = FlightLaw.Default(ProjectileID.Bullet);
        if (bullet.Gravity != null || bullet.Drag.Horizontal != 1f || bullet.Drag.Vertical != 1f)
        {
            EmitLedgerRows.Detail($"bullet prior: expected straight, got gravity {bullet.Gravity} drag {bullet.Drag.Horizontal}");
            return 1;
        }
        Console.WriteLine("bullet prior: straight, as AI_001 flies a style-1 projectile without the arrow flag");
        return 0;
    }

    private static int VerifySweptTerrainAndNoise()
    {
        Vector2 muzzle = new(160f, 500f);
        var target = new NPC { active = true, width = 10, height = 10, position = new Vector2(306f, 495f), velocity = Vector2.Zero, noGravity = true };
        var weapon = new WeaponId(0, 0, false, 10, 10f, 0f, 1, 0, 0f, ProjectileID.WoodenArrowFriendly, false);
        EnemyForecast forecast = ForecastEnemies.ForSingle(target);
        var enemies = new[] { forecast };
        CombatWorld world = CombatWorld.Current(muzzle, Main.LocalPlayer.Center, live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Revision);
        bool Lands()
        {
            PlanningBudget budget = PlanningBudget.Unbounded();
            return SolveAims.FirstLanding(weapon, muzzle, forecast, world, enemies, 0, ref budget) != null;
        }
        if (!Lands())
        {
            EmitLedgerRows.Detail("projectile sweep: clear line did not reach its target");
            return 1;
        }

        // A 16px ceiling tile lies between two 10px projectile endpoints. The old endpoint-only check skipped
        // this geometry; the simulated flight dies on the wall instead. Asked of one direct use rather than of
        // the sweep, because the sweep is allowed to answer a thin wall with a lob over it.
        static bool DirectHits(WeaponId weapon, Vector2 muzzle, EnemyForecast forecast, CombatWorld world, EnemyForecast[] enemies)
        {
            Vector2 aim = forecast.PredictedCentre(1);
            PlanningBudget budget = PlanningBudget.Unbounded();
            var use = Simulate.Simulate(weapon, muzzle, aim, Vector2.Normalize(aim - muzzle), world, enemies,
                ModifierState.None, 0, ref budget);
            foreach (var hit in use.Hits)
                if (hit.Slot == forecast.Slot)
                    return true;
            return false;
        }
        Tile wall = Main.tile[14, 31];
        wall.HasTile = true;
        wall.TileType = 1;
        bool blocked = !DirectHits(weapon, muzzle, forecast, world, enemies);
        wall.HasTile = false;
        bool reopened = DirectHits(weapon, muzzle, forecast, world, enemies);

        // An accuracy rotation off a landing aim must miss: simulate the rotated launch itself.
        PlanningBudget aimBudget = PlanningBudget.Unbounded();
        var landed = SolveAims.FirstLanding(weapon, muzzle, forecast, world, enemies, 0, ref aimBudget);
        bool noisyMisses = landed == null;
        if (landed != null)
        {
            Vector2 rotated = landed.Value.Aim.LaunchDirection.RotatedBy(MathHelper.ToRadians(4f));
            PlanningBudget simBudget = PlanningBudget.Unbounded();
            var use = Simulate.Simulate(weapon, muzzle, landed.Value.Aim.AimPoint, rotated, world, enemies, ModifierState.None, 0, ref simBudget);
            noisyMisses = true;
            foreach (var hit in use.Hits)
                if (hit.Slot == forecast.Slot)
                    noisyMisses = false;
        }
        if (!blocked || !reopened || !noisyMisses)
        {
            EmitLedgerRows.Detail($"projectile sweep: blocked={blocked} reopened={reopened} noisy-misses={noisyMisses}");
            return 1;
        }
        Console.WriteLine("projectile sweep: thin wall blocks the direct use, its removal reopens it, and an accuracy rotation is rejected when it misses");
        return 0;
    }

    /// <summary>
    /// The prior for this type, flown by the law's own step, against the native AI plus the engine's
    /// move, for <paramref name="ticks"/> ticks from the same launch. The box is compared too, because
    /// the sweep collides the box the sample claims and a wrong box proves a clear line the game would strike.
    /// </summary>
    private static int PriorMatchesNative(string name, int type, float speed, int ticks)
    {
        var native = new Projectile();
        native.SetDefaults(type);
        native.position = new Vector2(1000f, 1000f);
        native.velocity = new Vector2(speed, 0f);
        native.owner = Main.myPlayer;
        FlightLaw law = FlightLaw.Default(type);
        Vector2 lawPosition = native.position;
        Vector2 lawVelocity = native.velocity;
        int update = 0;

        for (int tick = 1; tick <= ticks; tick++)
        {
            native.VanillaAI();
            native.position += native.velocity;
            for (int sub = 0; sub < law.UpdatesPerTick; sub++)
                FlightLaw.Advance(in law, ref lawPosition, ref lawVelocity, ref update,
                    Vector2.Zero, Vector2.Zero, Vector2.Zero, wet: false);
            if (Vector2.Distance(native.position, lawPosition) > .001f || Vector2.Distance(native.velocity, lawVelocity) > .001f)
            {
                EmitLedgerRows.Detail($"projectile {name} tick {tick}: native pos={native.position} vel={native.velocity}; law pos={lawPosition} vel={lawVelocity}");
                return 1;
            }
        }
        Console.WriteLine($"projectile {name}: {ticks} native free-flight ticks, {native.width}x{native.height} box, the prior's onset/gravity/drag/cap matched");
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
    /// One shot: solve with whatever law currently stands, spawn the native projectile at the solved
    /// launch, fly it with the game's own AI until it meets the target's box, a solid or the world's
    /// edge, feeding the recorder when asked. Returns whether it hit.
    /// </summary>
    private static bool Shoot(int type, int itemType, float speed, Vector2 muzzle, NPC target, bool learn)
    {
        var weapon = new WeaponId(itemType, 0, false, 10, speed, 0f, 1, 0, 0f, type, false);
        EnemyForecast forecast = ForecastEnemies.ForSingle(target);
        var enemies = new[] { forecast };
        CombatWorld world = CombatWorld.Current(muzzle, Main.LocalPlayer.Center, live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Revision);
        PlanningBudget budget = PlanningBudget.Unbounded();
        var landed = SolveAims.FirstLanding(weapon, muzzle, forecast, world, enemies, 0, ref budget);
        if (landed == null)
            return false;
        var native = new Projectile();
        native.SetDefaults(type);
        native.whoAmI = 30; native.active = true; native.owner = Main.myPlayer;
        native.Center = muzzle;
        native.velocity = landed.Value.Aim.LaunchDirection * speed;
        var item = new Item();
        item.SetDefaults(itemType);
        if (learn)
            Recording.RecordProjectileFlights.NoteSpawn(30, native,
                new Terraria.DataStructures.EntitySource_ItemUse(Main.player[Main.myPlayer], item));
        bool hit = false;
        for (int tick = 1; tick <= 150; tick++)
        {
            native.VanillaAI();
            native.position += native.velocity;
            if (learn)
                Recording.RecordProjectileFlights.NoteStep(native);
            if (native.Hitbox.Intersects(target.Hitbox)) { hit = true; break; }
            if (!WorldGen.InWorld((int)(native.position.X / 16f), (int)(native.position.Y / 16f), 5)
                || Collision.SolidCollision(native.position, native.width, native.height))
                break;
        }
        if (learn)
            Recording.RecordProjectileFlights.NoteDeath(native);
        return hit;
    }

    private static float HitsPerShot(int type, int itemType, float speed, Vector2 muzzle, NPC target)
    {
        int hits = 0;
        for (int shot = 0; shot < MeasuredShots; shot++)
            if (Shoot(type, itemType, speed, muzzle, target, learn: false)) hits++;
        return hits / (float)MeasuredShots;
    }

    /// <summary>A vanilla arrow, which has a prior, lands from its first shot at thirty tiles.</summary>
    private static int APriorArrowHitsFromTheFirstShot()
    {
        Recording.RecordProjectileFlights.Clear();
        Laws.Reset();
        var (muzzle, target) = Range(30);
        float ratio = HitsPerShot(ProjectileID.WoodenArrowFriendly, ItemID.WoodenBow, 9.6f, muzzle, target);
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
    /// The measurement the lane is judged by. The learner believes the type flies straight — which is what
    /// any modded projectile starts as — and hits per shot are taken at that guess, then the player fires
    /// calibration shots with the learner watching until the fit recovers the game's numbers, then hits per
    /// shot are taken again with the learner frozen. The pass line, declared before the first run: after is above
    /// before, and after is at least <see cref="LearnedHitsPerShotFloor"/>. The learned numbers are then held
    /// against the game's, which the prior carries, so the fit is shown to recover the mechanism and not
    /// merely to land.
    /// </summary>
    private static int AnUnknownArcIsLearnedFromWatchedShots(string name, int type, int itemType, float speed, int tilesAway,
        int onset, float gravity, float drag, int onsetTolerance, float gravityTolerance, float dragTolerance)
    {
        Recording.RecordProjectileFlights.Clear();
        Laws.Reset();
        Laws.AssumeLaw(type, FlightLaw.Straight(type));
        var (muzzle, target) = Range(tilesAway);
        float before = HitsPerShot(type, itemType, speed, muzzle, target);

        bool Recovered(FlightLaw law)
            => law.Revision >= 1 && law.Gravity is { } term
                && System.Math.Abs(term.OnsetUpdate - onset) <= onsetTolerance
                && MathF.Abs(term.Acceleration - gravity) <= gravityTolerance
                && MathF.Abs(law.Drag.Horizontal - drag) <= dragTolerance;

        int calibration = 0;
        while (calibration < 6 && !Recovered(Laws.LawFor(type)))
        {
            Shoot(type, itemType, speed, muzzle, target, learn: true);
            calibration++;
        }
        FlightLaw learned = Laws.LawFor(type);
        float after = HitsPerShot(type, itemType, speed, muzzle, target);

        EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "EngineReplay", $"arc learning: {name} believed straight, hits per shot before learning at {tilesAway} tiles", before, "hits/shot", "up");
        EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "EngineReplay", $"arc learning: {name} after learning, hits per shot at {tilesAway} tiles", after, "hits/shot", "up");
        EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "EngineReplay", $"arc learning: {name} calibration shots until the first fit", calibration, "shots", "down");
        Console.WriteLine($"arc learning: {name} at {tilesAway} tiles: {before:0.00} hits per shot believed straight, {calibration} calibration shot(s), {after:0.00} after learning; learned gravity={learned.Gravity} drag={learned.Drag.Horizontal:0.000}");

        if (!Recovered(learned))
        {
            EmitLedgerRows.Detail($"{name}: the fit does not recover the game's numbers; learned gravity={learned.Gravity} drag={learned.Drag.Horizontal:0.000}");
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
