#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AICompanion.Tools.SessionReport;

/// <summary>What a drawn picture holds, for the line that names its file.</summary>
public sealed record PictureSummary(string Path, long Tick, int OriginX, int OriginY, int WidthTiles, int HeightTiles, int PixelsPerTile,
    int KnownTiles, string Terrain, int Hostiles, int Drops, int OffPicture)
{
    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"{Path}  tick {Tick:n0}, tiles x {OriginX}..{OriginX + WidthTiles - 1} y {OriginY}..{OriginY + HeightTiles - 1} at {PixelsPerTile} px a tile; "
        + $"terrain {Terrain}; {Hostiles} hostile(s) and {Drops} drop(s) drawn{(OffPicture > 0 ? $", {OffPicture} outside the picture" : "")}");
}

/// <summary>
/// A recorded tick drawn to a PNG with no graphics device: the terrain around the bodies, the companion,
/// the player, the intent region, the steering target and the destination, the hostiles and drops the
/// record placed, a scale bar and a legend. It is for a reader who cannot open the game — the owner away
/// from it, or an agent that may not open a window on his machine — and it draws only what
/// <see cref="ReconstructTheSceneAtATick"/> and <see cref="ReconstructTerrainWindow"/> recovered, so an
/// empty patch means the record did not hold it.
///
/// <para><b>The legend's order is fixed and is also the order the picture paints in</b>, background to
/// foreground: solid, platform or half block, the four liquids, air, unknown, the two trails, the region
/// outline, a straight line from the orb to its steering target and a dashed straight line on to its destination
/// (not the route, whose corners no schema records, so it can cross a wall the route goes round), the destination and the
/// navigator's goal, the activity's own target, drops, hostiles, the player and the orb last so nothing
/// hides them. Unknown terrain is grey with a diagonal hatch and never the colour of rock, because the
/// reconstruction's two consumers want opposite things from an unknown tile and this one must not be read
/// as a wall. A hostile last seen more than sixty ticks before the picture is drawn hollow, with its slot.</para>
///
/// <para><b>The picture's axes are world tiles and y grows downward in both the world and the image</b>,
/// so nothing is flipped; the header names the tile rectangle so any mark can be read back to a coordinate.
/// Positions are placed in floating point from their pixel values, so the orb sits at its sub-tile centre.</para>
/// </summary>
public static class DrawTickPicture
{
    /// <summary>The smallest window drawn, in tiles, so a picture of two bodies side by side still shows their surroundings.</summary>
    private const int MinimumWidthTiles = 72, MinimumHeightTiles = 44;

    /// <summary>The largest window drawn, so a hostile a hundred tiles away does not shrink the bodies to a dot.</summary>
    private const int MaximumWidthTiles = 180, MaximumHeightTiles = 110;

    /// <summary>Tiles of margin around the bodies, the region and the destination.</summary>
    private const int MarginTiles = 8;

    private const int TextScale = 2, LineHeight = 14, Pad = 6;

    /// <summary>Where the map's top-left tile is painted in the image; the self-test reads pixels back through these.</summary>
    internal const int MapLeft = Pad, MapTop = 3 * LineHeight + Pad;

    // Internal so the self-test can read a pixel back and name what it should be.
    internal static readonly Rgb Background = new(24, 24, 28), Ink = new(240, 240, 240), DarkInk = new(20, 20, 20);
    internal static readonly Rgb Solid = new(96, 66, 44), SolidPassThrough = new(150, 110, 70), Platform = new(196, 150, 70);
    internal static readonly Rgb Water = new(40, 100, 225), Lava = new(235, 80, 20), Honey = new(235, 185, 40), Shimmer = new(200, 140, 235);
    internal static readonly Rgb Air = new(208, 226, 242), Unknown = new(150, 150, 150), UnknownHatch = new(118, 118, 118);
    internal static readonly Rgb CompanionFill = new(225, 30, 200), CompanionTrail = new(245, 150, 230);
    internal static readonly Rgb PlayerFill = new(30, 165, 60), PlayerTrail = new(140, 215, 150);
    internal static readonly Rgb RegionLine = new(255, 145, 0), PathLine = new(0, 190, 215), Destination = new(210, 0, 0);
    internal static readonly Rgb Target = new(250, 230, 0), HostileFill = new(200, 0, 0), DropFill = new(245, 200, 0), OtherNpcFill = new(120, 70, 170);

    public static PictureSummary Draw(Session session, long tick, string outPath)
    {
        if (session.Count == 0) throw new InvalidDataException($"{System.IO.Path.GetFileName(session.Path)} holds no rows to draw");
        int row = ReconstructTheSceneAtATick.RowOf(session, tick);
        if (row < 0) throw new InvalidDataException($"tick {tick:n0} is before the capture's first row (tick {session.Tick(0):n0})");
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
        TickScene scene = ReconstructTheSceneAtATick.At(session, log, row);

        // The window: every place the tick's own decision is about, padded, then grown to the minimum around its centre.
        var points = new List<(float X, float Y)>();
        if (scene.Companion is { } c) points.Add(c);
        if (scene.PlayerFeet is { } p) { points.Add(p); points.Add((p.X, p.Y - 42)); }
        if (scene.Region is { } r) { points.Add((r.CX - r.HX, r.CY - r.HY)); points.Add((r.CX + r.HX, r.CY + r.HY)); }
        if (scene.Spot is { } s) points.Add((s.X * 16 + 8, s.Y * 16 + 8));
        if (scene.Lookahead is { } l) points.Add(l);
        if (points.Count == 0) throw new InvalidDataException($"tick {scene.Tick:n0} records neither body's position (npc_px, player_px), so there is nothing to centre a picture on");
        int minX = (int)MathF.Floor(points.Min(q => q.X) / 16) - MarginTiles, maxX = (int)MathF.Floor(points.Max(q => q.X) / 16) + MarginTiles;
        int minY = (int)MathF.Floor(points.Min(q => q.Y) / 16) - MarginTiles, maxY = (int)MathF.Floor(points.Max(q => q.Y) / 16) + MarginTiles;
        Grow(ref minX, ref maxX, MinimumWidthTiles);
        Grow(ref minY, ref maxY, MinimumHeightTiles);
        // Hostiles and drops widen it up to the cap; beyond it they are counted rather than drawn.
        foreach (Sighting a in scene.Hostiles.Concat(scene.Drops))
        {
            int ax = (int)MathF.Floor(a.X / 16), ay = (int)MathF.Floor(a.Y / 16);
            if (ax - 2 < minX && maxX - (ax - 2) + 1 <= MaximumWidthTiles) minX = ax - 2;
            if (ax + 2 > maxX && (ax + 2) - minX + 1 <= MaximumWidthTiles) maxX = ax + 2;
            if (ay - 2 < minY && maxY - (ay - 2) + 1 <= MaximumHeightTiles) minY = ay - 2;
            if (ay + 2 > maxY && (ay + 2) - minY + 1 <= MaximumHeightTiles) maxY = ay + 2;
        }
        if (maxX - minX + 1 > MaximumWidthTiles) { int mid = (minX + maxX) / 2; minX = mid - MaximumWidthTiles / 2; maxX = minX + MaximumWidthTiles - 1; }
        if (maxY - minY + 1 > MaximumHeightTiles) { int mid = (minY + maxY) / 2; minY = mid - MaximumHeightTiles / 2; maxY = minY + MaximumHeightTiles - 1; }
        int widthTiles = maxX - minX + 1, heightTiles = maxY - minY + 1;
        int scale = Math.Clamp(1100 / widthTiles, 4, 10);

        // Terrain: the chunk snapshots joined on the recorder's stopwatch, as the scenario extractor joins them;
        // where none reached, the terrain a combat snapshot carried at or before the tick.
        double cutoff = session.Find("wall_elapsed_ms") is { } wall && !float.IsNaN(wall.Number[row]) ? wall.Number[row] : double.PositiveInfinity;
        var chunks = log.Events.Where(e => e.kind == ReconstructTerrainWindow.Kind && e.tick <= scene.Tick)
            .Select(e => new TerrainSnapshotRecord(e.wall_elapsed_ms, e.pos_x, e.pos_y, e.detail)).ToList();
        TerrainWindow terrain = ReconstructTerrainWindow.From(chunks, minX, minY, widthTiles, heightTiles, cutoff, unknownGlyph: '?');
        var combat = ReconstructTheSceneAtATick.CombatSnapshotTerrain(log).Where(t => t.Tick <= scene.Tick).ToList();
        TerrainWindow? fallback = combat.Count == 0 ? null
            : ReconstructTerrainWindow.From(combat.Select(t => t.Record), minX, minY, widthTiles, heightTiles, double.PositiveInfinity, unknownGlyph: '?');
        int known = 0;
        string terrainNote;

        int mapW = widthTiles * scale, mapH = heightTiles * scale;
        int canvasW = Math.Max(mapW, 760) + 2 * Pad;
        int headerH = MapTop;
        var legend = LegendEntries();
        int legendRows = LayoutLegend(legend, canvasW - 2 * Pad, out _);
        int legendH = legendRows * (LineHeight + 4) + LineHeight + 3 * Pad;
        var raster = new Raster(canvasW, headerH + mapH + legendH, Background);
        int mapX = MapLeft, mapY = MapTop;

        for (int ty = 0; ty < heightTiles; ty++)
            for (int tx = 0; tx < widthTiles; tx++)
            {
                char glyph = '?';
                byte amount = 0, type = 0;
                if (terrain.Known[ty, tx]) { glyph = terrain.Glyphs[ty, tx]; amount = terrain.LiquidAmount[ty, tx]; type = terrain.LiquidType[ty, tx]; }
                else if (fallback != null && fallback.Known[ty, tx]) glyph = fallback.Glyphs[ty, tx];
                if (glyph != '?') known++;
                PaintTile(raster, mapX + tx * scale, mapY + ty * scale, scale, glyph, amount, type);
            }
        int total = widthTiles * heightTiles;
        string share = string.Create(CultureInfo.InvariantCulture, $"{100.0 * known / total:0}% known");
        if (terrain.Snapshots > 0)
            terrainNote = string.Create(CultureInfo.InvariantCulture,
                $"{share} from {terrain.Snapshots} snapshot(s), oldest {(cutoff - terrain.OldestMs) / 1000.0:0.0}s before the tick{(fallback != null ? " and a combat snapshot" : "")}");
        else if (fallback != null && fallback.Snapshots > 0)
            terrainNote = string.Create(CultureInfo.InvariantCulture,
                $"{share} from the combat snapshot at tick {combat.Last().Tick:n0} ({scene.Tick - combat.Last().Tick:n0} ticks old); no terrain-snapshot occurrence reached this window");
        else
            terrainNote = log.Events.Any(e => e.kind == ReconstructTerrainWindow.Kind)
                ? "none: no snapshot at or before this tick covers the window"
                : "none: the sidecar holds no terrain-snapshot occurrence at all";
        if (terrain.Malformed > 0) terrainNote += $"; {terrain.Malformed} malformed snapshot(s) ignored";

        (double X, double Y) ToImage(float px, float py) => (mapX + (px / 16.0 - minX) * scale, mapY + (py / 16.0 - minY) * scale);
        bool Inside(float px, float py) => px / 16 >= minX && px / 16 < maxX + 1 && py / 16 >= minY && py / 16 < maxY + 1;

        foreach (var (x, y) in scene.CompanionTrail) { var at = ToImage(x, y); raster.FillRect((int)at.X - 1, (int)at.Y - 1, 2, 2, CompanionTrail); }
        foreach (var (x, y) in scene.PlayerTrail) { var at = ToImage(x, y - 21); raster.FillRect((int)at.X - 1, (int)at.Y - 1, 2, 2, PlayerTrail); }

        if (scene.Region is { } region)
        {
            var a = ToImage(region.CX - region.HX, region.CY - region.HY);
            var b = ToImage(region.CX + region.HX, region.CY + region.HY);
            raster.OutlineRect((int)a.X, (int)a.Y, (int)(b.X - a.X), (int)(b.Y - a.Y), RegionLine, 2);
        }

        if (scene.Companion is { } orb)
        {
            var from = ToImage(orb.X, orb.Y);
            if (scene.Lookahead is { } look)
            {
                var la = ToImage(look.X, look.Y);
                raster.Line(from.X, from.Y, la.X, la.Y, PathLine, 2);
                if (scene.Spot is { } s2) { var sp = ToImage(s2.X * 16 + 8, s2.Y * 16 + 8); raster.Line(la.X, la.Y, sp.X, sp.Y, PathLine, 1, dash: 4); }
                raster.FillRect((int)la.X - 3, (int)la.Y - 3, 7, 7, PathLine);
            }
            else if (scene.Spot is { } s3)
            {
                var sp = ToImage(s3.X * 16 + 8, s3.Y * 16 + 8);
                raster.Line(from.X, from.Y, sp.X, sp.Y, PathLine, 1, dash: 4);
            }
        }
        if (scene.Spot is { } spot)
        {
            var at = ToImage(spot.X * 16 + 8, spot.Y * 16 + 8);
            raster.Line(at.X - 5, at.Y - 5, at.X + 5, at.Y + 5, Destination, 2);
            raster.Line(at.X - 5, at.Y + 5, at.X + 5, at.Y - 5, Destination, 2);
        }
        if (scene.NavigatorGoal is { } goal)
        {
            var at = ToImage(goal.X * 16 + 8, goal.Y * 16 + 8);
            raster.OutlineRect((int)at.X - 6, (int)at.Y - 6, 13, 13, Destination, 1);
        }
        if (scene.ActivityTarget is { } target && Inside(target.X, target.Y))
        {
            var at = ToImage(target.X, target.Y);
            raster.Ring(at.X, at.Y, 9, Target, 2);
        }

        int off = 0;
        foreach (Sighting d in scene.Drops)
        {
            if (!Inside(d.X, d.Y)) { off++; continue; }
            var at = ToImage(d.X, d.Y);
            int half = Math.Max(3, scale / 2);
            raster.FillRect((int)at.X - half - 1, (int)at.Y - half - 1, 2 * half + 3, 2 * half + 3, DarkInk);
            raster.FillRect((int)at.X - half, (int)at.Y - half, 2 * half + 1, 2 * half + 1, DropFill);
        }
        foreach (Sighting n in scene.OtherNpcs)
        {
            if (!Inside(n.X, n.Y)) { off++; continue; }
            var at = ToImage(n.X, n.Y);
            int half = Math.Max(3, scale / 2);
            raster.FillRect((int)at.X - half - 1, (int)at.Y - half - 1, 2 * half + 3, 2 * half + 3, DarkInk);
            raster.FillRect((int)at.X - half, (int)at.Y - half, 2 * half + 1, 2 * half + 1, OtherNpcFill);
        }
        foreach (Sighting h in scene.Hostiles)
        {
            if (!Inside(h.X, h.Y)) { off++; continue; }
            var at = ToImage(h.X, h.Y);
            int half = Math.Max(5, scale);
            raster.FillRect((int)at.X - half - 1, (int)at.Y - half - 1, 2 * half + 3, 2 * half + 3, DarkInk);
            if (h.Age(scene.Tick) > 60)
            {
                raster.FillRect((int)at.X - half, (int)at.Y - half, 2 * half + 1, 2 * half + 1, Air);
                raster.OutlineRect((int)at.X - half, (int)at.Y - half, 2 * half + 1, 2 * half + 1, HostileFill, 2);
            }
            else raster.FillRect((int)at.X - half, (int)at.Y - half, 2 * half + 1, 2 * half + 1, HostileFill);
            string label = h.Slot.ToString(CultureInfo.InvariantCulture);
            raster.FillRect((int)at.X + half + 2, (int)at.Y - 6, Raster.TextWidth(label) + 2, 12, DarkInk);
            raster.Text((int)at.X + half + 3, (int)at.Y - 5, label, Ink);
        }

        if (scene.PlayerFeet is { } feet)
        {
            // The player's hitbox is 20 by 42 pixels standing on his feet.
            var a = ToImage(feet.X - 10, feet.Y - 42);
            var b = ToImage(feet.X + 10, feet.Y);
            int w = Math.Max(3, (int)Math.Round(b.X - a.X)), hgt = Math.Max(5, (int)Math.Round(b.Y - a.Y));
            raster.FillRect((int)a.X - 1, (int)a.Y - 1, w + 2, hgt + 2, DarkInk);
            raster.FillRect((int)a.X, (int)a.Y, w, hgt, PlayerFill);
        }
        if (scene.Companion is { } body)
        {
            var at = ToImage(body.X, body.Y);
            // The orb is twenty pixels across.
            raster.Disc(at.X, at.Y, Math.Max(4.5, 10.0 * scale / 16), CompanionFill, DarkInk);
        }

        // Header: what tick, what the companion was doing, and the tile rectangle so any mark reads back to a coordinate.
        string schema = session.Metadata.TryGetValue("schema", out string? v) ? v : "unrecorded";
        string Cell(string column) => session.Find(column) is { } col ? col.Text[row] : "absent";
        raster.Text(Pad, Pad, Fit(string.Create(CultureInfo.InvariantCulture,
            $"tick {scene.Tick} of {System.IO.Path.GetFileNameWithoutExtension(session.Path)}  schema {schema}{(session.Metadata.ContainsKey("synthetic") ? "  synthetic" : "")}"), canvasW), Ink);
        raster.Text(Pad, Pad + LineHeight, Fit($"action {Cell("action")}  request {Cell("request")}  nav {Cell("nav_status")}  hostiles {scene.Hostiles.Count} other npcs {scene.OtherNpcs.Count} drops {scene.Drops.Count}{(off > 0 ? $" ({off} off picture)" : "")}", canvasW), Ink);
        raster.Text(Pad, Pad + 2 * LineHeight, Fit(string.Create(CultureInfo.InvariantCulture,
            $"tiles x {minX}..{maxX} y {minY}..{maxY}  terrain {terrainNote}"), canvasW), Ink);

        // Legend and scale bar under the map.
        int legendY = mapY + mapH + Pad;
        LayoutLegend(legend, canvasW - 2 * Pad, out var placed);
        foreach (var (entry, lx, lrow) in placed)
        {
            int y = legendY + lrow * (LineHeight + 4);
            entry.Swatch(raster, Pad + lx, y);
            raster.Text(Pad + lx + 16, y + 1, entry.Label, Ink);
        }
        int barY = legendY + legendRows * (LineHeight + 4) + 4;
        raster.FillRect(Pad, barY, 10 * scale, 4, Ink);
        raster.FillRect(Pad, barY - 3, 2, 10, Ink);
        raster.FillRect(Pad + 10 * scale - 2, barY - 3, 2, 10, Ink);
        raster.Text(Pad + 10 * scale + 8, barY - 3, string.Create(CultureInfo.InvariantCulture, $"10 tiles = 160 px  ({scale} image px a tile)"), Ink);

        string? directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outPath));
        if (directory != null) Directory.CreateDirectory(directory);
        File.WriteAllBytes(outPath, EncodePng.Encode(raster.Width, raster.Height, raster.Pixels));
        return new PictureSummary(outPath, scene.Tick, minX, minY, widthTiles, heightTiles, scale, known, terrainNote,
            scene.Hostiles.Count - scene.Hostiles.Count(h => !Inside(h.X, h.Y)), scene.Drops.Count - scene.Drops.Count(d => !Inside(d.X, d.Y)), off);
    }

    /// <summary>One tile, painted pixel by pixel so half blocks, slopes and platforms keep their shape.</summary>
    private static void PaintTile(Raster raster, int x0, int y0, int scale, char glyph, byte amount, byte type)
    {
        Rgb liquid = type switch { 1 => Lava, 2 => Honey, 3 => Shimmer, _ => Water };
        for (int py = 0; py < scale; py++)
            for (int px = 0; px < scale; px++)
            {
                double u = (px + 0.5) / scale, v = (py + 0.5) / scale;
                Rgb c = glyph switch
                {
                    '#' => Solid,
                    '=' => v < 0.35 ? Platform : Air,
                    '_' => v >= 0.5 ? Solid : Air,
                    '{' => v >= 0.5 ? SolidPassThrough : Air,
                    '\\' => v >= u ? Solid : Air,
                    '(' => v >= u ? SolidPassThrough : Air,
                    '/' => v >= 1 - u ? Solid : Air,
                    ')' => v >= 1 - u ? SolidPassThrough : Air,
                    '<' => v <= 1 - u ? Solid : Air,
                    '>' => v <= u ? Solid : Air,
                    '~' => amount > 0 ? liquid : Water,
                    'L' => Lava,
                    '?' => ((x0 + px) + (y0 + py)) % 6 == 0 ? UnknownHatch : Unknown,
                    _ => amount > 0 && v >= 1 - amount / 255.0 ? liquid : Air,
                };
                raster.Set(x0 + px, y0 + py, c);
            }
    }

    private sealed record LegendEntry(string Label, Action<Raster, int, int> Swatch);

    /// <summary>The legend in painting order, background to foreground; <c>Tools/SessionReport/Write/CLAUDE.md</c> lists the same order.</summary>
    internal static readonly string[] LegendOrder =
        { "solid", "pass-through", "platform", "water", "lava", "honey", "shimmer", "air", "unknown", "orb trail", "player trail",
          "intent region", "orb to lookahead", "destination", "navigator goal", "activity target", "drop", "other npc", "hostile", "hostile >60 ticks old", "player", "orb" };

    private static List<LegendEntry> LegendEntries()
    {
        LegendEntry Box(string label, Rgb c) => new(label, (r, x, y) => { r.FillRect(x, y, 12, 12, c); r.OutlineRect(x, y, 12, 12, DarkInk); });
        var entries = new List<LegendEntry>
        {
            Box("solid", Solid), Box("pass-through", SolidPassThrough), Box("platform", Platform),
            Box("water", Water), Box("lava", Lava), Box("honey", Honey), Box("shimmer", Shimmer), Box("air", Air),
            new("unknown", (r, x, y) => { for (int yy = 0; yy < 12; yy++) for (int xx = 0; xx < 12; xx++) r.Set(x + xx, y + yy, (xx + yy) % 6 == 0 ? UnknownHatch : Unknown); }),
            new("orb trail", (r, x, y) => { r.FillRect(x + 2, y + 5, 2, 2, CompanionTrail); r.FillRect(x + 8, y + 5, 2, 2, CompanionTrail); }),
            new("player trail", (r, x, y) => { r.FillRect(x + 2, y + 5, 2, 2, PlayerTrail); r.FillRect(x + 8, y + 5, 2, 2, PlayerTrail); }),
            new("intent region", (r, x, y) => r.OutlineRect(x, y, 12, 12, RegionLine, 2)),
            new("orb to lookahead", (r, x, y) => { r.Line(x, y + 6, x + 8, y + 6, PathLine, 2); r.FillRect(x + 8, y + 3, 5, 6, PathLine); }),
            new("destination", (r, x, y) => { r.Line(x + 1, y + 1, x + 11, y + 11, Destination, 2); r.Line(x + 1, y + 11, x + 11, y + 1, Destination, 2); }),
            new("navigator goal", (r, x, y) => r.OutlineRect(x, y, 12, 12, Destination, 1)),
            new("activity target", (r, x, y) => r.Ring(x + 6, y + 6, 6, Target, 2)),
            Box("drop", DropFill), Box("other npc", OtherNpcFill), Box("hostile", HostileFill),
            new("hostile >60 ticks old", (r, x, y) => { r.FillRect(x, y, 12, 12, Air); r.OutlineRect(x, y, 12, 12, HostileFill, 2); }),
            Box("player", PlayerFill),
            new("orb", (r, x, y) => r.Disc(x + 6, y + 6, 6, CompanionFill, DarkInk)),
        };
        return entries;
    }

    /// <summary>Flow the legend's entries into rows no wider than <paramref name="width"/>; returns the row count.</summary>
    private static int LayoutLegend(List<LegendEntry> entries, int width, out List<(LegendEntry Entry, int X, int Row)> placed)
    {
        placed = new List<(LegendEntry, int, int)>();
        int x = 0, row = 0;
        foreach (LegendEntry entry in entries)
        {
            int w = 16 + Raster.TextWidth(entry.Label) + 14;
            if (x > 0 && x + w > width) { x = 0; row++; }
            placed.Add((entry, x, row));
            x += w;
        }
        return row + 1;
    }

    private static void Grow(ref int min, ref int max, int minimum)
    {
        int size = max - min + 1;
        if (size >= minimum) return;
        int extra = minimum - size;
        min -= extra / 2;
        max += extra - extra / 2;
    }

    private static string Fit(string text, int canvasWidth)
    {
        int chars = (canvasWidth - 2 * Pad) / (4 * TextScale);
        return text.Length <= chars ? text : text[..(chars - 1)] + ".";
    }
}
