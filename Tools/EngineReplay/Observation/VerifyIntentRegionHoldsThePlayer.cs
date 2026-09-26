extern alias live;

using Microsoft.Xna.Framework;
using Terraria;

using InferPlayerActivity = live::AICompanion.Companion.Brain.Infrastructure.Observation.InferPlayerActivity;
using Navigator = live::AICompanion.Companion.Brain.Infrastructure.Movement.Navigator;
using PlayerIntentRegion = live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerIntentRegion;
using PlayerIntentRegionSense = live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerIntentRegionSense;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;

/// <summary>
/// The player's intent region always holds the player, and is shaped the way the 16 September play asked: the follow comfort
/// with no extra scale, growing by up to fifteen percent as its lead reaches the clamp, the player's centre at the centre of
/// its bottom third, the horizontal lead stopping at the third-lines rather than the left and right edges, a held
/// direction that is not moving him still sliding the box, and vertical travel that moves the box by a fraction of his
/// own motion so a one-tile drop is a nudge and a long fall still fills the clamp.
///
/// <para>The replay is the pass line the 15 September ruling came from. On the 14:16 capture of that day the player stood
/// outside his own region on 5,226 of 35,011 rows, 14.9%, and on 37% of the rows he was falling, because the lead was clamped
/// to half the screen. The capture's player track is committed beside the other scenarios, so this runs in a fresh clone, and
/// it is fed through the real lead filter and the real player-activity inference rather than a copy of either; the region's
/// containment is a property of the geometry for every lead the filter can produce, so the track is the realistic input and
/// not the only one — the sweep below is the rest.</para>
///
/// <para>The expected base size and growth cap are written as the follow comfort and 1.15 here, rather than read from the
/// scale constants, so a change to those constants is a red row and not a silent one. The geometry ran on three distance
/// modes until the owner removed them on 26 September 2026; the short-box branch below stays because the property it
/// guards — the player inside by the slack however short the box — is the geometry's, not a mode's.</para>
/// </summary>
internal static class VerifyIntentRegionHoldsThePlayer
{
    private const string Track = "2026-09-15_13-16-33-496-player-track.txt";

    public static int Run()
    {
        // Each half runs whatever the other did and every failure is reported together, because an abort on the first throw
        // lets a geometry failure hide whether the replay still holds, which is exactly what a mutation of the clamp needs to see.
        var failures = new List<string>();
        void Each(string half, Action check)
        {
            try { check(); }
            catch (InvalidOperationException e) { failures.Add($"{half}: {e.Message}"); }
        }
        Each("geometry", Geometry);
        Each("replay", Replay);
        Each("held direction", HeldDirectionSlidesTheBox);
        Each("vertical travel", VerticalTravelMovesTheBox);
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(" | ", failures));
        Console.WriteLine("intent region: the geometry holds, a held direction still slides the box, a one-tile drop is a nudge and a long fall still fills the clamp, and the recorded player is inside his region on every row");
        return 0;
    }

    private static void Geometry()
    {
        const string Where = "geometry";
        float slack = Navigator.SettleRadius;
        Vector2 expectedBase = new Vector2(Weights.FollowHorizontalComfort, Weights.FollowVerticalComfort);
        Vector2 player = new(8000f, 4000f);
        Require(Weights.IntentRegionBaseScale == 1f, $"{Where}: the region is the follow comfort, not a scaled-up box; scale {Weights.IntentRegionBaseScale}");
        Require(MathF.Abs(Weights.IntentRegionGrowthCap - 0.15f) < 0.001f, $"{Where}: growth cap is fifteen percent; {Weights.IntentRegionGrowthCap}");

        var still = PlayerIntentRegion.Around(player, Vector2.Zero,false, slack);
        float below = player.Y - still.Centre.Y;
        Require(Near(still.HalfSize, expectedBase), $"{Where}: at zero lead the half-size must be the follow comfort; {still.HalfSize} against {expectedBase}");
        Require(MathF.Abs(player.X - still.Centre.X) < 0.01f, $"{Where}: at zero lead the box must be centred on the player horizontally; centre {still.Centre} player {player}");
        bool thirdFits = still.HalfSize.Y / 3f >= slack;
        float expectedBelow = thirdFits ? still.HalfSize.Y * 2f / 3f : still.HalfSize.Y - slack;
        Require(MathF.Abs(below - expectedBelow) < 0.01f, thirdFits
            ? $"{Where}: at zero lead the player's centre must be at the centre of the bottom third, {expectedBelow:0.00} px below the box's centre; it is {below:0.00}"
            : $"{Where}: where a third of the half-height is under the slack the player must sit as low as the slack allows, {expectedBelow:0.00} px below; it is {below:0.00}");
        Require(Holds(still, player, slack), $"{Where}: at zero lead the player must be inside by the slack; {Describe(still, player)}");

        foreach (Vector2 direction in new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
                     new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
        {
            var full = PlayerIntentRegion.Around(player, direction * 100000f,true, slack);
            var grownLimits = PlayerIntentRegion.LeadLimits(expectedBase * 1.15f, slack);
            bool canLead = (direction.X != 0f && grownLimits.Across > 0f)
                || (direction.Y > 0f && grownLimits.Down > 0f)
                || (direction.Y < 0f && grownLimits.Up > 0f);
            if (canLead)
                Require(Near(full.HalfSize, expectedBase * 1.15f), $"{Where} {direction}: at the clamp the half-size must be 1.15 times the comfort; {full.HalfSize} against {expectedBase * 1.15f}");
            Vector2 offset = player - full.Centre;
            if (direction.X != 0f)
                Require(MathF.Abs(MathF.Abs(offset.X) - full.HalfSize.X / 3f) < 0.01f,
                    $"{Where} {direction}: at the clamp the player must be at a third-line, not the side edge; {Describe(full, player)}");
            if (direction.Y > 0f)
                Require(MathF.Abs(offset.Y + (full.HalfSize.Y - slack)) < 0.01f,
                    $"{Where} {direction}: led downward to the clamp the player must be at the region's top edge less the slack; {Describe(full, player)}");
            if (direction.Y < 0f && full.HalfSize.Y / 3f >= slack)
                Require(MathF.Abs(offset.Y - (full.HalfSize.Y - slack)) < 0.01f,
                    $"{Where} {direction}: led upward to the clamp the player must be at the region's bottom edge less the slack; {Describe(full, player)}");
            Require(Holds(full, player, slack), $"{Where} {direction}: at the clamp the player must be inside by the slack; {Describe(full, player)}");
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
                var region = PlayerIntentRegion.Around(player, lead,true, slack);
                checkedLeads++;
                Require(Holds(region, player, slack), $"{Where}: lead {lead} must leave the player inside by the slack; {Describe(region, player)}");
                bool grown = Near(region.HalfSize, expectedBase * 1.15f);
                if (thirdFits && !grown)
                    Require(Near(region.Lead, lead), $"{Where}: a lead short of full growth must not be clamped; asked {lead}, applied {region.Lead}, half-size {region.HalfSize}");
            }
        }
        Console.WriteLine($"intent region geometry on {Where}: base {expectedBase}, player {below:0.0} px below the centre at zero lead, {checkedLeads} leads held");
    }

    /// <summary>
    /// Holding a direction that is not moving the player still slides the box: right into a wall until the left
    /// third-line, down into the floor until the top edge. Displacement-based intent is zero in both cases.
    /// </summary>
    private static void HeldDirectionSlidesTheBox()
    {
        float slack = Navigator.SettleRadius;
        Vector2 player = new(8000f, 4000f);
        var sense = new PlayerIntentRegionSense();
        Vector2 heldRight = new(Weights.IntentRegionHeldPace, 0f);
        for (int i = 0; i < 500; i++)
            sense.Update(player, player, Vector2.Zero, false, false, 60, heldRight);
        var right = sense.Region;
        float x = player.X - right.Centre.X;
        Require(x < -1f, $"holding right must move the box ahead of the player; {Describe(right, player)}");
        Require(MathF.Abs(x + right.HalfSize.X / 3f) < 1f,
            $"holding right long enough must clamp to the left third-line; {Describe(right, player)}");
        Require(Holds(right, player, slack), $"holding right must leave the player inside; {Describe(right, player)}");

        sense.Update(player, player, Vector2.Zero, false, false, 60, new Vector2(-Weights.IntentRegionHeldPace, 0f));
        float reversed = player.X - sense.Region.Centre.X;
        Require(reversed > x + 1f,
            $"a left hold at the right clamp must start moving the box left on the first tick; was {x:0.0}, now {reversed:0.0}; {Describe(sense.Region, player)}");

        Vector2 leftoverRight = new(Weights.IntentRegionHeldPace, 0f);
        for (int i = 0; i < 20; i++)
            sense.Update(player, player, leftoverRight, true, false, 60, Vector2.Zero);
        float released = player.X - sense.Region.Centre.X;
        Require(released > reversed + 2f,
            $"releasing the hold must ease back toward rest even if walk-intent still points right; was {reversed:0.0}, now {released:0.0}; {Describe(sense.Region, player)}");

        sense = new PlayerIntentRegionSense();
        Vector2 heldDown = new(0f, Weights.IntentRegionHeldPace);
        for (int i = 0; i < 600; i++)
            sense.Update(player, player, Vector2.Zero, false, false, 60, heldDown);
        var down = sense.Region;
        Require(down.Centre.Y > player.Y + 1f, $"holding down must sink the box; {Describe(down, player)}");
        float y = player.Y - down.Centre.Y;
        Require(MathF.Abs(y + (down.HalfSize.Y - slack)) < 1f,
            $"holding down long enough must clamp the player to the top edge less the slack; {Describe(down, player)}");
        Require(Holds(down, player, slack), $"holding down must leave the player inside; {Describe(down, player)}");
    }

    /// <summary>
    /// Vertical travel moves the box by a fraction of the player's own motion, and that is a
    /// different input from a held key. A one-tile drop with nothing held is a nudge, not a slam
    /// to the floor; a long fall with nothing held still fills the clamp, so dropping into a cave
    /// still takes the box with him.
    /// </summary>
    private static void VerticalTravelMovesTheBox()
    {
        float slack = Navigator.SettleRadius;
        Vector2 player = new(8000f, 4000f);
        const float tile = 16f;
        int dropTicks = 8;
        float vy = tile / dropTicks;
        var sense = new PlayerIntentRegionSense();
        for (int i = 0; i < dropTicks; i++)
            sense.Update(player, player, new Vector2(0f, vy), true, false, 60);
        var nudged = sense.Region;
        Require(nudged.Lead.Y > 1f, $"a one-tile drop must move the box down; {Describe(nudged, player)}");
        Require(nudged.Lead.Y < tile * 0.5f,
            $"a one-tile drop must not slam the box toward the floor; lead {nudged.Lead.Y:0.0} px after {tile:0} px of fall; {Describe(nudged, player)}");
        Require(Holds(nudged, player, slack), $"a one-tile drop must leave the player inside; {Describe(nudged, player)}");

        sense = new PlayerIntentRegionSense();
        for (int i = 0; i < 250; i++)
            sense.Update(player, player, new Vector2(0f, 10f), true, false, 60);
        var cave = sense.Region;
        Require(cave.Centre.Y > player.Y + 1f, $"a long fall with nothing held must still sink the box; {Describe(cave, player)}");
        float y = player.Y - cave.Centre.Y;
        Require(MathF.Abs(y + (cave.HalfSize.Y - slack)) < 1f,
            $"a long fall with nothing held must still clamp the player to the top edge less the slack; {Describe(cave, player)}");
        Require(Holds(cave, player, slack), $"a long fall must leave the player inside; {Describe(cave, player)}");
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
            sense.Update(centre, centre, activity.Travel, travelling, false, activity.Samples);
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
