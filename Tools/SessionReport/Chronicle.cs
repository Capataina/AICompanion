#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Turns the high-rate record into a chronological account without promoting a measurement into
/// a story it cannot prove. A support change is reported as a support change; only a run of
/// observed slope supports is called an ascent or descent, and a life fall remains a life fall
/// unless the recorder supplied a hit event.
/// </summary>
public static class Chronicle
{
    private static readonly string[] Needs =
    {
        "wall_elapsed_ms", "sample_phase", "player_px", "player_vel", "player_ground", "player_liquid", "player_life", "player_hit", "npc_hit", "player_state", "player_activity", "player_support",
        "life", "npc_support", "control", "control_source", "state", "action", "request", "spot",
    };

    private const int DefaultLimit = 80;
    private const float MotionSpeed = 0.2f;

    public static bool IsAvailable(Session session, out string missing)
    {
        var absent = new List<string>();
        foreach (string name in Needs)
            if (!session.Has(name))
                absent.Add(name);
        missing = string.Join(", ", absent);
        return absent.Count == 0;
    }

    public static string Of(Session session, bool full)
    {
        if (!IsAvailable(session, out string missing))
            return $"chronology  unavailable — this record predates chronological coverage: {missing}\n";
        if (session.Count == 0)
            return "chronology  no samples were written; the schema is present but there is no elapsed interval to narrate\n";
        if (!TimesAdvance(session, out string badTime))
            return $"chronology  unavailable — wall_elapsed_ms is not monotonic ({badTime}); row order cannot be presented as time order\n";

        int limit = full ? int.MaxValue : DefaultLimit;
        var sb = new StringBuilder();
        string schema = session.Metadata.TryGetValue("schema", out string? value) ? value : "unlabelled";
        string started = session.Metadata.TryGetValue("started_utc", out string? utc) ? utc : "not recorded";
        sb.Append($"chronology  schema {schema}; session started UTC {started}; samples are {session["sample_phase"].Text[0]}\n");
        sb.Append("            elapsed wall time is observed by Stopwatch; game ticks are listed separately and are not converted to wall time\n");

        var events = Events(session);
        int shown = Math.Min(events.Count, limit);
        for (int i = 0; i < shown; i++)
            sb.Append("  ").Append(events[i]).Append('\n');
        if (events.Count > shown)
            sb.Append($"  … {events.Count - shown:n0} more chronological event(s) omitted; rerun with --timeline for the complete account\n");
        if (events.Count == 0)
            sb.Append("  no transitions were observed in the sampled columns\n");
        return sb.ToString();
    }

    private static List<string> Events(Session s)
    {
        var events = new List<string>();
        int begin = 0;
        string previousKey = Key(s, 0);
        for (int row = 1; row < s.Count; row++)
        {
            string key = Key(s, row);
            if (key == previousKey)
                continue;
            events.Add(Interval(s, begin, row - 1));
            AppendTransitions(s, row - 1, row, events);
            begin = row;
            previousKey = key;
        }
        events.Add(Interval(s, begin, s.Count - 1));
        AppendLackOfProgress(s, events);
        events.Sort(StringComparer.Ordinal);
        return events;
    }

    private static string Key(Session s, int row) => string.Join('|',
        Direction(s["player_vel"].Text[row]), s["player_ground"].Text[row], s["player_liquid"].Text[row], s["player_activity"].Text[row], s["player_support"].Text[row],
        Direction(CompanionVelocity(s, row)), s["npc_support"].Text[row], s["control"].Text[row], s["control_source"].Text[row], s["state"].Text[row], s["action"].Text[row], s["request"].Text[row], s["spot"].Text[row], s["player_hit"].Text[row]);

    private static string Interval(Session s, int first, int last)
    {
        string playerGround = s["player_ground"].Text[first] == "1" ? "grounded" : "airborne";
        string player = $"player {Direction(s["player_vel"].Text[first])}, {s["player_activity"].Text[first]}, {playerGround}, {s["player_liquid"].Text[first]}, support {s["player_support"].Text[first]}";
        string companion = $"companion {Direction(CompanionVelocity(s, first))}, {s["state"].Text[first]}, action {s["action"].Text[first]}, controls {s["control"].Text[first]} from {s["control_source"].Text[first]}, support {s["npc_support"].Text[first]}";
        string progress = Progress(s, first, last);
        return $"{When(s, first)}..{When(s, last)} (ticks {s.Tick(first)}..{s.Tick(last)}): observed {player}; observed {companion}{progress}";
    }

    private static void AppendTransitions(Session s, int before, int row, List<string> events)
    {
        string when = $"{When(s, row)} (tick {s.Tick(row)})";
        Transition(s, before, row, "player_liquid", "player entered", events, when);
        Transition(s, before, row, "player_support", "player support changed to", events, when);
        Transition(s, before, row, "npc_support", "companion support changed to", events, when);
        Transition(s, before, row, "player_activity", "player activity became", events, when);
        Transition(s, before, row, "control_source", "companion control source became", events, when);
        if (s.Has("edge_n", "edge_kind", "edge_from", "edge_to", "edge_outcome", "edge_took")
            && s["edge_n"].Text[before] != s["edge_n"].Text[row])
            events.Add($"{when}: recorded movement attempt {s["edge_kind"].Text[row]} {s["edge_from"].Text[row]} -> {s["edge_to"].Text[row]} ended {s["edge_outcome"].Text[row]} after {s["edge_took"].Text[row]} ticks");
        LifeFall(s, before, row, "player_life", "player", events, when);
        LifeFall(s, before, row, "life", "companion", events, when, "npc_hit");
        if (s["player_hit"].Text[row] != "-")
        {
            events.Add($"{when}: observed player hit event {s["player_hit"].Text[row]}");
            if (VelocityJump(s, "player_vel", before, row))
                events.Add($"{when}: inference from the observed hit event and velocity discontinuity: the hit likely changed player motion; the recorded source and knockback are above");
        }
        if (s["npc_hit"].Text[row] != "-")
        {
            events.Add($"{when}: observed companion hit event {s["npc_hit"].Text[row]}");
            if (VelocityJump(s, CompanionVelocityColumn(s), before, row))
                events.Add($"{when}: inference from the observed companion hit event and velocity discontinuity: the hit likely changed companion motion; the recorded knockback is above and attacker source is unrecorded");
        }

        string playerDirection = Direction(s["player_vel"].Text[row]);
        if (IsSlope(s["player_support"].Text[row]) && (playerDirection == "right" || playerDirection == "left"))
        {
            float dy = DeltaY(s, "player_px", before, row);
            if (MathF.Abs(dy) >= 0.1f)
                events.Add($"{when}: inference from observed slope support and position: player {(dy < 0 ? "ascended" : "descended")} a slope");
        }
    }

    private static void Transition(Session s, int a, int b, string column, string verb, List<string> events, string when)
    {
        if (s[column].Text[a] != s[column].Text[b])
            events.Add($"{when}: observed {verb} {s[column].Text[b]}");
    }

    private static void LifeFall(Session s, int a, int b, string column, string who, List<string> events, string when, string? hitColumn = null)
    {
        float was = s[column].Number[a], now = s[column].Number[b];
        if (!float.IsNaN(was) && !float.IsNaN(now) && now < was && (hitColumn == null || s[hitColumn].Text[b] == "-"))
            events.Add($"{when}: observed {who} life fell {was:0}->{now:0}; cause and knockback are unmeasured unless a recorded hit event names them");
    }

    private static string Direction(string velocity)
    {
        if (!Session.TryPair(velocity, out float x, out _))
            return "unknown motion";
        return x > MotionSpeed ? "right" : x < -MotionSpeed ? "left" : "still";
    }

    private static float DeltaY(Session s, string column, int a, int b)
        => Session.TryPair(s[column].Text[a], out _, out float ay) && Session.TryPair(s[column].Text[b], out _, out float by) ? by - ay : 0f;

    private static bool VelocityJump(Session s, string column, int a, int b)
    {
        if (!Session.TryPair(s[column].Text[a], out float ax, out float ay) || !Session.TryPair(s[column].Text[b], out float bx, out float by))
            return false;
        float dx = bx - ax, dy = by - ay;
        return dx * dx + dy * dy >= 1f;
    }

    private static bool IsSlope(string support) => support is "slope-lower-left" or "slope-lower-right" or "platform-slope-lower-left" or "platform-slope-lower-right";

    private static string Progress(Session s, int first, int last)
    {
        if (first == last || s["spot"].Text[first] != s["spot"].Text[last] || !Session.TryPair(s["spot"].Text[first], out float sx, out float sy)
            || !CompanionPosition(s, first, out float ax, out float ay) || !CompanionPosition(s, last, out float bx, out float by))
            return "";
        float start = MathF.Sqrt((ax - sx) * (ax - sx) + (ay - sy) * (ay - sy));
        float end = MathF.Sqrt((bx - sx) * (bx - sx) + (by - sy) * (by - sy));
        return end < start - 0.1f
            ? $"; observed companion net progress toward its recorded spot: {start:0.0}->{end:0.0} px"
            : "";
    }

    private static string CompanionVelocity(Session s, int row) => s.Has("observed_vel") ? s["observed_vel"].Text[row] : s["npc_vel"].Text[row];

    private static string CompanionVelocityColumn(Session s) => s.Has("observed_vel") ? "observed_vel" : "npc_vel";

    /// <summary>
    /// A stall is not stillness. It needs one stable target, a control request that could move the
    /// body, no reflex ownership, and entry observations that do not move for the whole window.
    /// That leaves a deliberate hold, a completed arrival and a dodge outside the inference.
    /// </summary>
    private static void AppendLackOfProgress(Session s, List<string> events)
    {
        const int MinTicks = 120;
        int start = 0;
        while (start < s.Count)
        {
            if (!StallCandidate(s, start)) { start++; continue; }
            int end = start;
            while (end + 1 < s.Count && StallCandidate(s, end + 1) && s["spot"].Text[end + 1] == s["spot"].Text[start]
                && CompanionPosition(s, start, out float x0, out float y0) && CompanionPosition(s, end + 1, out float x1, out float y1)
                && MathF.Abs(x1 - x0) < 0.1f && MathF.Abs(y1 - y0) < 0.1f)
                end++;
            if (s.Tick(end) - s.Tick(start) >= MinTicks)
                events.Add($"{When(s, start)}..{When(s, end)} (ticks {s.Tick(start)}..{s.Tick(end)}): inferred lack of progress — the observed-before-AI body stayed at {PositionText(s, start)} while {s["control"].Text[start]} from {s["control_source"].Text[start]} requested movement toward recorded spot {s["spot"].Text[start]}");
            start = Math.Max(start + 1, end + 1);
        }
    }

    private static bool StallCandidate(Session s, int row)
        => s["state"].Text[row] == "up" && s["spot"].Text[row] != "-" && s["control_source"].Text[row] != "reflex" && IsActiveControl(s["control"].Text[row]);

    private static bool IsActiveControl(string control)
        => !control.Contains("move=0.00", StringComparison.Ordinal) || control.Contains("jump=1", StringComparison.Ordinal) || control.Contains("fall=1", StringComparison.Ordinal);

    private static string PositionText(Session s, int row)
        => CompanionPosition(s, row, out float x, out float y) ? $"{x:0.00},{y:0.00}" : "an unavailable position";

    private static bool CompanionPosition(Session s, int row, out float x, out float y)
    {
        if (s.Has("observed_left", "observed_bottom", "npc_width"))
        {
            x = s["observed_left"].Number[row] + s["npc_width"].Number[row] / 2f;
            y = s["observed_bottom"].Number[row];
            return !float.IsNaN(x) && !float.IsNaN(y);
        }
        return Session.TryPair(s["npc_px"].Text[row], out x, out y);
    }

    private static bool TimesAdvance(Session s, out string bad)
    {
        float previous = s["wall_elapsed_ms"].Number[0];
        if (float.IsNaN(previous))
        {
            bad = $"row 0 is {s["wall_elapsed_ms"].Text[0]}";
            return false;
        }
        for (int row = 1; row < s.Count; row++)
        {
            float current = s["wall_elapsed_ms"].Number[row];
            if (float.IsNaN(current) || current < previous)
            {
                bad = $"rows {row - 1}/{row} are {previous:0.000}/{s["wall_elapsed_ms"].Text[row]} ms";
                return false;
            }
            previous = current;
        }
        bad = "";
        return true;
    }

    private static string When(Session s, int row)
    {
        float elapsed = s["wall_elapsed_ms"].Number[row];
        if (float.IsNaN(elapsed))
            return "wall time unavailable";
        return TimeSpan.FromMilliseconds(elapsed).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }
}
