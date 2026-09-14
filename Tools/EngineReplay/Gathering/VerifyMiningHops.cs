extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using FindToolAccess = live::AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess;
using MineOre = live::AICompanion.Companion.Brain.Activities.Gathering.MineOre;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using AttemptStatus = live::AICompanion.Companion.Brain.Activities.AttemptStatus;
using OfferEligibility = live::AICompanion.Companion.Brain.Activities.OfferEligibility;
using LiveTerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using LiveMovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using LiveReach = live::AICompanion.Companion.Brain.Infrastructure.Movement.Reachability.Reach;
using LiveLimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;

/// <summary>
/// Ceiling-ore hops against the ways a take-off stops being one: an arrival that slides off an overhang, a
/// body displaced after the take-off was chosen, a take-off that stops proving a jump with the body at rest
/// on it, and a scan the planning deadline cuts short. Also a ceiling tall enough to need most of a jump's
/// rise and a take-off on a ledge, which the minimal flat case in the ore suite cannot distinguish.
/// </summary>
internal static class VerifyMiningHops
{
    private const string LostTakeOff = "interaction-jump-lost-take-off";

    public static int Run()
    {
        int red = 0;
        // Whole-brain fixtures catch a phase by condition (walking within reach of a take-off, at rest on it), and
        // under the wall-clock planning allowances machine load decides how far a search gets and so which phase
        // the loop catches; they run with the allowances lifted. The deadline fixture is the exception: a lifted
        // limit sets no deadline at all, so the expiry it proves could never fire.
        void Each(string name, Action fixture, bool productionAllowances = false)
        {
            LiveLimitPlanningWork.Unbounded = !productionAllowances;
            try { fixture(); Console.WriteLine($"GREEN {name}"); }
            catch (Exception e) { red++; Console.WriteLine($"RED {name}: {e.Message}"); }
            finally { LiveLimitPlanningWork.Unbounded = false; }
        }
        Each("pit-edge", APitEdgeTakeOffSurvivesAWalkingArrival);
        Each("displaced-landed", () => ADisplacedHopTargetIsReleased(true));
        Each("displaced-placed", () => ADisplacedHopTargetIsReleased(false));
        Each("lost-take-off", ALostTakeOffEndsTheAttemptAndIsNotReoffered);
        Each("deadline", HopScansCutShortByTheDeadlineAreUnknown, productionAllowances: true);
        Each("tall-ceiling", ATallCeilingNeedsMostOfTheJumpsRise);
        Each("ledge", ALedgeTakeOffIsClimbedToAndLandedOn);
        Each("G1 narrow top refused by arrival-slide admission (known limitation)", ANarrowTopIsRefusedByArrivalSlideAdmission);
        if (red > 0) return red;
        MeasureStandingNoHopCost(warmUp: true);
        MeasureStandingNoHopCost(warmUp: false);
        MeasureTraversalJumpHopCost(warmUp: true);
        MeasureTraversalJumpHopCost(warmUp: false);
        Console.WriteLine("mining hops: pit-edge arrival, displaced take-off, lost take-off outcome, deadline-bounded scans, tall ceiling and ledge take-off pass");
        return 0;
    }

    /// <summary>
    /// A take-off beside a pit edge. BodyPhysics.Stand accepts a pose whose body overhangs the edge by two
    /// pixels; the 2026-09-13 review measured that pose mining from rest and sliding into the pit when the
    /// body arrived at walking speed. The admitted take-off must hold a body arriving at walking speed from
    /// either side under the game's own NPC collision, and the whole brain must mine without ever dropping
    /// below the floor it approached on.
    /// </summary>
    private static void APitEdgeTakeOffSurvivesAWalkingArrival()
    {
        Point ore = new(23, 52);
        var ctx = BuildPitScene(ore, pitLeft: 21, pitRight: 26);
        ctx.Npc.Bottom = new Vector2(12 * 16 + 8, 60 * 16);
        LiveTerrainChanges.Reset();
        var hop = FindToolAccess.HopApproach(ore, ctx.Companion.Motor.State, ctx.Companion.Brain.Senses.Reach, out Vector2 takeOff);
        Require(hop == LiveReach.Yes, $"the pit scene's ceiling ore must still have a proven take-off; got {hop}");
        foreach (float arrival in new[] { BodyPhysics.WalkSpeed, -BodyPhysics.WalkSpeed })
        {
            var body = new BodyState(takeOff.X - BodyPhysics.Width / 2f, takeOff.Y, arrival, 0f, true);
            for (int tick = 0; tick < 60; tick++) body = VerifyEngineMotion.RunEngine(body, Controls.None);
            Require(body.OnGround && MathF.Abs(body.Bottom - takeOff.Y) < 0.5f,
                $"a body arriving at walking speed {arrival} on take-off {takeOff} must come to rest on it under native collision; ended at {body}");
        }
        float lowest = ctx.Npc.Bottom.Y;
        long last = ctx.Companion.Miner.LastOutcome?.Attempt ?? -1;
        int strikes = 0;
        for (int tick = 0; tick < 900 && Main.tile[ore.X, ore.Y].HasTile; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            lowest = MathF.Max(lowest, ctx.Npc.Bottom.Y);
            if (ctx.Companion.Miner.LastOutcome is { Productive: true } outcome && outcome.Attempt != last) { strikes++; last = outcome.Attempt; }
        }
        Require(!Main.tile[ore.X, ore.Y].HasTile && strikes > 0 && lowest <= 60 * 16 + 0.5f,
            $"the whole brain must mine the ceiling ore from a take-off it cannot slide off; broken={!Main.tile[ore.X, ore.Y].HasTile} strikes={strikes} lowest feet={lowest} take-off={takeOff}");
    }

    /// <summary>
    /// The body is displaced into the pit after choosing its take-off: dropped in from above, which is what
    /// knockback does, or set down on the pit floor without a fall. From the pit no take-off exists, so the
    /// mining offer must stop being usable and the attempt must close as a failed method within a bounded
    /// time, instead of requesting the unreachable take-off for ever as the review measured.
    /// </summary>
    private static void ADisplacedHopTargetIsReleased(bool landed)
    {
        Point ore = new(23, 52);
        var ctx = BuildPitScene(ore, pitLeft: 21, pitRight: 26);
        ctx.Npc.Bottom = new Vector2(12 * 16 + 8, 60 * 16);
        LiveTerrainChanges.Reset();
        var mine = ctx.Companion.Brain.Chooser.Actions.OfType<MineOre>().Single();
        int tick = 0;
        for (; tick < 300 && !(mine.TargetStandPosition is Vector2 stand && ctx.Companion.Brain.LastAction?.Name == "mine"
            && Vector2.Distance(ctx.Npc.Bottom, stand) < 48f); tick++)
            VerifyOreWork.AdvanceBrain(ctx);
        Require(mine.TargetStandPosition is Vector2 && mine.Eligibility == OfferEligibility.Usable && ctx.Npc.Bottom.Y <= 960.5f,
            $"the displacement fixture must catch the body walking to its take-off; tick={tick} status={mine.Status} feet={ctx.Npc.Bottom}");
        Vector2 takeOff = mine.TargetStandPosition!.Value;
        int jobBefore = mine.JobId;
        ctx.Npc.velocity = Vector2.Zero;
        ctx.Npc.Bottom = new Vector2(24 * 16 + 8, landed ? 73 * 16 : 75 * 16);
        long strikesBefore = ctx.Companion.Miner.LastOutcome?.Attempt ?? -1;
        int limit = landed ? 90 : live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.ObjectiveProgressWindowTicks + 90;
        int released = -1;
        for (int t = 0; t < limit + 60; t++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            bool stillOffered = mine.Eligibility == OfferEligibility.Usable && mine.TargetStandPosition == takeOff;
            if (!stillOffered) { released = t; break; }
        }
        Require(released >= 0 && released <= limit,
            $"a take-off the displaced body cannot reach must stop being offered within {limit} ticks (landed={landed}); released at {released}, status={mine.Status} offer={mine.Eligibility}/{mine.EligibilityReason} request={ctx.Companion.Brain.LastRequest}");
        for (int t = 0; t < 30; t++) VerifyOreWork.AdvanceBrain(ctx);
        var attempt = ctx.Companion.Brain.Chooser.Activity.RecentAttempts.LastOrDefault(a => a.Activity == "mine");
        Require((ctx.Companion.Miner.LastOutcome?.Attempt ?? -1) == strikesBefore && mine.JobId != jobBefore
            && attempt is { Status: AttemptStatus.Failed, ProductiveEffects: 0 },
            $"the released job must end as a failed method with no strike (landed={landed}); job {jobBefore}->{mine.JobId} attempt={attempt}");
    }

    /// <summary>
    /// Water pours onto a proven take-off while the body walks to it. Liquid moving is not a tile edit, so no
    /// terrain revision announces it, and a wet body cannot make the dry jump the take-off was proven with.
    /// Standing at rest on the take-off with no proof left must end the job as a failed method with that
    /// cause, and the same take-off must not be offered again while nothing has changed; a terrain change
    /// ends the wait and the ore is mined.
    /// </summary>
    private static void ALostTakeOffEndsTheAttemptAndIsNotReoffered()
    {
        Point ore = new(25, 52);
        var ctx = BuildCeilingScene(ore, slabTop: 51);
        // Far enough along the floor that the body has to walk to its take-off, so the water arrives first.
        ctx.Npc.Bottom = new Vector2(10 * 16 + 8, 60 * 16);
        LiveTerrainChanges.Reset();
        var mine = ctx.Companion.Brain.Chooser.Actions.OfType<MineOre>().Single();
        VerifyOreWork.AdvanceBrain(ctx);
        Require(mine.TargetStandPosition is Vector2 && mine.Status.Contains("take-off"),
            $"the lost-take-off fixture needs a proven hop job; status={mine.Status}");
        Vector2 takeOff = mine.TargetStandPosition!.Value;
        Point takeOffTile = LiveMovementQueries.FeetTile(takeOff);
        int firstJob = mine.JobId;
        for (int x = takeOffTile.X - 2; x <= takeOffTile.X + 2; x++)
        {
            Tile water = Main.tile[x, takeOffTile.Y];
            water.LiquidType = LiquidID.Water;
            water.LiquidAmount = 255;
        }
        long strikesBefore = ctx.Companion.Miner.LastOutcome?.Attempt ?? -1;
        for (int tick = 0; tick < 400 && mine.JobId == firstJob; tick++) VerifyOreWork.AdvanceBrain(ctx);
        for (int tick = 0; tick < 5; tick++) VerifyOreWork.AdvanceBrain(ctx);
        var attempt = ctx.Companion.Brain.Chooser.Activity.RecentAttempts.LastOrDefault(a => a.Activity == "mine");
        Require(Main.tile[ore.X, ore.Y].HasTile && (ctx.Companion.Miner.LastOutcome?.Attempt ?? -1) == strikesBefore
            && mine.JobId != firstJob && attempt is { Status: AttemptStatus.Failed, Cause: LostTakeOff, ProductiveEffects: 0 },
            $"a take-off that stops proving a jump at rest must end its job as a failed method with its cause; job {firstJob}->{mine.JobId} status={mine.Status} attempt={attempt}");
        for (int tick = 0; tick < 240; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            Require(!(mine.TargetStandPosition == takeOff || mine.Eligibility == OfferEligibility.Usable),
                $"the lost take-off must not be offered again while the terrain is unchanged; tick {tick} status={mine.Status} offer={mine.Eligibility}/{mine.EligibilityReason}");
        }
        for (int x = takeOffTile.X - 2; x <= takeOffTile.X + 2; x++) Main.tile[x, takeOffTile.Y].LiquidAmount = 0;
        Point elsewhere = new(80, 59);
        VerifyOreWork.Place(elsewhere, TileID.Dirt);
        LiveTerrainChanges.Changed(elsewhere.X, elsewhere.Y);
        var run = VerifyOreWork.RunBrainUntilBroken(ctx, ore, 900);
        Require(run.Broken, $"a terrain change must end the wait and let the drained take-off mine the ore; status={mine.Status} offer={mine.Eligibility}/{mine.EligibilityReason}");
    }

    /// <summary>
    /// Each hop pose runs a body simulation that never looked at the planning deadline. A scan the deadline cuts
    /// short must answer Unknown: where the unbounded scan says Yes (the body already stands on the take-off, so
    /// the walker question needs no search), and where it says No (ore above any jump's rise).
    /// </summary>
    private static void HopScansCutShortByTheDeadlineAreUnknown()
    {
        Point ore = new(25, 52);
        var ctx = BuildCeilingScene(ore, slabTop: 51);
        var reachable = FindToolAccess.HopApproach(ore, ctx.Companion.Motor.State, ctx.Companion.Brain.Senses.Reach, out Vector2 takeOff);
        Require(reachable == LiveReach.Yes, $"the deadline fixture needs a proven take-off; got {reachable}");
        // The row that asked the same question again standing on the take-off is gone rather than converted. It
        // tested the walker leg's dependence on where the body was asking from, and the scan no longer has one:
        // the flood it reads was run from the companion's feet, so a "from" argument was a parameter nothing read
        // and it went with the search that needed it. What survives is the deadline, which is the body proof's.
        Require(WithExpiredDeadline(() => FindToolAccess.HopApproach(ore, ctx.Companion.Motor.State, ctx.Companion.Brain.Senses.Reach, out _)) == LiveReach.Unknown,
            "a scan whose deadline passed before any proof ran has established nothing and must be Unknown, not Yes");
        // Ore above the floor's eye, so hop poses exist and each needs a body proof, under a low ceiling that stops
        // every jump at the head: the unbounded scan proves every pose and answers No.
        Point blocked = new(25, 49);
        var blockedCtx = BuildCeilingScene(blocked, slabTop: 48);
        for (int x = 5; x < 45; x++) VerifyOreWork.Place(new Point(x, 56), TileID.Dirt);
        LiveTerrainChanges.Reset();
        Require(FindToolAccess.HopApproach(blocked, blockedCtx.Companion.Motor.State, blockedCtx.Companion.Brain.Senses.Reach, out _) == LiveReach.No,
            "ore whose every jump is stopped by a low ceiling must be proven unreachable by an unbounded scan");
        Require(WithExpiredDeadline(() => FindToolAccess.HopApproach(blocked, blockedCtx.Companion.Motor.State, blockedCtx.Companion.Brain.Senses.Reach, out _)) == LiveReach.Unknown,
            "a scan cut short by the deadline must never report No for poses it never proved");
    }

    private static LiveReach WithExpiredDeadline(Func<LiveReach> scan)
    {
        LiveLimitPlanningWork.Begin(0.000001);
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!LiveLimitPlanningWork.Expired && clock.ElapsedMilliseconds < 50) { }
            Require(LiveLimitPlanningWork.Expired, "the deadline fixture could not expire the planning deadline");
            return scan();
        }
        finally { LiveLimitPlanningWork.End(); }
    }

    /// <summary>
    /// Ore six rows above the reach of standing work. The hop pose search is bounded by a jump's rise below the
    /// reach box, so a bound under seven rows cannot find this take-off; the minimal ceiling case needs only one.
    /// </summary>
    private static void ATallCeilingNeedsMostOfTheJumpsRise()
    {
        Point ore = new(25, 46);
        var ctx = BuildCeilingScene(ore, slabTop: 45);
        Vector2 standingFeet = ctx.Npc.Bottom;
        Require(FindToolAccess.Approach(ore, standingFeet, ctx.Companion.Brain.Senses.Reach, out _) != LiveReach.Yes, "the tall ceiling ore must be out of standing reach");
        var hop = FindToolAccess.HopApproach(ore, ctx.Companion.Motor.State, ctx.Companion.Brain.Senses.Reach, out Vector2 takeOff);
        Point takeOffTile = LiveMovementQueries.FeetTile(takeOff);
        Require(hop == LiveReach.Yes && takeOffTile.Y - ore.Y > Player.tileRangeY + 6,
            $"the tall ceiling ore must be proven only from a take-off more than six rows below standing reach; hop={hop} take-off tile={takeOffTile}");
        var run = VerifyOreWork.RunBrainUntilBroken(ctx, ore, 900);
        Require(run.Broken && run.StrikeFeet.All(feet => FindToolAccess.InReach(feet, ore) && feet.Y < standingFeet.Y - 1f),
            $"the whole brain must mine the tall ceiling ore from a raised body in reach; broken={run.Broken} strikes={string.Join("; ", run.StrikeFeet)}");
    }

    /// <summary>
    /// Ore a jump from the floor cannot reach and a jump from a two-tile ledge can. The take-off must be on the
    /// ledge, the whole brain must climb to it and mine, and the body must land back on the ledge.
    /// </summary>
    private static void ALedgeTakeOffIsClimbedToAndLandedOn()
    {
        Point ore = new(26, 44);
        var ctx = BuildCeilingScene(ore, slabTop: 43);
        for (int x = 22; x <= 30; x++)
            for (int y = 58; y <= 59; y++)
                VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
        LiveTerrainChanges.Reset();
        // The ledge was built after the ceiling scene flooded, and the take-off this row is about stands on it.
        VerifyOreWork.ResettleReach(ctx);
        var hop = FindToolAccess.HopApproach(ore, ctx.Companion.Motor.State, ctx.Companion.Brain.Senses.Reach, out Vector2 takeOff);
        Require(hop == LiveReach.Yes && LiveMovementQueries.FeetTile(takeOff).Y == 57,
            $"the only proven take-off must be on the ledge top; hop={hop} take-off={takeOff}");
        var mine = ctx.Companion.Brain.Chooser.Actions.OfType<MineOre>().Single();
        // Mining is the only activity, so the flight that finishes the ore is not steered by whatever wins next;
        // where that flight lands is the property, and following the player afterwards is a different question.
        ctx.Companion.Brain.Chooser.Actions.RemoveAll(action => action is not MineOre);
        var trace = new List<string>();
        var strikes = new List<Vector2>();
        long last = ctx.Companion.Miner.LastOutcome?.Attempt ?? -1;
        for (int tick = 0; tick < 1200 && Main.tile[ore.X, ore.Y].HasTile; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            if (ctx.Companion.Miner.LastOutcome is { Productive: true } outcome && outcome.Attempt != last) { strikes.Add(ctx.Npc.Bottom); last = outcome.Attempt; }
            if (tick % 30 == 0)
                trace.Add($"t{tick} feet={ctx.Npc.Bottom.X:0.0},{ctx.Npc.Bottom.Y:0.0} ground={ctx.Companion.Motor.State.OnGround} action={ctx.Companion.Brain.LastAction?.Name} "
                    + $"status={mine.Status} offer={mine.Eligibility}/{mine.EligibilityReason} request={ctx.Companion.Brain.LastRequest.Kind}@{ctx.Companion.Brain.LastRequest.Anchor} nav={ctx.Companion.Brain.Navigator.Status}");
        }
        bool broken = !Main.tile[ore.X, ore.Y].HasTile;
        Require(broken && strikes.All(feet => feet.Y < 58 * 16 - 1f),
            $"the whole brain must climb the ledge and mine from a hop off it; take-off={takeOff} broken={broken} strikes={string.Join("; ", strikes)}\n  " + string.Join("\n  ", trace));
        for (int tick = 0; tick < 120 && !ctx.Companion.Motor.State.OnGround; tick++) VerifyOreWork.AdvanceBrain(ctx);
        Require(ctx.Companion.Motor.State.OnGround && MathF.Abs(ctx.Npc.Bottom.Y - 58 * 16) < 0.5f,
            $"the hop must land back on the ledge; feet={ctx.Npc.Bottom}");
    }

    /// <summary>
    /// Brain cost on a scene where every discovered ore has a proven standing No, so each discovery pays a
    /// full hop scan: eight copper ores in a ceiling slab above any hop, a flat floor, production planning
    /// allowances. Prints decide and whole-tick timing; it asserts nothing, because a timing is a
    /// measurement of this machine rather than a property of the code.
    /// </summary>
    private static void MeasureStandingNoHopCost(bool warmUp)
    {
        Point placeholder = new(40, 59);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        for (int x = 5; x < 45; x++)
            for (int y = 43; y <= 44; y++)
                VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
        for (int i = 0; i < 8; i++) VerifyOreWork.Place(new Point(14 + i * 3, 44), TileID.Copper);
        LiveTerrainChanges.Reset();
        var decide = new List<double>();
        var whole = new List<double>();
        var clock = new System.Diagnostics.Stopwatch();
        for (int tick = 0; tick < 300; tick++)
        {
            clock.Restart();
            VerifyOreWork.AdvanceBrain(ctx);
            whole.Add(clock.Elapsed.TotalMilliseconds);
            decide.Add(ctx.Companion.Brain.DecideMs);
        }
        if (warmUp) return;
        static string Stats(List<double> samples)
        {
            var sorted = samples.OrderBy(v => v).ToList();
            return $"p50 {sorted[sorted.Count / 2]:0.000} p95 {sorted[(int)(sorted.Count * .95)]:0.000} max {sorted[^1]:0.000} mean {sorted.Average():0.000}";
        }
        var mine = ctx.Companion.Brain.Chooser.Actions.OfType<MineOre>().Single();
        Console.WriteLine($"hop cost, standing-No ceiling scene (300 ticks): decide {Stats(decide)} | tick {Stats(whole)} | mine offer={mine.Eligibility}/{mine.EligibilityReason}");
    }

    /// <summary>
    /// G1, a known limitation asserted as it stands so that a change to it is visible. Ore a jump from the floor cannot reach
    /// sits above a pillar two tiles wide and two tall. A jump from rest on the pillar top brings the ore into reach and the
    /// walker can climb there, yet no take-off on it is admitted, because admission requires support under the body through a
    /// walking-speed stopping slide either side of a tile-centred pose, and a top that narrow has drops on both sides within
    /// that slide. The same scene with the top three tiles wide admits a take-off on it.
    /// </summary>
    private static void ANarrowTopIsRefusedByArrivalSlideAdmission()
    {
        Point ore = new(26, 44);
        foreach (int width in new[] { 3, 2 })
        {
            var ctx = BuildCeilingScene(ore, slabTop: 43);
            for (int x = 25; x < 25 + width; x++)
                for (int y = 58; y <= 59; y++)
                    VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
            LiveTerrainChanges.Reset();
            // The top was built after the ceiling scene flooded, and it is the take-off this row is about.
            VerifyOreWork.ResettleReach(ctx);
            var hop = FindToolAccess.HopApproach(ore, ctx.Companion.Motor.State, ctx.Companion.Brain.Senses.Reach, out Vector2 takeOff);
            if (width == 3)
            {
                Require(hop == LiveReach.Yes && LiveMovementQueries.FeetTile(takeOff).Y == 57,
                    $"a three-wide top must admit a take-off on it; hop={hop} take-off={takeOff}");
                continue;
            }
            Require(hop == LiveReach.No,
                $"G1 changed: a two-wide top was refused by arrival-slide admission and the ore had no proven pose; now hop={hop} take-off={takeOff}. Update the Mining and WorldInteractions guides");
            Point top = new(26, 57);
            Vector2 feet = LiveMovementQueries.FeetWorld(top);
            var rest = ctx.Companion.Motor.State with
            {
                Left = feet.X - live::AICompanion.Companion.Brain.Infrastructure.Movement.BodyPhysics.Width / 2f, Bottom = feet.Y, Vx = 0f, Vy = 0f, OnGround = true,
                CollideX = false, Stuck = false, Pinned = false, Wet = false, StairFall = false, LiquidKind = 0,
            };
            Require(live::AICompanion.Companion.Brain.Infrastructure.Movement.ProveInteractionJump.CanReach(
                    live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World, rest, rising => FindToolAccess.InReach(rising.Feet, ore)),
                "premise: a jump from rest on the two-wide top must bring the ore into reach and land, or the refusal is physics rather than admission");
            Require(LiveMovementQueries.WalkerReach(LiveMovementQueries.FeetTile(ctx.Npc.Bottom), top) == LiveReach.Yes,
                "premise: the walker must reach the two-wide top");
        }
    }

    /// <summary>
    /// S1 measurement under the production planning allowances. Ceiling ore above a ledge four tiles tall, reached from the
    /// left over a two-tile step, so the walk to the take-off needs traversal jumps (a one-tile rise is climbed by native
    /// step-up and jumps nothing) whose landings are away from the take-off. Prints landings, hop-target releases with the
    /// decide time on each release tick, and decide and Gathering preparation timings; asserts nothing.
    /// </summary>
    private static void MeasureTraversalJumpHopCost(bool warmUp)
    {
        Point ore = new(26, 44);
        var ctx = BuildCeilingScene(ore, slabTop: 43);
        for (int x = 14; x <= 21; x++)
            for (int y = 58; y <= 59; y++)
                VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
        for (int x = 22; x <= 30; x++)
            for (int y = 56; y <= 59; y++)
                VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
        LiveTerrainChanges.Reset();
        ctx.Npc.Bottom = new Vector2(8 * 16 + 8, 60 * 16);
        ctx.Player.Bottom = ctx.Npc.Bottom;
        var brain = ctx.Companion.Brain;
        var mine = brain.Chooser.Actions.OfType<MineOre>().Single();
        brain.Chooser.Actions.RemoveAll(action => action is not MineOre);
        var progressFor = typeof(MineOre).GetField("hopProgressFor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var decide = new List<double>();
        var gathering = new List<double>();
        var releaseDecide = new List<string>();
        int landings = 0, releases = 0, ticks = 0;
        bool wasAir = false;
        object? lastProgress = null;
        for (; ticks < 1200 && Main.tile[ore.X, ore.Y].HasTile; ticks++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            decide.Add(brain.DecideMs);
            if (brain.ChoiceEvaluated)
                foreach (var family in brain.Chooser.Queries.LastFamilies)
                    if (family.Family.ToString() == "Gathering") gathering.Add(family.Milliseconds);
            bool air = !ctx.Companion.Motor.State.OnGround;
            if (wasAir && !air) landings++;
            wasAir = air;
            object? progress = progressFor.GetValue(mine);
            if (lastProgress != null && progress == null) { releases++; releaseDecide.Add($"{brain.DecideMs:0.000}@{ticks}"); }
            lastProgress = progress;
        }
        if (warmUp) return;
        Console.WriteLine($"hop cost, step-then-ledge walk to a take-off ({ticks} ticks, production allowances, broken={!Main.tile[ore.X, ore.Y].HasTile}): "
            + $"landings {landings}, hop-target releases {releases} (decide ms@tick {string.Join(", ", releaseDecide)}) | decide {Stats(decide)} | prepare Gathering {Stats(gathering)}");
    }

    private static string Stats(List<double> samples)
    {
        if (samples.Count == 0) return "no samples";
        var sorted = samples.OrderBy(v => v).ToList();
        return $"p50 {sorted[sorted.Count / 2]:0.000} p95 {sorted[(int)(sorted.Count * .95)]:0.000} max {sorted[^1]:0.000} mean {sorted.Average():0.000}";
    }

    /// <summary>A flat floor at row 60 under a two-row dirt slab whose lower row holds the ore, so the ore's only open face is underneath it.</summary>
    internal static ActionContext BuildCeilingScene(Point ore, int slabTop)
    {
        Point placeholder = new(40, 59);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        for (int x = 5; x < 45; x++)
            for (int y = slabTop; y <= ore.Y; y++)
                if (new Point(x, y) != ore) VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
        VerifyOreWork.Place(ore, TileID.Copper);
        LiveTerrainChanges.Reset();
        // The slab went up after the shared setup flooded, so the region describes a world without it. Every
        // take-off question below reads that region rather than searching, so a stale one answers about the
        // wrong scene and a never-flooded one answers "not yet known" about every pose.
        VerifyOreWork.ResettleReach(ctx);
        return ctx;
    }

    /// <summary>A floor at row 60 with a pit from <paramref name="pitLeft"/> to <paramref name="pitRight"/> down to row 75,
    /// under a two-row dirt ceiling holding the ore, so the ore's only open face is underneath it.</summary>
    private static ActionContext BuildPitScene(Point ore, int pitLeft, int pitRight)
    {
        var ctx = BuildCeilingScene(ore, slabTop: ore.Y - 1);
        for (int x = pitLeft; x <= pitRight; x++)
        {
            Main.tile[x, 60].ClearEverything();
            VerifyOreWork.Place(new Point(x, 75), TileID.Dirt);
        }
        for (int y = 60; y <= 75; y++)
        {
            VerifyOreWork.Place(new Point(pitLeft - 1, y), TileID.Dirt);
            VerifyOreWork.Place(new Point(pitRight + 1, y), TileID.Dirt);
        }
        LiveTerrainChanges.Reset();
        // The pit was dug after the ceiling scene flooded, and a pit is exactly the kind of edit the region
        // has to see: its floor is reachable one way and not the other.
        VerifyOreWork.ResettleReach(ctx);
        return ctx;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
