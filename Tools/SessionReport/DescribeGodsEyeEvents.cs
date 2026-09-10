#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Linq;

namespace AICompanion.Tools.SessionReport;

/// <summary>Reads the sparse event sibling without treating a missing or malformed event line as a clean session.</summary>
public static class DescribeGodsEyeEvents
{
    public static string Of(string tsvPath, bool full = false)
    {
        string path = Path.ChangeExtension(tsvPath, null) + "-events.jsonl";
        if (!File.Exists(path)) return "events    unavailable — this session predates the God’s Eye event stream\n";
        int valid = 0, malformed = 0; var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        var chronology = new List<EventLine>();
        var lifecycle = new List<EventLine>();
        bool opened = false, closed = false;
        int lastSequence = -1, missingSequences = 0;
        foreach (string line in File.ReadLines(path))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
                foreach (string field in new[] { "v", "seq", "tick", "wall_elapsed_ms", "kind", "subject", "related", "label", "channel", "pos_x", "pos_y", "vel_x", "vel_y", "expected_x", "expected_y", "amount", "detail" })
                    if (!document.RootElement.TryGetProperty(field, out _)) throw new InvalidDataException();
                EventLine e = JsonSerializer.Deserialize<EventLine>(line) ?? throw new InvalidDataException();
                if (e.v != 1 || string.IsNullOrWhiteSpace(e.kind) || e.tick < 0 || e.wall_elapsed_ms < 0 || !double.IsFinite(e.wall_elapsed_ms)
                    || e.related == null || e.label == null || e.channel == null || e.detail == null || e.seq <= lastSequence
                    || !float.IsFinite(e.pos_x) || !float.IsFinite(e.pos_y) || !float.IsFinite(e.vel_x) || !float.IsFinite(e.vel_y)
                    || !float.IsFinite(e.expected_x) || !float.IsFinite(e.expected_y)) throw new InvalidDataException();
                if (e.seq > lastSequence + 1) missingSequences += e.seq - lastSequence - 1;
                lastSequence = e.seq;
                string kind = e.kind;
                if (kind == "session") { opened = true; continue; }
                if (kind == "session-end") { closed = true; continue; }
                valid++; kinds[kind] = kinds.TryGetValue(kind, out int count) ? count + 1 : 1;
                chronology.Add(e);
                if (kind == "lifecycle") lifecycle.Add(e);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException) { malformed++; }
        }
        var text = new StringBuilder($"events    {valid:n0} occurrence record(s)" + (malformed == 0 ? "\n" : $", {malformed:n0} malformed line(s)\n"));
        text.Append($"coverage  start={(opened ? "recorded" : "missing")}; end={(closed ? "normal close" : "missing — active or interrupted capture")}; missing-sequences={missingSequences}; terrain is sampled locally, uncaptured terrain remains unknown\n");
        if (lifecycle.Count == 0)
            text.Append("lifecycle unavailable — this session predates lifecycle callback evidence\n");
        else
            foreach (EventLine e in lifecycle.OrderBy(e => e.seq))
                text.Append($"lifecycle {e.label}: {e.detail}\n");
        foreach (var pair in kinds) text.Append($"  {pair.Key} {pair.Value:n0}\n");
        chronology.Sort((a, b) => a.wall_elapsed_ms != b.wall_elapsed_ms ? a.wall_elapsed_ms.CompareTo(b.wall_elapsed_ms) : a.seq.CompareTo(b.seq));
        var launches = new Dictionary<int, EventLine>();
        var contacted = new HashSet<int>();
        var rejections = new HashSet<string>(StringComparer.Ordinal);
        foreach (EventLine e in chronology)
        {
            if (e.kind == "shot" && e.channel.StartsWith("projectile=", StringComparison.Ordinal) && int.TryParse(e.channel[11..], out int id)) launches[id] = e;
            if (e.kind == "projectile-terrain-hit" && !contacted.Contains(e.subject) && launches.TryGetValue(e.subject, out EventLine? shot) && shot != null)
                text.Append($"causal    {TimeSpan.FromMilliseconds(e.wall_elapsed_ms):hh\\:mm\\:ss\\.fff} tick {e.tick}: {shot.label} projectile {e.subject} intended target {shot.related} at {shot.expected_x:0.0},{shot.expected_y:0.0} from {shot.pos_x:0.0},{shot.pos_y:0.0} met terrain at {e.pos_x:0.0},{e.pos_y:0.0} before any recorded enemy contact (observed obstruction; cause requires the recorded trajectory and terrain)\n");
            if (e.kind == "projectile-enemy-hit" && launches.TryGetValue(e.subject, out EventLine? hitShot) && hitShot != null)
                contacted.Add(e.subject);
            if (e.kind == "movement-state" && TryRejection(e.detail, out string rejected) && rejections.Add(rejected))
                text.Append($"causal    {TimeSpan.FromMilliseconds(e.wall_elapsed_ms):hh\\:mm\\:ss\\.fff} tick {e.tick}: recorded movement rejection at {e.pos_x:0.0},{e.pos_y:0.0}; {Abbreviate(rejected, 280)} (observed planner/execution evidence; its underlying terrain cause remains unknown unless a matching terrain snapshot covers it)\n");
        }
        // The default spans the entire run: aggregate each time window rather than showing only
        // the first few seconds of a long session. The full trace preserves every causal record.
        if (!full)
        {
            foreach (var episode in chronology.GroupBy(e => (long)(e.wall_elapsed_ms / 30000d)))
            {
                var meaningful = episode.Where(e => e.kind is "decision" or "navigation-state" or "pickup" or "npc-death" or "player-damage" or "npc-damage" or "movement-state" or "shot").ToList();
                if (meaningful.Count == 0) continue;
                var last = meaningful[^1];
                text.Append($"  {TimeSpan.FromSeconds(episode.Key * 30):hh\\:mm\\:ss}–{TimeSpan.FromSeconds((episode.Key + 1) * 30):hh\\:mm\\:ss}: "
                    + string.Join(", ", meaningful.GroupBy(e => e.kind).Select(group => $"{group.Count()} {group.Key}"))
                    + $"; latest position {last.pos_x:0.0},{last.pos_y:0.0}\n");
                var decision = meaningful.LastOrDefault(e => e.kind == "decision");
                if (decision != null) text.Append($"    intention {decision.label}, request {decision.channel}; {Abbreviate(decision.detail, 700)}\n");
                var movement = meaningful.LastOrDefault(e => e.kind == "movement-state");
                if (movement != null) text.Append($"    movement {MovementSummary(movement.detail)}\n");
                var navigation = meaningful.LastOrDefault(e => e.kind == "navigation-state");
                if (navigation != null) text.Append($"    decision/search {navigation.label}, request {navigation.channel}; {Abbreviate(navigation.detail, 700)}\n");
            }
            return text.Append("  Use --timeline for every occurrence record; TSV chronology below supplies continuous player and companion motion.\n").ToString();
        }
        int shown = chronology.Count;
        for (int i = 0; i < shown; i++)
        {
            var e = chronology[i]; var time = TimeSpan.FromMilliseconds(e.wall_elapsed_ms);
            text.Append($"  {time:hh\\:mm\\:ss\\.fff} tick {e.tick:n0} {e.kind} subject={e.subject} related={e.related} {e.label} channel={e.channel} pos={e.pos_x:0.0},{e.pos_y:0.0} velocity={e.vel_x:0.0},{e.vel_y:0.0} expected={e.expected_x:0.0},{e.expected_y:0.0}" + (e.amount == 0 ? "" : $" amount={e.amount}") + (string.IsNullOrEmpty(e.detail) ? "\n" : $" {e.detail}\n"));
        }
        return text.ToString();
    }

    private static string MovementSummary(string detail)
    {
        const string rejection = ";last-rejection=";
        int index = detail.IndexOf(rejection, StringComparison.Ordinal);
        if (index < 0) return Abbreviate(detail, 700);
        string before = detail[..index];
        string value = detail[(index + rejection.Length)..];
        if (string.IsNullOrEmpty(value)) return before + "; last rejection none";
        int reason = value.LastIndexOf("Reason = ", StringComparison.Ordinal);
        return reason >= 0
            ? before + "; last rejection recorded (" + Abbreviate(value[reason..], 160) + ")"
            : before + "; last rejection recorded (detail retained in --timeline)";
    }

    private static string Abbreviate(string value, int maximum)
        => value.Length <= maximum ? value : value[..maximum] + "… (full detail in --timeline)";

    private static bool TryRejection(string detail, out string rejection)
    {
        const string marker = "last-rejection=";
        int start = detail.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) { rejection = ""; return false; }
        rejection = detail[(start + marker.Length)..];
        return !string.IsNullOrEmpty(rejection);
    }

    private sealed record EventLine(int v, int seq, long tick, double wall_elapsed_ms, string kind, int subject, string related, string label, string channel,
        float pos_x, float pos_y, float vel_x, float vel_y, float expected_x, float expected_y, int amount, string detail);
}
