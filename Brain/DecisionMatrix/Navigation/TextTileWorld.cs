#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// A world read from the telemetry's plan-dump text: one character per tile, rows top to
/// bottom, at a world offset the header names. The alphabet is the dump's: # solid,
/// = platform or half block, ~ water, L lava, and everything else air, with the markers
/// S G E N P treated as air and remembered as positions. Outside the window is solid, so
/// a search cannot wander off the edge of what was captured.
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

    private char At(int x, int y)
    {
        int lx = x - OriginX, ly = y - OriginY;
        return lx < 0 || ly < 0 || lx >= Width || ly >= Height ? '#' : tiles[lx, ly];
    }

    public bool InWorld(int x, int y) => At(x, y) != '#' || (x >= OriginX && y >= OriginY && x < OriginX + Width && y < OriginY + Height);
    public bool Solid(int x, int y) => At(x, y) == '#';
    public bool Support(int x, int y) => At(x, y) is '#' or '=';
    public bool Water(int x, int y) => At(x, y) == '~';
    public bool Lava(int x, int y) => At(x, y) == 'L';

    /// <summary>The character at a world tile, for drawing a result over the same window.</summary>
    public char Glyph(int x, int y) => At(x, y);

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
            if (line.StartsWith("companion ", StringComparison.Ordinal) || line.StartsWith("player ", StringComparison.Ordinal) || line.StartsWith("threat ", StringComparison.Ordinal))
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
        return new TextTileWorld(ox, oy, rows);
    }
}
