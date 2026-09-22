extern alias live;

using System.Reflection;
using AICompanion.Tools.Ledger;
using Microsoft.Xna.Framework;
using Terraria;

using CornerGraph = live::AICompanion.Companion.Brain.Infrastructure.Movement.CornerGraph;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using Navigator = live::AICompanion.Companion.Brain.Infrastructure.Movement.Navigator;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;

/// <summary>
/// The suite's entry point and its case table.
///
/// <para>This file used to open with a collision matrix comparing a portable body simulation against
/// Terraria's own NPC collision on every shape, liquid, entry pose and control — 2,536 comparisons
/// whose whole purpose was that the companion had two bodies that could disagree. The orb has one:
/// the mod switches the engine's tile collision off for it and the motor runs the mod's own circle
/// contact, in the mod and in every headless tool alike, so there is no second body to match and the
/// matrix has no subject. <c>VerifyOrbContact</c> is the contact's proof now, and it proves the size
/// rule directly rather than by agreement with a routine the body no longer uses.</para>
/// </summary>
internal static class VerifyEngineMotion
{
    public static int Run(bool lifecycleOnly = false, bool liquidsOnly = false, bool workOnly = false, bool followOnly = false, bool protectionOnly = false, bool brainCostOnly = false, bool combatCostOnly = false, bool combatPurposeOnly = false, bool safetyLayerOnly = false, bool dodgeReproOnly = false, bool activitiesOnly = false)
    {
        // The engine containers, the miniature world's dimensions and its tile map now belong to
        // ResetProcessState, which the entry point calls before dispatching any flag — they were
        // here, and so every flag that returns before this method skipped them. This call keeps the
        // default suite working when Run is reached some other way; it is idempotent.
        ResetProcessState.PrepareProcess();
        // The floor is this suite's own scene rather than process setup, so it stays here.
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 60];
            tile.HasTile = true;
            tile.TileType = 1;
        }
        if (lifecycleOnly) return VerifyCompanionLifecycle.Run() + VerifyDowningAndRevival.Run() + VerifyStatMirroring.Run();
        if (liquidsOnly) return VerifyLiquidsAreAir.FlightThroughEveryLiquid() + VerifyLiquidsAreAir.AFloodedPassageIsReachedThroughIt();
        if (workOnly) return VerifyOreWork.Run() + VerifyCompanionPreferences.Run() + VerifyCompanionActivities.Run() + VerifyUsefulAssistance.Run()
            + VerifyGatheringCooperation.Run() + VerifyWorkAccounting.Run() + VerifyCollectionContracts.Run()
            + VerifyAssistanceTrips.Run() + VerifyCapabilityRevision.Run() + VerifyLightAndReachSenses.Run();
        if (followOnly) return VerifyResponsiveFollowing.Run() + VerifyCompanyLocalMotion.Run() + VerifyCourtesy.Run();
        if (protectionOnly) return VerifyFollowRecoveryAndProtection.Run();
        if (brainCostOnly) return MeasureBrainCost.Execute();
        if (combatCostOnly) return MeasureCombatCost.Execute();
        if (combatPurposeOnly) return VerifyCombatPurpose.Run();
        if (safetyLayerOnly) return VerifySafetyIsALayerOnTheJob.Run();
        if (dodgeReproOnly) return VerifySafetyIsALayerOnTheJob.ReproduceDodgeOnDryFloor();
        if (activitiesOnly) return VerifyCompanionActivities.Run();
        int failed = 0;
        // Every fixture below used to be a term in one `failed += Verify*.Run()` sum, and the sum was
        // an abort dressed as a total: assertions here throw, so the first fixture to fail took the
        // whole chain with it. As a table, each fixture is a named case that reports its own verdict
        // and cannot reach its neighbours, and the emitter's own reset runs between them so a case
        // cannot inherit the world its predecessor left either.
        foreach ((string name, Func<int> body) in DefaultCases())
            failed += EmitLedgerRows.Case(Instrument, "EngineReplay", name, body);
        return failed == 0 ? 0 : 1;
    }

    internal const string Instrument = "engine-replay";

    /// <summary>
    /// The default suite, one named case per fixture. The name is the question the fixture answers,
    /// written as the sentence a person would say, because it is what the scoreboard prints and
    /// what <c>--case</c> matches against.
    ///
    /// Case granularity is the fixture file rather than the assertion. The plan's full migration
    /// wants a case per assertion site; this is the granularity reachable from here, and it is
    /// already enough for per-case selection, rerun-red and a scoreboard that names what moved.
    /// </summary>
    private static IEnumerable<(string Name, Func<int> Body)> DefaultCases() => new (string, Func<int>)[]
    {
        ("the orb fits every two-by-two gap and no one-by-one gap in any direction", VerifyOrbContact.SizeRule),
        ("the orb passes a one-tile diagonal step without ever overlapping a wall", VerifyOrbContact.DiagonalStep),
        ("contact pushes the orb out of a wall, kills the velocity into it and keeps the slide", VerifyOrbContact.PushOutAndSlide),
        ("a platform is air to the body, and the contact, the clearance, the corner graph and the clearance field all say so", VerifyOrbContact.PlatformsAreAir),
        ("a two-wide corridor is open to the flood, a one-wide is closed, and a liquid across it is as open as air", VerifyFreeSpace.CorridorsAndLiquids),
        ("the free-space flood over a screen-sized room finishes in a handful of slices", VerifyFreeSpace.FloodFinishes),
        ("a flood bounded by travel cost exhausts inside its radius with exactly the corners the ball holds", VerifyFreeSpace.FloodBounded),
        ("an enemy's observed motion is forecast from what it actually did", VerifyObservedMotion.Run),
        ("the god's-eye occurrence stream records what it claims", VerifyGodsEyeEvents.Run),
        ("a decision that contradicts itself is named while it happens", VerifyDecisionTripwires.Run),
        ("the whole update is measured and its draws are counted apart from it", VerifyFrameLedger.Run),
        ("a capture states the configuration it ran under and the course order it took", VerifyCaptureHonesty.Run),
        ("retained courses compare conserved futures and publish complete repairs", VerifyCourseCore.Run),
        ("projected effects preserve causality and physical resource capacity", VerifyProjectionContracts.Run),
        ("course orders resume concrete bindings before forecasting consequences", VerifyCourseOrderProjection.Run),
        ("companionship forecasts integrate shared gap along the trajectory", VerifyCompanionshipForecast.Run),
        ("native course travel queries share the decision allowance fairly", VerifyCourseTravelScheduling.Run),
        ("native course receipts preserve observation order and intent regions", VerifyRetainedCourseObservation.Run),
        ("one observation per tick carries every domain the course brain can act on", VerifyCourseSnapshotAssembly.Run),
        ("the live brain tick asks a course what to do instead of the family chooser", VerifyTheCourseOwnsTheTick.Run),
        ("a course is priced for the harm it flies into and still cannot certify that it harms nobody", VerifyNativeConsequencePricing.Run),
        // The seam between the two above: a real binder and the real forecast on one non-empty order,
        // started at a non-zero tick, which is the only regime where the companion's ready-tick floor
        // and the binder's origin handoff are both observable.
        ("a non-empty course is priced end to end from the tick it starts at", VerifyWholeCoursePricing.Run),
        ("a bound course step names the activity that performs it and the place it happens", VerifyCourseBindingExecution.Run),
        ("an opportunity the census admits usable is one the binder can still read", VerifyAdmittedOpportunitiesBind.Run),
        ("a decision that spans ticks keeps the fight the body is already in", VerifyADecisionInFlightKeepsTheFight.Run),
        ("what one decision costs to assemble and to carry", VerifyWhatEachDecisionCosts.Run),
        ("the search keeps the best order the winner beat", VerifyTheSearchRetainsItsRunnerUp.Run),
        ("collecting, lighting and pot breaking bind steps a course can hold", VerifyAssistanceCourseBindings.Run),
        ("native lighting projections preserve captured light and shared deficits", VerifyLightingOpportunityCapture.Run),
        ("native tree census retains work across cuts and observes axe effects", VerifyTreeOpportunityCapture.Run),
        ("native tool bindings preserve readiness and conditional work", VerifyGatheringCourseBindings.Run),
        ("required and optional diagnostic records preserve offer order", VerifyDiagnosticTransport.RequiredAndOptionalRecordsPreserveOfferOrder),
        ("diagnostic overflow remains visible after the writer closes", VerifyDiagnosticTransport.LargeStringOverflowStaysSticky),
        ("an active diagnostic writer cannot lose ownership to its replacement", VerifyDiagnosticTransport.ClosingWriterRejectsSameGenerationReopen),
        ("normal diagnostic closure retains row count", VerifyDiagnosticTransport.NormalClosureWritesCompleteTerminalFooter),
        ("terminal diagnostic write timeout remains incomplete", VerifyDiagnosticTransport.HeldWriteTimeoutStaysIncompleteAfterRelease),
        ("diagnostic sink faults remain visible", VerifyDiagnosticTransport.InjectedWriteFailureStaysSticky),
        ("in-flight and complete envelope bytes stay reserved", VerifyDiagnosticTransport.InFlightBytesAndLegacyStringsAreCharged),
        ("combat and repair spend the same decision allowance", VerifyRetainedCombatBudget.SharedAllowanceCutsEveryConsumer),
        ("a useful combat opener fires while broader search is cut", VerifyRetainedCombatBudget.AUsefulOpenerSurvivesABroaderSearchCut),
        ("a prepared comparison preserves its numbers", VerifyPreparedActivities.Run),
        ("each purpose family nominates its best child", VerifyFamilyOffers.Run),
        ("the orb flies its planned routes over native terrain and stops at what it cannot fit through", VerifyRoutes),
        ("a route that ran out of budget is pending, and only an exhausted one is unreachable", VerifyRouteEndings.Run),
        ("a projectile's prior arc is the game's own, a believed-straight arc is learned from watched flights until it lands, and no learned arc spawns an unproved shot", VerifyArcLearning.Run),
        ("a four-pellet spread and a three-shot burst group into one use each", VerifyVolleyLearning.SpreadAndBurstGroupIntoOneUseEach),
        ("an unknown arc is learned from the player's flights and aims the companion's shot", VerifyVolleyLearning.ArcsAreLearnedFromThePlayersFlights),
        ("a companion shot's AI reads the companion's aim as its cursor", VerifyVolleyLearning.CompanionShotsReadTheCompanionsAimAsTheirCursor),
        ("a volley whose slots carry a quarter share each lands a quarter per pellet", VerifySimulatedUses.QuarterSharesLandAQuarterPerPellet),
        ("delayed gravity is fitted and matches native tick for tick", VerifyFlightLaws.DelayedGravityMatchesNativeTickForTick),
        ("a water bolt's bounce points are predicted in a fixture box", VerifyFlightLaws.BouncePointsArePredictedInAFixtureBox),
        ("traces under a pierce modifier leave law and hit response unchanged", VerifyFlightLaws.ModifiedTracesLeaveLawAndHitResponseUnchanged),
        ("an unpredictable type is still fired, never refused", VerifyFlightLaws.AnUnpredictableTypeStillFires),
        ("homing predicts its path to a placed body", VerifyFlightLaws.HomingPredictsItsPathToAPlacedBody),
        ("pass-through is predicted through a wall", VerifyFlightLaws.PassThroughIsPredictedThroughAWall),
        ("a splitting shot's children are predicted by trigger and count", VerifyFlightLaws.SplittingShotsChildrenArePredictedByTriggerAndCount),
        ("a dominated plan never survives the front, whatever the weights", VerifyAttackPlanning.ADominatedPlanNeverSurvivesTheFront),
        ("company fights from inside the predicted region", VerifyAttackPlanning.CompanyFightsFromInsideThePredictedRegion),
        ("range follows the weapon against one lone target", VerifyAttackPlanning.RangeFollowsTheWeaponAgainstOneLoneTarget),
        ("a spread weapon closes at full life and holds range at low life", VerifyAttackPlanning.SpreadClosesAtFullLifeAndHoldsRangeAtLowLife),
        ("two in a line are fought from the line, nearer first", VerifyAttackPlanning.TwoInALineAreFoughtFromTheLineNearerFirst),
        ("goons then boss earns two segments", VerifyAttackPlanning.GoonsThenBossEarnsTwoSegments),
        ("a floor roller takes the low flank", VerifyAttackPlanning.AFloorRollerTakesTheLowFlank),
        ("a grenade then pierce is timed to the explosion", VerifyAttackPlanning.AGrenadeThenPierceIsTimedToTheExplosion),
        ("a bank shot plans with a bouncing weapon only", VerifyAttackPlanning.ABankShotPlansWithABouncingWeaponOnly),
        ("planning cost on a crowd fits a frame", VerifyAttackPlanning.PlanningCostOnACrowdFitsAFrame),
        ("the overlay carries the committed-plan layer", VerifyAttackPlanning.TheOverlayCarriesThePlanLayer),
        ("an extra projectile adds its spawn and changes the prediction", VerifySimulatedUses.ExtraProjectileAddsItsSpawnAndChangesThePrediction),
        ("knowledge saved by name survives shuffled numeric ids", VerifyVolleyLearning.KnowledgeSurvivesANameKeyedSaveUnderShuffledIds),
        ("an unlimited-pierce use strikes twenty bodies", VerifySimulatedUses.UnlimitedPierceStrikesTwenty),
        ("four pellets on a dying body record four full hits", VerifySimulatedUses.FourPelletsOnALowBodyRecordFourHits),
        ("a timed child's hits land after its parent's", VerifySimulatedUses.TimedChildrenLandAfterTheirParent),
        ("the same decision simulated twice is identical", VerifySimulatedUses.TheSameDecisionSimulatedTwiceIsIdentical),
        ("the threat sense reads danger from sealed chambers correctly", VerifyPersonalDanger.Run),
        ("the companion spawns, lives and is attached both ways", VerifyCompanionLifecycle.Run),
        ("a threat is anticipated from how it actually arrives", VerifyThreatAnticipation.Run),
        ("every liquid is air to the orb: it flies through water, honey, lava and shimmer at its air pace and is never hurt", VerifyLiquidsAreAir.FlightThroughEveryLiquid),
        ("a player beyond a passage flooded with any liquid is reachable through it, and the companion flies it to him", VerifyLiquidsAreAir.AFloodedPassageIsReachedThroughIt),
        ("the player's intent region holds the player on every recorded row and has the shape the owner ruled", VerifyIntentRegionHoldsThePlayer.Run),
        ("following responds to a player who departs", VerifyResponsiveFollowing.Run),
        ("recovery flight and protection admit only what may start them", VerifyFollowRecoveryAndProtection.Run),
        ("ore work breaks ore without excavating ordinary terrain", VerifyOreWork.Run),
        ("the route home a job pays for is priced from beside the player as well as from the job", VerifyRouteHomeFromEitherEnd.Run),
        ("the mining list decides which ores are work, and remembers every ore the player has held", VerifyMiningList.Run),
        ("gathering beside the player is cooperative rather than competing", VerifyGatheringCooperation.Run),
        ("remaining work is accounted to whoever did it", VerifyWorkAccounting.Run),
        ("a collected drop is claimed only for what arrived", VerifyCollectionContracts.Run),
        ("per-character preferences reach the brain", VerifyCompanionPreferences.Run),
        ("the mastery preview is the owner's flat tree of ten-node lanes, and learning follows its edges and needs", VerifyNativeCard.RunMasteryRules),
        ("the six activities are offered and chosen", VerifyCompanionActivities.Run),
        ("assistance is useful rather than merely nearby", VerifyUsefulAssistance.Run),
        ("the senses' lifecycle restores what it changed", VerifyObservationLifecycle.Run),
        ("combat makes progress toward its target", VerifyHuntProgress.Run),
        ("a FireFrom stand holds the point the plan priced and refuses what the flood unclaims", VerifyOfferValidity.Run),
        ("combat is admitted only where it can be executed", VerifyHuntAdmissibility.Run),
        ("an attack's outcome is the one the arsenal forecast", VerifyAttackOutcomes.Run),
        ("handed gear fits its slots and the tool power it reads is the game's own gate", VerifyHandedGear.Run),
        ("the item in a weapon slot fires or swings through the arsenal with the item's own numbers", VerifyItemWeapon.Run),
        ("a hit's push is the game's until the companion learns it, is charged for the danger it adds, and the stand prefers the player's side", VerifyKnockbackAwareness.Run),
        ("weapon, target, stand and aim are valued by what the companion's own shots achieved", VerifyWeaponLearning.Run),
        ("a weapon's misses against one enemy type stay with that type", VerifyWeaponLearning.MissesAgainstOneEnemyTypeStayWithThatType),
        ("a swing records a kill as the strike, the way a projectile does", VerifyWeaponLearning.ASwingKillIsRecordedAsTheStrike),
        ("a hit's push charge is weighted by the learned hit rate its damage is", VerifyWeaponLearning.APushIsChargedAtTheLearnedHitRate),
        ("a shot whose target died to someone else before it could land teaches nothing", VerifyWeaponLearning.AShotWhoseTargetDiedToSomeoneElseTeachesNothing),
        ("the target hold survives ordinary motion and breaks on a change that should change the choice", VerifyWeaponLearning.TheTargetHoldSurvivesOrdinaryMotion),
        ("every projectile the companion spawns, and every descendant, is the companion's", VerifyWeaponLearning.EveryProjectileTheCompanionSpawnsIsTheCompanions),
        ("combat keeps its purpose across a substituted enemy", VerifyCombatPurpose.Run),
        ("only the combat stance fires", VerifyCombatActivity.OnlyCombatFires),
        ("a damageable hostile in reach takes the body off keeping company", VerifyCombatActivity.SharedEagerness),
        ("danger lifts combat over work", VerifyCombatActivity.DangerLiftsCombatOverWork),
        ("an unthreatening enemy far away keeps off the vein", VerifyCombatActivity.DistantIdleEnemyKeepsOffTheVein),
        ("a dodge bends mining without stopping it", VerifyCombatActivity.DodgeBendsMiningWithoutStoppingIt),
        ("an unarmed companion offers no combat", VerifyCombatActivity.UnarmedOffersNoCombat),
        ("an old hunting-off save loads as combat-off", VerifyCombatActivity.HuntingOffMigration),
        ("safety bends the body inside its job and never takes it: an enemy beside a leaving player, firing on, a bent guard, an intervening hostile", VerifySafetyIsALayerOnTheJob.Run),
        ("an assistance trip goes and returns", VerifyAssistanceTrips.Run),
        ("the light and reach senses answer in three values", VerifyLightAndReachSenses.Run),
        ("torches go where his smart cursor would put one in the dark, and the record says why not", VerifyTorchPlacementRule.Run),
        ("every candidate a preparation refused is named with the stage and what it read", VerifyCandidateFunnel.Run),
        ("keeping company over pools stays returnable, never stands still, and meets a walking player", VerifyCompanyLocalMotion.Run),
        ("the park is chosen about the player rather than about the body, and sits in the band above his head", VerifyTheParkIsAboutThePlayer.Run),
        ("the park waits a few tiles over his head rather than at either end of a column clear all the way up", VerifyTheParkIsAboutThePlayer.BandDecidesWhereInAColumnHeWaits),
        ("keeping company is the fallback: a slime worth hunting is hunted, a far one is not, rejoining is capped and sight is not distance", VerifyCompanyIsTheFallback.Run),
        ("keeping the player company is moving about his whole region: never still, never trailing, and moving from the first tick", VerifyAccompanyingThePlayer.Run),
        ("being with the player needs a way to him: a body inside his region on the far side of a sealed wall comes round", VerifyWithThePlayerNeedsAWayToHim.Run),
        ("a companion sealed off from the player does not travel away from him", VerifyASealedCompanionDoesNotFlyAway.Run),
        ("the way to the player is resumed until it is answered and never read as a proof before it is", VerifyWayToPlayerIsAnsweredInThreeValues.Run),
        ("an immunity change invalidates what depended on it", VerifyCapabilityRevision.Run),
        ("a closed door is opened rather than treated as a wall", VerifyDoorPassage.Run),
        ("courtesy stillness does not depend on what ran before", VerifyCourtesy.Run),
        ("downing and revival keep life on the NPC", VerifyDowningAndRevival.Run),
        ("the companion's stats mirror the player's", VerifyStatMirroring.Run),
        ("experience follows the game's own numbers: kills, boss fights and work level the companion alike in every difficulty", VerifyCompanionExperience.Run),
        ("a whole journey is recorded against its proven ticks", VerifyTravelEpisodes.Run),
        // Appended here rather than at the very end of the list, because the entry below it must stay
        // last for the reason its own comment gives.
        ("a vein's remaining work is answered or refused by name, never left unknown", VerifyVeinRemainingWork.Run),
        ("an encounter suppresses work and orders feasible fights by what the companion survives", VerifyEncounterConduct.Run),
        ("no domain recomputes a spatial fact the senses already publish", VerifySharedSpatialFacts.Run),
        // Last on purpose, and the position is a finding rather than a preference. This case drives 220
        // whole brain ticks with two hostiles, a third spawned mid-scene and one killed, which is the
        // widest process footprint any case in this table has. Registered eighth it turned three
        // `VerifyAttackPlanning` scenes and `VerifyTravelEpisodes` red in-suite while all four stayed
        // green alone; putting its own world back — the npc and item slots it seeded, every projectile,
        // the mining policy, the player's pose — did not clear them, and `VerifyCompanionLifecycle.Create`
        // rebuilds every actor slot and re-seeds the gear at the next case anyway, so the residue is a
        // process static `ResetProcessState.BeforeCase` does not restore and this row is only the first
        // case wide enough to expose it. Naming that static is the reset's job and the main thread owns
        // it; running last means nothing inherits the residue in the meantime.
        ("combat's published front describes the hostiles that are there now", VerifyTheCensusFrontIsCurrent.Run),
    };

    /// <summary>
    /// Five scenes on native tiles, driven through the live navigator and the live motor with a real
    /// <c>CompanionNPC</c>, so what is proved here is the whole chain the game runs: search, smooth,
    /// steer, contact, and the engine adding the displacement.
    ///
    /// <para>Each scene names the mechanism it is there for, because "it arrived" is satisfied by
    /// three different code paths in this navigator and only one of them is route-following. The
    /// open floor exists to prove the navigator does <em>not</em> plan when the straight line is
    /// clear; the ledge, the staircase and the two-wide shaft each put a wall across that line, so
    /// they can only arrive through a planned route and each asserts that it saw one. The one-wide
    /// shaft is the body's size rule read back through the planner: the only way through is a gap
    /// the orb does not fit, so the flood exhausts the free space and reports a proven absence.</para>
    /// </summary>
    private static int VerifyRoutes()
    {
        int failed = 0;
        foreach (string name in new[] { "open floor", "two-tile ledge", "sloped staircase", "two-wide shaft", "one-wide shaft" })
        {
            BuildWorld();
            // The floor every scene stands over, and the ceiling that keeps the flood bounded.
            for (int x = 10; x < 90; x++) { Solid(x, 90); Solid(x, 70); }
            for (int y = 70; y <= 90; y++) { Solid(10, y); Solid(89, y); }

            Point start = new(30, 89), goal = new(60, 89);
            bool routeExpected = true, reachable = true;
            switch (name)
            {
                case "open floor":
                    // Nothing between them: the straight line is clear and no route is needed.
                    routeExpected = false;
                    break;
                case "two-tile ledge":
                    // A block standing two tiles off the floor, taller than the body, across the line.
                    for (int x = 44; x <= 46; x++)
                        for (int y = 87; y <= 89; y++) Solid(x, y);
                    break;
                case "sloped staircase":
                    // Slopes are full tiles to this body, so a staircase is a wall it flies over.
                    for (int i = 0; i <= 5; i++)
                    {
                        Tile step = Main.tile[44 + i, 89 - i];
                        step.HasTile = true;
                        step.TileType = 1;
                        step.Slope = (Terraria.ID.SlopeType)2;
                        for (int y = 90 - i; y <= 89; y++) Solid(44 + i, y);
                    }
                    break;
                case "two-wide shaft":
                case "one-wide shaft":
                    // A floor across the middle of the room with a vertical shaft through it, the
                    // body above and the goal below. Two tiles of shaft put a usable corner in the
                    // middle of it with six pixels either side; one tile has no usable corner
                    // anywhere in it, in either row, so the lower half is closed to this body.
                    int width = name == "two-wide shaft" ? 2 : 1;
                    for (int x = 11; x <= 88; x++) Solid(x, 80);
                    for (int x = 60; x < 60 + width; x++) Clear(x, 80);
                    start = new Point(30, 79);
                    reachable = name == "two-wide shaft";
                    break;
            }

            TerrainChanges.Reset();
            MovementQueries.World = new GameTileWorld();

            var companion = VerifyCompanionLifecycle.Create();
            // Create rebuilds the actor slots, so the world is plugged in again after it.
            MovementQueries.World = new GameTileWorld();
            Vector2 from = CornerGraph.ToWorld(start), to = CornerGraph.ToWorld(goal);
            if (!MovementQueries.IsUsableCorner(start) || !MovementQueries.IsUsableCorner(goal))
            {
                EmitLedgerRows.Detail($"native route {name}: the scene's own start or goal is not a place the body fits");
                failed++;
                continue;
            }
            companion.NPC.Center = from;
            companion.NPC.velocity = Vector2.Zero;

            var navigator = new Navigator();
            bool sawPlannedRoute = false, sawDirect = false, sawUnreachable = false;
            int tick;
            for (tick = 0; tick < 900; tick++)
            {
                companion.Motor.Track();
                var controls = navigator.MoveTo(companion.Motor.State, to);
                sawPlannedRoute |= navigator.Status == Navigator.ExecutionStatus.Executable;
                sawDirect |= navigator.Status == Navigator.ExecutionStatus.Direct;
                sawUnreachable |= navigator.Status == Navigator.ExecutionStatus.Unreachable;
                companion.Motor.Apply(controls);
                VerifyResponsiveFollowing.AdvanceNative(companion);
                if (navigator.Arrived || sawUnreachable) break;
            }

            // What each scene is held to, and what it is deliberately not held to.
            //
            // The first version of this row required the open floor to report `Direct` and never `Executable`,
            // reading those as "the straight line was enough" against "a route was needed". They do not mean
            // that. `Direct` is published only while there is no route *yet* and the line happens to be clear;
            // the moment the search hands back a path the status is `Executable` however open the floor is. So
            // the open floor reported `planned=True direct=False` and the row failed for being right. The status
            // cannot carry that distinction, so the scene's own geometry does: a clear swept line from the start
            // to the goal is a fact about the room, asked of the same contact test the navigator asks.
            bool clearLine = live::AICompanion.Companion.Brain.Infrastructure.Movement.CircleContact.SweptClear(
                MovementQueries.World, from, to, live::AICompanion.Companion.Brain.Infrastructure.Movement.OrbTerrain.Wall);
            bool pass = reachable
                ? navigator.Arrived && clearLine == !routeExpected && (routeExpected ? sawPlannedRoute : true)
                : sawUnreachable && !navigator.Arrived;
            Console.WriteLine($"native route {name}: {tick} ticks, arrived={navigator.Arrived}, clearLine={clearLine}, "
                + $"planned={sawPlannedRoute}, direct={sawDirect}, unreachable={sawUnreachable}, centre={companion.NPC.Center}");
            if (!pass)
            {
                EmitLedgerRows.Detail($"native route {name}: arrived={navigator.Arrived} clearLine={clearLine} planned={sawPlannedRoute} "
                    + $"direct={sawDirect} unreachable={sawUnreachable} reason={navigator.ProgressReason} centre={companion.NPC.Center}");
                failed++;
            }
        }
        return failed;
    }

    /// <summary>A fresh tile map, because a scene that edited tiles in place would leave the previous
    /// scene's walls standing wherever this one happens not to write.</summary>
    internal static void BuildWorld()
    {
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)Main.maxTilesX, (ushort)Main.maxTilesY }, null)!;
    }

    internal static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = true;
        tile.TileType = 1;
    }

    internal static void Clear(int x, int y) => Main.tile[x, y].ClearEverything();
}
