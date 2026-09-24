#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>An RGB colour, three bytes.</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>
/// An RGB image in memory with the handful of primitives a tick's picture needs: filled and outlined
/// rectangles, a filled disc, a line, and text in a 3×5 bitmap font scaled up. Drawing off the edge is
/// clipped, never an error, because every picture is cut around the bodies and some marks lie outside it.
/// </summary>
public sealed class Raster
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public Raster(int width, int height, Rgb background)
    {
        Width = width; Height = height;
        Pixels = new byte[width * height * 3];
        FillRect(0, 0, width, height, background);
    }

    public void Set(int x, int y, Rgb c)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        int i = (y * Width + x) * 3;
        Pixels[i] = c.R; Pixels[i + 1] = c.G; Pixels[i + 2] = c.B;
    }

    public Rgb Get(int x, int y)
    {
        int i = (y * Width + x) * 3;
        return new Rgb(Pixels[i], Pixels[i + 1], Pixels[i + 2]);
    }

    public void FillRect(int x, int y, int w, int h, Rgb c)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(Width, x + w), y1 = Math.Min(Height, y + h);
        for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++)
                Set(xx, yy, c);
    }

    public void OutlineRect(int x, int y, int w, int h, Rgb c, int thickness = 1)
    {
        FillRect(x, y, w, thickness, c);
        FillRect(x, y + h - thickness, w, thickness, c);
        FillRect(x, y, thickness, h, c);
        FillRect(x + w - thickness, y, thickness, h, c);
    }

    public void Disc(double cx, double cy, double radius, Rgb fill, Rgb? outline = null)
    {
        int x0 = (int)Math.Floor(cx - radius - 1), x1 = (int)Math.Ceiling(cx + radius + 1);
        int y0 = (int)Math.Floor(cy - radius - 1), y1 = (int)Math.Ceiling(cy + radius + 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double d = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
                if (d <= radius) Set(x, y, outline is { } o && d > radius - 1.5 ? o : fill);
            }
    }

    public void Ring(double cx, double cy, double radius, Rgb c, double thickness = 2)
    {
        int x0 = (int)Math.Floor(cx - radius - 1), x1 = (int)Math.Ceiling(cx + radius + 1);
        int y0 = (int)Math.Floor(cy - radius - 1), y1 = (int)Math.Ceiling(cy + radius + 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double d = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
                if (d <= radius && d > radius - thickness) Set(x, y, c);
            }
    }

    /// <summary>A line of the given thickness, stepped along its longer axis; dashed when <paramref name="dash"/> is positive.</summary>
    public void Line(double x0, double y0, double x1, double y1, Rgb c, int thickness = 1, int dash = 0)
    {
        double dx = x1 - x0, dy = y1 - y0;
        int steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));
        for (int i = 0; i <= steps; i++)
        {
            if (dash > 0 && (i / dash) % 2 == 1) continue;
            double t = steps == 0 ? 0 : (double)i / steps;
            int x = (int)Math.Round(x0 + dx * t), y = (int)Math.Round(y0 + dy * t);
            FillRect(x - thickness / 2, y - thickness / 2, thickness, thickness, c);
        }
    }

    /// <summary>
    /// Text in a 3×5 bitmap font, each font pixel <paramref name="scale"/> pixels square, a column of space
    /// between glyphs. Lower case is drawn as upper case; a character the font lacks is drawn as a solid block
    /// so a missing glyph is visible rather than silently dropped — and a block rather than a hollow box,
    /// because a hollow 3×5 box is the digit zero, and a missing semicolon read as a 0 is a number the record
    /// never held.
    /// </summary>
    public int Text(int x, int y, string text, Rgb c, int scale = 2)
    {
        int cursor = x;
        foreach (char raw in text)
        {
            char ch = char.ToUpperInvariant(raw);
            if (!Font.TryGetValue(ch, out string? rows)) rows = MissingGlyph;
            for (int r = 0; r < 5; r++)
                for (int col = 0; col < 3; col++)
                    if (rows[r * 3 + col] == '1')
                        FillRect(cursor + col * scale, y + r * scale, scale, scale, c);
            cursor += 4 * scale;
        }
        return cursor - x;
    }

    public static int TextWidth(string text, int scale = 2) => text.Length * 4 * scale;

    /// <summary>What a character the font lacks is drawn as: a filled block no glyph resembles.</summary>
    internal const string MissingGlyph = "111111111111111";

    /// <summary>Whether the font draws <paramref name="ch"/> as itself rather than as <see cref="MissingGlyph"/>.</summary>
    internal static bool Draws(char ch) => Font.ContainsKey(char.ToUpperInvariant(ch));

    /// <summary>Each glyph as fifteen bits, three per row, top row first.</summary>
    private static readonly Dictionary<char, string> Font = new()
    {
        [' '] = "000000000000000", ['A'] = "010101111101101", ['B'] = "110101110101110", ['C'] = "011100100100011",
        ['D'] = "110101101101110", ['E'] = "111100110100111", ['F'] = "111100110100100", ['G'] = "011100101101011",
        ['H'] = "101101111101101", ['I'] = "111010010010111", ['J'] = "001001001101010", ['K'] = "101101110101101",
        ['L'] = "100100100100111", ['M'] = "101111111101101", ['N'] = "110101101101101", ['O'] = "010101101101010",
        ['P'] = "110101110100100", ['Q'] = "010101101110011", ['R'] = "110101110101101", ['S'] = "011100010001110",
        ['T'] = "111010010010010", ['U'] = "101101101101111", ['V'] = "101101101101010", ['W'] = "101101111111101",
        ['X'] = "101101010101101", ['Y'] = "101101010010010", ['Z'] = "111001010100111",
        ['0'] = "111101101101111", ['1'] = "010110010010111", ['2'] = "110001010100111", ['3'] = "110001010001110",
        ['4'] = "101101111001001", ['5'] = "111100110001110", ['6'] = "011100111101111", ['7'] = "111001010010010",
        ['8'] = "111101111101111", ['9'] = "111101111001110",
        ['.'] = "000000000000010", [','] = "000000000010100", [':'] = "000010000010000", ['-'] = "000000111000000",
        ['+'] = "000010111010000", ['/'] = "001001010100100", ['('] = "010100100100010", [')'] = "010001001001010",
        ['='] = "000111000111000", ['%'] = "101001010100101", ['>'] = "100010001010100", ['<'] = "001010100010001",
        ['#'] = "101111101111101", ['_'] = "000000000000111", ['?'] = "110001010000010", ['\''] = "010010000000000",
        ['*'] = "000101010101000", ['×'] = "000101010101000", [';'] = "000010000010100",
    };
}
