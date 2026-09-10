extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System.Reflection;
using MineAction = live::AICompanion.Companion.Brain.Behaviours.Work.MineAction;
using WorkPolicies = live::AICompanion.Companion.Brain.Behaviours.Work.WorkPolicies;
using WorkPolicy = live::AICompanion.Companion.Brain.Behaviours.Work.WorkPolicy;
using ActionContext = live::AICompanion.Companion.Brain.Behaviours.ActionContext;
using AStar = live::AICompanion.Companion.Brain.SharedMovementSystem.AStar;
using Reachability = live::AICompanion.Companion.Brain.SharedMovementSystem.Reachability;

/// <summary>Native-tile regression checks for the work policy and retained ore-job contracts.</summary>
internal static class VerifyOreWork
{
    public static int Run()
    {
        try
        {
            DisabledDoesNotStartAJob();
            MimicStartsFromThePlayersVein();
            OpportunisticKeepsOneVeinAcrossAnInterruption();
            DirtIsNeverAWorkTarget();
            ADepletedTileRelocatesWithinTheVein();
            AWeakPickDoesNotMaskFartherOre();
            AnUnmineableVeinDoesNotBecomeWork();
            ASealedTreeYieldsToReachableOre();
            Console.WriteLine("ore work: disabled/mimic/opportunistic policy, retained vein, relocation, ore-only and tool gates pass");
            return 0;
        }
        finally
        {
            WorkPolicies.Mining = WorkPolicy.Opportunistic;
        }
    }

    private static void DisabledDoesNotStartAJob()
    {
        var (action, ctx) = SetUp(WorkPolicy.Disabled, TileID.Copper, new Point(25, 59));
        Require(action.Score(ctx) == 0f && action.RemainingTiles == 0 && action.Status == "disabled", "disabled mining must not retain ore work");
    }

    private static void MimicStartsFromThePlayersVein()
    {
        Point ore = new(25, 59);
        var (action, ctx) = SetUp(WorkPolicy.Mimic, TileID.Copper, ore, playerHit: ore);
        Reachability.Reach directReach = Reachability.WalkerReach(new Point(20, 59), new Point(24, 59));
        float score = action.Score(ctx);
        Require(score > 0f && action.TargetTile == ore,
            $"mimic mining must select the ore vein the player hit (score={score}, target={action.TargetTile}, status={action.Status}, observed={ctx.Senses.Player.MinedOre}, direct={directReach})");
    }

    private static void OpportunisticKeepsOneVeinAcrossAnInterruption()
    {
        var (action, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, new Point(25, 59));
        Require(action.Score(ctx) > 0f, "nearby ore must start opportunistic mining without a player hit");
        int id = action.JobId;
        action.Exit(ctx); // Guard/self-defence switching actions must not discard retained work.
        Require(action.Score(ctx) > 0f && action.JobId == id, "an interrupted vein must resume with the same job identity");
        ctx.Companion.NPC.Bottom += new Vector2(64, 0);
        Require(action.Score(ctx) > 0f && action.JobId == id && action.TargetStandPosition == ctx.Npc.Bottom,
            "scoring early during guard must not retain an old approach after guard moves the body again");
    }

    private static void DirtIsNeverAWorkTarget()
    {
        var (action, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Dirt, new Point(25, 59));
        Require(action.Score(ctx) == 0f && action.TargetTile == null, "ordinary terrain must not be selected for mining");
    }

    private static void ADepletedTileRelocatesWithinTheVein()
    {
        Point first = new(25, 59), second = new(26, 59);
        var (action, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, first, second);
        Require(action.Score(ctx) > 0f && action.RemainingTiles == 2, "the fixture must start as a two-tile vein");
        Tile removed = Main.tile[first.X, first.Y];
        removed.ClearEverything();
        ctx.Companion.NPC.position = new Vector2(26 * 16, 60 * 16 - ctx.Companion.NPC.height);
        Require(action.Score(ctx) > 0f && action.TargetTile == second && action.RemainingTiles == 1,
            "after one tile disappears, the retained job must relocate to the remaining ore");
        Require(action.TargetStandPosition == ctx.Companion.NPC.Bottom,
            "an in-reach resumed tile must use the body’s current stand instead of walking back to an old one");
    }

    private static void AWeakPickDoesNotMaskFartherOre()
    {
        Point weak = new(25, 59), usable = new(27, 59);
        var (action, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Chlorophyte, weak, usable);
        Tile tile = Main.tile[usable.X, usable.Y];
        tile.TileType = TileID.Copper;
        Main.tileSolid[TileID.Copper] = true;
        Require(action.Score(ctx) > 0f && action.TargetTile == usable,
            "a nearby unmineable ore must not mask a farther ore the current pick can mine");
    }

    private static void AnUnmineableVeinDoesNotBecomeWork()
    {
        var (action, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Chlorophyte, new Point(25, 59));
        Require(action.Score(ctx) == 0f && action.RemainingTiles == 0 && action.Status == "no mineable ore",
            "a pickaxe that cannot damage ore must not create a retained mining job");
    }

    private static void ASealedTreeYieldsToReachableOre()
    {
        var (mine, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, new Point(25, 59));
        for (int y = 0; y < 100; y++)
        {
            Tile wall = Main.tile[30, y];
            wall.HasTile = true;
            wall.TileType = TileID.Dirt;
        }
        Tile trunk = Main.tile[40, 59];
        trunk.HasTile = true;
        trunk.TileType = TileID.Trees;
        Main.tileAxe[TileID.Trees] = true;
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        var chop = new live::AICompanion.Companion.Brain.Behaviours.Work.ChopAction();
        var tree = new live::AICompanion.Companion.Brain.WorldInteractions.Chopping.TreeFinder.ChoppableTree(
            new Point(40, 59), new Vector2(38 * 16 + 8, 60 * 16), 1);
        typeof(live::AICompanion.Companion.Brain.Behaviours.Work.ChopAction)
            .GetField("tree", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(chop, tree);
        Require(chop.Score(ctx) == 0f && mine.Score(ctx) > 0f,
            "a retained tree across a sealed wall must yield to reachable ore beside the companion");
        for (int tick = 0; tick < 20; tick++) chop.Score(ctx);
        Require((int)typeof(live::AICompanion.Companion.Brain.Behaviours.Work.ChopAction)
            .GetField("sinceReach", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chop)! == 20,
            "unchanged retained work must reuse its reach verdict rather than search every scoring tick");
    }

    private static (MineAction Action, ActionContext Context) SetUp(WorkPolicy policy, ushort tileType, params Point[] ore)
        => SetUp(policy, tileType, ore, null);

    private static (MineAction Action, ActionContext Context) SetUp(WorkPolicy policy, ushort tileType, Point ore, Point? playerHit)
        => SetUp(policy, tileType, new[] { ore }, playerHit);

    private static (MineAction Action, ActionContext Context) SetUp(WorkPolicy policy, ushort tileType, Point[] ore, Point? playerHit)
    {
        Main.maxTilesX = Main.maxTilesY = 100;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance
            | BindingFlags.Public | BindingFlags.NonPublic, null,
            new object[] { (ushort)100, (ushort)100 }, null)!;
        WorkPolicies.Mining = policy;
        var companion = VerifyCompanionLifecycle.Create();
        Main.gameMenu = false;
        InitialiseVanillaTileHooks();
        AStar.MsBudget = 0;
        AStar.InvalidateEdges();
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World = new live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld();
        var copperPickaxe = new Item();
        copperPickaxe.SetDefaults(ItemID.CopperPickaxe);
        ContentSamples.ItemsByType[ItemID.CopperPickaxe] = copperPickaxe;
        // EngineReplay's minimal tile table seeds only dirt. Ores are solid in Terraria's
        // live table, so the fixture must state that fact before testing native occlusion.
        Main.tileSolid[tileType] = true;
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2;
        player.position = new Vector2(20 * 16, 60 * 16 - player.height);
        companion.NPC.position = new Vector2(20 * 16, 60 * 16 - companion.NPC.height);
        Player.tileRangeX = Player.tileRangeY = 5;
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile floor = Main.tile[x, 60];
            floor.ClearEverything();
            floor.HasTile = true;
            floor.TileType = TileID.Dirt;
        }
        foreach (Point p in ore)
        {
            Tile tile = Main.tile[p.X, p.Y];
            tile.ClearEverything();
            tile.HasTile = true;
            tile.TileType = tileType;
        }
        ProbeOreLineTarget(companion.NPC.Bottom, ore[0]);
        if (playerHit is Point hit)
        {
            bool fail = false, effectOnly = false, noItem = false;
            new live::AICompanion.Companion.Brain.WorldObservation.TileDamageWatcher()
                .KillTile(hit.X, hit.Y, tileType, ref fail, ref effectOnly, ref noItem);
        }
        companion.Brain.Senses.Update(companion.NPC, player, companion.Breath);
        return (new MineAction(), new ActionContext(companion, companion.Brain.Senses));
    }

    private static void ProbeOreLineTarget(Vector2 feet, Point ore)
    {
        Vector2 eye = feet + new Vector2(0f, -30f);
        bool oreCentre = Collision.CanHitLine(eye, 1, 1, ore.ToWorldCoordinates(), 1, 1);
        bool exposedFace = Collision.CanHitLine(eye, 1, 1, new Point(ore.X - 1, ore.Y).ToWorldCoordinates(), 1, 1);
        Require(!oreCentre && exposedFace,
            "native CanHitLine must reject the solid ore destination but accept its exposed adjacent face");
    }

    private static void InitialiseVanillaTileHooks()
    {
        FieldInfo field = typeof(TileLoader).GetField("HookCanKillTile", BindingFlags.Static | BindingFlags.NonPublic)!;
        if (field.GetValue(null) == null)
            field.SetValue(null, Array.CreateInstance(field.FieldType.GetElementType()!, 0));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
