extern alias live;

using System.Reflection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using Microsoft.Xna.Framework;
using Terraria;


/// <summary>
/// P07's outward/return and resource-consuming-return contract. The shared round-trip query must
/// keep "can get there" apart from "can get back" and charge breath over both legs, and its
/// verdicts are held against what the native body actually does on the same terrain rather than
/// only against each other: a return the query refuses must end absent when the body tries it,
/// a return it grants must be walked, and the breath it charges must not be less than the native
/// body spends under water.
/// </summary>
internal static class VerifyRoundTripEvidence
{
    /// <summary>The companion's breathing rule in the query's units, built from the rule's own constants.</summary>
    // The walker's breath figures, kept as literals until this fixture is deleted with the walker.
    private static Reachability.BreathEnvelope FullBreath => new(200 * 7, 200 * 7, 3 * 7);

    public static int Run()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        double planBudget = Navigator.PlanMsBudget, preparationBudget = PlanLocalMovement.PreparationMsBudget;
        LimitPlanningWork.End();
        Navigator.PlanMsBudget = 0;
        PlanLocalMovement.PreparationMsBudget = 0;
        int failed = 0;
        try
        {
            failed += Case("a drop into a sealed pit has no return, a shallow pit returns, and native execution agrees", PitReturnMatchesNativeExecution);
            failed += Case("a spent search budget is an unknown return, never a return", SpentBudgetIsUnknownReturn);
            failed += Case("a planning deadline another search left behind does not starve a round trip", InheritedDeadlineDoesNotStarveRoundTrip);
            failed += Case("breath is charged over the return leg and never below the native body's submersion", BreathCoversTheReturnLeg);
            failed += Case("breath over a trip walked entirely under water is never charged below the native body's submersion", SubmergedWalkIsNotUnderCharged);
        }
        finally
        {
            Navigator.PlanMsBudget = planBudget;
            PlanLocalMovement.PreparationMsBudget = preparationBudget;
        }
        Console.WriteLine(failed == 0
            ? "round-trip evidence: outward/return separation, unknown return and breath over both legs pass against native execution"
            : $"round-trip evidence: {failed} case(s) failed");
        return failed;
    }

    private static int Case(string name, Action test) => VerifyMovementFailures.Case(name, test, "round trip");

    private static readonly Point Start = new(20, 79);

    /// <summary>
    /// X02/L05: a ten-row drop into a pit walled on both sides is reachable and has no way back; the
    /// same pit two rows deep has one. The query's return verdict is then tested by the native body:
    /// driven into the deep pit and asked to return, it must end absent without arriving; driven into
    /// the shallow one, it must walk back to the start.
    /// </summary>
    private static void PitReturnMatchesNativeExecution()
    {
        foreach (int depth in new[] { 10, 2 })
        {
            BuildPit(depth);
            Point goal = new(35, 79 + depth);
            var evidence = Reachability.RoundTrip(Start, goal, FullBreath);
            Console.WriteLine($"   pit depth {depth}: {evidence}");
            Require(evidence.Outward == Reachability.Reach.Yes, $"depth {depth}: the pit floor must be reachable, got {evidence.Outward}");
            Reachability.Reach expected = depth == 10 ? Reachability.Reach.No : Reachability.Reach.Yes;
            Require(evidence.Return == expected, $"depth {depth}: return must be {expected}, got {evidence.Return}");
            Require(depth != 10 || evidence.Breath == Reachability.Reach.Unknown, "breath cannot be judged over a trip with no return");

            var outward = new VerifyMovementFailures.Drive(Start);
            while (outward.Tick < 900 && !outward.Arrived) outward.Step(goal);
            Require(outward.Arrived, $"depth {depth}: the native body must reach the pit floor; last {outward.Describe()}");
            var back = new VerifyMovementFailures.Drive(outward.Body);
            while (back.Tick < 900 && !back.Arrived && back.Movement.Navigator.Failure != MovementFailure.AbsentTransition) back.Step(Start);
            Console.WriteLine($"   pit depth {depth} native return: arrived={back.Arrived} after {back.Tick}, failure {back.Movement.Navigator.LastFailure}");
            if (depth == 10)
                Require(!back.Arrived && back.Movement.Navigator.Failure == MovementFailure.AbsentTransition,
                    $"the native body in the deep pit must end absent without returning; last {back.Describe()}");
            else
                Require(back.Arrived, $"the native body in the shallow pit must return as the query says; last {back.Describe()}");
        }
    }

    /// <summary>
    /// "A reverse-search deadline is not a return certificate": the same deep pit asked with a
    /// budget too small to explore either direction must answer unknown both ways, and unknown breath.
    /// </summary>
    private static void SpentBudgetIsUnknownReturn()
    {
        BuildPit(10);
        var evidence = Reachability.RoundTrip(Start, new Point(35, 89), FullBreath, budget: 1);
        Console.WriteLine($"   spent budget: {evidence}");
        Require(evidence.Outward == Reachability.Reach.Unknown && evidence.Return == Reachability.Reach.Unknown,
            $"a one-expansion budget must establish nothing in either direction, got {evidence.Outward}/{evidence.Return}");
        Require(evidence.Breath == Reachability.Reach.Unknown, "breath must be unknown when neither leg was found");
    }

    /// <summary>
    /// The query's searches read the static per-search millisecond allowance, which the navigator's plan
    /// sets to its own planning allowance and leaves behind. A navigator starved to one work unit per tick
    /// plans once; then, with nothing else changed, the shallow pit's round trip must still answer yes both
    /// ways rather than inherit a deadline that stops each leg after its first node.
    /// </summary>
    private static void InheritedDeadlineDoesNotStarveRoundTrip()
    {
        BuildPit(2);
        Point goal = new(35, 81);
        double before = AStar.MsBudget;
        try
        {
            Navigator.PlanMsBudget = .000001;
            var leak = new VerifyMovementFailures.Drive(Start);
            leak.Step(goal);
            Navigator.PlanMsBudget = 0;
            double inherited = AStar.MsBudget;
            var evidence = Reachability.RoundTrip(Start, goal, FullBreath);
            Console.WriteLine($"   inherited deadline: per-search allowance after a starved plan {inherited} ms, round trip {evidence}, allowance after the round trip {AStar.MsBudget} ms");
            Require(evidence.Outward == Reachability.Reach.Yes && evidence.Return == Reachability.Reach.Yes,
                $"a round trip after a starved plan must answer from its own budget, got {evidence.Outward}/{evidence.Return}");
            Require(AStar.MsBudget == inherited, $"the round trip must leave the caller's allowance as it found it, {inherited} became {AStar.MsBudget}");
        }
        finally
        {
            Navigator.PlanMsBudget = 0;
            AStar.MsBudget = before;
        }
    }

    /// <summary>
    /// The resource-consuming return. A flooded basin with a submerged staircase out: a dry goal costs
    /// no breath at any reserve; the far end of the basin fits a full bar; the same trip with a reserve
    /// that covers the outward leg but not the return must be refused, which is the case that proves the
    /// return is charged at all. The native body is then driven there and back, and the submerged ticks
    /// the query charged must be no fewer than the ticks its head actually spent under water.
    /// </summary>
    private static void BreathCoversTheReturnLeg()
    {
        BuildBasin();
        var dry = Reachability.RoundTrip(Start, new Point(10, 79), FullBreath with { TicksLeft = 1 });
        Console.WriteLine($"   dry trip: {dry}");
        Require(dry.Breath == Reachability.Reach.Yes && dry.OutwardSubmergedTicks + dry.ReturnSubmergedTicks == 0,
            $"a dry round trip must cost no breath at any reserve, got {dry}");

        Point goal = new(58, 85);
        var full = Reachability.RoundTrip(Start, goal, FullBreath);
        Console.WriteLine($"   basin trip, full breath: {full}");
        Require(full.Outward == Reachability.Reach.Yes && full.Return == Reachability.Reach.Yes,
            $"the basin's far end must be reachable and returnable by the staircase, got {full.Outward}/{full.Return}");
        Require(full.OutwardSubmergedTicks > 0 && full.ReturnSubmergedTicks > 0, $"the basin trip must be charged under water both ways, got {full}");
        Require(full.Breath == Reachability.Reach.Yes, $"a full bar must cover this basin trip, got {full}");

        // The refusal half starts under water at the basin's near end. From the dry rim the approach
        // refills the bar before the water is reached, exactly as the breathing rule does, so a reduced
        // reserve there never binds; submerged for the whole trip, it must.
        Point underwater = new(36, 85);
        var submergedTrip = Reachability.RoundTrip(underwater, goal, FullBreath);
        Require(submergedTrip.Outward == Reachability.Reach.Yes && submergedTrip.Return == Reachability.Reach.Yes
            && submergedTrip.OutwardSubmergedTicks > 0 && submergedTrip.ReturnSubmergedTicks > 0,
            $"the under-water basin trip must be found and charged both ways, got {submergedTrip}");
        int enoughForOutward = submergedTrip.OutwardSubmergedTicks + submergedTrip.ReturnSubmergedTicks / 2;
        var short_ = Reachability.RoundTrip(underwater, goal, FullBreath with { TicksLeft = enoughForOutward });
        Console.WriteLine($"   under-water trip, full breath: {submergedTrip}");
        Console.WriteLine($"   under-water trip, reserve {enoughForOutward}: {short_}");
        Require(short_.Breath == Reachability.Reach.No,
            $"a reserve that covers the outward submersion but not the return's must be refused, got {short_}");

        int nativeSubmerged = 0;
        var outward = new VerifyMovementFailures.Drive(Start);
        while (outward.Tick < 2400 && !outward.Arrived)
        {
            outward.Step(goal);
            if (Submerged(outward.Body)) nativeSubmerged++;
        }
        Require(outward.Arrived, $"the native body must reach the basin's far end; last {outward.Describe()}");
        var back = new VerifyMovementFailures.Drive(outward.Body);
        while (back.Tick < 2400 && !back.Arrived)
        {
            back.Step(Start);
            if (Submerged(back.Body)) nativeSubmerged++;
        }
        int charged = full.OutwardSubmergedTicks + full.ReturnSubmergedTicks;
        Console.WriteLine($"   basin native: out {outward.Tick} ticks, back {back.Tick} ticks (arrived={back.Arrived}), head under water {nativeSubmerged} ticks, query charged {charged}");
        Require(back.Arrived, $"the native body must climb out and return as the query says; last {back.Describe()}");
        Require(charged >= nativeSubmerged, $"the query charged {charged} submerged ticks where the native head spent {nativeSubmerged}");
    }

    /// <summary>
    /// The basin's margin comes from its staircase: a jump or drop with one end under water is charged in
    /// full while the head is under for only part of it. A trip walked on a flat flooded floor has no such
    /// step, so every charged step is one the native head spends wholly under water, and this is the case
    /// that shows whether the rule errs high when the route is walking alone. The body is driven from rest
    /// out along the floor and back, and its head-underwater ticks are compared with the query's charge.
    /// </summary>
    private static void SubmergedWalkIsNotUnderCharged()
    {
        BuildFloodedCorridor();
        Point from = new(22, 89), to = new(56, 89);
        var trip = Reachability.RoundTrip(from, to, FullBreath);
        Console.WriteLine($"   flooded corridor trip: {trip}");
        Require(trip.Outward == Reachability.Reach.Yes && trip.Return == Reachability.Reach.Yes,
            $"the flooded corridor must be walkable both ways, got {trip.Outward}/{trip.Return}");
        Require(trip.OutwardSubmergedTicks == trip.OutwardTicks && trip.ReturnSubmergedTicks == trip.ReturnTicks,
            $"every step of a trip on a flooded floor must be charged as submerged, got {trip}");

        int outSubmerged = 0, backSubmerged = 0;
        var outward = new VerifyMovementFailures.Drive(from);
        while (outward.Tick < 2400 && !outward.Arrived)
        {
            outward.Step(to);
            if (Submerged(outward.Body)) outSubmerged++;
        }
        Require(outward.Arrived, $"the native body must walk the flooded corridor out; last {outward.Describe()}");
        var back = new VerifyMovementFailures.Drive(outward.Body);
        while (back.Tick < 2400 && !back.Arrived)
        {
            back.Step(from);
            if (Submerged(back.Body)) backSubmerged++;
        }
        Require(back.Arrived, $"the native body must walk the flooded corridor back; last {back.Describe()}");
        int charged = trip.OutwardSubmergedTicks + trip.ReturnSubmergedTicks, native = outSubmerged + backSubmerged;
        Console.WriteLine($"   flooded corridor native: out {outward.Tick} ticks ({outSubmerged} submerged) against {trip.OutwardSubmergedTicks} charged, back {back.Tick} ticks ({backSubmerged} submerged) against {trip.ReturnSubmergedTicks} charged; total charged {charged}, native {native}");
        Require(charged >= native, $"a walked flooded trip was charged {charged} submerged ticks where the native head spent {native}");
    }

    /// <summary>The movement-failure corridor (a floor at row 90 walled at columns 19 and 60), flooded from row 80 to
    /// the floor, so a body standing anywhere on it has its head under water and every route along it is walks.</summary>
    private static void BuildFloodedCorridor()
    {
        VerifyMovementFailures.NewWorld(90);
        for (int y = 70; y < 90; y++) { VerifyMovementFailures.Solid(19, y); VerifyMovementFailures.Solid(60, y); }
        for (int x = 20; x <= 59; x++)
        for (int y = 80; y <= 89; y++)
        {
            Tile water = Main.tile[x, y];
            water.LiquidAmount = byte.MaxValue;
            water.LiquidType = 0;
        }
        VerifyMovementFailures.Finish();
    }

    private static bool Submerged(BodyState body)
        => Collision.DrownCollision(new Vector2(body.Left, body.Bottom - BodyPhysics.Height), BodyPhysics.Width, BodyPhysics.Height, 1f);

    /// <summary>A floor at row 80 with a pit from column 30 to 40, walled at 29 and 41, whose floor lies <paramref name="depth"/> rows down.</summary>
    private static void BuildPit(int depth)
    {
        VerifyMovementFailures.NewWorld(80);
        for (int x = 30; x <= 40; x++) VerifyMovementFailures.Clear(x, 80);
        for (int x = 29; x <= 41; x++) VerifyMovementFailures.Solid(x, 80 + depth);
        for (int y = 81; y < 80 + depth; y++) { VerifyMovementFailures.Solid(29, y); VerifyMovementFailures.Solid(41, y); }
        VerifyMovementFailures.Finish();
    }

    /// <summary>
    /// A floor at row 80 with a basin from column 30 to 60 whose floor is row 86, flooded from row 81
    /// to 85 so a body standing on the basin floor has its head under water, and a staircase of
    /// two-row rises from the basin floor back up to the rim at columns 30 to 34.
    /// </summary>
    private static void BuildBasin()
    {
        VerifyMovementFailures.NewWorld(80);
        for (int x = 30; x <= 60; x++) VerifyMovementFailures.Clear(x, 80);
        for (int x = 29; x <= 61; x++) VerifyMovementFailures.Solid(x, 86);
        for (int y = 81; y <= 86; y++) VerifyMovementFailures.Solid(61, y);
        for (int x = 30; x <= 60; x++)
        for (int y = 81; y <= 85; y++)
        {
            Tile water = Main.tile[x, y];
            water.LiquidAmount = byte.MaxValue;
            water.LiquidType = 0;
        }
        for (int y = 80; y <= 85; y++) VerifyMovementFailures.Solid(30, y);
        for (int x = 31; x <= 32; x++) for (int y = 82; y <= 85; y++) VerifyMovementFailures.Solid(x, y);
        for (int x = 33; x <= 34; x++) for (int y = 84; y <= 85; y++) VerifyMovementFailures.Solid(x, y);
        VerifyMovementFailures.Finish();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
