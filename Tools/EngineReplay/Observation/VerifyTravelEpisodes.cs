extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Map;
using BrainTelemetry = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainTelemetry;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;

/// <summary>
/// What a journey's recorded duration is allowed to contain, driven through the real recorder on a real downing.
///
/// The episode is a duration measurement, and a duration measurement is only worth what its denominator excludes. The
/// census next to it counts asks, so it is right for the census to ignore a downing: an ask does not stop being one
/// because the body died inside it. This instrument reports actual ticks and pixels-per-tick against that span, so the
/// same silence charges every tick of a death to travel at zero displacement — and a downing runs into the hundreds of
/// ticks, which is longer than most journeys. An instrument built to answer "it took forever to get to me" would then
/// attribute a death to the follower, which is the one answer it must never give.
///
/// So this drives the whole brain until a journey is under way, kills the body mid-journey, waits for it to get up on
/// its own, and reads the occurrence back. The fixture counts the downed ticks itself while it drives, so the producer's
/// figure is checked against an independent count of the same thing rather than against its own arithmetic.
/// </summary>
internal static class VerifyTravelEpisodes
{
    private const int FloorRow = 80;
    /// <summary>Ticks of ordinary travel before the body is killed. It is deliberately short: this scene's journey to the
    /// player closes around tick 65 once the body is inside the comfort box, so a death at ninety would land after the
    /// episode it is supposed to interrupt and the fixture would measure nothing.</summary>
    private const int Travelling = 30;
    private const int SelfRevivalWatch = 900;
    private const int AfterRevival = 90;

    public static int Run()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, Path.GetTempPath());
        bool server = Main.dedServ;
        Main.dedServ = true;
        LimitPlanningWork.Unbounded = true;
        int failed = 0;
        try
        {
            failed += RunOneRow.Case("a journey interrupted by a death is not charged the death's ticks",
                ADeathInsideAJourneyIsNotChargedToTravel, "travel episodes");
        }
        finally
        {
            LimitPlanningWork.Unbounded = false;
            Main.dedServ = server;
        }
        Console.WriteLine(failed == 0
            ? "travel episodes: a journey spanning a downing reports the ticks the body could travel, not the ticks the clock ran"
            : $"travel episodes: {failed} case(s) failed");
        return failed;
    }

    private static void ADeathInsideAJourneyIsNotChargedToTravel()
    {
        CompanionNPC companion = SetUpFloorWithThePlayerDownTheFloor();
        var recorder = new BrainTelemetry();
        VerifyObservationLifecycle.Attach(recorder);
        recorder.OnWorldLoad();
        string path = Directory.GetFiles(BrainTelemetry.Folder, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).First();

        try
        {
            // Long enough for the follow request to open an episode and for the body to be walking inside it.
            for (int tick = 0; tick < Travelling; tick++) Tick(companion);

            companion.NPC.life = 0;
            companion.CheckDead();
            Require(companion.IsDowned, "premise: lethal damage must down the companion rather than kill it");

            // The player stays down the floor, so nobody revives it and it must get up on its own. The fixture counts
            // the downed ticks as it drives them, which is the independent figure the producer is checked against.
            int downedTicks = 0;
            long revivedAt = -1;
            for (int tick = 0; tick < SelfRevivalWatch && revivedAt < 0; tick++)
            {
                Tick(companion);
                if (companion.IsDowned) downedTicks++;
                else revivedAt = (long)Main.GameUpdateCount;
            }
            Require(revivedAt > 0, $"premise: the companion must get up on its own inside {SelfRevivalWatch} ticks");
            Require(downedTicks > 100, $"premise: the downing must be long enough to dominate a journey; it lasted {downedTicks} tick(s)");

            for (int tick = 0; tick < AfterRevival; tick++) Tick(companion);
            // The unload is what closes the open episode: TravelEpisodes.Close runs from the recorder's own close path,
            // so a fixture that read the sidecar without unloading would find the journey it cares about still open.
            recorder.OnWorldUnload();

            Capture capture = Capture.Read(path);
            var episodes = capture.Events.Where(e => e.Kind == "route-episode").ToList();
            // The journey is found by the downing it holds, not by comparing its tick stamps against the engine counter
            // this fixture read: the recorder rebases its own ticks on the world load, so those two are the same number
            // only when this scene happens to be the first in the process. Exactly one downing was driven, so exactly one
            // journey may report one — and on a producer that charges the death to travel, none reports one at all.
            var spanning = episodes.Where(e => Capture.Long(Capture.Field(e.Detail, "downed-ticks")) > 0).ToList();
            Require(spanning.Count == 1,
                $"one downing was driven inside one journey, and {spanning.Count} journey(s) report downed ticks; episodes: "
                    + string.Join(" | ", episodes.Select(e => e.Detail)));
            Require(episodes.Any(e => (Capture.Long(Capture.Field(e.Detail, "end-tick")) - Capture.Long(Capture.Field(e.Detail, "start-tick"))) >= downedTicks),
                "premise: a journey must have been open across the whole downing for this to measure anything; episodes: "
                    + string.Join(" | ", episodes.Select(e => e.Detail)));

            foreach (Capture.Event episode in spanning)
            {
                long start = Capture.Long(Capture.Field(episode.Detail, "start-tick")) ?? -1;
                long end = Capture.Long(Capture.Field(episode.Detail, "end-tick")) ?? -1;
                long actual = Capture.Long(Capture.Field(episode.Detail, "actual-ticks")) ?? -1;
                long downed = Capture.Long(Capture.Field(episode.Detail, "downed-ticks")) ?? -1;
                long span = end - start;

                Require(downed >= 0, "a journey that spanned a downing did not report its downed ticks at all: " + episode.Detail);
                // Two ticks of slack each way: the downing tick itself and the revival tick are boundary ticks the
                // fixture and the observer can legitimately attribute differently, and one tick of either is not a defect.
                Require(Math.Abs(downed - downedTicks) <= 2,
                    $"the journey reports {downed} downed tick(s) against the {downedTicks} this fixture drove: {episode.Detail}");
                Require(actual <= span - downed + 2,
                    $"the journey charged {actual} tick(s) of travel over a {span}-tick span holding {downed} downed tick(s), "
                        + $"so the death is being counted as travel: {episode.Detail}");
                Require(actual > 0, "a journey whose downed ticks were removed reported no travel at all: " + episode.Detail);

                double mean = double.Parse(Capture.Field(episode.Detail, "mean-speed-px-per-tick") ?? "-1",
                    System.Globalization.CultureInfo.InvariantCulture);
                Require(mean > 0,
                    $"the mean speed over a journey holding a death is {mean:0.00} px/tick, which is the death dividing the "
                        + $"distance rather than the travel: {episode.Detail}");
                Console.WriteLine($"  journey across a death: span {span}, downed {downed} (fixture counted {downedTicks}), "
                    + $"travel {actual}, mean {mean:0.00} px/tick");
            }
        }
        finally
        {
            try { recorder.OnWorldUnload(); } catch { /* already closed on the passing path */ }
        }
    }

    /// <summary>
    /// The downing fixture's floor, with the player far enough down it that ordinary following asks for a journey and
    /// near enough that nothing hands the body to recovery. Nobody stands beside the companion, so a downing here has to
    /// run its full self-revival rather than being cut short by a rescue.
    /// </summary>
    private static CompanionNPC SetUpFloorWithThePlayerDownTheFloor()
    {
        Main.maxTilesX = Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)100, (ushort)100 }, null)!;
        Main.tileSolid[TileID.Stone] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile floor = Main.tile[x, FloorRow];
            floor.ClearEverything();
            floor.HasTile = true;
            floor.TileType = TileID.Stone;
        }
        CompanionNPC companion = VerifyCompanionLifecycle.Create();
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
        companion.NPC.position = new Vector2(30 * 16 + 8 - companion.NPC.width / 2f, FloorRow * 16 - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.velocity = Vector2.Zero;
        player.Center = new Vector2(55 * 16 + 8, FloorRow * 16 - player.height / 2f);
        return companion;
    }

    /// <summary>The downing fixture's own tick, borrowed whole: it carries the tolerance for the revival tick's stale
    /// presentation, which is a known finding rather than a fault of this scene.</summary>
    private static void Tick(CompanionNPC companion) => VerifyDowningAndRevival.Tick(companion);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
