#nullable enable

extern alias live;

using System;
using System.Collections.Generic;
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

namespace AICompanion.Tools.CombatAudit;

/// <summary>
/// Rows A1-A4: the audit proving itself on scenes it builds. A1 snapshots a live search and replays it
/// exactly, then drops the verdicts and shows the replay failing without them. A2 plants proposals that
/// miss the best stand and shows the exhaustive grid finding it with "no generator" — and nothing when
/// handed only the live proposals. A3 plants a damage-driven flip, shows the sweep reporting it, and
/// shows a unit sweep reporting nothing. A4 pairs a matched and a mismatched shot against synthetic
/// events and shows the calibration splitting them. Each row bakes its file-8 mutation in as the
/// second assertion, so the mutation that must fail it fails it every run, not by hand-reversion.
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
        Console.WriteLine(red == 0 ? "combat-audit self-test: 4 rows green" : $"combat-audit self-test: {red} rows red");
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
            companion.Brain.Senses, null);
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
        string json = ExportCombatSnapshot.Build(scene.Ctx, combat, result.Plan, result, weights, budget, scene.Radius);
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
        return $"stand {result.Plan!.Segments[0].Stand.Stand}, {result.FrontSize} on the front, verdict-less replay {dropped.Reason}";
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
        string json = ExportCombatSnapshot.Build(scene.Ctx, combat, pinned.Plan, pinned, weights, budget, scene.Radius);
        RestoredDecision restored = RestoreSnapshot.Restore(json);
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
        SearchAttackPlans.SearchResult both = Search(scene, weights, 1,
            new SearchAttackPlans.SearchOptions(new[] { near, far }));
        Require(both.Plan != null, "neither pinned stand solves: " + both.Reason);
        var budget = PlanningBudget.FromMilliseconds(1000f);
        string json = ExportCombatSnapshot.Build(scene.Ctx, combat, both.Plan, both, weights, budget, scene.Radius);
        RestoredDecision restored = RestoreSnapshot.Restore(json);
        List<AuditWeights.SweepMove> moves = AuditWeights.Sweep(restored, new[] { 1.0f, 2.0f });
        AuditWeights.SweepMove? damageDouble = null;
        foreach (AuditWeights.SweepMove move in moves)
        {
            if (move.Factor == 1f)
            {
                Require(!move.Changed, $"a unit sweep moves {move.Name}");
                Require(move.Weighted == both.Plan!.Weighted,
                    $"a unit sweep reprices {move.Name}: {move.Weighted} != {both.Plan.Weighted}");
            }
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
}
