#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// A playtest failure turned into a corpus fixture: the terrain around the companion at one
/// recorded tick, cut out of the capture's own terrain snapshots and written in the committed
/// scenario format, with the recorded body, the destination it was asked for and the player's trail.
///
/// This is the route by which a defect the player can feel becomes a block the tool replays. Before
/// it, a scenario existed only where one of the recorder's nine detectors happened to fire, so a
/// failure nobody had thought to threshold produced no fixture at all; with it, any tick a reader
/// can name becomes a case.
///
/// **An uncaptured tile is written solid and counted, never left as air.** The glyph alphabet reads
/// anything outside it as air, so an absent snapshot would silently become open sky the planner is
/// happy to route through — a window that is missing terrain would then read as a window with an
/// easy route. The native water replay states the same rule for the same reason: unknown is closed,
/// and reaching it is reduced coverage rather than proof of a route. The coverage figure in the
/// header is how a reader prices the fixture before trusting its verdict — and it is coverage as
/// *last written*, because the recorder writes a chunk only when it changed, so a tile counted as
/// known carries the shape it had when a snapshot last reached it. The header therefore also names
/// how far behind the tick the oldest contributing snapshot was: a window built from chunks minutes
/// old is one whose terrain may have been mined since, and full coverage does not say otherwise.
/// </summary>
internal static class ExtractScenarioFromCapture
{
    internal readonly record struct Extract(string Path, int Tick, int Width, int Height, int Known, int Snapshots, int TrailLength, double OldestAgeSeconds, Point Start, Point Goal, Point Player)
    {
        public double Coverage => Width * Height == 0 ? 0 : 100.0 * Known / (Width * Height);
    }

    /// <summary>How many of the player's recorded feet tiles the trail carries, oldest first.</summary>
    private const int TrailTiles = 200;

    internal static Extract Run(string capture, int tick, int width, int height, string? into, Action<string> say)
    {
        string[] header = Array.Empty<string>();
        Dictionary<string, string>? row = null;
        // The player's own feet as the capture recorded them, one entry per change of tile. It is
        // built on the way past rather than by a second pass, because the file runs to tens of
        // megabytes and the rows before the tick are exactly the rows already being read.
        var trail = new List<Point>();
        foreach (string line in File.ReadLines(capture))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] cells = line.TrimStart('﻿').Split('\t');
            if (header.Length == 0) { header = cells; continue; }
            if (cells.Length != header.Length) continue;
            int at = Array.IndexOf(header, "tick");
            if (at < 0 || !int.TryParse(cells[at], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rowTick)) continue;
            if (PixelTile(Cell(header, cells, "player_px")) is Point feet && (trail.Count == 0 || trail[^1] != feet))
            {
                trail.Add(feet);
                if (trail.Count > TrailTiles) trail.RemoveAt(0);
            }
            if (rowTick == tick) { row = header.Zip(cells).ToDictionary(p => p.First, p => p.Second); break; }
        }
        if (row == null)
            throw new InvalidDataException($"no complete sample at tick {tick} in {Path.GetFileName(capture)}");

        float left = Single(row, "observed_left"), bottom = Single(row, "observed_bottom");
        float boxWidth = row.TryGetValue("npc_width", out string? w) && float.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out float bw) ? bw : 0f;
        float boxHeight = row.TryGetValue("npc_height", out string? h) && float.TryParse(h, NumberStyles.Float, CultureInfo.InvariantCulture, out float bh) ? bh : 0f;
        Point start = new((int)MathF.Floor((left + boxWidth / 2f) / 16f), (int)MathF.Floor((bottom - boxHeight / 2f) / 16f));
        Point player = PixelTile(row.GetValueOrDefault("player_px", "")) ?? start;
        // The destination the brain had actually asked for on that tick, which is what makes the
        // fixture a replay of the ask rather than of a goal invented afterwards. Without one the
        // player's own feet stand in, because "could it have reached me" is the design's pass line.
        Point goal = TilePair(row.GetValueOrDefault("spot", "")) ?? player;

        int originX = start.X - width / 2, originY = start.Y - height / 2;
        // The window is widened rather than moved when an actor falls outside it: a fixture whose
        // goal is not in its own window is skipped by the replay, which is a fixture that cost a
        // command and answers nothing.
        foreach (Point p in new[] { goal, player })
        {
            if (p.X < originX) originX = p.X - 2;
            if (p.Y < originY) originY = p.Y - 2;
            if (p.X >= originX + width) width = p.X - originX + 3;
            if (p.Y >= originY + height) height = p.Y - originY + 3;
        }

        var glyphs = new char[height, width];
        var known = new bool[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                glyphs[y, x] = '#';

        double elapsed = double.Parse(row["wall_elapsed_ms"], CultureInfo.InvariantCulture);
        string events = Path.ChangeExtension(capture, null) + "-events.jsonl";
        if (!File.Exists(events))
            throw new FileNotFoundException($"the capture's terrain lives in {Path.GetFileName(events)}, which is not beside it");
        int snapshots = 0;
        // The age of the oldest snapshot that contributed a tile, because coverage says every tile
        // is *known* and not that every tile is *current*: the recorder writes a chunk only when it
        // changed since it last wrote it, so a chunk mined while nobody was near it keeps the shape
        // it had when it was last seen. A window whose oldest contributing chunk is minutes behind
        // the tick is a window that may be describing terrain that no longer existed.
        double oldestMs = double.MaxValue, newestMs = double.MinValue;
        foreach (string line in File.ReadLines(events))
        {
            // The cheap string test before the parse: the events file runs to tens of megabytes and
            // parsing every line as JSON to discard all but four hundred of them is most of the
            // command's wall clock.
            if (!line.Contains("terrain-snapshot", StringComparison.Ordinal)) continue;
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement e = document.RootElement;
            if (e.GetProperty("kind").GetString() != "terrain-snapshot") continue;
            // Joined on the recorder's own stopwatch rather than on the tick, the way the native
            // water replay joins it: the two streams are written by different producers and only the
            // elapsed millisecond is guaranteed to mean the same thing in both.
            double snapshotMs = e.GetProperty("wall_elapsed_ms").GetDouble();
            if (snapshotMs > elapsed) continue;
            int cx = (int)(e.GetProperty("pos_x").GetSingle() / 16) - originX;
            int cy = (int)(e.GetProperty("pos_y").GetSingle() / 16) - originY;
            var fields = e.GetProperty("detail").GetString()!.Split(';').Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
            if (!fields.TryGetValue("tiles", out string? tiles)) continue;
            int sw = int.Parse(fields["width"], CultureInfo.InvariantCulture);
            int sh = int.Parse(fields["height"], CultureInfo.InvariantCulture);
            if (tiles.Length != sw * sh) throw new InvalidDataException("a terrain snapshot's dimensions disagree with its payload");
            if (cx >= width || cy >= height || cx + sw <= 0 || cy + sh <= 0) continue;
            snapshots++;
            oldestMs = Math.Min(oldestMs, snapshotMs);
            newestMs = Math.Max(newestMs, snapshotMs);
            for (int i = 0; i < tiles.Length; i++)
            {
                int x = cx + i % sw, y = cy + i / sw;
                // '?' is the recorder's own mark for a tile outside the loaded world. It stays
                // unknown, which stays solid, rather than becoming the air its glyph is not.
                if (x < 0 || y < 0 || x >= width || y >= height || tiles[i] == '?') continue;
                glyphs[y, x] = tiles[i];
                known[y, x] = true;
            }
        }
        if (snapshots == 0)
            throw new InvalidDataException($"no terrain snapshot written at or before tick {tick} covers the window around {start.X},{start.Y}");

        int knownTiles = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (known[y, x]) knownTiles++;

        var trailInWindow = trail.Where(t => t.X >= originX && t.Y >= originY && t.X < originX + width && t.Y < originY + height).ToList();
        string name = into ?? Path.Combine("Tools", "Scenarios",
            $"extracted-{Path.GetFileNameWithoutExtension(capture)}-tick-{tick}.txt");

        var lines = new List<string>
        {
            // The provenance sits after the recorded keys and carries none of their words, because
            // every key is found by its first occurrence in the line: a coverage note written ahead
            // of the real goal would become the goal.
            $"tick {tick} cut from a recording: start {start.X},{start.Y} goal {goal.X},{goal.Y} expansions 0"
                + $" npc {start.X},{start.Y} player {player.X},{player.Y}"
                + $" npcbox {left.ToString("0.0", CultureInfo.InvariantCulture)},{bottom.ToString("0.0", CultureInfo.InvariantCulture)},{boxWidth:0},{boxHeight:0}"
                + $" window x {originX}..{originX + width - 1} y {originY}..{originY + height - 1}"
                + $" || from {Path.GetFileName(capture)} || {snapshots} snapshots covered {knownTiles} of {width * height} tiles"
                + $" ({100.0 * knownTiles / (width * height):F1}%) as last written, the rest closed"
                + $", oldest contributing snapshot {(elapsed - oldestMs) / 1000.0:F1}s before the tick and newest {(elapsed - newestMs) / 1000.0:F1}s",
            // Every position named outright rather than drawn over the grid, because a marker glyph
            // written onto a half block or a slope erases the support the body is standing on.
            $"markers S {start.X},{start.Y} G {goal.X},{goal.Y} N {start.X},{start.Y} P {player.X},{player.Y}",
        };
        if (trailInWindow.Count > 0)
            lines.Add("trail " + string.Join(' ', trailInWindow.Select(t => $"{t.X},{t.Y}")));
        for (int y = 0; y < height; y++)
        {
            var text = new StringBuilder(width);
            for (int x = 0; x < width; x++)
                text.Append(glyphs[y, x]);
            lines.Add(text.ToString());
        }
        Directory.CreateDirectory(Path.GetDirectoryName(name)!);
        File.WriteAllLines(name, lines);
        say($"extract: {name}");
        return new Extract(name, tick, width, height, knownTiles, snapshots, trailInWindow.Count, (elapsed - oldestMs) / 1000.0, start, goal, player);
    }

    private static string Cell(string[] header, string[] cells, string key)
    {
        int at = Array.IndexOf(header, key);
        return at >= 0 && at < cells.Length ? cells[at] : "";
    }

    private static float Single(Dictionary<string, string> row, string key)
        => float.Parse(row[key], NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>
    /// A pixel pair as the tile its feet stand in. Which columns are pixels and which are tiles is
    /// read off the column, never inferred from the text: <c>npc_px</c> writes <c>54426,9177</c>
    /// with no decimal point and <c>spot</c> writes <c>3407,570</c>, so a parser that decided by
    /// punctuation would read a body's pixel position as a tile fifty thousand columns away and the
    /// window would be cut somewhere nobody has ever been.
    /// </summary>
    private static Point? PixelTile(string value)
    {
        string[] xy = value.Split(',');
        return xy.Length == 2
            && float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float px)
            && float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float py)
                ? new Point((int)MathF.Floor(px / 16f), (int)MathF.Floor((py - 0.01f) / 16f))
                : null;
    }

    /// <summary>A tile pair as written; null for an absent cell or the dash a column writes when it has no answer.</summary>
    private static Point? TilePair(string value)
    {
        string[] xy = value.Split(',');
        return xy.Length == 2
            && int.TryParse(xy[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tx)
            && int.TryParse(xy[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ty)
                ? new Point(tx, ty)
                : null;
    }
}
