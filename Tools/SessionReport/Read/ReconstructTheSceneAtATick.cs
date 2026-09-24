#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AICompanion.Tools.SessionReport;

/// <summary>A hostile or a drop as the record last saw it at or before a tick: where, by which occurrence, and how long ago.</summary>
public sealed record Sighting(string Name, int Slot, float X, float Y, long SeenTick, string Source, bool Exact)
{
    /// <summary>Ticks between the sighting and the tick being explained.</summary>
    public long Age(long tick) => tick - SeenTick;
}

/// <summary>
/// Where everything stood at one recorded tick, assembled only from what the record holds. Every field
/// that a schema does not carry is null and named in <see cref="Absent"/>, because a picture or an
/// explanation that drew a default in its place would be read as an observation.
/// </summary>
public sealed class TickScene
{
    public required int Row { get; init; }
    public required long Tick { get; init; }
    /// <summary>The orb's centre, <c>npc_px</c>.</summary>
    public (float X, float Y)? Companion { get; init; }
    /// <summary>The player's feet, <c>player_px</c>, which the recorder writes from <c>Player.Bottom</c>.</summary>
    public (float X, float Y)? PlayerFeet { get; init; }
    /// <summary>The intent region's centre and half size in pixels, <c>intent_region</c> = <c>cx,cy;hx,hy</c>.</summary>
    public (float CX, float CY, float HX, float HY)? Region { get; init; }
    /// <summary>The destination tile the positioner chose, <c>spot</c>.</summary>
    public (int X, int Y)? Spot { get; init; }
    /// <summary>The steering target in pixels, <c>lookahead</c>; a dash while no route is held.</summary>
    public (float X, float Y)? Lookahead { get; init; }
    /// <summary>The navigator's goal tile from the latest <c>movement-state</c> occurrence at or before the tick.</summary>
    public (int X, int Y, long Tick)? NavigatorGoal { get; init; }
    /// <summary>What the activity said it was aiming at, <c>activity-target=</c> on the latest <c>decision</c> occurrence.</summary>
    public (float X, float Y, long Tick)? ActivityTarget { get; init; }
    public IReadOnlyList<(float X, float Y)> CompanionTrail { get; init; } = Array.Empty<(float, float)>();
    public IReadOnlyList<(float X, float Y)> PlayerTrail { get; init; } = Array.Empty<(float, float)>();
    /// <summary>NPCs the combat census or a combat snapshot held: the record's own witness that they were hostile.</summary>
    public IReadOnlyList<Sighting> Hostiles { get; init; } = Array.Empty<Sighting>();
    /// <summary>NPCs placed by a spawn or a damage record and never held by the combat census: critters, or hostiles the census never saw.</summary>
    public IReadOnlyList<Sighting> OtherNpcs { get; init; } = Array.Empty<Sighting>();
    public IReadOnlyList<Sighting> Drops { get; init; } = Array.Empty<Sighting>();
    /// <summary>Hostile sightings left out because they were older than <see cref="ReconstructTheSceneAtATick.SightingHorizon"/>.</summary>
    public int StaleHostiles { get; init; }
    /// <summary>Which occurrence kinds the capture offered as hostile and drop positions at all.</summary>
    public string SightingSources { get; init; } = "";
    public IReadOnlyList<string> Absent { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The positions a tick's picture and its explanation are drawn from. Nothing is synthesised: a hostile
/// is placed only where an occurrence named where it stood, with its age, and the reader never infers a
/// drop from a death, a position from a count, or a route from a point count.
///
/// <para><b>Which occurrence places a hostile depends on the schema, and the newest captures hold
/// fewest.</b> Up to 0.45.0 the `candidate-funnel` occurrence named every hostile the combat census
/// held, by slot and tile, whenever the list changed; 0.46.0 retired it with the family chooser, and
/// after that the record places a hostile only at its spawn, its damage and its death (`npc-spawn`,
/// `npc-damage`, `npc-death`) and inside a `combat-snapshot` while a plan is held. A world run at 0.48.0
/// wrote none of the first three on 24 September 2026 — their producers are a global-NPC hook and the
/// terrain chunks a mod-system hook, and nothing in the world run's host calls either, which is inferred
/// from the source rather than observed — so a world-run capture places a hostile on a handful of ticks,
/// and the scene says so rather than drawing an empty world.</para>
///
/// <para><b>Route corners are not in any schema.</b> <c>route_points</c> is a count (it replaced the
/// walker's <c>path_steps</c>), <c>route_index</c> the segment the body is on, and the only points the
/// record holds are the steering target (<c>lookahead</c>), the destination tile (<c>spot</c>) and the
/// navigator's goal tile on <c>movement-state</c>, so those three are what gets drawn.</para>
/// </summary>
public static class ReconstructTheSceneAtATick
{
    /// <summary>How old a hostile sighting may be and still be drawn: five seconds at sixty ticks a second. A
    /// sighting older than that is a position the hostile has almost certainly left, so it is counted, not placed.</summary>
    public const long SightingHorizon = 300;

    /// <summary>How many rows of trail each body carries behind it: two seconds.</summary>
    public const int TrailRows = 120;

    private static readonly Regex FunnelEntry = new(@"^(npc|item)(\d+):(\d+)@(-?\d+),(-?\d+)", RegexOptions.CultureInvariant);

    /// <summary>The row holding <paramref name="tick"/>, or the last row before it, or -1 when the capture starts after it.</summary>
    public static int RowOf(Session session, long tick)
    {
        int found = -1;
        for (int i = 0; i < session.Count; i++)
        {
            long at = session.Tick(i);
            if (at == tick) return i;
            if (at < tick) found = i;
        }
        return found;
    }

    public static TickScene At(Session session, GodsEyeEventLog log, int row)
    {
        long tick = session.Tick(row);
        var absent = new List<string>();

        (float, float)? Pixel(string column)
        {
            if (session.Find(column) is not { } c) { absent.Add(column); return null; }
            return Session.TryPair(c.Text[row], out float x, out float y) ? (x, y) : null;
        }

        var companion = Pixel("npc_px");
        var player = Pixel("player_px");
        var lookahead = Pixel("lookahead");
        (int, int)? spot = null;
        if (session.Find("spot") is { } spotColumn)
        { if (Session.TryPair(spotColumn.Text[row], out float sx, out float sy)) spot = ((int)sx, (int)sy); }
        else absent.Add("spot");

        (float, float, float, float)? region = null;
        if (session.Find("intent_region") is { } regionColumn)
        {
            string[] halves = regionColumn.Text[row].Split(';');
            if (halves.Length == 2 && Session.TryPair(halves[0], out float cx, out float cy) && Session.TryPair(halves[1], out float hx, out float hy))
                region = (cx, cy, hx, hy);
        }
        else absent.Add("intent_region");

        var companionTrail = new List<(float, float)>();
        var playerTrail = new List<(float, float)>();
        for (int i = Math.Max(0, row - TrailRows); i <= row; i++)
        {
            if (session.Find("npc_px") is { } n && Session.TryPair(n.Text[i], out float nx, out float ny)) companionTrail.Add((nx, ny));
            if (session.Find("player_px") is { } p && Session.TryPair(p.Text[i], out float px, out float py)) playerTrail.Add((px, py));
        }

        (int, int, long)? goal = null;
        (float, float, long)? activityTarget = null;
        // Latest sighting per hostile slot, and the deaths that retire them.
        var hostiles = new Dictionary<int, Sighting>();
        var deaths = new Dictionary<int, long>();
        var drops = new List<Sighting>();
        var pickups = new List<(long Tick, string Type, float X, float Y)>();
        List<Sighting>? funnelHostiles = null, funnelDrops = null;
        long funnelHostileTick = -1, funnelDropTick = -1;
        var sources = new SortedSet<string>(StringComparer.Ordinal);

        // `npc-damage` names its hostile by the spawn's generation identity and carries no slot, so the slot is
        // learned from the `npc-spawn` that minted the identity.
        var slotBySubject = new Dictionary<int, int>();
        // The tick each slot's current occupant spawned, and the last tick the combat census or a combat snapshot held the slot.
        var spawned = new Dictionary<int, long>();
        var censused = new Dictionary<int, long>();
        foreach (GodsEyeEvent e in log.Events)
        {
            if (e.tick > tick) continue;
            switch (e.kind)
            {
                case "movement-state":
                    if (e.Field("goal") is { } g && ParseBracePoint(g) is { } gp) goal = ((int)gp.X, (int)gp.Y, e.tick);
                    else goal = null;
                    break;
                case "decision":
                    activityTarget = e.Field("activity-target") is { } t && ParseBracePoint(t) is { } tp ? (tp.X, tp.Y, e.tick) : null;
                    break;
                case "candidate-funnel":
                {
                    sources.Add("candidate-funnel");
                    int start = e.detail.IndexOf("entries=", StringComparison.Ordinal);
                    // The entries run to the end of the detail and carry their own ';' inside brackets, so they are
                    // read as the tail rather than through the key=value splitter.
                    string entries = start < 0 ? "" : e.detail[(start + "entries=".Length)..];
                    var seen = new List<Sighting>();
                    foreach (string entry in entries.Split('|'))
                    {
                        Match m = FunnelEntry.Match(entry);
                        if (!m.Success) continue;
                        int slot = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                        float x = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) * 16 + 8f;
                        float y = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture) * 16 + 8f;
                        seen.Add(new Sighting($"type {m.Groups[3].Value}", slot, x, y, e.tick, $"{e.label} funnel", Exact: false));
                    }
                    if (e.label == "combat")
                    {
                        funnelHostiles = seen; funnelHostileTick = e.tick;
                        foreach (Sighting s in seen) censused[s.Slot] = e.tick;
                    }
                    else if (e.label == "collect") { funnelDrops = seen; funnelDropTick = e.tick; }
                    break;
                }
                case "npc-spawn":
                case "npc-damage":
                {
                    if (e.label == "Companion") break;
                    sources.Add(e.kind);
                    int slot = SlotOf(e);
                    if (slot >= 0) slotBySubject[e.subject] = slot;
                    else if (!slotBySubject.TryGetValue(e.subject, out slot)) break;
                    if (e.kind == "npc-spawn") spawned[slot] = e.tick;
                    hostiles[slot] = new Sighting(e.label, slot, e.pos_x, e.pos_y, e.tick, e.kind, Exact: true);
                    break;
                }
                case "npc-death":
                {
                    if (e.label == "Companion") break;
                    int slot = SlotOf(e);
                    if (slot >= 0) deaths[slot] = e.tick;
                    break;
                }
                case "combat-snapshot":
                    sources.Add("combat-snapshot");
                    foreach (Sighting s in SnapshotNpcs(e)) { hostiles[s.Slot] = s; censused[s.Slot] = e.tick; }
                    break;
                case "drop-sighted":
                    sources.Add("drop-sighted");
                    drops.Add(new Sighting($"item {e.label}", -1, e.pos_x, e.pos_y, e.tick, "drop-sighted", Exact: true));
                    break;
                case "pickup":
                    pickups.Add((e.tick, e.label, e.pos_x, e.pos_y));
                    break;
            }
        }

        // The combat funnel lists every hostile the census held at its tick, so its list is the set and each
        // entry is that hostile's latest position unless an exact occurrence saw it more recently.
        if (funnelHostiles != null)
            foreach (Sighting s in funnelHostiles)
                if (!hostiles.TryGetValue(s.Slot, out Sighting? known) || known.SeenTick < s.SeenTick) hostiles[s.Slot] = s;

        var placed = new List<Sighting>();
        var others = new List<Sighting>();
        int stale = 0;
        foreach (Sighting s in hostiles.Values)
        {
            if (deaths.TryGetValue(s.Slot, out long died) && died >= s.SeenTick) continue;
            // A funnel entry older than the last funnel is a hostile the census had dropped by then.
            if (s.Source == "combat funnel" && s.SeenTick < funnelHostileTick) continue;
            if (s.Age(tick) > SightingHorizon) { stale++; continue; }
            // Hostile only if the combat census or a combat snapshot held this slot's current occupant: a spawn
            // names squirrels, fireflies and bunnies as readily as zombies, and the record has no other witness.
            bool hostile = censused.TryGetValue(s.Slot, out long held) && held >= spawned.GetValueOrDefault(s.Slot, long.MinValue);
            (hostile ? placed : others).Add(s);
        }

        if (funnelDrops != null && tick - funnelDropTick <= SightingHorizon) drops.AddRange(funnelDrops);
        var standing = drops.Where(d => d.Age(tick) <= SightingHorizon
                && !pickups.Any(p => p.Tick >= d.SeenTick && d.Name.EndsWith(" " + p.Type, StringComparison.Ordinal)
                    && MathF.Abs(p.X - d.X) <= 48 && MathF.Abs(p.Y - d.Y) <= 48))
            .ToList();

        return new TickScene
        {
            Row = row, Tick = tick,
            Companion = companion, PlayerFeet = player, Region = region, Spot = spot, Lookahead = lookahead,
            NavigatorGoal = goal, ActivityTarget = activityTarget,
            CompanionTrail = companionTrail, PlayerTrail = playerTrail,
            Hostiles = placed.OrderBy(s => s.Slot).ToList(), OtherNpcs = others.OrderBy(s => s.Slot).ToList(), Drops = standing, StaleHostiles = stale,
            SightingSources = sources.Count == 0 ? "none" : string.Join(", ", sources),
            Absent = absent,
        };
    }

    private static int SlotOf(GodsEyeEvent e)
        => e.Field("slot") is { } text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) ? slot : -1;

    /// <summary>The hostiles a <c>combat-snapshot</c> held, from its JSON detail's <c>Npcs</c> list.</summary>
    private static IEnumerable<Sighting> SnapshotNpcs(GodsEyeEvent e)
    {
        var found = new List<Sighting>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(e.detail);
            if (!document.RootElement.TryGetProperty("Npcs", out JsonElement npcs) || npcs.ValueKind != JsonValueKind.Array) return found;
            foreach (JsonElement npc in npcs.EnumerateArray())
            {
                if (!npc.TryGetProperty("Slot", out JsonElement slot) || !npc.TryGetProperty("Position", out JsonElement position)) continue;
                string name = npc.TryGetProperty("Type", out JsonElement type) ? type.ToString() : "?";
                found.Add(new Sighting(name, slot.GetInt32(), position.GetProperty("X").GetSingle(), position.GetProperty("Y").GetSingle(),
                    e.tick, "combat-snapshot", Exact: true));
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { }
        return found;
    }

    /// <summary>
    /// The terrain a <c>combat-snapshot</c> carried, as a snapshot record the shared reconstruction can apply:
    /// its <c>Terrain</c> block is a glyph rectangle at a tile origin, in the same alphabet as a chunk. It is the
    /// only terrain a capture holds where the chunk recorder never ran, which is every world run at 0.48.0.
    /// </summary>
    internal static IEnumerable<(long Tick, TerrainSnapshotRecord Record)> CombatSnapshotTerrain(GodsEyeEventLog log)
    {
        foreach (GodsEyeEvent e in log.Events)
        {
            if (e.kind != "combat-snapshot") continue;
            TerrainSnapshotRecord? record = null;
            try
            {
                using JsonDocument document = JsonDocument.Parse(e.detail);
                if (document.RootElement.TryGetProperty("Terrain", out JsonElement t) && t.ValueKind == JsonValueKind.Object)
                {
                    int x = t.GetProperty("X").GetInt32(), y = t.GetProperty("Y").GetInt32();
                    int w = t.GetProperty("Width").GetInt32(), h = t.GetProperty("Height").GetInt32();
                    string glyphs = t.GetProperty("Glyphs").GetString() ?? "";
                    record = new TerrainSnapshotRecord(e.wall_elapsed_ms, x * 16f, y * 16f,
                        string.Create(CultureInfo.InvariantCulture, $"width={w};height={h};tiles={glyphs}"));
                }
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { }
            if (record is { } found) yield return (e.tick, found);
        }
    }

    /// <summary>A <c>{X:3463 Y:357}</c> point as the recorder writes a Point or a Vector2.</summary>
    internal static (float X, float Y)? ParseBracePoint(string text)
    {
        Match m = Regex.Match(text, @"X:(-?[\d.]+)\s+Y:(-?[\d.]+)", RegexOptions.CultureInvariant);
        return m.Success
            && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                ? (x, y) : null;
    }
}
