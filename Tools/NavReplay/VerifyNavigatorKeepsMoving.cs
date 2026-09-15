#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection;

/// <summary>
/// The orb hovered in open air for two seconds at a time in the second play of 0.27.0 (15 September 2026, capture
/// 2026-09-15_10-27-40-531, ticks 9213-9254 and 10373-10485), with the brain's cost at eight to ten milliseconds a tick
/// against its usual one or two. Nothing touched it. The navigator had been handed goals it could not plan to — a
/// firing stand in a pocket the search ran to its node limit three times over without finding, and a follow anchor
/// inside the rock under the player's floor with no free corner near it — and its answer to "no route yet and no clear
/// line" was to hover where the body was, then ask the same question again. Four scenes, each with its pass lines
/// declared before the fix was written, and each run red against the navigator that hovered.
///
/// <para>The recorded absence replays the plan dump the capture wrote at tick 10418: the body at the orb's recorded
/// centre and the goal at the stand it was sent to. Inside the recorded window that stand's pocket is sealed from the
/// body's, so the search proves the absence in one slice, where the live world was large enough to run it to the node
/// limit instead. The navigator that hovered began that same search again on every one of six hundred ticks. The goal
/// must be searched once while it and the terrain stay put, published as proven unreachable, which the brain bans a spot on,
/// and the body must hover where it waited rather than fly anywhere, because a proven absence is the positioner's to
/// answer with another place.</para>
///
/// <para>A goal inside rock is the follow anchor's case: a room over a thick floor and a goal ten tiles into the floor,
/// with no usable corner within the two tiles the planner looked. The body must be the brain's progress distance nearer
/// the goal ninety ticks in, and never still.</para>
///
/// <para>A search to its node limit is the live half of the recorded wait: an open world with more free corners than
/// one search may close, and the goal in a sealed pocket in its far corner. While the search runs the body must be the
/// progress distance nearer the pocket thirty ticks in; every search after the first must begin at least that distance
/// nearer than the one before; the body must end at least half-way from where it started to the pocket; and it must never
/// be still.</para>
///
/// <para>A long search that succeeds is a wall with one gap at its far end and the goal straight through the wall: the
/// search needs many slices, and fifteen ticks in the body must be nearer the goal by half of what the pace's ramp from
/// rest can cover, must arrive, and must never be still.</para>
///
/// <para>Two of those lines were declared differently and missed on the fixed navigator for reasons about the world rather
/// than the fix, and each says so where it is asserted.</para>
/// </summary>
internal static class VerifyNavigatorKeepsMoving
{
    // The session reader's still speed and run, the same threshold the arrival and anchors rows use.
    private const float StillSpeed = 0.3f;
    private const int StillRunTicks = 10;
    private const string Capture = "2026-09-15_10-27-40-531-plans-orb-waits-for-route.txt";

    public static int Run()
    {
        LimitPlanningWork.Unbounded = true;
        ITileWorld? previous = null;
        try { previous = MovementQueries.World; } catch (InvalidOperationException) { }
        int failures = 0;
        try
        {
            failures += TheRecordedAbsenceIsSearchedOnceAndHandedBack();
            failures += AGoalInsideRockIsApproached();
            failures += ASearchToItsNodeLimitStillFliesTowardTheGoal();
            failures += ALongSearchThatSucceedsFliesWhileItRuns();
        }
        finally
        {
            LimitPlanningWork.Unbounded = false;
            FreeSpaceSearch.WorldOverride = null;
            if (previous != null) MovementQueries.World = previous;
            ClearanceField.Shared.Invalidate();
        }
        return failures;
    }

    private static void Plug(TextTileWorld world)
    {
        MovementQueries.World = world;
        FreeSpaceSearch.WorldOverride = world;
        ClearanceField.Shared.Invalidate();
    }

    private sealed class Watch
    {
        public float Start, Farthest;
        public int Still, Longest, Searches;
        public readonly List<string> Ends = new();
        public void Tick(Navigator navigator, Vector2 origin, Vector2 centre, Vector2 velocity, int tick)
        {
            Farthest = MathF.Max(Farthest, Vector2.Distance(centre, origin));
            Still = velocity.Length() < StillSpeed ? Still + 1 : 0;
            Longest = Math.Max(Longest, Still);
            Searches = (int)navigator.SearchId;
            if (navigator.PlannedThisTick && navigator.LastSearchStop is not (FreeSpaceSearch.StopReason.ExpansionBudget or FreeSpaceSearch.StopReason.Deadline)
                && Ends.Count < 12)
                Ends.Add($"t{tick}:{navigator.LastSearchStop}");
        }
    }

    private static int TheRecordedAbsenceIsSearchedOnceAndHandedBack()
    {
        string[] lines = File.ReadAllLines(Path.Combine("Tools", "Scenarios", Capture));
        int at = Array.FindIndex(lines, l => l.StartsWith("tick 10418 ", StringComparison.Ordinal));
        if (at < 0) return Report("recorded absence: the tick 10418 block is missing from the committed capture");
        Match window = Regex.Match(lines[at], @"orb (?<ox>[\d.]+),(?<oy>[\d.]+) .*window x (?<x>\d+)\.\.\d+ y (?<y>\d+)\.\.\d+");
        var rows = new List<string>();
        for (int i = at + 3; i < lines.Length && lines[i].Length > 0 && !lines[i].StartsWith("tick ", StringComparison.Ordinal); i++)
            rows.Add(lines[i]);
        var world = new TextTileWorld(int.Parse(window.Groups["x"].Value), int.Parse(window.Groups["y"].Value), rows);
        Plug(world);
        Vector2 centre = new(float.Parse(window.Groups["ox"].Value, CultureInfo.InvariantCulture), float.Parse(window.Groups["oy"].Value, CultureInfo.InvariantCulture));
        Point g = world.Markers['G'];
        Vector2 goal = new(g.X * 16 + 8, g.Y * 16 + 8), waited = centre;

        var navigator = new Navigator();
        Vector2 velocity = Vector2.Zero;
        var watch = new Watch();
        FreeSpaceSearch.StopReason firstStop = FreeSpaceSearch.StopReason.ExpansionBudget;
        for (int tick = 0; tick < 600; tick++)
        {
            Tick(navigator, world, goal, ref centre, ref velocity);
            if (tick == 0) firstStop = navigator.LastSearchStop;
            watch.Tick(navigator, waited, centre, velocity, tick);
        }
        string ledger = $"{watch.Searches} searches begun, ends [{string.Join(" ", watch.Ends)}], status {navigator.Status}, proven unreachable {navigator.GoalProvenUnreachable}, drifted {watch.Farthest:0.0} px, longest still run {watch.Longest}";
        Console.WriteLine($"recorded absence: {ledger}");
        if (firstStop != FreeSpaceSearch.StopReason.Exhausted)
            return Report($"recorded absence premise: inside the recorded window the stand's pocket must be proven sealed on the first tick; read {firstStop}; {ledger}");
        int failures = 0;
        if (watch.Searches != 1)
            failures += Report($"recorded absence: a goal proven unreachable over unchanged terrain must be searched once in 600 ticks; {ledger}");
        // Declared first as "handed back with two strikes". The strikes count windows without progress and the route-endings
        // fixture holds them to exactly that, so the hand-back is its own published fact, which the brain bans a spot on.
        if (!navigator.GoalProvenUnreachable)
            failures += Report($"recorded absence: the goal must be published as proven unreachable, which the brain bans a spot on; {ledger}");
        if (watch.Farthest > Navigator.SettleRadius || watch.Longest >= StillRunTicks)
            failures += Report($"recorded absence: the body must hover where it waited, within the settle radius ({Navigator.SettleRadius:0} px), and never sit still; {ledger}");
        return failures;
    }

    private static int AGoalInsideRockIsApproached()
    {
        const int width = 60, height = 40, floor = 20;
        var rows = new List<string>();
        for (int y = 0; y < height; y++)
        {
            char[] row = new char[width];
            for (int x = 0; x < width; x++)
                row[x] = y == 0 || y == height - 1 || x == 0 || x == width - 1 || y >= floor ? '#' : '.';
            rows.Add(new string(row));
        }
        var world = new TextTileWorld(0, 0, rows);
        Plug(world);
        Vector2 goal = new(40 * 16 + 8, (floor + 10) * 16 + 8);
        if (CornerGraph.NearestUsable(world, goal, 2, requireSweep: false) != null)
            return Report("goal inside rock premise: the goal must have no usable corner within two tiles, which is the recorded plan_failed shape");

        var navigator = new Navigator();
        Vector2 centre = new(15 * 16 + 8, 10 * 16), velocity = Vector2.Zero;
        var watch = new Watch { Start = Vector2.Distance(centre, goal) };
        float atNinety = watch.Start;
        for (int tick = 0; tick < 240; tick++)
        {
            Tick(navigator, world, goal, ref centre, ref velocity);
            if (tick == 89) atNinety = Vector2.Distance(centre, goal);
            watch.Tick(navigator, centre, centre, velocity, tick);
        }
        string ledger = $"start {watch.Start:0} px from the goal, {atNinety:0} px at tick 90, {Vector2.Distance(centre, goal):0} px at tick 240, status {navigator.Status}, {watch.Searches} searches, longest still run {watch.Longest}";
        Console.WriteLine($"goal inside rock: {ledger}");
        int failures = 0;
        if (watch.Start - atNinety < Weights.ObjectiveProgressPixels)
            failures += Report($"goal inside rock: the body must be at least {Weights.ObjectiveProgressPixels:0} px nearer the goal by tick 90; {ledger}");
        if (watch.Longest >= StillRunTicks)
            failures += Report($"goal inside rock: the body must never sit still for {StillRunTicks} ticks; {ledger}");
        return failures;
    }

    private static int ASearchToItsNodeLimitStillFliesTowardTheGoal()
    {
        // Wide enough that the body's free space holds more corners than one search may close.
        const int width = 330, height = 270;
        var rows = new List<string>();
        for (int y = 0; y < height; y++)
        {
            char[] row = new char[width];
            for (int x = 0; x < width; x++)
            {
                bool border = y == 0 || y == height - 1 || x == 0 || x == width - 1;
                // A sealed six-by-six pocket with a two-tile shell, near the far corner.
                bool shell = x is >= 300 and <= 309 && y is >= 250 and <= 259;
                bool pocket = x is >= 302 and <= 307 && y is >= 252 and <= 257;
                row[x] = border || (shell && !pocket) ? '#' : '.';
            }
            rows.Add(new string(row));
        }
        var world = new TextTileWorld(0, 0, rows);
        Plug(world);
        Vector2 goal = new(304 * 16 + 8, 254 * 16 + 8);
        Vector2 centre = new(20 * 16 + 8, 20 * 16 + 8), velocity = Vector2.Zero;
        var navigator = new Navigator();
        var watch = new Watch { Start = Vector2.Distance(centre, goal) };
        float atThirty = watch.Start;
        bool sawNodeLimit = false;
        long seenSearch = 0;
        var searchStarts = new List<float>();
        for (int tick = 0; tick < 1200; tick++)
        {
            float before = Vector2.Distance(centre, goal);
            Tick(navigator, world, goal, ref centre, ref velocity);
            if (navigator.SearchId != seenSearch) { seenSearch = navigator.SearchId; searchStarts.Add(before); }
            if (tick == 29) atThirty = Vector2.Distance(centre, goal);
            sawNodeLimit |= navigator.PlannedThisTick && navigator.LastSearchStop == FreeSpaceSearch.StopReason.NodeLimit;
            watch.Tick(navigator, centre, centre, velocity, tick);
        }
        float end = Vector2.Distance(centre, goal);
        string ledger = $"start {watch.Start:0} px from the pocket, {atThirty:0} px at tick 30, {end:0} px at tick 1200; {watch.Searches} searches begun at [{string.Join(" ", searchStarts.ConvertAll(d => d.ToString("0", CultureInfo.InvariantCulture)))}] px, ends [{string.Join(" ", watch.Ends)}], status {navigator.Status}, longest still run {watch.Longest}";
        Console.WriteLine($"node limit: {ledger}");
        if (!sawNodeLimit)
            return Report($"node limit premise: the search must reach its node limit, or this is not the live wait; {ledger}");
        int failures = 0;
        if (watch.Start - atThirty < Weights.ObjectiveProgressPixels)
            failures += Report($"node limit: the body must be at least {Weights.ObjectiveProgressPixels:0} px nearer the pocket by tick 30 while the search runs; {ledger}");
        // Declared first as "no more than three searches in 1200 ticks", which assumed one capped search explores all the way
        // to the pocket. It does not: the clearance price widens the search, so each capped search reaches about a fifth of
        // the way and every leg genuinely earns the next. What the line was for is that a search is never asked again without
        // the body having got nearer, and that is what is asserted: the navigator that hovered began forty searches here, all
        // from the same place.
        for (int i = 1; i < searchStarts.Count; i++)
            if (searchStarts[i] > searchStarts[i - 1] - Weights.ObjectiveProgressPixels)
                failures += Report($"node limit: search {i + 1} must begin at least {Weights.ObjectiveProgressPixels:0} px nearer the pocket than search {i}; {ledger}");
        if (end > watch.Start * 0.5f)
            failures += Report($"node limit: the body must end at least half-way to the pocket; {ledger}");
        if (watch.Longest >= StillRunTicks)
            failures += Report($"node limit: the body must never sit still for {StillRunTicks} ticks; {ledger}");
        return failures;
    }

    private static int ALongSearchThatSucceedsFliesWhileItRuns()
    {
        const int width = 300, height = 200, wallRow = 100;
        var rows = new List<string>();
        for (int y = 0; y < height; y++)
        {
            char[] row = new char[width];
            for (int x = 0; x < width; x++)
            {
                bool border = y == 0 || y == height - 1 || x == 0 || x == width - 1;
                bool wall = y is >= wallRow and <= wallRow + 1 && x < width - 5;
                row[x] = border || wall ? '#' : '.';
            }
            rows.Add(new string(row));
        }
        var world = new TextTileWorld(0, 0, rows);
        Plug(world);
        Vector2 goal = new(40 * 16 + 8, (wallRow + 12) * 16 + 8);
        Vector2 centre = new(40 * 16 + 8, (wallRow - 12) * 16 + 8), velocity = Vector2.Zero;
        var navigator = new Navigator();
        var watch = new Watch { Start = Vector2.Distance(centre, goal) };
        float atFifteen = watch.Start;
        int pendingTicks = 0, arrivedAt = -1;
        for (int tick = 0; tick < 2400 && arrivedAt < 0; tick++)
        {
            Tick(navigator, world, goal, ref centre, ref velocity);
            if (navigator.Status == Navigator.ExecutionStatus.Pending) pendingTicks++;
            if (tick == 14) atFifteen = Vector2.Distance(centre, goal);
            if (navigator.Arrived) arrivedAt = tick;
            watch.Tick(navigator, centre, centre, velocity, tick);
        }
        string ledger = $"start {watch.Start:0} px, {atFifteen:0} px at tick 15, {pendingTicks} ticks pending, arrived at tick {arrivedAt}, {watch.Searches} searches, status {navigator.Status}, longest still run {watch.Longest}";
        Console.WriteLine($"long search: {ledger}");
        if (pendingTicks < 5)
            return Report($"long search premise: the search must need several slices; {ledger}");
        int failures = 0;
        // Declared first as the progress distance by tick 15, which is more than the pace's own ease-in covers from rest in
        // fifteen ticks. The bar is half of what the speed-change cap allows from rest over those ticks instead; the
        // navigator that hovered moved away from the goal over the same ticks.
        float ramp = 0.5f * OrbPace.SpeedChange * 15f * 15f;
        if (watch.Start - atFifteen < 0.5f * ramp)
            failures += Report($"long search: the body must be at least {0.5f * ramp:0.0} px nearer the goal by tick 15 while the search runs, half of what the pace's ramp from rest covers; {ledger}");
        if (arrivedAt < 0)
            failures += Report($"long search: the body must arrive; {ledger}");
        if (watch.Longest >= StillRunTicks)
            failures += Report($"long search: the body must never sit still for {StillRunTicks} ticks; {ledger}");
        return failures;
    }

    private static void Tick(Navigator navigator, ITileWorld world, Vector2 goal, ref Vector2 centre, ref Vector2 velocity)
    {
        Controls controls = navigator.MoveTo(new OrbState(centre, velocity), goal);
        velocity = OrbPace.Step(velocity, controls.Desired, controls.Burst);
        centre += velocity;
        CircleContact.Resolve(world, ref centre, ref velocity);
    }

    private static int Report(string message)
    {
        Console.WriteLine(message);
        return 1;
    }
}
