using System.Globalization;
using System.Text.Json;
using Microsoft.Xna.Framework;

/// <summary>
/// A capture's `replay-inputs` occurrences read back as frames, one per recorded companion tick, for
/// <see cref="ReproduceTheCapture"/> to put the world back from.
///
/// The recorder writes each entity's fields as deltas against what it last wrote for that slot, so a frame here
/// carries the deltas and the reproduction accumulates them; the reader keeps no whole-scene copy per frame,
/// because a capture of a few thousand ticks with a dozen actors would otherwise hold tens of megabytes of
/// strings nobody reads twice.
///
/// Anything below the schema that introduced these inputs is refused by name in <see cref="Refusal"/>, as is a
/// capture whose sidecar has none, a format version this reader does not know, or a cut file: each would replay into
/// a confident wrong answer, which is worse than a named refusal.
/// </summary>
internal static class ReadReplayInputs
{
    /// <summary>The recorder schema that first carries replay inputs.</summary>
    public const string FirstSchema = "0.49.0";

    /// <summary>The one line format this reader knows.</summary>
    public const int KnownFormat = 1;

    internal readonly record struct Body(Vector2 Position, Vector2 Velocity, int Life, bool Downed);

    /// <summary>One entity slot's change: the fields that moved, or null for a slot the tick found empty.</summary>
    internal readonly record struct SlotChange(int Slot, IReadOnlyDictionary<string, string>? Fields);

    internal readonly record struct TileEdit(int X, int Y, string State);

    internal sealed record Frame(
        ulong Tick,
        Body Companion,
        IReadOnlyDictionary<string, string> PlayerChanges,
        IReadOnlyDictionary<string, string> WorldChanges,
        string? Inventory,
        string? Gear,
        IReadOnlyList<SlotChange> Npcs,
        IReadOnlyList<SlotChange> Items,
        IReadOnlyList<TileEdit> WorldEdits,
        ulong? LightSeed,
        string Clock,
        string? Random,
        string? RandomEnd,
        string Ops,
        IReadOnlyList<TileEdit> CompanionEdits,
        string Decision,
        int Characters);

    internal sealed record Record(
        string Capture,
        string Schema,
        bool Synthetic,
        string SyntheticLine,
        IReadOnlyList<Frame> Frames,
        /// <summary>Null when the capture can be reproduced; otherwise the named reason it cannot.</summary>
        string? Refusal);

    public static Record Read(string capturePath, int maxFrames = 0)
    {
        string capture = Path.GetFileName(capturePath);
        string schema = "unknown", synthetic = "";
        foreach (string line in File.ReadLines(capturePath))
        {
            if (!line.TrimStart('﻿').StartsWith('#')) break;
            string comment = line.TrimStart('﻿')[1..].Trim();
            if (comment.StartsWith("schema=", StringComparison.Ordinal)) schema = comment[7..].Trim();
            if (comment.StartsWith("synthetic=", StringComparison.Ordinal)) synthetic = comment;
        }

        if (!SchemaAtLeast(schema, FirstSchema))
            return new Record(capture, schema, synthetic.Length > 0, synthetic, Array.Empty<Frame>(),
                $"{capture} is schema {schema}, and replay inputs begin at {FirstSchema}: it holds no per-tick actors, no "
                + "terrain edits as edits, no decision-clock answers and no random state, so a reproduction of it would "
                + "replace every one of those with this process's own and report the result as the play's");

        string events = ReadRecordedActors.EventsPathFor(capturePath);
        if (!File.Exists(events))
            return new Record(capture, schema, synthetic.Length > 0, synthetic, Array.Empty<Frame>(),
                $"no events sidecar at {Path.GetFileName(events)}; the replay inputs live there, and Telemetry/ being gitignored "
                + "makes a capture copied without it the common loss");

        var frames = new List<Frame>();
        int lineNumber = 0;
        foreach (string line in File.ReadLines(events))
        {
            lineNumber++;
            if (line.Length == 0 || line[0] != '{' || !line.Contains("\"replay-inputs\"", StringComparison.Ordinal)) continue;
            string detail;
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != "replay-inputs") continue;
                detail = root.GetProperty("detail").GetString() ?? "";
            }
            catch (JsonException failure)
            {
                return new Record(capture, schema, synthetic.Length > 0, synthetic, frames,
                    $"the sidecar is cut at {Path.GetFileName(events)} line {lineNumber}: {failure.Message}; a reproduction "
                    + "stops being one at the cut, and a partial one would be reported as whole");
            }
            Frame frame;
            try
            {
                frame = ParseFrame(detail);
            }
            catch (Exception failure) when (failure is FormatException or KeyNotFoundException or IndexOutOfRangeException)
            {
                return new Record(capture, schema, synthetic.Length > 0, synthetic, frames,
                    $"replay-inputs at {Path.GetFileName(events)} line {lineNumber} does not parse: {failure.Message}");
            }
            frames.Add(frame);
            if (maxFrames > 0 && frames.Count >= maxFrames) break;
        }
        if (frames.Count == 0)
            return new Record(capture, schema, synthetic.Length > 0, synthetic, frames,
                $"{Path.GetFileName(events)} has no replay-inputs occurrence although its schema is {schema}; the recorder's "
                + "session never reached a companion tick, or its sidecar was written by a different build");
        return new Record(capture, schema, synthetic.Length > 0, synthetic, frames, null);
    }

    /// <summary>Whether a dotted version is at least another, compared part by part as numbers.</summary>
    public static bool SchemaAtLeast(string schema, string floor)
    {
        string[] have = schema.Split('.'), want = floor.Split('.');
        for (int index = 0; index < want.Length; index++)
        {
            if (index >= have.Length || !int.TryParse(have[index], NumberStyles.None, CultureInfo.InvariantCulture, out int part))
                return false;
            int wanted = int.Parse(want[index], CultureInfo.InvariantCulture);
            if (part != wanted) return part > wanted;
        }
        return true;
    }

    private static Frame ParseFrame(string detail)
    {
        int decisionAt = detail.IndexOf(";decision=", StringComparison.Ordinal);
        if (decisionAt < 0) throw new FormatException("no decision field");
        string decision = detail[(decisionAt + ";decision=".Length)..];
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string part in detail[..decisionAt].Split(';'))
        {
            int equals = part.IndexOf('=');
            if (equals < 0) throw new FormatException($"the part \"{part}\" has no key");
            parts[part[..equals]] = part[(equals + 1)..];
        }
        int version = int.Parse(parts["v"], CultureInfo.InvariantCulture);
        if (version != KnownFormat) throw new FormatException($"replay-inputs format {version}, and this reader knows {KnownFormat}");

        string[] body = parts["body"].Split(',');
        var companion = new Body(new Vector2(Float(body[0]), Float(body[1])), new Vector2(Float(body[2]), Float(body[3])),
            int.Parse(body[4], CultureInfo.InvariantCulture), body[5] == "1");

        return new Frame(
            ulong.Parse(parts["tick"], CultureInfo.InvariantCulture),
            companion,
            Fields(parts["player"]),
            Fields(parts["world"]),
            parts.GetValueOrDefault("inv"),
            parts.GetValueOrDefault("gear"),
            Slots(parts["npc"]),
            Slots(parts["item"]),
            Edits(parts["edits"]),
            parts["light"] == "-" ? null : ulong.Parse(parts["light"], CultureInfo.InvariantCulture),
            parts["clock"],
            parts.GetValueOrDefault("rand"),
            parts.GetValueOrDefault("rand-end"),
            parts["ops"],
            Edits(parts["cedits"]),
            decision,
            detail.Length);
    }

    private static float Float(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static IReadOnlyDictionary<string, string> Fields(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (text.Length == 0) return fields;
        foreach (string pair in text.Split(','))
        {
            int equals = pair.IndexOf('=');
            if (equals < 0) throw new FormatException($"the field \"{pair}\" has no value");
            fields[pair[..equals]] = pair[(equals + 1)..];
        }
        return fields;
    }

    private static IReadOnlyList<SlotChange> Slots(string text)
    {
        if (text.Length == 0) return Array.Empty<SlotChange>();
        var slots = new List<SlotChange>();
        foreach (string entry in text.Split('|'))
        {
            int colon = entry.IndexOf(':');
            int slot = int.Parse(entry[..colon], CultureInfo.InvariantCulture);
            string rest = entry[(colon + 1)..];
            slots.Add(new SlotChange(slot, rest == "-" ? null : Fields(rest)));
        }
        return slots;
    }

    private static IReadOnlyList<TileEdit> Edits(string text)
    {
        if (text.Length == 0) return Array.Empty<TileEdit>();
        var edits = new List<TileEdit>();
        foreach (string entry in text.Split('|'))
        {
            string[] parts = entry.Split(',');
            edits.Add(new TileEdit(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), parts[2]));
        }
        return edits;
    }
}
