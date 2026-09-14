extern alias live;

using System.Reflection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using Microsoft.Xna.Framework;
using Terraria;

using FollowPlayerObjective = live::AICompanion.Companion.Brain.Infrastructure.Position.FollowPlayerObjective;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;

/// <summary>
/// Exercises the production brain through its live alias. The player is moved directly because
/// this fixture measures the brain's response to observed motion, not Terraria's player physics;
/// companion controls still pass through the native NPC collision adapter every tick.
/// </summary>
/// <summary>
/// The screen the game will actually build the intent region at, declared for the duration of a
/// fixture and put back afterwards.
///
/// <para>The region's lead is clamped to half the player's own screen, with a fixed floor so that a
/// headless run — where <c>Main.screenWidth</c> is zero — does not pin the region to his feet and
/// pass every row for the reason the region exists to remove. That floor is smaller than any screen
/// anyone plays at, so a fixture that declares nothing measures a systematically shorter-leading
/// region than play: every clamped quantity, and every number derived from one, was being taken
/// under a lead the game never uses. A fixture that cares about the region therefore states the
/// screen it means rather than inheriting the floor.</para>
///
/// <para>Restoring is not tidiness. <c>Main.screenWidth</c> is process-global and two other readers
/// take it: the light field sizes its sampling window on it, and hunting's on-screen rule is a
/// rectangle grown from it. A fixture that widened the screen and walked away would quietly enlarge
/// the light window and the hunt's admissible area for every fixture ordered after it in the same
/// process, which is a cost and a behaviour change arriving with nothing in the scene to explain
/// them.</para>
/// </summary>
internal readonly struct PlaySizedScreen : IDisposable
{
    // A common desktop size, and the point is only that it is a real one: any screen wider than
    // about 1374px clamps the lead further out than the headless floor does, so the floor and the
    // play clamp stop agreeing there and everything measured under the floor stops transferring.
    public const int Width = 1920;
    public const int Height = 1080;

    private readonly int width;
    private readonly int height;

    private PlaySizedScreen(int width, int height) { this.width = width; this.height = height; }

    public static PlaySizedScreen Declare()
    {
        var held = new PlaySizedScreen(Main.screenWidth, Main.screenHeight);
        Main.screenWidth = Width;
        Main.screenHeight = Height;
        return held;
    }

    public void Dispose()
    {
        Main.screenWidth = width;
        Main.screenHeight = height;
    }
}

internal static class VerifyResponsiveFollowing
{
    public static int Run()
    {
        // Every row below reads the intent region, directly or through the brain, so the whole
        // fixture runs at the screen the game builds that region at rather than at the headless floor.
        using var screen = PlaySizedScreen.Declare();
        VerifyLocalMotionDoesNotBecomeTravel();
        VerifyIntentEvidenceAndRevision();
        VerifyCompanyMethodsShareOneActivity();
        VerifyTwoAxisObjective();
        VerifyArrivalSlackCannotStrandFollowing();
        VerifyVerticalPlayerMotionReachesTheProductionFollowAction();
        VerifyOccludedPlayerStillProvidesADestination();
        VerifyCturnCompletesThroughTheProductionBrain();
        VerifyRecentActivityChangesTheMeetingPlace();
        VerifyMeetingPlacesFollowTheCompanionsOwnRoutes();
        VerifyAnUnfinishedMeetingFloodKeepsItsProgress();
        VerifyADroppedMeetingPlaceIsNotWalkedTo();
        VerifyAnAirborneTickCannotSatisfyFollowing();
        VerifyAStoppedPlayerDoesNotOscillateTheMethod();
        // The two narrow region rows run before the whole-walk one. Assertions here throw, so the
        // first failure takes every row after it: ordering the specific rows first means a change
        // that breaks the pull is reported as the pull rather than as a walk that came in low, and
        // the walk's own row is not the only thing anybody sees.
        VerifyATravellingPlayerIsKeptUpWithFromInsideTheRegion();
        VerifyTheReunionCurveHasNoStepAtTheRegionsEdge();
        VerifyTheCompanionLeadsATravellingPlayer();
        VerifyAClimbingPlayerGrowsTheRegionUpwards();
        VerifyAGroundedTickOnSlopedGroundEntersSatisfaction();
        Console.WriteLine("responsive following: vertical intent, two-axis arrival, live brain follow selection, activity-dependent meeting places, route-priced reunion, retained meeting floods, dropped meeting places, leading a travelling player, a reunion curve with no step at the region's edge, a settled companion keeping up with a travelling player, a region that grows up a hill, grounded satisfaction and a settling stop passed");
        return 0;
    }

    private static void VerifyLocalMotionDoesNotBecomeTravel()
    {
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(400, 1000);
        var sense = companion.Brain.Senses.Player;
        for (int tick = 0; tick < 240; tick++)
        {
            player.velocity = new Vector2(tick % 60 < 30 ? 4 : -4, 0);
            player.position += player.velocity;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            sense.Update(player, companion.NPC);
        }
        Require(!sense.IsTravelling,
            $"repeated motion inside one local area must not become confident travel: intent={sense.Intent}");
    }

    private static void VerifyCompanyMethodsShareOneActivity()
    {
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        var brain = companion.Brain;
        Player player = Main.LocalPlayer;
        player.dead = false;
        player.velocity = Vector2.Zero;
        companion.NPC.Bottom = player.Bottom = new Vector2(400, 1280);
        var company = brain.Chooser.Actions.OfType<live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany>().Single();
        Require(!brain.Chooser.Actions.Any(a => a.Name is "walk-with" or "wander"), "obsolete companionship candidates remain registered");
        brain.Chooser.Actions.RemoveAll(a => !ReferenceEquals(a, company));
        var context = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, brain.Senses);
        brain.Senses.Update(companion.NPC, player, companion.Breath);
        Require(brain.Chooser.Choose(context) == company && company.Score() > 0, "company must be a positive ordinary offer while nearby");
        long identity = brain.Chooser.Activity.Id;
        bool rested = false, strolled = false;
        var random = Main.rand;
        Main.rand = new Terraria.Utilities.UnifiedRandom(1729);
        try
        {
            for (int tick = 0; tick < 2400; tick++)
            {
                var request = company.Execute(context);
                // Resolved every tick as the brain does: a stroll goal must lie in the positioner's returnable region, and that
                // region grows only on the resolver's own cadence, so executing without resolving leaves it at its first slice.
                brain.Positioner.Resolve(request, brain.Senses, null);
                rested |= request.Kind == RequestKind.Hold;
                strolled |= request.Kind == RequestKind.Exact;
                Require(request.Kind != RequestKind.WithPlayer, "calm co-location should not keep requesting reunion");
            }
        }
        finally { Main.rand = random; }
        Require(rested && strolled, $"company must preserve both resting and nearby movement methods; rested={rested} strolled={strolled} "
            + $"returnable={brain.Positioner.ReturnableCount} reach={brain.Positioner.ReachCount} complete={brain.Positioner.ReachComplete}");
        player.Bottom += new Vector2(480, 0);
        brain.Senses.Update(companion.NPC, player, companion.Breath);
        Require(brain.Chooser.Choose(context) == company && company.Execute(context).Kind == RequestKind.WithPlayer,
            "departure must switch the same company activity to reunion");
        Require(brain.Chooser.Activity.Id == identity, "a company method change must not create a new purpose");
        var stranded = context with { Stranded = true };
        company.Prepare(stranded);
        Require(company.Execute(stranded).Kind == RequestKind.Roam, "sealed-pocket company must retain its local roaming method");
    }

    private static void VerifyIntentEvidenceAndRevision()
    {
        var travel = new live::AICompanion.Companion.Brain.Infrastructure.Observation.InferPlayerActivity();
        var working = new live::AICompanion.Companion.Brain.Infrastructure.Observation.InferPlayerActivity();
        Vector2 position = Vector2.Zero;
        ulong tick = 0;
        travel.Observe(position, Vector2.Zero, false, false, tick);
        working.Observe(position, Vector2.Zero, true, false, tick);
        void Move(Vector2 velocity, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                position += velocity;
                tick++;
                travel.Observe(position, velocity, false, false, tick);
                working.Observe(position, velocity, true, false, tick);
            }
        }
        Move(new Vector2(4, 0), 120);
        Require(travel.Interpretation == "travelling" && travel.Confidence > .9f && travel.Travel.X > 3,
            "sustained displacement must establish travel with supported confidence");
        Require(working.Confidence < travel.Confidence && working.Travel.X > 0,
            "local work must reduce travel certainty without vetoing all movement");
        Vector2 before = travel.Travel;
        int samples = travel.Samples;
        travel.Observe(position + new Vector2(999, 0), new Vector2(-20, 0), false, false, tick);
        Require(travel.Travel == before && travel.Samples == samples, "duplicate observation cannot advance evidence");
        Move(new Vector2(-4, 0), 6);
        Require(travel.Travel.X > 0, "a brief reversal must not invent a new journey immediately");
        Move(new Vector2(-4, 0), 120);
        Require(travel.Travel.X < -3 && travel.Confidence > .9f, "sustained backtracking must replace old intent");
        Move(Vector2.Zero, 120);
        Require(travel.Travel == Vector2.Zero && travel.Interpretation == "paused", "a sustained pause must clear travel");
        Move(new Vector2(0, -4), 120);
        Require(travel.Travel.Y < -3, "vertical travel must use the same evidence as horizontal travel");
        travel.Observe(position + new Vector2(1000, 0), Vector2.Zero, false, false, ++tick);
        Require(travel.Samples == 0 && travel.Confidence == 0, "position correction is not travel evidence");
        travel.Observe(position, Vector2.Zero, false, false, tick + 50);
        Require(travel.Samples == 0, "an unobserved interval cannot become a traversed path");
    }

    /// <summary>A settled objective over a region centred where the test wants it, so the geometry rules
    /// can be exercised without driving a body for a rescore first. Settled is what an airborne body does
    /// not have, and the fixture that cares about that says so by asking for it false.</summary>
    private static FollowPlayerObjective ObjectiveAt(Vector2 centre, Vector2 anchor, bool settled = true, bool grounded = true)
    {
        float scale = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current.FollowComfortScale;
        var region = new live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerIntentRegion(centre,
            new Vector2(live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.FollowHorizontalComfort * scale,
                live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.FollowVerticalComfort * scale),
            Vector2.Zero, IsTravelling: false);
        return new FollowPlayerObjective(region, anchor, settled, grounded);
    }

    private static void VerifyTwoAxisObjective()
    {
        var objective = ObjectiveAt(new Vector2(480, 800), new Vector2(480, 752));
        Require(!objective.IsSatisfied(new Vector2(480, 1120), locallyConnected: true),
            "a companion directly below the player on another floor must not satisfy following");
        Require(!objective.AcceptsDestination(new Vector2(480, 1120), locallyConnected: true),
            "a different-floor incumbent must not remain a valid following destination");
        Require(objective.AcceptsDestination(new Vector2(640, 752), locallyConnected: true),
            "a nearby standable point in the predicted player region remains a valid following destination");
        Require(!objective.IsSatisfied(new Vector2(480, 800), locallyConnected: false),
            "a nearby but sealed floor must not satisfy following without a local connection");
        Require(!ObjectiveAt(new Vector2(480, 800), new Vector2(480, 752), settled: false)
                .IsSatisfied(new Vector2(480, 800), locallyConnected: true),
            "a body standing at the region's own centre must not satisfy following before it has settled there");
    }

    private static void VerifyArrivalSlackCannotStrandFollowing()
    {
        // A destination inside the comfort box but inside the navigator's stopping radius of its
        // edge must not be admitted, or the body stops short and following never satisfies.
        var objective = ObjectiveAt(new Vector2(500, 1280), new Vector2(500, 1280));
        Require(!objective.AcceptsDestination(new Vector2(500 + objective.HorizontalComfort - 6f, 1280), true),
            "follow destination must reserve the navigator's stopping radius");
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(500 - player.width / 2f, 1280 - player.height);
        player.velocity = Vector2.Zero;
        companion.NPC.position = new Vector2(698.56f - companion.NPC.width / 2f, 1280 - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;
        bool arrived = false;
        for (int tick = 0; tick < 240; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI();
            AdvanceNative(companion);
            arrived = objective.IsSatisfied(companion.NPC.Bottom, true);
            if (arrived) break;
        }
        Require(arrived, $"the recorded boundary stall must finish following through the production brain: feet={companion.NPC.Bottom}; action={companion.Brain.LastAction?.Name}; goal={companion.Brain.Positioner.Chosen}; status={companion.Brain.Navigator.Status}; control={companion.Motor.AppliedControls}");
    }

    private static void VerifyVerticalPlayerMotionReachesTheProductionFollowAction()
    {
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(480, 758);
        companion.NPC.position = new Vector2(480, 1238);
        companion.NPC.velocity = Vector2.Zero;

        bool sawVerticalIntent = false;
        for (int tick = 0; tick < 12; tick++)
        {
            player.velocity = new Vector2(0, -4);
            player.position += player.velocity;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI();
            sawVerticalIntent |= companion.Brain.Senses.Player.Intent.Y < -1.2f;
            AdvanceNative(companion);
        }

        Require(sawVerticalIntent, "vertical-only player movement must produce travel intent");
        Require(companion.Brain.LastRequest.Kind == RequestKind.WithPlayer,
            "the production chooser must keep a vertically moving player in the follow action");
        Require(!companion.Brain.Positioner.FollowObjectiveSatisfied
            && companion.Brain.Positioner.FollowVerticalGap > 0f,
            "the live positioner must report unsatisfied vertical follow progress instead of accepting the lower floor");
    }

    private static void VerifyCturnCompletesThroughTheProductionBrain()
    {
        BuildCturn();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(70 * 16, 80 * 16 - player.height);
        companion.NPC.position = new Vector2(50 * 16, 70 * 16 - BodyPhysics.Height);
        companion.NPC.velocity = Vector2.Zero;

        bool initiallyMovedAway = false;
        bool arrived = false;
        for (int tick = 0; tick < 720; tick++)
        {
            // The observed player is travelling right, so the leftward first control cannot be a
            // travel-bias artefact: the only useful route leaves the pocket to the left.
            player.velocity = new Vector2(1f, 0f);
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI();
            initiallyMovedAway |= companion.Motor.AppliedControls.MoveX < 0f;
            Require(!companion.Brain.Navigator.SearchPending || !companion.Brain.Navigator.LastPlanFailed,
                "an unfinished retained search must not be classified failed before it publishes a prefix");
            Require(companion.Brain.Navigator.Path is not { Finished: false } || !companion.Brain.Navigator.LastPlanFailed,
                "a usable route prefix must not be classified as a failed plan");
            AdvanceNative(companion);
            // Selection may already yield to idle after arrival, clearing the positioner's
            // request-scoped flag. The contract is the actual body reaching the usable floor.
            // The live sense's own objective, so the fixture judges arrival by exactly what the brain
            // judges it by — including the grounded settle, which is the point of the change.
            arrived = companion.Brain.Senses.Intent.Objective.IsSatisfied(companion.NPC.Bottom,
                Collision.CanHitLine(companion.NPC.position, companion.NPC.width, companion.NPC.height,
                    player.position, player.width, player.height));
            if (arrived)
                break;
        }
        Require(initiallyMovedAway,
            "the production follow route must accept the initially-away first leg of a C-turn");
        Require(arrived,
            $"the production brain must complete the C-turn at the player's usable floor rather than hold its start pocket; feet={companion.NPC.Bottom}, action={companion.Brain.LastAction?.Name}, spot={companion.Brain.Positioner.Chosen}, status={companion.Brain.Navigator.Status}, stop={companion.Brain.Navigator.LastSearchStop}");
    }

    private static void VerifyOccludedPlayerStillProvidesADestination()
    {
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(54 * 16, 80 * 16 - player.height);
        companion.NPC.position = new Vector2(46 * 16, 80 * 16 - companion.NPC.height);
        for (int y = 77; y < 80; y++)
        {
            Tile door = Main.tile[50, y];
            door.HasTile = true;
            door.TileType = Terraria.ID.TileID.ClosedDoor;
            door.TileFrameY = (short)((y - 77) * 18);
        }
        Main.tileSolid[Terraria.ID.TileID.ClosedDoor] = true;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        Require(!Collision.CanHitLine(companion.NPC.position, companion.NPC.width, companion.NPC.height,
            player.position, player.width, player.height), "closed-door fixture must occlude the player");
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        companion.AI();
        Require(companion.Brain.LastRequest.Kind == RequestKind.WithPlayer,
            "nearby occluded player must still request following");
        Require(companion.Brain.Positioner.Chosen is Vector2 goal && goal.X > 50 * 16,
            "a closed door must not veto player-side follow destinations");
    }

    /// <summary>
    /// The player ends on the same tile in four scenes whose recent activity differs, with the companion
    /// ten tiles to its right on one floor. Walking right, the player will pass the companion, so the
    /// priced meeting place lies on the journey ahead and the companion waits there; placing torches in
    /// place, the player is met where they stand; a brief turn keeps the journey; having turned round for
    /// good, the player is heading away and is met at or behind where they stand. The pairs come from the
    /// proposal's intent-to-destination acceptance and exercise the production observer and selector.
    /// </summary>
    private static void VerifyRecentActivityChangesTheMeetingPlace()
    {
        var travel = MeetingAfter(new[] { (3f, 120) }, placingTorches: false);
        var torches = MeetingAfter(Enumerable.Repeat(new[] { (2f, 10), (-2f, 10) }, 6).SelectMany(s => s).ToArray(), placingTorches: true);
        var brief = MeetingAfter(new[] { (3f, 110), (-3f, 10) }, placingTorches: false);
        var backtrack = MeetingAfter(new[] { (3f, 30), (-3f, 120) }, placingTorches: false);
        string ledger = string.Join("; ", new[] { ("travel", travel), ("torches", torches), ("brief", brief), ("backtrack", backtrack) }
            .Select(s => $"{s.Item1}: reason={s.Item2.Reason} anchorX={s.Item2.Anchor.X / 16:0.0} playerX={s.Item2.PlayerX / 16:0.0}"));
        Require(new[] { travel, torches, brief, backtrack }.All(s => MathF.Abs(s.PlayerX - 40 * 16) < 2f),
            $"every scene must end with the player on the same tile, or the pairs compare positions rather than activity; {ledger}");
        // The property is that the anchor lies ahead on the journey; which of the two mechanisms put
        // it there is not the contract. A priced place that does not reach at least as far along the
        // travel direction as the intent region's centre now loses to the region's leading edge, which
        // is further ahead rather than less far, so both answers satisfy the scene and the fixture
        // names the property instead of the winner.
        Require(travel.Reason is "meeting-ahead-priced" or "meeting-place-outside-intent-region"
                && travel.Anchor.X >= travel.PlayerX + 3 * 16,
            $"a player walking towards the companion is met on the journey ahead; {ledger}");
        Require(torches.Reason == "player-not-travelling" && MathF.Abs(torches.Anchor.X - torches.PlayerX) < 16f,
            $"a player placing torches in place is met where they stand; {ledger}");
        Require(brief.Anchor.X >= brief.PlayerX + 3 * 16,
            $"a brief reversal must not replace the journey's meeting place; {ledger}");
        Require(backtrack.Anchor.X <= backtrack.PlayerX + 16f,
            $"sustained backtracking replaces the journey, so the meeting place is no longer ahead towards the companion; {ledger}");
    }

    private static (Vector2 Anchor, string Reason, float PlayerX) MeetingAfter((float Vx, int Ticks)[] script, bool placingTorches)
    {
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        float travelled = script.Sum(s => s.Vx * s.Ticks);
        player.position = new Vector2(40 * 16 - travelled - player.width / 2f, 80 * 16 - player.height);
        companion.NPC.position = new Vector2(50 * 16 - companion.NPC.width / 2f, 80 * 16 - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;
        Item held = player.inventory[player.selectedItem];
        if (placingTorches) held.SetDefaults(Terraria.ID.ItemID.Torch); else held.TurnToAir();
        foreach (var (vx, ticks) in script)
            for (int i = 0; i < ticks; i++)
            {
                player.velocity = new Vector2(vx, 0f);
                player.position += player.velocity;
                player.itemAnimation = placingTorches ? 10 : 0;
                VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
                companion.Brain.Senses.Update(companion.NPC, player, companion.Breath);
            }
        player.itemAnimation = 0;
        held.TurnToAir();
        var meeting = companion.Brain.Meeting;
        // The observation is frozen here; later calls only let the companion's flood settle.
        for (int i = 0; i < 60; i++)
        {
            meeting.Resolve(companion.NPC.Bottom, companion.Brain.Senses.Player, companion.Brain.Senses.Intent.Region, Main.GameUpdateCount + (ulong)i);
            if (meeting.Reason is not ("meeting-undecided" or "retained-while-undecided")) break;
        }
        return (meeting.Destination, meeting.Reason, player.Bottom.X);
    }

    /// <summary>
    /// The player walks right along an upper floor while the companion stands on the floor beneath it.
    /// In one world the lower floor rises to the player's journey through a gap ahead; in the other it
    /// ends at a cliff, and the only way up is the ledge behind. The gap sits far enough ahead that the
    /// region around a point extrapolated from the player's velocity is nearer by route through the ledge
    /// behind: on this geometry that anchor turned the companion back and reunited at tick 349, where the
    /// priced meeting place goes forward and reunites by about tick 265 (probe of gaps at 62, 70, 76 and
    /// 82 tiles; at 62 both went forward, so a nearer gap cannot tell them apart). In the cliff world
    /// both go back to the ledge. Reunion must complete in both through the production brain and native
    /// collision.
    /// </summary>
    private static void VerifyMeetingPlacesFollowTheCompanionsOwnRoutes()
    {
        foreach (int gapAt in new[] { 76, -1 })
        {
            bool reconnects = gapAt > 0;
            BuildParallelRoutes(gapAt);
            var companion = VerifyCompanionLifecycle.Create();
            companion.Brain.Chooser.Actions.RemoveAll(a => a.Name != "keep-company");
            Player player = Main.player[0];
            player.dead = false;
            player.position = new Vector2(30 * 16, 76 * 16 - player.height);
            player.velocity = Vector2.Zero;
            companion.NPC.position = new Vector2(35 * 16, 80 * 16 - companion.NPC.height);
            companion.NPC.velocity = Vector2.Zero;
            for (int tick = 0; tick < 30; tick++)
            {
                player.velocity = new Vector2(3f, 0f);
                player.position += player.velocity;
                VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
                companion.Brain.Senses.Update(companion.NPC, player, companion.Breath);
            }
            int decidedAt = -1, lastTick = 0;
            string decision = "";
            float anchorX = 0, playerX = 0, startX = 0, moved = 0;
            bool arrived = false;
            for (int tick = 0; tick < 900; tick++)
            {
                lastTick = tick;
                player.velocity = player.Bottom.X < 88 * 16 ? new Vector2(3f, 0f) : Vector2.Zero;
                player.position += player.velocity;
                VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
                companion.AI();
                AdvanceNative(companion);
                var meeting = companion.Brain.Meeting;
                if (decidedAt < 0 && meeting.Reason is "meeting-ahead-priced" or "player-position-priced")
                {
                    decidedAt = tick; decision = meeting.Reason;
                    anchorX = meeting.Destination.X; playerX = player.Bottom.X; startX = companion.NPC.Bottom.X;
                }
                if (decidedAt >= 0 && tick == decidedAt + 60) moved = companion.NPC.Bottom.X - startX;
                arrived = companion.Brain.Senses.Intent.Objective.IsSatisfied(companion.NPC.Bottom,
                    Collision.CanHitLine(companion.NPC.position, companion.NPC.width, companion.NPC.height,
                        player.position, player.width, player.height));
                if (arrived && decidedAt >= 0 && tick > decidedAt + 60) break;
            }
            string evidence = $"reconnects={reconnects}; decision={decision} at tick {decidedAt}; anchorX={anchorX / 16:0.0}; playerX={playerX / 16:0.0}; "
                + $"moved {moved / 16:0.0} tiles in the 60 ticks after deciding; arrived={arrived} by tick {lastTick}; meeting={companion.Brain.Meeting.Reason}; "
                + $"feet={companion.NPC.Bottom}; player={player.Bottom}; goal={companion.Brain.Positioner.Chosen}; status={companion.Brain.Navigator.Status}";
            Require(decidedAt >= 0, $"a travelling player's meeting place must be priced from the companion's routes; {evidence}");
            if (reconnects)
                Require(decision == "meeting-ahead-priced" && anchorX > playerX + 8 * 16 && moved > 3 * 16,
                    $"a lower route that rises to the player's journey ahead must be taken forward to meet it; {evidence}");
            else
                Require(moved < -3 * 16,
                    $"a lower route that ends at a cliff must be abandoned for the way up behind; {evidence}");
            Require(arrived, $"reunion must complete through the production brain; {evidence}");
            Console.WriteLine($"meeting place {(reconnects ? "gap ahead" : "cliff")}: {decision} at tick {decidedAt}, anchor {(anchorX - playerX) / 16:+0.0;-0.0} tiles from the player, "
                + $"companion moved {moved / 16:+0.0;-0.0} tiles in 60 ticks, reunited by tick {lastTick}");
        }
    }

    /// <summary>
    /// The companion's flood keeps its progress while the body moves inside the region it has proven it can
    /// return from. Restarting it whenever the re-root cadence passed meant a flood needing more than one
    /// cadence never finished, so reunion in a large cave never got a priced place. Every call here arrives
    /// after the cadence with the body one tile along, which restarted the old flood on each call. While
    /// nothing is decided the anchor is the bounded continuation of the player's travel rather than their
    /// current feet, which a travelling player has already left.
    /// </summary>
    private static void VerifyAnUnfinishedMeetingFloodKeepsItsProgress()
    {
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        try
        {
            BuildParallelRoutes(76);
            var companion = VerifyCompanionLifecycle.Create();
            Player player = Main.player[0];
            player.dead = false;
            player.position = new Vector2(30 * 16, 76 * 16 - player.height);
            companion.NPC.position = new Vector2(35 * 16, 80 * 16 - companion.NPC.height);
            companion.NPC.velocity = Vector2.Zero;
            for (int tick = 0; tick < 30; tick++)
            {
                player.velocity = new Vector2(3f, 0f);
                player.position += player.velocity;
                VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
                companion.Brain.Senses.Update(companion.NPC, player, companion.Breath);
            }
            var meeting = companion.Brain.Meeting;
            var sense = companion.Brain.Senses.Player;
            ulong start = Main.GameUpdateCount + 1;
            int cadence = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.MeetingRerootTicks;
            var region = companion.Brain.Senses.Intent.Region;
            Vector2 first = meeting.Resolve(companion.NPC.Bottom, sense, region, start);
            // The undecided anchor is the intent region's leading edge now, not a second extrapolation
            // of the player's velocity with a lead time of its own.
            Vector2 continuation = region.LeadingEdge;
            Require(meeting.Reason == "meeting-undecided",
                $"one slice must not finish the flood, or nothing about retaining its progress is tested; reason={meeting.Reason} flood={meeting.FloodState}");
            Require(Vector2.Distance(first, continuation) < 1f && MathF.Abs(first.X - sense.Bottom.X) >= 16f,
                $"an undecided meeting place must aim at the player's continued travel, not the feet they have left; anchor={first} continuation={continuation} feet={sense.Bottom}");
            int calls = 1;
            for (; calls < 200 && meeting.Reason == "meeting-undecided"; calls++)
                meeting.Resolve(companion.NPC.Bottom + new Vector2(calls % 2 * 16, 0), sense, region, start + (ulong)(calls * (cadence + 1)));
            // The property is that the flood finished and a place was decided from it. Every one of
            // these three reasons is only reachable after a decision was taken, the third being a
            // decision the intent region then overruled because the priced tile lay outside it, so
            // all three are evidence that the progress was kept across the slices.
            Require(meeting.Reason is "meeting-ahead-priced" or "player-position-priced" or "meeting-place-outside-intent-region",
                $"an unfinished flood must keep its progress while the body stays in the region it can return from; reason={meeting.Reason} after {calls} slices, flood={meeting.FloodState}");
            Console.WriteLine($"meeting flood: {meeting.Reason} after {calls} slices, each past the re-root cadence with the body moved; flood {meeting.FloodState}");
        }
        finally { live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false; }
    }

    /// <summary>
    /// A meeting place reaches the positioner as an exact tile. When reunion drops it, because the flood
    /// re-rooted or the player stopped, the next request asks for the region around a different anchor, and
    /// the old tile must not survive the rescore cadence as the destination: it can lie behind a player who
    /// has moved on.
    /// </summary>
    private static void VerifyADroppedMeetingPlaceIsNotWalkedTo()
    {
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(30 * 16 - player.width / 2f, 80 * 16 - player.height);
        companion.NPC.position = new Vector2(40 * 16 - companion.NPC.width / 2f, 80 * 16 - companion.NPC.height);
        companion.Brain.Senses.Update(companion.NPC, player, companion.Breath);
        var positioner = companion.Brain.Positioner;
        Vector2 place = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.FeetWorld(new Point(80, 79));
        positioner.Resolve(new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(RequestKind.WithPlayer, place, MeetingPlace: true),
            companion.Brain.Senses, null);
        Require(positioner.Chosen is Vector2 held && Vector2.Distance(held, place) < 1f && positioner.ChoiceReason == "priced-meeting-place",
            $"a priced meeting place must first become the exact destination, or dropping it tests nothing; chosen={positioner.Chosen} reason={positioner.ChoiceReason}");
        positioner.Resolve(new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(RequestKind.WithPlayer, player.Bottom),
            companion.Brain.Senses, null);
        Require(positioner.Chosen is not Vector2 kept || Vector2.Distance(kept, place) >= 16f,
            $"a dropped meeting place must not remain the destination until the next rescore; chosen={positioner.Chosen} reason={positioner.ChoiceReason} place={place}");
    }

    /// <summary>
    /// The row the owner looks at first. On an authored straight walk the signed offset along the
    /// player's travel direction is measured on every tick the player actually moves, and the
    /// companion must be ahead on more than half of them. A follow region centred on the player's
    /// current feet cannot pass this at any comfort size: its gradient inside is zero, so the body
    /// coasts to whatever edge it entered by and stays there, which on the 2026-09-14 capture left
    /// it behind by more than three tiles on 71.5% of moving rows and ahead by four on 4.7%.
    /// </summary>
    private static void VerifyTheCompanionLeadsATravellingPlayer()
    {
        // A long floor, because the margin is real and small: the companion walks at the player's own
        // run speed times CompanionWalkPace, so it closes a gap at a fraction of a pixel a tick and a
        // short walk measures the catch-up rather than the lead. The player travels below his own top
        // speed, which is what walking across a cave looks like; a player sprinting at exactly his cap
        // is the one case a body a tenth faster can only just stay level with.
        BuildLongFloor();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.Bottom = new Vector2(240f, 1280f);
        player.velocity = Vector2.Zero;
        companion.NPC.Bottom = new Vector2(240f, 1280f);
        companion.NPC.velocity = Vector2.Zero;
        const float PlayerSpeed = 2.4f;
        int ahead = 0, moving = 0;
        float worstBehind = 0f, total = 0f;
        for (int tick = 0; tick < 1200; tick++)
        {
            player.velocity = new Vector2(PlayerSpeed, 0f);
            player.Bottom += player.velocity;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI();
            AdvanceNative(companion);
            // The first ticks are the intent history filling: the player has not yet been observed
            // travelling, so there is nothing to lead. Measure once travel is established.
            if (!companion.Brain.Senses.Player.IsTravelling) continue;
            float signed = (companion.NPC.Bottom.X - player.Bottom.X) * MathF.Sign(PlayerSpeed);
            moving++;
            total += signed;
            if (signed > 0f) ahead++;
            if (signed < worstBehind) worstBehind = signed;
        }
        float share = moving == 0 ? 0f : ahead / (float)moving;
        // Printed rather than only asserted, because the number is the row the owner reads and a pass
        // that says only "passed" cannot be compared with the capture it was measured against.
        Console.WriteLine(FormattableString.Invariant(
            $"leading a travelling player: ahead on {ahead}/{moving} moving rows ({share:P1}), mean signed offset {total / MathF.Max(1, moving):F1}px, worst behind {worstBehind:F1}px"));
        Require(moving > 200, $"the walk must establish travel for most of its length: travelling rows={moving}");
        // The floor of the band this walk actually produces, not a round number below it. At "more
        // than half" the row was green with the central pull deleted — the lead alone carried 57.1%
        // of the rows, so the constant was live with nothing measuring it, which is the surviving
        // mutation this line exists to answer. The band with the pull in is 66-79%, so the line sits
        // at its floor: it is above anything the lead can reach on its own and below the worst
        // healthy run, which is the only placement that makes the row a test of the pull rather than
        // of the lead. Lowering it because a run came in under is how it stops being one.
        Require(share > 0.66f, FormattableString.Invariant(
            $"the companion must lead a travelling player on at least two thirds of the moving rows: ahead={ahead}/{moving} ({share:P1}), mean signed offset={total / MathF.Max(1, moving):F1}px, worst behind={worstBehind:F1}px"));
    }

    /// <summary>
    /// The reunion curve has no step anywhere, sampled a pixel at a time straight out through the
    /// region's own leading edge at the screen the game builds the region at.
    ///
    /// <para>The claim was made in prose and held only headless, where the clamp floor keeps the lead
    /// short enough that the two halves of the curve nearly meet by accident. At a play-sized screen
    /// the lead is longer, so a companion standing on the leading edge is further from the player's
    /// body — and the outside slope used to be measured on exactly that distance, so it was already
    /// partway up at the boundary where the inside gradient tops out, and the curve jumped. The jump
    /// grew with the lead, which is to say with the screen, which is why no headless row could see
    /// it. The slope is measured on the gap beyond the region now, so it starts at zero where the
    /// inside gradient finishes.</para>
    ///
    /// <para>Sampled rather than reasoned about: the assertion is that no one-pixel step along the
    /// curve exceeds what a one-pixel step can account for on either half, which is a bound on the
    /// steeper of the two gradients and not a number chosen to pass.</para>
    ///
    /// <para><b>The player runs, and that is the premise rather than a detail.</b> The step's size is
    /// the lead's length: measured body-to-body the slope reads the lead plus a half-width at the
    /// leading edge, and subtracting the region's own width leaves exactly the lead. So at a walking
    /// pace the old slope reached about 0.18 at the edge against a central pull of 0.25 and there was
    /// no step to find — planting the old rule back under a walking player leaves this row green,
    /// measured. It takes a lead near seven hundred pixels before the outside slope overtakes the
    /// inside gradient, which is a player with movement accessories on, and that lead only exists at
    /// all because the clamp is half a real screen: the headless floor caps it at a third of that and
    /// caps the defect with it. This row is therefore impossible to write without declaring the
    /// screen, and it asserts the clamp it is standing on so that it fails rather than quietly
    /// weakens if the declaration is ever removed.</para>
    /// </summary>
    private static void VerifyTheReunionCurveHasNoStepAtTheRegionsEdge()
    {
        BuildLongFloor();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.Bottom = new Vector2(240f, 1280f);
        companion.NPC.Bottom = new Vector2(240f, 1280f);
        companion.NPC.velocity = Vector2.Zero;
        // A running pace rather than a walking one. The lead is a duration times the player's own
        // observed speed, so this is what asks for the long lead the step lives in.
        const float RunningSpeed = 6f;
        for (int tick = 0; tick < 600; tick++)
        {
            player.velocity = new Vector2(RunningSpeed, 0f);
            player.Bottom += player.velocity;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.Brain.Senses.Update(companion.NPC, player, companion.Breath);
        }
        var objective = companion.Brain.Senses.Intent.Objective;
        var region = companion.Brain.Senses.Intent.Region;
        Require(companion.Brain.Senses.Player.IsTravelling,
            "the curve row needs a travelling player, or the central half of it is zero by definition");
        // The premise, in two parts, because either one missing makes the row green for free.
        // First: the lead must be long enough that a body-to-body slope would overtake the inside
        // gradient at the edge. That is the step, and a shorter lead simply has not got one.
        float leadLength = region.Lead.Length();
        float inner = MathF.Max(region.HalfSize.X, region.HalfSize.Y);
        float span = MathF.Max(1f, live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current.RecoveryRadius - inner);
        float wouldStepTo = MathF.Min(1f, leadLength / span);
        float centralAtEdge = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.IntentRegionCentralPull;
        Require(wouldStepTo > centralAtEdge * 1.2f, FormattableString.Invariant(
            $"the scene must lead far enough for a step to exist at all: lead={leadLength:F0}px would put a body-relative slope at {wouldStepTo:F3} against a central pull of {centralAtEdge:F3} at the edge"));
        // Second: that lead must be the play clamp's, not the headless floor's. At the floor the lead
        // is capped well below the length above, so this row could not be written headless — which is
        // the clamp's own contract, asserted here rather than assumed.
        float headlessClamp = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.IntentRegionMinimumClampX - region.HalfSize.X;
        Require(leadLength > headlessClamp, FormattableString.Invariant(
            $"the region must be built at a play-sized screen: lead={leadLength:F0}px is within the headless clamp of {headlessClamp:F0}px, so the screen declaration is not reaching the sense"));

        float worstStep = 0f;
        float worstAt = 0f;
        float previous = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany
            .GeometricPull(objective, new Vector2(region.Centre.X, region.Centre.Y), true);
        // Straight out along the lead, from the centre to well past the edge.
        for (float offset = 1f; offset <= region.HalfSize.X * 3f; offset += 1f)
        {
            Vector2 feet = new(region.Centre.X + offset, region.Centre.Y);
            float here = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany
                .GeometricPull(objective, feet, true);
            float step = MathF.Abs(here - previous);
            if (step > worstStep) { worstStep = step; worstAt = offset; }
            previous = here;
        }
        // What one pixel can legitimately buy on the steeper of the two halves. Inside, the pull
        // spans the central weight over a half-width; outside, it spans one over the slope's length.
        float inside = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.IntentRegionCentralPull / region.HalfSize.X;
        float outside = 1f / MathF.Max(1f, live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current.RecoveryRadius
            - MathF.Max(region.HalfSize.X, region.HalfSize.Y));
        float allowed = MathF.Max(inside, outside) * 1.5f;
        Console.WriteLine(FormattableString.Invariant(
            $"reunion curve: worst one-pixel step {worstStep:F5} at {worstAt:F0}px from the centre (edge at {region.HalfSize.X:F0}px), one pixel buys at most {allowed:F5}"));
        Require(worstStep <= allowed, FormattableString.Invariant(
            $"the reunion curve must have no step: worst one-pixel change {worstStep:F5} at {worstAt:F0}px from the centre, region half-width {region.HalfSize.X:F1}px, a pixel buys at most {allowed:F5}"));
    }

    /// <summary>
    /// What the central pull is for, as the one row that dies without it: a companion already settled
    /// inside the region beside a player who is still walking must want to keep up with him, not
    /// stroll where it stands.
    ///
    /// <para>The constant had no row of its own. Deleting it left the straight walk green, because
    /// the lead alone puts the body ahead often enough to pass a half-the-rows line, so the pull was
    /// live with nothing measuring it. This scene removes the lead's help deliberately: the body is
    /// placed inside the region and held grounded there until arrival is settled, which zeroes the
    /// regroup urgency and satisfies the objective, so every reason to move except the pull itself
    /// has been taken away. With the pull the offer is a reunion; with it at zero the wander floor is
    /// the largest thing left and the companion strolls beside a departing player, which is the
    /// behaviour the region was built to end.</para>
    /// </summary>
    private static void VerifyATravellingPlayerIsKeptUpWithFromInsideTheRegion()
    {
        BuildLongFloor();
        var companion = VerifyCompanionLifecycle.Create();
        var brain = companion.Brain;
        Player player = Main.player[0];
        player.dead = false;
        player.Bottom = new Vector2(240f, 1280f);
        companion.NPC.Bottom = new Vector2(240f, 1280f);
        companion.NPC.velocity = Vector2.Zero;
        var company = brain.Chooser.Actions.OfType<live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany>().Single();
        var context = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, brain.Senses);
        // The body is held at a fixed share of the way out from the region's centre, on the ground,
        // while the player walks. Holding the share rather than the pixel keeps the pull constant as
        // the region travels, so the streak settles at a known point on the curve rather than at
        // whatever the body drifted to.
        const float Share = 0.8f;
        float pull = 0f;
        for (int tick = 0; tick < 420; tick++)
        {
            player.velocity = new Vector2(2.4f, 0f);
            player.Bottom += player.velocity;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            var region = brain.Senses.Intent.Region;
            companion.NPC.Bottom = new Vector2(region.Centre.X - region.HalfSize.X * Share, 1280f);
            companion.NPC.velocity = Vector2.Zero;
            brain.Senses.Update(companion.NPC, player, companion.Breath);
            pull = brain.Senses.Intent.Region.Pull(companion.NPC.Bottom);
        }
        Require(brain.Senses.Player.IsTravelling, "the player must read as travelling, or the central pull is zero by definition");
        Require(brain.Senses.Intent.Settled, FormattableString.Invariant(
            $"the body must have settled inside the region, or regrouping rather than the pull is what is being measured: grounded-inside ticks={brain.Senses.Intent.GroundedInsideTicks}"));
        Require(pull > 0.5f && pull <= 1f, FormattableString.Invariant(
            $"the body must sit well out from the centre and inside the edge, or the pull under test is near zero: pull={pull:F3}"));
        Require(brain.Chooser.RegroupUrgency <= live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.WanderFloor,
            FormattableString.Invariant(
                $"a settled body must carry no regroup urgency, or that and not the pull is what beats the wander floor: urgency={brain.Chooser.RegroupUrgency:F3}"));
        company.Prepare(context);
        float floor = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.WanderFloor;
        Console.WriteLine(FormattableString.Invariant(
            $"central pull: settled at pull {pull:F3} beside a travelling player, keeping company offers {company.EligibilityReason} at {company.Score():F3} against a wander floor of {floor:F3}"));
        Require(company.Score() > floor, FormattableString.Invariant(
            $"a settled companion beside a travelling player must be worth more than standing about: value={company.Score():F3} floor={floor:F3} pull={pull:F3}"));
        Require(company.EligibilityReason == "reunion-method", FormattableString.Invariant(
            $"a travelling player must be kept up with rather than strolled beside: method={company.EligibilityReason} value={company.Score():F3} pull={pull:F3}"));
    }

    /// <summary>
    /// A player climbing a hill. The region leads upwards as well as along, and it grows on both axes
    /// with the lead, which is what lets a companion on the slope below count as being with him instead
    /// of reading a vertical gap and abandoning whatever it was doing. Growth is capped, so the test is
    /// that the region is taller and higher than a still player's and by no more than the cap.
    ///
    /// <para>What this deliberately does not assert is that the companion walks up the slope. Walking a
    /// 1:1 slope still times out — AIC-212 — and the native company fixtures keep their rising-slope
    /// scenes out of the suite for exactly that reason; a fixture here that required the climb would be
    /// red for a defect this lane does not own and would say nothing about the region.</para>
    /// </summary>
    private static void VerifyAClimbingPlayerGrowsTheRegionUpwards()
    {
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.Bottom = new Vector2(600f, 1280f);
        player.velocity = Vector2.Zero;
        companion.NPC.Bottom = new Vector2(600f, 1280f);
        companion.NPC.velocity = Vector2.Zero;
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        companion.Brain.Senses.Update(companion.NPC, player, companion.Breath);
        var still = companion.Brain.Senses.Intent.Region;
        for (int tick = 0; tick < 240; tick++)
        {
            // A diagonal climb, which is what a hill is: along and up together.
            player.velocity = new Vector2(2f, -1.5f);
            player.Bottom += player.velocity;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.Brain.Senses.Update(companion.NPC, player, companion.Breath);
        }
        var climbing = companion.Brain.Senses.Intent.Region;
        float cap = 1f + live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.IntentRegionGrowthCap;
        Require(climbing.Lead.Y < -16f && climbing.Centre.Y < player.Bottom.Y - 16f, FormattableString.Invariant(
            $"a climbing player's region must lead above his feet: lead={climbing.Lead} centre={climbing.Centre} feet={player.Bottom}"));
        Require(climbing.HalfSize.Y > still.HalfSize.Y && climbing.HalfSize.X > still.HalfSize.X, FormattableString.Invariant(
            $"the region must grow on both axes with the lead, not only along it: still={still.HalfSize} climbing={climbing.HalfSize}"));
        Require(climbing.HalfSize.Y <= still.HalfSize.Y * cap + 0.01f && climbing.HalfSize.X <= still.HalfSize.X * cap + 0.01f,
            FormattableString.Invariant($"growth must stop at the cap: still={still.HalfSize} climbing={climbing.HalfSize} cap={cap}"));
        Require(climbing.LeadingEdge.Y < climbing.Centre.Y, FormattableString.Invariant(
            $"the leading edge must be on the climb's own side of the region: edge={climbing.LeadingEdge} centre={climbing.Centre}"));
    }

    /// <summary>
    /// A body standing on real sloped ground must be able to arrive.
    ///
    /// <para>Arrival needs a grounded streak and the grounded test is `velocity.Y == 0f`, which is the motor's own
    /// expression on the same body — deliberately one definition, so the brain and the motor cannot disagree about
    /// whether a body is standing. That test had never been asked on a slope. Every scene that exercised it stood
    /// the body on flat ground, where the question is trivial, while the root guide records that `Collision.StepUp`
    /// writes position without touching `velocity.Y` — which is exactly the kind of thing that leaves a resting
    /// body carrying vertical velocity it is not using. A companion that could never read as settled on a hillside
    /// would never satisfy following there, and the region's own growth on the vertical axis exists precisely so
    /// that a companion on the slope below counts as being with the player.</para>
    ///
    /// <para>The engine rests a body on a slope's diagonal rather than on a tile's top edge, so the hill is built
    /// from the game's own slope shapes and not from a staircase of full blocks, and the row asserts that it is
    /// standing on one before it reads anything. It deliberately does not walk the slope: a 1:1 slope walk still
    /// times out (AIC-212), and a row that required the climb would be red for a defect this lane does not own.</para>
    /// </summary>
    private static void VerifyAGroundedTickOnSlopedGroundEntersSatisfaction()
    {
        BuildSlopedHill();
        var companion = VerifyCompanionLifecycle.Create();
        var brain = companion.Brain;
        Player player = Main.player[0];
        player.dead = false;
        // Both on the hill, two columns apart, the companion downslope of the player.
        const int PlayerColumn = 47, CompanionColumn = 45;
        player.Bottom = new Vector2(PlayerColumn * 16f + 8f, (80 - (PlayerColumn - HillLeft)) * 16f);
        player.velocity = Vector2.Zero;
        companion.NPC.Bottom = new Vector2(CompanionColumn * 16f + 8f, (80 - (CompanionColumn - HillLeft)) * 16f - 8f);
        companion.NPC.velocity = Vector2.Zero;

        // Let native collision settle the body onto the diagonal. Nothing is asserted about how long that takes;
        // what matters is the state it comes to rest in.
        for (int tick = 0; tick < 60; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            AdvanceNative(companion);
        }
        Point feet = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.FeetTile(companion.NPC.Bottom);
        var shape = live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World.Shape(feet.X, feet.Y + 1);
        Console.WriteLine(FormattableString.Invariant(
            $"sloped ground: body rests at {companion.NPC.Bottom} on tile {feet.X},{feet.Y} over a {shape} with vy={companion.NPC.velocity.Y}"));
        // The premise, in two parts. The ground under the body has to be an actual slope, or this is the flat row
        // again; and the body has to have come to rest on it rather than still be falling past it.
        Require(shape is live::AICompanion.Companion.Brain.Infrastructure.Movement.TileShape.SolidLowerLeft
                or live::AICompanion.Companion.Brain.Infrastructure.Movement.TileShape.SolidLowerRight,
            FormattableString.Invariant($"the body must come to rest over a sloped tile, or the row is the flat one again; tile under the feet is {shape}"));
        Require(companion.NPC.velocity.Y == 0f, FormattableString.Invariant(
            $"a body at rest on a slope must read as grounded, and the whole brain's arrival depends on it: vy={companion.NPC.velocity.Y} at {companion.NPC.Bottom} over a {shape}"));

        // Now the streak, which is the thing arrival actually reads. The body is left alone on the slope and the
        // senses are rebuilt each tick, exactly as a resting companion's would be.
        int rescore = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.PositionRescoreTicks;
        for (int tick = 0; tick < rescore + 4; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            AdvanceNative(companion);
            brain.Senses.Update(companion.NPC, player, companion.Breath);
        }
        Console.WriteLine(FormattableString.Invariant(
            $"sloped ground: grounded={brain.Senses.Intent.Grounded} streak={brain.Senses.Intent.GroundedInsideTicks} settled={brain.Senses.Intent.Settled}"));
        Require(brain.Senses.Intent.Grounded, FormattableString.Invariant(
            $"the sense must read a body resting on a slope as grounded: vy={companion.NPC.velocity.Y}"));
        Require(brain.Senses.Intent.Settled, FormattableString.Invariant(
            $"a body standing still on a slope inside the region must enter satisfaction, or it can never arrive on a hillside; streak={brain.Senses.Intent.GroundedInsideTicks} of {rescore}"));

        // And the objective itself, which is what keeping company and the positioner both read.
        var objective = brain.Senses.Intent.Objective;
        Require(objective.IsSatisfied(companion.NPC.Bottom, true), FormattableString.Invariant(
            $"following must read as satisfied on the slope: reason={objective.Reason(companion.NPC.Bottom, true)}, companion={companion.NPC.Bottom}, player={player.Bottom}"));
    }

    /// <summary>
    /// Ticks 8127 and 8128 of the 2026-09-14 capture, as geometry. Both bodies are in the air and
    /// the companion's feet sit inside the region, which the old symmetric box read as satisfied;
    /// keeping company then flipped to its local method and issued a Hold that cancelled the jump
    /// one tick after take-off. Satisfaction is judged from the ground, so an airborne tick cannot
    /// enter it however close the two bodies are, and the method cannot change on that tick.
    /// </summary>
    private static void VerifyAnAirborneTickCannotSatisfyFollowing()
    {
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        var brain = companion.Brain;
        Player player = Main.player[0];
        player.dead = false;
        // Both bodies rising, a third of a tile apart horizontally: well inside any comfort box.
        player.Bottom = new Vector2(600f, 1230f);
        player.velocity = new Vector2(0f, -6.4f);
        companion.NPC.Bottom = new Vector2(620f, 1226f);
        companion.NPC.velocity = new Vector2(2f, -6.4f);
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        brain.Senses.Update(companion.NPC, player, companion.Breath);
        var request = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
            RequestKind.WithPlayer, player.Bottom);
        brain.Positioner.Resolve(request, brain.Senses, null);
        Require(!brain.Positioner.FollowObjectiveSatisfied, FormattableString.Invariant(
            $"an airborne body must not read as having arrived with the player: companion={companion.NPC.Bottom} vy={companion.NPC.velocity.Y} player={player.Bottom} vy={player.velocity.Y} reason={brain.Positioner.FollowObjectiveReason}"));
        // The record has to say which kind of unsettled tick this was, because the grounded body
        // standing out its first ticks of the streak is the same geometry and a different situation.
        Require(brain.Positioner.FollowObjectiveReason == "follow-airborne-deferred", FormattableString.Invariant(
            $"an airborne deferral must name itself in the record: reason={brain.Positioner.FollowObjectiveReason} vy={companion.NPC.velocity.Y}"));
        var company = brain.Chooser.Actions.OfType<live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany>().Single();
        var context = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, brain.Senses);
        company.Prepare(context);
        string airborneMethod = company.EligibilityReason;
        companion.NPC.velocity = new Vector2(2f, -6.0f);
        companion.NPC.Bottom += new Vector2(2f, -6.0f);
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        brain.Senses.Update(companion.NPC, player, companion.Breath);
        company.Prepare(context);
        Require(company.EligibilityReason == airborneMethod, FormattableString.Invariant(
            $"keeping company must not change method on an airborne tick: {airborneMethod} became {company.EligibilityReason}"));
    }

    /// <summary>
    /// The other end of the same rule. A player who stops leaves a region that drifts back over the
    /// filter's time constant rather than snapping, and the companion must settle inside it without
    /// the method flickering while it does. The ceiling is declared here rather than discovered: a
    /// settle is allowed one change into the local method and nothing after it.
    /// </summary>
    private static void VerifyAStoppedPlayerDoesNotOscillateTheMethod()
    {
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        var brain = companion.Brain;
        Player player = Main.player[0];
        player.dead = false;
        player.Bottom = new Vector2(400f, 1280f);
        companion.NPC.Bottom = new Vector2(400f, 1280f);
        companion.NPC.velocity = Vector2.Zero;
        var company = brain.Chooser.Actions.OfType<live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany>().Single();
        var context = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, brain.Senses);
        for (int tick = 0; tick < 240; tick++)
        {
            player.velocity = new Vector2(3f, 0f);
            player.Bottom += player.velocity;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI();
            AdvanceNative(companion);
        }
        player.velocity = Vector2.Zero;
        string method = "";
        int changes = 0;
        int mislabelled = 0;
        for (int tick = 0; tick < 600; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI();
            AdvanceNative(companion);
            // The other half of the airborne fixture's assertion, and the half a grounded body can
            // check: a settling tick on the floor must never be filed as an airborne deferral, or the
            // column sends whoever reads it looking for a jump that never happened.
            if (companion.NPC.velocity.Y == 0f
                && brain.Positioner.FollowObjectiveReason == "follow-airborne-deferred") mislabelled++;
            company.Prepare(context);
            if (company.EligibilityReason != method)
            {
                if (method.Length > 0) changes++;
                method = company.EligibilityReason;
            }
        }
        Require(mislabelled == 0, FormattableString.Invariant(
            $"a grounded settling tick must not be recorded as an airborne deferral: {mislabelled} of 600 ticks"));
        const int SettleChangeCeiling = 1;
        Require(changes <= SettleChangeCeiling, FormattableString.Invariant(
            $"a stopped player must let the method settle: {changes} method changes over 600 ticks, ceiling {SettleChangeCeiling}, ending {method}"));
        Require(Vector2.Distance(companion.NPC.Bottom, player.Bottom) < live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.FollowHorizontalComfort,
            FormattableString.Invariant($"the companion must settle beside a stopped player: companion={companion.NPC.Bottom} player={player.Bottom}"));
    }

    private static void BuildParallelRoutes(int gapAt)
    {
        BuildFloor();
        // The player's journey is an upper floor four rows above the lower one; its left end is a ledge
        // the companion can jump onto from the lower floor.
        for (int x = 26; x <= 90; x++) Solid(x, 76);
        if (gapAt > 0)
            for (int x = gapAt; x <= gapAt + 2; x++) Clear(x, 76);
        else
            // The lower route ends at a cliff: a pit too deep to climb out of, so nothing beyond it is returnable.
            for (int x = 50; x <= 60; x++)
            {
                for (int y = 80; y <= 89; y++) Clear(x, y);
                Solid(x, 90);
            }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
    }

    /// <summary>One flat floor across a world wide enough for a walk of a thousand ticks, so a fixture
    /// measuring what the companion does while travelling is not measuring what it does when it reaches
    /// the end of the ground.</summary>
    private static void BuildLongFloor()
    {
        Main.maxTilesX = 400;
        Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)400, (ushort)100 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 395; x++)
        {
            Tile tile = Main.tile[x, 80];
            tile.HasTile = true;
            tile.TileType = 1;
        }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
    }

    private static void BuildFloor()
    {
        Main.maxTilesX = Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)100, (ushort)100 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 80];
            tile.HasTile = true;
            tile.TileType = 1;
        }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
    }

    /// <summary>
    /// A hill of real sloped tiles rising one row per column, the game's own slope shapes rather than a staircase
    /// of full blocks. The distinction is the whole point of the row that uses it: the engine rests a body on a
    /// slope's diagonal rather than on a tile's top edge, and whether that body reads as standing is what the
    /// grounded test asserts and no fixture had asked.
    /// </summary>
    private static void BuildSlopedHill()
    {
        BuildFloor();
        for (int x = HillLeft; x <= HillRight; x++)
        {
            int top = 80 - (x - HillLeft);
            for (int y = top; y <= 84; y++) Solid(x, y);
            // Held in a local before the field is written: Tile is a view returned by value from the tile map's
            // indexer, so assigning through the indexer expression writes to a copy.
            Tile step = Main.tile[x, top];
            step.Slope = (Terraria.ID.SlopeType)2;
        }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
    }

    private const int HillLeft = 40, HillRight = 52;

    private static void BuildCturn()
    {
        BuildFloor();
        // The companion begins on the raised shelf. A ceiling and right wall make the direct
        // player direction impossible; it must walk left, drop through the opening, then return
        // right along the lower floor. This is the smallest native geometry that catches a
        // partial-result policy which rejects every first step that increases Euclidean distance.
        for (int x = 40; x <= 60; x++) Solid(x, 70);
        for (int x = 40; x <= 60; x++) Solid(x, 64);
        for (int y = 65; y <= 70; y++) Solid(60, y);
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
    }

    internal static void AdvanceNative(live::AICompanion.Companion.CharacterBody.CompanionNPC companion)
    {
        // The production motor has already applied MovementAbilities and StepUp/StepDown. Calling
        // VerifyEngineMotion.RunEngine here would apply the same controls a second time, so finish
        // this tick with Terraria's own gravity and collision phases only.
        NPC npc = companion.NPC;
        // Suppress the splash visual, whose dust/audio services do not exist headless.
        // Native wet detection, velocity changes and collision still run.
        npc.wetCount = 2;
        Invoke(npc, "UpdateNPC_UpdateGravity");
        npc.velocity.Y = MathF.Min(npc.velocity.Y + npc.gravity, npc.maxFallSpeed);
        Invoke(npc, "UpdateCollision");
    }

    private static void Clear(int x, int y) => Main.tile[x, y].ClearEverything();

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = true;
        tile.TileType = 1;
    }

    private static void Invoke(NPC npc, string method)
        => typeof(NPC).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(npc, null);

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
