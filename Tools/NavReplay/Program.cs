#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using AICompanion.Brain.DecisionMatrix.Navigation;

// Replays navigation scenarios off-game: for every file given, load its tile window, ask the
// mod's own planner for the path the game asked for (S to G, the recorded plan) and, when the
// player's feet are also in the window, the path to the player (N or S to P), draw the answer
// over the window, and say pass or fail. A scenario is the telemetry's plan-dump shape (or a
// scenario file, which is the same with entity lines). A scenario passes when the recorded
// goal is reached by a whole path; the player line is printed beside it because "could it
// have reached the player" is the question the design is judged on, and the two can differ
// when the positioner picked a spot the player never stood on.
// Exit code: 0 when every executed scenario passed and nothing was skipped or missing, 1
// otherwise; the counts are in the last line, never in the status, which wraps at 256.

int failed = 0, passed = 0, skipped = 0, missing = 0;
bool traceJump = false;
var files = new List<string>();
foreach (string arg in args)
{
    if (arg == "--trace-jump")
        traceJump = true;
    else if (Directory.Exists(arg))
        files.AddRange(Directory.GetFiles(arg, "*.txt"));
    else if (File.Exists(arg))
        files.Add(arg);
    else
    {
        Console.Error.WriteLine($"no such file: {arg}");
        missing++;
    }
}
if (files.Count == 0)
{
    Console.Error.WriteLine("usage: NavReplay <scenario.txt | folder> ...");
    return 2;
}
files.Sort(StringComparer.Ordinal);

foreach (string file in files)
{
    foreach ((int index, List<string> block) in Blocks(File.ReadAllLines(file)))
    {
        var world = TextTileWorld.Parse(block, out string header, out List<string> extras);
        NavGrid.World = world;
        AStar.AllowLava = false;
        AStar.Avoid.Clear();
        string name = $"{Path.GetFileName(file)}#{index}";

        Point? start = world.Markers.TryGetValue('S', out Point s) ? s : world.Markers.TryGetValue('N', out Point n0) ? n0 : null;
        Point? goal = world.Markers.TryGetValue('G', out Point g) ? g : null;
        Point? player = world.Markers.TryGetValue('P', out Point p) ? p : null;
        // The header carries the recorded request in full; the grid only carries what the window
        // captured and what no other marker hid. A goal the header names but the window does not
        // hold is untestable, never a pass to the player's tile instead.
        Point? headerGoal = HeaderPoint(header, "goal");
        Point? headerStart = HeaderPoint(header, "start");
        goal ??= headerGoal ?? (headerStart == null && player != null ? player : null);
        start ??= headerStart;
        if (start == null || goal == null)
        {
            Console.WriteLine($"SKIP {name}: no start or goal ({header})");
            skipped++;
            continue;
        }
        if (!world.InWorld(goal.Value.X, goal.Value.Y))
        {
            Console.WriteLine($"SKIP {name}: recorded goal {Fmt(goal.Value)} is outside the captured window ({header})");
            skipped++;
            continue;
        }

        // --trace-jump: the simulated jump from S to G, tick by tick, from standing and from a
        // run-up, so a jump the planner refuses can be read as the arc the body would fly.
        if (traceJump)
        {
            TraceJump(start.Value, goal.Value);
            continue;
        }

        Point? from = Ground(world, start.Value);
        Search main = Run(from, goal.Value);
        Search toPlayer = player is Point pl && pl != goal ? Run(from, pl) : main;
        bool pass = main.Pass;
        if (pass) passed++; else failed++;

        Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}: {header}");
        Console.WriteLine($"     recorded goal: start {Fmt(start.Value)} -> {(from == null ? "no standable tile" : Fmt(from.Value))}, goal {Fmt(goal.Value)}, {Describe(main)}");
        if (player is Point pl2 && pl2 != goal)
            Console.WriteLine($"     player:        {(toPlayer.Pass ? "reached" : "NOT reached")} at {Fmt(pl2)}, {Describe(toPlayer)}");
        // The positioner's own question, with the positioner's own budget: is the goal inside the
        // region the companion can flood to from its feet? "out" with a complete region is a goal
        // that can never be reached; "out" with the budget spent is a goal the flood did not get to.
        if (from is Point f)
        {
            HashSet<Point> region = AStar.Region(f, AICompanion.Brain.DecisionMatrix.Decision.Weights.ReachFloodBudget, out bool complete);
            string goalIn = region.Contains(goal.Value) ? "in" : "out";
            string playerIn = player is Point pl3 ? (region.Contains(pl3) ? ", player in" : ", player out") : "";
            Console.WriteLine($"     reach flood:   {region.Count} tiles, {(complete ? "complete" : "budget spent")}, goal {goalIn}{playerIn}");
            // The player's trail is the design's own pass line: every tile the player's feet were
            // in is a tile the companion must be able to stand in and get to. The first tile the
            // grid refuses names the missing link; a tile outside a complete region is refused too.
            foreach (string extra in extras)
            {
                if (!extra.StartsWith("trail "))
                    continue;
                int inWindow = 0, total = 0;
                string? refused = null;
                foreach (string pair in extra[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] xy = pair.Split(',');
                    var tile = new Point(int.Parse(xy[0]), int.Parse(xy[1]));
                    total++;
                    if (tile.X < world.OriginX || tile.X >= world.OriginX + world.Width || tile.Y < world.OriginY || tile.Y >= world.OriginY + world.Height)
                        continue;
                    inWindow++;
                    if (refused != null)
                        continue;
                    if (!NavGrid.IsStandable(tile.X, tile.Y))
                        refused = $"{Fmt(tile)} not standable";
                    else if (complete && !region.Contains(tile))
                        refused = $"{Fmt(tile)} unreachable from the start";
                }
                Console.WriteLine($"     trail:         {inWindow} of {total} tiles in the window, {(refused == null ? "every one standable and reachable" : "first refused " + refused)}");
            }
        }
        Console.WriteLine(Draw(world, main.Path, start.Value, goal.Value, pass ? null : main.Closed));
    }
}
Console.WriteLine($"{passed}/{passed + failed} passed, {skipped} skipped, {missing} missing inputs, planner {Timing.PlannerMs:F0} ms in total");
return failed == 0 && skipped == 0 && missing == 0 && passed > 0 ? 0 : 1;

static string Fmt(Point p) => $"{p.X},{p.Y}";

static void TraceJump(Point start, Point goal)
{
    if (NavGrid.StandAt(start.X, start.Y, false) is not BodyPhysics.Pose from)
    {
        Console.WriteLine($"trace-jump: no pose at {Fmt(start)}");
        return;
    }
    int rise = start.Y - goal.Y;
    float scale = rise >= 2 ? BodyPhysics.JumpScaleForTiles(rise) : 1f;
    foreach (float startVx in new[] { 0f, Math.Sign(goal.X - start.X) * BodyPhysics.WalkSpeed })
    {
        Console.WriteLine($"trace-jump {Fmt(start)} -> {Fmt(goal)} scale {scale:F2} startVx {startVx:F1}: pose left {from.Left} bottom {from.Bottom}");
        BodyPhysics.Pose? landing = BodyPhysics.SimulateJump(NavGrid.World, from, scale, startVx, goal.X, 120, out int ticks, tick =>
            Console.WriteLine($"   t{tick.Tick,3} left {tick.Left,7:F1} bottom {tick.Bottom,7:F1} vx {tick.Vx,5:F2} vy {tick.Vy,5:F2} feet {tick.FeetColumn},{tick.FeetRow}"));
        Console.WriteLine(landing is BodyPhysics.Pose l
            ? $"   landed after {ticks} ticks at left {l.Left:F1} bottom {l.Bottom:F1}, feet tile {(int)Math.Floor(l.CentreX / 16f)},{BodyPhysics.FeetRow(l.Bottom)}"
            : $"   never landed within {ticks} ticks");
    }
}

static string Describe(Search s)
    => $"{(s.Path == null ? "no path" : $"{s.Path.Steps.Count} steps{(s.Path.Partial ? $", partial, ends {Fmt(s.Path.Goal)}" : "")}")}, {s.Used} expansions, {s.Closed.Count} tiles reached";

// "goal 3526,480" out of a dump header; null when the header does not carry that key.
static Point? HeaderPoint(string header, string key)
{
    int at = header.IndexOf(key + " ", StringComparison.Ordinal);
    if (at < 0)
        return null;
    string[] xy = header[(at + key.Length + 1)..].Split(' ', 2)[0].Split(',');
    return xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y) ? new Point(x, y) : null;
}

// A start captured mid-jump lands first: the game plans only from the ground now, so the
// question is what the body can do from where it comes down. The fall runs to the bottom of
// the captured window; below it the world is unknown and the start stays unstandable.
static Point? Ground(TextTileWorld world, Point start)
{
    Point grounded = start;
    while (grounded.Y < world.OriginY + world.Height && !NavGrid.IsBlock(grounded.X, grounded.Y + 1) && !NavGrid.IsStandable(grounded.X, grounded.Y))
        grounded.Y++;
    return NavGrid.NearestStandable(grounded, 2);
}

// Each search keeps its own closed set: the recorded-goal search and the player search share
// the planner's trace slot, and the drawing must show the tiles the search it explains reached.
static Search Run(Point? from, Point goal)
{
    Point? to = NavGrid.NearestStandable(goal, 3);
    AStar.TraceClosed = new HashSet<Point>();
    int used = 0;
    var clock = System.Diagnostics.Stopwatch.StartNew();
    NavPath? path = from == null || to == null ? null : AStar.Find(from.Value, to.Value, 20000, out used);
    Timing.PlannerMs += clock.Elapsed.TotalMilliseconds;
    return new Search(path, used, new HashSet<Point>(AStar.TraceClosed), path != null && !path.Partial);
}

// A plans file holds many dumps separated by blank lines; a scenario file holds one.
static IEnumerable<(int, List<string>)> Blocks(string[] lines)
{
    var block = new List<string>();
    int index = 0;
    bool inRows = false;
    foreach (string line in lines)
    {
        if (line.Length == 0)
        {
            if (inRows && block.Count > 0)
            {
                yield return (index++, block);
                block = new List<string>();
                inRows = false;
            }
            continue;
        }
        block.Add(line);
        if (!line.StartsWith("tick ") && !line.StartsWith("scenario ") && !line.StartsWith("companion ") && !line.StartsWith("player ") && !line.StartsWith("threat ") && !line.StartsWith("trail ") && !line.StartsWith("markers "))
            inRows = true;
    }
    if (block.Count > 0)
        yield return (index, block);
}

// Path steps as w j d f; on a failure, every tile the search closed as c, so "no path" reads
// as "it got this far and no edge left from here".
static string Draw(TextTileWorld world, NavPath? path, Point start, Point goal, HashSet<Point>? reached)
{
    var overlay = new Dictionary<Point, char>();
    if (reached != null)
        foreach (Point t in reached)
            overlay[t] = 'c';
    if (path != null)
        foreach (NavStep step in path.Steps)
            overlay[step.Tile] = step.Kind switch { MoveKind.Walk => 'w', MoveKind.Jump => 'j', MoveKind.Drop => 'd', _ => 'f' };
    overlay[start] = 'N';
    overlay[goal] = 'G';
    if (world.Markers.TryGetValue('P', out Point p) && p != goal)
        overlay[p] = 'P';
    var sb = new StringBuilder();
    for (int y = world.OriginY; y < world.OriginY + world.Height; y++)
    {
        sb.Append("     ");
        for (int x = world.OriginX; x < world.OriginX + world.Width; x++)
        {
            var t = new Point(x, y);
            if (overlay.TryGetValue(t, out char c)) sb.Append(c);
            else
            {
                char g = world.Glyph(x, y);
                sb.Append(g == '.' && NavGrid.IsStandable(x, y) ? 'o' : g);
            }
        }
        sb.Append('\n');
    }
    return sb.ToString();
}

/// <summary>Planner wall-clock over the run, because the simulated jump is the first edge whose cost is a number worth watching.</summary>
static class Timing
{
    public static double PlannerMs;
}

/// <summary>One search's answer with its own closed set, so two searches in one scenario never share a drawing.</summary>
record Search(NavPath? Path, int Used, HashSet<Point> Closed, bool Pass);
