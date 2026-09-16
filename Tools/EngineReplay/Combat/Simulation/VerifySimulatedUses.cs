extern alias live;

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Recording = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording;
using LearnVolleys = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnVolleyShapes;
using LearnWalls = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnWallResponses;
using LearnChildren = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnChildSpawns;
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
/// What a use is predicted to put into the world: volley shares (S3), unlimited pierce (S1), overkill kept
/// whole in the sim (S2), timed children landing late (S4) and bit-identical reruns (S6). Knowledge is planted
/// — learned from synthetic uses or read from the game's samples — so these rows test simulation, not learning.
/// </summary>
internal static class VerifySimulatedUses
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
        LearnVolleys.Reset();
        Laws.Reset();
        LearnWalls.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnHitResponses.Reset();
        LearnChildren.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.WeaponEffects.Reset();
    }

    /// <summary>
    /// A fresh headless player holds zero damage modifiers — the game rebuilds them every tick — so a row that
    /// composes item numbers stands them up itself; without this the composition is zero and the shares divide
    /// by nothing. Every class, because GetTotalDamage combines all of them and one zero multiplicative zeroes
    /// the total.
    /// </summary>
    private static void StandUpDamage()
    {
        foreach (DamageClass damageClass in new DamageClass[] { DamageClass.Generic, DamageClass.Melee, DamageClass.Ranged, DamageClass.Magic, DamageClass.Summon, DamageClass.Throwing })
            Main.LocalPlayer.GetDamage(damageClass) = StatModifier.Default;
        Main.LocalPlayer.arrowDamage = StatModifier.Default;
        Main.LocalPlayer.bulletDamage = StatModifier.Default;
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

    private static void BuildWall(int tileX, int y0, int y1)
    {
        for (int y = y0; y <= y1; y++)
        {
            Tile tile = Main.tile[tileX, y];
            tile.HasTile = true;
            tile.TileType = 1;
            tile.Slope = 0;
            tile.IsHalfBlock = false;
            tile.LiquidAmount = 0;
        }
    }

    /// <summary>
    /// S3: a volley whose four slots each carry a quarter of the use's damage fires four pellets at a quarter
    /// each. Firing every slot at full damage — the behaviour before volleys — is the mutation this row kills.
    /// The ammo's projectile is substituted where the slot names none.
    /// </summary>
    public static int QuarterSharesLandAQuarterPerPellet()
    {
        Reset();
        Vector2 shooter = new(1000f, 1000f);
        Vector2 aim = new(1400f, 1000f);
        Main.LocalPlayer.Center = shooter;
        StandUpDamage();
        // A flintlock with musket balls composes to twenty, which quarters exactly.
        (float composedDamage, float composedSpeed) = LearnVolleys.ComposedStats(ItemID.FlintlockPistol, ItemID.MusketBall);
        int whole = (int)composedDamage;
        Require(whole >= 4 && whole % 4 == 0,
            $"the row needs a use damage divisible into quarters; flintlock with musket balls composes to {composedDamage:0.00}");
        int quarter = whole / 4;

        LearnFourPelletVolley(shooter, aim, composedSpeed, quarter);

        var shape = LearnVolleys.ShapeFor(ItemID.FlintlockPistol);
        var specs = shape.Expand(shooter, aim, Vector2.UnitX, ProjectileID.Bullet, composedDamage, composedSpeed);
        Require(specs.Count == 4, $"a four-pellet use fires four pellets, not {specs.Count}");
        foreach (var spec in specs)
        {
            Require(spec.Damage == quarter,
                $"a quarter share lands a quarter: pellet damage {spec.Damage}, want {quarter}");
            Require(spec.ProjectileType == ProjectileID.Bullet,
                $"an ammo slot fires the ammo's projectile; got type {spec.ProjectileType}");
            Require(MathF.Abs(spec.Velocity.Length() - composedSpeed) < 1e-3f,
                $"a full-speed slot fires at full speed; got {spec.Velocity.Length():0.000}, want {composedSpeed:0.000}");
        }
        float[] angles = new float[specs.Count];
        for (int i = 0; i < specs.Count; i++)
            angles[i] = specs[i].Velocity.ToRotation();
        Array.Sort(angles);
        Require(angles[3] - angles[0] > 0.1f,
            $"the volley keeps its spread; pellet angles span {angles[3] - angles[0]:0.000} rad");
        Console.WriteLine($"volley damage: four pellets at {quarter} each of {whole}, the ammo's type, the spread kept");
        return 0;
    }

    /// <summary>One complete player use of a four-pellet same-tick spread, taught to the volley shapes.</summary>
    private static void LearnFourPelletVolley(Vector2 shooter, Vector2 aim, float speed, int pelletDamage)
    {
        var use = new Recording.ProjectileUse
        {
            Id = 1,
            Shooter = Recording.Shooter.Player,
            ItemType = ItemID.FlintlockPistol,
            StartTick = 100,
            AimPoint = aim,
            ShooterCentre = shooter,
            BuffsAtStart = Array.Empty<int>(),
            Complete = true,
        };
        for (int pellet = 0; pellet < 4; pellet++)
        {
            use.Spawns.Add(new Recording.UseSpawn(10 + pellet, ProjectileID.Bullet, 100, shooter,
                new Vector2(speed, 0f).RotatedBy((pellet - 1.5f) * 0.06f), pelletDamage,
                ItemID.MusketBall, shooter, aim));
        }
        LearnVolleys.Learn(use);
    }

    /// <summary>
    /// S1: an unlimited-pierce use flown along twenty bodies strikes all twenty. Reinstating the cap of eight —
    /// the arsenal's old buffer — is the mutation this row kills.
    /// </summary>
    public static int UnlimitedPierceStrikesTwenty()
    {
        Reset();
        var sample = new Projectile();
        sample.SetDefaults(ProjectileID.DemonSickle);
        Require(sample.penetrate < 0,
            $"the row needs a type the game never spends; the sickle penetrates {sample.penetrate}");
        FlightLaw law = Laws.LawFor(ProjectileID.DemonSickle);
        float speed = 10f;
        float range = law.LifetimeUpdates * speed / Math.Max(1, law.UpdatesPerTick);
        Require(range > 1300f, $"the scythe must cross all twenty bodies in-bounds; range={range:0}px");
        Vector2 muzzle = new(100f, 1100f);
        Main.LocalPlayer.Center = muzzle;
        var enemies = new List<EnemyForecast>();
        for (int i = 0; i < 20; i++)
            enemies.Add(Forecast(1 + i, new Vector2(160f + i * 50f, 1100f), 30, 60, 1000f));
        var weapon = new WeaponId(ItemID.DemonScythe, 0, false, 30, speed, 5f, 20, 10, 0f,
            ProjectileID.DemonSickle, false);
        CombatWorld world = CombatWorld.Current(muzzle, muzzle, 0);
        PlanningBudget budget = PlanningBudget.Unbounded();
        SimulatedUse use = Simulate.Simulate(weapon, muzzle, new Vector2(1500f, 1100f), Vector2.UnitX,
            world, enemies, ModifierState.None, 0, ref budget);
        Require(!use.Cut && !budget.Cut, "an unbounded sim of one scythe must run to its lifetime");
        Require(use.Hits.Count == 20, $"twenty bodies in a line must take twenty hits, not {use.Hits.Count}");
        Require(use.Struck == 20, $"twenty distinct bodies must be struck, not {use.Struck}");
        for (int i = 0; i < 20; i++)
            Require(use.Hits[i].Slot == 1 + i, $"hit {i} must land on body {1 + i}, landed on {use.Hits[i].Slot}");
        Console.WriteLine($"simulated uses: the scythe strikes all twenty bodies, {use.TotalDamage:0} damage, no cap");
        return 0;
    }

    /// <summary>
    /// S2: four pellets on a body with less life than one pellet record four hits, each at full damage. The
    /// sim reserves nothing: stopping pellets at the body's death is the mutation this half kills. Crediting
    /// one kill and the body's life is the plan evaluator's half, asserted where the vector lands in phase D.
    /// </summary>
    public static int FourPelletsOnALowBodyRecordFourHits()
    {
        Reset();
        Vector2 muzzle = new(100f, 1100f);
        Vector2 aim = new(1500f, 1100f);
        Main.LocalPlayer.Center = muzzle;
        StandUpDamage();
        (float composedDamage, float composedSpeed) = LearnVolleys.ComposedStats(ItemID.FlintlockPistol, ItemID.MusketBall);
        int whole = (int)composedDamage;
        Require(whole >= 4 && whole % 4 == 0,
            $"the row needs a use damage divisible into quarters; flintlock composes to {composedDamage:0.00}");
        int quarter = whole / 4;
        LearnFourPelletVolley(muzzle, aim, composedSpeed, quarter);
        var enemies = new[] { Forecast(1, new Vector2(300f, 1100f), 80, 80, quarter - 1f) };
        var weapon = new WeaponId(ItemID.FlintlockPistol, 0, false, whole, composedSpeed, 5f, 20, 0, 0f,
            ProjectileID.Bullet, false);
        CombatWorld world = CombatWorld.Current(muzzle, muzzle, 0);
        PlanningBudget budget = PlanningBudget.Unbounded();
        SimulatedUse use = Simulate.Simulate(weapon, muzzle, aim, Vector2.UnitX, world, enemies,
            ModifierState.None, 0, ref budget);
        Require(!use.Cut && !budget.Cut, "an unbounded sim of one volley must run to its lifetime");
        Require(use.Hits.Count == 4, $"four pellets on a dying body record four hits, not {use.Hits.Count}");
        foreach (var hit in use.Hits)
        {
            Require(hit.Slot == 1, $"every pellet lands on the one body; hit on slot {hit.Slot}");
            Require(MathF.Abs(hit.Damage - quarter) < 0.01f,
                $"no pellet is trimmed by the body's remaining life; damage {hit.Damage:0.00}, want {quarter}");
        }
        Console.WriteLine($"simulated uses: four pellets at {quarter} each record four full hits on a {quarter - 1}-life body");
        return 0;
    }

    /// <summary>
    /// S4: a timed child's hits land at their simulated tick, after the parent's. The timer (period ten, one
    /// child, straight on at eight) is planted through the learner's own front door; landing children at the
    /// parent's tick is the mutation this row kills. The children are scythes rather than bullets, because a
    /// bullet child would spend its single pierce on the near body and never reach the far one — which is the
    /// engine's own immunity and pierce behaviour, not the row's subject.
    /// </summary>
    public static int TimedChildrenLandAfterTheirParent()
    {
        Reset();
        var parentSample = new Projectile();
        parentSample.SetDefaults(ProjectileID.Bullet);
        Require(parentSample.penetrate > 0,
            $"the row needs a parent that dies on its first body; the bullet penetrates {parentSample.penetrate}");
        var childSample = new Projectile();
        childSample.SetDefaults(ProjectileID.DemonScythe);
        Require(childSample.penetrate > 1,
            $"the row needs a child that flies through the near body; the scythe penetrates {childSample.penetrate}");
        Vector2 muzzle = new(100f, 1100f);
        Main.LocalPlayer.Center = muzzle;
        var trace = new Recording.FlightTrace { ProjectileType = ProjectileID.Bullet, UseSample = true };
        for (int update = 10; update <= 30; update += 10)
            trace.Children.Add(new Recording.ChildSpawn(update, ProjectileID.DemonScythe, Vector2.Zero,
                new Vector2(8f, 0f), Recording.ChildTrigger.Timer, 1f));
        LearnChildren.Learn(trace);
        var models = LearnChildren.ChildrenFor(ProjectileID.Bullet);
        Require(models.Count == 1 && models[0].Period.Median() == 10f,
            $"the planted timer must read period ten; got {models.Count} models");
        var enemies = new[]
        {
            Forecast(1, new Vector2(500f, 1100f), 40, 80, 1000f),
            Forecast(2, new Vector2(800f, 1100f), 40, 80, 1000f),
        };
        var weapon = new WeaponId(ItemID.FlintlockPistol, 0, false, 20, 10f, 5f, 20, 0, 0f,
            ProjectileID.Bullet, false);
        CombatWorld world = CombatWorld.Current(muzzle, muzzle, 0);
        PlanningBudget budget = PlanningBudget.Unbounded();
        SimulatedUse use = Simulate.Simulate(weapon, muzzle, new Vector2(1500f, 1100f), Vector2.UnitX,
            world, enemies, ModifierState.None, 0, ref budget);
        Require(!use.Cut && !budget.Cut, "an unbounded sim of one timed volley must run to its lifetime");
        int parentTick = -1;
        int childHits = 0, firstChildTick = int.MaxValue;
        foreach (var hit in use.Hits)
        {
            if (hit.Slot == 1 && parentTick < 0) parentTick = hit.Tick;
            if (hit.Slot == 2) { childHits++; firstChildTick = Math.Min(firstChildTick, hit.Tick); }
        }
        Require(parentTick >= 0, "the parent must strike the near body");
        Require(childHits == 3, $"all three timed scythes must reach the far body, each on its own spawn's immunity; far hits={childHits}");
        Require(firstChildTick > parentTick + 10,
            $"the child lands on its own late tick, not the parent's; parent={parentTick} child={firstChildTick}");
        Require(use.Deaths.Count == 4, $"the parent and its three timed children must all die, with no grandchildren; deaths={use.Deaths.Count}");
        Console.WriteLine($"simulated uses: the parent lands at tick {parentTick}, its timed child at {firstChildTick}");
        return 0;
    }

    /// <summary>
    /// S6: the same decision simulated twice returns identical hits, bounces, deaths, totals and paths.
    /// Drawing spread angles at random — or reading any clock, die or unordered map — is the mutation this
    /// row kills. The scene is a four-pellet volley against a reflecting backstop, so the determinism covers
    /// expansion, bounces and immunity alike; timers are S4's, and planting one here would recurse through
    /// children of children and buy nothing.
    /// </summary>
    public static int TheSameDecisionSimulatedTwiceIsIdentical()
    {
        Reset();
        Vector2 muzzle = new(100f, 1100f);
        Vector2 aim = new(1500f, 1100f);
        Main.LocalPlayer.Center = muzzle;
        StandUpDamage();
        (float composedDamage, float composedSpeed) = LearnVolleys.ComposedStats(ItemID.FlintlockPistol, ItemID.MusketBall);
        int whole = (int)composedDamage;
        LearnFourPelletVolley(muzzle, aim, composedSpeed, whole / 4);
        var contact = new Recording.FlightTrace { ProjectileType = ProjectileID.Bullet, UseSample = true };
        contact.Walls.Add(new Recording.WallContact(50, new Vector2(1400f, 1100f), new Vector2(10f, 0f),
            new Vector2(-10f, 0f), new Vector2(1f, 0f), false));
        contact.Walls.Add(new Recording.WallContact(60, new Vector2(1400f, 1100f), new Vector2(9f, 1f),
            new Vector2(-9f, 1f), new Vector2(1f, 0f), false));
        LearnWalls.Learn(contact);
        Require(LearnWalls.ResponseFor(ProjectileID.Bullet).Kind
            == live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.WallKind.Reflects,
            "the planted contacts must read reflects, or the scene has no bounce to be deterministic about");
        // One backstop only: a front wall would overlap the spawn boxes at the muzzle and bounce
        // every pellet on its first tick. Pellets that miss it expire past the edge, which the engine's
        // clamped collision reads deterministically.
        BuildWall(90, 55, 80);
        // Narrow bodies: the inner pellets strike the near one and die, the outer pair misses it and
        // both bounce off the backstop, so the scene holds hits, bounces and expiries at once.
        var enemies = new[]
        {
            Forecast(1, new Vector2(400f, 1100f), 30, 30, 1000f),
            Forecast(2, new Vector2(700f, 1100f), 30, 30, 1000f),
        };
        var weapon = new WeaponId(ItemID.FlintlockPistol, 0, false, whole, composedSpeed, 5f, 20, 0, 0f,
            ProjectileID.Bullet, false);
        CombatWorld world = CombatWorld.Current(muzzle, muzzle, 0);
        PlanningBudget firstBudget = PlanningBudget.Unbounded();
        PlanningBudget secondBudget = PlanningBudget.Unbounded();
        SimulatedUse first = Simulate.Simulate(weapon, muzzle, aim, Vector2.UnitX, world, enemies,
            ModifierState.None, 0, ref firstBudget);
        SimulatedUse second = Simulate.Simulate(weapon, muzzle, aim, Vector2.UnitX, world, enemies,
            ModifierState.None, 0, ref secondBudget);
        Require(!first.Cut && !second.Cut, "unbounded sims must run to their lifetimes");
        Require(first.Hits.Count == second.Hits.Count && first.Hits.Count > 0,
            $"both runs must hit identically and hit something; {first.Hits.Count} vs {second.Hits.Count}");
        for (int i = 0; i < first.Hits.Count; i++)
        {
            var a = first.Hits[i];
            var b = second.Hits[i];
            Require(a.Slot == b.Slot && a.Tick == b.Tick && a.Damage == b.Damage && a.PushX == b.PushX && a.Dead == b.Dead,
                $"hit {i} differs: ({a.Slot},{a.Tick},{a.Damage},{a.PushX},{a.Dead}) vs ({b.Slot},{b.Tick},{b.Damage},{b.PushX},{b.Dead})");
        }
        Require(first.Bounces.Count == second.Bounces.Count && first.Bounces.Count > 0,
            $"both runs must bounce identically and bounce somewhere; {first.Bounces.Count} vs {second.Bounces.Count}");
        for (int i = 0; i < first.Bounces.Count; i++)
        {
            var a = first.Bounces[i];
            var b = second.Bounces[i];
            Require(a.Position == b.Position && a.Tick == b.Tick && a.In == b.In && a.Out == b.Out,
                $"bounce {i} differs");
        }
        Require(first.Deaths.Count == second.Deaths.Count,
            $"both runs must die identically; {first.Deaths.Count} vs {second.Deaths.Count}");
        for (int i = 0; i < first.Deaths.Count; i++)
            Require(first.Deaths[i].Equals(second.Deaths[i]), $"death {i} differs");
        Require(first.TotalDamage == second.TotalDamage && first.Struck == second.Struck
            && first.Predictability == second.Predictability && first.EnemyConfidence == second.EnemyConfidence,
            "totals and confidences must be identical");
        Require(first.Paths.Count == second.Paths.Count, "both runs must fly the same spawns");
        for (int i = 0; i < first.Paths.Count; i++)
        {
            Require(first.Paths[i].Count == second.Paths[i].Count, $"path {i} differs in length");
            for (int j = 0; j < first.Paths[i].Count; j++)
                Require(first.Paths[i][j] == second.Paths[i][j], $"path {i} step {j} differs");
        }
        Console.WriteLine($"simulated uses: two runs agree exactly over {first.Hits.Count} hits, {first.Bounces.Count} bounces, {first.Deaths.Count} deaths");
        return 0;
    }
}
