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
using TerrainChanges = live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges;
using OfferEligibility = live::AICompanion.Companion.Brain.Behaviours.OfferEligibility;
using AttemptStatus = live::AICompanion.Companion.Brain.Behaviours.AttemptStatus;
using TileMiner = live::AICompanion.Companion.Brain.WorldInteractions.Mining.TileMiner;

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
            AnAttemptConcludesOnlyFromItsOwnEvidence();
            TheNearestFirstApproachMatchesTheExhaustiveScan();
            ABlockedNearestOreYieldsToAnExposedFartherOre();
            AMaximumReachPoseMinesWithoutClosingIn();
            EveryArrivalOffsetEndsInUsableWork();
            CeilingOreIsMinedFromAProvenHop();
            ToolPowerChangesDuringAJobChangeEligibility();
            PlayerTerrainEditsCloseAndReopenAccess();
            AJointlyClearedVeinIsASharedCompletion();
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
            Console.WriteLine("ore work: policy, retained vein, tool gates, unproven approach, blocked nearest ore, reach edge, arrival offsets, ceiling hops, tool power changes, player terrain edits and native productive break pass");
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
        Require(action.Eligibility == live::AICompanion.Companion.Brain.Behaviours.OfferEligibility.PolicyForbidden && action.EligibilityReason == "mining-disabled",
            $"a disabled policy must be classified as a policy prohibition, not as absent ore; got {action.Eligibility}/{action.EligibilityReason}");
        WorkPolicies.Mining = WorkPolicy.Opportunistic;
        Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) > 0f
            && action.Eligibility == live::AICompanion.Companion.Brain.Behaviours.OfferEligibility.Usable,
            $"the same exposed ore with mining enabled must be a usable offer; got {action.Eligibility}/{action.EligibilityReason}");
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
        Require(ctx.Companion.Brain.Chooser.Activity.LastAttempt is { Status: live::AICompanion.Companion.Brain.Behaviours.AttemptStatus.Interrupted, Activity: "mine", ProductiveEffects: >= 1 }
            && !ctx.Companion.Brain.Chooser.Activity.AttemptOpen,
            $"interrupted productive mining must close as an interruption carrying its credited effect, never a failure; attempt={ctx.Companion.Brain.Chooser.Activity.LastAttempt}");
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
                var admission = live::AICompanion.Companion.Brain.BehaviourSelection.ValidatePreparedActivity.Capture(action);
                if (chopping) WorkPolicies.Chopping = WorkPolicy.Disabled;
                else WorkPolicies.Mining = WorkPolicy.Disabled;
                Require(admission.Rejection(action) == "work-disabled",
                    $"revoked work must fail activation admission: chopping={chopping}; inPosition={inPosition}; rejection={admission.Rejection(action)}");
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
        Require(mine.ConcludeAttempt(0).Status == live::AICompanion.Companion.Brain.Behaviours.AttemptStatus.Invalid,
            $"material that stopped qualifying makes the attempt invalid rather than completed or failed; got {mine.ConcludeAttempt(0)}");
    }

    /// <summary>
    /// A conclusion reads only evidence produced after its attempt opened. The first job ends during
    /// a preparation and the attempt that owned it concludes from that end; a new attempt then opens
    /// and discovers the next job, and must not inherit the first job's clear. Every call here runs
    /// on one engine tick, which is exactly where a tick comparison could not tell the two apart.
    /// </summary>
    private static void AnAttemptConcludesOnlyFromItsOwnEvidence()
    {
        Point first = new(25, 89), second = new(40, 89);
        var (mine, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, first);
        Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) > 0, "the evidence fixture needs a first job");
        int firstJob = mine.JobId;
        mine.BeginAttempt();
        Tile removed = Main.tile[first.X, first.Y];
        removed.HasTile = false;
        VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        Require(mine.LastConclusion is { ObservedClear: true } end && end.JobId == firstJob, "the first job must end observed clear");
        Require(mine.ConcludeAttempt(0).Status == live::AICompanion.Companion.Brain.Behaviours.AttemptStatus.Invalid,
            "the attempt that owned the externally cleared job must conclude invalid");
        mine.BeginAttempt();
        Tile ore = Main.tile[second.X, second.Y];
        ore.ClearEverything();
        ore.HasTile = true;
        ore.TileType = TileID.Copper;
        for (int i = 0; i < 61 && mine.JobId <= firstJob; i++) VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        Require(mine.JobId > firstJob, "the fixture must discover the second job before concluding");
        Require(mine.ConcludeAttempt(0).Status == live::AICompanion.Companion.Brain.Behaviours.AttemptStatus.Attempted,
            $"a new attempt must not conclude from the previous job's end; got {mine.ConcludeAttempt(0)}");
    }

    /// <summary>The companion removes one tile of a two-tile vein with its own native strikes and the
    /// player removes the other: the vein is observed clear and the attempt complete, but shared.</summary>
    private static void AJointlyClearedVeinIsASharedCompletion()
    {
        Point left = new(24, 89), right = new(25, 89);
        var (mine, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, left, right);
        Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) > 0, "the joint fixture needs a two-tile vein");
        mine.BeginAttempt();
        int effects = 0;
        for (int tick = 0; tick < 600 && Main.tile[left.X, left.Y].HasTile && Main.tile[right.X, right.Y].HasTile; tick++)
        {
            long before = ctx.Companion.Miner.LastOutcome?.Attempt ?? -1;
            mine.Execute(ctx);
            if (ctx.Companion.Miner.LastOutcome is { Productive: true } outcome && outcome.Attempt != before) effects++;
            ctx.Companion.Miner.Tick();
        }
        Point other = Main.tile[left.X, left.Y].HasTile ? left : right;
        Require(!Main.tile[(other == left ? right : left).X, (other == left ? right : left).Y].HasTile && effects > 0,
            "the companion must remove one tile with its own native strikes");
        Tile taken = Main.tile[other.X, other.Y];
        taken.HasTile = false;
        VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        Require(mine.LastConclusion is { ObservedClear: true, Tracked: 2, CompanionRemovals: 1 },
            $"the joint vein must end clear with one companion removal; got {mine.LastConclusion}");
        Require(mine.ConcludeAttempt(effects) is { Status: live::AICompanion.Companion.Brain.Behaviours.AttemptStatus.Complete,
                Attribution: live::AICompanion.Companion.Brain.Behaviours.AttemptAttribution.Shared },
            $"a vein finished together must be a shared completion, not the companion's own; got {mine.ConcludeAttempt(effects)}");
    }

    /// <summary>
    /// The nearest-first approach must return exactly what the exhaustive scan returned: the same
    /// verdict and the same stand, ties included. The reference below is the previous algorithm
    /// verbatim, run on the same native terrain after the production call so both see warm caches.
    /// Geometry varies the ore's height, the body's side and distance, and a wall that seals poses.
    /// </summary>
    private static void TheNearestFirstApproachMatchesTheExhaustiveScan()
    {
        var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, new Point(25, 89));
        for (int y = 80; y < 90; y++) { Tile wall = Main.tile[45, y]; wall.HasTile = true; wall.TileType = TileID.Dirt; }
        var ores = new[] { new Point(25, 89), new Point(30, 86), new Point(38, 84), new Point(47, 89), new Point(60, 88) };
        var feet = new[] { 12f, 20f, 33f, 52f, 70f };
        // All terrain is final before the first query, so both searches read one world and one cache.
        foreach (Point ore in ores)
        {
            Tile tile = Main.tile[ore.X, ore.Y];
            tile.HasTile = true;
            tile.TileType = TileID.Copper;
        }
        AStar.InvalidateEdges();
        int compared = 0, reachable = 0;
        foreach (Point ore in ores)
        foreach (float x in feet)
        {
            Vector2 from = new(x * 16 + 8, 90 * 16);
            var actual = FindToolAccess.Approach(ore, from, out Vector2 actualStand);
            var expected = ExhaustiveApproach(ore, from, out Vector2 expectedStand);
            Require(actual == expected && actualStand == expectedStand,
                $"nearest-first approach diverged from the exhaustive scan: ore={ore} from={from} actual={actual}@{actualStand} expected={expected}@{expectedStand}");
            compared++;
            if (actual == Reachability.Reach.Yes) reachable++;
        }
        Require(compared == ores.Length * feet.Length && reachable > 0 && reachable < compared,
            $"the comparison must include reachable and unreachable approaches to mean anything; reachable={reachable}/{compared}");
    }

    private static Reachability.Reach ExhaustiveApproach(Point tile, Vector2 fromFeet, out Vector2 stand)
    {
        if (FindToolAccess.InReach(fromFeet, tile)) { stand = fromFeet; return Reachability.Reach.Yes; }
        Vector2 eye = new(0f, -30f), tileCentre = tile.ToWorldCoordinates(8f, 8f);
        Point from = live::AICompanion.Companion.Brain.SharedMovementSystem.MovementQueries.FeetTile(fromFeet);
        Vector2? best = null;
        bool unknown = false;
        float bestDist = float.MaxValue;
        for (int dx = -Player.tileRangeX; dx <= Player.tileRangeX; dx++)
            for (int dy = -Player.tileRangeY; dy <= Player.tileRangeY + 2; dy++)
            {
                int x = tile.X + dx, y = tile.Y + dy;
                if (!live::AICompanion.Companion.Brain.SharedMovementSystem.MovementQueries.IsStandable(x, y)) continue;
                Vector2 feet = live::AICompanion.Companion.Brain.SharedMovementSystem.MovementQueries.FeetWorld(new Point(x, y));
                if (!FindToolAccess.InReach(feet, tile) || !FindToolAccess.InReach(feet + new Vector2(-8, 0), tile) || !FindToolAccess.InReach(feet + new Vector2(8, 0), tile)) continue;
                float d = Vector2.DistanceSquared(feet + eye, tileCentre);
                var reach = live::AICompanion.Companion.Brain.SharedMovementSystem.MovementQueries.WalkerReach(from, new Point(x, y));
                if (reach == Reachability.Reach.Unknown) unknown = true;
                if (d < bestDist && reach == Reachability.Reach.Yes) { bestDist = d; best = feet; }
            }
        stand = best ?? default;
        return best != null ? Reachability.Reach.Yes : unknown ? Reachability.Reach.Unknown : Reachability.Reach.No;
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
            // Direct Execute calls here bypass the activity owner, so the credited effect count is
            // supplied: the question is whether the same cleared vein reads differently with and
            // without the attempt's own productive effects.
            var attempt = mine.ConcludeAttempt(ownRemoval ? 3 : 0);
            Require(ownRemoval
                    ? attempt is { Status: live::AICompanion.Companion.Brain.Behaviours.AttemptStatus.Complete,
                        Attribution: live::AICompanion.Companion.Brain.Behaviours.AttemptAttribution.Companion }
                    : attempt.Status == live::AICompanion.Companion.Brain.Behaviours.AttemptStatus.Invalid,
                $"external ore removal must change remaining work without earning a completed attempt, and the companion's own clearance is its own: own={ownRemoval}; attempt={attempt}");
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

    /// <summary>
    /// The nearest ore is unusable in two different ways — sealed on every face, or exposed only inside a
    /// chamber the walker cannot enter — while a farther ore on the open floor is usable. Discovery must
    /// choose the farther ore, the whole brain must break it, and neither nearer ore nor its enclosure may
    /// be dug out to make access.
    /// </summary>
    private static void ABlockedNearestOreYieldsToAnExposedFartherOre()
    {
        foreach (bool pocket in new[] { false, true })
        {
            Point usable = new(27, 59);
            Point blocked = pocket ? new Point(16, 59) : new Point(17, 58);
            var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, usable);
            var walls = new List<Point>();
            if (pocket)
            {
                // A chamber two tiles wide and three tall at columns 14–15. The ore is the foot of its right
                // wall, a second wall column seals it from outside, and it faces only the chamber.
                for (int y = 56; y <= 59; y++) { walls.Add(new Point(13, y)); walls.Add(new Point(17, y)); }
                for (int y = 56; y <= 58; y++) walls.Add(new Point(16, y));
                walls.Add(new Point(14, 56));
                walls.Add(new Point(15, 56));
            }
            else
                walls.AddRange(new[] { new Point(16, 58), new Point(18, 58), new Point(17, 57), new Point(17, 59) });
            foreach (Point wall in walls) Place(wall, TileID.Dirt);
            Place(blocked, TileID.Copper);
            TerrainChanges.Reset();
            string shape = pocket ? "chambered" : "sealed";
            Require(Vector2.DistanceSquared(ctx.Npc.Bottom, blocked.ToWorldCoordinates()) < Vector2.DistanceSquared(ctx.Npc.Bottom, usable.ToWorldCoordinates()),
                $"the {shape} ore must be the nearer one, or the fixture tests nothing");
            var standing = FindToolAccess.Approach(blocked, ctx.Npc.Bottom, out _);
            var hop = FindToolAccess.HopApproach(blocked, ctx.Npc.Bottom, ctx.Companion.Motor.State, out _);
            Require(standing != Reachability.Reach.Yes && hop != Reachability.Reach.Yes,
                $"the {shape} ore must have no usable approach, standing or hopping; got {standing}/{hop}");
            var mine = ctx.Companion.Brain.Chooser.Actions.OfType<MineOre>().Single();
            float value = VerifyPreparedActivities.PrepareAndScore(mine, ctx);
            Require(value > 0f && mine.TargetTile == usable && mine.Eligibility == OfferEligibility.Usable,
                $"a {shape} nearer ore must not mask the exposed farther one; value={value} target={mine.TargetTile} offer={mine.Eligibility}/{mine.EligibilityReason}");
            var run = RunBrainUntilBroken(ctx, usable, 900);
            Require(run.Broken, $"the whole brain must break the exposed ore beside a {shape} one; feet={ctx.Npc.Bottom} status={mine.Status} action={ctx.Companion.Brain.LastAction?.Name}");
            Require(Main.tile[blocked.X, blocked.Y].HasTile && walls.All(wall => Main.tile[wall.X, wall.Y].HasTile),
                $"the {shape} ore and its enclosure must be left intact; mining never excavates to make access");
        }
    }

    /// <summary>
    /// A pose exactly on the edge of tool reach mines from where it stands. Approach reserves a margin for a
    /// future arrival; that margin must not shrink reach at a pose the body already occupies, or the
    /// companion walks in to a pose it never needed.
    /// </summary>
    private static void AMaximumReachPoseMinesWithoutClosingIn()
    {
        Point ore = new(25, 59);
        var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        float edge = ore.ToWorldCoordinates(8f, 8f).X - (Player.tileRangeX * 16f + 8f);
        ctx.Npc.Bottom = new Vector2(edge, ctx.Npc.Bottom.Y);
        Require(FindToolAccess.InReach(ctx.Npc.Bottom, ore) && !FindToolAccess.InReach(ctx.Npc.Bottom - new Vector2(1f, 0f), ore),
            "the fixture pose must sit exactly on the edge of actual tool reach");
        Vector2 start = ctx.Npc.Bottom;
        TerrainChanges.Reset();
        var run = RunBrainUntilBroken(ctx, ore, 600);
        Require(run.Broken && run.StrikeFeet.Count > 0, $"a maximum-reach pose must produce a native break; feet={ctx.Npc.Bottom}");
        float drift = run.StrikeFeet.Max(feet => MathF.Abs(feet.X - start.X));
        Require(drift < 4f, $"every strike must come from the edge pose itself rather than from walking in; the largest drift was {drift:0.0} px");
    }

    /// <summary>
    /// The approach delivers the body from both sides and from different sub-tile offsets, so it arrives at
    /// different working poses. Every start must end in a native break, and every productive strike must come
    /// from a pose with actual reach rather than from wherever the navigator happened to stop.
    /// </summary>
    private static void EveryArrivalOffsetEndsInUsableWork()
    {
        Point ore = new(25, 59);
        var arrivals = new List<string>();
        foreach (float startX in new[] { 8 * 16f + 3f, 13 * 16f + 11f, 34 * 16f + 5f, 39 * 16f + 14f })
        {
            var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
            ctx.Npc.Bottom = new Vector2(startX, ctx.Npc.Bottom.Y);
            TerrainChanges.Reset();
            Require(!FindToolAccess.InReach(ctx.Npc.Bottom, ore), $"start x={startX} must begin outside reach so the approach delivers the pose");
            var run = RunBrainUntilBroken(ctx, ore, 900);
            Require(run.Broken && run.StrikeFeet.Count > 0,
                $"start x={startX}: the delivered pose must produce a native break; feet={ctx.Npc.Bottom} request={ctx.Companion.Brain.LastRequest} nav={ctx.Companion.Brain.Navigator.Status}");
            Require(run.StrikeFeet.All(feet => FindToolAccess.InReach(feet, ore)),
                $"start x={startX}: every productive strike must come from actual reach; strikes at {string.Join("; ", run.StrikeFeet)}");
            arrivals.Add($"{startX:0}->{run.StrikeFeet[0].X:0.0}");
        }
        Console.WriteLine($"ore arrival offsets (start x -> first strike x): {string.Join(", ", arrivals)}");
    }

    /// <summary>
    /// Ore set in a ceiling slab above standing reach is worked the way a player works it: walk under it,
    /// jump, swing while rising. Discovery must find it through a proven hop, the whole brain must break it
    /// and land, and the slab must stay whole. Ore beyond the reach of any hop must never be offered.
    /// </summary>
    private static void CeilingOreIsMinedFromAProvenHop()
    {
        foreach ((int oreRow, bool mirrored, bool reachable) in new[] { (52, false, true), (52, true, true), (44, false, false) })
        {
            Point placeholder = new(25, 59);
            var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, placeholder);
            Tile cleared = Main.tile[placeholder.X, placeholder.Y];
            cleared.ClearEverything();
            Point ore = new(mirrored ? 15 : 25, oreRow);
            // Two rows thick, so the ore's only open face is the one underneath it.
            var slab = new List<Point>();
            for (int x = 5; x < 40; x++)
                for (int y = oreRow - 1; y <= oreRow; y++)
                    if (new Point(x, y) != ore) slab.Add(new Point(x, y));
            foreach (Point p in slab) Place(p, TileID.Dirt);
            Place(ore, TileID.Copper);
            TerrainChanges.Reset();
            Vector2 standingFeet = ctx.Npc.Bottom;
            var standing = FindToolAccess.Approach(ore, standingFeet, out _);
            var hop = FindToolAccess.HopApproach(ore, standingFeet, ctx.Companion.Motor.State, out Vector2 takeOff);
            Require(standing != Reachability.Reach.Yes, $"ore row {oreRow} must be out of standing reach, or this is not a ceiling case; got {standing}");
            var mine = ctx.Companion.Brain.Chooser.Actions.OfType<MineOre>().Single();
            if (!reachable)
            {
                Require(hop != Reachability.Reach.Yes, $"ore row {oreRow} is beyond any hop and must not be proven; got {hop} from {takeOff}");
                for (int tick = 0; tick < 240; tick++) AdvanceBrain(ctx);
                Require(Main.tile[ore.X, ore.Y].HasTile && slab.All(p => Main.tile[p.X, p.Y].HasTile)
                    && mine.Eligibility is not (OfferEligibility.Usable or OfferEligibility.Unresolved),
                    $"ore beyond any hop must stay unoffered and untouched; offer={mine.Eligibility}/{mine.EligibilityReason}");
                continue;
            }
            Require(hop == Reachability.Reach.Yes, $"ore row {oreRow} mirrored={mirrored} must be reachable by a proven hop from a walkable take-off; got {hop}");
            var run = RunBrainUntilBroken(ctx, ore, 900);
            Require(run.Broken && run.StrikeFeet.Count > 0,
                $"ceiling ore mirrored={mirrored} must be mined from a hop; status={mine.Status} feet={ctx.Npc.Bottom} action={ctx.Companion.Brain.LastAction?.Name} request={ctx.Companion.Brain.LastRequest}");
            Require(run.StrikeFeet.All(feet => FindToolAccess.InReach(feet, ore) && feet.Y < standingFeet.Y - 1f),
                $"every strike must come from a raised body in actual reach, which only the hop provides; strikes at {string.Join("; ", run.StrikeFeet)}");
            for (int tick = 0; tick < 120 && !ctx.Companion.Motor.State.OnGround; tick++) AdvanceBrain(ctx);
            Require(ctx.Companion.Motor.State.OnGround && slab.All(p => Main.tile[p.X, p.Y].HasTile),
                "the hop must land and the ceiling slab must stay whole");
        }
    }

    /// <summary>
    /// Tool power moves in both directions around one job. A pick too weak for the only ore offers no work
    /// and says the tool is why; equipping a stronger pick starts the job on the next preparation; swapping
    /// back after real damage ends it as partial work with the ore still in place.
    /// </summary>
    private static void ToolPowerChangesDuringAJobChangeEligibility()
    {
        Point ore = new(25, 59);
        var (mine, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Chlorophyte, ore);
        var strong = new Item();
        strong.SetDefaults(ItemID.Picksaw);
        int weakPower = TileMiner.PickaxeFor(ctx.Player).pick;
        Require(!ctx.Companion.Miner.CanMine(ore, weakPower) && ctx.Companion.Miner.CanMine(ore, strong.pick),
            $"the fixture needs ore the fallback pick ({weakPower}) cannot damage and the stronger pick ({strong.pick}) can");
        Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) == 0f && mine.RemainingTiles == 0
            && mine.Eligibility == OfferEligibility.KnownUnusable,
            $"a pick too weak for the only ore must offer no work and say the tool is why; got {mine.Eligibility}/{mine.EligibilityReason}");

        ctx.Player.inventory[ctx.Player.selectedItem] = strong;
        float stronger = VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        Require(stronger > 0f && mine.JobId > 0 && mine.Eligibility == OfferEligibility.Usable,
            $"a stronger pick must start the job on the next preparation, not after a search cadence; value={stronger} job={mine.JobId} offer={mine.Eligibility}/{mine.EligibilityReason}");

        mine.BeginAttempt();
        int effects = 0;
        for (int tick = 0; tick < 120 && effects == 0; tick++)
        {
            long before = ctx.Companion.Miner.LastOutcome?.Attempt ?? -1;
            mine.Execute(ctx);
            if (ctx.Companion.Miner.LastOutcome is { Productive: true } outcome && outcome.Attempt != before) effects++;
            ctx.Companion.Miner.Tick();
        }
        Require(effects > 0 && Main.tile[ore.X, ore.Y].HasTile, "the stronger pick must land real partial damage before the swap");

        ctx.Player.inventory[ctx.Player.selectedItem] = new Item();
        float weaker = VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        Require(weaker == 0f && mine.Eligibility == OfferEligibility.KnownUnusable && Main.tile[ore.X, ore.Y].HasTile,
            $"a weaker pick must end the offer as a tool that cannot mine, with the ore still in place; value={weaker} offer={mine.Eligibility}/{mine.EligibilityReason}");
        var conclusion = mine.ConcludeAttempt(effects);
        Require(conclusion.Status == AttemptStatus.Partial,
            $"real damage followed by a lost tool is partial work, never complete or failed; got {conclusion}");
    }

    /// <summary>
    /// The player walls off the ore while the companion walks to it, then opens one face again. Sealing must
    /// end the approach as a failed method with no strike and no digging, and the offer must stop claiming
    /// usable or undecided work; reopening must let the same ore be mined through the new face with the rest
    /// of the wall untouched. The edits reach the companion through the same invalidation call the game's
    /// placement and kill hooks make.
    /// </summary>
    private static void PlayerTerrainEditsCloseAndReopenAccess()
    {
        // The premise is a proven job the player then seals. Under production millisecond allowances a
        // cold first search can stop at its deadline and leave the approach undecided, which is correct
        // live behaviour and not this fixture's question, so the allowances are lifted as brain-cost does.
        live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = true;
        try { SealThenReopenAnApproachedOre(); }
        finally { live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = false; }
    }

    private static void SealThenReopenAnApproachedOre()
    {
        Point ore = new(25, 59);
        var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        ctx.Npc.Bottom = new Vector2(15 * 16f + 8f, ctx.Npc.Bottom.Y);
        TerrainChanges.Reset();
        var mine = ctx.Companion.Brain.Chooser.Actions.OfType<MineOre>().Single();
        AdvanceBrain(ctx);
        Require(mine.JobId > 0 && ctx.Companion.Brain.LastAction?.Name == "mine" && !FindToolAccess.InReach(ctx.Npc.Bottom, ore),
            $"the fixture must catch the companion walking to a proven job; job={mine.JobId} action={ctx.Companion.Brain.LastAction?.Name} status={mine.Status} feet={ctx.Npc.Bottom}");
        var seal = new[] { new Point(24, 59), new Point(26, 59), new Point(25, 58) };
        foreach (Point p in seal) { Place(p, TileID.Dirt); TerrainChanges.Changed(p.X, p.Y); }
        long strikesBefore = ctx.Companion.Miner.LastOutcome?.Attempt ?? -1;
        for (int tick = 0; tick < 240; tick++) AdvanceBrain(ctx);
        Require(Main.tile[ore.X, ore.Y].HasTile && seal.All(p => Main.tile[p.X, p.Y].HasTile)
            && (ctx.Companion.Miner.LastOutcome?.Attempt ?? -1) == strikesBefore,
            "a sealed ore must receive no strike and its seal no digging");
        Require(mine.Score() == 0f && mine.Eligibility is not (OfferEligibility.Usable or OfferEligibility.Unresolved),
            $"a sealed ore must stop being offered as usable or undecided work; offer={mine.Eligibility}/{mine.EligibilityReason}");
        var sealedAttempt = ctx.Companion.Brain.Chooser.Activity.RecentAttempts.LastOrDefault(attempt => attempt.Activity == "mine");
        Require(sealedAttempt is { Status: AttemptStatus.Failed, Cause: "remaining ore has no proven working pose", ProductiveEffects: 0 },
            $"the approach lost to the player's wall must close as a failed method with no credited effect; got {sealedAttempt}");

        Point opened = seal[0];
        Tile gap = Main.tile[opened.X, opened.Y];
        gap.ClearEverything();
        TerrainChanges.Changed(opened.X, opened.Y);
        // The reopened face is a one-tile notch with the player's block diagonally above it: the swing
        // reaches in, and the game's wide beam test would have refused it.
        Vector2 besideFeet = live::AICompanion.Companion.Brain.SharedMovementSystem.MovementQueries.FeetWorld(new Point(23, 59));
        Require(FindToolAccess.InReach(besideFeet, ore)
            && !Collision.CanHitLine(besideFeet + new Vector2(0f, -30f), 1, 1, opened.ToWorldCoordinates(8f, 8f), 1, 1),
            "the notch fixture must be one the wide native beam refuses and a swing reaches, or it does not test the face-access walk");
        var run = RunBrainUntilBroken(ctx, ore, 900);
        var approachNow = FindToolAccess.Approach(ore, ctx.Npc.Bottom, out Vector2 standNow);
        bool standableBeside = live::AICompanion.Companion.Brain.SharedMovementSystem.MovementQueries.IsStandable(23, 59);
        Require(run.Broken, $"reopening one face must let the same ore be mined; feet={ctx.Npc.Bottom} status={mine.Status} "
            + $"offer={mine.Eligibility}/{mine.EligibilityReason} action={ctx.Companion.Brain.LastAction?.Name} approach-now={approachNow}@{standNow} "
            + $"in-reach-now={FindToolAccess.InReach(ctx.Npc.Bottom, ore)} mineable={ctx.Companion.Miner.CanMine(ore, TileMiner.PickaxeFor(ctx.Player).pick)} "
            + $"standable(23,59)={standableBeside} families={string.Join(";", ctx.Companion.Brain.Chooser.Queries.LastFamilies)}");
        Require(seal.Skip(1).All(p => Main.tile[p.X, p.Y].HasTile), "the rest of the player's wall must stay as the player built it");
    }

    /// <summary>Runs the whole brain against native collision until the ore breaks or the tick limit passes,
    /// returning the feet at every productive strike so a fixture can check the pose each strike came from.</summary>
    internal static (bool Broken, List<Vector2> StrikeFeet) RunBrainUntilBroken(ActionContext ctx, Point ore, int ticks)
    {
        var strikes = new List<Vector2>();
        long last = ctx.Companion.Miner.LastOutcome?.Attempt ?? -1;
        for (int tick = 0; tick < ticks && Main.tile[ore.X, ore.Y].HasTile; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
            // The swing happens inside the brain tick, before the engine moves the body, so these are the
            // feet the strike was actually taken from.
            if (ctx.Companion.Miner.LastOutcome is { Productive: true } outcome && outcome.Attempt != last)
            {
                strikes.Add(ctx.Npc.Bottom);
                last = outcome.Attempt;
            }
            VerifyResponsiveFollowing.AdvanceNative(ctx.Companion);
        }
        return (!Main.tile[ore.X, ore.Y].HasTile, strikes);
    }

    internal static void AdvanceBrain(ActionContext ctx)
    {
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
        VerifyResponsiveFollowing.AdvanceNative(ctx.Companion);
    }

    internal static void Place(Point tile, ushort type)
    {
        Tile placed = Main.tile[tile.X, tile.Y];
        placed.ClearEverything();
        placed.HasTile = true;
        placed.TileType = type;
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
        bool oreCentre = Collision.CanHit(eye, 1, 1, ore.ToWorldCoordinates(), 1, 1);
        bool exposedFace = Collision.CanHit(eye, 1, 1, new Point(ore.X - 1, ore.Y).ToWorldCoordinates(), 1, 1);
        Require(!oreCentre && exposedFace,
            "native CanHit must reject the solid ore destination but accept its exposed adjacent face");
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
