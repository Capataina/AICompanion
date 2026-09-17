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
    private static void TeachBurst(int item)
    {
        var random = new Random(11);
        for (int i = 0; i < 40; i++)
        {
            bool debuffed = i % 2 == 0;
            float[] x = AttackLearning.Context(150f + 400f * (float)random.NextDouble(), 800f, 0f, 0.1f, (float)random.NextDouble(), 0,
                (float)random.NextDouble(), 6f, debuffed);
            AttackLearning.Observe(item, NPCID.Zombie, x, (debuffed ? 1.6f : .6f) + .05f * (2f * (float)random.NextDouble() - 1f));
        }
    }

    /// <summary>
    /// P3: a debuff weapon opens so the exploiting weapon lands its boosted hit. The platinum bow lands
    /// six tenths of its forecast on a plain zombie and one and six tenths on one the wooden bow has
    /// debuffed — planted, not learned here — so the weaker wooden bow opens and the platinum bow's later
    /// hit is priced against the debuff. Clearing the wooden bow's debuff record — the file-8 mutation,
    /// weapons priced independently because the interaction is gone — must flip the opener back to platinum.
    /// </summary>
    public static int ADebuffWeaponOpensForItsExploitingWeapon()
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
        var platinumSample = new Item();
        platinumSample.SetDefaults(ItemID.PlatinumBow);
        Terraria.ID.ContentSamples.ItemsByType[ItemID.PlatinumBow] = platinumSample;
        TeachBurst(ItemID.PlatinumBow);
        for (int i = 0; i < 8; i++) AttackLearning.ObserveDebuff(ItemID.WoodenBow, NPCID.Zombie, applied: true, 600);
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
        zombie.life = zombie.lifeMax = 500;
        zombie.Bottom = new Vector2(69 * 16f, 60 * 16f);
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        VerifyOreWork.SettleReach(companion, player);
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = companion.Combat;
        var gear = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear;
        gear.Slots[0].SetDefaults(ItemID.PlatinumBow);
        gear.Slots[1].SetDefaults(ItemID.WoodenBow);

        CombatWeights weights = WeighCombatObjectives.ForSenses(ctx);
        Budget budget = Budget.Unbounded();
        SearchPlans.SearchResult result = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, weights, combat.NextPlanId++, ref budget);
        Require(result.Plan != null, "the debuff scene offers nothing: " + result.Reason);
        var uses = result.Plan.Segments[0].Uses;
        Require(uses.Length >= 2, $"the debuff opening needs a later boosted hit; the segment holds {uses.Length} uses");
        int opener = combat.Weapons[uses[0].WeaponSlot].ItemType;
        Require(opener == ItemID.WoodenBow, $"the weaker debuffing bow must open; opener is {opener}");
        bool platinumLater = false;
        for (int i = 1; i < uses.Length; i++)
            if (combat.Weapons[uses[i].WeaponSlot].ItemType == ItemID.PlatinumBow)
                platinumLater = true;
        Require(platinumLater, "the platinum bow must land its boosted hit later in the opening segment");

        AttackLearning.AssumeDebuff(ItemID.WoodenBow, NPCID.Zombie, struck: 0, applied: 0, ticks: 0);
        CombatWeights plainWeights = WeighCombatObjectives.ForSenses(ctx);
        Budget plainBudget = Budget.Unbounded();
        SearchPlans.SearchResult plain = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, plainWeights, combat.NextPlanId++, ref plainBudget);
        Require(plain.Plan != null, "the debuff-less search offers nothing: " + plain.Reason);
        int plainOpener = combat.Weapons[plain.Plan.Segments[0].Uses[0].WeaponSlot].ItemType;
        Require(plainOpener == ItemID.PlatinumBow, $"without the debuff the burst bow must open; opener is {plainOpener}");
        Require(result.Plan.Weighted > plain.Plan.Weighted,
            $"the debuff-exploiting plan must beat the independent one; with {result.Plan.Weighted:0.00}, without {plain.Plan.Weighted:0.00}");
        Console.WriteLine($"attack planning: wooden opens for a later platinum hit (value {result.Plan.Weighted:0.00}); without the debuff platinum opens (value {plain.Plan.Weighted:0.00})");
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
    /// P5: goon then boss. A weak goon holds behind a wall the body's own shots cannot
    /// cross, while a killable boss holds in the open too far for the body to finish: level one
    /// flies over the wall and clears the goon in one blast, and level two — proposed against the
    /// enemies left alive — closes on the boss. Shotgun only with a wide cone; the wall is the gate,
    /// not range, because four pellets at two damage each after armour cannot kill two goons inside
    /// the horizon while missing from the body — the arithmetic is infeasible, so one goon carries
    /// the phasing. The front holds the far-then-close two-segment plan, and depth one — the file-8
    /// mutation, which can only stand once — holds none. B5's "beats both single segments" is about
    /// harm the close stand avoids after the goons die; this row's scene has no harm, so the
    /// single-goon plan wins the weighted sum on time and the phased plan earns its place on threat.
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
        for (int y = 55; y < 60; y++)
        {
            Tile wall = Main.tile[50, y];
            wall.HasTile = true;
            wall.TileType = TileID.Dirt;
            wall.Slope = 0;
            wall.IsHalfBlock = false;
            wall.LiquidAmount = 0;
        }
        NPC goonA = Main.npc[30];
        goonA.SetDefaults(NPCID.Zombie);
        goonA.whoAmI = 30;
        goonA.active = true;
        goonA.velocity = Vector2.Zero;
        goonA.damage = 30;
        goonA.life = goonA.lifeMax = 6;
        goonA.Bottom = new Vector2(852f, 60 * 16f);
        Main.npc[31].active = false;
        NPC boss = Main.npc[32];
        boss.SetDefaults(NPCID.Zombie);
        boss.whoAmI = 32;
        boss.active = true;
        boss.velocity = Vector2.Zero;
        boss.damage = 20;
        boss.life = boss.lifeMax = 8;
        boss.Bottom = new Vector2(452f, 60 * 16f);
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        VerifyOreWork.SettleReach(companion, player);
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = companion.Combat;
        string threats = string.Join(";", System.Linq.Enumerable.Select(companion.Brain.Senses.Threats.Threats, t => t.Npc.whoAmI + ":" + t.Npc.type + ":d" + t.Npc.damage + ":l" + t.Npc.life));
        Require(companion.Brain.Senses.Threats.Threats.Count >= 2, $"premise: goon and boss sensed as threats; got {threats}");
        NPC goonCheck = Main.npc[30];
        Require(goonCheck.CanBeChasedBy(), $"premise: the goon is damageable; active={goonCheck.active} life={goonCheck.life} friendly={goonCheck.friendly}");
        var gear = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear;
        gear.Slots[0].SetDefaults(ItemID.Boomstick);
        gear.Slots[1] = new Item();

        CombatWeights weights = WeighCombatObjectives.ForSenses(ctx);
        Budget budget = Budget.Unbounded();
        SearchPlans.SearchResult result = SearchPlans.Search(ctx, combat, companion.Brain.Positioner,
            _ => true, weights, combat.NextPlanId++, ref budget);
        Require(result.Plan != null, "the goons scene offers nothing: " + result.Reason);
        AttackPlan? phased = null;
        foreach (AttackPlan candidate in result.Front)
            if (candidate.Segments.Length == 2)
            {
                float far = Vector2.Distance(candidate.Segments[0].Stand.Stand, boss.Center);
                float near = Vector2.Distance(candidate.Segments[1].Stand.Stand, boss.Center);
                if (far > 300f && near < 250f) { phased = candidate; break; }
            }
        string front = string.Join(";", System.Linq.Enumerable.Select(result.Front, p => p.Segments.Length + "@" + p.Segments[0].Stand.Stand.ToString()));
        Require(phased != null, $"the front must hold a far-then-close two-segment plan; front {front}");
        float firstDist = Vector2.Distance(phased.Segments[0].Stand.Stand, boss.Center);
        float secondDist = Vector2.Distance(phased.Segments[1].Stand.Stand, boss.Center);

        CombatWeights singleWeights = WeighCombatObjectives.ForSenses(ctx);
        Budget singleBudget = Budget.Unbounded();
        SearchPlans.SearchResult single = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, singleWeights, combat.NextPlanId++, ref singleBudget);
        Require(single.Plan != null, "the depth-one search offers nothing: " + single.Reason);
        foreach (AttackPlan candidate in single.Front)
            Require(candidate.Segments.Length == 1, "depth one must offer only single segments");
        Console.WriteLine($"attack planning: far {firstDist:0}px then close {secondDist:0}px on the front (best {result.Plan.Weighted:0.00}); depth one {single.Plan.Weighted:0.00}");
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
}
