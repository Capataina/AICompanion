extern alias live;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Tools.Ledger;
using live::AICompanion.Companion.CharacterBody;

/// <summary>
/// A committed scenario as a checkpoint: the whole brain and the native body in the real world the
/// scenario was cut from, with the orb placed where the recording had it and the player standing
/// where he stood, and the rows being whether the orb got to him and whether it ever touched water
/// or lava on the way.
///
/// The scenario file supplies the actors — its header names the companion's centre in pixels, the
/// player's feet tile and the window — and the world file supplies the terrain. The grid inside the
/// file is the recording's own picture of that terrain and is checked against the world rather than
/// played in: a snapshot is written only when a chunk changes and carries no liquid, so the grid can
/// be behind the world and always reads a pool as air, and a checkpoint about water played on the
/// grid would pass without any water in it. The agreement between grid and world is reported as a
/// measure so a reader can tell a world that has been mined since from the one the scenario names.
///
/// The player stands still. That is the scenario's own question — the orb starting from the pocket
/// or below the ledge, the player already at the far side — and it is what makes both rows a
/// property of the body and the planner rather than of the recorded track.
/// </summary>
internal static class RunTheScenario
{
    /// <summary>Long enough for the orb to cross a window several times over and to leave a pocket it started in.</summary>
    private const int DefaultTicks = 900;

    /// <summary>Tiles of world beyond the window over which the grid is compared; the window itself is what is compared.</summary>
    private const int LightHalf = 40;

    internal sealed record Scenario(string Path, string Name, Point Start, Point Goal, Point Companion, Point Player,
        Vector2 OrbCentre, Rectangle Window, string[] Rows);

    public static int Run(string scenarioPath, string worldPath, string suite, int ticks, bool driveLight)
    {
        Scenario scenario = Read(scenarioPath);
        var loaded = LoadTheSavedWorld.Load(worldPath);
        (int agreed, int compared, int wet) = CompareGridToWorld(scenario);
        string agreement = $"the grid agrees with {loaded.Name} on {agreed} of {compared} window tiles and the world holds {wet} wet tiles in the window";
        Console.WriteLine($"SCENARIO {scenario.Name}: orb at {scenario.OrbCentre.X:0},{scenario.OrbCentre.Y:0}, player at tile {scenario.Player.X},{scenario.Player.Y}; {agreement}");
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "window tiles on which the scenario grid agrees with the world", agreed, "tiles", "up", "unbounded-allowances",
            message: agreement + "; a fall says the world has been mined since the recording, and a grid never carries liquid");
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "wet tiles the world holds inside the scenario window", wet, "tiles", null, "unbounded-allowances",
            message: agreement + "; zero makes the liquid row vacuous, and it says so");

        Vector2 playerFeet = new(scenario.Player.X * 16f + 8f, (scenario.Player.Y + 1) * 16f - 0.01f);
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        Outcome outcome;
        try
        {
            outcome = Play(scenario, playerFeet, ticks, driveLight);
        }
        finally
        {
            live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false;
        }

        string note = $"{scenario.Name}: {ticks} ticks, closest {outcome.Closest:0} px at tick {outcome.ClosestAt}, final {outcome.Final:0} px, "
            + $"minimum clearance {outcome.MinimumClearance:0.0} px, last action {outcome.LastAction}, navigator {outcome.LastStatus}, {outcome.Seconds:0.0}s";
        Console.WriteLine("SCENARIO " + note);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "ticks until the orb first came within the follow comfort of the player",
            outcome.ReachedAt < 0 ? ticks : outcome.ReachedAt, "ticks", "down", "unbounded-allowances", message: note);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "closest the orb came to the player's feet", outcome.Closest, "px", "down", "unbounded-allowances", message: note);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "minimum clearance the orb had from a wall", outcome.MinimumClearance, "px", "up", "unbounded-allowances", message: note);

        int failures = 0;
        float reach = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.FollowVerticalComfort;
        if (outcome.ReachedAt >= 0)
            EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, "the orb reaches the player from where the recording left it",
                $"within {reach:0} px of his feet at tick {outcome.ReachedAt}; " + note, mode: "unbounded-allowances",
                killedBy: "a search that never finishes over the window, a route the steering cannot keep through the gap, or a positioner that answers nothing and a state search that never arrives");
        else
        {
            failures++;
            EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, "the orb reaches the player from where the recording left it",
                $"never within {reach:0} px of his feet; " + note, mode: "unbounded-allowances");
        }
        string liquidNote = wet == 0 ? "the world holds no wet tile in the window, so this row is vacuous here; " : "";
        if (outcome.WetTicks == 0)
            EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, "the orb never touches water or lava",
                liquidNote + note, mode: "unbounded-allowances",
                killedBy: "a flood that treats a wet tile as free, a route smoothed through a pool, or a positioner that picks a spot under water");
        else
        {
            failures++;
            EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, "the orb never touches water or lava",
                $"wet on {outcome.WetTicks} ticks, first at tick {outcome.FirstWetAt} at {outcome.FirstWet.X:0},{outcome.FirstWet.Y:0}; " + note, mode: "unbounded-allowances");
        }
        return failures;
    }

    private sealed record Outcome(int ReachedAt, float Closest, int ClosestAt, float Final, float MinimumClearance,
        int WetTicks, int FirstWetAt, Vector2 FirstWet, string LastAction, string LastStatus, double Seconds);

    private static Outcome Play(Scenario scenario, Vector2 playerFeet, int ticks, bool driveLight)
    {
        live::AICompanion.Companion.Brain.Infrastructure.Movement.BehaviourCensus.Reset();
        PrepareTheHeadlessEngine.PinEveryRandomSource(1);
        PrepareTheHeadlessEngine.StartTheWorldClockAt(1);
        PrepareTheHeadlessEngine.PrepareLightServices();
        PrepareTheHeadlessEngine.ForgetEverythingLearnedAboutTheWorld();
        var companion = PrepareTheHeadlessEngine.AttachCompanion(scenario.OrbCentre, playerFeet);
        Player player = Main.player[0];
        player.velocity = Vector2.Zero;
        player.dead = false;
        if (driveLight) PrepareTheHeadlessEngine.WarmTheLightEngine(player.Bottom.ToTileCoordinates(), LightHalf, LightHalf);

        var world = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World;
        float reach = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.FollowVerticalComfort;
        int reachedAt = -1, closestAt = 0, wetTicks = 0, firstWetAt = -1;
        float closest = float.PositiveInfinity, minimumClearance = float.PositiveInfinity;
        Vector2 firstWet = Vector2.Zero;
        var clock = Stopwatch.StartNew();
        for (int tick = 0; tick < ticks; tick++)
        {
            player.Bottom = playerFeet;
            player.velocity = Vector2.Zero;
            PrepareTheHeadlessEngine.AdvanceTheWorldClock();
            if (driveLight) PrepareTheHeadlessEngine.DriveLightOnce(player.Bottom.ToTileCoordinates(), LightHalf, LightHalf);
            try
            {
                companion.AI();
                PrepareTheHeadlessEngine.AdvanceTheNativeBody(companion);
            }
            catch (Exception failure)
            {
                var b = companion.Brain;
                throw new InvalidOperationException($"the scenario threw at tick {tick}, last action {b.LastAction?.Name ?? "-"}, request {b.LastRequest.Kind}, "
                    + $"navigator {b.Navigator.Status}, body at {companion.NPC.Center.X:0},{companion.NPC.Center.Y:0}", failure);
            }
            Vector2 centre = companion.NPC.Center;
            float apart = Vector2.Distance(centre, playerFeet);
            if (apart < closest) { closest = apart; closestAt = tick; }
            if (reachedAt < 0 && apart <= reach) reachedAt = tick;
            minimumClearance = MathF.Min(minimumClearance, live::AICompanion.Companion.Brain.Infrastructure.Movement.CircleContact.Clearance(world, centre));
            // The motor's own liquid reading, which is the contact circle against wet tiles: honey and
            // shimmer only slow the body and are not counted, water and lava are the two that hurt.
            int liquid = companion.Motor.LiquidKind;
            if (liquid is 0 or 1)
            {
                if (wetTicks == 0) { firstWetAt = tick; firstWet = centre; }
                wetTicks++;
            }
        }
        clock.Stop();
        return new Outcome(reachedAt, closest, closestAt, Vector2.Distance(companion.NPC.Center, playerFeet), minimumClearance,
            wetTicks, firstWetAt, firstWet, companion.Brain.LastAction?.Name ?? "-", companion.Brain.Navigator.Status.ToString(), clock.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// The grid against the world, tile for tile over the window: solid-or-not from the grid's glyph
    /// against the loaded world's own shape, so a mined world or a wrong world is a number rather
    /// than a surprise. Liquid is counted from the world alone, because the grid never carries it.
    /// </summary>
    private static (int Agreed, int Compared, int Wet) CompareGridToWorld(Scenario scenario)
    {
        var world = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
        int agreed = 0, compared = 0, wet = 0;
        for (int y = 0; y < scenario.Rows.Length; y++)
        {
            string row = scenario.Rows[y];
            for (int x = 0; x < scenario.Window.Width; x++)
            {
                int wx = scenario.Window.X + x, wy = scenario.Window.Y + y;
                if (!world.InWorld(wx, wy)) continue;
                char glyph = x < row.Length ? row[x] : '#';
                bool gridSolid = live::AICompanion.Companion.Brain.Infrastructure.Movement.TextTileWorld.ShapeOf(glyph)
                    != live::AICompanion.Companion.Brain.Infrastructure.Movement.TileShape.Air;
                bool worldSolid = world.Shape(wx, wy) != live::AICompanion.Companion.Brain.Infrastructure.Movement.TileShape.Air;
                compared++;
                if (gridSolid == worldSolid) agreed++;
                if (world.LiquidAmount(wx, wy) > 0) wet++;
            }
        }
        return (agreed, compared, wet);
    }

    /// <summary>
    /// The scenario's first block: the header's actors and window, then the grid rows. The header
    /// is the extractor's own format, keys followed by values, and only the keys named here are read.
    /// </summary>
    internal static Scenario Read(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0) throw new InvalidDataException($"{path} is empty");
        string header = lines[0];
        Point Tile(string key)
        {
            int at = header.IndexOf(key + " ", StringComparison.Ordinal);
            if (at < 0) throw new InvalidDataException($"{Path.GetFileName(path)} has no '{key}' in its header");
            string[] xy = header[(at + key.Length + 1)..].Split(' ')[0].Split(',');
            return new Point(int.Parse(xy[0], CultureInfo.InvariantCulture), int.Parse(xy[1], CultureInfo.InvariantCulture));
        }
        int orbAt = header.IndexOf("orb ", StringComparison.Ordinal);
        if (orbAt < 0) throw new InvalidDataException($"{Path.GetFileName(path)} has no 'orb' centre in its header; a scenario without one cannot place the body");
        string[] orb = header[(orbAt + 4)..].Split(' ')[0].Split(',');
        var centre = new Vector2(float.Parse(orb[0], CultureInfo.InvariantCulture), float.Parse(orb[1], CultureInfo.InvariantCulture));
        int windowAt = header.IndexOf("window x ", StringComparison.Ordinal);
        if (windowAt < 0) throw new InvalidDataException($"{Path.GetFileName(path)} has no window in its header");
        string[] window = header[(windowAt + 9)..].Split(' ');
        int[] xs = window[0].Split("..").Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        int[] ys = window[2].Split("..").Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        var bounds = new Rectangle(xs[0], ys[0], xs[1] - xs[0] + 1, ys[1] - ys[0] + 1);

        var rows = new List<string>();
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Length == 0) break;
            if (line.StartsWith("markers", StringComparison.Ordinal) || line.StartsWith("trail", StringComparison.Ordinal)) continue;
            if (line.StartsWith("tick ", StringComparison.Ordinal)) break;
            rows.Add(line);
        }
        return new Scenario(path, Path.GetFileNameWithoutExtension(path), Tile("start"), Tile("goal"), Tile("npc"), Tile("player"), centre, bounds, rows.ToArray());
    }

    public static int DefaultTickCount => DefaultTicks;
}
