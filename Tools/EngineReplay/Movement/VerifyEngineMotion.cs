using System.Reflection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Tools.Ledger;
using Microsoft.Xna.Framework;
using Terraria;

internal static class VerifyEngineMotion
{
    public static int Run(bool lifecycleOnly = false, bool escapeOnly = false, bool workOnly = false, bool followOnly = false, bool protectionOnly = false, bool miningBaselineOnly = false, bool brainCostOnly = false, bool combatCostOnly = false, bool combatPurposeOnly = false, bool safetyAftermathOnly = false, bool dodgeReproOnly = false)
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
        if (escapeOnly) return VerifyCapturedEscape.Run();
        if (workOnly) return VerifyOreWork.Run() + VerifyCompanionPreferences.Run() + VerifyCompanionActivities.Run() + VerifyUsefulAssistance.Run()
            + VerifyMiningHops.Run() + VerifyGatheringCooperation.Run() + VerifyWorkAccounting.Run() + VerifyCollectionContracts.Run()
            + VerifyAssistanceTrips.Run() + VerifyCapabilityRevision.Run() + VerifyLightAndReachSenses.Run();
        if (miningBaselineOnly) return VerifyOreWork.RunRaisedLipBaseline();
        if (followOnly) return VerifyResponsiveFollowing.Run() + VerifyCompanyLocalMotion.Run() + VerifyCourtesy.Run();
        if (protectionOnly) return VerifyFollowRecoveryAndProtection.Run();
        if (brainCostOnly) return MeasureBrainCost.Execute();
        if (combatCostOnly) return MeasureCombatCost.Execute();
        if (combatPurposeOnly) return VerifyCombatPurpose.Run();
        if (safetyAftermathOnly) return VerifySafetyAftermath.Run();
        if (dodgeReproOnly) return VerifySafetyAftermath.ReproduceDodgeOnDryFloor();
        int failed = 0;
        // The collision matrix is a case like every other one, which it was not: it emitted its row
        // through Row and Measure directly, so it ran all 2,536 comparisons on a --case run that had
        // excluded it and then filed a pass for a case the filter said not to run. Its suite stays
        // "Movement" so the row keeps the key its history is under.
        failed += EmitLedgerRows.Case(Instrument, "Movement",
            "the portable body matches native NPC collision on every shape, liquid and control", MatchNativeCollision);

        // Every fixture below used to be a term in one `failed += Verify*.Run()` sum, and the sum was
        // an abort dressed as a total: assertions here throw, so the first fixture to fail took the
        // whole chain with it. As a table, each fixture is a named case that reports its own verdict
        // and cannot reach its neighbours, and the emitter's own reset runs between them so a case
        // cannot inherit the world its predecessor left either.
        foreach ((string name, Func<int> body) in DefaultCases())
            failed += EmitLedgerRows.Case(Instrument, "EngineReplay", name, body);
        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// Every shape, liquid, entry pose and control the portable body claims to reproduce, against
    /// Terraria's own NPC collision on the same tiles, plus the liquid-transition grid.
    /// </summary>
    private static int MatchNativeCollision()
    {
        int checkedCases = 0, failed = 0;
        foreach (int altitude in new[] { 0, 30 })
        foreach (int shape in Enumerable.Range(0, 7))
        foreach (int liquid in Enumerable.Range(0, 4))
        foreach (float left in new[] { 390f, 398f, 405f })
        foreach (float bottom in new[] { 951f, 960f, 925f })
        foreach (Controls controls in new[] { Controls.None, new Controls(4), new Controls(-4, Jump: true),
            new Controls(-4, Jump: true, JumpScale: BodyPhysics.JumpScaleForTiles(2) * .5f), new Controls(2, FallThrough: true, Descend: true) })
        {
            for (int x = 20; x < 35; x++)
            {
                Tile support = Main.tile[x, 60 + altitude];
                support.HasTile = true;
                support.TileType = (ushort)(shape >= 5 ? 19 : 1);
                support.Slope = (Terraria.ID.SlopeType)(shape is >= 1 and <= 4 ? shape : shape == 6 ? 1 : 0);
                for (int y = 50 + altitude; y < 60 + altitude; y++)
                {
                    Tile water = Main.tile[x, y];
                    water.LiquidAmount = liquid == 0 ? (byte)0 : (byte)255;
                    water.LiquidType = liquid == 1 ? 0 : liquid == 2 ? 2 : 3;
                }
            }
            var state = new BodyState(left, bottom + altitude * 16, 2.4f, bottom == 960 ? 0 : 1.2f, bottom == 960,
                Wet: liquid != 0, LiquidKind: liquid == 1 ? 0 : liquid);
            var scratch = Scratch();
            BodyState predicted = SimulateTerrariaBody.Step(state, controls, MovementCapabilities.Basic);
            if (Scratch() != scratch) throw new InvalidOperationException("Prediction changed Collision scratch fields");
            BodyState actual = RunEngine(state, controls);
            checkedCases++;
            if (Vector2.Distance(predicted.Feet, actual.Feet) > .001f || MathF.Abs(predicted.Vx - actual.Vx) > .001f
                || MathF.Abs(predicted.Vy - actual.Vy) > .001f || predicted.Wet != actual.Wet
                || predicted.LiquidKind != actual.LiquidKind || predicted.StairFall != actual.StairFall
                || predicted.OnGround != actual.OnGround || predicted.CollideX != actual.CollideX)
            {
                if (failed++ < 12) EmitLedgerRows.Detail($"mismatch shape={shape} liquid={liquid} entry={state} controls={controls}; predicted={predicted}; engine={actual}");
            }
        }
        foreach (int previous in Enumerable.Range(0, 4))
        foreach (int current in Enumerable.Range(0, 4))
        {
            for (int x = 20; x < 35; x++)
            for (int y = 50; y < 60; y++)
            {
                Tile tile = Main.tile[x, y];
                tile.LiquidAmount = current == 0 ? (byte)0 : (byte)255;
                tile.LiquidType = current == 1 ? 0 : current == 2 ? 2 : 3;
            }
            var state = new BodyState(400, 925, 3, 1, false, Wet: previous != 0, LiquidKind: previous == 1 ? 0 : previous);
            BodyState predicted = SimulateTerrariaBody.Step(state, Controls.None, MovementCapabilities.Basic);
            BodyState actual = RunEngine(state, Controls.None);
            checkedCases++;
            if (predicted != actual)
            {
                failed++;
                EmitLedgerRows.Detail($"liquid transition {previous}->{current} mismatch: predicted={predicted} engine={actual}");
            }
        }
        Console.WriteLine($"engine motion: {checkedCases - failed}/{checkedCases} matched native NPC collision; {failed} mismatches; Collision scratch preserved");
        EmitLedgerRows.Measure(Instrument, "Movement", "native-collision-cases-matched", checkedCases - failed, "cases", "up",
            message: $"out of {checkedCases} shape, liquid, entry and control combinations");
        return failed;
    }

    internal const string Instrument = "engine-replay";

    /// <summary>
    /// The default suite, one named case per fixture. The name is the question the fixture answers,
    /// written as the sentence a person would say, because it is what the scoreboard prints and
    /// what <c>--case</c> matches against.
    ///
    /// Case granularity is the fixture file rather than the assertion. The plan's full migration
    /// wants a case per assertion site, about 1,180 of them across thirty-nine files, and most of
    /// those files belong to other lanes; this is the granularity reachable from here, and it is
    /// already enough for per-case selection, rerun-red and a scoreboard that names what moved.
    /// </summary>
    private static IEnumerable<(string Name, Func<int> Body)> DefaultCases() => new (string, Func<int>)[]
    {
        ("the orb fits every two-by-two gap and no one-by-one gap in any direction", VerifyOrbContact.SizeRule),
        ("the orb passes a one-tile diagonal step without ever overlapping a wall", VerifyOrbContact.DiagonalStep),
        ("contact pushes the orb out of a wall, kills the velocity into it and keeps the slide", VerifyOrbContact.PushOutAndSlide),
        ("the motor's observed motion is what the engine actually did", VerifyObservedMotion.Run),
        ("the god's-eye occurrence stream records what it claims", VerifyGodsEyeEvents.Run),
        ("a prepared comparison preserves its numbers", VerifyPreparedActivities.Run),
        ("each purpose family nominates its best child", VerifyFamilyOffers.Run),
        ("planned routes reach their goals", VerifyRoutes),
        ("a projectile flies the arc the solver predicted", VerifyProjectileMotion.Run),
        ("the threat sense reads danger from sealed chambers correctly", VerifyPersonalDanger.Run),
        ("the companion spawns, lives and is attached both ways", VerifyCompanionLifecycle.Run),
        ("a threat is anticipated from how it actually arrives", VerifyThreatAnticipation.Run),
        ("a captured escape gets the body out", VerifyCapturedEscape.Run),
        ("following responds to a player who departs", VerifyResponsiveFollowing.Run),
        ("recovery flight and protection admit only what may start them", VerifyFollowRecoveryAndProtection.Run),
        ("ore work breaks ore without excavating ordinary terrain", VerifyOreWork.Run),
        ("mining hops reach the vein", VerifyMiningHops.Run),
        ("gathering beside the player is cooperative rather than competing", VerifyGatheringCooperation.Run),
        ("remaining work is accounted to whoever did it", VerifyWorkAccounting.Run),
        ("a collected drop is claimed only for what arrived", VerifyCollectionContracts.Run),
        ("per-character preferences reach the brain", VerifyCompanionPreferences.Run),
        ("the seven activities are offered and chosen", VerifyCompanionActivities.Run),
        ("assistance is useful rather than merely nearby", VerifyUsefulAssistance.Run),
        ("the senses' lifecycle restores what it changed", VerifyObservationLifecycle.Run),
        ("a hunt makes progress toward its target", VerifyHuntProgress.Run),
        ("a firing position is one a shot actually solves from", VerifyFiringPosition.Run),
        ("a bounded stand search that ran out says so, and a stand is proved against the target's forecast", VerifyOfferValidity.Run),
        ("a hunt is admitted only where it can be executed", VerifyHuntAdmissibility.Run),
        ("an attack's outcome is the one the arsenal forecast", VerifyAttackOutcomes.Run),
        ("a movement failure is classified as what it was", VerifyMovementFailures.Run),
        ("a route proves it can come home as well as go", VerifyRoundTripEvidence.Run),
        ("combat keeps its purpose across a substituted enemy", VerifyCombatPurpose.Run),
        ("safety releases the body after the danger passes", VerifySafetyAftermath.Run),
        ("an assistance trip goes and returns", VerifyAssistanceTrips.Run),
        ("the light and reach senses answer in three values", VerifyLightAndReachSenses.Run),
        ("keeping company strolls without walking into hazards", VerifyCompanyLocalMotion.Run),
        ("a capability change invalidates what depended on it", VerifyCapabilityRevision.Run),
        ("a closed door is opened rather than treated as a wall", VerifyDoorPassage.Run),
        ("courtesy stillness does not depend on what ran before", VerifyCourtesy.Run),
        ("downing and revival keep life on the NPC", VerifyDowningAndRevival.Run),
        ("the companion's stats mirror the player's", VerifyStatMirroring.Run),
        ("a whole journey is recorded against its proven ticks", VerifyTravelEpisodes.Run),
    };

    internal static BodyState RunEngine(BodyState state, Controls controls)
    {
        var npc = new NPC
        {
            width = BodyPhysics.Width, height = BodyPhysics.Height, type = 0,
            aiStyle = controls.FallThrough ? 10 : -1, lavaImmune = true, wetCount = 2,
            position = new Vector2(state.Left, state.Bottom - BodyPhysics.Height), velocity = new Vector2(state.Vx, state.Vy),
            wet = state.Wet, honeyWet = state.LiquidKind == 2, shimmerWet = state.LiquidKind == 3,
            lavaWet = state.LiquidKind == 1, stairFall = state.StairFall,
            waterMovementSpeed = .5f, lavaMovementSpeed = .5f, honeyMovementSpeed = .25f, shimmerMovementSpeed = .375f
        };
        Invoke(npc, "UpdateNPC_UpdateGravity");
        BodyState driven = MovementAbilities.ApplyControls(state, controls, MovementCapabilities.Basic);
        npc.velocity = new Vector2(driven.Vx, driven.Vy);
        if (npc.velocity.Y == 0 && !controls.FallThrough)
            Collision.StepDown(ref npc.position, ref npc.velocity, npc.width, npc.height, ref npc.stepSpeed, ref npc.gfxOffY);
        if (npc.velocity.Y >= 0)
            Collision.StepUp(ref npc.position, ref npc.velocity, npc.width, npc.height, ref npc.stepSpeed, ref npc.gfxOffY, 1, !controls.Descend, 1);
        npc.velocity.Y = MathF.Min(npc.velocity.Y + npc.gravity, npc.maxFallSpeed);
        if (MathF.Abs(npc.velocity.X) < .005f) npc.velocity.X = 0;
        Invoke(npc, "UpdateCollision");
        return driven with { Left = npc.position.X, Bottom = npc.Bottom.Y, Vx = npc.velocity.X, Vy = npc.velocity.Y,
            OnGround = npc.velocity.Y == 0, CollideX = npc.collideX, Wet = npc.wet,
            LiquidKind = npc.shimmerWet ? 3 : npc.honeyWet ? 2 : npc.lavaWet ? 1 : 0, StairFall = npc.stairFall };
    }

    private static void Invoke(NPC npc, string method) => typeof(NPC).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(npc, null);
    private static (bool, bool, bool, bool, bool, bool, bool) Scratch() =>
        (Collision.up, Collision.down, Collision.stair, Collision.stairFall, Collision.honey, Collision.shimmer, Collision.sloping);

    private static int VerifyRoutes()
    {
        int failed = 0;
        foreach (string name in new[] { "flat", "two-tile-ledge", "stairs-up", "stairs-down" })
        {
            Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, new object[] { (ushort)100, (ushort)100 }, null)!;
            for (int x = 5; x < 95; x++)
            {
                Tile tile = Main.tile[x, 90]; tile.HasTile = true; tile.TileType = 1;
            }
            Point from = new(30, 89), to = new(45, 89);
            if (name == "two-tile-ledge")
            {
                for (int x = 35; x < 65; x++)
                for (int y = 88; y < 90; y++)
                {
                    Tile tile = Main.tile[x, y]; tile.HasTile = true; tile.TileType = 1;
                }
                to = new Point(40, 87);
            }
            if (name.StartsWith("stairs"))
            {
                for (int x = 35; x <= 40; x++)
                {
                    Tile tile = Main.tile[x, 89 - (x - 35)]; tile.HasTile = true; tile.TileType = 19; tile.Slope = (Terraria.ID.SlopeType)2;
                }
                to = new Point(40, 83);
                if (name == "stairs-down") (from, to) = (to, from);
            }
            TerrainChanges.Reset();
            NavGrid.World = new GameTileWorld();
            BodyPhysics.Pose? pose = NavGrid.StandAt(from.X, from.Y, false);
            if (pose == null) { EmitLedgerRows.Detail($"native route {name}: no start pose"); failed++; continue; }
            BodyState live = BodyState.Standing(pose.Value);
            var movement = new CoordinateMovement();
            int tick;
            for (tick = 0; tick < 1800; tick++)
            {
                movement.Configure((uint)tick, false, true);
                Controls controls = movement.MoveTo(live, NavGrid.FeetWorld(to));
                live = RunEngine(live, controls);
                if (movement.Navigator.Arrived) break;
            }
            bool pass = movement.Navigator.Arrived;
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")} native route {name}: {tick} ticks, {movement.Navigator.FaultCount} faults, final {live.FeetTile}");
            if (!pass) failed++;
        }
        return failed;
    }
}
