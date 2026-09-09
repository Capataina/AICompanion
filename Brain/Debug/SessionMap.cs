#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.Debug;

/// <summary>
/// One zoomed-out picture of a whole session: everywhere the body went, everywhere it was asked
/// to go, and for each of those asks whether the distance ever closed.
///
/// It exists because every dump this project writes is a window around an anomaly, and a window
/// per anomaly can only ever show what a detector already decided was worth looking at. A session
/// where nothing tripped a threshold produces no windows at all and reads as clean, which is the
/// same blind spot the behaviour census closes from the other side: the census says which
/// categories were empty, and this says where in the world the emptiness happened.
///
/// The reading it is built for is Caner's own, from the playtest of 2026-09-09: a spot marked on
/// the screen that the companion clearly wants and never approaches, while the body moves the
/// whole time. That is invisible to a stuck detector, because the body is not stuck, and
/// invisible to a follow detector, because it can happen at any distance from the player. On this
/// map it is a mark with a "never closed" line beside it, and it is legible without anyone
/// knowing in advance to look for it.
///
/// The map is deliberately coarse. Everywhere the body went in nine minutes is thousands of tiles
/// across, so it is downsampled to something that fits in a terminal and each cell says what the
/// most interesting thing in it was — an ask beats a footprint, because a footprint is where the
/// body was and an ask is what it wanted.
/// </summary>
public static class SessionMap
{
    /// <summary>One goal the brain asked for, held as one episode however many ticks it lasted.</summary>
    private sealed class Ask
    {
        public Point Tile;
        public float First;
        public float Best;
        public int Ticks;
        public bool Reached;
    }

    private static readonly Dictionary<Point, int> body = new();
    private static readonly HashSet<Point> player = new();
    private static readonly List<Ask> asks = new();
    private static Ask? open;

    /// <summary>How wide and tall the drawn map may be, in characters; a terminal's width and a screenful.</summary>
    private const int MaxColumns = 170;
    private const int MaxRows = 56;

    /// <summary>How much closer an ask must get before its distance counts as having closed, in px.</summary>
    private const float ClosedPx = 32f;

    /// <summary>A ceiling on the tiles remembered, so a very long session cannot grow without bound.</summary>
    private const int MaxTilesRemembered = 200_000;

    public static void Reset()
    {
        body.Clear();
        player.Clear();
        asks.Clear();
        open = null;
    }

    /// <summary>Where the body and the player are this tick, and what the body was asked for; called once a tick.</summary>
    public static void Watch(Point companionFeet, Point playerFeet, Point? goal, float distanceToGoal)
    {
        if (body.Count < MaxTilesRemembered)
            body[companionFeet] = body.TryGetValue(companionFeet, out int seen) ? seen + 1 : 1;
        if (player.Count < MaxTilesRemembered)
            player.Add(playerFeet);

        if (goal is not Point want)
        {
            open = null;
            return;
        }
        // An ask is one episode of wanting one place, so the goal drifting a tile under an
        // unchanged request does not become a second entry; the same slack the navigator uses to
        // decide whether the goal moved at all.
        if (open == null || Math.Abs(open.Tile.X - want.X) > GoalSlackTiles || Math.Abs(open.Tile.Y - want.Y) > GoalSlackTiles)
        {
            open = new Ask { Tile = want, First = distanceToGoal, Best = distanceToGoal };
            asks.Add(open);
        }
        open.Ticks++;
        open.Tile = want;
        if (distanceToGoal < open.Best)
            open.Best = distanceToGoal;
        if (distanceToGoal <= ArrivedPx)
            open.Reached = true;
    }

    /// <summary>The same slack the navigator applies, so the two agree on what counts as one goal.</summary>
    private const int GoalSlackTiles = 2;

    /// <summary>Close enough to the ask to call it arrived, in px; the navigator's own arrival slack.</summary>
    private const float ArrivedPx = 12f;

    /// <summary>How long an ask must be held before it is worth a line of its own, in ticks; a second.</summary>
    private const int WorthReporting = 60;

    public static string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("session map — everywhere the body went, and everywhere it was asked to go");
        sb.AppendLine();
        if (body.Count == 0)
        {
            sb.AppendLine("  nothing was recorded, which means the companion never ran");
            return sb.ToString();
        }

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        void Grow(Point p)
        {
            minX = Math.Min(minX, p.X);
            maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y);
            maxY = Math.Max(maxY, p.Y);
        }
        foreach (Point p in body.Keys)
            Grow(p);
        foreach (Point p in player)
            Grow(p);
        foreach (Ask a in asks)
            Grow(a.Tile);

        int width = maxX - minX + 1, height = maxY - minY + 1;
        int scale = Math.Max(1, Math.Max((width + MaxColumns - 1) / MaxColumns, (height + MaxRows - 1) / MaxRows));
        int columns = (width + scale - 1) / scale, rows = (height + scale - 1) / scale;

        var grid = new char[rows, columns];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                grid[r, c] = ' ';

        // Least interesting first, so the more interesting thing in a cell survives: the player's
        // route, then the body's, then what the body was asked for.
        foreach (Point p in player)
            Put(grid, rows, columns, p, minX, minY, scale, '.');
        foreach ((Point p, int seen) in body)
            Put(grid, rows, columns, p, minX, minY, scale, seen > DenseTicks ? '#' : '+');
        foreach (Ask a in asks)
        {
            if (a.Ticks < WorthReporting)
                continue;
            char glyph = a.Reached ? 'G' : a.First - a.Best > ClosedPx ? '>' : 'x';
            Put(grid, rows, columns, a.Tile, minX, minY, scale, glyph);
        }

        sb.AppendLine($"  {width} by {height} tiles, one character per {scale} by {scale} tiles, tile {minX},{minY} at the top left");
        sb.AppendLine("  . the player went there   + the body passed   # the body spent time there");
        sb.AppendLine("  G asked for and reached   > asked for and got closer   x asked for and never got closer");
        sb.AppendLine();
        for (int r = 0; r < rows; r++)
        {
            var line = new StringBuilder("  ");
            for (int c = 0; c < columns; c++)
                line.Append(grid[r, c]);
            sb.AppendLine(line.ToString().TrimEnd());
        }

        sb.AppendLine();
        sb.AppendLine("  every place asked for, longest held first");
        var held = new List<Ask>(asks);
        held.RemoveAll(a => a.Ticks < WorthReporting);
        held.Sort((a, b) => b.Ticks.CompareTo(a.Ticks));
        if (held.Count == 0)
            sb.AppendLine("    nothing was asked for long enough to report");
        for (int i = 0; i < held.Count && i < AsksListed; i++)
        {
            Ask a = held[i];
            string verdict = a.Reached ? "reached"
                : a.First - a.Best > ClosedPx ? $"closed {a.First - a.Best:n0} px and stopped"
                : "never got closer";
            sb.AppendLine($"    {a.Tile.X},{a.Tile.Y}  held {a.Ticks,6:n0} ticks   from {a.First,7:n0} px   best {a.Best,7:n0} px   {verdict}");
        }
        if (held.Count > AsksListed)
            sb.AppendLine($"    and {held.Count - AsksListed:n0} more, shorter held");
        return sb.ToString();
    }

    /// <summary>How many ticks in one tile before the body is drawn as having spent time there rather than passed through.</summary>
    private const int DenseTicks = 30;

    /// <summary>How many asks the list under the map names before folding the rest into a count.</summary>
    private const int AsksListed = 40;

    private static void Put(char[,] grid, int rows, int columns, Point tile, int minX, int minY, int scale, char glyph)
    {
        int c = (tile.X - minX) / scale, r = (tile.Y - minY) / scale;
        if (r < 0 || r >= rows || c < 0 || c >= columns)
            return;
        grid[r, c] = glyph;
    }
}
