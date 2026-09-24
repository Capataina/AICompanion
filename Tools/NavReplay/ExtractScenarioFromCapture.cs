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

    /// <param name="reason">Why this window was worth cutting, written into the fixture's own header.
    /// A committed scenario outlives the session that cut it and the only thing anybody can read it
    /// with is the file, so a cut whose reason lives in a folder guide instead is a fact with two homes
    /// of which the guide is the one that drifts. Omitted where the caller genuinely has no reason —
    /// a hand-typed `--extract-scenario` at a tick somebody was curious about — and the header then
    /// says nothing rather than inventing one.</param>
    internal static Extract Run(string capture, int tick, int width, int height, string? into, Action<string> say,
        string? reason = null)
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

        // The body is its centre: `npc_px` is the orb's centre in whole pixels, sampled inside the AI
        // phase after the motor's contact, so it is where the game's own circle test last left it.
        string centrePx = row.GetValueOrDefault("npc_px", "");
        Point start = PixelTile(centrePx) ?? throw new InvalidDataException($"tick {tick} has no npc_px centre to start the body from");
        float[] centre = centrePx.Split(',').Select(s => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
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

        double elapsed = double.Parse(row["wall_elapsed_ms"], CultureInfo.InvariantCulture);
        string events = Path.ChangeExtension(capture, null) + "-events.jsonl";
        if (!File.Exists(events))
            throw new FileNotFoundException($"the capture's terrain lives in {Path.GetFileName(events)}, which is not beside it");
        // The reconstruction is shared with the session reader's tick picture (`ReconstructTerrainWindow`);
        // what is this extractor's own is closing every unknown tile as solid, so no search routes
        // through terrain nobody saw.
        TerrainWindow window = ReconstructTerrainWindow.From(ReconstructTerrainWindow.ReadSnapshots(events),
            originX, originY, width, height, elapsed, unknownGlyph: '#');
        if (window.Malformed > 0)
            throw new InvalidDataException($"{window.Malformed} terrain snapshot(s) in {Path.GetFileName(events)} could not be read — a line that does not parse, "
                + $"a missing tile payload, or dimensions that disagree with it — so the window around tick {tick} may hold a hole nobody saw");
        int snapshots = window.Snapshots;
        if (snapshots == 0)
            throw new InvalidDataException($"no terrain snapshot written at or before tick {tick} covers the window around {start.X},{start.Y}");
        char[,] glyphs = window.Glyphs;
        int knownTiles = window.KnownTiles;
        // The age of the oldest snapshot that contributed a tile, because coverage says every tile
        // is *known* and not that every tile is *current*: the recorder writes a chunk only when it
        // changed since it last wrote it, so a chunk mined while nobody was near it keeps the shape
        // it had when it was last seen. A window whose oldest contributing chunk is minutes behind
        // the tick is a window that may be describing terrain that no longer existed.
        double oldestMs = window.OldestMs, newestMs = window.NewestMs;

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
                + $" orb {centre[0].ToString("0.0", CultureInfo.InvariantCulture)},{centre[1].ToString("0.0", CultureInfo.InvariantCulture)}"
                + $" window x {originX}..{originX + width - 1} y {originY}..{originY + height - 1}"
                + $" || from {Path.GetFileName(capture)} || {snapshots} snapshots covered {knownTiles} of {width * height} tiles"
                + $" ({100.0 * knownTiles / (width * height):F1}%) as last written, the rest closed"
                + $", oldest contributing snapshot {(elapsed - oldestMs) / 1000.0:F1}s before the tick and newest {(elapsed - newestMs) / 1000.0:F1}s"
                // Last on the line, after every recorded key, for the same reason the provenance is:
                // a reason holding the word "goal" written ahead of the real one would become the goal.
                + (string.IsNullOrWhiteSpace(reason) ? "" : $" || cut for {reason.Replace('\n', ' ').Trim()}"),
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
