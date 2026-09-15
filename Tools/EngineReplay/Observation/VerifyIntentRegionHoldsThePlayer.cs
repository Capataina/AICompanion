extern alias live;

using Microsoft.Xna.Framework;
using Terraria;

using InferPlayerActivity = live::AICompanion.Companion.Brain.Infrastructure.Observation.InferPlayerActivity;
using Navigator = live::AICompanion.Companion.Brain.Infrastructure.Movement.Navigator;
using PlayerIntentRegion = live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerIntentRegion;
using PlayerIntentRegionSense = live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerIntentRegionSense;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using DistanceMode = live::AICompanion.Companion.PlayerIntegration.CompanionDistanceMode;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;

/// <summary>
/// The player's intent region always holds the player, and is shaped the way the owner ruled on 15 September 2026: a quarter
/// larger than the follow comfort, growing by up to a quarter as its lead reaches the clamp, with the player's centre at the
/// centre of its bottom third and the lead clamped so he never leaves it.
///
/// <para>The replay is the pass line the ruling came from. On the 14:16 capture of that day the player stood outside his own
/// region on 5,226 of 35,011 rows, 14.9%, and on 37% of the rows he was falling, because the lead was clamped to half the
/// screen. The capture's player track is committed beside the other scenarios, so this runs in a fresh clone, and it is fed
/// through the real lead filter and the real player-activity inference rather than a copy of either; the region's
/// containment is a property of the geometry for every lead the filter can produce, so the track is the realistic input and
/// not the only one — the sweep below is the rest.</para>
///
/// <para>The geometry rows run on all three distance modes, because the Close mode's box is too short for the bottom third
/// to leave the player inside by the slack, and a row that ran only on Standard would pass a placement that fails there.
/// The expected base size is written as comfort times scale times 1.25 here, from the constants the ruling was made
/// against, rather than read from the base-scale constant, so a change to that constant is a red row and not a silent one.</para>
/// </summary>
internal static class VerifyIntentRegionHoldsThePlayer
{
    private const string Track = "2026-09-15_13-16-33-496-player-track.txt";

    public static int Run()
    {
        var preferences = Preferences.Current;
        DistanceMode held = preferences.DistanceMode;
        // Each half runs whatever the other did and every failure is reported together, because an abort on the first throw
        // lets a geometry failure hide whether the replay still holds, which is exactly what a mutation of the clamp needs to see.
        var failures = new List<string>();
        void Each(string half, Action check)
        {
            try { check(); }
            catch (InvalidOperationException e) { failures.Add($"{half}: {e.Message}"); }
        }
        try
        {
            foreach (DistanceMode mode in new[] { DistanceMode.Close, DistanceMode.Standard, DistanceMode.Free })
                Each($"geometry on {mode}", () => { preferences.DistanceMode = mode; Geometry(mode); });
            // The capture was recorded on Standard: its first row's region is the unscaled follow comfort.
            Each("replay", () => { preferences.DistanceMode = DistanceMode.Standard; Replay(); });
        }
        finally
        {
            preferences.DistanceMode = held;
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(" | ", failures));
        Console.WriteLine("intent region: the geometry holds on every distance mode and the recorded player is inside his region on every row");
        return 0;
    }

    private static void Geometry(DistanceMode mode)
    {
        float slack = Navigator.SettleRadius;
        float scale = Preferences.Current.FollowComfortScale;
        Vector2 expectedBase = new Vector2(Weights.FollowHorizontalComfort, Weights.FollowVerticalComfort) * scale * 1.25f;
        Vector2 player = new(8000f, 4000f);

        var still = PlayerIntentRegion.Around(player, Vector2.Zero, scale, false, slack);
        float below = player.Y - still.Centre.Y;
        Require(Near(still.HalfSize, expectedBase), $"{mode}: at zero lead the half-size must be 1.25 times the comfort; {still.HalfSize} against {expectedBase}");
        Require(MathF.Abs(player.X - still.Centre.X) < 0.01f, $"{mode}: at zero lead the box must be centred on the player horizontally; centre {still.Centre} player {player}");
        bool thirdFits = still.HalfSize.Y / 3f >= slack;
        float expectedBelow = thirdFits ? still.HalfSize.Y * 2f / 3f : still.HalfSize.Y - slack;
        Require(MathF.Abs(below - expectedBelow) < 0.01f, thirdFits
            ? $"{mode}: at zero lead the player's centre must be at the centre of the bottom third, {expectedBelow:0.00} px below the box's centre; it is {below:0.00}"
            : $"{mode}: where a third of the half-height is under the slack the player must sit as low as the slack allows, {expectedBelow:0.00} px below; it is {below:0.00}");
        Require(Holds(still, player, slack), $"{mode}: at zero lead the player must be inside by the slack; {Describe(still, player)}");

        foreach (Vector2 direction in new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
                     new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
        {
            var full = PlayerIntentRegion.Around(player, direction * 100000f, scale, true, slack);
            Require(Near(full.HalfSize, expectedBase * 1.25f), $"{mode} {direction}: at the clamp the half-size must be 1.25 times 1.25 times the comfort; {full.HalfSize} against {expectedBase * 1.25f}");
            Vector2 offset = player - full.Centre;
            if (direction.X != 0f)
                Require(MathF.Abs(MathF.Abs(offset.X) - (full.HalfSize.X - slack)) < 0.01f,
                    $"{mode} {direction}: at the clamp the player must be at the region's side edge less the slack; {Describe(full, player)}");
            if (direction.Y > 0f)
                Require(MathF.Abs(offset.Y + (full.HalfSize.Y - slack)) < 0.01f,
                    $"{mode} {direction}: led downward to the clamp the player must be at the region's top edge less the slack; {Describe(full, player)}");
            if (direction.Y < 0f && full.HalfSize.Y / 3f >= slack)
                Require(MathF.Abs(offset.Y - (full.HalfSize.Y - slack)) < 0.01f,
                    $"{mode} {direction}: led upward to the clamp the player must be at the region's bottom edge less the slack; {Describe(full, player)}");
            Require(Holds(full, player, slack), $"{mode} {direction}: at the clamp the player must be inside by the slack; {Describe(full, player)}");
        }

        // Every lead the filter could produce, in every direction: containment everywhere, and on the modes whose box is tall
        // enough for the bottom third, a lead short of full growth is never clamped — growth and the clamp agree.
        int checkedLeads = 0;
        for (int step = 0; step < 52; step++)
        {
            float angle = step * MathF.Tau / 52f;
            for (float length = 0f; length <= 2000f; length += 10f)
            {
                Vector2 lead = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length;
                var region = PlayerIntentRegion.Around(player, lead, scale, true, slack);
                checkedLeads++;
                Require(Holds(region, player, slack), $"{mode}: lead {lead} must leave the player inside by the slack; {Describe(region, player)}");
                bool grown = Near(region.HalfSize, expectedBase * 1.25f);
                if (thirdFits && !grown)
                    Require(Near(region.Lead, lead), $"{mode}: a lead short of full growth must not be clamped; asked {lead}, applied {region.Lead}, half-size {region.HalfSize}");
            }
        }
        Console.WriteLine($"intent region geometry on {mode}: base {expectedBase}, player {below:0.0} px below the centre at zero lead, {checkedLeads} leads held");
    }

    private static void Replay()
    {
        string path = FindScenario(Track);
        var activity = new InferPlayerActivity();
        var sense = new PlayerIntentRegionSense();
        float halfHeight = Player.defaultHeight / 2f;
        ulong tick = 0;
        int rows = 0, outside = 0, falling = 0, fallingOutside = 0, worstRow = -1;
        float worstGap = 0f;
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] parts = line.Split(',');
            Vector2 feet = new(Parse(parts[0]), Parse(parts[1]));
            Vector2 velocity = new(Parse(parts[2]), Parse(parts[3]));
            Vector2 centre = feet - new Vector2(0f, halfHeight);
            activity.Observe(feet, velocity, false, false, ++tick);
            bool travelling = activity.Travel.LengthSquared() > Weights.PlayerIntentTravelSpeed * Weights.PlayerIntentTravelSpeed;
            sense.Update(centre, Vector2.Zero, centre, activity.Travel, travelling, false, activity.Samples);
            var region = sense.Region;
            rows++;
            bool isFalling = velocity.Y > 4f;
            if (isFalling) falling++;
            if (region.Contains(centre)) continue;
            outside++;
            if (isFalling) fallingOutside++;
            float gap = region.GapBeyond(centre);
            if (gap > worstGap) { worstGap = gap; worstRow = rows; }
        }
        string ledger = $"{rows} rows, player outside his region on {outside} ({(rows == 0 ? 0 : 100f * outside / rows):0.0}%), falling faster than 4 px/tick on {falling} rows and outside on {fallingOutside} of them, worst {worstGap:0.0} px beyond the edge at row {worstRow}";
        Console.WriteLine($"intent region replay of {Track}: {ledger}");
        Require(rows == 35011, $"the replay premise: the committed track holds the capture's 35,011 rows; {ledger}");
        Require(outside == 0, $"the recorded player must be inside his own intent region on every row; {ledger}");
    }

    private static bool Holds(PlayerIntentRegion region, Vector2 player, float slack)
        => MathF.Abs(player.X - region.Centre.X) <= region.HalfSize.X - slack + 0.01f
            && MathF.Abs(player.Y - region.Centre.Y) <= region.HalfSize.Y - slack + 0.01f;

    private static bool Near(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 0.01f;

    private static string Describe(PlayerIntentRegion region, Vector2 player)
        => $"centre {region.Centre} half {region.HalfSize} lead {region.Lead} player {player} offset {player - region.Centre}";

    private static float Parse(string text) => float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The committed scenario folder, found by walking up from wherever the suite was started, because the suite is
    /// run from the repository root by the verify script and from the tool's own folder by hand.</summary>
    private static string FindScenario(string name)
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, "Tools", "Scenarios", name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        throw new FileNotFoundException($"the committed player track {name} was not found under any Tools/Scenarios above {Environment.CurrentDirectory}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
