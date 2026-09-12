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
            AnUnprovenApproachWalksInsteadOfScoringZero();
            AReachableOreProducesANativeBreak();
            AUsefulCurrentPoseNeedsNoApproach();
            NativeToolOutcomesDistinguishAttemptsFromProgress();
            Console.WriteLine("ore work: policy, retained vein, tool gates, unproven approach and native productive break pass");
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
        Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) == 0f && action.RemainingTiles == 0 && action.Status == "disabled", "disabled mining must not retain ore work");
    }

    private static void AReachableOreProducesANativeBreak()
    {
        Point ore = new(25, 59);
        var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        Require(live::AICompanion.Companion.Brain.WorldInteractions.Mining.OreFinder.InReach(ctx.Npc.Bottom, ore),
            "the productive-work control must begin within actual tool reach");
        Item pick = live::AICompanion.Companion.Brain.WorldInteractions.Mining.TileMiner.PickaxeFor(ctx.Player);
        int swings = 0;
        for (int tick = 0; tick < 600 && Main.tile[ore.X, ore.Y].HasTile; tick++)
        {
            ctx.Companion.Miner.Tick();
            if (ctx.Companion.Miner.Swing(ore, pick)) swings++;
        }
        Require(!Main.tile[ore.X, ore.Y].HasTile,
            $"a usable fixed pose must produce a real native tile break, not merely report {swings} swings");
        Require(ctx.Companion.Miner.LastOutcome is { Effect: live::AICompanion.Companion.Brain.WorldInteractions.TileToolEffect.Removed, Productive: true },
            "native tile removal must remain distinguishable from a requested swing or partial damage");
    }

    private static void AUsefulCurrentPoseNeedsNoApproach()
    {
        Point ore = new(25, 59);
        var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        Vector2 feet = ctx.Npc.Bottom;
        Require(live::AICompanion.Companion.Brain.WorldInteractions.Mining.OreFinder.InReach(feet, ore),
            "the current-pose fixture must already satisfy actual tool range and exposed access");
        var reach = live::AICompanion.Companion.Brain.WorldInteractions.Mining.OreFinder.Approach(ore, feet, out Vector2 stand);
        Require(reach == Reachability.Reach.Yes && stand == feet,
            $"a usable current pose needs no approach; got {reach} at {stand} instead of {feet}");
    }

    private static void NativeToolOutcomesDistinguishAttemptsFromProgress()
    {
        Point ore = new(25, 59);
        var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        Item pick = live::AICompanion.Companion.Brain.WorldInteractions.Mining.TileMiner.PickaxeFor(ctx.Player);
        Require(ctx.Companion.Miner.Swing(ore, pick), "the native partial-damage fixture must actually swing");
        var partial = ctx.Companion.Miner.LastOutcome;
        Require(partial is { Effect: live::AICompanion.Companion.Brain.WorldInteractions.TileToolEffect.Damaged, Productive: true }
            && partial.Value.After.Damage > partial.Value.Before.Damage && Main.tile[ore.X, ore.Y].HasTile,
            "partial native damage must be observed without claiming that ore was removed");
        Require(!ctx.Companion.Miner.Swing(ore, pick) && ctx.Companion.Miner.LastOutcome == partial,
            "cooldown must not fabricate a new native attempt");

        var (_, weak) = SetUp(WorkPolicy.Opportunistic, TileID.Chlorophyte, ore);
        Require(weak.Companion.Miner.Swing(ore, pick)
            && weak.Companion.Miner.LastOutcome is { Effect: live::AICompanion.Companion.Brain.WorldInteractions.TileToolEffect.NoObservedChange, Productive: false }
            && Main.tile[ore.X, ore.Y].HasTile,
            "a native weak-pick call must remain an attempt without productive damage");

        var (_, tree) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        Tile trunk = Main.tile[ore.X, ore.Y];
        trunk.TileType = TileID.Trees;
        Main.tileAxe[TileID.Trees] = true;
        Main.tileSolid[TileID.Trees] = false;
        Item axe = live::AICompanion.Companion.Brain.WorldInteractions.Chopping.TileChopper.AxeFor(tree.Player);
        Require(tree.Companion.Chopper.Swing(ore, axe)
            && tree.Companion.Chopper.LastOutcome is { Effect: live::AICompanion.Companion.Brain.WorldInteractions.TileToolEffect.Damaged, Productive: true },
            "axe progress must use its own native hit table");
        trunk.ClearEverything();
        var idleAxe = new live::AICompanion.Companion.Brain.WorldInteractions.Chopping.TileChopper();
        Require(!idleAxe.Swing(ore, axe) && idleAxe.LastOutcome == null,
            "a vanished trunk must not become a successful axe attempt");
    }

    private enum BaselineMode { FullBrain, HeldActivity, FixedWorkingPose }

    internal static int RunRaisedLipBaseline()
    {
        bool all = true;
        foreach (bool mirrored in new[] { false, true })
            foreach (BaselineMode mode in Enum.GetValues<BaselineMode>())
                all &= MeasureRaisedLipWork(mirrored, mode);
        Console.WriteLine(all ? "mining baseline: every method produced a native break"
            : "mining baseline: at least one method failed; this is a recorded implementation gap, not a passing acceptance result");
        return all ? 0 : 1;
    }

    private static bool MeasureRaisedLipWork(bool mirrored, BaselineMode mode)
    {
        Point ore = new(25, 59);
        var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        int lipX = mirrored ? 26 : 24;
        for (int y = 58; y <= 59; y++)
        {
            Tile lip = Main.tile[lipX, y];
            lip.HasTile = true;
            lip.TileType = TileID.Dirt;
        }
        if (mirrored)
        {
            ctx.Npc.position = new Vector2(30 * 16, 60 * 16 - ctx.Npc.height);
            ctx.Player.position = new Vector2(30 * 16, 60 * 16 - ctx.Player.height);
        }
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        var mine = ctx.Companion.Brain.Chooser.Actions.OfType<MineAction>().Single();
        if (mode == BaselineMode.HeldActivity)
        {
            ctx.Companion.Brain.Chooser.Actions.Clear();
            ctx.Companion.Brain.Chooser.Actions.Add(mine);
        }
        Require(!live::AICompanion.Companion.Brain.WorldInteractions.Mining.OreFinder.InReach(ctx.Npc.Bottom, ore),
            "the raised-lip fixture must obstruct the initial tool line, not test an already usable pose");
        if (mode == BaselineMode.FixedWorkingPose)
        {
            // Set the experimental initial pose; production code never teleports. This perch
            // overlaps the lip in either orientation and reaches the ore's exposed upper face.
            ctx.Npc.Bottom = new Vector2(408, 928);
            Require(!Collision.SolidCollision(ctx.Npc.position, ctx.Npc.width, ctx.Npc.height)
                && live::AICompanion.Companion.Brain.WorldInteractions.Mining.OreFinder.InReach(ctx.Npc.Bottom, ore),
                "the fixed-pose control must be native-clear and within tool reach");
        }
        var initialBody = ctx.Companion.Motor.State;
        int miningTicks = 0;
        for (int tick = 0; tick < 600 && Main.tile[ore.X, ore.Y].HasTile; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            if (mode == BaselineMode.FixedWorkingPose)
            {
                ctx.Companion.Miner.Tick();
                ctx.Companion.Miner.Swing(ore,
                    live::AICompanion.Companion.Brain.WorldInteractions.Mining.TileMiner.PickaxeFor(ctx.Player));
            }
            else ctx.Companion.AI();
            if (ctx.Companion.Brain.LastAction?.Name == "mine") miningTicks++;
            VerifyResponsiveFollowing.AdvanceNative(ctx.Companion);
            if (tick % 60 == 0)
                Console.WriteLine($"lip mirror={mirrored} mode={mode} tick={tick} feet={ctx.Npc.Bottom} "
                    + $"mine={mine.Status} target={mine.TargetTile} stand={mine.TargetStandPosition} "
                    + $"action={ctx.Companion.Brain.LastAction?.Name} request={ctx.Companion.Brain.LastRequest} nav={ctx.Companion.Brain.Navigator.Status}");
        }
        bool broken = !Main.tile[ore.X, ore.Y].HasTile;
        if (!broken)
        {
            bool jumpProven = live::AICompanion.Companion.Brain.SharedMovementSystem.ProveInteractionJump.CanReach(
                live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World, initialBody,
                body => live::AICompanion.Companion.Brain.WorldInteractions.Mining.OreFinder.InReach(body.Feet, ore));
            Console.WriteLine($"lip initial-pose ground-jump proof={jumpProven}");
            AStar.MsBudget = 0;
            var approach = live::AICompanion.Companion.Brain.WorldInteractions.Mining.OreFinder.Approach(ore, ctx.Npc.Bottom, out Vector2 stand);
            Console.WriteLine($"lip approach without wall-time limit={approach} stand={stand}; expansion bound remains {Reachability.WalkerBudget}");
            for (int x = 22; x <= 28; x++)
                for (int y = 56; y <= 59; y++)
                {
                    if (!live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.IsStandable(x, y)) continue;
                    Vector2 feet = live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.FeetWorld(new Point(x, y));
                    bool useful = live::AICompanion.Companion.Brain.WorldInteractions.Mining.OreFinder.InReach(feet, ore);
                    if (useful) Console.WriteLine($"lip usable={feet} route={Reachability.WalkerReach(live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.FeetTile(ctx.Npc.Bottom), new Point(x, y))}");
                }
        }
        Console.WriteLine($"raised-lip native mining broken={broken} (mirrored={mirrored}, mode={mode}, miningTicks={miningTicks}, "
            + $"feet={ctx.Npc.Bottom}, action={ctx.Companion.Brain.LastAction?.Name}, "
            + $"request={ctx.Companion.Brain.LastRequest}, navigator={ctx.Companion.Brain.Navigator.Status})");
        Require(Main.tile[lipX, 58].HasTile && Main.tile[lipX, 59].HasTile,
            "mining must overcome the lip through useful positioning, without excavating ordinary terrain");
        return broken;
    }

    private static void MimicStartsFromThePlayersVein()
    {
        Point ore = new(25, 59);
        var (action, ctx) = SetUp(WorkPolicy.Mimic, TileID.Copper, ore, playerHit: ore);
        Reachability.Reach directReach = Reachability.WalkerReach(new Point(20, 59), new Point(24, 59));
        float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
        Require(score > 0f && action.TargetTile == ore,
            $"mimic mining must select the ore vein the player hit (score={score}, target={action.TargetTile}, status={action.Status}, observed={ctx.Senses.Player.MinedOre}, direct={directReach})");
    }

    private static void OpportunisticKeepsOneVeinAcrossAnInterruption()
    {
        var (action, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, new Point(25, 59));
        Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) > 0f, "nearby ore must start opportunistic mining without a player hit");
        int id = action.JobId;
        action.Exit(ctx); // Guard/self-defence switching actions must not discard retained work.
        Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) > 0f && action.JobId == id, "an interrupted vein must resume with the same job identity");
        ctx.Companion.NPC.Bottom += new Vector2(64, 0);
        Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) > 0f && action.JobId == id && action.TargetStandPosition == ctx.Npc.Bottom,
            "scoring early during guard must not retain an old approach after guard moves the body again");
    }

    private static void DirtIsNeverAWorkTarget()
    {
        var (action, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Dirt, new Point(25, 59));
        Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) == 0f && action.TargetTile == null, "ordinary terrain must not be selected for mining");
    }

    private static void ADepletedTileRelocatesWithinTheVein()
    {
        Point first = new(25, 59), second = new(26, 59);
        var (action, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, first, second);
        Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) > 0f && action.RemainingTiles == 2, "the fixture must start as a two-tile vein");
        float capturedValue = action.Score(), capturedTrip = action.ForecastTicks();
        var capturedTarget = action.ActivityTarget;
        Tile removed = Main.tile[first.X, first.Y];
        removed.ClearEverything();
        ctx.Companion.NPC.position = new Vector2(26 * 16, 60 * 16 - ctx.Companion.NPC.height);
        Require(action.Score() == capturedValue && action.ForecastTicks() == capturedTrip
            && action.ActivityTarget == capturedTarget && action.RemainingTiles == 2,
            "comparison must not prune externally removed ore or recompute the prepared trip");
        Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) > 0f && action.TargetTile == second && action.RemainingTiles == 1,
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
        Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) > 0f && action.TargetTile == usable,
            "a nearby unmineable ore must not mask a farther ore the current pick can mine");
    }

    private static void AnUnmineableVeinDoesNotBecomeWork()
    {
        var (action, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Chlorophyte, new Point(25, 59));
        Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) == 0f && action.RemainingTiles == 0 && action.Status == "no mineable ore",
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
        Require(VerifyPreparedActivities.PrepareAndScore(chop, ctx) == 0f && VerifyPreparedActivities.PrepareAndScore(mine, ctx) > 0f,
            "a retained tree across a sealed wall must yield to reachable ore beside the companion");
        var sinceReachField = typeof(live::AICompanion.Companion.Brain.Behaviours.Work.ChopAction)
            .GetField("sinceReach", BindingFlags.NonPublic | BindingFlags.Instance)!;
        int preparedAge = (int)sinceReachField.GetValue(chop)!;
        for (int comparison = 0; comparison < 20; comparison++)
            Require(chop.Score() == 0f, "repeated comparison must preserve an unavailable tree's value");
        Require((int)sinceReachField.GetValue(chop)! == preparedAge,
            "chopping comparison must not advance its discovery timer or repeat its reach search");
        for (int tick = 0; tick < 20; tick++) VerifyPreparedActivities.PrepareAndScore(chop, ctx);
        Require((int)typeof(live::AICompanion.Companion.Brain.Behaviours.Work.ChopAction)
            .GetField("sinceReach", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chop)! == 20,
            "unchanged retained work must reuse its reach verdict rather than search every scoring tick");
    }

    internal static (MineAction Action, ActionContext Context) SetUp(WorkPolicy policy, ushort tileType, params Point[] ore)
        => SetUp(policy, tileType, ore, null);

    /// <summary>
    /// An approach the bounded search cannot decide used to score zero, and that zero was
    /// self-fulfilling: the reachability question is re-asked fresh from the companion's feet each
    /// time, so a body that never moves gets the same "could not tell" for ever, and walking closer
    /// — the one thing that shortens the search — is exactly what a zero score prevents. A quarter
    /// of the 2026-09-11 session sat there: 3,444 ticks with ore found, wanted, and contributing
    /// nothing to the decision.
    ///
    /// The search is starved of its time budget here rather than buried under distance, because the
    /// mechanism under test is a bounded search declining to answer, and that is precisely what a
    /// spent budget produces.
    /// </summary>
    private static void AnUnprovenApproachWalksInsteadOfScoringZero()
    {
        // Far enough along the floor that the approach search has real work to do. Ore beside the
        // companion resolves through the start-equals-goal shortcut before any budget is consulted,
        // so a near fixture cannot reach the undecided state at all.
        var (action, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, new Point(50, 59));
        double budget = AStar.MsBudget;
        try
        {
            AStar.MsBudget = 0.0001d;
            float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
            Require(action.Status == "approach unknown",
                $"the fixture must actually reach an undecided approach, or it tests nothing; status={action.Status}");
            Require(score > 0f,
                $"ore whose approach the search could not decide scored zero, so the companion stands still and the search is asked the same unanswerable question for ever; status={action.Status}");
            var request = action.Execute(ctx);
            Require(request.Kind == live::AICompanion.Companion.Brain.PositionSelection.RequestKind.Exact,
                $"an undecided approach must produce a walk toward the ore, since moving is what makes the approach decidable; got {request.Kind}");
            Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) < 0.7f,
                "an unproven approach must score below a proven ore job, so reachable ore always wins");
        }
        finally
        {
            AStar.MsBudget = budget;
        }
    }

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
        // Native destruction returns a cosmetic dust slot even in dedicated-server mode, and
        // clears mining caches on every player. Populate engine-owned arrays without drawing.
        for (int i = 0; i < Main.dust.Length; i++) Main.dust[i] ??= new Dust();
        for (int i = 0; i < Main.player.Length; i++) Main.player[i] ??= new Player();
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
        // Mod loading normally creates these arrays. This no-mod fixture still runs actual
        // native permission, placement and destruction operations, with no registered mod hooks.
        foreach (FieldInfo field in typeof(TileLoader).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
            if (field.Name.StartsWith("Hook") && field.FieldType.IsArray && field.GetValue(null) == null)
                field.SetValue(null, Array.CreateInstance(field.FieldType.GetElementType()!, 0));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
