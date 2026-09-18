#nullable enable

extern alias live;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using AICompanion.Tools.Ledger;
using AICompanion.Tools.SessionReport;
using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Activities.Combat.Planning;
using live::AICompanion.Companion.Brain.Infrastructure.Movement;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection;
using live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;
using live::AICompanion.Companion.CharacterBody;
using live::AICompanion.Companion.PlayerIntegration;
using Recording = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording;
using Forecasts = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.ForecastUses;
using OrbPace = live::AICompanion.Companion.Brain.Infrastructure.Movement.OrbPace;

namespace AICompanion.Tools.CombatAudit;

/// <summary>
/// Rows A1-A4 and the hold row: the audit proving itself on scenes it builds. A1 snapshots a live
/// search and replays it exactly, then drops the verdicts and shows the replay failing without them.
/// A2 plants proposals that miss the best stand and shows the exhaustive grid finding it with "no
/// generator" — and nothing when handed only the live proposals. A3 plants a damage-driven flip, shows
/// the sweep reporting it, and shows a unit sweep reporting nothing. A4 pairs a matched and a
/// mismatched shot against synthetic events and shows the calibration splitting them. The hold row
/// commits a plan, snapshots it, and shows the restored commitment's Validate answering what the live
/// one's did across a hold and one release from each invalidation class. The cut row caps a search at
/// one simulation, shows it cutting identically twice, and replays the cut from the snapshot. The last
/// two rows are the plan's P9 and P10, and the cut row is its P11: file 8 predates the self-test's
/// search scenes and names EngineReplay for all three, but the behaviors are search behaviors and the
/// scenes live here now. Each row bakes its file-8 mutation in as the second assertion, so the
/// mutation that must fail it fails it every run, not by hand-reversion.
/// </summary>
internal static class SelfTest
{
    private const string Instrument = "combat-audit";
    private const string Suite = "SelfTest";

    public static int Run()
    {
        int red = 0;
        red += Row("a snapshot replayed with its live budget and proposals reproduces its plan", SnapshotReplaysExactly);
        red += Row("the exhaustive audit finds a better stand and names no generator", ExhaustiveFindsBetterStand);
        red += Row("the weight sweep reports what a doubled weight moves", WeightSweepReportsMoves);
        red += Row("the knowledge audit splits matched shots from miscalibrated types", KnowledgeAuditSplits);
        red += Row("the hold audit reproduces the live commitment's verdict", HoldReproducesLiveVerdict);
        red += Row("a count-capped search cuts at the same simulation twice and replays its cut", CountCutReplays);
        red += Row("danger commits the harm-prevention plan over the damage plan", DangerCommitsHarmPreventionOverDamage);
        red += Row("an unchanged scene keeps one plan across rescores", UnchangedSceneKeepsOnePlanAcrossRescores);
        Console.WriteLine(red == 0 ? "combat-audit self-test: 8 rows green" : $"combat-audit self-test: {red} rows red");
        return red == 0 ? 0 : 1;
    }

    private static int Row(string name, Func<string> test)
    {
        try
        {
            string detail = test();
            EmitLedgerRows.Pass(Instrument, Suite, name, detail);
            Console.WriteLine($"  PASS {name}: {detail}");
            return 0;
        }
        catch (Exception error)
        {
            EmitLedgerRows.Fail(Instrument, Suite, name, error.Message);
            Console.WriteLine($"  FAIL {name}: {error.Message}");
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Scene
    {
        public CompanionNPC Companion = null!;
        public ActionContext Ctx;
        public float Radius;
    }

    private static Scene Setup(int floorRow, Vector2 playerTile, Vector2 companionTile,
        (int X, int Y, int W, int H)[]? walls, params (int Slot, int Type, Vector2 Tile)[] zombies)
    {
        AuditHost.SizeWorld(0, 0, 100, 100);
        for (int x = 0; x < Main.maxTilesX; x++)
            for (int y = 0; y < Main.maxTilesY; y++)
            {
                Tile tile = Main.tile[x, y];
                tile.HasTile = false;
                tile.LiquidAmount = 0;
            }
        for (int x = 0; x < Main.maxTilesX; x++)
        {
            Tile tile = Main.tile[x, floorRow];
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
        }
        // Walls before the flood grows: the live search prices the world the snapshot carries, the way a
        // rescore already ran would leave it — a wall raised after the flood is a stale-flood decision,
        // which is a live-faithful state but not the one these rows are about.
        if (walls != null)
            foreach ((int wx, int wy, int ww, int wh) in walls)
                for (int x = wx; x < wx + ww; x++)
                    for (int y = wy; y < wy + wh; y++)
                    {
                        Tile tile = Main.tile[x, y];
                        tile.HasTile = true;
                        tile.TileType = TileID.Dirt;
                    }
        AuditHost.CreateActors();
        Player player = Main.player[Main.myPlayer];
        player.width = 20;
        player.height = 42;
        player.position = new Vector2(playerTile.X * 16f, playerTile.Y * 16f);
        player.statLife = player.statLifeMax2 = 100;
        player.statManaMax2 = 20;
        player.active = true;
        player.dead = false;
        var gear = AuditHost.CompanionPlayer.Gear;
        gear.Slots[0].SetDefaults(ItemID.WoodenBow);
        gear.Slots[1].SetDefaults(ItemID.ThrowingKnife);
        gear.Slots[2].SetDefaults(ItemID.CopperPickaxe);
        gear.Slots[3].SetDefaults(ItemID.CopperAxe);
        AuditHost.RegisterSample(ItemID.WoodenBow);
        AuditHost.RegisterSample(ItemID.ThrowingKnife);
        AuditHost.RegisterSample(ItemID.WoodenArrow);
        AuditHost.RegisterProjectileSample(ProjectileID.WoodenArrowFriendly);
        AuditHost.RegisterProjectileSample(ProjectileID.ThrowingKnife);
        CompanionNPC companion = AuditHost.Companion;
        companion.NPC.position = new Vector2(companionTile.X * 16f, companionTile.Y * 16f);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.life = companion.NPC.lifeMax = 100;
        companion.NPC.active = true;
        foreach ((int slot, int type, Vector2 tile) in zombies)
        {
            NPC npc = Main.npc[slot];
            npc.SetDefaults(type);
            npc.position = new Vector2(tile.X * 16f, (tile.Y + 1) * 16f - npc.height);
            npc.velocity = Vector2.Zero;
            npc.active = true;
            npc.whoAmI = slot;
            // Spawned, like every live body: a directly placed NPC reads generation zero, which no live
            // snapshot ever stamps, so without this every scene would file the pre-stamp unresolved note.
            HostileAttackSources.Spawn(npc);
        }
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        var reach = companion.Brain.Senses.Reach;
        reach.Refresh(companion.Brain.Senses);
        for (int i = 0; i < 20000 && !reach.Complete; i++)
            reach.Grow();
        Require(reach.Complete, "the self-test flood never completes");
        companion.Brain.Positioner.PrepareOffer(
            new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
                live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.FireFrom,
                companion.NPC.Center),
            companion.Brain.Senses);
        var scene = new Scene { Companion = companion, Ctx = new ActionContext(companion, companion.Brain.Senses) };
        scene.Radius = CompanionPreferences.Current.NewActivityRadius;
        return scene;
    }

    private static Func<Vector2, bool> Allows(Scene scene)
    {
        Vector2 heading = scene.Ctx.Senses.Intent.Region.Heading;
        Vector2 feet = scene.Ctx.Npc.Bottom;
        float radius = scene.Radius;
        return point => Vector2.DistanceSquared(point, heading) <= radius * radius
            && Vector2.DistanceSquared(feet, heading) <= radius * radius;
    }

    private static SearchAttackPlans.SearchResult Search(Scene scene, CombatWeights weights, int planId,
        SearchAttackPlans.SearchOptions? options = null)
    {
        var combat = scene.Companion.Combat;
        var budget = PlanningBudget.FromMilliseconds(1000f);
        return SearchAttackPlans.SearchDepthOne(scene.Ctx, combat, scene.Companion.Brain.Positioner,
            Allows(scene), weights, planId, ref budget, options);
    }

    private static string SnapshotReplaysExactly()
    {
        // A zombie on the player: danger high, the search explores nothing, the replay has no draws to miss.
        // A wall stands between the body and the zombie, so where the body hovers solves nothing and the
        // winning stand is away from it — reached by the flood and priced by its verdict, which is what the
        // mutation below drops. Without the wall Here wins, and Here replays without verdicts by definition.
        Scene scene = Setup(60, new Vector2(50, 56), new Vector2(40, 54), new[] { (45, 35, 1, 25) },
            (25, NPCID.Zombie, new Vector2(51, 59)));
        var combat = scene.Companion.Combat;
        CombatWeights weights = WeighCombatObjectives.ForSenses(scene.Ctx);
        var budget = PlanningBudget.FromMilliseconds(1000f);
        SearchAttackPlans.SearchResult result = SearchAttackPlans.SearchDepthOne(scene.Ctx, combat,
            scene.Companion.Brain.Positioner, Allows(scene), weights, combat.NextPlanId++, ref budget);
        Require(result.Plan != null, "the live search offers nothing: " + result.Reason);
        string json = ExportCombatSnapshot.Build(scene.Ctx, combat, result.Plan, result, weights, budget, scene.Radius, true);
        RestoredDecision restored = RestoreSnapshot.Restore(json);
        AuditSearch.ReplayVerdict replay = AuditSearch.Replay(restored);
        Require(replay.Reproduced, "replay diverged: " + string.Join("; ", replay.Diffs));
        // The mutation: verdicts omitted, proposals kept. The restored flood never grew, so every stand
        // re-assesses undecided and the replay offers nothing — the snapshot without verdicts is not a replay.
        var combat2 = restored.Companion.Combat;
        var budget2 = PlanningBudget.Unbounded();
        SearchAttackPlans.SearchResult dropped = SearchAttackPlans.SearchDepthOne(restored.Ctx, combat2,
            restored.Companion.Brain.Positioner, Allows(new Scene { Companion = restored.Companion, Ctx = restored.Ctx, Radius = restored.Snapshot.AllowanceRadius }),
            restored.Weights, combat2.NextPlanId++, ref budget2,
            new SearchAttackPlans.SearchOptions(restored.Proposals));
        Require(dropped.Plan == null, "a verdict-less replay still offers a plan");
        // The second scene is calm: the zombie is fifteen tiles off, danger reads under the ceiling, and the
        // search explores — draws Thompson samples — off taught outcomes. The replay restores the tick and the
        // taught models, draws the same samples, and reproduces bit for bit. Dropping the tick stamp diverges
        // the replay on damage, removal, prevention, weight and front size, so the half-row kills the mutation
        // it names; it also killed the replay's forced means, which priced calm replays at the posterior mean
        // against live samples until this scene showed the two apart.
        Scene calm = Setup(60, new Vector2(50, 56), new Vector2(40, 54), null,
            (25, NPCID.Zombie, new Vector2(65, 59)));
        float reach = calm.Companion.Combat.Weapons[0].Reach;
        float[] context = AttackLearning.Context(300f, reach, 0f, 0.1f, 0f, 0, 0f, OrbPace.MaxSpeed, false);
        AttackLearning.Observe(ItemID.WoodenBow, NPCID.Zombie, context, 0.8f);
        AttackLearning.Observe(ItemID.WoodenBow, NPCID.Zombie, context, 1.2f);
        AttackLearning.Observe(ItemID.WoodenBow, NPCID.Zombie, context, 1.0f);
        // Both hands taught, so whichever weapon wins is priced by a sample: teaching the bow alone left
        // the knife — no evidence, factor one, nothing drawn — winning, and the replay proved nothing.
        AttackLearning.Observe(ItemID.ThrowingKnife, NPCID.Zombie, context, 0.9f);
        AttackLearning.Observe(ItemID.ThrowingKnife, NPCID.Zombie, context, 1.1f);
        var calmCombat = calm.Companion.Combat;
        CombatWeights calmWeights = WeighCombatObjectives.ForSenses(calm.Ctx);
        Require(Forecasts.Explore(calm.Ctx), "premise: the calm scene explores");
        var calmBudget = PlanningBudget.FromMilliseconds(1000f);
        SearchAttackPlans.SearchResult calmResult = SearchAttackPlans.SearchDepthOne(calm.Ctx, calmCombat,
            calm.Companion.Brain.Positioner, Allows(calm), calmWeights, calmCombat.NextPlanId++, ref calmBudget);
        Require(calmResult.Plan != null, "the calm search offers nothing: " + calmResult.Reason);
        Require(AttackLearning.LastSampledFactor != 1f, "the calm search drew no sample");
        string calmJson = ExportCombatSnapshot.Build(calm.Ctx, calmCombat, calmResult.Plan, calmResult,
            calmWeights, calmBudget, calm.Radius, true);
        RestoredDecision calmRestored = RestoreSnapshot.Restore(calmJson);
        AuditSearch.ReplayVerdict calmReplay = AuditSearch.Replay(calmRestored);
        Require(calmReplay.Reproduced, "calm replay diverged: " + string.Join("; ", calmReplay.Diffs));
        // The third scene shoots back and stalls: the zombie's contact damage is zeroed, so its recent
        // hostile shot is its only teeth — the threat sense reads it as a threat only through the shot —
        // and a second body carries an assumed deferral. The replay restores the generation, the shot and
        // the deferral's hold, and reproduces bit for bit. Stripping the stamps from the JSON drops the
        // threat from the replay, which diverges and names the missing stamp; advancing the terrain
        // revision after the stall reopens the deferral on both sides, which stamping the deferral
        // current — the old restore — would have held.
        Scene hot = Setup(60, new Vector2(50, 56), new Vector2(40, 54), null,
            (25, NPCID.Zombie, new Vector2(51, 59)),
            (26, NPCID.Zombie, new Vector2(70, 59)));
        NPC shooter = Main.npc[25];
        shooter.damage = 0;
        HostileAttackSources.Observe(new Projectile { hostile = true, damage = 37 }, new EntitySource_Parent(shooter));
        hot.Companion.Brain.Senses.Update(hot.Companion.NPC, Main.player[Main.myPlayer]);
        var hotCombat = hot.Companion.Combat;
        int liveGen = HostileAttackSources.Generation(shooter);
        Require(HostileAttackSources.RecentDamage(shooter) == 37, "premise: the live shot is not attributed");
        int gen26 = HostileAttackSources.Generation(Main.npc[26]);
        hotCombat.Planner.AssumeDeferred(26, gen26, 100, Main.npc[26].Center, hot.Companion.NPC.Bottom,
            hot.Ctx.Senses.Tick, TerrainChanges.Revision);
        Require(hotCombat.Planner.IsDeferred(hot.Ctx, 26, gen26, Main.npc[26].Center),
            "premise: the live deferral does not hold");
        CombatWeights hotWeights = WeighCombatObjectives.ForSenses(hot.Ctx);
        var hotBudget = PlanningBudget.FromMilliseconds(1000f);
        SearchAttackPlans.SearchResult hotResult = SearchAttackPlans.SearchDepthOne(hot.Ctx, hotCombat,
            hot.Companion.Brain.Positioner, Allows(hot), hotWeights, hotCombat.NextPlanId++, ref hotBudget);
        Require(hotResult.Plan != null, "the hot search offers nothing: " + hotResult.Reason);
        string hotJson = ExportCombatSnapshot.Build(hot.Ctx, hotCombat, hotResult.Plan, hotResult,
            hotWeights, hotBudget, hot.Radius, true);
        RestoredDecision hotRestored = RestoreSnapshot.Restore(hotJson);
        Require(HostileAttackSources.Generation(Main.npc[25]) == liveGen, "the restore drops the generation");
        Require(HostileAttackSources.RecentDamage(Main.npc[25]) == 37, "the restore drops the shot");
        Require(hotRestored.Companion.Combat.Planner.IsDeferred(hotRestored.Ctx, 26, gen26, Main.npc[26].Center),
            "the restore drops the held deferral");
        AuditSearch.ReplayVerdict hotReplay = AuditSearch.Replay(hotRestored);
        Require(hotReplay.Reproduced, "hot replay diverged: " + string.Join("; ", hotReplay.Diffs));
        string stripped = hotJson.Replace("\"ShotDamage\":37", "\"ShotDamage\":0").Replace("\"Generation\":1", "\"Generation\":0");
        RestoredDecision strippedRestored = RestoreSnapshot.Restore(stripped);
        Require(strippedRestored.Unresolved.Contains("npc[25].threat-shots"),
            "the stripped restore names no missing stamp");
        AuditSearch.ReplayVerdict strippedReplay = AuditSearch.Replay(strippedRestored);
        Require(!strippedReplay.Reproduced, "a replay without the shot still reproduces");
        // The reopen half: a dig after the stall, then the snapshot. Live reopens on the revision — read
        // after the snapshot is built, because asking consumes the entry — and the restore reopens with it.
        Scene stale = Setup(60, new Vector2(50, 56), new Vector2(40, 54), null,
            (25, NPCID.Zombie, new Vector2(51, 59)));
        var staleCombat = stale.Companion.Combat;
        int staleGen = HostileAttackSources.Generation(Main.npc[25]);
        staleCombat.Planner.AssumeDeferred(25, staleGen, 100, Main.npc[25].Center, stale.Companion.NPC.Bottom,
            stale.Ctx.Senses.Tick, TerrainChanges.Revision);
        TerrainChanges.Changed(50, 50);
        CombatWeights staleWeights = WeighCombatObjectives.ForSenses(stale.Ctx);
        var staleBudget = PlanningBudget.FromMilliseconds(1000f);
        SearchAttackPlans.SearchResult staleResult = SearchAttackPlans.SearchDepthOne(stale.Ctx, staleCombat,
            stale.Companion.Brain.Positioner, Allows(stale), staleWeights, staleCombat.NextPlanId++, ref staleBudget);
        Require(staleResult.Plan != null, "the stale search offers nothing: " + staleResult.Reason);
        string staleJson = ExportCombatSnapshot.Build(stale.Ctx, staleCombat, staleResult.Plan, staleResult,
            staleWeights, staleBudget, stale.Radius, true);
        Vector2 staleCentre = Main.npc[25].Center;
        Require(!staleCombat.Planner.IsDeferred(stale.Ctx, 25, staleGen, staleCentre),
            "premise: the dug deferral still holds live");
        RestoredDecision staleRestored = RestoreSnapshot.Restore(staleJson);
        Require(!staleRestored.Companion.Combat.Planner.IsDeferred(staleRestored.Ctx, 25, staleGen, Main.npc[25].Center),
            "the restore holds a deferral the dig reopened");
        // The fourth scene is kitted: the player plated with endurance at Expert effectiveness
        // and an arrow bonus, the body mirroring his defence, the bow Powerful — a prefix the game
        // accepts on a zero-knockback bow, where it refuses the knockers. Effective damage, urgency,
        // the bow's own numbers and the class-scaled scalar all read the stamps, and the replay
        // reproduces bit for bit. The arrow sample is pulled before the restore, so the replay
        // proves the restore registers the default ammo rather than inheriting the scene's. The
        // strip rebuilds the snapshot naked, stock and unbonused through the DTOs and diverges.
        Scene clad = Setup(60, new Vector2(50, 56), new Vector2(40, 54), null,
            (25, NPCID.Zombie, new Vector2(51, 59)));
        Player cladPlayer = Main.player[Main.myPlayer];
        cladPlayer.statDefense = Player.DefenseStat.Default + 20;
        cladPlayer.endurance = 0.2f;
        cladPlayer.DefenseEffectiveness = Terraria.ModLoader.MultipliableFloat.One * 0.75f;
        cladPlayer.arrowDamage += 0.25f;
        clad.Companion.NPC.defense = 20;
        Item cladBow = AuditHost.CompanionPlayer.Gear.Slots[0];
        Require(cladBow.Prefix(PrefixID.Powerful), "premise: the bow takes Powerful");
        int livePrefix = cladBow.prefix, liveDamage = cladBow.damage, liveUseTime = cladBow.useTime;
        float liveShootSpeed = cladBow.shootSpeed;
        clad.Companion.Brain.Senses.Update(clad.Companion.NPC, Main.player[Main.myPlayer]);
        var cladCombat = clad.Companion.Combat;
        int livePerHit = cladCombat.Weapons[0].DamagePerHit(clad.Ctx);
        CombatWeights cladWeights = WeighCombatObjectives.ForSenses(clad.Ctx);
        var cladBudget = PlanningBudget.FromMilliseconds(1000f);
        SearchAttackPlans.SearchResult cladResult = SearchAttackPlans.SearchDepthOne(clad.Ctx, cladCombat,
            clad.Companion.Brain.Positioner, Allows(clad), cladWeights, cladCombat.NextPlanId++, ref cladBudget);
        Require(cladResult.Plan != null, "the clad search offers nothing: " + cladResult.Reason);
        string cladJson = ExportCombatSnapshot.Build(clad.Ctx, cladCombat, cladResult.Plan, cladResult,
            cladWeights, cladBudget, clad.Radius, true);
        Require(Terraria.ID.ContentSamples.ItemsByType.Remove(ItemID.WoodenArrow),
            "premise: the arrow sample was registered to pull");

        RestoredDecision cladRestored = RestoreSnapshot.Restore(cladJson);

        Player restoredPlayer = Main.player[Main.myPlayer];
        Require((int)restoredPlayer.statDefense == 20, "the restore drops the player's defence");
        Require(restoredPlayer.endurance == 0.2f, "the restore drops the player's endurance");
        Require(restoredPlayer.DefenseEffectiveness.Value == 0.75f, "the restore drops the effectiveness");
        Require(AuditHost.Companion.NPC.defense == 20, "the restore drops the body's defence");
        Item restoredBow = AuditHost.CompanionPlayer.Gear.Slots[0];
        Require(restoredBow.prefix == livePrefix && restoredBow.damage == liveDamage
            && restoredBow.useTime == liveUseTime && restoredBow.shootSpeed == liveShootSpeed,
            "the restore drops the bow's prefix");
        Require(live::AICompanion.Companion.Inventory.CompanionGear.DefaultAmmo(restoredBow) != null,
            "the restore derives no ammo for the bow");
        int restoredPerHit = cladRestored.Companion.Combat.Weapons[0].DamagePerHit(cladRestored.Ctx);
        Require(restoredPerHit == livePerHit, $"the restored hit prices {restoredPerHit}, not the live {livePerHit}");
        AuditSearch.ReplayVerdict cladReplay = AuditSearch.Replay(cladRestored);
        Require(cladReplay.Reproduced, "clad replay diverged: " + string.Join("; ", cladReplay.Diffs));
        var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
        ExportCombatSnapshot.SnapshotDto? cladDto =
            JsonSerializer.Deserialize<ExportCombatSnapshot.SnapshotDto>(cladJson, jsonOptions);
        Require(cladDto != null, "premise: the clad snapshot deserializes");
        ExportCombatSnapshot.SnapshotDto bareDto = cladDto! with
        {
            Player = cladDto.Player with { Defense = 0, Endurance = 0f, DefenseEffectiveness = 0.5f },
            Body = cladDto.Body with { Defense = 0 },
            GearPrefixes = new List<int> { 0, 0, 0, 0 },
            GearStats = new List<ExportCombatSnapshot.GearStatDto?> { null, null, null, null },
            WeaponScaledDamage = null,
        };
        AuditSearch.ReplayVerdict bareReplay = AuditSearch.Replay(
            RestoreSnapshot.Restore(JsonSerializer.Serialize(bareDto, jsonOptions)));
        Require(!bareReplay.Reproduced, "a replay without kit still reproduces");
        return $"stand {result.Plan!.Segments[0].Stand.Stand}, {result.FrontSize} on the front, verdict-less replay {dropped.Reason}, calm replay exact, hot replay exact, clad replay exact";
    }

    private static string ExhaustiveFindsBetterStand()
    {
        // Here solves from far away; the grid walks closer for a richer shot no proposal names.
        Scene scene = Setup(60, new Vector2(50, 56), new Vector2(30, 54), null, (25, NPCID.Zombie, new Vector2(52, 59)));
        var combat = scene.Companion.Combat;
        CombatWeights weights = WeighCombatObjectives.ForSenses(scene.Ctx);
        var here = new StandProposal(scene.Ctx.Npc.Center, StandReason.Here, -1, new[] { 25 });
        SearchAttackPlans.SearchResult pinned = Search(scene, weights, 1,
            new SearchAttackPlans.SearchOptions(new[] { here }));
        Require(pinned.Plan != null, "Here solves nothing: " + pinned.Reason);
        var budget = PlanningBudget.FromMilliseconds(1000f);
        string json = ExportCombatSnapshot.Build(scene.Ctx, combat, pinned.Plan, pinned, weights, budget, scene.Radius, true);
        RestoredDecision restored = RestoreSnapshot.Restore(json);
        // Restore SetDefaults a knife to one. The stack cap then prices a single throw from Here
        // and from the closer cell, and harm at the closer cell wins. A handed pile is the scene.
        foreach (var weapon in restored.Companion.Combat.Weapons)
            if (weapon is live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.ItemWeapon item
                && item.ItemType == ItemID.ThrowingKnife)
                item.Item.stack = 999;
        AuditSearch.ExhaustiveVerdict exhaustive = AuditSearch.Exhaustive(restored);
        Require(exhaustive.Regret > 0f, "the grid finds nothing better than Here");
        Require(exhaustive.Generator == "none", "the better stand attributes to " + exhaustive.Generator);
        AuditSearch.ExhaustiveVerdict liveOnly = AuditSearch.Exhaustive(restored, liveProposalsOnly: true);
        Require(liveOnly.Regret == 0f, "the live proposals alone report regret " + liveOnly.Regret);
        return $"regret {exhaustive.Regret:0.00} at {exhaustive.BetterStand}, live-only regret {liveOnly.Regret:0.00}";
    }

    private static string WeightSweepReportsMoves()
    {
        // P3's scene at low life, which Phase E reuses for the spread row: a bloodied boss on the player, a
        // Boomstick whose learned spread wins only close, and the body at a fifth of its life. Near stands
        // beside the boss with the whole volley landing; far stands past the body, safe, with the outer
        // pellets missing. At base weights harm holds the body back; doubling damage sends it in. The boss
        // is bloodied because damage is priced as a share of the encounter's remaining life: at full health
        // its two thousand life would shrink the volley below any weight's notice and no doubling could ever
        // send the body in. The spread and the pellets' flight law are seeded through the recording paths the
        // K8 and K0 rows prove, the way volleys the player fired last session would have taught them.
        Scene scene = Setup(60, new Vector2(50, 56), new Vector2(40, 54), null, (25, NPCID.KingSlime, new Vector2(51, 59)));
        var gear = AuditHost.CompanionPlayer.Gear;
        gear.Slots[0].SetDefaults(ItemID.Boomstick);
        gear.Slots[1] = new Item();
        Main.npc[25].damage = 12;
        AuditHost.RegisterSample(ItemID.Boomstick);
        AuditHost.RegisterSample(ItemID.MusketBall);
        AuditHost.RegisterProjectileSample(ProjectileID.Bullet);
        SeedBoomstickSpread();
        scene.Companion.NPC.life = 20;
        Main.npc[25].life = 250;
        scene.Companion.Brain.Senses.Update(scene.Companion.NPC, Main.player[Main.myPlayer]);
        var combat = scene.Companion.Combat;
        CombatWeights weights = WeighCombatObjectives.ForSenses(scene.Ctx);
        Vector2 boss = Main.npc[25].Center, body = scene.Ctx.Npc.Center;
        Vector2 toward = body - boss;
        toward = toward.LengthSquared() > 1f ? Vector2.Normalize(toward) : -Vector2.UnitX;
        // Beside the boss, not inside it: the overlap's harm is binary and always too big, while the
        // distance harm tunes continuously with the stand's distance. The whole volley still lands here,
        // so only the harm moves with the knob.
        var near = new StandProposal(boss + toward * 80f, StandReason.AuditGrid, -1, new[] { 25 });
        float travel = Vector2.Distance(body, near.Stand);
        var far = new StandProposal(body + toward * (travel + 36f), StandReason.AuditGrid, -1, new[] { 25 });
        AttackLearning.ForceMeans = true;
        SearchAttackPlans.SearchResult both;
        try
        {
            both = Search(scene, weights, 1,
                new SearchAttackPlans.SearchOptions(new[] { near, far }));
        }
        finally
        {
            AttackLearning.ForceMeans = false;
        }
        Require(both.Plan != null, "neither pinned stand solves: " + both.Reason);
        var budget = PlanningBudget.FromMilliseconds(1000f);
        string json = ExportCombatSnapshot.Build(scene.Ctx, combat, both.Plan, both, weights, budget, scene.Radius, true);
        RestoredDecision restored = RestoreSnapshot.Restore(json);
        List<AuditWeights.SweepMove> moves = AuditWeights.Sweep(restored, new[] { 1.0f, 2.0f });
        AuditWeights.SweepMove? damageDouble = null;
        foreach (AuditWeights.SweepMove move in moves)
        {
            if (move.Factor == 1f)
                Require(!move.Changed, $"a unit sweep moves {move.Name}");
            if (move.Name == "damage" && move.Factor == 2f)
                damageDouble = move;
            if (move.Name == "mana" && move.Factor == 2f)
                Require(!move.Changed, "doubling an unspent weight moves the stand");
        }
        Require(damageDouble != null && damageDouble.Changed,
            "doubling damage moves nothing; base stand " + both.Plan!.Segments[0].Stand.Stand);
        return $"damage x2 moves {both.Plan!.Segments[0].Stand.Stand} to {damageDouble!.Stand}, unit sweep still";
    }

    /// <summary>A four-pellet Boomstick spread taught the way K8 teaches it: one player animation, four spawns
    /// on the tick at a wider cone than K8's (±13°/±4.3°), two closing animations. The outer pellets miss a
    /// King Slime past three hundred pixels and all four land beside it, which is the falloff the sweep tunes.
    /// The pellets' flight law is fitted first, the way K0 fits it: two of the player's own musket-ball flights
    /// through the game's own AI. Without it the sim flies the prior, lobs for a drop the pellets never had,
    /// and the spread composes with the lob into chaos that lands one pellet from everywhere.</summary>
    private static void SeedBoomstickSpread()
    {
        Recording.RecordProjectileFlights.Clear();
        Recording.GroupSpawnsIntoUses.Clear();
        LearnVolleyShapes.Reset();
        FitFlightLaws.Reset();
        var ammo = new Item();
        ammo.SetDefaults(ItemID.MusketBall);
        for (int flight = 0; flight < 2; flight++)
        {
            var native = new Projectile();
            native.SetDefaults(ProjectileID.Bullet);
            native.whoAmI = 30 + flight;
            native.active = true;
            native.owner = Main.myPlayer;
            native.Center = new Vector2(1000f, 1000f);
            native.velocity = new Vector2(10f, 0f);
            Recording.RecordProjectileFlights.NoteSpawn(native.whoAmI, native,
                new EntitySource_ItemUse(Main.player[Main.myPlayer], ammo));
            for (int tick = 1; tick <= 46; tick++)
            {
                native.VanillaAI();
                native.position += native.velocity;
                Recording.RecordProjectileFlights.NoteStep(native);
            }
            Recording.RecordProjectileFlights.NoteDeath(native);
        }
        Require(FitFlightLaws.LawFor(ProjectileID.Bullet).Revision >= 1, "the player's ball flights fit no law");
        Vector2 shooter = new(1000f, 1000f), aim = new(1400f, 1000f);
        Main.LocalPlayer.Center = shooter;
        // The use's aim is the mouse world at the spawn's tick, not the spawn's own aim point: K8 never sets
        // the mouse, so its angles measure 135° off the aim line and it never notices, asserting grouping only.
        // The sweep tunes the falloff, so its seed aims the mouse where the spawns say they were aimed.
        Main.screenPosition = Vector2.Zero;
        Main.mouseX = (int)aim.X;
        Main.mouseY = (int)aim.Y;
        Recording.GroupSpawnsIntoUses.NotePlayerAnimation(100, 20, 20, ItemID.Boomstick, System.Array.Empty<int>());
        for (int pellet = 0; pellet < 4; pellet++)
        {
            var spawn = new Projectile { whoAmI = 10 + pellet, type = ProjectileID.Bullet, damage = 10, active = true };
            spawn.Center = shooter;
            spawn.velocity = new Vector2(10f, 0f).RotatedBy((pellet - 1.5f) * 0.15f);
            Recording.GroupSpawnsIntoUses.NoteSpawn(100, spawn.whoAmI, spawn, ItemID.Boomstick, ItemID.MusketBall, aim);
        }
        Recording.GroupSpawnsIntoUses.NotePlayerAnimation(101, 0, 20, ItemID.Boomstick, System.Array.Empty<int>());
        Recording.GroupSpawnsIntoUses.NotePlayerAnimation(102, 0, 20, ItemID.Boomstick, System.Array.Empty<int>());
        Require(LearnVolleyShapes.HasShape(ItemID.Boomstick), "the seeded spread teaches no shape");
    }

    private static string KnowledgeAuditSplits()
    {
        var events = new List<GodsEyeEvent>
        {
            Event("npc-spawn", 10, subject: 1, related: NPCID.Zombie.ToString(), label: "Zombie", detail: "slot=5"),
            Event("npc-spawn", 10, subject: 2, related: NPCID.Zombie.ToString(), label: "Zombie", detail: "slot=6"),
            Event("shot", 100, subject: 3, related: "1", label: "Wooden Bow", channel: "projectile=7",
                detail: "predicted=5@20:8.0;sim=m=1,2:a=3,4:k=9:t=0"),
            Event("npc-damage", 120, subject: 1, label: "Zombie", amount: 8, detail: "raw=8;effective=8;life-now=37;knockback=1.00"),
            Event("shot-event", 121, subject: 7, label: ProjectileID.WoodenArrowFriendly.ToString(), detail: "hits=1;walls=0;first-hit=type=3;tick=20;damage=8"),
            Event("shot", 200, subject: 3, related: "2", label: "Wooden Bow", channel: "projectile=8",
                detail: "predicted=6@20:8.0;sim=m=1,2:a=3,4:k=9:t=0"),
            Event("shot-event", 206, subject: 8, label: ProjectileID.WoodenArrowFriendly.ToString(), detail: "hits=0;walls=1;first-wall=tick=6;damage=0"),
        };
        AuditKnowledge.KnowledgeVerdict verdict = AuditKnowledge.Audit(events, null);
        Require(verdict.Types.Count == 1, $"one type, not {verdict.Types.Count}");
        AuditKnowledge.TypeCalibration row = verdict.Types[0];
        Require(row.PredictedHits == 2 && row.MatchedHits == 1,
            $"matched {row.MatchedHits} of {row.PredictedHits}, want 1 of 2");
        Require(row.WallSurprise == 1, $"wall surprise {row.WallSurprise}, want 1");
        return $"{row.Type}: 1 of 2 predicted hits landed, 1 wall surprise";
    }

    private static GodsEyeEvent Event(string kind, long tick, int subject, string related = "", string label = "",
        string channel = "", int amount = 0, string detail = "")
        => new(1, 0, tick, 0.0, kind, subject, related, label, channel, 0f, 0f, 0f, 0f, 0f, 0f, amount, detail);

    private static string HoldReproducesLiveVerdict()
    {
        // Four scenes, one question each: does the restored commitment's Validate answer what the live
        // one's did — a hold, and one release from each invalidation class: the clock, the world, what
        // was learned. The snapshot is built before the live verdict is read, because a releasing
        // Validate ends the live plan; the JSON carries the world the verdict was about either way.
        var reasons = new List<string>
        {
            RevalidateScene("hold", arrange: null, running: true),
            RevalidateScene("stall", arrange: StallPastWindow, running: true),
            RevalidateScene("target-gone", arrange: KillTarget, running: true),
            RevalidateScene("knowledge", arrange: ReviseKnowledge, running: true),
        };
        return "live and restored agree: " + string.Join(", ", reasons);
    }

    private static string RevalidateScene(string name, Action<Scene, AttackPlan>? arrange, bool running)
    {
        Scene scene = Setup(60, new Vector2(50, 56), new Vector2(40, 54), null,
            (25, NPCID.Zombie, new Vector2(51, 59)));
        var combat = scene.Companion.Combat;
        CombatWeights weights = WeighCombatObjectives.ForSenses(scene.Ctx);
        SearchAttackPlans.SearchResult result = Search(scene, weights, combat.NextPlanId++);
        Require(result.Plan != null, $"the {name} search offers nothing: " + result.Reason);
        combat.Planner.Commit(result.Plan);
        arrange?.Invoke(scene, result.Plan);
        var budget = PlanningBudget.FromMilliseconds(1000f);
        string json = ExportCombatSnapshot.Build(scene.Ctx, combat, result.Plan, result,
            weights, budget, scene.Radius, running);
        bool liveHolds = combat.Planner.Validate(scene.Ctx, scene.Companion.Brain.Positioner,
            Allows(scene), running);
        string liveReason = liveHolds ? "valid" : combat.Planner.LastInvalidation;
        RestoredDecision restored = RestoreSnapshot.Restore(json);
        AuditHold.HoldVerdict held = AuditHold.Revalidate(restored);
        Require(held.Holds == liveHolds && held.Reason == liveReason,
            $"the {name} hold disagrees: live {(liveHolds ? "valid" : liveReason)}, " +
            $"restored {(held.Holds ? "valid" : held.Reason)}");
        return liveReason;
    }

    /// <summary>Jump the clock a stall window past the segment's start: progress stays at the search.</summary>
    private static void StallPastWindow(Scene scene, AttackPlan plan)
    {
        int jumpTo = plan.Segments[0].StartTick + Weights.CombatPlanStallTicks + 1;
        Require(jumpTo < plan.Segments[0].EndTick, "premise: the stall jump lands mid-segment");
        scene.Companion.Brain.Senses.AssumeTick(jumpTo);
    }

    /// <summary>Kill the plan's target and let the senses rebuild without it.</summary>
    private static void KillTarget(Scene scene, AttackPlan plan)
    {
        Main.npc[25].active = false;
        Main.npc[25].life = 0;
        scene.Companion.Brain.Senses.Update(scene.Companion.NPC, Main.player[Main.myPlayer]);
    }

    /// <summary>Teach one outcome past the search: the revision the plan was priced at is gone.</summary>
    private static void ReviseKnowledge(Scene scene, AttackPlan plan)
    {
        float reach = scene.Companion.Combat.Weapons[0].Reach;
        float[] context = AttackLearning.Context(300f, reach, 0f, 0.1f, 0f, 0, 0f, OrbPace.MaxSpeed, false);
        AttackLearning.Observe(ItemID.WoodenBow, NPCID.Zombie, context, 1.0f);
    }

    private static string CountCutReplays()
    {
        // One simulation allowed: the search prices the first sim, cuts on the second question, and
        // offers unresolved with the cut's own word — twice identically, because the count cuts at
        // the count on every machine. Unbounded the same scene offers a plan, so the cut is the
        // cap's doing and nothing else's. The cut snapshots with no plan and replays its cut: the
        // restore searches under the stamped count and stops at the same simulation with the same word.
        Scene scene = Setup(60, new Vector2(50, 56), new Vector2(40, 54), null,
            (25, NPCID.Zombie, new Vector2(51, 59)));
        var combat = scene.Companion.Combat;
        CombatWeights weights = WeighCombatObjectives.ForSenses(scene.Ctx);
        var firstBudget = PlanningBudget.FromMilliseconds(1000f, maxSimulations: 1);
        SearchAttackPlans.SearchResult first = CappedSearch(scene, weights, combat.NextPlanId++, ref firstBudget);
        var secondBudget = PlanningBudget.FromMilliseconds(1000f, maxSimulations: 1);
        SearchAttackPlans.SearchResult second = CappedSearch(scene, weights, combat.NextPlanId++, ref secondBudget);
        Require(first.Plan == null && first.Reason == "budget-cut"
            && first.Eligibility == OfferEligibility.Unresolved,
            $"a one-sim search offers {first.Reason}, not an unresolved cut");
        Require(firstBudget.Simulations == 1, $"a one-sim search simulates {firstBudget.Simulations}, not one");
        Require(second.Reason == first.Reason && secondBudget.Simulations == firstBudget.Simulations,
            "the capped search cuts somewhere else the second time");
        SearchAttackPlans.SearchResult free = Search(scene, weights, combat.NextPlanId++);
        Require(free.Plan != null, "the scene offers nothing unbounded: " + free.Reason);
        string json = ExportCombatSnapshot.Build(scene.Ctx, combat, null, first, weights, firstBudget, scene.Radius, true);
        RestoredDecision restored = RestoreSnapshot.Restore(json);
        var replayBudget = PlanningBudget.FromMilliseconds(restored.Snapshot.AllowanceMs,
            restored.Snapshot.MaxSimulations);
        SearchAttackPlans.SearchResult replayed = SearchAttackPlans.SearchDepthOne(restored.Ctx,
            restored.Companion.Combat, restored.Companion.Brain.Positioner, Allows(new Scene
            {
                Companion = restored.Companion,
                Ctx = restored.Ctx,
                Radius = restored.Snapshot.AllowanceRadius,
            }),
            restored.Weights, restored.Companion.Combat.NextPlanId++, ref replayBudget,
            new SearchAttackPlans.SearchOptions(restored.Proposals, restored.Verdicts));
        Require(replayed.Plan == null && replayed.Reason == "budget-cut"
            && replayBudget.Simulations == firstBudget.Simulations,
            $"the restored cut offers {replayed.Reason} at {replayBudget.Simulations} sims, not the live cut");
        return $"cut at {firstBudget.Simulations} sim, twice identical, replay cuts the same";
    }

    private static SearchAttackPlans.SearchResult CappedSearch(Scene scene, CombatWeights weights, int planId,
        ref PlanningBudget budget)
    {
        var combat = scene.Companion.Combat;
        return SearchAttackPlans.SearchDepthOne(scene.Ctx, combat, scene.Companion.Brain.Positioner,
            Allows(scene), weights, planId, ref budget);
    }

    /// <summary>
    /// P9: the player is hurt with a killable zombie on him and a fat naked one mid-range. The live
    /// weights — prevention rising with danger — commit the rescue; the same scene under damage-only
    /// weights, the constant-weights mutation baked in, farms the fat body. The mutation is damage-only
    /// rather than uniform because the kill is worth ~1.5 removal units against DPS in the hundredths,
    /// so any positive removal weight agrees with danger and only a damage maximizer disagrees; the fat
    /// body is naked rather than big because size changes nothing at these ranges and nakedness flips it.
    /// </summary>
    private static string DangerCommitsHarmPreventionOverDamage()
    {
        Scene scene = Setup(60, new Vector2(50, 56), new Vector2(35, 54), null,
            (25, NPCID.Zombie, new Vector2(51, 59)),
            (26, NPCID.Zombie, new Vector2(38, 59)));
        Player player = Main.player[Main.myPlayer];
        player.statLife = 40;
        NPC urgent = Main.npc[25];
        NPC fat = Main.npc[26];
        urgent.life = urgent.lifeMax = 15;
        urgent.defense = 6;
        fat.life = fat.lifeMax = 400;
        fat.defense = 0;
        scene.Companion.Brain.Senses.Update(scene.Companion.NPC, player);
        bool urgentListed = false, fatListed = false;
        foreach (ThreatRecord threat in scene.Ctx.Senses.Threats.Threats)
        {
            if (threat.Npc != null && threat.Npc.whoAmI == 25) urgentListed = true;
            if (threat.Npc != null && threat.Npc.whoAmI == 26) fatListed = true;
        }
        Require(urgentListed && fatListed, "premise: both bodies are threat-listed");
        Require(scene.Ctx.Senses.Threats.PlayerDanger > 0.5f,
            $"premise: the player is not in danger ({scene.Ctx.Senses.Threats.PlayerDanger:0.00})");
        var combat = scene.Companion.Combat;
        SearchAttackPlans.SearchResult rescue = Search(scene, WeighCombatObjectives.ForSenses(scene.Ctx),
            combat.NextPlanId++);
        Require(rescue.Plan != null, "the danger search offers nothing: " + rescue.Reason);
        Require(rescue.Plan.PrimaryTarget == 25,
            $"danger commits target {rescue.Plan.PrimaryTarget}, not the urgent 25");
        var constant = new CombatWeights(1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
        SearchAttackPlans.SearchResult greedy = Search(scene, constant, combat.NextPlanId++);
        Require(greedy.Plan != null, "the constant search offers nothing: " + greedy.Reason);
        Require(greedy.Plan.PrimaryTarget == 26,
            $"constant weights commit {greedy.Plan.PrimaryTarget}, not the fat 26");
        return $"danger targets {rescue.Plan.PrimaryTarget}, constant targets {greedy.Plan.PrimaryTarget}";
    }

    /// <summary>
    /// P10: one zombie, nothing moving. Four rescores through the activity hold the committed plan by
    /// reference with no new search: validity keeps it, not a score bonus, and a bonus would re-search
    /// and reselect, advancing the plan id this row pins. Suspended throughout, so the stall clock
    /// that would end an unfought plan stays paused while every other validity check still runs.
    /// </summary>
    private static string UnchangedSceneKeepsOnePlanAcrossRescores()
    {
        Scene scene = Setup(60, new Vector2(50, 56), new Vector2(40, 54), null,
            (25, NPCID.Zombie, new Vector2(51, 59)));
        var combat = scene.Companion.Combat;
        CombatWeights weights = WeighCombatObjectives.ForSenses(scene.Ctx);
        SearchAttackPlans.SearchResult result = Search(scene, weights, combat.NextPlanId++);
        Require(result.Plan != null, "the scene offers nothing: " + result.Reason);
        AttackPlan held = result.Plan;
        combat.Planner.Commit(held);
        int afterCommit = combat.NextPlanId;
        var fight = new live::AICompanion.Companion.Brain.Activities.Combat.FightEnemies();
        for (int rescore = 0; rescore < 4; rescore++)
        {
            scene.Companion.Brain.Senses.AssumeTick(scene.Companion.Brain.Senses.Tick + 30);
            scene.Companion.Brain.Senses.Update(scene.Companion.NPC, Main.player[Main.myPlayer]);
            fight.Prepare(scene.Ctx);
            Require(ReferenceEquals(combat.Planner.Committed, held),
                $"rescore {rescore} drops the plan: {combat.Planner.LastInvalidation}");
            Require(combat.NextPlanId == afterCommit, $"rescore {rescore} researches");
        }
        return $"plan {held.Id} held across 4 rescores, no research";
    }
}
