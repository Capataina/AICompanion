#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// One validated occurrence from the <c>-events.jsonl</c> sibling. The lower-case member names are
/// the writer's JSON field names (<c>RecordGodsEyeEvents.cs</c>, <c>EventRecord</c>), bound by name,
/// so a renamed writer field makes every line malformed rather than silently zero.
/// </summary>
public sealed record GodsEyeEvent(int v, int seq, long tick, double wall_elapsed_ms, string kind, int subject, string related, string label, string channel,
    float pos_x, float pos_y, float vel_x, float vel_y, float expected_x, float expected_y, int amount, string detail,
    string? payload_kind = null, int? payload_version = null, string? phase = null, long? observation_ordinal = null, long? receipt_watermark = null,
    JsonElement? context = null, JsonElement? payload = null, JsonElement? snapshot = null)
{
    public string? Field(string key) => ReadGodsEyeEvents.Field(detail, key);
    public string? ChannelField(string key) => ReadGodsEyeEvents.Field(channel, key);
}

/// <summary>The sibling as read: every valid occurrence except the session markers, and what the reading could not vouch for.</summary>
public sealed class GodsEyeEventLog
{
    public bool Present { get; init; }
    public IReadOnlyList<GodsEyeEvent> Events { get; init; } = Array.Empty<GodsEyeEvent>();
    public int Malformed { get; init; }
    public int MissingSequences { get; init; }
    public bool Opened { get; init; }
    public bool Closed { get; init; }
}

/// <summary>
/// The one reader of the occurrence sibling. The event summary, the attempt identity join and the
/// identity checks all consume it, so a line one of them accepts cannot be a line another rejects.
/// </summary>
public static class ReadGodsEyeEvents
{
    private static readonly string[] RequiredFields = { "v", "seq", "tick", "wall_elapsed_ms", "kind", "subject", "related", "label", "channel", "pos_x", "pos_y", "vel_x", "vel_y", "expected_x", "expected_y", "amount", "detail" };

    // A report reads the same sidecar for its summary, its identity view and two checks; a playtest
    // sidecar reaches tens of megabytes, so the last read is kept while the file is unchanged.
    private static (string Path, DateTime Written, long Length, GodsEyeEventLog Log)? cached;

    public static string PathFor(string tsvPath) => Path.ChangeExtension(tsvPath, null) + "-events.jsonl";

    public static GodsEyeEventLog Read(string tsvPath)
    {
        string path = PathFor(tsvPath);
        if (!File.Exists(path)) return new GodsEyeEventLog { Present = false };
        var info = new FileInfo(path);
        string full = info.FullName;
        if (cached is { } hit && hit.Path == full && hit.Written == info.LastWriteTimeUtc && hit.Length == info.Length)
            return hit.Log;

        var events = new List<GodsEyeEvent>();
        int malformed = 0, missingSequences = 0, lastSequence = -1;
        bool opened = false, closed = false;
        foreach (string line in File.ReadLines(path))
        {
            try
            {
                using (var document = JsonDocument.Parse(line))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
                    foreach (string field in RequiredFields)
                        if (!document.RootElement.TryGetProperty(field, out _)) throw new InvalidDataException();
                }
                GodsEyeEvent e = JsonSerializer.Deserialize<GodsEyeEvent>(line) ?? throw new InvalidDataException();
                if (e.v != 1 || string.IsNullOrWhiteSpace(e.kind) || e.tick < 0 || e.wall_elapsed_ms < 0 || !double.IsFinite(e.wall_elapsed_ms)
                    || e.related == null || e.label == null || e.channel == null || e.detail == null || e.seq <= lastSequence
                    || !float.IsFinite(e.pos_x) || !float.IsFinite(e.pos_y) || !float.IsFinite(e.vel_x) || !float.IsFinite(e.vel_y)
                    || !float.IsFinite(e.expected_x) || !float.IsFinite(e.expected_y)) throw new InvalidDataException();
                if (e.seq > lastSequence + 1) missingSequences += e.seq - lastSequence - 1;
                lastSequence = e.seq;
                if (e.kind == "session") { opened = true; continue; }
                if (e.kind == "session-end") { closed = true; continue; }
                events.Add(e);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException) { malformed++; }
        }
        var log = new GodsEyeEventLog { Present = true, Events = events, Malformed = malformed, MissingSequences = missingSequences, Opened = opened, Closed = closed };
        cached = (full, info.LastWriteTimeUtc, info.Length, log);
        return log;
    }

    /// <summary>
    /// The value of <paramref name="key"/> in a <c>key=value;key=value</c> payload, first occurrence
    /// winning, or null. First-wins is deliberate: a control grant embeds whole control strings
    /// (<c>requested-controls=desired=1.00,-2.00</c>) whose own nested <c>=</c> must not shadow
    /// anything, and every identity key the reader uses is written before those strings. The control
    /// string carries no semicolon of its own today, which makes the hazard smaller rather than
    /// absent — the rule holds whatever shape the motor's controls take next.
    /// </summary>
    public static string? Field(string payload, string key)
    {
        int start = 0;
        while (start <= payload.Length)
        {
            int end = payload.IndexOf(';', start);
            if (end < 0) end = payload.Length;
            if (end - start > key.Length && string.CompareOrdinal(payload, start, key, 0, key.Length) == 0 && payload[start + key.Length] == '=')
                return payload.Substring(start + key.Length + 1, end - start - key.Length - 1);
            start = end + 1;
        }
        return null;
    }

    public static bool TryLong(string? value, out long result)
    {
        result = 0;
        return value != null && long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out result);
    }
}
