extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.SharedMovementSystem;
using FindToolAccess = live::AICompanion.Companion.Brain.WorldInteractions.FindToolAccess;
using MineOre = live::AICompanion.Companion.Brain.PurposeFamilies.Gathering.MineOre;
using WorkPolicy = live::AICompanion.Companion.Brain.Behaviours.Work.WorkPolicy;
using ActionContext = live::AICompanion.Companion.Brain.Behaviours.ActionContext;
using AttemptStatus = live::AICompanion.Companion.Brain.Behaviours.AttemptStatus;
using OfferEligibility = live::AICompanion.Companion.Brain.Behaviours.OfferEligibility;
using LiveTerrainChanges = live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges;
using LiveMovementQueries = live::AICompanion.Companion.Brain.SharedMovementSystem.MovementQueries;
using LiveReach = live::AICompanion.Companion.Brain.SharedMovementSystem.Reachability.Reach;
using LiveLimitPlanningWork = live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork;

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
        if (red > 0) return red;
        MeasureStandingNoHopCost(warmUp: true);
        MeasureStandingNoHopCost(warmUp: false);
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
        var hop = FindToolAccess.HopApproach(ore, ctx.Npc.Bottom, ctx.Companion.Motor.State, out Vector2 takeOff);
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
        int limit = landed ? 90 : live::AICompanion.Companion.Brain.BehaviourSelection.Weights.ObjectiveProgressWindowTicks + 90;
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
        var reachable = FindToolAccess.HopApproach(ore, ctx.Npc.Bottom, ctx.Companion.Motor.State, out Vector2 takeOff);
        Require(reachable == LiveReach.Yes, $"the deadline fixture needs a proven take-off; got {reachable}");
        Require(FindToolAccess.HopApproach(ore, takeOff, ctx.Companion.Motor.State, out _) == LiveReach.Yes,
            "standing on the take-off, the unbounded scan must prove it");
        Require(WithExpiredDeadline(() => FindToolAccess.HopApproach(ore, takeOff, ctx.Companion.Motor.State, out _)) == LiveReach.Unknown,
            "a scan whose deadline passed before any proof ran has established nothing and must be Unknown, not Yes");
        // Ore above the floor's eye, so hop poses exist and each needs a body proof, under a low ceiling that stops
        // every jump at the head: the unbounded scan proves every pose and answers No.
        Point blocked = new(25, 49);
        var blockedCtx = BuildCeilingScene(blocked, slabTop: 48);
        for (int x = 5; x < 45; x++) VerifyOreWork.Place(new Point(x, 56), TileID.Dirt);
        LiveTerrainChanges.Reset();
        Require(FindToolAccess.HopApproach(blocked, blockedCtx.Npc.Bottom, blockedCtx.Companion.Motor.State, out _) == LiveReach.No,
            "ore whose every jump is stopped by a low ceiling must be proven unreachable by an unbounded scan");
        Require(WithExpiredDeadline(() => FindToolAccess.HopApproach(blocked, blockedCtx.Npc.Bottom, blockedCtx.Companion.Motor.State, out _)) == LiveReach.Unknown,
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
        Require(FindToolAccess.Approach(ore, standingFeet, out _) != LiveReach.Yes, "the tall ceiling ore must be out of standing reach");
        var hop = FindToolAccess.HopApproach(ore, standingFeet, ctx.Companion.Motor.State, out Vector2 takeOff);
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
        var hop = FindToolAccess.HopApproach(ore, ctx.Npc.Bottom, ctx.Companion.Motor.State, out Vector2 takeOff);
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
        return ctx;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
