#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// A world read from the telemetry's plan-dump text: one character per tile, rows top to
/// bottom, at a world offset the header names. The alphabet is the dump's, and the dump
/// writes it through <see cref="Glyph"/> so the two can never disagree: # solid, = platform,
/// _ half block, \ and / floor slopes (solid below the line drawn), &lt; and &gt; ceiling
/// slopes (solid on the side the bracket points away from), ~ water, L lava, everything
/// else air, with the markers S G E N P treated as air and remembered as positions.
/// Outside the window is solid to the sides and above and open air below, so a search
/// cannot wander off the edge of what was captured and cannot stand on ground it never saw.
/// </summary>
public sealed class TextTileWorld : ITileWorld
{
    private readonly char[,] tiles;
    public readonly int OriginX, OriginY, Width, Height;
    public readonly Dictionary<char, Point> Markers = new();

    public TextTileWorld(int originX, int originY, IReadOnlyList<string> rows)
    {
        OriginX = originX;
        OriginY = originY;
        Height = rows.Count;
        Width = 0;
        foreach (string row in rows)
            Width = Math.Max(Width, row.Length);
        tiles = new char[Width, Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                char c = x < rows[y].Length ? rows[y][x] : '#';
                if (c is 'S' or 'G' or 'E' or 'N' or 'P')
                {
                    Markers[c] = new Point(originX + x, originY + y);
                    c = '.';
                }
                tiles[x, y] = c;
            }
        }
    }

    /// <summary>
    /// Outside the window is a wall on three sides and a void below: a wall so a search cannot
    /// leave what was captured, a void so the last captured row never stands on ground nobody
    /// saw, because a floor invented under the window's bottom edge passed an all-air scenario.
    /// </summary>
    private char At(int x, int y)
    {
        int lx = x - OriginX, ly = y - OriginY;
        if (lx < 0 || ly < 0 || lx >= Width || ly >= Height)
        {
            AskedOutside = true;
            return ly >= Height ? '.' : '#';
        }
        return tiles[lx, ly];
    }

    /// <summary>
    /// Set whenever a tile outside the window is read, and cleared by whoever wants to know.
    /// A search that never asked is a search the window's edge had no part in, which is how the
    /// replay tells a pocket sealed in the world from a region the capture cut short.
    /// </summary>
    public bool AskedOutside;

    public bool InWorld(int x, int y) => x >= OriginX && y >= OriginY && x < OriginX + Width && y < OriginY + Height;

    /// <summary>
    /// Rewrite one tile inside the window, for the replay's churn check, which breaks a tile
    /// the way a pickaxe does and asks whether the planner's cache noticed. Outside the window
    /// nothing changes, the same as the game's edge.
    /// </summary>
    public void Set(int x, int y, char c)
    {
        if (InWorld(x, y))
            tiles[x - OriginX, y - OriginY] = c;
    }
    public TileShape Shape(int x, int y) => ShapeOf(At(x, y));
    public bool Water(int x, int y) => At(x, y) == '~';
    public bool Lava(int x, int y) => At(x, y) == 'L';

    /// <summary>The character at a world tile, for drawing a result over the same window.</summary>
    public char Glyph(int x, int y) => At(x, y);

    /// <summary>The shape a dump character means; anything not in the alphabet is air.</summary>
    public static TileShape ShapeOf(char c) => c switch
    {
        '#' => TileShape.Solid,
        '=' => TileShape.Platform,
        '_' => TileShape.Half,
        '\\' => TileShape.SolidLowerLeft,
        '/' => TileShape.SolidLowerRight,
        '<' => TileShape.SolidUpperLeft,
        '>' => TileShape.SolidUpperRight,
        _ => TileShape.Air,
    };

    /// <summary>The dump character for a tile, shape first because a liquid inside a block is not walkable water.</summary>
    public static char Glyph(TileShape shape, bool water, bool lava) => shape switch
    {
        TileShape.Solid => '#',
        TileShape.Platform => '=',
        TileShape.Half => '_',
        TileShape.SolidLowerLeft => '\\',
        TileShape.SolidLowerRight => '/',
        TileShape.SolidUpperLeft => '<',
        TileShape.SolidUpperRight => '>',
        _ => lava ? 'L' : water ? '~' : '.',
    };

    /// <summary>
    /// Parse one scenario in the plan-dump shape: a header line carrying "window x A..B y C..D"
    /// and, until a blank line, the rows. Extra header lines starting with "companion",
    /// "player" or "threat" are kept for the caller.
    /// </summary>
    public static TextTileWorld Parse(IReadOnlyList<string> lines, out string header, out List<string> extras)
    {
        header = "";
        extras = new List<string>();
        var rows = new List<string>();
        int ox = 0, oy = 0;
        foreach (string raw in lines)
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("tick ", StringComparison.Ordinal) || line.StartsWith("scenario ", StringComparison.Ordinal))
            {
                header = line;
                int wx = line.IndexOf("window x ", StringComparison.Ordinal);
                if (wx >= 0)
                {
                    string[] parts = line[(wx + 9)..].Split(new[] { "..", " y ", " " }, StringSplitOptions.RemoveEmptyEntries);
                    ox = int.Parse(parts[0]);
                    oy = int.Parse(parts[2]);
                }
                continue;
            }
            if (line.StartsWith("companion ", StringComparison.Ordinal) || line.StartsWith("player ", StringComparison.Ordinal) || line.StartsWith("threat ", StringComparison.Ordinal) || line.StartsWith("trail ", StringComparison.Ordinal) || line.StartsWith("markers ", StringComparison.Ordinal))
            {
                extras.Add(line);
                continue;
            }
            if (line.Length == 0)
            {
                if (rows.Count > 0)
                    break;
                continue;
            }
            rows.Add(line);
        }
        var world = new TextTileWorld(ox, oy, rows);
        // A markers line names every position outright, so a marker never has to replace the
        // shape of the tile it stands in (on a half block or a floor slope the feet tile is the
        // support itself); it overrides whatever the grid carried, and old dumps without one
        // still read their markers off the grid.
        foreach (string extra in extras)
        {
            if (!extra.StartsWith("markers ", StringComparison.Ordinal))
                continue;
            string[] parts = extra[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                string[] xy = parts[i + 1].Split(',');
                if (parts[i].Length == 1 && xy.Length == 2 && int.TryParse(xy[0], out int mx) && int.TryParse(xy[1], out int my))
                    world.Markers[parts[i][0]] = new Point(mx, my);
            }
        }
        return world;
    }
}
