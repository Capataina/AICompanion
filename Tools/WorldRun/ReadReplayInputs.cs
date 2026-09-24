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
/// Because every frame is a delta, one lost line silently corrupts every frame after it, so a lost line is a refusal
/// here rather than something a reproduction discovers later as drift. Format 2 (schema 0.50.0) numbers its lines, and a
/// gap in the numbers is named with the tick after it and how many lines it lost. Format 1 (0.49.0) has no numbers, so it
/// is checked through the sidecar envelope's own sequence: any occurrence missing between its first and last
/// `replay-inputs` line might have been one of them, and that capture is refused rather than trusted. A reproduction does
/// not resume from the keyframe the recorder writes after a lost line, because the world edits and the random state that
/// line carried are gone with it, and a terrain that silently lacks them is the confident wrong answer this file exists to
/// refuse; the keyframe is what lets every other reader of the capture read past the gap.
///
/// Anything below the schema that introduced these inputs is refused by name in <see cref="Record.Refusal"/>, as is a
/// capture whose sidecar has none, a format version this reader does not know, or a cut file.
/// </summary>
internal static class ReadReplayInputs
{
    /// <summary>The recorder schema that first carries replay inputs.</summary>
    public const string FirstSchema = "0.49.0";

    /// <summary>The line formats this reader knows: 1 (schema 0.49.0) and 2 (0.50.0).</summary>
    public const int OldestFormat = 1, NewestFormat = 2;

    internal readonly record struct Body(Vector2 Position, Vector2 Velocity, int Life, bool Downed);

    /// <summary>One entity slot's change: the fields that moved, or null for a slot the tick found empty.</summary>
    internal readonly record struct SlotChange(int Slot, IReadOnlyDictionary<string, string>? Fields);

    /// <summary>A tile as an edit left it. <c>Announcements</c> is how many times the edit log heard of it before the tick,
    /// zero for a neighbour the game reframed without announcing, which the reproduction writes and does not announce.</summary>
    internal readonly record struct TileEdit(int X, int Y, string State, int Announcements);

    internal sealed record Frame(
        int Format,
        /// <summary>The line's ordinal within its session (format 2), or null for a format 1 line.</summary>
        long? Ordinal,
        bool Keyframe,
        ulong Tick,
        Body Companion,
        /// <summary>The companion's own NPC slot when this line recorded it (format 2: first line, keyframes, a move).</summary>
        int? Self,
        /// <summary>The weapon knowledge digest on a keyframe, and its revision when it moved (format 2).</summary>
        string? Knowledge,
        int? KnowledgeRevision,
        IReadOnlyDictionary<string, string> PlayerChanges,
        IReadOnlyDictionary<string, string> WorldChanges,
        string? Inventory,
        string? Gear,
        string? Bag,
        IReadOnlyList<SlotChange> Npcs,
        IReadOnlyList<SlotChange> Items,
        IReadOnlyList<SlotChange> Projectiles,
        IReadOnlyList<TileEdit> WorldEdits,
        /// <summary>World edits the recorder counted and did not keep before this tick, because too many waited for a
        /// companion tick; zero on an ordinary line.</summary>
        int EditsLost,
        ulong? LightSeed,
        /// <summary>`left,top:hash` of the tiles around the body, on the ticks the recorder took one, else null.</summary>
        string? Terrain,
        string Clock,
        string? Random,
        string? RandomEnd,
        /// <summary>`WorldGen.genRand`'s state at the tick's start and its end fingerprint, on a tick that drew from it.</summary>
        string? GenRandom,
        string? GenRandomEnd,
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
        // Every envelope sequence number in the sidecar, and the first and last of the replay-inputs lines, for the format 1 gap check.
        var sequences = new List<int>();
        int firstReplaySequence = -1, lastReplaySequence = -1;
        int lineNumber = 0;
        foreach (string line in File.ReadLines(events))
        {
            lineNumber++;
            if (line.Length == 0 || line[0] != '{') continue;
            if (EnvelopeSequence(line) is { } sequence) sequences.Add(sequence);
            if (!line.Contains("\"replay-inputs\"", StringComparison.Ordinal)) continue;
            if (maxFrames > 0 && frames.Count >= maxFrames) continue;
            string detail;
            int? replaySequence;
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != "replay-inputs") continue;
                detail = root.GetProperty("detail").GetString() ?? "";
                replaySequence = root.TryGetProperty("seq", out JsonElement seq) ? seq.GetInt32() : null;
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
            if (replaySequence is { } at)
            {
                if (firstReplaySequence < 0) firstReplaySequence = at;
                lastReplaySequence = at;
            }
            frames.Add(frame);
        }
        if (frames.Count == 0)
            return new Record(capture, schema, synthetic.Length > 0, synthetic, frames,
                $"{Path.GetFileName(events)} has no replay-inputs occurrence although its schema is {schema}; the recorder's "
                + "session never reached a companion tick, or its sidecar was written by a different build");
        string? gap = frames[0].Format >= 2 ? OrdinalGap(frames) : EnvelopeGap(sequences, firstReplaySequence, lastReplaySequence, frames);
        return new Record(capture, schema, synthetic.Length > 0, synthetic, frames, gap);
    }

    /// <summary>A format 2 capture's first lost line, named with the tick after the gap and how many lines it lost.</summary>
    private static string? OrdinalGap(IReadOnlyList<Frame> frames)
    {
        long expected = 0;
        for (int index = 0; index < frames.Count; index++)
        {
            long ordinal = frames[index].Ordinal ?? throw new FormatException($"frame {index} is format 2 and carries no ordinal");
            if (ordinal != expected)
                return string.Create(CultureInfo.InvariantCulture,
                    $"the replay-inputs lines are not whole: {ordinal - expected} line(s) (n={expected}..{ordinal - 1}) were lost before recorded tick {frames[index].Tick} (frame {index}), because the writer's queue refused them; every later frame is a delta against what they said, so the capture is refused rather than reported as a share, and a reproduction does not resume from the keyframe the recorder wrote after them, because the world edits and random state those lines carried are gone");
            expected = ordinal + 1;
        }
        return null;
    }

    /// <summary>
    /// A format 1 capture's gap, read from the sidecar envelope's sequence: format 1 lines carry no ordinal, so an occurrence
    /// missing anywhere between the first and last replay-inputs line may have been one of them.
    /// </summary>
    private static string? EnvelopeGap(List<int> sequences, int first, int last, IReadOnlyList<Frame> frames)
    {
        if (first < 0) return null;
        var present = new HashSet<int>(sequences);
        int missing = 0, firstMissing = -1;
        for (int sequence = first; sequence <= last; sequence++)
            if (!present.Contains(sequence)) { missing++; if (firstMissing < 0) firstMissing = sequence; }
        if (missing == 0) return null;
        return string.Create(CultureInfo.InvariantCulture,
            $"{missing} sidecar occurrence(s) are missing between the first and last replay-inputs line (the first at seq {firstMissing}); a format 1 capture (schema 0.49.0) numbers no replay-inputs line, so any of them may have been one, and every frame after a lost line is a delta against it — refused rather than reproduced across {frames.Count} frames that may not be whole");
    }

    /// <summary>The envelope's `seq`, read without parsing the line, because the sidecar runs to tens of megabytes.</summary>
    private static int? EnvelopeSequence(string line)
    {
        int at = line.IndexOf("\"seq\":", StringComparison.Ordinal);
        if (at < 0) return null;
        int start = at + 6, end = start;
        while (end < line.Length && char.IsAsciiDigit(line[end])) end++;
        return end > start && int.TryParse(line.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;
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
        if (version < OldestFormat || version > NewestFormat)
            throw new FormatException($"replay-inputs format {version}, and this reader knows {OldestFormat} to {NewestFormat}");

        string[] body = parts["body"].Split(',');
        var companion = new Body(new Vector2(Float(body[0]), Float(body[1])), new Vector2(Float(body[2]), Float(body[3])),
            int.Parse(body[4], CultureInfo.InvariantCulture), body[5] == "1");

        return new Frame(
            version,
            parts.TryGetValue("n", out string? ordinal) ? long.Parse(ordinal, CultureInfo.InvariantCulture) : null,
            parts.GetValueOrDefault("key") == "1",
            ulong.Parse(parts["tick"], CultureInfo.InvariantCulture),
            companion,
            parts.TryGetValue("self", out string? self) ? int.Parse(self, CultureInfo.InvariantCulture) : null,
            parts.GetValueOrDefault("kn"),
            parts.TryGetValue("kr", out string? revision) ? int.Parse(revision, CultureInfo.InvariantCulture) : null,
            Fields(parts["player"]),
            Fields(parts["world"]),
            parts.GetValueOrDefault("inv"),
            parts.GetValueOrDefault("gear"),
            parts.GetValueOrDefault("bag"),
            Slots(parts["npc"]),
            Slots(parts["item"]),
            parts.TryGetValue("proj", out string? projectiles) ? Slots(projectiles) : Array.Empty<SlotChange>(),
            Edits(parts["edits"]),
            parts.TryGetValue("edits-lost", out string? lost) ? int.Parse(lost, CultureInfo.InvariantCulture) : 0,
            parts["light"] == "-" ? null : ulong.Parse(parts["light"], CultureInfo.InvariantCulture),
            parts.GetValueOrDefault("terrain"),
            parts["clock"],
            parts.GetValueOrDefault("rand"),
            parts.GetValueOrDefault("rand-end"),
            parts.GetValueOrDefault("grand"),
            parts.GetValueOrDefault("grand-end"),
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

    /// <summary>`x,y,state,mark` entries joined by `|`: the mark is `n` for an unannounced tile, `a` for one announcement
    /// and `a3` for three (format 2 coalesces a tile's repeats); a companion edit carries no mark and counts once.</summary>
    private static IReadOnlyList<TileEdit> Edits(string text)
    {
        if (text.Length == 0) return Array.Empty<TileEdit>();
        var edits = new List<TileEdit>();
        foreach (string entry in text.Split('|'))
        {
            string[] parts = entry.Split(',');
            int announcements = parts.Length < 4 ? 1
                : parts[3] == "n" ? 0
                : parts[3] == "a" ? 1
                : parts[3].StartsWith('a') ? int.Parse(parts[3].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture)
                : throw new FormatException($"an edit's mark is n, a or a<count>; got \"{parts[3]}\"");
            edits.Add(new TileEdit(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), parts[2], announcements));
        }
        return edits;
    }
}
