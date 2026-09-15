extern alias live;

using Microsoft.Xna.Framework;
using Terraria;

using AttemptEnding = live::AICompanion.Companion.Brain.Infrastructure.Movement.AttemptEnding;
using OrbState = live::AICompanion.Companion.Brain.Infrastructure.Movement.OrbState;
using CornerGraph = live::AICompanion.Companion.Brain.Infrastructure.Movement.CornerGraph;
using FreeSpaceSearch = live::AICompanion.Companion.Brain.Infrastructure.Movement.FreeSpaceSearch;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using Navigator = live::AICompanion.Companion.Brain.Infrastructure.Movement.Navigator;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;

/// <summary>
/// How a travel attempt ends, which is the half of the retired walker failure fixture that survived
/// the body change. That fixture classified ledge entries, preparation faults, divergence between
/// two bodies and unannounced walls — every one of them a property of a body that walked and of the
/// second simulation it was checked against, and all of them gone with it. Two properties it proved
/// are about the navigator rather than the body, and they are what this file keeps.
///
/// <para>The first is the distinction the whole "not yet is not no" rule upstream stands on: a
/// search that ran out of its expansion budget has proved nothing, and must report itself pending,
/// while only a search that exhausted the free space without reaching the goal is a proven absence.
/// An optional activity that cannot tell those apart declines work it could have done, or asserts
/// the world is empty on the strength of a search that stopped early.</para>
///
/// <para>The second is that an attempt is scored by who ended it: arriving completes it, another
/// owner taking the body pre-empts it, the brain releasing it cancels it, and a body that makes no
/// net progress over two windows strikes twice so the positioner can ban the spot. The strike rows
/// carry a control scene — the same harness with a body that can actually travel — because a
/// fixture that drives a stationary state proves two strikes by construction rather than by
/// measurement.</para>
/// </summary>
internal static class VerifyRouteEndings
{
    public static int Run()
    {
        int failed = 0;
        failed += BudgetExhaustionIsPendingAndOnlyAnExhaustedSearchIsUnreachable();
        failed += AnAttemptIsScoredByWhoEndedIt();
        failed += TwoWindowsWithoutProgressStrikeTwice();
        return failed;
    }

    /// <summary>
    /// One scene answers both halves, because they are the same search at two different moments: a
    /// goal sealed inside its own pocket at the far side of a room large enough that the flood
    /// cannot close it within one slice's expansion budget. On the first tick the search has spent
    /// its budget and proved nothing; some slices later it has closed every corner the room has and
    /// the absence is proven.
    /// </summary>
    private static int BudgetExhaustionIsPendingAndOnlyAnExhaustedSearchIsUnreachable()
    {
        int failed = 0;
        BuildLargeRoomWithASealedPocket();
        var navigator = new Navigator();
        var start = new Vector2(20 * 16, 20 * 16);
        Vector2 goal = CornerGraph.ToWorld(new Point(81, 81));
        var body = new OrbState(start, Vector2.Zero);

        navigator.MoveTo(body, goal);
        if (navigator.Status != Navigator.ExecutionStatus.Pending)
            failed += Report($"a search that spent its expansion budget must be pending, not {navigator.Status}");
        if (navigator.LastSearchStop != FreeSpaceSearch.StopReason.ExpansionBudget)
            failed += Report($"the first slice must stop on its expansion budget, not {navigator.LastSearchStop}");

        int slices = 1;
        while (navigator.Status == Navigator.ExecutionStatus.Pending && slices < 200)
        {
            navigator.MoveTo(body, goal);
            slices++;
        }
        if (slices < 2) failed += Report("the room must be large enough that the flood needs more than one slice");
        if (navigator.Status != Navigator.ExecutionStatus.Unreachable)
            failed += Report($"an exhausted search over a sealed goal must be unreachable, not {navigator.Status}");
        if (navigator.LastSearchStop != FreeSpaceSearch.StopReason.Exhausted)
            failed += Report($"the absence must be proven by exhaustion, not by {navigator.LastSearchStop}");
        if (navigator.LastExpansions <= 2500)
            failed += Report($"an exhausted flood over this room must have closed more than one slice's worth of corners; closed {navigator.LastExpansions}");
        Console.WriteLine($"route endings: budget exhaustion stayed pending for {slices - 1} slices, then proved the absence over {navigator.LastExpansions} corners");
        return failed;
    }

    private static int AnAttemptIsScoredByWhoEndedIt()
    {
        int failed = 0;
        BuildOpenRoom();
        var navigator = new Navigator();
        var at = new Vector2(30 * 16, 30 * 16);
        var body = new OrbState(at, Vector2.Zero);

        // Another owner takes the body mid-attempt.
        navigator.MoveTo(body, new Vector2(60 * 16, 30 * 16));
        navigator.Interrupt(body, AttemptEnding.Preempted, "downing");
        if (navigator.LastEnding != AttemptEnding.Preempted || navigator.PreemptedAttempts != 1)
            failed += Report($"an owner taking the body pre-empts the attempt; ending={navigator.LastEnding} preempted={navigator.PreemptedAttempts}");

        // The brain releasing its own request.
        navigator.MoveTo(body, new Vector2(62 * 16, 30 * 16));
        navigator.Interrupt(body, AttemptEnding.Cancelled, "released");
        if (navigator.LastEnding != AttemptEnding.Cancelled || navigator.CancelledAttempts != 1)
            failed += Report($"a released request cancels the attempt; ending={navigator.LastEnding} cancelled={navigator.CancelledAttempts}");

        // Arrival, which the navigator scores itself rather than being told.
        navigator.MoveTo(body, at + new Vector2(4f, 0f));
        if (!navigator.Arrived || navigator.LastEnding != AttemptEnding.Completed || navigator.CompletedAttempts != 1)
            failed += Report($"reaching the goal completes the attempt; arrived={navigator.Arrived} ending={navigator.LastEnding} completed={navigator.CompletedAttempts}");

        // An interrupt with nothing open must not invent an ending, or every tick of an idle body
        // would file one and the counts would measure how often the brain was asked rather than
        // how often it travelled.
        int cancelled = navigator.CancelledAttempts;
        navigator.Interrupt(body, AttemptEnding.Cancelled, "released");
        if (navigator.CancelledAttempts != cancelled)
            failed += Report($"an interrupt with no attempt open must score nothing; cancelled went {cancelled} to {navigator.CancelledAttempts}");

        Console.WriteLine($"route endings: attempts scored {navigator.CompletedAttempts} completed, {navigator.PreemptedAttempts} pre-empted, {navigator.CancelledAttempts} cancelled");
        return failed;
    }

    /// <summary>
    /// The body is walled into a pocket it fits in and asked for a goal outside it: it can move, and
    /// there is nowhere to move to. The control scene is the same body and the same harness on an
    /// open floor, where it must strike nothing at all — without it, "a stationary body strikes
    /// twice" is a statement about the fixture rather than about the navigator.
    /// </summary>
    private static int TwoWindowsWithoutProgressStrikeTwice()
    {
        int failed = 0;

        BuildSealedPocketAroundTheBody();
        var companion = VerifyCompanionLifecycle.Create();
        MovementQueries.World = new GameTileWorld();
        var navigator = new Navigator();
        Vector2 pocket = CornerGraph.ToWorld(new Point(30, 30));
        companion.NPC.Center = pocket;
        companion.NPC.velocity = Vector2.Zero;
        Vector2 outside = CornerGraph.ToWorld(new Point(60, 30));
        for (int tick = 0; tick < 2 * 180; tick++)
        {
            companion.Motor.Track();
            companion.Motor.Apply(navigator.MoveTo(companion.Motor.State, outside));
            VerifyResponsiveFollowing.AdvanceNative(companion);
        }
        float travelled = Vector2.Distance(pocket, companion.NPC.Center);
        if (navigator.StuckStrikes != 2)
            failed += Report($"two windows without net progress must strike twice; struck {navigator.StuckStrikes} after {travelled:0.0}px of travel");
        if (travelled > 32f)
            failed += Report($"the sealed scene must actually hold the body still; it travelled {travelled:0.0}px, so the strikes measured something else");

        BuildOpenRoom();
        var moving = VerifyCompanionLifecycle.Create();
        MovementQueries.World = new GameTileWorld();
        var second = new Navigator();
        Vector2 from = CornerGraph.ToWorld(new Point(20, 30));
        moving.NPC.Center = from;
        moving.NPC.velocity = Vector2.Zero;
        Vector2 to = CornerGraph.ToWorld(new Point(80, 30));
        for (int tick = 0; tick < 2 * 180; tick++)
        {
            moving.Motor.Track();
            moving.Motor.Apply(second.MoveTo(moving.Motor.State, to));
            VerifyResponsiveFollowing.AdvanceNative(moving);
        }
        float control = Vector2.Distance(from, moving.NPC.Center);
        if (control < 32f)
            failed += Report($"the control body must travel, or it proves nothing about the strikes; it moved {control:0.0}px");
        if (second.StuckStrikes != 0)
            failed += Report($"a body making progress must not strike; struck {second.StuckStrikes} over {control:0.0}px");

        Console.WriteLine($"route endings: a walled body struck {navigator.StuckStrikes} over {travelled:0.0}px, a travelling body struck {second.StuckStrikes} over {control:0.0}px");
        return failed;
    }

    private static void BuildOpenRoom()
    {
        VerifyEngineMotion.BuildWorld();
        for (int x = 10; x <= 89; x++) { VerifyEngineMotion.Solid(x, 10); VerifyEngineMotion.Solid(x, 89); }
        for (int y = 10; y <= 89; y++) { VerifyEngineMotion.Solid(10, y); VerifyEngineMotion.Solid(89, y); }
        Replug();
    }

    /// <summary>A room whose free corners number several times one slice's expansion budget, with a
    /// goal in a pocket nothing connects to.</summary>
    private static void BuildLargeRoomWithASealedPocket()
    {
        BuildOpenRoom();
        for (int x = 78; x <= 83; x++)
            for (int y = 78; y <= 83; y++) VerifyEngineMotion.Solid(x, y);
        foreach ((int x, int y) in new[] { (80, 80), (81, 80), (80, 81), (81, 81) }) VerifyEngineMotion.Clear(x, y);
        Replug();
    }

    private static void BuildSealedPocketAroundTheBody()
    {
        BuildOpenRoom();
        for (int x = 27; x <= 32; x++)
            for (int y = 27; y <= 32; y++) VerifyEngineMotion.Solid(x, y);
        foreach ((int x, int y) in new[] { (29, 29), (30, 29), (29, 30), (30, 30) }) VerifyEngineMotion.Clear(x, y);
        Replug();
    }

    /// <summary>A rebuilt tile map is a different world to every retained search and every clearance
    /// chunk, and they compare the world by reference, so the reference has to change with it.</summary>
    private static void Replug()
    {
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
    }

    private static int Report(string message)
    {
        AICompanion.Tools.Ledger.EmitLedgerRows.Detail("route endings: " + message);
        return 1;
    }
}
