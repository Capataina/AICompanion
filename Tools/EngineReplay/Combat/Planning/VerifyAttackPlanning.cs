extern alias live;

using System;
using System.Collections.Generic;
using AICompanion.Tools.Ledger;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using CombatOutcome = live::AICompanion.Companion.Brain.Activities.Combat.Planning.CombatOutcome;
using KeepOnlyUndominated = live::AICompanion.Companion.Brain.Activities.Combat.Planning.KeepOnlyUndominated;
using WeighCombatObjectives = live::AICompanion.Companion.Brain.Activities.Combat.Planning.WeighCombatObjectives;
using CombatWeights = live::AICompanion.Companion.Brain.Activities.Combat.Planning.CombatWeights;
using SearchPlans = live::AICompanion.Companion.Brain.Activities.Combat.Planning.SearchAttackPlans;
using AttackPlan = live::AICompanion.Companion.Brain.Activities.Combat.Planning.AttackPlan;
using Budget = live::AICompanion.Companion.Brain.Activities.Combat.Planning.PlanningBudget;
using C = live::AICompanion.Companion.Brain.Activities.ActionContext;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using LearnVolleys = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnVolleyShapes;
using Laws = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.FitFlightLaws;
using FlightLaw = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.FlightLaw;
using Recording = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording;
using AttackLearning = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.AttackLearning;
using LearnHits = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnHitResponses;
using HitResponse = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.HitResponse;
using WallResponse = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.WallResponse;
using WallKind = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.WallKind;
using StandReason = live::AICompanion.Companion.Brain.Activities.Combat.Planning.StandReason;
using ForecastUses = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.ForecastUses;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;
using CachePlanned = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.CachePlannedSims;
using CacheSims = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.CacheSimulatedUses;
using Modifiers = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.ApplyCompanionModifiers;
using Positioner = live::AICompanion.Companion.Brain.Infrastructure.Position.Positioner;
using HostileAttackSources = live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources;
using ModifierState = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.ModifierState;
using Persist = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.PersistWeaponKnowledge;
using WeaponIdentity = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.WeaponIdentity;

/// <summary>
/// Planning rows (P): knowledge and forecasts planted, so they test planning and not learning.
/// P9 and P10 pin search behavior in the audit self-test, whose scenes price real searches, and P11 is
/// the self-test's cut row; file 8 names this folder for all three, but the behaviors moved with the scenes.
/// P1-P8 live here: one planted scene per behavior, each with its file-8 mutation baked in as a second
/// search the row requires to decide differently.
/// </summary>
internal static class VerifyAttackPlanning
{
    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    /// <summary>
    /// P1: a slime behind a travelling player is fought from inside his predicted region. The body starts
    /// with the slime behind the player, so the near-slime stand wins on time to first damage and the
    /// in-region stand pays travel; company gap is what sends the committed stand forward. The slime's
    /// life is planted high so neither stand kills it and damage-per-second cannot decide — a normal
    /// slime dies sooner from the near stand and the faster kill wins on damage whatever company says.
    /// The second weapon slot is empty so both stands price the same bow: with the knife in hand the
    /// near stand wins on the weapon, which is a different decision than the row is about. The hands are
    /// planted on a late cooldown so both stands fire once at nearly the same tick: damage-per-second is
    /// duration-normalised, so stands firing at different ticks can never tie on damage, and the row is
    /// about company, not about which tick the hands were ready on.
    /// Dropping the company gap objective — the file-8 mutation — must send it back to the slime.
    /// </summary>
    public static int CompanyFightsFromInsideThePredictedRegion()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear.Slots[1] = new Item();
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 60];
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
            tile.Slope = 0;
            tile.IsHalfBlock = false;
            tile.LiquidAmount = 0;
        }
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.statManaMax2 = 20;
        player.active = true;
        player.controlRight = true;
        player.Bottom = new Vector2(50 * 16f, 60 * 16f);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.life = companion.NPC.lifeMax = 100;
        companion.NPC.active = true;
        NPC slime = Main.npc[30];
        slime.SetDefaults(NPCID.GreenSlime);
        slime.whoAmI = 30;
        slime.active = true;
        slime.velocity = Vector2.Zero;
        slime.damage = 1;
        slime.life = slime.lifeMax = 1000;
        for (int tick = 0; tick < 30; tick++)
        {
            player.velocity = new Vector2(3f, 0f);
            player.position += player.velocity;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.Brain.Senses.Update(companion.NPC, player);
        }
        var region = companion.Brain.Senses.Intent.Region;
        Require(region.IsTravelling, "premise: the player must read as travelling, or company gap prices a standing player");
        float edge = region.Centre.X - region.HalfSize.X;
        companion.NPC.Bottom = new Vector2(edge - 100f, 60 * 16f - 40f);
        slime.Bottom = new Vector2(edge - 100f, 60 * 16f);
        player.velocity = new Vector2(3f, 0f);
        player.position += player.velocity;
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        companion.Brain.Senses.Update(companion.NPC, player);
        player.velocity = Vector2.Zero;
        region = companion.Brain.Senses.Intent.Region;
        Require(region.IsTravelling, "premise: the player must still read as travelling after the bodies are placed");
        Require(!region.Contains(slime.Center), "premise: the slime must start outside the predicted region, or staying is company already");
        Require(!region.Contains(companion.NPC.Center), "premise: the body must start outside with the slime, or the near stand pays no gap");
        VerifyOreWork.SettleReach(companion, player);
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = companion.Combat;
        combat.AssumeCooldown(150);
        CombatWeights weights = WeighCombatObjectives.ForSenses(ctx);
        Budget budget = Budget.Unbounded();
        SearchPlans.SearchResult result = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, weights, combat.NextPlanId++, ref budget);
        Require(result.Plan != null, "the company scene offers nothing: " + result.Reason);
        Vector2 stand = result.Plan.Segments[0].Stand.Stand;
        Require(region.Contains(stand), $"the committed stand must stay inside the predicted region; got {stand} against centre {region.Centre} half {region.HalfSize}");
        CombatWeights noCompany = weights with { CompanyGap = 0f };
        Budget mutationBudget = Budget.Unbounded();
        SearchPlans.SearchResult mutated = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, noCompany, combat.NextPlanId++, ref mutationBudget);
        Require(mutated.Plan != null, "the company-gap-less search offers nothing: " + mutated.Reason);
        Vector2 back = mutated.Plan.Segments[0].Stand.Stand;
        Require(!region.Contains(back), $"dropping the company gap must send the stand back to the slime; stayed inside at {back}");
        float liveGap = region.GapBeyond(stand);
        float mutatedGap = region.GapBeyond(back);
        Console.WriteLine($"attack planning: company holds {stand} inside the region (gap {liveGap:0}, value {result.Plan.Weighted:0.00}); without the gap objective it falls back to {back} (gap {mutatedGap:0}, value {mutated.Plan.Weighted:0.00})");
        return 0;
    }

    /// <summary>
    /// A fresh headless player holds zero damage modifiers, so a row that composes item numbers stands
    /// them up itself; without this the composition is zero and the shares divide by nothing.
    /// </summary>
    private static void StandUpDamage()
    {
        foreach (var damageClass in new Terraria.ModLoader.DamageClass[] { Terraria.ModLoader.DamageClass.Generic, Terraria.ModLoader.DamageClass.Melee, Terraria.ModLoader.DamageClass.Ranged, Terraria.ModLoader.DamageClass.Magic, Terraria.ModLoader.DamageClass.Summon, Terraria.ModLoader.DamageClass.Throwing })
            Main.LocalPlayer.GetDamage(damageClass) = Terraria.ModLoader.StatModifier.Default;
        Main.LocalPlayer.arrowDamage = Terraria.ModLoader.StatModifier.Default;
        Main.LocalPlayer.bulletDamage = Terraria.ModLoader.StatModifier.Default;
    }

    /// <summary>
    /// One complete player use of a four-pellet Boomstick spread, taught to the volley shapes: same-tick
    /// spawns at the given cone factor times ±1.5/±0.5 slots. The narrow cone (0.06, ±5.2°/±1.7°) holds
    /// the whole volley in a zombie's box close and the inner pair far; the wide cone (0.15,
    /// ±12.9°/±4.3°) misses a distant zombie entirely. The pellets' law is planted straight, so the
    /// row prices the spread and not an arc it never taught.
    /// </summary>
    private static void SeedBoomstickSpread(float cone = 0.06f)
    {
        StandUpDamage();
        (float composedDamage, float composedSpeed) = LearnVolleys.ComposedStats(ItemID.Boomstick, ItemID.MusketBall);
        int pelletDamage = Math.Max(1, (int)composedDamage / 4);
        Vector2 shooter = new(1000f, 1000f), aim = new(1400f, 1000f);
        var use = new Recording.ProjectileUse
        {
            Id = 1,
            Shooter = Recording.Shooter.Player,
            ItemType = ItemID.Boomstick,
            StartTick = 100,
            AimPoint = aim,
            ShooterCentre = shooter,
            BuffsAtStart = Array.Empty<int>(),
            Complete = true,
        };
        for (int pellet = 0; pellet < 4; pellet++)
            use.Spawns.Add(new Recording.UseSpawn(10 + pellet, ProjectileID.Bullet, 100, shooter,
                new Vector2(composedSpeed, 0f).RotatedBy((pellet - 1.5f) * cone), pelletDamage,
                ItemID.MusketBall, shooter, aim));
        LearnVolleys.Learn(use);
        Laws.AssumeLaw(ProjectileID.Bullet, FlightLaw.Straight(ProjectileID.Bullet));
        Require(LearnVolleys.HasShape(ItemID.Boomstick), "premise: the seeded spread teaches no shape");
    }

    /// <summary>
    /// P2: a flat long weapon's stand is at range and a spread weapon's close, against one lone target.
    /// The zombie stands far from the player, so the region's own samples are far stands and BestRange's
    /// peaks are close ones; the bow prices both and takes the far one on time, the learned Boomstick
    /// takes the close one on damage. Forgetting the spread — the file-8 mutation, a shotgun priced as
    /// the single shot it fires unobserved — must send the stand back out to range.
    /// </summary>
    public static int RangeFollowsTheWeaponAgainstOneLoneTarget()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 60];
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
            tile.Slope = 0;
            tile.IsHalfBlock = false;
            tile.LiquidAmount = 0;
        }
        SeedBoomstickSpread();
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.statManaMax2 = 20;
        player.active = true;
        player.Bottom = new Vector2(50 * 16f, 60 * 16f);
        companion.NPC.Bottom = new Vector2(47 * 16f, 60 * 16f - 40f);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.life = companion.NPC.lifeMax = 100;
        companion.NPC.active = true;
        NPC zombie = Main.npc[30];
        zombie.SetDefaults(NPCID.Zombie);
        zombie.whoAmI = 30;
        zombie.active = true;
        zombie.velocity = Vector2.Zero;
        zombie.Bottom = new Vector2(87 * 16f, 60 * 16f);
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        VerifyOreWork.SettleReach(companion, player);
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = companion.Combat;
        var gear = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear;

        gear.Slots[0].SetDefaults(ItemID.WoodenBow);
        gear.Slots[1] = new Item();
        CombatWeights bowWeights = WeighCombatObjectives.ForSenses(ctx);
        Budget bowBudget = Budget.Unbounded();
        SearchPlans.SearchResult bow = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, bowWeights, combat.NextPlanId++, ref bowBudget);
        Require(bow.Plan != null, "the bow search offers nothing: " + bow.Reason);
        float bowDist = Vector2.Distance(bow.Plan.Segments[0].Stand.Stand, zombie.Center);

        gear.Slots[0].SetDefaults(ItemID.Boomstick);
        CombatWeights spreadWeights = WeighCombatObjectives.ForSenses(ctx);
        Budget spreadBudget = Budget.Unbounded();
        SearchPlans.SearchResult spread = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, spreadWeights, combat.NextPlanId++, ref spreadBudget);
        Require(spread.Plan != null, "the spread search offers nothing: " + spread.Reason);
        float spreadDist = Vector2.Distance(spread.Plan.Segments[0].Stand.Stand, zombie.Center);

        LearnVolleys.Reset();
        CombatWeights singleWeights = WeighCombatObjectives.ForSenses(ctx);
        Budget singleBudget = Budget.Unbounded();
        SearchPlans.SearchResult single = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, singleWeights, combat.NextPlanId++, ref singleBudget);
        Require(single.Plan != null, "the unlearned-spread search offers nothing: " + single.Reason);
        float singleDist = Vector2.Distance(single.Plan.Segments[0].Stand.Stand, zombie.Center);

        Require(bowDist > 450f, $"the flat weapon must stand off at range; bow stand {bowDist:0}px from the zombie");
        Require(spreadDist < 400f, $"the spread weapon must close in; spread stand {spreadDist:0}px from the zombie");
        Require(spreadDist < bowDist - 100f, $"the spread stand must sit clearly inside the bow's; spread {spreadDist:0}px, bow {bowDist:0}px");
        Require(singleDist > 450f, $"ignoring the spread must send the shotgun back out to range; unlearned stand {singleDist:0}px");
        Console.WriteLine($"attack planning: bow stands at {bowDist:0}px, learned spread closes to {spreadDist:0}px, unlearned spread falls back to {singleDist:0}px");
        return 0;
    }

    /// <summary>Forty outcomes for the burst weapon, debuffed and plain in turn, from a seeded generator.</summary>
    private static void TeachBurst(int item, int npc = NPCID.Zombie)
    {
        var random = new Random(11);
        for (int i = 0; i < 40; i++)
        {
            bool debuffed = i % 2 == 0;
            float[] x = AttackLearning.Context(150f + 400f * (float)random.NextDouble(), 800f, 0f, 0.1f, (float)random.NextDouble(), 0,
                (float)random.NextDouble(), 6f, debuffed);
            AttackLearning.Observe(item, npc, x, (debuffed ? 1.6f : .6f) + .05f * (2f * (float)random.NextDouble() - 1f));
        }
    }

    /// <summary>
    /// P3: a shotgun that wins only close closes in at full life and holds range at low life. The
    /// Boomstick's learned spread peaks on the box up close, so a healthy body takes that stand; a
    /// wounded body pays the close stand's harm and keeps the far one. Holding the companion-harm
    /// weight at its healthy base — the file-8 mutation, harm that does not rise with missing life —
    /// must send the wounded body back in.
    /// </summary>
    public static int SpreadClosesAtFullLifeAndHoldsRangeAtLowLife()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 60];
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
            tile.Slope = 0;
            tile.IsHalfBlock = false;
            tile.LiquidAmount = 0;
        }
        SeedBoomstickSpread();
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.statManaMax2 = 20;
        player.active = true;
        player.Bottom = new Vector2(50 * 16f, 60 * 16f);
        companion.NPC.Bottom = new Vector2(47 * 16f, 60 * 16f - 40f);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.life = companion.NPC.lifeMax = 100;
        companion.NPC.active = true;
        NPC boss = Main.npc[30];
        boss.SetDefaults(NPCID.Zombie);
        boss.whoAmI = 30;
        boss.active = true;
        boss.velocity = Vector2.Zero;
        boss.damage = 14;
        boss.Bottom = new Vector2(87 * 16f, 60 * 16f);
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        VerifyOreWork.SettleReach(companion, player);
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = companion.Combat;
        var gear = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear;
        gear.Slots[0].SetDefaults(ItemID.Boomstick);
        gear.Slots[1] = new Item();

        CombatWeights healthyWeights = WeighCombatObjectives.ForSenses(ctx);
        Budget healthyBudget = Budget.Unbounded();
        SearchPlans.SearchResult healthy = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, healthyWeights, combat.NextPlanId++, ref healthyBudget);
        Require(healthy.Plan != null, "the healthy shotgun search offers nothing: " + healthy.Reason);
        float healthyDist = Vector2.Distance(healthy.Plan.Segments[0].Stand.Stand, boss.Center);
        string p3stands = string.Join(",", System.Linq.Enumerable.Select(healthy.Assessed, a =>
            a.Proposal.Reason + ":" + Vector2.Distance(a.Proposal.Stand, boss.Center).ToString("0") + "/" + a.Verdict.Reach));
        Require(healthyDist < 400f, $"at full life the shotgun must close in; stand {healthyDist:0}px {healthy.Plan.Segments[0].Stand.Reason} w{healthy.Plan.Weighted:0.00} dps {healthy.Plan.Outcome.DamagePerSecond:0.00} harm {healthy.Plan.Outcome.CompanionHarmTaken:0.00} CH {healthyWeights.CompanionHarm:0.00} assessed {p3stands}");

        companion.NPC.life = 20;
        companion.Brain.Senses.Update(companion.NPC, player);
        ctx = new C(companion, companion.Brain.Senses);
        CombatWeights hurtWeights = WeighCombatObjectives.ForSenses(ctx);
        Budget hurtBudget = Budget.Unbounded();
        SearchPlans.SearchResult hurt = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, hurtWeights, combat.NextPlanId++, ref hurtBudget);
        Require(hurt.Plan != null, "the wounded shotgun search offers nothing: " + hurt.Reason);
        float hurtDist = Vector2.Distance(hurt.Plan.Segments[0].Stand.Stand, boss.Center);
        string p3front = string.Join(";", System.Linq.Enumerable.Select(hurt.Front, p =>
            p.Segments[0].Stand.Reason + "@" + Vector2.Distance(p.Segments[0].Stand.Stand, boss.Center).ToString("0")
            + "w" + p.Weighted.ToString("0.00") + "h" + p.Outcome.CompanionHarmTaken.ToString("0.00")
            + "d" + p.Outcome.DamagePerSecond.ToString("0.00")));
        Require(hurtDist > 450f,
            $"at low life the shotgun must hold range; stand {hurtDist:0}px {hurt.Plan.Segments[0].Stand.Reason} healthy {healthyDist:0}px w{healthy.Plan.Weighted:0.00} CH {healthyWeights.CompanionHarm:0.00}->{hurtWeights.CompanionHarm:0.00} front {p3front}");
        Require(hurtDist > healthyDist + 80f, $"the wounded stand must sit clearly outside the healthy one; wounded {hurtDist:0}px, healthy {healthyDist:0}px");

        CombatWeights constantHarm = hurtWeights with { CompanionHarm = Weights.CombatWeightCompanionHarm * 0.5f };
        Budget mutationBudget = Budget.Unbounded();
        SearchPlans.SearchResult mutated = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, constantHarm, combat.NextPlanId++, ref mutationBudget);
        Require(mutated.Plan != null, "the constant-harm search offers nothing: " + mutated.Reason);
        float mutatedDist = Vector2.Distance(mutated.Plan.Segments[0].Stand.Stand, boss.Center);
        Require(mutatedDist < 400f, $"a constant companion-harm weight must send the wounded body back in; stand {mutatedDist:0}px");
        Console.WriteLine($"attack planning: healthy shotgun closes to {healthyDist:0}px, wounded holds {hurtDist:0}px, constant harm falls back to {mutatedDist:0}px");
        return 0;
    }

    /// <summary>
    /// P4: two enemies in a line are fought from the line, and the nearer dies first. The arrow's
    /// pass-through is planted — two bodies per penetrate, full damage to the second — so one shot
    /// wounds both and the nearer's shorter life dies first by construction. The body starts well off
    /// their line, so staying put is not a pierce stand; the stand must sit on the line outside the
    /// pair, and the opening use must name the nearer. Forgetting the
    /// pass-through — the file-8 mutation, no pierce priced — must drop the plan's value, because
    /// the line stand's second body was the value.
    /// </summary>
    public static int TwoInALineAreFoughtFromTheLineNearerFirst()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 60];
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
            tile.Slope = 0;
            tile.IsHalfBlock = false;
            tile.LiquidAmount = 0;
        }
        StandUpDamage();
        var pierce = new HitResponse { ProjectileType = ProjectileID.WoodenArrowFriendly };
        pierce.PierceRatio.Add(2f);
        LearnHits.AssumeResponse(pierce);
        Require(LearnHits.ResponseFor(ProjectileID.WoodenArrowFriendly).BodiesPerPenetrate == 2f,
            "premise: the planted pass-through reads two bodies per penetrate");
        Laws.AssumeLaw(ProjectileID.WoodenArrowFriendly, FlightLaw.Straight(ProjectileID.WoodenArrowFriendly));
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.statManaMax2 = 20;
        player.active = true;
        player.Bottom = new Vector2(50 * 16f, 60 * 16f);
        companion.NPC.Bottom = new Vector2(47 * 16f, 60 * 16f - 160f);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.life = companion.NPC.lifeMax = 100;
        companion.NPC.active = true;
        NPC nearer = Main.npc[30];
        nearer.SetDefaults(NPCID.Zombie);
        nearer.whoAmI = 30;
        nearer.active = true;
        nearer.velocity = Vector2.Zero;
        nearer.life = nearer.lifeMax = 30;
        nearer.Bottom = new Vector2(69 * 16f, 60 * 16f);
        NPC farther = Main.npc[31];
        farther.SetDefaults(NPCID.Zombie);
        farther.whoAmI = 31;
        farther.active = true;
        farther.velocity = Vector2.Zero;
        farther.life = farther.lifeMax = 100;
        farther.Bottom = new Vector2(93 * 16f, 60 * 16f);
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        VerifyOreWork.SettleReach(companion, player);
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = companion.Combat;
        var gear = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear;
        gear.Slots[0].SetDefaults(ItemID.WoodenBow);
        gear.Slots[1] = new Item();

        CombatWeights weights = WeighCombatObjectives.ForSenses(ctx);
        Budget budget = Budget.Unbounded();
        SearchPlans.SearchResult result = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, weights, combat.NextPlanId++, ref budget);
        Require(result.Plan != null, "the pierce scene offers nothing: " + result.Reason);
        Vector2 stand = result.Plan.Segments[0].Stand.Stand;
        float lineY = (nearer.Center.Y + farther.Center.Y) / 2f;
        var enemies = combat.EnsureForecast(ctx);
        var probe = ForecastUses.Forecast(ctx, combat.Weapons[0], 0, nearer, new Vector2(950f, lineY), record: false, 0, enemies, out string probeRej, out _);
        Require(probe != null, "premise: a fixed line point forecasts a shot at the nearer: " + probeRej);
        bool probeNearer = false, probeFarther = false;
        foreach (var hit in probe.Hits)
        {
            if (hit.Target == nearer.whoAmI) probeNearer = true;
            if (hit.Target == farther.whoAmI) probeFarther = true;
        }
        Require(probeNearer && probeFarther,
            $"premise: one shot from the line must strike both zombies; nearer={probeNearer} farther={probeFarther}");
        Require(MathF.Abs(stand.Y - lineY) < 30f, $"the stand must sit on the pair's line; stand {stand}, line y {lineY:0}");
        Require(stand.X < nearer.Center.X - 50f || stand.X > farther.Center.X + 50f,
            $"the stand must sit outside the pair, not between them; stand {stand}");
        int openerTarget = result.Plan.Segments[0].Uses[0].TargetSlot;
        Require(openerTarget == nearer.whoAmI, $"the opening use must name the nearer zombie; names {openerTarget}");
        var aimed = ForecastUses.Forecast(ctx, combat.Weapons[0], 0, nearer, stand, record: false, 0, enemies, out string rej, out _);
        Require(aimed != null, "premise: the line stand forecasts a shot at the nearer: " + rej);
        bool strikesNearer = false, strikesFarther = false;
        foreach (var hit in aimed.Hits)
        {
            if (hit.Target == nearer.whoAmI) strikesNearer = true;
            if (hit.Target == farther.whoAmI) strikesFarther = true;
        }
        Require(strikesNearer && strikesFarther,
            $"premise: one shot from the line stand must strike both zombies; nearer={strikesNearer} farther={strikesFarther}");

        LearnHits.AssumeResponse(new HitResponse { ProjectileType = ProjectileID.WoodenArrowFriendly });
        CombatWeights plainWeights = WeighCombatObjectives.ForSenses(ctx);
        Budget plainBudget = Budget.Unbounded();
        SearchPlans.SearchResult plain = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, plainWeights, combat.NextPlanId++, ref plainBudget);
        Require(plain.Plan != null, "the pierce-less search offers nothing: " + plain.Reason);
        Require(result.Plan.Weighted > plain.Plan.Weighted,
            $"pricing the pass-through must beat ignoring it; with {result.Plan.Weighted:0.00}, without {plain.Plan.Weighted:0.00}");
        Console.WriteLine($"attack planning: pierce stand {stand} on the line opens nearer (value {result.Plan.Weighted:0.00}); without pierce {plain.Plan.Weighted:0.00}");
        return 0;
    }

    /// <summary>
    /// P5: a two-segment plan, far while goons live, close after. Weak high-damage goons sit on the
    /// boss, so a close stand while they live is a beating; the bow from range clears them, and the
    /// shotgun then closes on the boss they were covering. Depth one — the file-8 mutation, which
    /// can only stand once — never produces the second segment.
    /// </summary>
    public static int GoonsThenBossEarnsTwoSegments()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 60];
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
            tile.Slope = 0;
            tile.IsHalfBlock = false;
            tile.LiquidAmount = 0;
        }
        StandUpDamage();
        SeedBoomstickSpread(0.15f);
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.statManaMax2 = 20;
        player.active = true;
        player.Bottom = new Vector2(47 * 16f, 60 * 16f);
        companion.NPC.Bottom = new Vector2(47 * 16f, 60 * 16f - 40f);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.life = companion.NPC.lifeMax = 100;
        companion.NPC.active = true;
        NPC goon = Main.npc[30];
        goon.SetDefaults(NPCID.Zombie);
        goon.whoAmI = 30;
        goon.active = true;
        goon.velocity = Vector2.Zero;
        goon.damage = 60;
        goon.life = goon.lifeMax = 6;
        goon.Bottom = new Vector2(87 * 16f, 60 * 16f);
        NPC goonB = Main.npc[31];
        goonB.SetDefaults(NPCID.Zombie);
        goonB.whoAmI = 31;
        goonB.active = true;
        goonB.velocity = Vector2.Zero;
        goonB.damage = 60;
        goonB.life = goonB.lifeMax = 6;
        goonB.Bottom = new Vector2(89 * 16f, 60 * 16f);
        NPC boss = Main.npc[32];
        boss.SetDefaults(NPCID.Zombie);
        boss.whoAmI = 32;
        boss.active = true;
        boss.velocity = Vector2.Zero;
        boss.damage = 12;
        boss.life = boss.lifeMax = 500;
        boss.Bottom = new Vector2(88 * 16f, 60 * 16f);
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        VerifyOreWork.SettleReach(companion, player);
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = companion.Combat;
        Require(companion.Brain.Senses.Threats.Threats.Count >= 3, "premise: goons and boss sensed as threats");
        var gear = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear;
        gear.Slots[0].SetDefaults(ItemID.WoodenBow);
        gear.Slots[1].SetDefaults(ItemID.Boomstick);

        CombatWeights weights = WeighCombatObjectives.ForSenses(ctx);
        Budget budget = Budget.Unbounded();
        SearchPlans.SearchResult result = SearchPlans.Search(ctx, combat, companion.Brain.Positioner,
            _ => true, weights, combat.NextPlanId++, ref budget);
        Require(result.Plan != null, "the goons scene offers nothing: " + result.Reason);
        string p5front = string.Join(";", System.Linq.Enumerable.Select(result.Front, p =>
            p.Segments.Length + ":" + p.Segments[0].Stand.Reason + "@" + Vector2.Distance(p.Segments[0].Stand.Stand, boss.Center).ToString("0")
            + (p.Segments.Length > 1 ? ">" + p.Segments[1].Stand.Reason + "@" + Vector2.Distance(p.Segments[1].Stand.Stand, boss.Center).ToString("0") : "")
            + "w" + p.Weighted.ToString("0.00")));
        string p5deep = result.DeeperAssessed == null ? "" : string.Join(",", System.Linq.Enumerable.Select(result.DeeperAssessed, d =>
            d.Proposal.Reason + ":" + Vector2.Distance(d.Proposal.Stand, boss.Center).ToString("0") + "/" + d.Verdict.Reach));
        Require(result.Plan.Segments.Length == 2,
            $"the committed plan must be far then close; got {result.Plan.Segments.Length} segment(s) {result.Plan.Segments[0].Stand.Reason} at {result.Plan.Segments[0].Stand.Stand} front {p5front} candidates {result.CandidatesEvaluated} deeper {p5deep}");
        float firstDist = Vector2.Distance(result.Plan.Segments[0].Stand.Stand, boss.Center);
        float secondDist = Vector2.Distance(result.Plan.Segments[1].Stand.Stand, boss.Center);
        Require(firstDist > 300f, $"the first segment must stand off while goons live; {firstDist:0}px from the boss");
        Require(secondDist < 250f, $"the second segment must close after; {secondDist:0}px from the boss");

        CombatWeights singleWeights = WeighCombatObjectives.ForSenses(ctx);
        Budget singleBudget = Budget.Unbounded();
        SearchPlans.SearchResult single = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, singleWeights, combat.NextPlanId++, ref singleBudget);
        Require(single.Plan != null, "the depth-one search offers nothing: " + single.Reason);
        foreach (AttackPlan candidate in single.Front)
            Require(candidate.Segments.Length == 1, "depth one must offer only single segments");
        Console.WriteLine($"attack planning: far {firstDist:0}px then close {secondDist:0}px committed (value {result.Plan.Weighted:0.00}); depth one {single.Plan.Weighted:0.00}");
        return 0;
    }

    /// <summary>
    /// P6: a low flank stand for a gravity-and-floor piercer against a group. Two slimes hold a
    /// row on the floor and the body hovers above their middle, so the flanks fall left and right;
    /// the arrow flies with gravity and a reflecting floor, piercing both bodies along the row, and
    /// the flank's two kills beat any direct stand's one. Removing the floor proposals — the file-8
    /// mutation — leaves only ranging stands, which this row kills by asserting the reason.
    /// </summary>
    public static int AFloorRollerTakesTheLowFlank()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 60];
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
            tile.Slope = 0;
            tile.IsHalfBlock = false;
            tile.LiquidAmount = 0;
        }
        StandUpDamage();
        var pierce = new HitResponse { ProjectileType = ProjectileID.WoodenArrowFriendly };
        pierce.PierceRatio.Add(2f);
        LearnHits.AssumeResponse(pierce);
        FlightLaw straight = FlightLaw.Default(ProjectileID.WoodenArrowFriendly);
        Require(straight.Gravity != null, "premise: the arrow law carries gravity");
        Laws.AssumeLaw(ProjectileID.WoodenArrowFriendly, straight with
        {
            Wall = new WallResponse
            {
                ProjectileType = ProjectileID.WoodenArrowFriendly,
                Kind = WallKind.Reflects,
                RestitutionNormal = 1f,
                RestitutionTangent = 1f,
                BounceCount = 4,
            },
        });
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.statManaMax2 = 20;
        player.active = true;
        player.Bottom = new Vector2(950f, 60 * 16f);
        companion.NPC.Bottom = new Vector2(950f, 60 * 16f - 160f);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.life = companion.NPC.lifeMax = 100;
        companion.NPC.active = true;
        NPC left = Main.npc[30];
        left.SetDefaults(NPCID.GreenSlime);
        left.whoAmI = 30;
        left.active = true;
        left.velocity = Vector2.Zero;
        left.damage = 10;
        left.life = left.lifeMax = 12;
        left.Bottom = new Vector2(935f, 60 * 16f);
        NPC right = Main.npc[31];
        right.SetDefaults(NPCID.GreenSlime);
        right.whoAmI = 31;
        right.active = true;
        right.velocity = Vector2.Zero;
        right.damage = 10;
        right.life = right.lifeMax = 12;
        right.Bottom = new Vector2(965f, 60 * 16f);
        Main.npc[32].active = false;
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        VerifyOreWork.SettleReach(companion, player);
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = companion.Combat;
        Require(companion.Brain.Senses.Threats.Threats.Count >= 2, "premise: both slimes sensed as threats");
        var gear = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear;
        gear.Slots[0].SetDefaults(ItemID.WoodenBow);
        gear.Slots[1] = new Item();

        CombatWeights weights = WeighCombatObjectives.ForSenses(ctx);
        Budget budget = Budget.Unbounded();
        SearchPlans.SearchResult result = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, weights, combat.NextPlanId++, ref budget);
        Require(result.Plan != null, "the roller scene offers nothing: " + result.Reason);
        Vector2 stand = result.Plan.Segments[0].Stand.Stand;
        StandReason reason = result.Plan.Segments[0].Stand.Reason;
        Require(reason == StandReason.FloorFlanks, $"the stand must come from the floor flanks; got {reason} at {stand}");
        Require(stand.Y > 800f, $"the flank must stay low; stand {stand}");
        Require(stand.X < 880f || stand.X > 1020f, $"the flank must sit outside the pair; stand {stand}");
        Console.WriteLine($"attack planning: roller flank {stand} by {reason} (value {result.Plan.Weighted:0.00})");
        return 0;
    }

    /// <summary>
    /// P7: an above stand for the area use, then a flank pierce timed to the explosion. Two tough
    /// slimes hold adjacent on the floor; the grenade drops from above and bursts on a planted
    /// sixty-tick fuse, wounding both, and the pierce starts at the burst — not at arrival — to
    /// finish both through. Starting at arrival only, the file-8 mutation, fires into full life.
    /// </summary>
    public static int AGrenadeThenPierceIsTimedToTheExplosion()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 60];
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
            tile.Slope = 0;
            tile.IsHalfBlock = false;
            tile.LiquidAmount = 0;
        }
        for (int x = 48; x <= 52; x++)
        for (int y = 50; y < 60; y++)
        {
            Tile wall = Main.tile[x, y];
            wall.HasTile = true;
            wall.TileType = TileID.Dirt;
            wall.Slope = 0;
            wall.IsHalfBlock = false;
            wall.LiquidAmount = 0;
        }
        StandUpDamage();
        var pierce = new HitResponse { ProjectileType = ProjectileID.WoodenArrowFriendly };
        pierce.PierceRatio.Add(2f);
        LearnHits.AssumeResponse(pierce);
        FlightLaw arrow = FlightLaw.Default(ProjectileID.WoodenArrowFriendly);
        Require(arrow.Gravity != null, "premise: the arrow law carries gravity");
        Laws.AssumeLaw(ProjectileID.WoodenArrowFriendly, arrow with
        {
            Wall = new WallResponse
            {
                ProjectileType = ProjectileID.WoodenArrowFriendly,
                Kind = WallKind.Reflects,
                RestitutionNormal = 1f,
                RestitutionTangent = 1f,
                BounceCount = 4,
            },
        });
        var grenadeSample = new Projectile();
        grenadeSample.SetDefaults(ProjectileID.Grenade);
        ContentSamples.ProjectilesByType[ProjectileID.Grenade] = grenadeSample;
        var boom = new HitResponse { ProjectileType = ProjectileID.Grenade };
        boom.Area = new live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.AreaResponse(120f, live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.AreaTrigger.OnDeath);
        LearnHits.AssumeResponse(boom);
        Laws.AssumeLaw(ProjectileID.Grenade, FlightLaw.Straight(ProjectileID.Grenade) with { LifetimeUpdates = 16 });
        for (int i = 0; i < 8; i++) AttackLearning.ObserveDebuff(ItemID.Grenade, NPCID.GreenSlime, applied: true, 600);
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.statManaMax2 = 20;
        player.active = true;
        player.Bottom = new Vector2(950f, 60 * 16f);
        companion.NPC.Bottom = new Vector2(640f, 901f);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.life = companion.NPC.lifeMax = 100;
        companion.NPC.active = true;
        NPC left = Main.npc[30];
        left.SetDefaults(NPCID.GreenSlime);
        left.whoAmI = 30;
        left.active = true;
        left.velocity = Vector2.Zero;
        left.damage = 10;
        left.life = left.lifeMax = 200;
        left.Bottom = new Vector2(935f, 60 * 16f);
        NPC right = Main.npc[31];
        right.SetDefaults(NPCID.GreenSlime);
        right.whoAmI = 31;
        right.active = true;
        right.velocity = Vector2.Zero;
        right.damage = 10;
        right.life = right.lifeMax = 200;
        right.Bottom = new Vector2(965f, 60 * 16f);
        Main.npc[32].active = false;
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        VerifyOreWork.SettleReach(companion, player);
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = companion.Combat;
        Require(companion.Brain.Senses.Threats.Threats.Count >= 2, "premise: both slimes sensed as threats");
        var gear = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear;
        gear.Slots[0].SetDefaults(ItemID.Grenade);
        gear.Slots[0].stack = 1;
        gear.Slots[1].SetDefaults(ItemID.WoodenBow);
        // One grenade wounds both (contact plus area ≈ 120 each). The bow has to finish the rest in
        // a couple of pierce shots, or the drop is a complete plan and the flank is a longer way to
        // deal the same damage. The launch fire-rate nerf makes a grenade occupy ninety ticks, so
        // the hands are still busy when the burst lands and only one arrow fits the horizon — the
        // row plants the grenade at its item cadence so the pierce has room.
        foreach (var weapon in combat.Weapons)
        {
            if (weapon.ItemType == ItemID.WoodenBow)
                weapon.AuditDamageOverride = 50f;
            if (weapon.ItemType == ItemID.Grenade)
                weapon.FireRateFactor = 1f;
        }

        CombatWeights weights = WeighCombatObjectives.ForSenses(ctx);
        Budget budget = Budget.Unbounded();
        SearchPlans.SearchResult result = SearchPlans.Search(ctx, combat, companion.Brain.Positioner,
            _ => true, weights, combat.NextPlanId++, ref budget);
        Require(result.Plan != null, "the grenade scene offers nothing: " + result.Reason);
        string p7front = string.Join(";", System.Linq.Enumerable.Select(result.Front, p =>
            p.Segments.Length + ":" + string.Join("+", System.Linq.Enumerable.Select(p.Segments, s => s.Stand.Reason.ToString()))
            + "w" + p.Weighted.ToString("0.00")));
        string p7ass = string.Join(",", System.Linq.Enumerable.Select(result.Assessed, a => a.Proposal.Reason.ToString()));
        string p7uses = result.Plan == null ? "" : string.Join(",", System.Linq.Enumerable.Select(result.Plan.Segments[0].Uses, u => u.WeaponSlot.ToString()));
        string p7deep = result.DeeperAssessed == null ? "" : string.Join(",", System.Linq.Enumerable.Select(result.DeeperAssessed, d => d.Proposal.Reason.ToString()));
        Require(result.Plan.Segments.Length >= 2,
            $"grenade then pierce earns a drop then a flank; got {result.Plan.Segments.Length} {result.Plan.Segments[0].Stand.Reason}@{result.Plan.Segments[0].Stand.Stand} uses {p7uses} w{result.Plan.Weighted:0.00} candidates {result.CandidatesEvaluated} front {p7front} assessed {p7ass} deeper {p7deep}");
        StandReason first = result.Plan.Segments[0].Stand.Reason;
        Require(first == StandReason.AboveArea, $"the first segment must drop from above; got {first} front {p7front}");
        Require(result.Plan.Segments[0].Uses.Length >= 1
            && result.Plan.Segments[0].Uses[0].WeaponSlot == 0,
            $"the drop throws the grenade; got slot {p7uses}");
        int pierceAt = -1;
        for (int i = 1; i < result.Plan.Segments.Length; i++)
        {
            StandReason reason = result.Plan.Segments[i].Stand.Reason;
            if (reason != StandReason.FloorFlanks && reason != StandReason.PierceLines)
                continue;
            if (result.Plan.Segments[i].Uses.Length < 1 || result.Plan.Segments[i].Uses[0].WeaponSlot != 1)
                continue;
            Vector2 stand = result.Plan.Segments[i].Stand.Stand;
            if (stand.Y <= 800f || (stand.X >= 900f && stand.X <= 1020f))
                continue;
            if (result.Plan.Segments[i].StartTick <= result.Plan.Segments[i].ArriveTick)
                continue;
            pierceAt = i;
            break;
        }
        Require(pierceAt > 0,
            $"a later segment must be a bow flank that waits for the burst; got {p7front}");
        StandReason pierceReason = result.Plan.Segments[pierceAt].Stand.Reason;
        float arrival = result.Plan.Segments[pierceAt].ArriveTick;
        int start = result.Plan.Segments[pierceAt].StartTick;

        Budget mutationBudget = Budget.Unbounded();
        SearchPlans.SearchResult mutated = SearchPlans.Search(ctx, combat, companion.Brain.Positioner,
            _ => true, weights, combat.NextPlanId++, ref mutationBudget,
            new SearchPlans.SearchOptions(ArrivalStartsOnly: true));
        Require(mutated.Plan != null, "the arrival-only search offers nothing: " + mutated.Reason);
        bool timedPastArrival = false;
        for (int i = 1; i < mutated.Plan.Segments.Length; i++)
        {
            StandReason reason = mutated.Plan.Segments[i].Stand.Reason;
            if ((reason == StandReason.FloorFlanks || reason == StandReason.PierceLines)
                && mutated.Plan.Segments[i].StartTick > mutated.Plan.Segments[i].ArriveTick)
                timedPastArrival = true;
        }
        Require(!timedPastArrival, "starting segments at arrival only must not wait past arrival for the pierce");
        Console.WriteLine($"attack planning: grenade above then {pierceReason} at {start} (arrival {arrival:0}, value {result.Plan.Weighted:0.00})");
        return 0;
    }

    /// <summary>
    /// P8: a target behind a corner is planned with a bouncing weapon and not with a straight one.
    /// A wall blocks the body's line; the water bolt's reflecting law commits to a BankShots stand,
    /// and the bow's dying law never names a bank. Disabling bounce aims — the file-8 mutation —
    /// kills the bolt's bank too.
    /// </summary>
    public static int ABankShotPlansWithABouncingWeaponOnly()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 60];
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
            tile.Slope = 0;
            tile.IsHalfBlock = false;
            tile.LiquidAmount = 0;
        }
        for (int x = 48; x <= 52; x++)
        for (int y = 50; y < 60; y++)
        {
            Tile wall = Main.tile[x, y];
            wall.HasTile = true;
            wall.TileType = TileID.Dirt;
            wall.Slope = 0;
            wall.IsHalfBlock = false;
            wall.LiquidAmount = 0;
        }
        StandUpDamage();
        Laws.AssumeLaw(ProjectileID.WoodenArrowFriendly, FlightLaw.Straight(ProjectileID.WoodenArrowFriendly));
        var boltSample = new Projectile();
        boltSample.SetDefaults(ProjectileID.WaterBolt);
        ContentSamples.ProjectilesByType[ProjectileID.WaterBolt] = boltSample;
        FlightLaw bolt = FlightLaw.Straight(ProjectileID.WaterBolt);
        Laws.AssumeLaw(ProjectileID.WaterBolt, bolt with
        {
            Wall = new WallResponse
            {
                ProjectileType = ProjectileID.WaterBolt,
                Kind = WallKind.Reflects,
                RestitutionNormal = 1f,
                RestitutionTangent = 1f,
                BounceCount = 4,
            },
        });
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.statManaMax2 = 200;
        player.active = true;
        player.Bottom = new Vector2(47 * 16f, 60 * 16f);
        companion.NPC.Bottom = new Vector2(47 * 16f, 60 * 16f - 40f);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.life = companion.NPC.lifeMax = 100;
        companion.NPC.active = true;
        NPC slime = Main.npc[30];
        slime.SetDefaults(NPCID.GreenSlime);
        slime.whoAmI = 30;
        slime.active = true;
        slime.velocity = Vector2.Zero;
        slime.damage = 10;
        slime.life = slime.lifeMax = 30;
        slime.Bottom = new Vector2(852f, 60 * 16f);
        Main.npc[31].active = false;
        Main.npc[32].active = false;
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        VerifyOreWork.SettleReach(companion, player);
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = companion.Combat;
        Require(companion.Brain.Senses.Threats.Threats.Count >= 1, "premise: the slime sensed as a threat");
        var gear = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear;

        gear.Slots[0].SetDefaults(ItemID.WaterBolt);
        gear.Slots[1] = new Item();
        CombatWeights bounceWeights = WeighCombatObjectives.ForSenses(ctx);
        Budget bounceBudget = Budget.Unbounded();
        SearchPlans.SearchResult bounce = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, bounceWeights, combat.NextPlanId++, ref bounceBudget);
        Require(bounce.Plan != null, "the bouncing weapon offers nothing: " + bounce.Reason);
        StandReason bounceReason = bounce.Plan.Segments[0].Stand.Reason;
        Require(bounceReason == StandReason.BankShots, $"the bolt must bank; got {bounceReason} at {bounce.Plan.Segments[0].Stand.Stand}");

        gear.Slots[0].SetDefaults(ItemID.WoodenBow);
        gear.Slots[1] = new Item();
        CombatWeights straightWeights = WeighCombatObjectives.ForSenses(ctx);
        Budget straightBudget = Budget.Unbounded();
        SearchPlans.SearchResult straight = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, straightWeights, combat.NextPlanId++, ref straightBudget);
        foreach (AttackPlan candidate in straight.Front)
            foreach (var segment in candidate.Segments)
                Require(segment.Stand.Reason != StandReason.BankShots,
                    $"the straight weapon must offer no bank; got {segment.Stand.Stand}");

        gear.Slots[0].SetDefaults(ItemID.WaterBolt);
        Budget noBankBudget = Budget.Unbounded();
        SearchPlans.SearchResult noBank = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, bounceWeights, combat.NextPlanId++, ref noBankBudget,
            new SearchPlans.SearchOptions(DisableBankAims: true));
        if (noBank.Plan != null)
            Require(noBank.Plan.Segments[0].Stand.Reason != StandReason.BankShots,
                $"disabling bounce aims must kill the bolt's bank; got {noBank.Plan.Segments[0].Stand.Reason}");
        Console.WriteLine($"attack planning: bolt banks from {bounce.Plan.Segments[0].Stand.Stand} (value {bounce.Plan.Weighted:0.00}); bow banks nowhere");
        return 0;
    }

    /// <summary>
    /// P12: a plan worse on every objective than another never survives, whatever the weights. Two halves:
    /// the filter drops the strictly dominated plan and keeps a tradeoff, and near-equals within tolerance
    /// both survive while a plan worse beyond tolerance on one objective falls; and across weight sweeps
    /// the weighted choice over the unfiltered set never lands on the dominated plan either, which is
    /// arithmetic rather than filtering — with positive weights a strictly dominated plan cannot win a sum.
    /// The filter's observable is the front: the recorder writes it and the audit replays against it, so a
    /// front that held dominated plans would call a ragged search exhaustive. Skipping the filter is the
    /// mutation this row kills.
    /// </summary>
    public static int ADominatedPlanNeverSurvivesTheFront()
    {
        var good = new CombatOutcome(0.5f, 0.5f, 0.5f, 0.1f, 0.1f, 0.1f, 0.2f, 0.1f);
        var dominated = new CombatOutcome(0.4f, 0.4f, 0.4f, 0.2f, 0.2f, 0.2f, 0.3f, 0.2f);
        var tradeoff = new CombatOutcome(0.4f, 0.5f, 0.5f, 0.1f, 0.1f, 0.1f, 0.05f, 0.1f);
        var plans = new (string Name, CombatOutcome Outcome)[] { ("good", good), ("dominated", dominated), ("tradeoff", tradeoff) };
        List<(string Name, CombatOutcome Outcome)> front =
            KeepOnlyUndominated.Filter(plans, p => p.Outcome);
        Require(front.Count == 2, $"the front holds the good plan and the tradeoff; got {front.Count}");
        Require(front[0].Name == "good" && front[1].Name == "tradeoff",
            "the front keeps its original order minus the dominated plan");

        CombatOutcome tolerances = CombatOutcome.Tolerances;
        var nearEqual = new CombatOutcome(
            good.DamagePerSecond - tolerances.DamagePerSecond / 2f,
            good.ThreatRemoved - tolerances.ThreatRemoved / 2f,
            good.PlayerHarmPrevented - tolerances.PlayerHarmPrevented / 2f,
            good.CompanionHarmTaken + tolerances.CompanionHarmTaken / 2f,
            good.PushDangerAdded + tolerances.PushDangerAdded / 2f,
            good.CompanyGap + tolerances.CompanyGap / 2f,
            good.TimeToFirstDamage + tolerances.TimeToFirstDamage / 2f,
            good.ManaSpent + tolerances.ManaSpent / 2f);
        Require(KeepOnlyUndominated.Filter(new[] { good, nearEqual }, o => o).Count == 2,
            "plans that differ by rounding are not different plans; both survive");
        var worseOne = good with { DamagePerSecond = good.DamagePerSecond - tolerances.DamagePerSecond * 2f };
        Require(KeepOnlyUndominated.Filter(new[] { good, worseOne }, o => o).Count == 1,
            "a plan worse beyond tolerance on one objective and no better anywhere falls");

        CombatWeights baseWeights = WeighCombatObjectives.For(0.5f, 0.2f, 0.1f, true, 0.8f, 0.9f);
        var sweeps = new List<CombatWeights> { baseWeights };
        for (int i = 0; i < CombatOutcome.Count; i++)
            sweeps.Add(baseWeights.Scaled(i, 2f));
        foreach (CombatWeights weights in sweeps)
        {
            float best = float.NegativeInfinity;
            string winner = "";
            foreach ((string name, CombatOutcome outcome) in plans)
            {
                float value = weights.Weighted(outcome);
                if (value > best) { best = value; winner = name; }
            }
            Require(winner != "dominated", "no weight sweep lands the weighted choice on the dominated plan");
        }
        Console.WriteLine($"attack planning: the front holds 2 of 3, near-equals survive, and {sweeps.Count} weight sweeps never choose the dominated plan");
        return 0;
    }

    /// <summary>
    /// C1: forty hostiles, two handed weapons (the kit has two slots, not four) and a forty-pellet
    /// volley, the full proposal set. Per-rescore planning time at the 50th, 90th and 99th
    /// percentiles, with the simulation cache and without it. Phase E is not accepted until the
    /// 99th with the cache fits inside one frame.
    /// </summary>
    public static int PlanningCostOnACrowdFitsAFrame()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 60];
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
            tile.Slope = 0;
            tile.IsHalfBlock = false;
            tile.LiquidAmount = 0;
        }
        StandUpDamage();
        (float composedDamage, float composedSpeed) = LearnVolleys.ComposedStats(ItemID.Boomstick, ItemID.MusketBall);
        int pelletDamage = Math.Max(1, (int)composedDamage / 40);
        Vector2 shooter = new(1000f, 1000f), aim = new(1400f, 1000f);
        var use = new Recording.ProjectileUse
        {
            Id = 1,
            Shooter = Recording.Shooter.Player,
            ItemType = ItemID.Boomstick,
            StartTick = 100,
            AimPoint = aim,
            ShooterCentre = shooter,
            BuffsAtStart = Array.Empty<int>(),
            Complete = true,
        };
        for (int pellet = 0; pellet < 40; pellet++)
            use.Spawns.Add(new Recording.UseSpawn(10 + pellet, ProjectileID.Bullet, 100, shooter,
                new Vector2(composedSpeed, 0f).RotatedBy((pellet - 19.5f) * 0.02f), pelletDamage,
                ItemID.MusketBall, shooter, aim));
        LearnVolleys.Learn(use);
        Laws.AssumeLaw(ProjectileID.Bullet, FlightLaw.Straight(ProjectileID.Bullet));
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.statManaMax2 = 20;
        player.active = true;
        player.Bottom = new Vector2(50 * 16f, 60 * 16f);
        companion.NPC.Bottom = new Vector2(47 * 16f, 60 * 16f - 40f);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.life = companion.NPC.lifeMax = 100;
        companion.NPC.active = true;
        for (int i = 0; i < 40; i++)
        {
            NPC npc = Main.npc[30 + i];
            npc.SetDefaults(NPCID.Zombie);
            npc.whoAmI = 30 + i;
            npc.active = true;
            npc.velocity = Vector2.Zero;
            npc.life = npc.lifeMax = 50;
            npc.Bottom = new Vector2((20 + i % 50) * 16f, 60 * 16f);
        }
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        VerifyOreWork.SettleReach(companion, player);
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = companion.Combat;
        var gear = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear;
        gear.Slots[0].SetDefaults(ItemID.Boomstick);
        gear.Slots[1].SetDefaults(ItemID.WoodenBow);

        CombatWeights weights = WeighCombatObjectives.ForSenses(ctx);
        double[] WithCache(bool cache)
        {
            CachePlanned.Enabled = cache;
            CacheSims.Enabled = cache;
            CachePlanned.Clear();
            CacheSims.Clear();
            // P1–P8 run first under --combat-cost / --attack-planning and JIT the planning path.
            // A couple of untimed crowd searches then fill the sim cache so p99 of twelve is not
            // the compiling miss. Uncached needs no extra crowd searches: one warmup is enough.
            int warmups = cache ? 3 : 1;
            for (int w = 0; w < warmups; w++)
            {
                if (!cache)
                {
                    CachePlanned.Clear();
                    CacheSims.Clear();
                }
                Budget warm = Budget.Unbounded();
                SearchPlans.Search(ctx, combat, companion.Brain.Positioner, _ => true, weights,
                    combat.NextPlanId++, ref warm);
            }
            var samples = new double[12];
            for (int i = 0; i < samples.Length; i++)
            {
                if (!cache)
                {
                    CachePlanned.Clear();
                    CacheSims.Clear();
                }
                Budget budget = Budget.Unbounded();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                SearchPlans.Search(ctx, combat, companion.Brain.Positioner, _ => true, weights,
                    combat.NextPlanId++, ref budget);
                clock.Stop();
                samples[i] = clock.Elapsed.TotalMilliseconds;
            }
            CachePlanned.Enabled = true;
            CacheSims.Enabled = true;
            System.Array.Sort(samples);
            return samples;
        }

        double[] cached = WithCache(true);
        double[] uncached = WithCache(false);
        double Pct(double[] s, float p) => s[Math.Min(s.Length - 1, (int)Math.Floor(p * (s.Length - 1)))];
        double cached99 = Pct(cached, 0.99f);
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"attack planning cost: cache p50={Pct(cached, 0.5f):0.000} p90={Pct(cached, 0.9f):0.000} p99={cached99:0.000}; " +
            $"no-cache p50={Pct(uncached, 0.5f):0.000} p90={Pct(uncached, 0.9f):0.000} p99={Pct(uncached, 0.99f):0.000}"));
        Console.Out.Flush();
        Require(cached99 < 16.67, $"the 99th percentile with the cache must fit a frame; got {cached99:0.000} ms");
        return 0;
    }

    /// <summary>
    /// The inspector's plan layer is a bit on the overlay, so a capture and a play can turn it on.
    /// Missing the bit would draw nothing and look like a missing plan.
    /// </summary>
    public static int TheOverlayCarriesThePlanLayer()
    {
        Require((live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.AllLayers & 32768) != 0,
            "AllLayers must include the plan bit");
        live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.Layers = 32768;
        Require(live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.ShowPlan,
            "bit 15 must switch the committed-plan layer on");
        live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.Layers =
            live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.AllLayers;
        Console.WriteLine("overlay: committed-plan layer is bit 15");
        return 0;
    }

    /// <summary>
    /// The millisecond budget is compared to TickCount64, which is milliseconds. A configured 4 must
    /// store 4, not 40 from a TimeSpan-tick conversion.
    /// </summary>
    public static int ThePlanningClockStoresMilliseconds()
    {
        Require(Budget.FromMilliseconds(4f).AllowanceMilliseconds == 4f,
            $"a 4 ms budget must store 4 ms, got {Budget.FromMilliseconds(4f).AllowanceMilliseconds}");
        Console.WriteLine("planning clock: 4 ms stores 4 ms");
        return 0;
    }

    /// <summary>
    /// SafeRange emits at full life, off a horizontal flyer's strip, not toward the player on the path.
    /// </summary>
    public static int SafeRangeStepsOffAHorizontalFlyer()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 60];
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
        }
        Player player = Main.player[0];
        player.dead = false;
        player.active = true;
        player.statLife = player.statLifeMax2 = 100;
        player.Bottom = new Vector2(50 * 16f, 60 * 16f);
        companion.NPC.Center = new Vector2(50 * 16f, 50 * 16f);
        companion.NPC.life = companion.NPC.lifeMax = 100;
        companion.NPC.active = true;
        NPC eye = Main.npc[30];
        eye.SetDefaults(NPCID.DemonEye);
        eye.whoAmI = 30;
        eye.active = true;
        eye.Center = new Vector2(50 * 16f, 52 * 16f);
        eye.velocity = new Vector2(6f, 0f);
        eye.noGravity = true;
        HostileAttackSources.Spawn(eye);
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        VerifyOreWork.SettleReach(companion, player);
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = companion.Combat;
        Budget budget = Budget.Unbounded();
        Require(eye.noGravity, "premise: a Demon Eye must read as noGravity");
        Require(companion.Brain.Senses.Threats.Threats.Count > 0, "premise: the Eye must be a threat");
        SearchPlans.SearchResult probe = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, WeighCombatObjectives.ForSenses(ctx), combat.NextPlanId++, ref budget);
        Require(probe.Assessed != null && probe.Assessed.Count > 0, "the flyer scene assessed nothing");
        bool offStrip = false;
        var reasons = new System.Text.StringBuilder();
        foreach (var assessed in probe.Assessed)
        {
            reasons.Append(assessed.Proposal.Reason).Append('@').Append(
                (assessed.Proposal.Stand.Y - eye.Center.Y).ToString("0")).Append(';');
            if (assessed.Proposal.Reason != StandReason.SafeRange)
                continue;
            offStrip |= MathF.Abs(assessed.Proposal.Stand.Y - eye.Center.Y) > 32f;
        }
        Require(offStrip, "SafeRange at full life must stand off the Eye's horizontal strip; " + reasons);
        Console.WriteLine("attack planning: SafeRange leaves a horizontal flyer's strip at full life");
        return 0;
    }

    /// <summary>
    /// Harm at a stand is path occupancy. On the body is a beating; 200 px off a still body is not.
    /// </summary>
    public static int HarmAtStandIsPathOccupancy()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 60];
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
        }
        Player player = Main.player[0];
        player.dead = false;
        player.active = true;
        player.Bottom = new Vector2(50 * 16f, 60 * 16f);
        companion.NPC.Center = new Vector2(40 * 16f, 50 * 16f);
        companion.NPC.life = companion.NPC.lifeMax = 100;
        NPC zombie = Main.npc[30];
        zombie.SetDefaults(NPCID.Zombie);
        zombie.whoAmI = 30;
        zombie.active = true;
        zombie.Bottom = new Vector2(50 * 16f, 60 * 16f);
        zombie.velocity = Vector2.Zero;
        HostileAttackSources.Spawn(zombie);
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        var senses = companion.Brain.Senses;
        float onPath = Positioner.PredictedHarmAt(zombie.Center, senses, 100f);
        float offPath = Positioner.PredictedHarmAt(zombie.Center + new Vector2(0f, -200f), senses, 100f);
        Require(onPath > 0f, $"standing on a still zombie must occupy its path; harm {onPath}");
        Require(offPath == 0f, $"200 px above a still zombie is not its path; harm {offPath}");
        Console.WriteLine($"attack planning: path occupancy on-body {onPath:0.00}, 200px off {offPath:0.00}");
        return 0;
    }

    /// <summary>
    /// A known hostile creeping inside the slack keeps the plan; a jump past it, or a new urgent body,
    /// dumps it. Last night's 32 plans per second of combat was the 0.01 creep.
    /// </summary>
    public static int AHoldSurvivesCreepAndDumpsAJump()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 60];
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
        }
        Player player = Main.player[0];
        player.dead = false;
        player.active = true;
        player.statLife = player.statLifeMax2 = 100;
        player.Bottom = new Vector2(50 * 16f, 60 * 16f);
        companion.NPC.Bottom = new Vector2(47 * 16f, 60 * 16f - 40f);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.life = companion.NPC.lifeMax = 100;
        companion.NPC.active = true;
        NPC zombie = Main.npc[30];
        zombie.SetDefaults(NPCID.Zombie);
        zombie.whoAmI = 30;
        zombie.active = true;
        zombie.velocity = Vector2.Zero;
        zombie.Bottom = new Vector2(55 * 16f, 60 * 16f);
        HostileAttackSources.Spawn(zombie);
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        VerifyOreWork.SettleReach(companion, player);
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = companion.Combat;
        Budget budget = Budget.Unbounded();
        SearchPlans.SearchResult result = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, WeighCombatObjectives.ForSenses(ctx), combat.NextPlanId++, ref budget);
        Require(result.Plan != null, "the hold scene offers nothing: " + result.Reason);
        combat.Planner.Commit(result.Plan);
        float admitted = result.Plan.Validity.AdmittedMaxUrgency;
        foreach (var threat in companion.Brain.Senses.Threats.Threats)
        {
            threat.Urgency = admitted + Weights.CombatUrgencyHoldSlack * 0.5f;
            threat.UrgencyToCompanion = threat.Urgency;
        }
        Require(combat.Planner.Validate(ctx, companion.Brain.Positioner, _ => true, true),
            "creep inside the slack must hold; dumped " + combat.Planner.LastInvalidation);
        foreach (var threat in companion.Brain.Senses.Threats.Threats)
        {
            threat.Urgency = admitted + Weights.CombatUrgencyHoldSlack + 0.05f;
            threat.UrgencyToCompanion = threat.Urgency;
        }
        Require(!combat.Planner.Validate(ctx, companion.Brain.Positioner, _ => true, true),
            "a jump past the slack must dump");
        Require(combat.Planner.LastInvalidation == "new-urgent-hostile",
            "the jump must name new-urgent-hostile, got " + combat.Planner.LastInvalidation);
        Console.WriteLine("attack planning: hold survives creep, dumps a jump");
        return 0;
    }
}
