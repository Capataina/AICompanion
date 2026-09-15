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
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(tsvPath);
        if (!log.Present) return "events    unavailable — this session predates the God’s Eye event stream\n";
        int valid = log.Events.Count, malformed = log.Malformed, missingSequences = log.MissingSequences;
        bool opened = log.Opened, closed = log.Closed;
        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (GodsEyeEvent e in log.Events) kinds[e.kind] = kinds.TryGetValue(e.kind, out int count) ? count + 1 : 1;
        var chronology = new List<GodsEyeEvent>(log.Events);
        var lifecycle = chronology.Where(e => e.kind == "lifecycle").ToList();
        var text = new StringBuilder($"events    {valid:n0} occurrence record(s)" + (malformed == 0 ? "\n" : $", {malformed:n0} malformed line(s)\n"));
        text.Append($"coverage  start={(opened ? "recorded" : "missing")}; end={(closed ? "normal close" : "missing — active or interrupted capture")}; missing-sequences={missingSequences}; terrain is sampled locally, uncaptured terrain remains unknown\n");
        if (lifecycle.Count == 0)
            text.Append("lifecycle unavailable — this session predates lifecycle callback evidence\n");
        else
            foreach (GodsEyeEvent e in lifecycle.OrderBy(e => e.seq))
                text.Append($"lifecycle {e.label}: {e.detail}\n");
        foreach (var pair in kinds) text.Append($"  {pair.Key} {pair.Value:n0}\n");
        chronology.Sort((a, b) => a.wall_elapsed_ms != b.wall_elapsed_ms ? a.wall_elapsed_ms.CompareTo(b.wall_elapsed_ms) : a.seq.CompareTo(b.seq));
        var launches = new Dictionary<int, GodsEyeEvent>();
        var contacted = new HashSet<int>();
        var rejections = new HashSet<string>(StringComparer.Ordinal);
        foreach (GodsEyeEvent e in chronology)
        {
            if (e.kind == "shot" && e.channel.StartsWith("projectile=", StringComparison.Ordinal) && int.TryParse(e.channel[11..], out int id)) launches[id] = e;
            if (e.kind == "projectile-terrain-hit" && !contacted.Contains(e.subject) && launches.TryGetValue(e.subject, out GodsEyeEvent? shot) && shot != null)
                text.Append($"causal    {TimeSpan.FromMilliseconds(e.wall_elapsed_ms):hh\\:mm\\:ss\\.fff} tick {e.tick}: {shot.label} projectile {e.subject} intended target {shot.related} at {shot.expected_x:0.0},{shot.expected_y:0.0} from {shot.pos_x:0.0},{shot.pos_y:0.0} met terrain at {e.pos_x:0.0},{e.pos_y:0.0} before any recorded enemy contact (observed obstruction; cause requires the recorded trajectory and terrain)\n");
            if (e.kind == "projectile-enemy-hit" && launches.TryGetValue(e.subject, out GodsEyeEvent? hitShot) && hitShot != null)
                contacted.Add(e.subject);
        }
        // The default spans the entire run: aggregate each time window rather than showing only
        // the first few seconds of a long session. The full trace preserves every causal record.
        if (!full)
        {
            foreach (var episode in chronology.GroupBy(e => (long)(e.wall_elapsed_ms / 30000d)))
            {
                var meaningful = episode.Where(e => e.kind is "decision" or "navigation-state" or "pickup" or "npc-death" or "player-damage" or "npc-damage" or "movement-state" or "shot" or "tool-effect" or "activity-state" or "control-grant" or "method-assessment" or "attempt-outcome").ToList();
                if (meaningful.Count == 0) continue;
                var last = meaningful[^1];
                text.Append($"  {TimeSpan.FromSeconds(episode.Key * 30):hh\\:mm\\:ss}–{TimeSpan.FromSeconds((episode.Key + 1) * 30):hh\\:mm\\:ss}: "
                    + string.Join(", ", meaningful.GroupBy(e => e.kind).Select(group => $"{group.Count()} {group.Key}"))
                    + $"; latest position {last.pos_x:0.0},{last.pos_y:0.0}\n");
                var decision = meaningful.LastOrDefault(e => e.kind == "decision");
                if (decision != null) text.Append($"    intention {decision.label}, request {decision.channel}; {Abbreviate(decision.detail, 700)}\n");
                // Keep both rejection and admission when the method changes inside one window.
                // These are recorded assessments, not a verdict about execution or usefulness.
                foreach (var methods in meaningful.Where(e => e.kind == "method-assessment")
                    .GroupBy(e => (e.subject, e.label, e.channel)))
                {
                    var method = methods.Last();
                    text.Append($"    method {method.label}, {method.channel}: {methods.Count()} assessment(s); latest subject={method.subject} related={method.related}; {Abbreviate(method.detail, 700)}\n");
                }
                var movement = meaningful.LastOrDefault(e => e.kind == "movement-state");
                if (movement != null) text.Append($"    movement {MovementSummary(movement.detail)}\n");
                var navigation = meaningful.LastOrDefault(e => e.kind == "navigation-state");
                if (navigation != null) text.Append($"    decision/search {navigation.label}, request {navigation.channel}; {Abbreviate(navigation.detail, 700)}\n");
                var tool = meaningful.LastOrDefault(e => e.kind == "tool-effect");
                if (tool != null) text.Append($"    tool {tool.label}, {tool.channel}; {Abbreviate(tool.detail, 700)}\n");
                var activity = meaningful.LastOrDefault(e => e.kind == "activity-state");
                if (activity != null) text.Append($"    activity {activity.label}; {Abbreviate(activity.detail, 700)}\n");
                // Every distinct outcome kind stays visible, so a completed attempt later in the window
                // cannot hide the interruption or failure that preceded it.
                foreach (var attempts in meaningful.Where(e => e.kind == "attempt-outcome").GroupBy(e => (e.label, e.channel)))
                {
                    var attempt = attempts.Last();
                    text.Append($"    attempt {attempt.label}, {attempt.channel}: {attempts.Count()} attempt(s); latest {Abbreviate(attempt.detail, 400)}\n");
                }
                var grant = meaningful.LastOrDefault(e => e.kind == "control-grant");
                if (grant != null) text.Append($"    control grant {grant.label}, hand {grant.channel}; {Abbreviate(grant.detail, 700)}\n");
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

    /// <summary>
    /// The navigator's sampled state, as the producer writes it: status, search stop, goal, the route's
    /// points and the segment in hand, the progress reason, and the stuck counters. There is no last
    /// rejection to unpack any more — a refusal was the walking body's macro proof turning a step down,
    /// and the orb's search either returns a corridor or does not.
    /// </summary>
    private static string MovementSummary(string detail) => Abbreviate(detail, 700);

    private static string Abbreviate(string value, int maximum)
        => value.Length <= maximum ? value : value[..maximum] + "… (full detail in --timeline)";
}
