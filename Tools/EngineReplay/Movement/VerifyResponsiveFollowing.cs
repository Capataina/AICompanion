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
        // Every row runs and reports, and the fixture fails afterwards if any did. The rows used to be called one after
        // another and their assertions throw, so the first red took every row after it: on 15 September 2026 the region
        // commit put four rows red and only the first was ever seen, until a copy of this fixture was run row by row. A
        // row's order no longer decides whether the rows after it are read, so the ordering notes that lived here went.
        var failures = new List<string>();
        foreach (var (name, row) in new (string Name, Action Row)[]
        {
            (nameof(VerifyLocalMotionDoesNotBecomeTravel), VerifyLocalMotionDoesNotBecomeTravel),
            (nameof(VerifyIntentEvidenceAndRevision), VerifyIntentEvidenceAndRevision),
            (nameof(VerifyCompanyMethodsShareOneActivity), VerifyCompanyMethodsShareOneActivity),
            (nameof(VerifyTwoAxisObjective), VerifyTwoAxisObjective),
            (nameof(VerifyArrivalSlackCannotStrandFollowing), VerifyArrivalSlackCannotStrandFollowing),
            (nameof(VerifyVerticalPlayerMotionReachesTheProductionFollowAction), VerifyVerticalPlayerMotionReachesTheProductionFollowAction),
            (nameof(VerifyOccludedPlayerStillProvidesADestination), VerifyOccludedPlayerStillProvidesADestination),
            (nameof(VerifyCturnCompletesThroughTheProductionBrain), VerifyCturnCompletesThroughTheProductionBrain),
            (nameof(VerifyRecentActivityChangesTheMeetingPlace), VerifyRecentActivityChangesTheMeetingPlace),
            (nameof(VerifyMeetingPlacesFollowTheCompanionsOwnRoutes), VerifyMeetingPlacesFollowTheCompanionsOwnRoutes),
            (nameof(VerifyAnUnfinishedMeetingFloodKeepsItsProgress), VerifyAnUnfinishedMeetingFloodKeepsItsProgress),
            (nameof(VerifyADroppedMeetingPlaceIsNotWalkedTo), VerifyADroppedMeetingPlaceIsNotWalkedTo),
            (nameof(VerifyAMovingTickInsideTheRegionKeepsTheMethod), VerifyAMovingTickInsideTheRegionKeepsTheMethod),
            (nameof(VerifyTheRejoinPullIsZeroInsideTheRegionAndHasNoStepAtItsEdge), VerifyTheRejoinPullIsZeroInsideTheRegionAndHasNoStepAtItsEdge),
            (nameof(VerifyTheCompanionLeadsATravellingPlayer), VerifyTheCompanionLeadsATravellingPlayer),
            (nameof(VerifyAClimbingPlayerGrowsTheRegionUpwards), VerifyAClimbingPlayerGrowsTheRegionUpwards),
            (nameof(VerifyABodyOnSlopedGroundInsideTheRegionIsWithThePlayer), VerifyABodyOnSlopedGroundInsideTheRegionIsWithThePlayer),
            (nameof(VerifyAStoppedPlayerDoesNotOscillateTheMethod), VerifyAStoppedPlayerDoesNotOscillateTheMethod),
        })
        {
            try
            {
                row();
                Console.WriteLine($"GREEN {name}");
            }
            catch (Exception e)
            {
                string message = e is InvalidOperationException ? e.Message : e.ToString();
                failures.Add($"{name}: {message}");
                Console.WriteLine($"RED {name}: {message}");
            }
        }
        if (failures.Count > 0)
            throw new InvalidOperationException($"{failures.Count} following rows red: {string.Join(" || ", failures)}");
        Console.WriteLine("responsive following: vertical intent, two-axis objective, live brain follow selection, activity-dependent meeting places, route-priced reunion, retained meeting floods, dropped meeting places, a moving tick keeping the method, leading a travelling player, a rejoin pull with no step at the region's edge, a region that grows up a hill, being with the player on sloped ground and a settling stop passed");
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
        var company = brain.Actions.OfType<live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany>().Single();
        Require(!brain.Actions.Any(a => a.Name is "walk-with" or "wander"), "obsolete companionship candidates remain registered");
        brain.Actions.RemoveAll(a => !ReferenceEquals(a, company));
        var context = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, brain.Senses);
        brain.Senses.Update(companion.NPC, player);
        // Prepared and selected rather than chosen: this scene has already removed every activity but
        // company, so the family chooser was only ever picking the sole candidate, and the subject is the
        // offer it makes and the purpose identity it keeps. `OwnCurrentActivity` is the owner the live
        // tick hands the course's chosen activity to, and is deliberately not part of the retired scorer.
        company.Prepare(context);
        brain.Activity.Select(company, context);
        Require(ReferenceEquals(brain.Activity.Current, company) && company.Score() > 0, "company must be a positive ordinary offer while nearby");
        long identity = brain.Activity.Id;
        // Company beside a resting player is the inside method: the request aims at the region's centre and the
        // positioner parks in the clearest air inside it. Calm co-location never asks to rejoin, and the request
        // must still grow the reach region to completion.
        bool accompanied = true;
        for (int tick = 0; tick < 2400; tick++)
        {
            var request = company.Execute(context);
            // Resolved every tick as the brain does, because the region is rooted and replaced on the resolver's own cadence.
            brain.Positioner.Resolve(request, brain.Senses);
            accompanied &= request.Kind == RequestKind.WithPlayer && brain.Positioner.Chosen != null
                && company.EligibilityReason == "local-company-method";
            Require(company.EligibilityReason != "reunion-method", "calm co-location should not keep requesting reunion");
        }
        Require(accompanied && brain.Positioner.ReachComplete,
            $"company beside a resting player must accompany him from inside his region, and that must still complete the reach region; accompanied={accompanied} "
            + $"returnable={brain.Positioner.ReturnableCount} reach={brain.Positioner.ReachCount} complete={brain.Positioner.ReachComplete} reason={brain.Positioner.ChoiceReason}");
        player.Bottom += new Vector2(480, 0);
        brain.Senses.Update(companion.NPC, player);
        company.Prepare(context);
        brain.Activity.Select(company, context);
        Require(ReferenceEquals(brain.Activity.Current, company) && company.Execute(context).Kind == RequestKind.WithPlayer,
            "departure must switch the same company activity to reunion");
        Require(brain.Activity.Id == identity, "a company method change must not create a new purpose");
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

    /// <summary>An objective over a region centred where the test wants it, so the geometry rules can be exercised without
    /// driving a body first. <paramref name="inside"/> is whether the body was inside on the tick before, which is what
    /// gives the region's edge its width.</summary>
    private static FollowPlayerObjective ObjectiveAt(Vector2 centre, Vector2 anchor, bool inside = false)
    {
        var region = new live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerIntentRegion(centre,
            new Vector2(live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.FollowHorizontalComfort,
                live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.FollowVerticalComfort),
            Vector2.Zero, IsTravelling: false);
        return new FollowPlayerObjective(region, anchor, inside);
    }

    private static void VerifyTwoAxisObjective()
    {
        var objective = ObjectiveAt(new Vector2(480, 800), new Vector2(480, 752));
        Require(!objective.IsSatisfied(new Vector2(480, 1120)),
            "a companion directly below the player on another floor must not satisfy following");
        Require(!objective.AcceptsDestination(new Vector2(480, 1120), locallyConnected: true),
            "a different-floor incumbent must not remain a valid following destination");
        Require(objective.AcceptsDestination(new Vector2(640, 752), locallyConnected: true),
            "a nearby standable point in the predicted player region remains a valid following destination");
        Require(objective.IsSatisfied(new Vector2(480, 800)),
            "a body at the region's own centre is with the player at once, with no settling to wait out");
        // Restated on 15 September 2026. The row also required a nearby sealed floor to fail following without a local
        // connection, and a body at the region's centre to fail it until it had settled there. Losing sight of the player
        // is not distance and nothing waits for a rest any more, so both went. What they were for is the edge's width,
        // which the inside latch now gives: asserted on both sides of the settle radius.
        float slack = live::AICompanion.Companion.Brain.Infrastructure.Movement.Navigator.SettleRadius;
        float edge = 480 + objective.HorizontalComfort;
        var justBeyond = new Vector2(edge + slack * 0.5f, 800);
        var farBeyond = new Vector2(edge + slack + 1f, 800);
        var wasInside = ObjectiveAt(new Vector2(480, 800), new Vector2(480, 752), inside: true);
        Require(!objective.IsSatisfied(justBeyond),
            "a body just beyond the edge that was not inside has not joined the player");
        Require(wasInside.IsSatisfied(justBeyond),
            "a body that was inside is still with the player within the settle radius beyond the edge");
        Require(!wasInside.IsSatisfied(farBeyond),
            "a body more than the settle radius beyond the edge has left the player, whatever it was before");
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
            // The live sense's objective at the body's centre, the one the brain reads. The literal box above admits
            // destinations; being with the player is the region the brain builds, not a box a fixture typed.
            arrived = companion.Brain.Senses.Intent.Objective.IsSatisfied(companion.NPC.Center);
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
        // Column 85, not 70. Restated on 15 September 2026: with the player at 70 his region, which always holds him and sits
        // mostly above him, reached back over the whole shelf, so the body on it was already with him and never had a reason to
        // take the away leg the row exists for. At 85 the region's near edge stands past the shelf's right wall.
        player.position = new Vector2(85 * 16, 80 * 16 - player.height);
        // Twelve above the floor row, not eight: eight puts the ten-pixel circle two pixels inside the floor,
        // and the first contact resolve would shove the body before the scene's first tick.
        companion.NPC.Center = new Vector2(50 * 16 + 8, 70 * 16 - 12);
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
            initiallyMovedAway |= companion.Motor.AppliedControls.Desired.X < 0f;
            Require(!companion.Brain.Navigator.SearchPending || !companion.Brain.Navigator.LastPlanFailed,
                "an unfinished retained search must not be classified failed before it publishes a prefix");
            // A route the body is following is never a failed plan, whatever the search behind it is still doing.
            Require(companion.Brain.Navigator.Path == null || !companion.Brain.Navigator.LastPlanFailed,
                "a usable route must not be classified as a failed plan");
            AdvanceNative(companion);
            // Selection may already yield to idle after arrival, clearing the positioner's
            // request-scoped flag. The contract is the actual body reaching the usable floor.
            // The live sense's own objective at the body's centre, so the fixture judges arrival by exactly what the brain
            // judges it by: being inside the player's region.
            arrived = companion.Brain.Senses.Intent.Objective.IsSatisfied(companion.NPC.Center);
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
        // Restated when rejoining became a measure from the player's region: the companion used to stand eight tiles away,
        // inside the region, and the row passed through a floor that raised rejoining whenever sight was lost. Losing sight
        // is not distance, so that floor is gone and a companion inside the region is given no rejoin pull at all. What the
        // row protects — a closed door does not veto a destination on the player's side — is asked where rejoining is
        // asked, so the companion now starts beyond the region's edge with the door still between them.
        companion.NPC.position = new Vector2(30 * 16, 80 * 16 - companion.NPC.height);
        // The door at column 36, not 50. Restated a second time the same day, when a companion outside the region began
        // rejoining at the nearest place inside it the flood holds: with the door at 50 the region reached back past the door,
        // so the nearest held place inside it was on the companion's own side and the row asserted a veto that was really
        // an arrival. At 36 the whole region, less the settle radius every destination reserves, lies on the player's side.
        const int DoorColumn = 36;
        for (int y = 77; y < 80; y++)
        {
            Tile door = Main.tile[DoorColumn, y];
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
        Require(companion.Brain.Senses.Intent.Region.GapBeyond(companion.NPC.Center) > 0,
            $"the occluded-player fixture must start the companion beyond the player's region; half={companion.Brain.Senses.Intent.Region.HalfSize} gap={companion.Brain.Senses.Intent.Region.GapBeyond(companion.NPC.Center)}");
        Require(companion.Brain.LastRequest.Kind == RequestKind.WithPlayer,
            $"an occluded player beyond the region must still request following; kind={companion.Brain.LastRequest.Kind} action={companion.Brain.LastAction?.Name} half={companion.Brain.Senses.Intent.Region.HalfSize} gap={companion.Brain.Senses.Intent.Region.GapBeyond(companion.NPC.Center)}");
        // A closed door is a wall to the orb's flood, so no candidate on the player's side is reachable and
        // the positioner chooses nothing; a follow request with nothing chosen aims the navigator at the
        // anchor itself, which is how the body reaches the door for the door interaction to open. Either
        // shape puts the goal on the player's side; a hold or a goal on this side is the veto this row refuses.
        Vector2? chosen = companion.Brain.Positioner.Chosen;
        Point? aim = companion.Brain.Navigator.GoalTile;
        var occludedRegion = companion.Brain.Senses.Intent.Region;
        float nearestAdmittedX = occludedRegion.Centre.X - occludedRegion.HalfSize.X + live::AICompanion.Companion.Brain.Infrastructure.Movement.Navigator.SettleRadius;
        Require(nearestAdmittedX > (DoorColumn + 1) * 16f,
            $"the premise: no part of the region a destination may be admitted in may lie on the companion's side of the door; nearest admitted x={nearestAdmittedX} door's right edge={(DoorColumn + 1) * 16}");
        Require((chosen is Vector2 goal && goal.X > DoorColumn * 16) || (aim is Point at && at.X > DoorColumn),
            $"a closed door must not veto player-side follow destinations; chosen={chosen} navigator-goal={aim} reason={companion.Brain.Positioner.ChoiceReason}");
    }

    /// <summary>
    /// The player ends on the same tile in four scenes whose recent activity differs, with the companion
    /// ten tiles to its right on one floor. Walking right, the player is met where he is or ahead of him,
    /// never behind; placing torches in place, he is met where he stands; a brief turn keeps the journey,
    /// so he is still not met behind; having turned round for good, he is heading away and is met at or
    /// behind where he stands. The pairs come from the proposal's intent-to-destination acceptance and
    /// exercise the production observer and selector.
    ///
    /// <para>Restated on 15 September 2026. The travelling and brief-turn scenes required the meeting place three
    /// tiles or more ahead of the player's feet, which held only while the region could be led off him: a region
    /// that always holds the player contains every place at his feet, so the cheapest meeting — where he already
    /// is — is inside it and wins. Being ahead of a travelling player is now what the companion does inside his
    /// region rather than where it meets him, and `VerifyAccompanyingThePlayer`'s trailing share holds it; this row
    /// keeps what the meeting place still owes, which is not meeting a travelling player behind him.</para>
    /// </summary>
    private static void VerifyRecentActivityChangesTheMeetingPlace()
    {
        var travel = MeetingAfter(new[] { (3f, 120) }, placingTorches: false);
        var torches = MeetingAfter(Enumerable.Repeat(new[] { (2f, 10), (-2f, 10) }, 6).SelectMany(s => s).ToArray(), placingTorches: true);
        var brief = MeetingAfter(new[] { (3f, 110), (-3f, 10) }, placingTorches: false);
        var backtrack = MeetingAfter(new[] { (3f, 30), (-3f, 120) }, placingTorches: false);
        string ledger = string.Join("; ", new[] { ("travel", travel), ("torches", torches), ("brief", brief), ("backtrack", backtrack) }
            .Select(s => $"{s.Item1}: reason={s.Item2.Reason} anchorX={s.Item2.Anchor.X / 16:0.0} playerX={s.Item2.PlayerX / 16:0.0}"
                + $" flood={s.Item2.Flood} priced={s.Item2.Priced}"));
        Require(new[] { travel, torches, brief, backtrack }.All(s => MathF.Abs(s.PlayerX - 40 * 16) < 2f),
            $"every scene must end with the player on the same tile, or the pairs compare positions rather than activity; {ledger}");
        // A travelling player is met where he is or on the journey ahead, never behind him; which priced reason put the
        // place there is not the contract. The summary above carries why "three tiles ahead" went.
        Require(travel.Reason is "meeting-ahead-priced" or "player-position-priced" or "meeting-place-outside-intent-region"
                && travel.Anchor.X >= travel.PlayerX - 16f,
            $"a player walking towards the companion is met where he is or ahead, never behind; {ledger}");
        Require(torches.Reason == "player-not-travelling" && MathF.Abs(torches.Anchor.X - torches.PlayerX) < 16f,
            $"a player placing torches in place is met where they stand; {ledger}");
        Require(brief.Anchor.X >= brief.PlayerX - 16f,
            $"a brief reversal must not turn the journey's meeting place round behind him; {ledger}");
        Require(backtrack.Anchor.X <= backtrack.PlayerX + 16f,
            $"sustained backtracking replaces the journey, so the meeting place is no longer ahead towards the companion; {ledger}");
    }

    private static (Vector2 Anchor, string Reason, float PlayerX, string Flood, int Priced) MeetingAfter((float Vx, int Ticks)[] script, bool placingTorches)
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
                companion.Brain.Senses.Update(companion.NPC, player);
            }
        player.itemAnimation = 0;
        held.TurnToAir();
        var meeting = companion.Brain.Meeting;
        // The observation is frozen here; later calls only let the companion's flood settle.
        for (int i = 0; i < 60; i++)
        {
            // The centre: the resolver's first parameter is the body's centre, and it roots its own free-space
            // flood at the nearest usable corner to it. A resting orb's `Bottom` is the floor line itself.
            meeting.Resolve(companion.NPC.Center, companion.Brain.Senses.Player, companion.Brain.Senses.Intent.Region, Main.GameUpdateCount + (ulong)i);
            if (meeting.Reason is not ("meeting-undecided" or "retained-while-undecided")) break;
        }
        Console.WriteLine($"meeting candidates ({(placingTorches ? "torches" : "walk")}): flood={meeting.FloodState} priced={meeting.Priced} "
            + $"body={companion.NPC.Center} playerFeet={player.Bottom} "
            + string.Join(" ", meeting.Candidates.Select(c => $"{c.Tile.X},{c.Tile.Y}:{(c.CompanionTicks is float t ? t.ToString("0.0") : "unpriced")}")));
        return (meeting.Destination, meeting.Reason, player.Bottom.X, meeting.FloodState, meeting.Priced);
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
        var arms = new List<(bool Reconnects, int DecidedAt, string Decision, float AnchorX, float PlayerX, float Moved, bool Arrived, string Evidence)>();
        foreach (int gapAt in new[] { 76, -1 })
        {
            bool reconnects = gapAt > 0;
            BuildParallelRoutes(gapAt);
            var companion = VerifyCompanionLifecycle.Create();
            companion.Brain.Actions.RemoveAll(a => a.Name != "keep-company");
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
                companion.Brain.Senses.Update(companion.NPC, player);
            }
            int decidedAt = -1, lastTick = 0;
            string decision = "";
            float anchorX = 0, playerX = 0, startX = 0, moved = 0;
            bool arrived = false;
            var trace = new List<string>();
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
                if (decidedAt >= 0 && (tick - decidedAt) % 30 == 0 && tick - decidedAt <= 240)
                    trace.Add($"+{tick - decidedAt}:{(companion.NPC.Bottom.X - startX) / 16:+0.0;-0.0}"
                        + $"/{companion.Brain.Navigator.Status}");
                arrived = companion.Brain.Senses.Intent.Objective.IsSatisfied(companion.NPC.Center);
                if (arrived && decidedAt >= 0 && tick > decidedAt + 60) break;
            }
            string evidence = $"reconnects={reconnects}; decision={decision} at tick {decidedAt}; anchorX={anchorX / 16:0.0}; playerX={playerX / 16:0.0}; "
                + $"moved {moved / 16:0.0} tiles in the 60 ticks after deciding; arrived={arrived} by tick {lastTick}; meeting={companion.Brain.Meeting.Reason}; "
                + $"feet={companion.NPC.Bottom}; player={player.Bottom}; goal={companion.Brain.Positioner.Chosen}; status={companion.Brain.Navigator.Status}; "
                + $"forward by tick offset: {string.Join(" ", trace)}";
            // Both arms are measured before either is judged. They are a matched pair whose whole point is the
            // difference between them, and asserting inside the loop meant the first arm's failure aborted
            // before the second arm had produced the number it is compared against — so a red here could never
            // say whether the pair still discriminates.
            arms.Add((reconnects, decidedAt, decision, anchorX, playerX, moved, arrived, evidence));
            Console.WriteLine($"meeting place {(reconnects ? "gap ahead" : "cliff")}: {decision} at tick {decidedAt}, anchor {(anchorX - playerX) / 16:+0.0;-0.0} tiles from the player, "
                + $"companion moved {moved / 16:+0.0;-0.0} tiles in 60 ticks, reunited by tick {lastTick}");
        }
        string both = string.Join(" || ", arms.Select(a => (a.Reconnects ? "gap-ahead" : "cliff") + ": " + a.Evidence));
        foreach (var arm in arms)
        {
            Require(arm.DecidedAt >= 0, $"a travelling player's meeting place must be priced from the companion's routes; {both}");
            Require(arm.Arrived, $"reunion must complete through the production brain; {both}");
        }
        foreach (var arm in arms)
            // "Ahead" is held to more than the player's own body plus a tile rather than to a fixed eight tiles, because the
            // meeting place is the soonest point on his journey the companion's routes reach, and a faster body reaches it
            // sooner and so nearer him: at twice his speed the anchors read +21.6 and +16.0 tiles, at three times +10.6 and
            // +5.0 (15 September 2026). The decision reason is what tells this from the player-position fallback; the lead
            // only refuses an "ahead" anchor standing at his feet.
            Require(arm.Decision == "meeting-ahead-priced" && arm.AnchorX > arm.PlayerX + 3 * 16,
                $"a travelling player must be met on the journey ahead of him; {both}");
        // DELETED: the directional half of this pair, which required the gap-ahead arm to set off forwards by
        // more than three tiles in its first sixty ticks and the cliff arm to set off backwards by more than
        // three. It is deleted rather than retuned, because no threshold can restore it: the two arms no longer
        // differ.
        //
        // The pair was a walker's contract, and what broke it is which openings the body can use rather than
        // any ability to ignore terrain. The orb is still stopped by tiles — the engine's box collision is off
        // for it, but the motor's circle contact is the body, and it no more passes through the row-76 floor
        // than the walker did. What changed is that the upper floor spans columns 26 to 90, so its open left
        // end is an opening the orb can simply rise through, with no ledge to climb and no jump to prove. From
        // column 35 that opening is about nine tiles behind, against a gap forty-one tiles ahead that then has
        // to be walked back from — so the way up behind is the cheaper route to the anchor in *both* arms, and
        // the gap at 76 stops being the thing that decides anything. Measured on this scene, the two arms
        // differ by about a tile and a half and by one tick:
        //
        //     gap ahead   -7.1  +1.9  +13.1  +24.4  +35.6  +41.8   arrived tick 395, ended (1486.03, 1177.41)
        //     cliff       -5.6  +4.1  +15.2  +26.5  +37.6  +42.5   arrived tick 396, ended (1486.18, 1177.46)
        //
        // Read the first column rather than the threshold: both arms set off *backwards*, and the gap-ahead arm
        // was failing on a sign, not on a margin. That reversal is the trip to the upper floor's open left end,
        // not a body drifting, and it is printed above rather than asserted because nothing here establishes
        // what the right amount of it is.
        //
        // What survives is what the scene can still witness on both terrains, and both arms are held to it: the
        // player is met on the journey ahead of him, priced from the companion's own routes, and reunion
        // completes through the production brain. Anyone reviving a directional contract for this body needs a
        // scene where the opening ahead is genuinely the only one — an upper floor sealed at both of its ends,
        // rather than one left open at its left end — and should know that this geometry is not that.
    }

    /// <summary>
    /// The companion's flood keeps its progress while the body moves inside the region it has proven it can
    /// return from. Restarting it whenever the re-root cadence passed meant a flood needing more than one
    /// cadence never finished, so reunion in a large cave never got a priced place. Every call here arrives
    /// after the cadence with the body one tile along, which restarted the old flood on each call. While
    /// nothing is decided the anchor is the nearest place inside the player's region to the body.
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
                companion.Brain.Senses.Update(companion.NPC, player);
            }
            var meeting = companion.Brain.Meeting;
            var sense = companion.Brain.Senses.Player;
            ulong start = Main.GameUpdateCount + 1;
            int cadence = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.MeetingRerootTicks;
            var region = companion.Brain.Senses.Intent.Region;
            Vector2 first = meeting.Resolve(companion.NPC.Center, sense, region, start);
            // Restated on 15 September 2026. The undecided anchor was the region's leading edge, and the row also required it to
            // stand off the player's feet, which was a property of that edge. It is now the nearest place inside the region to
            // the body, because rejoining means being inside the region again and inside it the companion moves about with the
            // region rather than aiming anywhere in it; the nearest place inside may be anywhere in the box, feet included.
            Vector2 nearestInside = region.NearestInside(companion.NPC.Center, live::AICompanion.Companion.Brain.Infrastructure.Movement.Navigator.SettleRadius);
            Require(meeting.Reason == "meeting-undecided",
                $"one slice must not finish the flood, or nothing about retaining its progress is tested; reason={meeting.Reason} flood={meeting.FloodState}");
            Require(Vector2.Distance(first, nearestInside) < 1f,
                $"an undecided meeting place must aim at the nearest place inside the player's region; anchor={first} nearest-inside={nearestInside} feet={sense.Bottom}");
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
        // Column 55, beyond the player's region, not 40. Restated on 15 September 2026: a meeting place is what rejoining aims at,
        // and a companion ten tiles from a still player is inside his region, where the positioner chooses no place at all, so
        // the priced place this row drops could never have been taken in the first place.
        companion.NPC.position = new Vector2(55 * 16 - companion.NPC.width / 2f, 80 * 16 - companion.NPC.height);
        companion.Brain.Senses.Update(companion.NPC, player);
        Require(companion.Brain.Senses.Intent.Region.GapBeyond(companion.NPC.Center) > 0,
            $"the premise: the companion must begin beyond the player's region; gap={companion.Brain.Senses.Intent.Region.GapBeyond(companion.NPC.Center)} half={companion.Brain.Senses.Intent.Region.HalfSize}");
        var positioner = companion.Brain.Positioner;
        Vector2 place = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.HoverPoint(new Point(80, 79));
        positioner.Resolve(new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(RequestKind.WithPlayer, place, MeetingPlace: true),
            companion.Brain.Senses);
        Require(positioner.Chosen is Vector2 held && Vector2.Distance(held, place) < 1f && positioner.ChoiceReason == "priced-meeting-place",
            $"a priced meeting place must first become the exact destination, or dropping it tests nothing; chosen={positioner.Chosen} reason={positioner.ChoiceReason}");
        positioner.Resolve(new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(RequestKind.WithPlayer, player.Bottom),
            companion.Brain.Senses);
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
            // The held key, which this row was missing and which decides the whole measurement. The
            // intent region's horizontal lead reads `PlayerSense.HeldMove`, and that is
            // `controlLeft`/`controlRight` alone — displacement feeds only the vertical part. Walking
            // him by writing position and velocity therefore produces a region with *zero* lead however
            // far he goes, so the box stays centred on him, its rear never closes, and this row asks the
            // companion to lead a player the brain was never told is going anywhere. Sibling rows in
            // this file already hold it; this one did not, and read 57.9% ahead against a bar of 66.7%.
            player.controlRight = true;
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
        // Released, because a held key is process state and the next case reads the same player.
        player.controlRight = false;
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
    /// The rejoin pull measured on the live region of a running player: exactly zero at every sampled point inside the
    /// rectangle, no one-pixel step anywhere along a line out through its edge, and never above the far cap however far
    /// out the line goes short of the hard leash.
    ///
    /// <para>The row it replaces sampled a curve with two halves, a central gradient inside and a slope outside, and asserted
    /// they met without a step. The owner ruled on 15 September 2026 that there is no pull anywhere inside the region, so the
    /// central half and the row that kept it alive went, and what is left to hold is simpler and stricter: the inside is flat
    /// zero, the outside starts from zero at the edge, and the whole curve stops at the cap, because rejoining is the fallback
    /// and never on its own outweighs a job genuinely worth doing. The player runs so the region is led and grown, which is
    /// the shape the edge is hardest to get right in.</para>
    /// </summary>
    private static void VerifyTheRejoinPullIsZeroInsideTheRegionAndHasNoStepAtItsEdge()
    {
        BuildLongFloor();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.Bottom = new Vector2(240f, 1280f);
        companion.NPC.Bottom = new Vector2(240f, 1280f);
        companion.NPC.velocity = Vector2.Zero;
        const float RunningSpeed = 6f;
        for (int tick = 0; tick < 600; tick++)
        {
            player.velocity = new Vector2(RunningSpeed, 0f);
            player.Bottom += player.velocity;
            player.controlRight = true;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.Brain.Senses.Update(companion.NPC, player);
        }
        player.controlRight = false;
        var region = companion.Brain.Senses.Intent.Region;
        // The lead chases the held key, not the observed pace, so a run without one reads no lead at
        // all; and the lead it chases is the third-line clamp, 240 x 1.15 / 3 = 92 px fully grown, not
        // the half-screen the old threshold was written against. Six hundred ticks saturate it.
        Require(companion.Brain.Senses.Player.IsTravelling && region.Lead.X > 90f, FormattableString.Invariant(
            $"the curve row needs a led region, or its edge is the easy case: lead={region.Lead}"));
        float cap = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.KeepCompanyFarCap;
        float leash = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.LeashHard;
        float span = MathF.Max(1f, live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current.RecoveryRadius
            - MathF.Max(region.HalfSize.X, region.HalfSize.Y));
        float allowed = 1.5f / span;
        float worstStep = 0f, worstAt = 0f, highest = 0f, insideMax = 0f;
        int insideSamples = 0;
        foreach (Vector2 direction in new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, -1), new Vector2(0, 1), Vector2.Normalize(new Vector2(1, -1)) })
        {
            float previous = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany.RejoinPull(region, region.Centre);
            for (float offset = 1f; offset <= leash; offset += 1f)
            {
                Vector2 point = region.Centre + direction * offset;
                float here = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany.RejoinPull(region, point);
                if (region.Contains(point)) { insideSamples++; insideMax = MathF.Max(insideMax, here); }
                highest = MathF.Max(highest, here);
                float step = MathF.Abs(here - previous);
                if (step > worstStep) { worstStep = step; worstAt = offset; }
                previous = here;
            }
        }
        Console.WriteLine(FormattableString.Invariant(
            $"rejoin pull: {insideSamples} samples inside the region, highest there {insideMax:F5}; worst one-pixel step {worstStep:F5} at {worstAt:F0}px, a pixel buys at most {allowed:F5}; highest anywhere short of the leash {highest:F3} against a cap of {cap:F3}"));
        Require(insideSamples > 0 && insideMax == 0f, FormattableString.Invariant(
            $"there is no pull anywhere inside the region: highest inside {insideMax:F5} over {insideSamples} samples"));
        Require(worstStep <= allowed, FormattableString.Invariant(
            $"the rejoin pull must have no step: worst one-pixel change {worstStep:F5} at {worstAt:F0}px, a pixel buys at most {allowed:F5}"));
        Require(highest <= cap + 1e-5f, FormattableString.Invariant(
            $"the rejoin pull must never exceed the far cap short of the hard leash: highest {highest:F4} against {cap:F4}"));
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
        companion.Brain.Senses.Update(companion.NPC, player);
        var still = companion.Brain.Senses.Intent.Region;
        for (int tick = 0; tick < 240; tick++)
        {
            // A diagonal climb, which is what a hill is: along and up together.
            player.velocity = new Vector2(2f, -1.5f);
            player.Bottom += player.velocity;
            player.controlRight = true;
            player.controlUp = true;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.Brain.Senses.Update(companion.NPC, player);
        }
        player.controlRight = false;
        player.controlUp = false;
        var climbing = companion.Brain.Senses.Intent.Region;
        float cap = 1f + live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.IntentRegionGrowthCap;
        // The upward allowance is h/3 less the slack, 110.4/3 - 32 = 4.8 px fully grown: the player
        // sits near the top of his own box, so there is almost nowhere to lead up to, and the climb
        // is carried by growth and the box sitting above him rather than by the lead. The old line
        // predates the clamp; what is asserted is the pin the travel presses into it.
        Require(climbing.Lead.Y < -4f && climbing.Centre.Y < player.Bottom.Y - 16f, FormattableString.Invariant(
            $"a climbing player's region must lead above his feet: lead={climbing.Lead} centre={climbing.Centre} feet={player.Bottom}"));
        Require(climbing.HalfSize.Y > still.HalfSize.Y && climbing.HalfSize.X > still.HalfSize.X, FormattableString.Invariant(
            $"the region must grow on both axes with the lead, not only along it: still={still.HalfSize} climbing={climbing.HalfSize}"));
        Require(climbing.HalfSize.Y <= still.HalfSize.Y * cap + 0.01f && climbing.HalfSize.X <= still.HalfSize.X * cap + 0.01f,
            FormattableString.Invariant($"growth must stop at the cap: still={still.HalfSize} climbing={climbing.HalfSize} cap={cap}"));
        // Where the region says he is going, on the climb's own side of him. The row read the region's leading edge until
        // 15 September 2026; the edge went when rejoining began aiming at the nearest place inside the region instead.
        Require(climbing.Heading.Y < player.Center.Y, FormattableString.Invariant(
            $"where the region says the player is going must be on the climb's own side of him: heading={climbing.Heading} player={player.Center}"));
    }

    /// <summary>
    /// A body on real sloped ground inside the player's region is with him.
    ///
    /// <para>Restated on 15 September 2026, when being with the player became being inside his region. The row used to ask
    /// whether a body resting over a slope could build the settled streak arrival then waited for, because the rest test had
    /// only ever been asked on flat ground, and a companion that could never settle on a hillside could never satisfy
    /// following there. Nothing waits for a rest now. What the row was protecting is still asked: a companion over sloped
    /// ground inside the player's region is with him. It is asked upslope of him rather than below. The region sits mostly
    /// above the player by the owner's ruling, with a third of its half-height below his centre, and two columns down a 1:1
    /// hill the body sat one pixel under the region's bottom edge on the first run of the restated row — outside, which is
    /// that ruling working rather than a defect.</para>
    ///
    /// <para>The engine rests a body on a slope's diagonal rather than on a tile's top edge, so the hill is built
    /// from the game's own slope shapes and not from a staircase of full blocks, and the row asserts that it is
    /// standing on one before it reads anything. It deliberately does not walk the slope: a 1:1 slope walk still
    /// times out (AIC-212), and a row that required the climb would be red for a defect this lane does not own.</para>
    /// </summary>
    private static void VerifyABodyOnSlopedGroundInsideTheRegionIsWithThePlayer()
    {
        BuildSlopedHill();
        var companion = VerifyCompanionLifecycle.Create();
        var brain = companion.Brain;
        Player player = Main.player[0];
        player.dead = false;
        // Both on the hill, two columns apart, the companion upslope of the player.
        const int PlayerColumn = 47, CompanionColumn = 49;
        player.Bottom = new Vector2(PlayerColumn * 16f + 8f, (80 - (PlayerColumn - HillLeft)) * 16f);
        player.velocity = Vector2.Zero;
        // Twelve pixels above the slope's own row rather than eighteen: the row below reads the tile under the
        // tile the *centre* sits in, so a body parked a whole tile clear would be reading air and failing its
        // own premise, while twelve is still past the ten-pixel radius so nothing starts inside terrain.
        companion.NPC.Center = new Vector2(CompanionColumn * 16f + 8f, (80 - (CompanionColumn - HillLeft)) * 16f - 12f);
        companion.NPC.velocity = Vector2.Zero;

        // Let native collision settle the body onto the diagonal. Nothing is asserted about how long that takes;
        // what matters is the state it comes to rest in.
        for (int tick = 0; tick < 60; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            AdvanceNative(companion);
        }
        Point at = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.Tile(companion.NPC.Center);
        var shape = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World.Shape(at.X, at.Y + 1);
        Console.WriteLine(FormattableString.Invariant(
            $"sloped ground: body rests at {companion.NPC.Center} in tile {at.X},{at.Y} over a {shape} with v={companion.NPC.velocity}"));
        // The premise, in two parts. The ground under the body has to be an actual slope, which for this body is
        // a full wall rather than a ramp; and the body has to have come to rest beside it rather than be drifting.
        Require(shape is live::AICompanion.Companion.Brain.Infrastructure.Movement.TileShape.SolidLowerLeft
                or live::AICompanion.Companion.Brain.Infrastructure.Movement.TileShape.SolidLowerRight,
            FormattableString.Invariant($"the body must come to rest over a sloped tile, or the row is the flat one again; tile under it is {shape}"));
        // The body is observed once where it came to rest over the hill, exactly as the brain observes it every tick; being
        // with the player is being inside his region, with no streak to build first.
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        brain.Senses.Update(companion.NPC, player);
        var objective = brain.Senses.Intent.Objective;
        var slopeRegion = brain.Senses.Intent.Region;
        Console.WriteLine(FormattableString.Invariant(
            $"sloped ground: inside={brain.Senses.Intent.Inside} gap={slopeRegion.GapBeyond(companion.NPC.Center):0.0} region={slopeRegion.Centre} half={slopeRegion.HalfSize}"));
        Require(brain.Senses.Intent.Inside && objective.IsSatisfied(companion.NPC.Center), FormattableString.Invariant(
            $"a body over a slope inside the region must be with the player: inside={brain.Senses.Intent.Inside} reason={objective.Reason(companion.NPC.Center)} companion={companion.NPC.Center} player={player.Bottom} region={slopeRegion.Centre} half={slopeRegion.HalfSize}"));
    }

    /// <summary>
    /// Ticks 8127 and 8128 of the 2026-09-14 capture, as geometry: both bodies in the air and moving, the companion inside
    /// the region. Keeping company flipped to its local method there and issued a Hold that cancelled a jump one tick after
    /// take-off, so for as long as the two methods asked for different requests a moving tick was refused as arrival.
    ///
    /// <para>Restated on 15 September 2026. A moving body inside the region is with the player now, because there is no
    /// arrival to refuse and both methods ask for the same kind of request. What the capture's ticks still demand is the
    /// harm itself: keeping company must not change method between two moving ticks inside the region.</para>
    /// </summary>
    private static void VerifyAMovingTickInsideTheRegionKeepsTheMethod()
    {
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        var brain = companion.Brain;
        Player player = Main.player[0];
        player.dead = false;
        // Both bodies moving, a third of a tile apart horizontally: well inside any comfort box.
        player.Bottom = new Vector2(600f, 1230f);
        player.velocity = new Vector2(0f, -6.4f);
        companion.NPC.Center = new Vector2(620f, 1216f);
        companion.NPC.velocity = new Vector2(2f, -6.4f);
        // The sense reads the body's speed from the motor's own momentum rather than from the NPC's velocity,
        // so a scene that means "moving" has to put the motor there: one application at that velocity is what
        // makes the momentum real, and without it the row would be a stationary body wearing a velocity.
        companion.Motor.Steer(new Vector2(2f, -6.4f), "fixture");
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        brain.Senses.Update(companion.NPC, player);
        var request = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
            RequestKind.WithPlayer, player.Bottom);
        brain.Positioner.Resolve(request, brain.Senses);
        Require(brain.Positioner.FollowObjectiveSatisfied, FormattableString.Invariant(
            $"a moving body inside the region is with the player: companion={companion.NPC.Center} v={companion.Motor.State.Velocity} player={player.Bottom} vy={player.velocity.Y} reason={brain.Positioner.FollowObjectiveReason} inside={brain.Senses.Intent.Inside}"));
        var company = brain.Actions.OfType<live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany>().Single();
        var context = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, brain.Senses);
        company.Prepare(context);
        string movingMethod = company.EligibilityReason;
        companion.Motor.Steer(new Vector2(2f, -6.0f), "fixture");
        companion.NPC.Center += new Vector2(2f, -6.0f);
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        brain.Senses.Update(companion.NPC, player);
        company.Prepare(context);
        Require(company.EligibilityReason == movingMethod, FormattableString.Invariant(
            $"keeping company must not change method on a moving tick: {movingMethod} became {company.EligibilityReason}"));
    }

    /// <summary>
    /// The other end of the same rule. A player who stops leaves a region that drifts back over the
    /// filter's time constant rather than snapping, and the companion must stay with him inside it without
    /// the method flickering while it does. The ceiling is declared here rather than discovered: one change
    /// into the inside method is allowed and nothing after it. Restated on 15 September 2026: the row also
    /// counted resting ticks filed as moving deferrals, a reason the follow objective no longer has, and
    /// required the body to end within the follow comfort of his feet, which a companion moving about the
    /// whole region by design does not; it now requires the body to end inside his region.
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
        var company = brain.Actions.OfType<live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany>().Single();
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
        var flips = new List<string>();
        for (int tick = 0; tick < 600; tick++)
        {
            float speed = companion.Motor.State.Velocity.Length();
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI();
            AdvanceNative(companion);
            // The method is read as the brain's own preparation left it. This loop used to call Prepare again after the tick,
            // which ran the method choice a second time per tick — advancing its rescore wait twice as fast and able to flip the
            // method itself — so the instrument was changing what it counted; removing it moved the count from 39 to 32 and was
            // not the cause.
            if (company.EligibilityReason != method)
            {
                if (method.Length > 0)
                {
                    changes++;
                    flips.Add(FormattableString.Invariant(
                        $"+{tick}:{method}->{company.EligibilityReason} speed={speed:0.00} follow={brain.Positioner.FollowObjectiveReason} inside={brain.Senses.Intent.Inside} gap={brain.Senses.Intent.Region.GapBeyond(companion.NPC.Center):0.0} half={brain.Senses.Intent.Region.HalfSize} centre={brain.Senses.Intent.Region.Centre} body={companion.NPC.Center}"));
                }
                method = company.EligibilityReason;
            }
        }
        string flipTrace = string.Join(" | ", flips);
        const int SettleChangeCeiling = 1;
        Require(changes <= SettleChangeCeiling, FormattableString.Invariant(
            $"a stopped player must let the method settle: {changes} method changes over 600 ticks, ceiling {SettleChangeCeiling}, ending {method}; {flipTrace}"));
        Require(brain.Senses.Intent.Inside, FormattableString.Invariant(
            $"the companion must end inside a stopped player's region: companion={companion.NPC.Center} player={player.Bottom} region={brain.Senses.Intent.Region.Centre} half={brain.Senses.Intent.Region.HalfSize}"));
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
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
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
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
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
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
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
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
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
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
    }

    internal static void AdvanceNative(live::AICompanion.Companion.CharacterBody.CompanionNPC companion)
    {
        // The engine finishes a no-gravity, no-tile-collide NPC's tick by adding its velocity to
        // its position and nothing else (NPC.UpdateNPC_Inner skips UpdateCollision for it); the
        // motor has already resolved contact on that displacement.
        NPC npc = companion.NPC;
        npc.oldPosition = npc.position;
        npc.position += npc.velocity;
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
