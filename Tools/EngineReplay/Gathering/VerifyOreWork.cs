extern alias live;

using FindToolAccess = live::AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System.Reflection;
using MineOre = live::AICompanion.Companion.Brain.Activities.Gathering.MineOre;
using WorkPolicies = live::AICompanion.Companion.Brain.Activities.WorkPolicies;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using AStar = live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar;
using Reachability = live::AICompanion.Companion.Brain.Infrastructure.Movement.Reachability;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using OfferEligibility = live::AICompanion.Companion.Brain.Activities.OfferEligibility;
using AttemptStatus = live::AICompanion.Companion.Brain.Activities.AttemptStatus;
using TileMiner = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining.TileMiner;

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
            AnUnprovenApproachIsNotAPlan();
            AColdFloodDoesNotLeaveTheBrainResting();
            AnUnknownApproachDoesNotSubstituteASealedNeighbour();
            AReachableOreProducesANativeBreak();
            AUsefulCurrentPoseNeedsNoApproach();
            AProjectileInterruptsCoherentToolOwnership();
            RaisedLipsAtBothGravitiesProduceWork();
            NativeToolOutcomesDistinguishAttemptsFromProgress();
            PreparedWorkForecastRespondsToNativeProgress();
            RemainingToolWorkMatchesNativeCompletion();
            DepartingPlayerChangesWhetherWorkIsWorthFinishing();
            ReunionChargeReadsDepartureAndTheRouteHome();
            Console.WriteLine("ore work: policy, retained vein, tool gates, unproven approach is not a plan, blocked nearest ore, reach edge, arrival offsets, ceiling hops, tool power changes, player terrain edits and native productive break pass");
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
        Require(action.Eligibility == live::AICompanion.Companion.Brain.Activities.OfferEligibility.PolicyForbidden && action.EligibilityReason == "mining-disabled",
            $"a disabled policy must be classified as a policy prohibition, not as absent ore; got {action.Eligibility}/{action.EligibilityReason}");
        WorkPolicies.Mining = WorkPolicy.Opportunistic;
        Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) > 0f
            && action.Eligibility == live::AICompanion.Companion.Brain.Activities.OfferEligibility.Usable,
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
            var workClock = new live::AICompanion.Companion.Brain.Infrastructure.Observation.TileDamageClock();
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
            Item pick = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining.TileMiner.PickaxeFor(ctx.Player);
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

    /// <summary>
    /// The reunion charge on optional work reads how fast the player is leaving and how long the walk home
    /// is. Eight scenes at one separation cross a stationary against a departing player, a way up to the
    /// player's floor near them against one only at the far end behind the companion, and fresh against
    /// one-hit work. Both route scenes put the player on the same upper floor over the companion's
    /// corridor, so sight and the follow objective match and only the route home differs; a stepped mound
    /// was tried first and cannot lengthen a route here, because a walk step moves one column and at most
    /// one row, so climbing costs no more columns than the floor. A
    /// calm player is met by quick justified work; a departing player's charge grows with the route home,
    /// so the same work is worth less when the way back is awkward; a stationary player's route home is
    /// not charged, because the charge scales the route by departure and calm absence is priced by
    /// accumulated separation instead. Route evidence is primed through the positioner's own flood, which
    /// is where the chooser reads it, and the mound must actually lengthen that route or nothing is compared.
    /// </summary>
    private static void ReunionChargeReadsDepartureAndTheRouteHome()
    {
        // Stationary, a slow walk away and a quick departure. Which activity the slow departure should pick is a
        // judgement for play, so its selection is printed rather than required; its charge must still grow with
        // the route home, because any departure scales the route by the same factor.
        float[] speeds = { 0f, 1.5f, 4f };
        var seen = new Dictionary<(float Speed, bool FarRoute, bool NearlyDone), (string Selected, float Mine, float Delay, float Return, float Route)>();
        // Route floods advance under millisecond slices; every edge on the mound runs a body simulation,
        // so a wall-clock slice would decide how far the route home is priced. Lifting the allowances
        // keeps each flood's work count as the only bound.
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        try
        {
        foreach (float speed in speeds)
        foreach (bool farRoute in new[] { false, true })
        foreach (bool nearlyDone in new[] { false, true })
        {
            Point ore = new(25, 89);
            var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
            BuildPlayersUpperFloor(90, gapNearPlayer: !farRoute);
            var brain = ctx.Companion.Brain;
            var workClock = new live::AICompanion.Companion.Brain.Infrastructure.Observation.TileDamageClock();
            workClock.OnWorldLoad();
            brain.Chooser.Actions.RemoveAll(action => action.Name is not ("mine" or "keep-company"));
            const int separation = 576;
            // The player stands on the upper floor, five rows above the companion's corridor floor.
            ctx.Player.Bottom = ctx.Npc.Bottom + new Vector2(separation - 120 * speed, -5 * 16);
            for (int tick = 0; tick < 120; tick++)
            {
                ctx.Player.velocity = new Vector2(speed, 0);
                ctx.Player.position += ctx.Player.velocity;
                VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
                brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
                workClock.PostUpdateEverything();
            }
            var home = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
                live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer, ctx.Player.Bottom);
            Point from = live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.FeetTile(ctx.Npc.Bottom);
            Point to = live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.FeetTile(ctx.Player.Bottom);
            // Throw the region away and flood it again, to completion rather than to first contact: a
            // cost-ordered flood can first reach the player's tile the long way round and lower its ticks
            // later along the short route. Thrown away rather than merely driven, because the upper floor
            // this row stands the player on was built after the setup had already flooded a world without
            // it, and a loop conditioned on the region being complete does nothing at all when the stale
            // region is complete — which is the quietest way a fixture can prime nothing and look primed.
            ResettleReach(ctx);
            for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++)
                brain.Positioner.Resolve(home, brain.Senses, null);
            if (brain.Positioner.EstimatedTravelTicks(from, to) == null)
            {
                // An extern alias cannot appear inside an interpolation hole, so the diagnostics are locals.
                bool standable = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.IsStandable(to.X, to.Y);
                var walker = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.WalkerReach(from, to);
                Require(false, $"the route home must be priced before return cost can be compared; speed={speed} farRoute={farRoute} from={from} to={to} "
                    + $"standable={standable} inRegion={brain.Positioner.Reaches(to)} regionComplete={brain.Positioner.ReachComplete} walker={walker}");
            }
            Item pick = TileMiner.PickaxeFor(ctx.Player);
            if (nearlyDone)
                while (ctx.Companion.Miner.EstimateRemaining(ore, pick) is { Hits: > 1 })
                {
                    Require(ctx.Companion.Miner.Swing(ore, pick), "paired completion fixture needs a native hit");
                    for (int tick = 0; tick < pick.useTime; tick++) ctx.Companion.Miner.Tick();
                }
            var selected = brain.Chooser.Choose(ctx);
            var mine = brain.Chooser.LastScores.Single(s => s.Action.Name == "mine");
            seen[(speed, farRoute, nearlyDone)] = (selected?.Name ?? "none", mine.Final, brain.Chooser.Reunion.DelayCostPerTick, brain.Chooser.EstimatedReturnTicks,
                brain.Positioner.EstimatedTravelTicks(from, to) ?? -1f);
        }
        }
        finally { live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false; }
        string ledger = string.Join("; ", seen.Select(s =>
            $"{(s.Key.Speed == 0 ? "stationary" : $"departing {s.Key.Speed}px")}/{(s.Key.FarRoute ? "far-route" : "near-route")}/{(s.Key.NearlyDone ? "one-hit" : "fresh")}: "
            + $"{s.Value.Selected} mine={s.Value.Mine:0.000} delay={s.Value.Delay:0.00000} return={s.Value.Return:0} route={s.Value.Route:0}"));
        foreach (bool nearlyDone in new[] { false, true })
        {
            Require(seen[(0f, false, nearlyDone)].Selected == "mine",
                $"a calm player is met by quick justified work; {ledger}");
            // The chooser reads the larger of the straight-line estimate and the route, so a near route
            // shorter than the straight line shows only the straight line; the guard is that the far route
            // is strictly longer in what the chooser read, not a margin chosen before seeing that floor.
            foreach (float speed in speeds)
                Require(seen[(speed, true, nearlyDone)].Return > seen[(speed, false, nearlyDone)].Return
                    && (speed == 0f || seen[(speed, true, nearlyDone)].Route > seen[(speed, false, nearlyDone)].Route),
                    $"the far way up must lengthen the priced route home, or the pairs compare nothing; speed={speed}; {ledger}");
            foreach (float speed in speeds.Where(s => s > 0f))
                Require(seen[(speed, true, nearlyDone)].Delay > seen[(speed, false, nearlyDone)].Delay
                    && seen[(speed, true, nearlyDone)].Mine < seen[(speed, false, nearlyDone)].Mine,
                    $"a departing player's reunion charge grows with the route home; speed={speed}; {ledger}");
            Require(MathF.Abs(seen[(0f, true, nearlyDone)].Delay - seen[(0f, false, nearlyDone)].Delay) < 1e-6f,
                $"a stationary player's route home is not charged to optional work; {ledger}");
        }
        Console.WriteLine($"reunion charge matrix: {ledger}");
    }

    /// <summary>An upper floor five rows above the companion's floor, over its mining corridor. Its left
    /// end is a ledge the companion can jump onto; with <paramref name="gapNearPlayer"/> a three-tile gap
    /// near the player is a second, near way up. Without it the walk home runs back to the ledge and all
    /// the way along the upper floor, which a straight-line estimate cannot see. The corridor is four rows
    /// tall because the ore sits on its floor: under a three-row ceiling, stepping onto the ore put the
    /// body's head into the upper floor, the corridor was closed at the ore, and every route home left
    /// through the ledge whether the gap existed or not.</summary>
    private static void BuildPlayersUpperFloor(int floorRow, bool gapNearPlayer)
    {
        int row = floorRow - 5;
        for (int x = 10; x <= 94; x++)
        {
            if (gapNearPlayer && x >= 58 && x <= 60) continue;
            Tile tile = Main.tile[x, row];
            tile.ClearEverything();
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
        }
        TerrainChanges.Reset();
    }

    private static void PreparedWorkForecastRespondsToNativeProgress()
    {
        Point ore = new(25, 89);
        var (mine, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        float untouched = mine.ForecastTicks();
        Item pick = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining.TileMiner.PickaxeFor(ctx.Player);
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
        Require(live::AICompanion.Companion.Brain.Infrastructure.Interactions.RemainingToolWork.Estimate(100, 35, 0, 10, "fixture")
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
                Item pick = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining.TileMiner.PickaxeFor(ctx.Player);
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
                live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining.TileMiner.PickaxeFor(weak.Player)) == null,
                "an incapable pick must not claim a finite completion estimate");
            var (_, tree) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, hardOre);
            Main.tile[hardOre.X, hardOre.Y].TileType = TileID.Trees;
            Main.tileAxe[TileID.Trees] = true;
            Main.tileSolid[TileID.Trees] = false;
            var chopper = tree.Companion.Chopper;
            Item axe = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Chopping.TileChopper.AxeFor(tree.Player);
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
        Item pick = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining.TileMiner.PickaxeFor(ctx.Player);
        int swings = 0;
        for (int tick = 0; tick < 600 && Main.tile[ore.X, ore.Y].HasTile; tick++)
        {
            ctx.Companion.Miner.Tick();
            if (ctx.Companion.Miner.Swing(ore, pick)) swings++;
        }
        Require(!Main.tile[ore.X, ore.Y].HasTile,
            $"a usable fixed pose must produce a real native tile break, not merely report {swings} swings");
        Require(ctx.Companion.Miner.LastOutcome is { Effect: live::AICompanion.Companion.Brain.Infrastructure.Interactions.TileToolEffect.Removed, Productive: true },
            "native tile removal must remain distinguishable from a requested swing or partial damage");
    }

    private static void AUsefulCurrentPoseNeedsNoApproach()
    {
        Point ore = new(25, 59);
        var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        Vector2 feet = ctx.Npc.Bottom;
        Require(FindToolAccess.InReach(feet, ore),
            "the current-pose fixture must already satisfy actual tool range and exposed access");
        var reach = FindToolAccess.Approach(ore, feet, ctx.Companion.Brain.Senses.Reach, out Vector2 stand);
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
            && ctx.Companion.Brain.ControlGrants.Last?.Hand == live::AICompanion.Companion.Brain.Infrastructure.Grants.HandGrant.WorkTool,
            "actual native mining must reserve the tool hand");
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
        Require(ctx.Companion.Miner.LastOutcome == effect && mine.HandsBusy
            && ctx.Companion.Brain.ControlGrants.Last?.Hand == live::AICompanion.Companion.Brain.Infrastructure.Grants.HandGrant.WorkTool,
            "a cooldown gap must retain tool ownership without fabricating another swing");

        Main.projectile[0] = new Projectile { whoAmI = 0, active = true, hostile = true, damage = 10,
            width = 8, height = 8, position = ctx.Npc.Center + new Vector2(28, -4), velocity = new Vector2(-8, 0), timeLeft = 100 };
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
        Require(ctx.Companion.Brain.Senses.Threats.Threats.Count == 0
            && ctx.Companion.Brain.Reflexes.Active == "avoid-collision"
            && ctx.Companion.Brain.ControlGrants.Last is { AppliedOwner: "combat-reflex", Hand: live::AICompanion.Companion.Brain.Infrastructure.Grants.HandGrant.Available }
            && ctx.Companion.Brain.Chooser.Activity.Phase == live::AICompanion.Companion.Brain.Infrastructure.Selection.ActivityPhase.Suspended
            && !mine.HandsBusy && ctx.Companion.Miner.LastOutcome == effect,
            $"a projectile without an enemy must suspend native work and grant avoidance with a free hand; enemies={ctx.Companion.Brain.Senses.Threats.Threats.Count}; projectiles={ctx.Companion.Brain.Senses.Projectiles.Threats.Count}; reflex={ctx.Companion.Brain.Reflexes.Active}; grant={ctx.Companion.Brain.ControlGrants.Last}; phase={ctx.Companion.Brain.Chooser.Activity.Phase}; busy={mine.HandsBusy}; same-effect={ctx.Companion.Miner.LastOutcome == effect}");
        Require(ctx.Companion.Brain.Chooser.Activity.LastAttempt is { Status: live::AICompanion.Companion.Brain.Activities.AttemptStatus.Interrupted, Activity: "mine", ProductiveEffects: >= 1 }
            && !ctx.Companion.Brain.Chooser.Activity.AttemptOpen,
            $"interrupted productive mining must close as an interruption carrying its credited effect, never a failure; attempt={ctx.Companion.Brain.Chooser.Activity.LastAttempt}");
        Main.projectile[0].active = false;
    }

    private static void NativeToolOutcomesDistinguishAttemptsFromProgress()
    {
        Point ore = new(25, 59);
        var (_, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        Item pick = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining.TileMiner.PickaxeFor(ctx.Player);
        Require(ctx.Companion.Miner.Swing(ore, pick), "the native partial-damage fixture must actually swing");
        var partial = ctx.Companion.Miner.LastOutcome;
        Require(partial is { Effect: live::AICompanion.Companion.Brain.Infrastructure.Interactions.TileToolEffect.Damaged, Productive: true }
            && partial.Value.After.Damage > partial.Value.Before.Damage && Main.tile[ore.X, ore.Y].HasTile,
            "partial native damage must be observed without claiming that ore was removed");
        Require(!ctx.Companion.Miner.Swing(ore, pick) && ctx.Companion.Miner.LastOutcome == partial,
            "cooldown must not fabricate a new native attempt");

        var (_, weak) = SetUp(WorkPolicy.Opportunistic, TileID.Chlorophyte, ore);
        Require(weak.Companion.Miner.Swing(ore, pick)
            && weak.Companion.Miner.LastOutcome is { Effect: live::AICompanion.Companion.Brain.Infrastructure.Interactions.TileToolEffect.NoObservedChange, Productive: false }
            && Main.tile[ore.X, ore.Y].HasTile,
            "a native weak-pick call must remain an attempt without productive damage");

        var (_, tree) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        Tile trunk = Main.tile[ore.X, ore.Y];
        trunk.TileType = TileID.Trees;
        Main.tileAxe[TileID.Trees] = true;
        Main.tileSolid[TileID.Trees] = false;
        Item axe = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Chopping.TileChopper.AxeFor(tree.Player);
        Require(tree.Companion.Chopper.Swing(ore, axe)
            && tree.Companion.Chopper.LastOutcome is { Effect: live::AICompanion.Companion.Brain.Infrastructure.Interactions.TileToolEffect.Damaged, Productive: true },
            "axe progress must use its own native hit table");
        trunk.ClearEverything();
        var idleAxe = new live::AICompanion.Companion.Brain.Infrastructure.Interactions.Chopping.TileChopper();
        Require(!idleAxe.Swing(ore, axe) && idleAxe.LastOutcome == null,
            "a vanished trunk must not become a successful axe attempt");
    }

    private enum BaselineMode { FullBrain, HeldActivity, FixedWorkingPose }

    /// <summary>
    /// A two-tile lip beside the ore used to be mined by walking at an unproven approach until a
    /// stand appeared. That walk is no longer a plan. If mining never starts, the new contract
    /// holds. If it does start, the ore must break without excavating the lip.
    /// </summary>
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
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
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
            live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        }
        int miningTicks = 0;
        for (int tick = 0; tick < 600 && Main.tile[ore.X, ore.Y].HasTile; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            if (mode == BaselineMode.FixedWorkingPose)
            {
                ctx.Companion.Miner.Tick();
                ctx.Companion.Miner.Swing(ore,
                    live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining.TileMiner.PickaxeFor(ctx.Player));
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
            bool jumpProven = live::AICompanion.Companion.Brain.Infrastructure.Movement.ProveInteractionJump.CanReach(
                live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World, initialBody,
                body => FindToolAccess.InReach(body.Feet, ore));
            Console.WriteLine($"lip initial-pose ground-jump proof={jumpProven}");
            AStar.MsBudget = 0;
            var approach = FindToolAccess.Approach(ore, ctx.Npc.Bottom, ctx.Companion.Brain.Senses.Reach, out Vector2 stand);
            // The wall-time limit is lifted here for the rest of the block; the approach itself no longer reads
            // one, because it ranks poses by geometry and answers reach from the flood rather than by searching.
            Console.WriteLine($"lip approach={approach} stand={stand}; answered from the reach flood, not a bounded search");
            for (int x = 22; x <= 28; x++)
                for (int y = floor - 4; y < floor; y++)
                {
                    if (!live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.IsStandable(x, y)) continue;
                    Vector2 feet = live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.FeetWorld(new Point(x, y));
                    bool useful = FindToolAccess.InReach(feet, ore);
                    if (useful) Console.WriteLine($"lip usable={feet} route={Reachability.WalkerReach(live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.FeetTile(ctx.Npc.Bottom), new Point(x, y))}");
                }
        }
        Console.WriteLine($"raised-lip native mining broken={broken} (floor={floor}, mirrored={mirrored}, mode={mode}, miningTicks={miningTicks}, "
            + $"feet={ctx.Npc.Bottom}, action={ctx.Companion.Brain.LastAction?.Name}, "
            + $"request={ctx.Companion.Brain.LastRequest}, navigator={ctx.Companion.Brain.Navigator.Status})");
        Require(Main.tile[lipX, floor - 2].HasTile && Main.tile[lipX, floor - 1].HasTile,
            "mining must overcome the lip through useful positioning, without excavating ordinary terrain");
        if (miningTicks == 0)
            return true;
        return broken;
    }

    private static void DescribeRaisedLipRoutes(in ActionContext ctx, Point ore)
    {
        var start = live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.FeetTile(ctx.Npc.Bottom);
        for (int x = ore.X - 3; x <= ore.X + 3; x++)
            for (int y = ore.Y - 3; y <= ore.Y; y++)
            {
                var pose = live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.StandAt(x, y, false);
                if (pose == null) continue;
                var tile = new Point(x, y);
                Vector2 feet = live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.FeetWorld(tile);
                if (!FindToolAccess.InReach(feet, ore)) continue;
                var route = AStar.Find(start, tile, Reachability.WalkerBudget, out int used, out var stop);
                Console.WriteLine($"lip initial useful pose={feet} route={stop} used={used} partial={route?.Partial} "
                    + $"steps={string.Join(';', route?.Steps.Select(step => $"{step.Kind}:{step.From}->{step.Tile}") ?? Array.Empty<string>())}");
            }
        for (int x = ore.X - 3; x <= ore.X + 3; x++)
        {
            var tile = new Point(x, ore.Y);
            var pose = live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.StandAt(tile.X, tile.Y, false);
            if (pose == null) continue;
            var jumps = new live::AICompanion.Companion.Brain.Infrastructure.Movement.JumpTraversal().Candidates(
                live::AICompanion.Companion.Brain.Infrastructure.Movement.NavNode.At(tile), pose, false);
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
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        // The wall went up after the setup flooded, so the region has to be flooded again before anything
        // asks about the far side of it; otherwise the tree reads reachable because the world it was proven
        // in had no wall.
        ResettleReach(ctx);
        var chop = new live::AICompanion.Companion.Brain.Activities.Gathering.ChopTree();
        var tree = new live::AICompanion.Companion.Brain.Infrastructure.Interactions.Chopping.TreeFinder.ChoppableTree(
            new Point(40, 59), new Vector2(38 * 16 + 8, 60 * 16), 1);
        typeof(live::AICompanion.Companion.Brain.Activities.Gathering.ChopTree)
            .GetField("tree", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(chop, tree);
        Require(VerifyPreparedActivities.PrepareAndScore(chop, ctx) == 0f && VerifyPreparedActivities.PrepareAndScore(mine, ctx) > 0f,
            "a retained tree across a sealed wall must yield to reachable ore beside the companion");
        var sinceReachField = typeof(live::AICompanion.Companion.Brain.Activities.Gathering.ChopTree)
            .GetField("sinceReach", BindingFlags.NonPublic | BindingFlags.Instance)!;
        int preparedAge = (int)sinceReachField.GetValue(chop)!;
        for (int comparison = 0; comparison < 20; comparison++)
            Require(chop.Score() == 0f, "repeated comparison must preserve an unavailable tree's value");
        Require((int)sinceReachField.GetValue(chop)! == preparedAge,
            "chopping comparison must not advance its discovery timer or repeat its reach search");
        for (int tick = 0; tick < 20; tick++) VerifyPreparedActivities.PrepareAndScore(chop, ctx);
        Require((int)typeof(live::AICompanion.Companion.Brain.Activities.Gathering.ChopTree)
            .GetField("sinceReach", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chop)! == 20,
            "unchanged retained work must reuse its reach verdict rather than search every scoring tick");
    }

    private static void ChoppingPrefersASeparateActiveTrunk()
    {
        WorkPolicy original = WorkPolicies.Chopping;
        var clock = new live::AICompanion.Companion.Brain.Infrastructure.Observation.TileDamageClock();
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
                new live::AICompanion.Companion.Brain.Infrastructure.Observation.TileDamageWatcher()
                    .KillTile(point.X, point.Y, TileID.Trees, ref fail, ref effectOnly, ref noItem);
                ctx.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
                Require(ctx.Senses.Player.ChoppedTree == point, "cooperation fixture must observe the actual active trunk");
            }
            WorkPolicies.Chopping = WorkPolicy.Opportunistic;
            PlayerHits(first);
            var chop = new live::AICompanion.Companion.Brain.Activities.Gathering.ChopTree();
            Require(VerifyPreparedActivities.PrepareAndScore(chop, ctx) > 0 && chop.ActivityTarget == second.ToWorldCoordinates(),
                "automatic chopping must prefer a separate usable tree over the nearer player trunk");
            PlayerHits(second);
            Require(VerifyPreparedActivities.PrepareAndScore(chop, ctx) > 0 && chop.ActivityTarget == first.ToWorldCoordinates(),
                "a new player trunk must refresh cooperation before the ordinary discovery deadline");
            Tile removed = Main.tile[first.X, first.Y];
            removed.HasTile = false;
            Require(VerifyPreparedActivities.PrepareAndScore(new live::AICompanion.Companion.Brain.Activities.Gathering.ChopTree(), ctx) > 0,
                "automatic cooperation is a preference and must permit the sole remaining player tree");
            WorkPolicies.Chopping = WorkPolicy.Mimic;
            Require(VerifyPreparedActivities.PrepareAndScore(new live::AICompanion.Companion.Brain.Activities.Gathering.ChopTree(), ctx) == 0,
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
                live::AICompanion.Companion.Brain.Activities.CompanionAction action = mine;
                if (chopping)
                {
                    Tile trunk = Main.tile[tile.X, tile.Y];
                    trunk.TileType = TileID.Trees;
                    Main.tileAxe[TileID.Trees] = true;
                    Main.tileSolid[TileID.Trees] = false;
                    TileID.Sets.IsATreeTrunk[TileID.Trees] = true;
                    action = new live::AICompanion.Companion.Brain.Activities.Gathering.ChopTree();
                }
                if (inPosition) ctx.Npc.Bottom = new Vector2(23 * 16 + 8, 90 * 16);
                else ctx.Npc.Bottom = new Vector2(15 * 16 + 8, 90 * 16);
                Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) > 0,
                    $"permission fixture needs prepared work: chopping={chopping}; inPosition={inPosition}");
                var admission = live::AICompanion.Companion.Brain.Infrastructure.Selection.ValidatePreparedActivity.Capture(action);
                if (chopping) WorkPolicies.Chopping = WorkPolicy.Disabled;
                else WorkPolicies.Mining = WorkPolicy.Disabled;
                Require(admission.Rejection(action) == "work-disabled",
                    $"revoked work must fail activation admission: chopping={chopping}; inPosition={inPosition}; rejection={admission.Rejection(action)}");
                var request = action.Execute(ctx);
                Require(request == live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest.Hold
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
                var chop = new live::AICompanion.Companion.Brain.Activities.Gathering.ChopTree();
                Require(VerifyPreparedActivities.PrepareAndScore(chop, ctx) > 0,
                    "a tree inside actual reach must prepare useful work");
                var request = chop.Execute(ctx);
                Require(request == live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest.Hold
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
        Require(mine.ConcludeAttempt(0).Status == live::AICompanion.Companion.Brain.Activities.AttemptStatus.Invalid,
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
        Require(mine.ConcludeAttempt(0).Status == live::AICompanion.Companion.Brain.Activities.AttemptStatus.Invalid,
            "the attempt that owned the externally cleared job must conclude invalid");
        mine.BeginAttempt();
        Tile ore = Main.tile[second.X, second.Y];
        ore.ClearEverything();
        ore.HasTile = true;
        ore.TileType = TileID.Copper;
        for (int i = 0; i < 61 && mine.JobId <= firstJob; i++) VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        Require(mine.JobId > firstJob, "the fixture must discover the second job before concluding");
        Require(mine.ConcludeAttempt(0).Status == live::AICompanion.Companion.Brain.Activities.AttemptStatus.Attempted,
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
        Require(mine.ConcludeAttempt(effects) is { Status: live::AICompanion.Companion.Brain.Activities.AttemptStatus.Complete,
                Attribution: live::AICompanion.Companion.Brain.Activities.AttemptAttribution.Shared },
            $"a vein finished together must be a shared completion, not the companion's own; got {mine.ConcludeAttempt(effects)}");
    }

    /// <summary>
    /// The nearest-first approach must return exactly what the exhaustive scan returned: the same
    /// verdict and the same stand, ties included. The reference below is the previous algorithm
    /// verbatim, run on the same native terrain after the production call so both see warm caches.
    /// Geometry varies the ore's height and a wall that seals poses.
    ///
    /// <para>The five <c>from</c> positions vary less than they look, and the row is worth reading with that
    /// in mind. They used to vary the search origin, because each pose's verdict came from a walker search
    /// starting at those feet; the verdict now comes from a flood run from the companion's own feet, which do
    /// not move across the twenty-five pairs, and the pose ranking measures each candidate stand against the
    /// ore rather than against <c>from</c>. So what <c>from</c> still varies is the <see cref="FindToolAccess.InReach"/>
    /// early return — whether the query answers before any pose is ranked at all. That is a real fork and the
    /// row keeps it; it is simply not the sweep of origins the five values suggest. The closing requirement is
    /// what keeps the row honest either way: a comparison holding no unreachable case compares nothing.</para>
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
        // The sealing wall went up after the setup flooded, so without this the region still describes an
        // open floor and every one of the twenty-five pairs reads reachable — which the row's own closing
        // requirement catches, because a comparison with no unreachable case in it compares nothing.
        ResettleReach(ctx);
        int compared = 0, reachable = 0;
        foreach (Point ore in ores)
        foreach (float x in feet)
        {
            Vector2 from = new(x * 16 + 8, 90 * 16);
            var actual = FindToolAccess.Approach(ore, from, ctx.Companion.Brain.Senses.Reach, out Vector2 actualStand);
            var expected = ExhaustiveApproach(ore, from, ctx.Companion.Brain.Senses.Reach, out Vector2 expectedStand);
            Require(actual == expected && actualStand == expectedStand,
                $"nearest-first approach diverged from the exhaustive scan: ore={ore} from={from} actual={actual}@{actualStand} expected={expected}@{expectedStand}");
            compared++;
            if (actual == Reachability.Reach.Yes) reachable++;
        }
        Require(compared == ores.Length * feet.Length && reachable > 0 && reachable < compared,
            $"the comparison must include reachable and unreachable approaches to mean anything; reachable={reachable}/{compared}");
    }

    /// <summary>The previous algorithm verbatim except for its oracle: it asks the same reach sense the
    /// production call asks, because this row measures the scan order rather than the reach question. Left on
    /// the walker search it would compare two different questions and report the difference as a divergence.</summary>
    private static Reachability.Reach ExhaustiveApproach(Point tile, Vector2 fromFeet,
        live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachSense sense, out Vector2 stand)
    {
        if (FindToolAccess.InReach(fromFeet, tile)) { stand = fromFeet; return Reachability.Reach.Yes; }
        Vector2 eye = new(0f, -30f), tileCentre = tile.ToWorldCoordinates(8f, 8f);
        Vector2? best = null;
        bool unknown = false;
        float bestDist = float.MaxValue;
        for (int dx = -Player.tileRangeX; dx <= Player.tileRangeX; dx++)
            for (int dy = -Player.tileRangeY; dy <= Player.tileRangeY + 2; dy++)
            {
                int x = tile.X + dx, y = tile.Y + dy;
                if (!live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.IsStandable(x, y)) continue;
                Vector2 feet = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.FeetWorld(new Point(x, y));
                if (!FindToolAccess.InReach(feet, tile) || !FindToolAccess.InReach(feet + new Vector2(-8, 0), tile) || !FindToolAccess.InReach(feet + new Vector2(8, 0), tile)) continue;
                float d = Vector2.DistanceSquared(feet + eye, tileCentre);
                var reach = sense.Reachable(new Point(x, y)) switch
                {
                    live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict.Reachable => Reachability.Reach.Yes,
                    live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict.NotYet => Reachability.Reach.Unknown,
                    _ => Reachability.Reach.No,
                };
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
                    ? attempt is { Status: live::AICompanion.Companion.Brain.Activities.AttemptStatus.Complete,
                        Attribution: live::AICompanion.Companion.Brain.Activities.AttemptAttribution.Companion }
                    : attempt.Status == live::AICompanion.Companion.Brain.Activities.AttemptStatus.Invalid,
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
                live::AICompanion.Companion.Brain.Activities.CompanionAction action = mine;
                Tile tile = Main.tile[point.X, point.Y];
                if (chopping)
                {
                    tile.TileType = TileID.Trees;
                    Main.tileAxe[TileID.Trees] = true;
                    Main.tileSolid[TileID.Trees] = false;
                    TileID.Sets.IsATreeTrunk[TileID.Trees] = true;
                    action = new live::AICompanion.Companion.Brain.Activities.Gathering.ChopTree();
                }
                Require(VerifyPreparedActivities.PrepareAndScore(action, ctx) > 0,
                    "replacement fixture needs a real prepared tool target");
                object? originalIdentity = action.ActivityIdentity;
                tile.TileType = chopping ? TileID.Cactus : TileID.Tin;
                Main.tileAxe[TileID.Cactus] = true;
                Main.tileSolid[TileID.Tin] = true;
                var request = action.Execute(ctx);
                Require(request == live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest.Hold
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
            Item axe = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Chopping.TileChopper.AxeFor(ctx.Player);
            Require(!ctx.Companion.Chopper.Swing(point, axe) && ctx.Companion.Chopper.LastOutcome == null,
                "native axe work must reject an axe-eligible material outside the discovery tree category");
        }
        finally { Main.tileAxe[TileID.WoodBlock] = original; }
    }

    internal static (MineOre Action, ActionContext Context) SetUp(WorkPolicy policy, ushort tileType, params Point[] ore)
        => SetUp(policy, tileType, ore, null);

    /// <summary>
    /// An approach the evidence cannot decide is not a plan. Walking at it was how 0.22.44
    /// spent 1,583 ticks of the 13:45 session on one unreachable pocket: the search declined to
    /// answer, mining treated that as an investigation, and covering any 32 px reset the stall
    /// clock so the vein was never refused.
    ///
    /// <para>The undecided state is now produced by an unsettled reach flood rather than by a starved A*
    /// clock, and the change of instrument is the point rather than an incidental. Mining's approach no
    /// longer runs a search, so <c>AStar.MsBudget</c> cannot reach it: left as it was, this row would have
    /// gone on passing while testing nothing, because the flood is empty in a fixture that never resolves
    /// and Unknown would have arrived for a reason the row does not name. Emptying the region is the
    /// mechanism that actually produces Unknown here, so that is what the row does, out loud.</para>
    /// </summary>
    private static void AnUnprovenApproachIsNotAPlan()
    {
        // Far enough along the floor that no pose is in reach from where the body stands, so the answer has
        // to come from the region. Ore beside the companion resolves through the in-reach shortcut before
        // the region is consulted at all, so a near fixture cannot reach the undecided state.
        var (action, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, new Point(50, 59));
        // An unflooded region: every tile is "not yet known" and nothing is proven either way. This is the
        // state the live brain is in for its first rescores after a world change.
        EmptyTheReachRegion(ctx);
        float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
        Require(action.Status == "approach unknown",
            $"the fixture must actually reach an undecided approach, or it tests nothing; status={action.Status}");
        Require(score == 0f,
            $"ore whose approach the evidence could not decide scored {score}, so mining would still win the tick; status={action.Status}");
        Require(action.TargetTile == null && action.ActivityTarget == null,
            "an undecided approach must not publish a plan target");
        Require(action.Execute(ctx).Kind == live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.Hold,
            "an undecided approach must not walk at the ore");
    }

    /// <summary>
    /// The only row in this suite that runs the whole brain from a flood nobody has run, and it exists because
    /// every other row's setup warms one. That warming is honest — the live game has been flooding since the
    /// companion spawned — but it means the suite says nothing about the state the live game is actually in at
    /// spawn and after every world edit, which is a region that is null rather than merely stale.
    ///
    /// <para>What makes that state worth a row of its own is that nothing an idle companion does is obliged to
    /// leave it. Work reads the sense, so with a null region every ore answers NotYet and every work activity
    /// offers zero; keeping company wins by default; and <c>Resolve</c>'s <c>Hold</c> branch returns before it
    /// reaches <c>Refresh</c>, so a resting tick ages the flood without growing it. The one thing that breaks
    /// the circle is that a stroll is an <c>Exact</c> request and <c>Exact</c> refreshes — and the stroll is
    /// chosen by a die roll rather than by anything that knows the region is empty. This row is the standing
    /// check that the circle stays broken; a change that makes an idle companion hold still would close it,
    /// and no other fixture here could tell.</para>
    ///
    /// <para>The ore sits fourteen tiles out, past both tool reach and the band a stroll picks goals in, so it
    /// cannot be reached by a stroll wandering into range: the only route to a break is the region growing,
    /// mining offering usable work and winning the tick. Whether a given idle window strolls or rests is a die
    /// roll, and the row needs no die of its own for that: every setup here goes through
    /// <c>VerifyCompanionLifecycle.Create</c>, which seeds <c>Main.rand</c>, so each row starts from the same
    /// rolls however many the rows before it took.</para>
    /// </summary>
    private static void AColdFloodDoesNotLeaveTheBrainResting()
    {
        Point ore = new(34, 59);
        var (mine, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        EmptyTheReachRegion(ctx);
        // The row must start in the cold state rather than assume it: a setup that warmed the region, or an
        // ore near enough to resolve through the in-reach shortcut, would make the run below prove nothing
        // about a cold flood while passing exactly as it does now.
        float cold = VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        Require(cold == 0f && mine.Status == "approach unknown",
            $"the row must begin with mining unable to answer, or the run proves nothing about a cold flood; "
            + $"score={cold} status={mine.Status}");
        var run = RunBrainUntilBroken(ctx, ore, 900);
        Require(run.Broken,
            $"a companion that spawns beside its player with a cold reach flood must still start the ore fourteen "
            + $"tiles away; feet={ctx.Npc.Bottom} offer={mine.Eligibility}/{mine.EligibilityReason} "
            + $"status={mine.Status} action={ctx.Companion.Brain.LastAction?.Name} "
            + $"reach-complete={ctx.Companion.Brain.Positioner.ReachComplete} strikes={run.StrikeFeet.Count}");
    }

    /// <summary>
    /// Put the reach sense back into the state it is in before its first flood: nothing claimed and nothing
    /// exhausted, so every tile answers NotYet. Both fields are written because the verdict reads both — a
    /// tile is Unreachable only where the set it was looked up in ran out of region, and leaving
    /// <c>Complete</c> true would turn every unclaimed tile into a proven No, which is the opposite state.
    /// </summary>
    internal static void EmptyTheReachRegion(ActionContext ctx)
    {
        var sense = ctx.Senses.Reach;
        var type = typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachSense);
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        // `scored` as well as `returnable`, because a non-null `scored` is what tells Refresh the region is
        // young enough to serve: leaving it set makes the next resolve hand back the region from before
        // whatever the caller just changed. The two searches go with them, and that is the part that is easy
        // to miss — Refresh reuses a live ContinueRouteSearch whenever it is valid and starts from the same
        // feet, and a reused one hands back the tiles it had already expanded into, so emptying only the
        // result sets refills them from a search that walked through the wall before the wall existed.
        foreach (string field in new[] { "scored", "returnable", "raw" })
            type.GetField(field, instance)!.SetValue(sense, null);
        foreach (string field in new[] { "returnSearch", "rawSearch" })
        {
            (type.GetField(field, instance)!.GetValue(sense) as System.IDisposable)?.Dispose();
            type.GetField(field, instance)!.SetValue(sense, null);
        }
        type.GetProperty("Complete")!.GetSetMethod(true)!.Invoke(sense, new object[] { false });
        type.GetProperty("ScoredComplete")!.GetSetMethod(true)!.Invoke(sense, new object[] { false });
    }

    /// <summary>
    /// Throw the region away and flood it again, for a fixture that edits terrain after its setup. The live
    /// brain gets this for nothing: a terrain revision forces a reflood inside the sense, and the next
    /// resolve pays for it. A fixture that edits the world and then prepares directly never resolves, so it
    /// would read the region from before its own wall and every row about that wall would be answered by a
    /// world that no longer exists.
    /// </summary>
    internal static void ResettleReach(ActionContext ctx) => ResettleReach(ctx.Companion, ctx.Player);

    /// <summary>The same, for a fixture that holds the companion and the player rather than a context —
    /// which is the shape a fixture has when it has just moved one of them, the other reason a region goes
    /// stale. The flood is run from the companion's feet, so moving the body invalidates it exactly as
    /// moving a wall does.</summary>
    internal static void ResettleReach(live::AICompanion.Companion.CharacterBody.CompanionNPC companion, Player player)
    {
        AStar.InvalidateEdges();
        var sense = companion.Brain.Senses.Reach;
        var type = typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachSense);
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (string field in new[] { "scored", "returnable", "raw" })
            type.GetField(field, instance)!.SetValue(sense, null);
        foreach (string field in new[] { "returnSearch", "rawSearch" })
        {
            (type.GetField(field, instance)!.GetValue(sense) as System.IDisposable)?.Dispose();
            type.GetField(field, instance)!.SetValue(sense, null);
        }
        type.GetProperty("Complete")!.GetSetMethod(true)!.Invoke(sense, new object[] { false });
        type.GetProperty("ScoredComplete")!.GetSetMethod(true)!.Invoke(sense, new object[] { false });
        SettleReach(companion, player);
    }

    /// <summary>
    /// A sealed nearby ore is proven-no. A farther ore the search has not finished asking is
    /// Unknown. Neither is a plan, and the sealed tile must not be substituted in as if it were
    /// the unresolved one.
    /// </summary>
    private static void AnUnknownApproachDoesNotSubstituteASealedNeighbour()
    {
        Point sealedOre = new(25, 59), unresolvedOre = new(50, 59);
        var (action, ctx) = SetUp(WorkPolicy.Opportunistic, TileID.Copper, sealedOre, unresolvedOre);
        foreach (Point side in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
        {
            Tile wall = Main.tile[sealedOre.X + side.X, sealedOre.Y + side.Y];
            wall.HasTile = true;
            wall.TileType = TileID.Dirt;
        }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        // The two verdicts this row needs side by side come from two different places, which is what makes it
        // worth keeping now that one flood answers every reach question. The sealed ore is No on geometry —
        // it has no exposed face, so the pose scan finds nothing to rank and never consults the region at
        // all — while the far ore is Unknown because the region has not settled. An unsettled region cannot
        // prove anything No, so a row needing both answers at once has to get one of them from geometry.
        EmptyTheReachRegion(ctx);
        Require(FindToolAccess.Approach(sealedOre, ctx.Npc.Bottom, ctx.Companion.Brain.Senses.Reach, out _) == Reachability.Reach.No,
            "the nearby ore must have no exposed working face, and must answer so from geometry rather than from the region");
        float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
        Require(score == 0f && action.TargetTile == null,
            $"neither the sealed ore nor the unsettled region may become a plan; status={action.Status} score={score} target={action.TargetTile}");
        Require(action.Status == "approach unknown",
            $"the farther ore must remain the discovery's unresolved candidate, not be replaced by the sealed neighbour; status={action.Status}");
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
            // The chamber was built after the setup flooded, so the region has to be flooded again before the
            // rows below ask whether the body can stand inside it.
            ResettleReach(ctx);
            string shape = pocket ? "chambered" : "sealed";
            Require(Vector2.DistanceSquared(ctx.Npc.Bottom, blocked.ToWorldCoordinates()) < Vector2.DistanceSquared(ctx.Npc.Bottom, usable.ToWorldCoordinates()),
                $"the {shape} ore must be the nearer one, or the fixture tests nothing");
            var standing = FindToolAccess.Approach(blocked, ctx.Npc.Bottom, ctx.Companion.Brain.Senses.Reach, out _);
            var hop = FindToolAccess.HopApproach(blocked, ctx.Companion.Motor.State, ctx.Companion.Brain.Senses.Reach, out _);
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
            var standing = FindToolAccess.Approach(ore, standingFeet, ctx.Companion.Brain.Senses.Reach, out _);
            var hop = FindToolAccess.HopApproach(ore, ctx.Companion.Motor.State, ctx.Companion.Brain.Senses.Reach, out Vector2 takeOff);
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
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        try { SealThenReopenAnApproachedOre(); }
        finally { live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false; }
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
        Vector2 besideFeet = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.FeetWorld(new Point(23, 59));
        Require(FindToolAccess.InReach(besideFeet, ore)
            && !Collision.CanHitLine(besideFeet + new Vector2(0f, -30f), 1, 1, opened.ToWorldCoordinates(8f, 8f), 1, 1),
            "the notch fixture must be one the wide native beam refuses and a swing reaches, or it does not test the face-access walk");
        var run = RunBrainUntilBroken(ctx, ore, 900);
        var approachNow = FindToolAccess.Approach(ore, ctx.Npc.Bottom, ctx.Companion.Brain.Senses.Reach, out Vector2 standNow);
        bool standableBeside = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.IsStandable(23, 59);
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
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
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
            new live::AICompanion.Companion.Brain.Infrastructure.Observation.TileDamageWatcher()
                .KillTile(hit.X, hit.Y, tileType, ref fail, ref effectOnly, ref noItem);
        }
        companion.Brain.Senses.Update(companion.NPC, player, companion.Breath);
        SettleReach(companion, player);
        return (new MineOre(), new ActionContext(companion, companion.Brain.Senses));
    }

    /// <summary>
    /// Drive the reach flood to completion before any fixture prepares. Mining asks its approach question of
    /// the reach sense rather than of a search of its own, so a scene whose flood has never been advanced
    /// answers "not yet known" about every pose and no ore is offered anywhere — which is the correct answer
    /// to a question nobody has asked, and nothing to do with the ore each row is about. The live game has
    /// been flooding since the companion spawned; a fixture that calls Prepare directly has not, and this is
    /// the difference rather than a behaviour being arranged.
    /// </summary>
    internal static void SettleReach(live::AICompanion.Companion.CharacterBody.CompanionNPC companion, Player player)
    {
        var brain = companion.Brain;
        var home = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer, player.Bottom);
        for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++)
            brain.Positioner.Resolve(home, brain.Senses, null);
        // Warming the region is the whole job, so the choice it took to warm it is thrown away. Those
        // resolves leave the positioner holding a destination, the request kind that produced it and a fresh
        // rescore clock, and a fixture that then runs the brain gets that destination handed back as a
        // retained position on its very first tick — the guard row in the safety suite walked to a spot the
        // ore setup had chosen, eighteen tiles short of the fight, and reported it as guarding's own answer.
        // A Hold resolve is the production path that clears exactly those three and touches nothing else:
        // the region it never refreshes stays warm.
        brain.Positioner.Resolve(
            new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
                live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.Hold, player.Bottom),
            brain.Senses, null);
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
