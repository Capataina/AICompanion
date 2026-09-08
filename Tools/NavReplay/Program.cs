#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using AICompanion.Brain.DecisionMatrix.Navigation;

// Replays navigation scenarios off-game: for every file given, load its tile window, ask the
// mod's own planner for a path from the companion's feet to the player's feet, draw the answer
// over the window, and say pass or fail. A scenario is the telemetry's plan-dump shape (or a
// scenario file, which is the same with entity lines); S or N is the start, G or P the goal.
// Exit code is the number of failed scenarios, so a shell loop or a CI step reads it directly.

int failed = 0, total = 0;
var files = new List<string>();
foreach (string arg in args)
{
    if (Directory.Exists(arg))
        files.AddRange(Directory.GetFiles(arg, "*.txt"));
    else if (File.Exists(arg))
        files.Add(arg);
    else
        Console.Error.WriteLine($"no such file: {arg}");
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
        total++;
        var world = TextTileWorld.Parse(block, out string header, out _);
        NavGrid.World = world;
        AStar.AllowLava = false;
        AStar.Avoid.Clear();

        Point? start = world.Markers.TryGetValue('N', out Point n) ? n : world.Markers.TryGetValue('S', out Point s) ? s : null;
        Point? goal = world.Markers.TryGetValue('P', out Point p) ? p : world.Markers.TryGetValue('G', out Point g) ? g : null;
        string name = $"{Path.GetFileName(file)}#{index}";
        if (start == null || goal == null)
        {
            Console.WriteLine($"SKIP {name}: no start or goal marker ({header})");
            continue;
        }

        // A start captured mid-jump lands first: the game plans only from the ground now, so
        // the question is what the body can do from where it comes down.
        Point grounded = start.Value;
        for (int drop = 0; drop < 12 && !NavGrid.IsSupport(grounded.X, grounded.Y + 1) && !NavGrid.IsSolid(grounded.X, grounded.Y + 1); drop++)
            grounded.Y++;
        Point? from = NavGrid.NearestStandable(grounded, 2);
        Point? to = NavGrid.NearestStandable(goal.Value, 3);
        AStar.TraceClosed = new HashSet<Point>();
        int used = 0;
        NavPath? path = from == null || to == null ? null : AStar.Find(from.Value, to.Value, 20000, out used);
        bool pass = path != null && !path.Partial;
        if (!pass) failed++;

        Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}: {header}");
        Console.WriteLine($"     start {Fmt(start.Value)} -> {(from == null ? "no standable tile" : Fmt(from.Value))}, goal {Fmt(goal.Value)} -> {(to == null ? "no standable tile" : Fmt(to.Value))}, {(path == null ? "no path" : $"{path.Steps.Count} steps{(path.Partial ? $", partial, ends {Fmt(path.Goal)}" : "")}")}, {used} expansions, {AStar.TraceClosed.Count} tiles reached");
        Console.WriteLine(Draw(world, path, start.Value, goal.Value, pass ? null : AStar.TraceClosed));
    }
}
Console.WriteLine($"{total - failed}/{total} passed");
return failed;

static string Fmt(Point p) => $"{p.X},{p.Y}";

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
        if (!line.StartsWith("tick ") && !line.StartsWith("scenario ") && !line.StartsWith("companion ") && !line.StartsWith("player ") && !line.StartsWith("threat "))
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
    overlay[goal] = 'P';
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
