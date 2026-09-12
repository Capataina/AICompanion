extern alias live;

using FindToolAccess = live::AICompanion.Companion.Brain.WorldInteractions.FindToolAccess;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System.Reflection;
using MineOre = live::AICompanion.Companion.Brain.PurposeFamilies.Gathering.MineOre;
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
            ChoppingPrefersASeparateActiveTrunk();
            RevokedWorkCannotExecuteAPreparedCandidate();
            ChoppingUsesActualReachRatherThanStandDistance();
            LosingWorkEligibilityDoesNotClaimCompletion();
            OreDisappearanceAndAttributedRemovalRemainSeparate();
            PreparedToolsRejectReplacementMaterial();
            AxeEligibilityAloneDoesNotMakeATree();
            AnUnprovenApproachWalksInsteadOfScoringZero();
            AnUnknownApproachKeepsItsOwnOreIdentity();
            AReachableOreProducesANativeBreak();
            AUsefulCurrentPoseNeedsNoApproach();
            AProjectileInterruptsCoherentToolOwnership();
            RaisedLipsAtBothGravitiesProduceWork();
            NativeToolOutcomesDistinguishAttemptsFromProgress();
            PreparedWorkForecastRespondsToNativeProgress();
            RemainingToolWorkMatchesNativeCompletion();
            DepartingPlayerChangesWhetherWorkIsWorthFinishing();
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

    private static void DepartingPlayerChangesWhetherWorkIsWorthFinishing()
    {
        foreach (int separation in new[] { 480, 576, 640 })
        foreach (bool nearlyDone in new[] { false, true })
        {
            Point ore = new(25, 89);
            var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
            var brain = ctx.Companion.Brain;
            var workClock = new live::AICompanion.Companion.Brain.WorldObservation.TileDamageClock();
            workClock.OnWorldLoad();
            brain.Chooser.Actions.RemoveAll(action => action.Name is not ("mine" or "keep-company"));
            ctx.Player.Bottom = ctx.Npc.Bottom + new Vector2(separation - 120 * 4, 0);
            for (int tick = 0; tick < 120; tick++)
            {
                ctx.Player.velocity = new Vector2(4, 0);
                ctx.Player.position += ctx.Player.velocity;
                VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
                brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
                workClock.PostUpdateEverything();
            }
            Item pick = live::AICompanion.Companion.Brain.WorldInteractions.Mining.TileMiner.PickaxeFor(ctx.Player);
            if (nearlyDone)
                while (ctx.Companion.Miner.EstimateRemaining(ore, pick) is { Hits: > 1 })
                {
                    Require(ctx.Companion.Miner.Swing(ore, pick), "paired completion fixture needs a native hit");
                    for (int tick = 0; tick < pick.useTime; tick++) ctx.Companion.Miner.Tick();
                }
            var selected = brain.Chooser.Choose(ctx);
            Require(selected?.Name == (nearlyDone ? "mine" : "keep-company"),
                $"departure should distinguish fresh work from a one-hit finish: separation={separation}; nearlyDone={nearlyDone}; selected={selected?.Name}; scores={string.Join(",", brain.Chooser.LastScores.Select(s => s.Action.Name + "=" + s.Final))}");
        }
    }

    private static void PreparedWorkForecastRespondsToNativeProgress()
    {
        Point ore = new(25, 89);
        var (mine, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        float untouched = mine.ForecastTicks();
        Item pick = live::AICompanion.Companion.Brain.WorldInteractions.Mining.TileMiner.PickaxeFor(ctx.Player);
        Require(ctx.Companion.Miner.Swing(ore, pick) && Main.tile[ore.X, ore.Y].HasTile,
            "remaining-work fixture needs actual partial native damage");
        for (int tick = 0; tick < pick.useTime; tick++) ctx.Companion.Miner.Tick();
        Require(mine.ForecastTicks() == untouched, "comparison must retain its prepared estimate until refreshed");
        VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        Require(mine.ForecastTicks() < untouched,
            $"remaining work must shrink after native progress with the same pose and target: before={untouched}; after={mine.ForecastTicks()}");
    }

    private static void RemainingToolWorkMatchesNativeCompletion()
    {
        Require(live::AICompanion.Companion.Brain.WorldInteractions.RemainingToolWork.Estimate(100, 35, 0, 10, "fixture")
            is { Hits: 1, Ticks: > 0 }, "an existing tile with a saturated buffer must still require an operation");
        bool priorWorld = Main.getGoodWorld;
        try
        {
            foreach (bool worldModifier in new[] { false, true })
            {
                Point ore = new(25, 89);
                var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
                Main.getGoodWorld = worldModifier;
                var miner = ctx.Companion.Miner;
                Item pick = live::AICompanion.Companion.Brain.WorldInteractions.Mining.TileMiner.PickaxeFor(ctx.Player);
                var initial = miner.EstimateRemaining(ore, pick);
                Require(initial is { Hits: > 0 } && miner.LastOutcome == null && miner.Ready,
                    "estimating native work must not swing or change cooldown");
                int actualHits = 0;
                while (Main.tile[ore.X, ore.Y].HasTile && actualHits <= initial.Value.Hits)
                {
                    Require(miner.Swing(ore, pick), "predicted native mining strike must be admitted");
                    actualHits++;
                    var cooling = miner.EstimateRemaining(ore, pick);
                    for (int tick = 0; tick < pick.useTime; tick++) miner.Tick();
                    var ready = miner.EstimateRemaining(ore, pick);
                    if (Main.tile[ore.X, ore.Y].HasTile)
                    {
                        Require(ready?.Hits == initial.Value.Hits - actualHits,
                            "native progress must reduce the predicted number of remaining hits");
                        Require(cooling?.Ticks - ready?.Ticks == pick.useTime,
                            "remaining work must include current tool cooldown exactly once");
                    }
                }
                Require(!Main.tile[ore.X, ore.Y].HasTile && actualHits == initial.Value.Hits,
                    $"native completion must match modelled hit count with world modifier={worldModifier}");
                Require(miner.EstimateRemaining(ore, pick) == null, "a removed tile has no pending tool completion");
            }
            Point hardOre = new(25, 89);
            var (_, weak) = SetUp(WorkPolicy.Opportunistic, TileID.Chlorophyte, hardOre);
            Require(weak.Companion.Miner.EstimateRemaining(hardOre,
                live::AICompanion.Companion.Brain.WorldInteractions.Mining.TileMiner.PickaxeFor(weak.Player)) == null,
                "an incapable pick must not claim a finite completion estimate");
            var (_, tree) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, hardOre);
            Main.tile[hardOre.X, hardOre.Y].TileType = TileID.Trees;
            Main.tileAxe[TileID.Trees] = true;
            Main.tileSolid[TileID.Trees] = false;
            var chopper = tree.Companion.Chopper;
            Item axe = live::AICompanion.Companion.Brain.WorldInteractions.Chopping.TileChopper.AxeFor(tree.Player);
            var before = chopper.EstimateRemaining(hardOre, axe);
            Require(before is { Hits: > 1 } && chopper.LastOutcome == null,
                "axe estimate must describe unfinished work without producing an effect");
            Require(chopper.Swing(hardOre, axe) && chopper.LastOutcome is { Productive: true },
                "axe estimate fixture must observe productive native damage");
            for (int tick = 0; tick < axe.useTime; tick++) chopper.Tick();
            var after = chopper.EstimateRemaining(hardOre, axe);
            Require(after?.Hits == before.Value.Hits - 1 && after?.Ticks < before.Value.Ticks,
                "a productive axe hit must reduce the next completion estimate");
        }
        finally { Main.getGoodWorld = priorWorld; }
    }

    private static void AReachableOreProducesANativeBreak()
    {
        Point ore = new(25, 59);
        var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        Require(FindToolAccess.InReach(ctx.Npc.Bottom, ore),
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
        Require(FindToolAccess.InReach(feet, ore),
            "the current-pose fixture must already satisfy actual tool range and exposed access");
        var reach = FindToolAccess.Approach(ore, feet, out Vector2 stand);
        Require(reach == Reachability.Reach.Yes && stand == feet,
            $"a usable current pose needs no approach; got {reach} at {stand} instead of {feet}");
    }

    private static void AProjectileInterruptsCoherentToolOwnership()
    {
        var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, new Point(25, 59));
        var mine = ctx.Companion.Brain.Chooser.Actions.OfType<MineOre>().Single();
        ctx.Companion.Brain.Chooser.Actions.Clear();
        ctx.Companion.Brain.Chooser.Actions.Add(mine);
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
        var effect = ctx.Companion.Miner.LastOutcome;
        Require(effect is { Productive: true } && mine.HandsBusy
            && ctx.Companion.Brain.ControlGrants.Last?.Hand == live::AICompanion.Companion.Brain.ActivityCoordination.HandGrant.WorkTool,
            "actual native mining must reserve the tool hand");
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
        Require(ctx.Companion.Miner.LastOutcome == effect && mine.HandsBusy
            && ctx.Companion.Brain.ControlGrants.Last?.Hand == live::AICompanion.Companion.Brain.ActivityCoordination.HandGrant.WorkTool,
            "a cooldown gap must retain tool ownership without fabricating another swing");

        Main.projectile[0] = new Projectile { whoAmI = 0, active = true, hostile = true, damage = 10,
            width = 8, height = 8, position = ctx.Npc.Center + new Vector2(28, -4), velocity = new Vector2(-8, 0), timeLeft = 100 };
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
        Require(ctx.Companion.Brain.Senses.Threats.Threats.Count == 0
            && ctx.Companion.Brain.Reflexes.Active == "avoid-collision"
            && ctx.Companion.Brain.ControlGrants.Last is { AppliedOwner: "combat-reflex", Hand: live::AICompanion.Companion.Brain.ActivityCoordination.HandGrant.Available }
            && ctx.Companion.Brain.Chooser.Activity.Phase == live::AICompanion.Companion.Brain.BehaviourSelection.ActivityPhase.Suspended
            && !mine.HandsBusy && ctx.Companion.Miner.LastOutcome == effect,
            $"a projectile without an enemy must suspend native work and grant avoidance with a free hand; enemies={ctx.Companion.Brain.Senses.Threats.Threats.Count}; projectiles={ctx.Companion.Brain.Senses.Projectiles.Threats.Count}; reflex={ctx.Companion.Brain.Reflexes.Active}; grant={ctx.Companion.Brain.ControlGrants.Last}; phase={ctx.Companion.Brain.Chooser.Activity.Phase}; busy={mine.HandsBusy}; same-effect={ctx.Companion.Miner.LastOutcome == effect}");
        Main.projectile[0].active = false;
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

    private static void RaisedLipsAtBothGravitiesProduceWork()
    {
        foreach (int floor in new[] { 60, 90 })
            foreach (bool mirrored in new[] { false, true })
                foreach (BaselineMode mode in new[] { BaselineMode.FullBrain, BaselineMode.HeldActivity })
                    Require(MeasureRaisedLipWork(mirrored, mode, floor),
                        $"raised lip must produce a native ore break: floor={floor}, mirrored={mirrored}, mode={mode}");
    }

    internal static int RunRaisedLipBaseline()
    {
        bool all = true;
        foreach (int floor in new[] { 60, 90 })
            foreach (bool mirrored in new[] { false, true })
                foreach (BaselineMode mode in Enum.GetValues<BaselineMode>())
                    all &= MeasureRaisedLipWork(mirrored, mode, floor, diagnostics: true);
        Console.WriteLine(all ? "mining baseline: every method produced a native break"
            : "mining baseline: at least one method failed; this is a recorded implementation gap, not a passing acceptance result");
        return all ? 0 : 1;
    }

    private static bool MeasureRaisedLipWork(bool mirrored, BaselineMode mode, int floor, bool diagnostics = false)
    {
        Point ore = new(25, floor - 1);
        var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        int lipX = mirrored ? 26 : 24;
        for (int y = floor - 2; y < floor; y++)
        {
            Tile lip = Main.tile[lipX, y];
            lip.HasTile = true;
            lip.TileType = TileID.Dirt;
        }
        if (mirrored)
        {
            ctx.Npc.position = new Vector2(30 * 16, floor * 16 - ctx.Npc.height);
            ctx.Player.position = new Vector2(30 * 16, floor * 16 - ctx.Player.height);
        }
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        var mine = ctx.Companion.Brain.Chooser.Actions.OfType<MineOre>().Single();
        if (mode == BaselineMode.HeldActivity)
        {
            ctx.Companion.Brain.Chooser.Actions.Clear();
            ctx.Companion.Brain.Chooser.Actions.Add(mine);
        }
        Require(!FindToolAccess.InReach(ctx.Npc.Bottom, ore),
            "the raised-lip fixture must obstruct the initial tool line, not test an already usable pose");
        if (mode == BaselineMode.FixedWorkingPose)
        {
            // Set the experimental initial pose; production code never teleports. This perch
            // overlaps the lip in either orientation and reaches the ore's exposed upper face.
            ctx.Npc.Bottom = new Vector2(408, (floor - 2) * 16);
            Require(!Collision.SolidCollision(ctx.Npc.position, ctx.Npc.width, ctx.Npc.height)
                && FindToolAccess.InReach(ctx.Npc.Bottom, ore),
                "the fixed-pose control must be native-clear and within tool reach");
        }
        var initialBody = ctx.Companion.Motor.State;
        if (diagnostics) Console.WriteLine($"lip environment floor={floor} worldSurface={Main.worldSurface} initial={initialBody}");
        if (diagnostics && mode == BaselineMode.HeldActivity)
        {
            DescribeRaisedLipRoutes(ctx, ore);
            live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        }
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
            else VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
            if (ctx.Companion.Brain.LastAction?.Name == "mine") miningTicks++;
            VerifyResponsiveFollowing.AdvanceNative(ctx.Companion);
            if (diagnostics && tick % 60 == 0)
                Console.WriteLine($"lip mirror={mirrored} mode={mode} tick={tick} feet={ctx.Npc.Bottom} "
                    + $"mine={mine.Status} target={mine.TargetTile} stand={mine.TargetStandPosition} "
                    + $"action={ctx.Companion.Brain.LastAction?.Name} request={ctx.Companion.Brain.LastRequest} nav={ctx.Companion.Brain.Navigator.Status}");
        }
        bool broken = !Main.tile[ore.X, ore.Y].HasTile;
        if (!broken)
        {
            bool jumpProven = live::AICompanion.Companion.Brain.SharedMovementSystem.ProveInteractionJump.CanReach(
                live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World, initialBody,
                body => FindToolAccess.InReach(body.Feet, ore));
            Console.WriteLine($"lip initial-pose ground-jump proof={jumpProven}");
            AStar.MsBudget = 0;
            var approach = FindToolAccess.Approach(ore, ctx.Npc.Bottom, out Vector2 stand);
            Console.WriteLine($"lip approach without wall-time limit={approach} stand={stand}; expansion bound remains {Reachability.WalkerBudget}");
            for (int x = 22; x <= 28; x++)
                for (int y = floor - 4; y < floor; y++)
                {
                    if (!live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.IsStandable(x, y)) continue;
                    Vector2 feet = live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.FeetWorld(new Point(x, y));
                    bool useful = FindToolAccess.InReach(feet, ore);
                    if (useful) Console.WriteLine($"lip usable={feet} route={Reachability.WalkerReach(live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.FeetTile(ctx.Npc.Bottom), new Point(x, y))}");
                }
        }
        Console.WriteLine($"raised-lip native mining broken={broken} (floor={floor}, mirrored={mirrored}, mode={mode}, miningTicks={miningTicks}, "
            + $"feet={ctx.Npc.Bottom}, action={ctx.Companion.Brain.LastAction?.Name}, "
            + $"request={ctx.Companion.Brain.LastRequest}, navigator={ctx.Companion.Brain.Navigator.Status})");
        Require(Main.tile[lipX, floor - 2].HasTile && Main.tile[lipX, floor - 1].HasTile,
            "mining must overcome the lip through useful positioning, without excavating ordinary terrain");
        return broken;
    }

    private static void DescribeRaisedLipRoutes(in ActionContext ctx, Point ore)
    {
        var start = live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.FeetTile(ctx.Npc.Bottom);
        for (int x = ore.X - 3; x <= ore.X + 3; x++)
            for (int y = ore.Y - 3; y <= ore.Y; y++)
            {
                var pose = live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.StandAt(x, y, false);
                if (pose == null) continue;
                var tile = new Point(x, y);
                Vector2 feet = live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.FeetWorld(tile);
                if (!FindToolAccess.InReach(feet, ore)) continue;
                var route = AStar.Find(start, tile, Reachability.WalkerBudget, out int used, out var stop);
                Console.WriteLine($"lip initial useful pose={feet} route={stop} used={used} partial={route?.Partial} "
                    + $"steps={string.Join(';', route?.Steps.Select(step => $"{step.Kind}:{step.From}->{step.Tile}") ?? Array.Empty<string>())}");
            }
        for (int x = ore.X - 3; x <= ore.X + 3; x++)
        {
            var tile = new Point(x, ore.Y);
            var pose = live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.StandAt(tile.X, tile.Y, false);
            if (pose == null) continue;
            var jumps = new live::AICompanion.Companion.Brain.SharedMovementSystem.JumpTraversal().Candidates(
                live::AICompanion.Companion.Brain.SharedMovementSystem.NavNode.At(tile), pose, false);
            Console.WriteLine($"lip jump proposals from={tile}: {string.Join(';', jumps.Select(edge => edge.Step.Tile))}");
        }
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
        var chop = new live::AICompanion.Companion.Brain.PurposeFamilies.Gathering.ChopTree();
        var tree = new live::AICompanion.Companion.Brain.WorldInteractions.Chopping.TreeFinder.ChoppableTree(
            new Point(40, 59), new Vector2(38 * 16 + 8, 60 * 16), 1);
        typeof(live::AICompanion.Companion.Brain.PurposeFamilies.Gathering.ChopTree)
            .GetField("tree", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(chop, tree);
        Require(VerifyPreparedActivities.PrepareAndScore(chop, ctx) == 0f && VerifyPreparedActivities.PrepareAndScore(mine, ctx) > 0f,
            "a retained tree across a sealed wall must yield to reachable ore beside the companion");
        var sinceReachField = typeof(live::AICompanion.Companion.Brain.PurposeFamilies.Gathering.ChopTree)
            .GetField("sinceReach", BindingFlags.NonPublic | BindingFlags.Instance)!;
        int preparedAge = (int)sinceReachField.GetValue(chop)!;
        for (int comparison = 0; comparison < 20; comparison++)
            Require(chop.Score() == 0f, "repeated comparison must preserve an unavailable tree's value");
        Require((int)sinceReachField.GetValue(chop)! == preparedAge,
            "chopping comparison must not advance its discovery timer or repeat its reach search");
        for (int tick = 0; tick < 20; tick++) VerifyPreparedActivities.PrepareAndScore(chop, ctx);
        Require((int)typeof(live::AICompanion.Companion.Brain.PurposeFamilies.Gathering.ChopTree)
            .GetField("sinceReach", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chop)! == 20,
            "unchanged retained work must reuse its reach verdict rather than search every scoring tick");
    }

    private static void ChoppingPrefersASeparateActiveTrunk()
    {
        WorkPolicy original = WorkPolicies.Chopping;
        var clock = new live::AICompanion.Companion.Brain.WorldObservation.TileDamageClock();
        try
        {
            Point first = new(25, 89), second = new(32, 89);
            var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, first);
            clock.OnWorldLoad();
            Main.tileAxe[TileID.Trees] = true;
            Main.tileSolid[TileID.Trees] = false;
            TileID.Sets.IsATreeTrunk[TileID.Trees] = true;
            foreach (Point point in new[] { first, second })
            {
                Tile trunk = Main.tile[point.X, point.Y];
                trunk.HasTile = true;
                trunk.TileType = TileID.Trees;
            }
            void PlayerHits(Point point)
            {
                bool fail = true, effectOnly = false, noItem = false;
                new live::AICompanion.Companion.Brain.WorldObservation.TileDamageWatcher()
                    .KillTile(point.X, point.Y, TileID.Trees, ref fail, ref effectOnly, ref noItem);
                ctx.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
                Require(ctx.Senses.Player.ChoppedTree == point, "cooperation fixture must observe the actual active trunk");
            }
            WorkPolicies.Chopping = WorkPolicy.Opportunistic;
            PlayerHits(first);
            var chop = new live::AICompanion.Companion.Brain.PurposeFamilies.Gathering.ChopTree();
            Require(VerifyPreparedActivities.PrepareAndScore(chop, ctx) > 0 && chop.ActivityTarget == second.ToWorldCoordinates(),
                "automatic chopping must prefer a separate usable tree over the nearer player trunk");
            PlayerHits(second);
            Require(VerifyPreparedActivities.PrepareAndScore(chop, ctx) > 0 && chop.ActivityTarget == first.ToWorldCoordinates(),
                "a new player trunk must refresh cooperation before the ordinary discovery deadline");
            Tile removed = Main.tile[first.X, first.Y];
            removed.HasTile = false;
            Require(VerifyPreparedActivities.PrepareAndScore(new live::AICompanion.Companion.Brain.PurposeFamilies.Gathering.ChopTree(), ctx) > 0,
                "automatic cooperation is a preference and must permit the sole remaining player tree");
            WorkPolicies.Chopping = WorkPolicy.Mimic;
            Require(VerifyPreparedActivities.PrepareAndScore(new live::AICompanion.Companion.Brain.PurposeFamilies.Gathering.ChopTree(), ctx) == 0,
                "Mimic must retain its explicit exclusion of the active player trunk");
        }
        finally { WorkPolicies.Chopping = original; clock.OnWorldUnload(); }
    }

    private static void RevokedWorkCannotExecuteAPreparedCandidate()
    {
        WorkPolicy original = WorkPolicies.Chopping;
        try
        {
            foreach (bool chopping in new[] { false, true })
            foreach (bool inPosition in new[] { false, true })
            {
                Point tile = new(25, 89);
                var (mine, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, tile);
                WorkPolicies.Chopping = WorkPolicy.Opportunistic;
                live::AICompanion.Companion.Brain.Behaviours.CompanionAction action = mine;
                if (chopping)
                {
                    Tile trunk = Main.tile[tile.X, tile.Y];
                    trunk.TileType = TileID.Trees;
                    Main.tileAxe[TileID.Trees] = true;
                    Main.tileSolid[TileID.Trees] = false;
                    TileID.Sets.IsATreeTrunk[TileID.Trees] = true;
                    action = new live::AICompanion.Companion.Brain.PurposeFamilies.Gathering.ChopTree();
                }
                if (inPosition) ctx.Npc.Bottom = new Vector2(23 * 16 + 8, 90 * 16);
                else ctx.Npc.Bottom = new Vector2(15 * 16 + 8, 90 * 16);
                Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) > 0,
                    $"permission fixture needs prepared work: chopping={chopping}; inPosition={inPosition}");
                if (chopping) WorkPolicies.Chopping = WorkPolicy.Disabled;
                else WorkPolicies.Mining = WorkPolicy.Disabled;
                var request = action.Execute(ctx);
                Require(request == live::AICompanion.Companion.Brain.PositionSelection.PositionRequest.Hold
                    && !action.HandsBusy && ctx.Companion.Miner.LastOutcome == null && ctx.Companion.Chopper.LastOutcome == null,
                    $"revoked work must neither approach nor swing: chopping={chopping}; inPosition={inPosition}; request={request}; hands={action.HandsBusy}");
            }
        }
        finally { WorkPolicies.Chopping = original; WorkPolicies.Mining = WorkPolicy.Opportunistic; }
    }

    private static void ChoppingUsesActualReachRatherThanStandDistance()
    {
        WorkPolicy original = WorkPolicies.Chopping;
        try
        {
            foreach (bool mirrored in new[] { false, true })
            {
                Point bottom = new(25, 89);
                var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, bottom);
                Tile trunk = Main.tile[bottom.X, bottom.Y];
                trunk.TileType = TileID.Trees;
                Main.tileAxe[TileID.Trees] = true;
                Main.tileSolid[TileID.Trees] = false;
                TileID.Sets.IsATreeTrunk[TileID.Trees] = true;
                WorkPolicies.Chopping = WorkPolicy.Opportunistic;
                ctx.Npc.Bottom = new Vector2((mirrored ? 30 : 20) * 16 + 8, 90 * 16);
                var chop = new live::AICompanion.Companion.Brain.PurposeFamilies.Gathering.ChopTree();
                Require(VerifyPreparedActivities.PrepareAndScore(chop, ctx) > 0,
                    "a tree inside actual reach must prepare useful work");
                var request = chop.Execute(ctx);
                Require(request == live::AICompanion.Companion.Brain.PositionSelection.PositionRequest.Hold
                    && chop.HandsBusy && ctx.Companion.Chopper.LastOutcome is { Productive: true },
                    $"actual axe access must produce native work without walking to a preferred stand: mirrored={mirrored}; request={request}");
                var firstEffect = ctx.Companion.Chopper.LastOutcome;
                for (int tick = 0; tick < 120; tick++) ctx.Companion.Chopper.Tick();
                Player.tileRangeX = 1;
                chop.Execute(ctx);
                Require(!chop.HandsBusy && ctx.Companion.Chopper.LastOutcome == firstEffect,
                    "a prepared working pose must not bypass a later reduction in tool reach");
                Player.tileRangeX = 5;
                for (int y = 80; y < 90; y++)
                {
                    Tile wall = Main.tile[mirrored ? 26 : 24, y];
                    wall.HasTile = true;
                    wall.TileType = TileID.Dirt;
                }
                Require(!FindToolAccess.InReach(ctx.Npc.Bottom, bottom), "native wall must occlude the retained axe target");
                chop.Execute(ctx);
                Require(!chop.HandsBusy && ctx.Companion.Chopper.LastOutcome == firstEffect,
                    "a wall added after preparation must prevent another native axe effect");
            }
        }
        finally { WorkPolicies.Chopping = original; }
    }

    private static void LosingWorkEligibilityDoesNotClaimCompletion()
    {
        Point ore = new(25, 89);
        var (mine, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) > 0, "completion fixture needs a retained vein");
        Tile replacement = Main.tile[ore.X, ore.Y];
        replacement.TileType = TileID.Chlorophyte;
        Main.tileSolid[TileID.Chlorophyte] = true;
        VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        Require(!mine.Status.StartsWith("completed", StringComparison.Ordinal),
            $"a transformed unmineable deposit must not be reported as completed work: {mine.Status}");
        Require(mine.LastConclusion is { Tracked: 1, Changed: 1, Missing: 0, ObservedClear: false, CompanionRemovals: 0 },
            "the ended job must retain the changed material instead of losing it during eligibility pruning");
    }

    private static void OreDisappearanceAndAttributedRemovalRemainSeparate()
    {
        foreach (bool ownRemoval in new[] { false, true })
        {
            Point ore = new(25, 89);
            var (mine, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
            Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) > 0, "attribution fixture needs a prepared vein");
            int id = mine.JobId;
            if (ownRemoval)
            {
                for (int tick = 0; tick < 600 && Main.tile[ore.X, ore.Y].HasTile; tick++)
                {
                    mine.Execute(ctx);
                    ctx.Companion.Miner.Tick();
                }
                Require(!Main.tile[ore.X, ore.Y].HasTile, "the companion must cause a real native ore removal");
            }
            else
            {
                Tile removed = Main.tile[ore.X, ore.Y];
                removed.HasTile = false;
            }
            VerifyPreparedActivities.PrepareAndScore(mine, ctx);
            Require(mine.LastConclusion is { Tracked: 1, Missing: 1, Present: 0, Changed: 0, ObservedClear: true } end
                && end.JobId == id && end.CompanionRemovals == (ownRemoval ? 1 : 0),
                $"a cleared observed vein must retain actual removal attribution: own={ownRemoval}; end={mine.LastConclusion}");
            var retained = mine.LastConclusion;
            VerifyPreparedActivities.PrepareAndScore(mine, ctx);
            Require(mine.LastConclusion == retained, "ending an empty job again must not overwrite its original evidence");
        }
    }

    private static void PreparedToolsRejectReplacementMaterial()
    {
        WorkPolicy original = WorkPolicies.Chopping;
        try
        {
            foreach (bool chopping in new[] { false, true })
            {
                Point point = new(25, 89);
                var (mine, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, point);
                WorkPolicies.Chopping = WorkPolicy.Opportunistic;
                live::AICompanion.Companion.Brain.Behaviours.CompanionAction action = mine;
                Tile tile = Main.tile[point.X, point.Y];
                if (chopping)
                {
                    tile.TileType = TileID.Trees;
                    Main.tileAxe[TileID.Trees] = true;
                    Main.tileSolid[TileID.Trees] = false;
                    TileID.Sets.IsATreeTrunk[TileID.Trees] = true;
                    action = new live::AICompanion.Companion.Brain.PurposeFamilies.Gathering.ChopTree();
                }
                Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) > 0,
                    "replacement fixture needs a real prepared tool target");
                object? originalIdentity = action.ActivityIdentity;
                tile.TileType = chopping ? TileID.Cactus : TileID.Tin;
                Main.tileAxe[TileID.Cactus] = true;
                Main.tileSolid[TileID.Tin] = true;
                var request = action.Execute(ctx);
                Require(request == live::AICompanion.Companion.Brain.PositionSelection.PositionRequest.Hold
                    && !action.HandsBusy && ctx.Companion.Miner.LastOutcome == null && ctx.Companion.Chopper.LastOutcome == null,
                    $"a prepared tool must not act on replacement material: chopping={chopping}; hand={action.HandsBusy}");
                float renewedValue = VerifyPreparedActivities.PrepareAndScore(action, ctx);
                Require(renewedValue > 0 && action.PreparedTargetRejection.Length == 0
                    && !Equals(originalIdentity, action.ActivityIdentity),
                    $"eligible replacement material needs fresh work: chopping={chopping}; value={renewedValue}; rejection={action.PreparedTargetRejection}; old={originalIdentity}; current={action.ActivityIdentity}; mine-status={mine.Status}; remaining={mine.RemainingTiles}");
            }
        }
        finally { WorkPolicies.Chopping = original; }
    }

    private static void AxeEligibilityAloneDoesNotMakeATree()
    {
        Point point = new(25, 89);
        var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, point);
        Tile tile = Main.tile[point.X, point.Y];
        tile.TileType = TileID.WoodBlock;
        bool original = Main.tileAxe[TileID.WoodBlock];
        try
        {
            // A mod can make a material axe-eligible without classifying it as a tree.
            Main.tileAxe[TileID.WoodBlock] = true;
            Item axe = live::AICompanion.Companion.Brain.WorldInteractions.Chopping.TileChopper.AxeFor(ctx.Player);
            Require(!ctx.Companion.Chopper.Swing(point, axe) && ctx.Companion.Chopper.LastOutcome == null,
                "native axe work must reject an axe-eligible material outside the discovery tree category");
        }
        finally { Main.tileAxe[TileID.WoodBlock] = original; }
    }

    internal static (MineOre Action, ActionContext Context) SetUp(WorkPolicy policy, ushort tileType, params Point[] ore)
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

    private static void AnUnknownApproachKeepsItsOwnOreIdentity()
    {
        Point sealedOre = new(25, 59), unresolvedOre = new(50, 59);
        var (action, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, sealedOre, unresolvedOre);
        foreach (Point side in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
        {
            Tile wall = Main.tile[sealedOre.X + side.X, sealedOre.Y + side.Y];
            wall.HasTile = true;
            wall.TileType = TileID.Dirt;
        }
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        double budget = AStar.MsBudget;
        try
        {
            AStar.MsBudget = 0.0001d;
            Require(FindToolAccess.Approach(sealedOre, ctx.Npc.Bottom, out _) == Reachability.Reach.No,
                "the nearby ore must have no exposed working face, independently of the search deadline");
            Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) > 0,
                "the farther unresolved ore must remain an approach opportunity");
            Require(action.TargetTile == unresolvedOre && action.ActivityTarget == unresolvedOre.ToWorldCoordinates()
                && action.ActivityIdentity != null && action.ForecastTicks() > 0,
                "the prepared offer must expose the same unresolved target and a nonzero travel estimate");
            var request = action.Execute(ctx);
            Require(request.Anchor == unresolvedOre.ToWorldCoordinates(),
                $"unresolved approach must retain the ore its evidence describes; expected {unresolvedOre}, got {request.Anchor}");
        }
        finally { AStar.MsBudget = budget; }
    }

    private static (MineOre Action, ActionContext Context) SetUp(WorkPolicy policy, ushort tileType, Point ore, Point? playerHit)
        => SetUp(policy, tileType, new[] { ore }, playerHit);

    private static (MineOre Action, ActionContext Context) SetUp(WorkPolicy policy, ushort tileType, Point[] ore, Point? playerHit)
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
        int floorRow = ore.Min(tile => tile.Y) + 1;
        player.position = new Vector2(20 * 16, floorRow * 16 - player.height);
        companion.NPC.position = new Vector2(20 * 16, floorRow * 16 - companion.NPC.height);
        Player.tileRangeX = Player.tileRangeY = 5;
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile floor = Main.tile[x, floorRow];
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
        return (new MineOre(), new ActionContext(companion, companion.Brain.Senses));
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
