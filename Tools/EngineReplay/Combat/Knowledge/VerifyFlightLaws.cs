extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Recording = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording;
using Laws = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.FitFlightLaws;
using FlightLaw = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.FlightLaw;
using Simulate = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.SimulateUse;
using WeaponId = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.WeaponId;
using CombatWorld = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.CombatWorld;
using ModifierState = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.ModifierState;
using EnemyForecast = live::AICompanion.Companion.Brain.Infrastructure.Observation.EnemyForecast;
using PlanningBudget = live::AICompanion.Companion.Brain.Activities.Combat.Planning.PlanningBudget;
using SimulatedUse = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.SimulatedUse;

/// <summary>
/// Flight laws fitted from watched flights: delayed gravity (K1), bounce (K2), homing (K3), pass-through (K4),
/// children (K5), modifier isolation (K7) and the unpredictable still fired (K9). Every row learns from native
/// <see cref="Projectile.VanillaAI"/> flights through the recorder's own notes, then holds the fitted law against
/// the game's code.
/// </summary>
internal static class VerifyFlightLaws
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reset()
    {
        VerifyCompanionLifecycle.Create();
        Recording.RecordProjectileFlights.Clear();
        Recording.GroupSpawnsIntoUses.Clear();
        Laws.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnWallResponses.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnHitResponses.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnChildSpawns.Reset();
    }

    /// <summary>One watched player flight of a type: spawn, native AI per tick with the recorder's step, death.</summary>
    private static void FlyPlayerFlight(int slot, int projectileType, int itemType, Vector2 muzzle, Vector2 launch, int ticks)
    {
        var item = new Item();
        item.SetDefaults(itemType);
        var native = new Projectile();
        native.SetDefaults(projectileType);
        native.whoAmI = slot;
        native.active = true;
        native.owner = Main.myPlayer;
        native.Center = muzzle;
        native.velocity = launch;
        Recording.RecordProjectileFlights.NoteSpawn(slot, native, new EntitySource_ItemUse(Main.player[Main.myPlayer], item));
        for (int tick = 1; tick <= ticks; tick++)
        {
            native.VanillaAI();
            Recording.RecordProjectileFlights.NoteStep(native);
            native.position += native.velocity;
        }
        Recording.RecordProjectileFlights.NoteDeath(native);
    }

    /// <summary>
    /// K1: the throwing knife's fitted law matches native tick for tick. Dropping the onset term — gravity and
    /// drag from update zero — is the mutation this row kills.
    /// </summary>
    public static int DelayedGravityMatchesNativeTickForTick()
    {
        Reset();
        Vector2 muzzle = new(1000f, 1000f);
        Main.LocalPlayer.Center = muzzle;
        Main.screenPosition = Vector2.Zero;
        Main.mouseX = 1400;
        Main.mouseY = 1000;
        FlyPlayerFlight(30, ProjectileID.ThrowingKnife, ItemID.ThrowingKnife, muzzle, new Vector2(10f, 0f), 46);
        FlyPlayerFlight(31, ProjectileID.ThrowingKnife, ItemID.ThrowingKnife, muzzle, new Vector2(10f, 0f), 46);

        FlightLaw law = Laws.LawFor(ProjectileID.ThrowingKnife);
        Require(law.Revision >= 1, $"two knife flights must fit a law; revision={law.Revision}");
        Require(law.Predictable, $"the knife's law must predict; residual={law.ResidualPerUpdate}");
        Require(law.Gravity is { OnsetUpdate: 19 } gravity && MathF.Abs(gravity.Acceleration - 0.4f) < 0.005f,
            $"the knife's gravity starts on its twentieth update at 0.4; fitted {law.Gravity}");
        Require(MathF.Abs(law.Drag.Horizontal - 0.97f) < 0.005f && law.Drag.OnsetUpdate == 19,
            $"the knife's drag damps from the same update; fitted {law.Drag}");

        var native = new Projectile();
        native.SetDefaults(ProjectileID.ThrowingKnife);
        native.Center = muzzle;
        native.velocity = new Vector2(10f, 0f);
        Vector2 position = native.position;
        Vector2 velocity = new Vector2(10f, 0f);
        int update = 0;
        for (int tick = 1; tick <= 40; tick++)
        {
            native.VanillaAI();
            native.position += native.velocity;
            FlightLaw.Advance(in law, ref position, ref velocity, ref update,
                Vector2.Zero, Vector2.Zero, Vector2.Zero, wet: false);
            Require(Vector2.Distance(native.position, position) <= 0.001f
                && Vector2.Distance(native.velocity, velocity) <= 0.001f,
                $"tick {tick}: native pos={native.position} vel={native.velocity}; law pos={position} vel={velocity}");
        }
        Console.WriteLine($"flight laws: the knife's fitted law matches native tick for tick over 40 ticks, residual {law.ResidualPerUpdate:0.0000}");
        return 0;
    }

    /// <summary>
    /// One watched player flight inside a walled box, moved the way the engine moves: the AI, then the engine's
    /// own <c>Collision.TileCollision</c>, then vanilla style-8's answer (full restitution on the blocked axes,
    /// death on the fifth contact), then the move. Returns the trace's wall contacts, in order.
    /// </summary>
    private static System.Collections.Generic.List<Recording.WallContact> FlyBoxFlight(int slot, Vector2 muzzle, Vector2 launch)
    {
        var item = new Item();
        item.SetDefaults(ItemID.WaterBolt);
        var native = new Projectile();
        native.SetDefaults(ProjectileID.WaterBolt);
        native.whoAmI = slot;
        native.active = true;
        native.owner = Main.myPlayer;
        native.Center = muzzle;
        native.velocity = launch;
        Recording.RecordProjectileFlights.NoteSpawn(slot, native, new EntitySource_ItemUse(Main.player[Main.myPlayer], item));
        for (int tick = 1; tick <= 400; tick++)
        {
            native.VanillaAI();
            Recording.RecordProjectileFlights.NoteStep(native);
            Vector2 before = native.velocity;
            Vector2 collided = Collision.TileCollision(native.position, before, native.width, native.height, false, false, 1);
            bool hitX = collided.X != before.X, hitY = collided.Y != before.Y;
            native.velocity = collided;
            if (hitX || hitY)
            {
                Recording.RecordProjectileFlights.NoteWallContact(native, before);
                native.ai[0]++;
                if (native.ai[0] >= 5f)
                {
                    // Death lands mid-tick, before any settling: the kill's own hook fills the contact.
                    native.active = false;
                    Recording.RecordProjectileFlights.NoteDeath(native);
                    break;
                }
                if (hitY) native.velocity.Y = 0f - before.Y;
                if (hitX) native.velocity.X = 0f - before.X;
            }
            native.position += native.velocity;
            Recording.RecordProjectileFlights.NoteSettled(native);
        }
        var closed = Recording.RecordProjectileFlights.ClosedFor(ProjectileID.WaterBolt);
        return closed.Count > 0
            ? new System.Collections.Generic.List<Recording.WallContact>(closed[closed.Count - 1].Walls)
            : new System.Collections.Generic.List<Recording.WallContact>();
    }

    private static void BuildBox()
    {
        for (int x = 38; x <= 72; x++)
            for (int y = 58; y <= 82; y++)
            {
                Tile tile = Main.tile[x, y];
                bool edge = x == 38 || x == 72 || y == 58 || y == 82;
                tile.HasTile = edge;
                tile.TileType = 1;
                tile.Slope = 0;
                tile.IsHalfBlock = false;
                tile.LiquidAmount = 0;
            }
    }

    /// <summary>
    /// K2: Water Bolt's learned law and wall response predict its bounce points in a fixture box. Disabling the
    /// reflect response — flying every contact as a death — is the mutation this row kills.
    /// </summary>
    public static int BouncePointsArePredictedInAFixtureBox()
    {
        Reset();
        BuildBox();
        Vector2 muzzle = new(55 * 16f, 70 * 16f);
        Main.LocalPlayer.Center = muzzle;
        Main.screenPosition = Vector2.Zero;
        Main.mouseX = 1400;
        Main.mouseY = 1000;
        FlyBoxFlight(30, muzzle, new Vector2(7f, 3f));

        var walls = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnWallResponses.ResponseFor(ProjectileID.WaterBolt);
        Require(walls.Kind == live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.WallKind.Reflects,
            $"water bolt contacts must learn reflects; got {walls.Kind}");
        Require(MathF.Abs(MathF.Abs(walls.RestitutionNormal) - 1f) < 0.01f && MathF.Abs(MathF.Abs(walls.RestitutionTangent) - 1f) < 0.01f,
            $"water bolt restitution must be full on both axes; got n={walls.RestitutionNormal:0.000} t={walls.RestitutionTangent:0.000}");
        Require(walls.BounceCount == 4, $"the fifth contact kills, so four precede it; learned {walls.BounceCount}");
        FlightLaw law = Laws.LawFor(ProjectileID.WaterBolt);
        Require(law.Predictable && law.Gravity == null, $"the bolt flies straight and predicts; gravity={law.Gravity} residual={law.ResidualPerUpdate}");

        // A fresh flight, predicted contact for contact: the learned law flown with the engine's axis answer
        // and the learned restitution, against the native flight's own contacts.
        var nativeContacts = FlyBoxFlight(31, muzzle, new Vector2(7f, 3f));
        Require(nativeContacts.Count == 5, $"the fresh flight must make five contacts; made {nativeContacts.Count}");
        var probe = new Projectile();
        probe.SetDefaults(ProjectileID.WaterBolt);
        probe.Center = muzzle;
        Vector2 position = probe.position;
        Vector2 velocity = new Vector2(7f, 3f);
        int update = 0;
        int predicted = 0;
        for (int tick = 1; tick <= 400 && predicted < nativeContacts.Count; tick++)
        {
            // The engine's order: the AI, the collision on the AI's velocity, the response, then the move.
            // Advance moves, so the contact is tested from the pre-move position it started the tick at.
            Vector2 preMove = position;
            FlightLaw.Advance(in law, ref position, ref velocity, ref update,
                Vector2.Zero, Vector2.Zero, Vector2.Zero, wet: false);
            Vector2 aiVelocity = velocity;
            Vector2 collided = Collision.TileCollision(preMove, aiVelocity, probe.width, probe.height, false, false, 1);
            bool hitX = collided.X != aiVelocity.X, hitY = collided.Y != aiVelocity.Y;
            velocity = collided;
            if (hitX || hitY)
            {
                Vector2 centre = preMove + new Vector2(probe.width, probe.height) / 2f;
                float error = Vector2.Distance(centre, nativeContacts[predicted].Position);
                Require(error <= 1f, $"bounce {predicted}: predicted {centre}, native {nativeContacts[predicted].Position}, error {error:0.00}px");
                predicted++;
                if (hitY) velocity.Y = aiVelocity.Y * walls.RestitutionNormal;
                if (hitX) velocity.X = aiVelocity.X * walls.RestitutionNormal;
                velocity *= walls.SpeedFactorAfterBounce;
                if (predicted >= walls.BounceCount + 1) break;
            }
            position = preMove + velocity;
        }
        Require(predicted == nativeContacts.Count, $"predicted {predicted} of {nativeContacts.Count} contacts");
        Console.WriteLine($"flight laws: water bolt reflects at full restitution, four bounces, all five contacts predicted within a pixel");
        return 0;
    }

    /// <summary>
    /// K7: a trace spawned under a +1 pierce modifier leaves the learned law and hit response unchanged.
    /// The setup flies the player's flight, because companion flights never fit laws (row K0) and the first
    /// version of this row flew the companion's, which passed only while the shooter gate was missing. The
    /// law half rides that gate; the hit response half is the modifier gate's own proof, because responses
    /// learn from every shooter's traces and only the modifier excludes this one. Letting modified traces
    /// update the hit response is the mutation this row kills.
    /// </summary>
    public static int ModifiedTracesLeaveLawAndHitResponseUnchanged()
    {
        Reset();
        Vector2 muzzle = new(1000f, 1000f);
        Main.LocalPlayer.Center = muzzle;
        Main.screenPosition = Vector2.Zero;
        Main.mouseX = 1400;
        Main.mouseY = 1000;
        var pierced = new live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.ModifierState(0, 1);

        FlyPlayerFlight(30, ProjectileID.ThrowingKnife, ItemID.ThrowingKnife, muzzle, new Vector2(10f, 0f), 46);
        FlightLaw before = Laws.LawFor(ProjectileID.ThrowingKnife);
        var hits = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnHitResponses.ResponseFor(ProjectileID.ThrowingKnife);
        int hitEvidence = hits.Evidence;
        Require(before.Revision >= 1, "one clean player flight must fit a law to compare against");

        FlyCompanionFlight(31, muzzle, new Vector2(10f, 0f), pierced);
        FlightLaw after = Laws.LawFor(ProjectileID.ThrowingKnife);
        Require(after.Revision == before.Revision && after.Evidence == before.Evidence
            && after.ResidualPerUpdate == before.ResidualPerUpdate,
            $"a +1 pierce trace must leave the law untouched; revision {before.Revision}->{after.Revision}, evidence {before.Evidence}->{after.Evidence}");
        Require(hits.Evidence == hitEvidence, $"a +1 pierce trace must leave the hit response untouched; evidence {hitEvidence}->{hits.Evidence}");
        Console.WriteLine($"flight laws: a +1 pierce flight changes neither the law (revision {after.Revision}) nor the hit response");
        return 0;
    }

    private static void FlyCompanionFlight(int slot, Vector2 muzzle, Vector2 launch,
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.ModifierState modifiers)
    {
        int useId = Recording.GroupSpawnsIntoUses.OpenCompanionUse(50, ItemID.ThrowingKnife, muzzle + launch * 10f, muzzle, 10f, 10f);
        var native = new Projectile();
        native.SetDefaults(ProjectileID.ThrowingKnife);
        native.whoAmI = slot;
        native.active = true;
        native.owner = Main.myPlayer;
        native.Center = muzzle;
        native.velocity = launch;
        Recording.RecordProjectileFlights.NoteCompanionSpawn(slot, native, useId, modifiers);
        for (int tick = 1; tick <= 46; tick++)
        {
            native.VanillaAI();
            Recording.RecordProjectileFlights.NoteStep(native);
            native.position += native.velocity;
        }
        Recording.RecordProjectileFlights.NoteDeath(native);
        Recording.GroupSpawnsIntoUses.CloseCompanionUse(useId, 51);
    }

    /// <summary>
    /// K3: a chlorophyte bullet's learned homing predicts its path to a placed body within tolerance.
    /// Dropping the homing term — flying the fitted drag alone — is the mutation this row kills.
    /// </summary>
    public static int HomingPredictsItsPathToAPlacedBody()
    {
        Reset();
        Vector2 muzzle = new(1000f, 1000f);
        Main.LocalPlayer.Center = muzzle;
        Main.screenPosition = Vector2.Zero;
        Main.mouseX = 1400;
        Main.mouseY = 1000;
        var body = new NPC();
        body.SetDefaults(NPCID.Zombie);
        body.whoAmI = 40;
        body.active = true;
        body.Center = muzzle + new Vector2(220f, -50f);
        Main.npc[40] = body;

        FlyHomingFlight(30, muzzle, new Vector2(10f, 0f), 30);
        FlightLaw law = Laws.LawFor(ProjectileID.ChlorophyteBullet);
        if (law.Homing is not { } homing)
            throw new InvalidOperationException($"chlorophyte flights must fit a homing term; fitted {DescribeLaw(law)}");
        Require(MathF.Abs(homing.Blend - 0.125f) < 0.03f,
            $"vanilla blends one eighth per sub-step; fitted {homing.Blend:0.000}");
        Require(law.Predictable, $"homing must predict; residual={law.ResidualPerUpdate:0.000}");

        // A fresh native flight against the law flown update for update, both steering at the same body.
        var native = new Projectile();
        native.SetDefaults(ProjectileID.ChlorophyteBullet);
        native.Center = muzzle;
        native.velocity = new Vector2(10f, 0f);
        Vector2 position = native.position;
        Vector2 velocity = new Vector2(10f, 0f);
        int update = 0;
        float totalError = 0f;
        int steps = 0;
        bool predictedHit = false;
        for (int tick = 1; tick <= 30 && !predictedHit; tick++)
        {
            for (int sub = 0; sub < 3; sub++)
            {
                native.VanillaAI();
                native.position += native.velocity;
                Vector2 toBody = body.Center - (position + new Vector2(native.width, native.height) / 2f);
                FlightLaw.Advance(in law, ref position, ref velocity, ref update, toBody,
                    Vector2.Zero, Vector2.Zero, wet: false);
                totalError += Vector2.Distance(native.position, position);
                steps++;
                if (new Rectangle((int)position.X, (int)position.Y, native.width, native.height).Intersects(body.Hitbox))
                    predictedHit = true;
            }
        }
        float mean = totalError / Math.Max(1, steps);
        Require(predictedHit, "the predicted path must reach the placed body, as the native flight does");
        Require(mean < 12f, $"mean path error {mean:0.00}px over {steps} sub-steps exceeds tolerance");
        Console.WriteLine($"flight laws: chlorophyte blend {homing.Blend:0.000} at radius {homing.Radius:0}, mean path error {mean:0.00}px, body reached");
        return 0;
    }

    private static string DescribeLaw(FlightLaw law)
        => $"rev={law.Revision} gravity={law.Gravity} drag={law.Drag} homing={law.Homing} residual={law.ResidualPerUpdate:0.000}";

    private static void FlyHomingFlight(int slot, Vector2 muzzle, Vector2 launch, int ticks)
    {
        var item = new Item();
        item.SetDefaults(ItemID.ChlorophyteBullet);
        var native = new Projectile();
        native.SetDefaults(ProjectileID.ChlorophyteBullet);
        native.whoAmI = slot;
        native.active = true;
        native.owner = Main.myPlayer;
        native.Center = muzzle;
        native.velocity = launch;
        Recording.RecordProjectileFlights.NoteSpawn(slot, native, new EntitySource_ItemUse(Main.player[Main.myPlayer], item));
        for (int tick = 1; tick <= ticks; tick++)
        {
            for (int sub = 0; sub < native.extraUpdates + 1; sub++)
            {
                native.VanillaAI();
                Recording.RecordProjectileFlights.NoteStep(native);
                native.position += native.velocity;
            }
            Recording.RecordProjectileFlights.NoteSettled(native);
        }
        Recording.RecordProjectileFlights.NoteDeath(native);
    }

    private static EnemyForecast Forecast(int slot, Vector2 centre, int width, int height, float life)
        => new()
        {
            Slot = slot,
            NpcType = NPCID.Zombie,
            Life = life,
            MaxLife = life,
            Defense = 0,
            KnockbackResist = 0.5f,
            Box = new Rectangle((int)(centre.X - width / 2f), (int)(centre.Y - height / 2f), width, height),
            Direction = 1,
            Velocity = Vector2.Zero,
        };

    /// <summary>
    /// K4: a tileCollide=false type reads passes with no contact ever seen, and the simulator flies it through
    /// a wall onto the body behind. Forcing die-on-contact — the behaviour before the pass response — is the
    /// mutation this row kills: the flight would end at the wall and the body go unhit. The shard's defaults
    /// carry the flag; the shadowbeam's do not (its AI style sets it mid-flight), which is why the beam is
    /// not the row's type.
    /// </summary>
    public static int PassThroughIsPredictedThroughAWall()
    {
        Reset();
        var sample = new Projectile();
        sample.SetDefaults(ProjectileID.CrystalShard);
        Require(!sample.tileCollide,
            $"the row needs a type the game flies through walls; the shard collides={sample.tileCollide}");
        var walls = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnWallResponses.ResponseFor(ProjectileID.CrystalShard);
        Require(walls.Kind == live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.WallKind.Passes,
            $"a tileCollide=false type with no contact seen must read passes, not unknown; got {walls.Kind}");
        FlightLaw law = Laws.LawFor(ProjectileID.CrystalShard);
        float speed = 10f;
        float range = law.LifetimeUpdates * speed / Math.Max(1, law.UpdatesPerTick);
        Require(range > 240f, $"the beam must reach past its wall in-bounds; range={range:0}px");
        Vector2 muzzle = new(800f, 1100f);
        float enemyDist = Math.Min(0.5f * range, 420f);
        Vector2 enemyCentre = muzzle + new Vector2(enemyDist, 0f);
        int wallX = (int)((muzzle.X + enemyDist / 2f) / 16f);
        for (int y = 55; y <= 72; y++)
        {
            Tile tile = Main.tile[wallX, y];
            tile.HasTile = true;
            tile.TileType = 1;
        }
        Main.LocalPlayer.Center = muzzle;
        var weapon = new WeaponId(ItemID.CrystalBullet, 0, false, 40, speed, 5f, 20, 10, 0f,
            ProjectileID.CrystalShard, false);
        CombatWorld world = CombatWorld.Current(muzzle, muzzle, 0);
        var enemies = new[] { Forecast(1, enemyCentre, 40, 40, 500f) };
        PlanningBudget budget = PlanningBudget.Unbounded();
        SimulatedUse use = Simulate.Simulate(weapon, muzzle, enemyCentre, Vector2.UnitX, world, enemies,
            ModifierState.None, 0, ref budget);
        Require(!use.Cut && !budget.Cut, "an unbounded sim of one beam must run to its lifetime");
        Require(use.Hits.Count == 1 && use.Hits[0].Slot == 1,
            $"the beam must strike the body behind the wall once; hits={use.Hits.Count}");
        Require(use.Bounces.Count == 0, $"pass-through records no bounce; bounces={use.Bounces.Count}");
        Console.WriteLine($"flight laws: the shard reads passes and strikes through its wall at {enemyDist:0}px");
        return 0;
    }

    /// <summary>
    /// K5: a splitting shot's children are learned by trigger and count through the engine's own parent source,
    /// and the simulator flies them onto the body past the parent's kill. The parent's flight is native
    /// <c>VanillaAI</c>; the kill itself does not run headless, so the three shards are filed with the parent
    /// source the engine's kill would file them with, at the death tick. Dropping child learning is the
    /// mutation this row kills: the far body would go unhit and the deaths would number one, not four.
    /// </summary>
    public static int SplittingShotsChildrenArePredictedByTriggerAndCount()
    {
        Reset();
        var parentSample = new Projectile();
        parentSample.SetDefaults(ProjectileID.CrystalBullet);
        Require(parentSample.penetrate > 0,
            $"the row needs a parent that dies on its first body; crystal bullet penetrates {parentSample.penetrate}");
        Vector2 muzzle = new(1000f, 1000f);
        Main.LocalPlayer.Center = muzzle;
        Main.screenPosition = Vector2.Zero;
        Main.mouseX = 1400;
        Main.mouseY = 1000;
        var item = new Item();
        item.SetDefaults(ItemID.CrystalBullet);
        var parent = new Projectile();
        parent.SetDefaults(ProjectileID.CrystalBullet);
        parent.whoAmI = 30;
        parent.active = true;
        parent.owner = Main.myPlayer;
        parent.Center = muzzle;
        parent.velocity = new Vector2(10f, 0f);
        Recording.RecordProjectileFlights.NoteSpawn(30, parent, new EntitySource_ItemUse(Main.player[Main.myPlayer], item));
        for (int tick = 1; tick <= 10; tick++)
        {
            parent.VanillaAI();
            Recording.RecordProjectileFlights.NoteStep(parent);
            parent.position += parent.velocity;
        }
        // Scattered sideways of the break, as real shrapnel: shards filed at the parent's centre
        // would be born overlapping the near body and die on it in the sim, which is the engine's own
        // behaviour for a fresh penetrate-one spawn, not the row's subject.
        float[] factors = new[] { 0.8f, 0.9f, 1.0f };
        for (int i = 0; i < 3; i++)
        {
            var child = new Projectile();
            child.SetDefaults(ProjectileID.CrystalShard);
            child.whoAmI = 31 + i;
            child.active = true;
            child.owner = Main.myPlayer;
            child.Center = parent.Center + new Vector2(0f, -60f);
            child.velocity = parent.velocity * factors[i];
            Recording.RecordProjectileFlights.NoteSpawn(31 + i, child, new EntitySource_Parent(parent));
        }
        Recording.RecordProjectileFlights.NoteDeath(parent);

        var models = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnChildSpawns.ChildrenFor(ProjectileID.CrystalBullet);
        Require(models.Count == 1, $"one child type must be learned; got {models.Count}");
        Require(models[0].ChildType == ProjectileID.CrystalShard, $"the child must be the shard; got {models[0].ChildType}");
        Require(models[0].Trigger == Recording.ChildTrigger.OnParentDeath, $"the death-tick spawns must read on-death; got {models[0].Trigger}");
        Require(models[0].Count.Median() == 3f, $"three shards must be counted; got {models[0].Count.Median()}");
        Require(MathF.Abs(models[0].OffsetAlong.Median()) < 0.01f && MathF.Abs(models[0].OffsetAcross.Median() + 60f) < 0.01f,
            $"the sideways scatter must be learned across the parent's frame; got ({models[0].OffsetAlong.Median():0.00}, {models[0].OffsetAcross.Median():0.00})");

        var weapon = new WeaponId(ItemID.CrystalBullet, 0, false, 20, 10f, 4f, 20, 0, 0f,
            ProjectileID.CrystalBullet, false);
        // The sim flies above the fixture box K2 builds in a shared flag run, and above the suite's floor.
        Vector2 simMuzzle = new(100f, 800f);
        CombatWorld world = CombatWorld.Current(simMuzzle, simMuzzle, 0);
        // Tall boxes: the sim flies the fitted straight law, but if the fit ever falls back to an
        // arrow prior the flight arcs and the row must still pin trigger, count and ordering, not gravity.
        var enemies = new[]
        {
            Forecast(1, new Vector2(400f, 800f), 40, 80, 100f),
            Forecast(2, new Vector2(700f, 740f), 40, 120, 1000f),
        };
        PlanningBudget budget = PlanningBudget.Unbounded();
        SimulatedUse use = Simulate.Simulate(weapon, simMuzzle, new Vector2(1500f, 800f), Vector2.UnitX,
            world, enemies, ModifierState.None, 0, ref budget);
        Require(!use.Cut && !budget.Cut, "an unbounded sim of one splitting shot must run to its lifetime");
        int parentTick = -1;
        int shardHits = 0, shardTick = -1;
        foreach (var hit in use.Hits)
        {
            if (hit.Slot == 1 && parentTick < 0) parentTick = hit.Tick;
            if (hit.Slot == 2) { shardHits++; shardTick = hit.Tick; }
        }
        Require(parentTick >= 0, "the parent must strike the near body");
        Require(shardHits == 3, $"all three predicted shards must strike the far body, each on its own spawn's immunity; far hits={shardHits}");
        Require(shardTick > parentTick, $"the shard must land after the parent's kill; parent={parentTick} shard={shardTick}");
        Require(use.Deaths.Count == 4, $"one parent and three flown shards must die; deaths={use.Deaths.Count}");
        Console.WriteLine($"flight laws: crystal bullet splits on death into three shards, the far body struck at tick {shardTick} after the kill at {parentTick}");
        return 0;
    }

    /// <summary>
    /// K9: a type no term fits is still fired and valued by outcomes, never refused. Restoring the refusal on
    /// unfittability is the mutation this row kills.
    /// </summary>
    public static int AnUnpredictableTypeStillFires()
    {
        Reset();
        Vector2 muzzle = new(1000f, 1000f);
        Main.LocalPlayer.Center = muzzle;
        Main.screenPosition = Vector2.Zero;
        Main.mouseX = 1400;
        Main.mouseY = 1000;
        var item = new Item();
        item.SetDefaults(ItemID.WoodenBow);
        // A rising, swinging flight: no gravity, drag, homing, steer or return explains the swing, and the arc
        // learner names the rise unfittable — so a restored refusal would bar the bow and this row would go red.
        var shot = new Projectile();
        shot.SetDefaults(ProjectileID.WoodenArrowFriendly);
        shot.whoAmI = 30;
        shot.active = true;
        shot.owner = Main.myPlayer;
        shot.Center = muzzle;
        Recording.RecordProjectileFlights.NoteSpawn(30, shot, new EntitySource_ItemUse(Main.player[Main.myPlayer], item));
        var random = new System.Random(7);
        Vector2 velocity = new Vector2(8f, 0f);
        for (int tick = 1; tick <= 60; tick++)
        {
            velocity = velocity.RotatedBy((float)(random.NextDouble() * 1.6 - 0.8));
            velocity.Y -= 0.05f;
            shot.velocity = velocity;
            shot.position += velocity;
            Recording.RecordProjectileFlights.NoteStep(shot);
        }
        Recording.RecordProjectileFlights.NoteDeath(shot);

        FlightLaw law = Laws.LawFor(ProjectileID.WoodenArrowFriendly);
        Require(!law.Predictable, $"a swinging rise must defeat the term library; residual={law.ResidualPerUpdate:0.000}");
        // Unpredictable is priced, not refused: a use simulated under this law carries zero law confidence, so the
        // planner weighs the prediction at nothing while the hand still fires it.
        var bowId = new WeaponId(ItemID.WoodenBow, 0, false, 10, 9.6f, 0f, 1, 0, 0f,
            ProjectileID.WoodenArrowFriendly, false);
        CombatWorld calm = CombatWorld.Current(muzzle, muzzle, 0);
        var calmEnemies = new[] { Forecast(1, muzzle + new Vector2(300f, 0f), 40, 40, 500f) };
        PlanningBudget calmBudget = PlanningBudget.Unbounded();
        SimulatedUse calmUse = Simulate.Simulate(bowId, muzzle, muzzle + new Vector2(300f, 0f), Vector2.UnitX,
            calm, calmEnemies, ModifierState.None, 0, ref calmBudget);
        Require(calmUse.Predictability == 0f,
            $"a use under an unpredictable law must carry zero law confidence; predictability={calmUse.Predictability}");
        var bow = new Item();
        bow.SetDefaults(ItemID.WoodenBow);
        Require(live::AICompanion.Companion.Inventory.CompanionGear.Accepts(
                live::AICompanion.Companion.Inventory.GearSlot.FirstWeapon, bow, out string reason),
            $"an unpredictable type is still fired, never refused; gear says {reason}");
        Console.WriteLine($"flight laws: residual {law.ResidualPerUpdate:0.000} marks the type unpredictable, and the bow stays accepted");
        return 0;
    }
}
