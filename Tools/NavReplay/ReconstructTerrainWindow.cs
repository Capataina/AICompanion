#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

/// <summary>
/// One <c>terrain-snapshot</c> occurrence as the reconstruction needs it: when it was written on the
/// recorder's own stopwatch, the world pixel of its top-left tile, and its <c>key=value;…</c> detail.
/// Plain values rather than a parsed event, because the two readers hold different event types —
/// NavReplay scans lines, SessionReport already holds a validated log — and the reconstruction must
/// be one piece of code whichever of them asked.
///
/// <para><c>Unreadable</c> marks a line that named the kind and did not parse: it is handed on rather than dropped, so the
/// window counts it under <see cref="TerrainWindow.Malformed"/> and a caller that refuses malformed terrain refuses it too.</para>
/// </summary>
internal readonly record struct TerrainSnapshotRecord(double ElapsedMs, float PosX, float PosY, string Detail, bool Unreadable = false);

/// <summary>
/// A rectangle of tiles rebuilt from a capture's snapshots, with what is known kept apart from what is
/// drawn. <see cref="Glyphs"/> holds the recorder's own alphabet (<c>TextTileWorld.Glyph</c>: # solid,
/// = platform, _ { half blocks, \ / ( ) floor slopes, &lt; &gt; ceiling slopes, ~ water, L lava, . air)
/// wherever a snapshot reached, and the caller's unknown glyph everywhere else; <see cref="Known"/>
/// says which is which, because the two consumers need opposite things from an unknown tile — the
/// scenario extractor closes it so no search routes through terrain nobody saw, and a picture paints it
/// its own colour so nobody reads it as rock.
/// </summary>
internal sealed class TerrainWindow
{
    public required int OriginX { get; init; }
    public required int OriginY { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    /// <summary>Row-major <c>[y, x]</c>, the recorder's glyph or the caller's unknown glyph.</summary>
    public required char[,] Glyphs { get; init; }
    public required bool[,] Known { get; init; }
    /// <summary>Liquid amount (0–255) and type (0 water, 1 lava, 2 honey, 3 shimmer) per tile, where the snapshot carried them.</summary>
    public required byte[,] LiquidAmount { get; init; }
    public required byte[,] LiquidType { get; init; }
    /// <summary>Snapshots at or before the cutoff that contributed at least one tile to the window.</summary>
    public int Snapshots { get; init; }
    /// <summary>Snapshots that could not be read — a line that did not parse, a missing tile payload or size, dimensions
    /// disagreeing with the glyphs. An unparseable line counts whatever its time, because its time is what could not be read.</summary>
    public int Malformed { get; init; }
    /// <summary>The recorder's stopwatch reading of the oldest and newest contributing snapshot; NaN with none.</summary>
    public double OldestMs { get; init; } = double.NaN;
    public double NewestMs { get; init; } = double.NaN;

    public int KnownTiles
    {
        get
        {
            int count = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (Known[y, x]) count++;
            return count;
        }
    }
}

/// <summary>
/// The terrain around a recorded moment, rebuilt from the snapshots the recorder wrote up to it. This
/// is the one implementation: <c>ExtractScenarioFromCapture</c> cuts a corpus fixture with it and the
/// session reader draws a tick's picture with it, so a rule about which snapshot counts cannot be
/// right in one and wrong in the other.
///
/// <para><b>Snapshots join on the recorder's stopwatch, never on the tick.</b> The rows and the
/// occurrences are written by different producers and only the elapsed millisecond is guaranteed to
/// mean the same thing in both, which is the join the native water replay makes for the same reason.
/// Snapshots are applied in the order given, so a later snapshot of a chunk overwrites an earlier one,
/// and the recorder writes a chunk only when it changed — so a known tile carries the shape it had
/// when a snapshot last reached it, which is why the window reports how old its oldest contribution
/// is rather than claiming to be current.</para>
///
/// <para><b>'?' is the recorder's mark for a tile outside the loaded world</b> and it stays unknown,
/// rather than becoming the air its glyph is not.</para>
/// </summary>
internal static class ReconstructTerrainWindow
{
    /// <summary>The occurrence kind the recorder writes a chunk under (<c>RecordGodsEyeEvents.RecordTerrainSnapshot</c>).</summary>
    public const string Kind = "terrain-snapshot";

    public static TerrainWindow From(IEnumerable<TerrainSnapshotRecord> snapshots, int originX, int originY, int width, int height,
        double cutoffElapsedMs, char unknownGlyph)
    {
        var glyphs = new char[height, width];
        var known = new bool[height, width];
        var amount = new byte[height, width];
        var type = new byte[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                glyphs[y, x] = unknownGlyph;

        int contributed = 0, malformed = 0;
        double oldest = double.MaxValue, newest = double.MinValue;
        foreach (TerrainSnapshotRecord snapshot in snapshots)
        {
            // An unreadable line has no time to compare against the cutoff, so it counts wherever it lay: whether it
            // described a tile of this window cannot be known, which is exactly what a malformed count exists to say.
            if (snapshot.Unreadable) { malformed++; continue; }
            if (snapshot.ElapsedMs > cutoffElapsedMs) continue;
            int cx = (int)(snapshot.PosX / 16) - originX;
            int cy = (int)(snapshot.PosY / 16) - originY;
            string? tiles = Field(snapshot.Detail, "tiles");
            if (tiles == null) { malformed++; continue; }
            if (!int.TryParse(Field(snapshot.Detail, "width"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sw)
                || !int.TryParse(Field(snapshot.Detail, "height"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sh)
                || sw <= 0 || sh <= 0 || tiles.Length != sw * sh)
            { malformed++; continue; }
            if (cx >= width || cy >= height || cx + sw <= 0 || cy + sh <= 0) continue;
            contributed++;
            oldest = Math.Min(oldest, snapshot.ElapsedMs);
            newest = Math.Max(newest, snapshot.ElapsedMs);
            byte[]? liquids = Liquids(Field(snapshot.Detail, "liquid-amount-type"), tiles.Length);
            for (int i = 0; i < tiles.Length; i++)
            {
                int x = cx + i % sw, y = cy + i / sw;
                if (x < 0 || y < 0 || x >= width || y >= height || tiles[i] == '?') continue;
                glyphs[y, x] = tiles[i];
                known[y, x] = true;
                amount[y, x] = liquids?[i * 2] ?? 0;
                type[y, x] = liquids?[i * 2 + 1] ?? 0;
            }
        }
        return new TerrainWindow
        {
            OriginX = originX, OriginY = originY, Width = width, Height = height,
            Glyphs = glyphs, Known = known, LiquidAmount = amount, LiquidType = type,
            Snapshots = contributed, Malformed = malformed,
            OldestMs = contributed == 0 ? double.NaN : oldest,
            NewestMs = contributed == 0 ? double.NaN : newest,
        };
    }

    /// <summary>
    /// Every snapshot in a sidecar, in file order, read line by line. The cheap string test comes before
    /// the parse because the sidecar runs to tens of megabytes and parsing every line as JSON to keep a
    /// few hundred is most of the wall clock. A line that names the kind and does not parse is handed on
    /// marked <c>Unreadable</c> rather than skipped, so it reaches <see cref="TerrainWindow.Malformed"/>
    /// and the scenario extractor's refusal: the extractor this was lifted from threw on such a line, and
    /// a silent skip would cut a fixture from a window with a hole nobody was told about.
    /// </summary>
    public static IEnumerable<TerrainSnapshotRecord> ReadSnapshots(string eventsPath)
    {
        foreach (string line in File.ReadLines(eventsPath))
        {
            if (!line.Contains(Kind, StringComparison.Ordinal)) continue;
            TerrainSnapshotRecord? record;
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement e = document.RootElement;
                record = e.GetProperty("kind").GetString() == Kind
                    ? new TerrainSnapshotRecord(e.GetProperty("wall_elapsed_ms").GetDouble(),
                        e.GetProperty("pos_x").GetSingle(), e.GetProperty("pos_y").GetSingle(), e.GetProperty("detail").GetString() ?? "")
                    : null;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                record = new TerrainSnapshotRecord(double.NaN, 0f, 0f, "", Unreadable: true);
            }
            if (record is { } found) yield return found;
        }
    }

    /// <summary>The first <c>key=value</c> in a <c>;</c>-separated detail, or null.</summary>
    private static string? Field(string detail, string key)
    {
        int start = 0;
        while (start < detail.Length)
        {
            int end = detail.IndexOf(';', start);
            if (end < 0) end = detail.Length;
            if (end - start > key.Length && string.CompareOrdinal(detail, start, key, 0, key.Length) == 0 && detail[start + key.Length] == '=')
                return detail.Substring(start + key.Length + 1, end - start - key.Length - 1);
            start = end + 1;
        }
        return null;
    }

    /// <summary>The two-bytes-a-tile liquid payload, or null where it is absent or the wrong length.</summary>
    private static byte[]? Liquids(string? base64, int tiles)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        try
        {
            byte[] bytes = Convert.FromBase64String(base64);
            return bytes.Length == tiles * 2 ? bytes : null;
        }
        catch (FormatException) { return null; }
    }
}
