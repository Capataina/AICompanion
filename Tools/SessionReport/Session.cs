#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// One column of a session file, read once and kept in both forms: the raw text, and the number
/// each cell parses to with NaN where it does not parse. <see cref="Unparsed"/> counts the cells
/// that held something and yielded no number, so a check reading a column the writer changed the
/// shape of reports a broken instrument instead of quietly measuring zeroes.
/// </summary>
public sealed class Column
{
    public required string Name { get; init; }
    public required string[] Text { get; init; }
    public required float[] Number { get; init; }
    public int Unparsed { get; init; }
}

/// <summary>
/// A parsed telemetry file, addressed by column name and never by index. The names are the
/// authority because columns are inserted in the middle whenever the brain grows a new fact:
/// reading <c>player_tile</c> at index 55 after two columns landed in front of it produced a
/// "mean distance of 3,520 tiles" on 2026-09-09, which is the defect this class exists to make
/// impossible. A check declares the columns it needs and is skipped, loudly, when one is absent,
/// so an old file reads as reduced coverage rather than as a clean run.
/// </summary>
public sealed class Session
{
    private readonly Dictionary<string, Column> byName = new(StringComparer.Ordinal);

    public string Path { get; private init; } = "";
    public int Count { get; private init; }
    public IReadOnlyList<string> Names { get; private init; } = Array.Empty<string>();

    /// <summary>
    /// Session-wide facts written before the tabular header. Metadata is deliberately separate
    /// from the rows: a start timestamp describes the file, whereas repeating it in every row
    /// invites a reader to mistake a game-tick estimate for an observed wall-clock timestamp.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; private init; } = new Dictionary<string, string>();

    /// <summary>Rows the file held that had the wrong number of cells; a truncated last line is the usual cause.</summary>
    public int Ragged { get; private init; }

    public bool Has(params string[] names) => names.All(byName.ContainsKey);

    public Column this[string name] => byName[name];

    public Column? Find(string name) => byName.TryGetValue(name, out Column? c) ? c : null;

    /// <summary>The tick column, or the row index where the file has none, so every finding can name a place.</summary>
    public float[] Ticks { get; private init; } = Array.Empty<float>();

    public static Session Load(string path)
    {
        string[] lines = File.ReadAllLines(path);
        int headerRow = Array.FindIndex(lines, line => line.Length > 0 && !line.StartsWith('#'));
        if (headerRow < 0)
            throw new InvalidDataException($"{path} holds {lines.Length} line(s); a session needs a tabular header.");

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < headerRow; i++)
            AddMetadata(metadata, lines[i]);

        // The writer opens the stream as UTF-8 and the framework prefixes a byte-order mark, so the
        // first column's name arrives as "﻿tick" and every lookup for "tick" misses.
        string[] names = lines[headerRow].TrimStart('﻿').Split('\t');
        int width = names.Length;

        var cells = new List<string[]>(lines.Length - 1);
        int ragged = 0;
        for (int i = headerRow + 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
                continue;
            // A '#' line after the header is the recorder's closing trailer (schema 0.28.0 ends a normal closure with
            // '# end='), so it is metadata; a row always starts with its tick and never with '#'.
            if (lines[i].StartsWith('#'))
            {
                AddMetadata(metadata, lines[i]);
                continue;
            }
            string[] row = lines[i].Split('\t');
            if (row.Length != width)
            {
                ragged++;
                continue;
            }
            cells.Add(row);
        }

        var columns = new Dictionary<string, Column>(StringComparer.Ordinal);
        for (int c = 0; c < width; c++)
        {
            var text = new string[cells.Count];
            var number = new float[cells.Count];
            int unparsed = 0;
            for (int r = 0; r < cells.Count; r++)
            {
                string cell = cells[r][c];
                text[r] = cell;
                number[r] = ParseNumber(cell);
                if (float.IsNaN(number[r]) && cell.Length > 0 && cell != "-")
                    unparsed++;
            }
            columns[names[c]] = new Column { Name = names[c], Text = text, Number = number, Unparsed = unparsed };
        }

        float[] ticks;
        if (columns.TryGetValue("tick", out Column? tickColumn))
            ticks = tickColumn.Number;
        else
        {
            ticks = new float[cells.Count];
            for (int i = 0; i < cells.Count; i++)
                ticks[i] = i;
        }

        var session = new Session
        {
            Path = path,
            Count = cells.Count,
            Names = names,
            Ragged = ragged,
            Ticks = ticks,
            Metadata = metadata,
        };
        foreach (var pair in columns)
            session.byName[pair.Key] = pair.Value;
        return session;
    }

    /// <summary>
    /// The metadata preamble alone, read without loading the rows, so a multi-run comparison of build
    /// identity costs a few lines per run rather than a second parse of every session.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReadMetadata(string path)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        bool rows = false;
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length > 0 && !line.TrimStart('﻿').StartsWith('#'))
            {
                rows = true;
                break;
            }
            AddMetadata(metadata, line);
        }
        if (!rows)
            return metadata;
        // The closing trailer follows the rows, so only the file's tail is read for it: the trailer is a line or two,
        // and reading every row of a long capture to find it would undo what reading the preamble alone saves.
        const int TailBytes = 4096;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(Math.Max(0, stream.Length - TailBytes), SeekOrigin.Begin);
        string[] tail = new StreamReader(stream).ReadToEnd().Split('\n');
        // The first tail line may start mid-row, so it is never read as a trailer line.
        var trailer = new List<string>();
        for (int i = tail.Length - 1; i >= (stream.Length > TailBytes ? 1 : 0); i--)
        {
            string line = tail[i].TrimEnd('\r');
            if (line.Length == 0) continue;
            if (!line.StartsWith('#')) break;
            trailer.Add(line);
        }
        for (int i = trailer.Count - 1; i >= 0; i--)
            AddMetadata(metadata, trailer[i]);
        return metadata;
    }

    /// <summary>
    /// One <c># key=value</c> line, split at its first equals sign. A value can itself hold
    /// <c>;key=value</c> pairs — the writer packs terraria, tml_assembly, runtime and os into one
    /// line — and those stay inside the first key's value here; <see cref="MultiRunReport"/> expands
    /// them where it compares provenance.
    /// </summary>
    private static void AddMetadata(Dictionary<string, string> metadata, string line)
    {
        line = line.TrimStart('﻿');
        if (!line.StartsWith("# ", StringComparison.Ordinal))
            return;
        int equals = line.IndexOf('=', 2);
        if (equals <= 2)
            return;
        metadata[line[2..equals]] = line[(equals + 1)..];
    }

    public int Tick(int row) => row >= 0 && row < Ticks.Length && !float.IsNaN(Ticks[row]) ? (int)Ticks[row] : row;

    /// <summary>
    /// The number a cell carries, whatever else it carries beside it. The writer packs a unit or a
    /// state letter into several columns — life is <c>86/100</c>, breath is <c>0.55u</c> underwater,
    /// self danger is <c>0.30L</c> in lava, horizon is the word <c>inf</c> with no threat — so a
    /// plain float parse throws on a third of the file. The leading number is what a check wants in
    /// every one of those cases, and the suffix is read from the text when it matters.
    /// </summary>
    public static float ParseNumber(string cell)
    {
        if (cell.Length == 0 || cell == "-")
            return float.NaN;
        if (cell == "inf")
            return float.PositiveInfinity;

        int slash = cell.IndexOf('/');
        if (slash > 0)
            cell = cell[..slash];

        int end = 0;
        while (end < cell.Length && (char.IsAsciiDigit(cell[end]) || cell[end] == '.' || (end == 0 && (cell[end] == '-' || cell[end] == '+'))))
            end++;
        if (end == 0)
            return float.NaN;
        return float.TryParse(cell.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : float.NaN;
    }

    /// <summary>A tile or pixel pair written as <c>x,y</c>, or null for the dash the writer uses for "none".</summary>
    public static bool TryPair(string cell, out float x, out float y)
    {
        x = y = 0f;
        int comma = cell.IndexOf(',');
        if (comma <= 0)
            return false;
        x = ParseNumber(cell[..comma]);
        y = ParseNumber(cell[(comma + 1)..]);
        return !float.IsNaN(x) && !float.IsNaN(y);
    }

    /// <summary>The distance in tiles between two tile-pair columns on one row, or NaN where either is absent.</summary>
    public float TileDistance(string a, string b, int row)
    {
        Column? first = Find(a), second = Find(b);
        if (first == null || second == null)
            return float.NaN;
        if (!TryPair(first.Text[row], out float ax, out float ay) || !TryPair(second.Text[row], out float bx, out float by))
            return float.NaN;
        return MathF.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    }
}
