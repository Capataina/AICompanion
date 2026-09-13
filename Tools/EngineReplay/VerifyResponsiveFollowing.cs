extern alias live;

using System.Reflection;
using AICompanion.Companion.Brain.SharedMovementSystem;
using Microsoft.Xna.Framework;
using Terraria;

using FollowPlayerObjective = live::AICompanion.Companion.Brain.PositionSelection.FollowPlayerObjective;
using RequestKind = live::AICompanion.Companion.Brain.PositionSelection.RequestKind;

/// <summary>
/// Exercises the production brain through its live alias. The player is moved directly because
/// this fixture measures the brain's response to observed motion, not Terraria's player physics;
/// companion controls still pass through the native NPC collision adapter every tick.
/// </summary>
internal static class VerifyResponsiveFollowing
{
    public static int Run()
    {
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
        Console.WriteLine("responsive following: vertical intent, two-axis arrival, live brain follow selection, activity-dependent meeting places, route-priced reunion, retained meeting floods and dropped meeting places passed");
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
        var company = brain.Chooser.Actions.OfType<live::AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance.KeepCompany>().Single();
        Require(!brain.Chooser.Actions.Any(a => a.Name is "walk-with" or "wander"), "obsolete companionship candidates remain registered");
        brain.Chooser.Actions.RemoveAll(a => !ReferenceEquals(a, company));
        var context = new live::AICompanion.Companion.Brain.Behaviours.ActionContext(companion, brain.Senses);
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
                rested |= request.Kind == RequestKind.Hold;
                strolled |= request.Kind == RequestKind.Exact;
                Require(request.Kind != RequestKind.WithPlayer, "calm co-location should not keep requesting reunion");
            }
        }
        finally { Main.rand = random; }
        Require(rested && strolled, "company must preserve both resting and nearby movement methods");
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
        var travel = new live::AICompanion.Companion.Brain.WorldObservation.InferPlayerActivity();
        var working = new live::AICompanion.Companion.Brain.WorldObservation.InferPlayerActivity();
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

    private static void VerifyTwoAxisObjective()
    {
        var objective = new FollowPlayerObjective(new Vector2(480, 800), new Vector2(480, 752));
        Require(!objective.IsSatisfied(new Vector2(480, 1120), locallyConnected: true),
            "a companion directly below the player on another floor must not satisfy following");
        Require(!objective.AcceptsDestination(new Vector2(480, 1120), locallyConnected: true),
            "a different-floor incumbent must not remain a valid following destination");
        Require(objective.AcceptsDestination(new Vector2(640, 752), locallyConnected: true),
            "a nearby standable point in the predicted player region remains a valid following destination");
        Require(!objective.IsSatisfied(new Vector2(480, 800), locallyConnected: false),
            "a nearby but sealed floor must not satisfy following without a local connection");
    }

    private static void VerifyArrivalSlackCannotStrandFollowing()
    {
        // Captured run: the destination was 190 pixels from the owner, but stopping twelve
        // pixels short left the body outside the 192-pixel follow region indefinitely.
        var objective = new FollowPlayerObjective(new Vector2(500, 1280), new Vector2(500, 1280));
        Require(!objective.AcceptsDestination(new Vector2(690, 1280), true),
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
            arrived = new FollowPlayerObjective(player.Bottom, player.Bottom).IsSatisfied(companion.NPC.Bottom,
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
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
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
        Require(travel.Reason == "meeting-ahead-priced" && travel.Anchor.X >= travel.PlayerX + 3 * 16,
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
            meeting.Resolve(companion.NPC.Bottom, companion.Brain.Senses.Player, Main.GameUpdateCount + (ulong)i);
            if (meeting.Reason is not ("meeting-undecided" or "retained-while-undecided")) break;
        }
        return (meeting.Anchor, meeting.Reason, player.Bottom.X);
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
                    anchorX = meeting.Anchor.X; playerX = player.Bottom.X; startX = companion.NPC.Bottom.X;                }
                if (decidedAt >= 0 && tick == decidedAt + 60) moved = companion.NPC.Bottom.X - startX;
                arrived = new FollowPlayerObjective(player.Bottom, player.Bottom).IsSatisfied(companion.NPC.Bottom,
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
        live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = true;
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
            int cadence = live::AICompanion.Companion.Brain.BehaviourSelection.Weights.MeetingRerootTicks;
            Vector2 first = meeting.Resolve(companion.NPC.Bottom, sense, start);
            Vector2 continuation = sense.Predict(live::AICompanion.Companion.Brain.BehaviourSelection.Weights.MeetingFallbackLeadTicks);
            Require(meeting.Reason == "meeting-undecided",
                $"one slice must not finish the flood, or nothing about retaining its progress is tested; reason={meeting.Reason} flood={meeting.FloodState}");
            Require(Vector2.Distance(first, continuation) < 1f && MathF.Abs(first.X - sense.Bottom.X) >= 16f,
                $"an undecided meeting place must aim at the player's continued travel, not the feet they have left; anchor={first} continuation={continuation} feet={sense.Bottom}");
            int calls = 1;
            for (; calls < 200 && meeting.Reason == "meeting-undecided"; calls++)
                meeting.Resolve(companion.NPC.Bottom + new Vector2(calls % 2 * 16, 0), sense, start + (ulong)(calls * (cadence + 1)));
            Require(meeting.Reason is "meeting-ahead-priced" or "player-position-priced",
                $"an unfinished flood must keep its progress while the body stays in the region it can return from; reason={meeting.Reason} after {calls} slices, flood={meeting.FloodState}");
            Console.WriteLine($"meeting flood: {meeting.Reason} after {calls} slices, each past the re-root cadence with the body moved; flood {meeting.FloodState}");
        }
        finally { live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = false; }
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
        Vector2 place = live::AICompanion.Companion.Brain.SharedMovementSystem.MovementQueries.FeetWorld(new Point(80, 79));
        positioner.Resolve(new live::AICompanion.Companion.Brain.PositionSelection.PositionRequest(RequestKind.WithPlayer, place, MeetingPlace: true),
            companion.Brain.Senses, null);
        Require(positioner.Chosen is Vector2 held && Vector2.Distance(held, place) < 1f && positioner.ChoiceReason == "priced-meeting-place",
            $"a priced meeting place must first become the exact destination, or dropping it tests nothing; chosen={positioner.Chosen} reason={positioner.ChoiceReason}");
        positioner.Resolve(new live::AICompanion.Companion.Brain.PositionSelection.PositionRequest(RequestKind.WithPlayer, player.Bottom),
            companion.Brain.Senses, null);
        Require(positioner.Chosen is not Vector2 kept || Vector2.Distance(kept, place) >= 16f,
            $"a dropped meeting place must not remain the destination until the next rescore; chosen={positioner.Chosen} reason={positioner.ChoiceReason} place={place}");
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
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World = new live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld();
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
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World = new live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld();
    }

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
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World = new live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld();
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
