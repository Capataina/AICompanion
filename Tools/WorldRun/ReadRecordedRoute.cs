using System.Globalization;
using Microsoft.Xna.Framework;

/// <summary>
/// A capture, read as a route: where the player went, tick by tick, and what both bodies were
/// allowed to do while they went there.
///
/// The route is the world run's second input, beside the world itself, and it is what separates
/// this instrument from the captured-window replay next door. That one seeds one pose and leaves
/// the player standing still, so no decision that depended on the player moving — a reunion, an
/// overtaking walk, a follow that gave up — can be reproduced at all. Here the player is moved
/// along the track it actually took, which is the only way those decisions happen twice.
///
/// The one thing this file will not do is guess. A capture written before the recorder carried the
/// two header lines has no ability flags and no world identity, and the plan refuses inferring
/// abilities from the track by name: a heuristic that watches where the player went and decides
/// which ability must have taken them there is a heuristic with its own false positives, running
/// underneath the very thing being measured. So an absent header line becomes an absent kit, and
/// every row that needed it becomes a skip carrying its reason rather than a verdict.
/// </summary>
internal static class ReadRecordedRoute
{
    /// <summary>One recorded tick: where both bodies were, and what the player was doing.</summary>
    internal readonly record struct Step(
        int Tick,
        Vector2 PlayerFeet,
        Vector2 PlayerVelocity,
        bool PlayerGrounded,
        Vector2 CompanionLeftBottom);

    /// <summary>
    /// What the capture says both bodies could do, or nothing at all.
    ///
    /// <c>Known</c> is false for every capture written before the recorder's capabilities line
    /// existed, which includes both of the captures this instrument was first calibrated against.
    /// It is deliberately not a set of defaults: a default kit is an inference wearing a constant's
    /// clothes, and it would silently decide the one question the checkpoint filter exists to ask.
    /// </summary>
    internal readonly record struct Kits(bool Known, string Raw, bool PlayerMount, bool PlayerWings, bool PlayerDash, bool PlayerRocketBoots)
    {
        /// <summary>Whether the player could reach places on something other than their own legs.</summary>
        public bool PlayerCanLeaveTheGround => PlayerMount || PlayerWings || PlayerRocketBoots;
    }

    internal sealed record Route(
        string Capture,
        string SourceRevision,
        string Schema,
        Kits Kits,
        string WorldLine,
        IReadOnlyList<Step> Steps)
    {
        public Step this[int index] => Steps[index];
        public int Count => Steps.Count;
    }

    public static Route Read(string capturePath, int fromTick, int maxTicks)
    {
        if (!File.Exists(capturePath)) throw new FileNotFoundException($"no capture at {capturePath}", capturePath);

        string schema = "unknown", revision = "unknown", worldLine = "", capabilities = "";
        string[]? header = null;
        var steps = new List<Step>();

        foreach (string line in File.ReadLines(capturePath))
        {
            if (line.Length == 0) continue;
            if (line[0] == '﻿' ? line[1] == '#' : line[0] == '#')
            {
                string comment = line.TrimStart('﻿', '#', ' ');
                if (comment.StartsWith("schema=", StringComparison.Ordinal)) schema = comment[7..].Trim();
                else if (comment.StartsWith("source_revision=", StringComparison.Ordinal)) revision = comment[16..].Split(';')[0].Trim();
                else if (comment.StartsWith("world=", StringComparison.Ordinal)) worldLine = comment[6..].Trim();
                else if (comment.StartsWith("capabilities=", StringComparison.Ordinal)) capabilities = comment[13..].Trim();
                continue;
            }
            string[] cells = line.TrimStart('﻿').Split('\t');
            if (header == null) { header = cells; continue; }
            if (cells.Length != header.Length) continue;   // a row torn by a flush is not a step

            int tick = int.Parse(cells[0], CultureInfo.InvariantCulture);
            if (tick < fromTick) continue;
            if (maxTicks > 0 && steps.Count >= maxTicks) break;

            steps.Add(new Step(
                tick,
                Pair(cells, header, "player_px"),
                Pair(cells, header, "player_vel"),
                Cell(cells, header, "player_ground") == "1",
                new Vector2(Number(cells, header, "observed_left"), Number(cells, header, "observed_bottom"))));
        }

        if (header == null) throw new InvalidDataException($"{capturePath} has no TSV header");
        if (steps.Count == 0) throw new InvalidDataException($"{capturePath} holds no rows at or after tick {fromTick}");
        ConfirmTheTrackIsContiguous(steps, capturePath);

        return new Route(Path.GetFileName(capturePath), revision, schema, ParseKits(capabilities), worldLine, steps);
    }

    /// <summary>
    /// Refuses a track with a hole in it, rather than replaying across one.
    ///
    /// A capture writes one row per companion AI tick, so a gap means ticks happened that nobody
    /// recorded — and driving the player from the row after a gap teleports them across whatever
    /// the gap contained. Interpolating instead would invent a path the player never walked and
    /// then measure the companion against it. Both captures this was built against are contiguous
    /// over every one of their rows, so this is a guard on a property that holds rather than a
    /// repair for one that does not.
    /// </summary>
    private static void ConfirmTheTrackIsContiguous(List<Step> steps, string capturePath)
    {
        for (int i = 1; i < steps.Count; i++)
            if (steps[i].Tick != steps[i - 1].Tick + 1)
                throw new InvalidDataException(
                    $"{Path.GetFileName(capturePath)} jumps from tick {steps[i - 1].Tick} to {steps[i].Tick}; "
                    + "a player track with a hole in it would be replayed as a teleport");
    }

    /// <summary>
    /// The capabilities header, parsed, or an honest absence.
    ///
    /// The line names both kits — the companion's declared movement kit and the player's mount,
    /// wings, dash and rocket boots. Only the player's half is read here, because the companion's
    /// half is asked of the mod itself at the moment it is needed: reading it out of a capture
    /// would compare a build against a kit some older build declared.
    /// </summary>
    private static Kits ParseKits(string capabilities)
    {
        if (capabilities.Length == 0) return new Kits(false, "", false, false, false, false);
        string player = capabilities.Split(';').FirstOrDefault(p => p.StartsWith("player:", StringComparison.Ordinal)) ?? "";
        var fields = player["player:".Length..].Split(',')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);

        bool Flag(string name) => fields.TryGetValue(name, out string? value)
            && !value.Equals("False", StringComparison.OrdinalIgnoreCase)
            && value != "0";

        return new Kits(true, capabilities, Flag("mount"), Flag("wings"), Flag("dash"), Flag("rocket-boots"));
    }

    private static string Cell(string[] cells, string[] header, string name)
    {
        int index = Array.IndexOf(header, name);
        if (index < 0) throw new InvalidDataException($"the capture has no column named {name}; this schema cannot be replayed as a route");
        return cells[index];
    }

    private static float Number(string[] cells, string[] header, string name)
        => float.Parse(Cell(cells, header, name), CultureInfo.InvariantCulture);

    private static Vector2 Pair(string[] cells, string[] header, string name)
    {
        string[] parts = Cell(cells, header, name).Split(',');
        return new Vector2(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture));
    }
}
