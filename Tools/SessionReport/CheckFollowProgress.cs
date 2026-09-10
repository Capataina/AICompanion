#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether a recorded follow objective remains unsatisfied without consuming a route step.
/// Euclidean distance is deliberately not the progress measure: a valid C-turn can first move
/// away from the player. Remaining ETA is evidence to display, never progress evidence by itself,
/// because an active traversal's estimate decreases as time passes even if the body is frozen.
/// </summary>
public sealed class FollowingMakesRouteProgress : ICheck
{
    /// <summary>Two seconds excludes a normal short replan while retaining a sustained wrong-floor run.</summary>
    private const int MinUnsatisfiedTicks = 120;

    private const string CheckName = "did an unsatisfied follow objective make route progress";
    public string Name => CheckName;
    public string[] Needs => new[]
    {
        "request", "follow_objective_valid", "follow_dx", "follow_dy", "follow_reason",
        "route_search_id", "route_attempt_id", "route_remaining_ticks", "path_at", "action", "recovery_active", "brain_fresh"
    };

    public IEnumerable<Finding> Run(Session session)
    {
        Column request = session["request"], satisfied = session["follow_objective_valid"];
        Column dx = session["follow_dx"], dy = session["follow_dy"], reason = session["follow_reason"];
        Column search = session["route_search_id"], attempt = session["route_attempt_id"];
        Column remaining = session["route_remaining_ticks"], completed = session["path_at"], action = session["action"], recovery = session["recovery_active"], fresh = session["brain_fresh"];
        int start = -1;
        for (int i = 0; i < session.Count; i++)
        {
            bool active = request.Text[i] == "WithPlayer" && satisfied.Number[i] == 0f
                && action.Text[i] == "walk-with" && recovery.Number[i] == 0f && fresh.Number[i] == 1f;
            if (!active)
            {
                if (start >= 0) foreach (Finding finding in ReportWindow(session, start, i - 1, dx, dy, reason, search, attempt, remaining, completed)) yield return finding;
                start = -1;
                continue;
            }
            bool stepCompleted = i > 0 && completed.Number[i] > completed.Number[i - 1];
            if (start < 0 || stepCompleted)
            {
                if (start >= 0) foreach (Finding finding in ReportWindow(session, start, i - 1, dx, dy, reason, search, attempt, remaining, completed)) yield return finding;
                // A completed waypoint is physical progress. Resetting the watch here prevents a
                // single early step from hiding a later multi-minute wrong-floor stretch.
                start = i;
                continue;
            }
        }
        if (start >= 0) foreach (Finding finding in ReportWindow(session, start, session.Count - 1, dx, dy, reason, search, attempt, remaining, completed)) yield return finding;
    }

    private static IEnumerable<Finding> ReportWindow(Session session, int start, int end, Column dx, Column dy, Column reason, Column search, Column attempt, Column remaining, Column completed)
    {
        int rows = end - start + 1;
        if (rows < MinUnsatisfiedTicks)
            yield break;
            float firstGap = MathF.Abs(dx.Number[start]) + MathF.Abs(dy.Number[start]);
            float lastGap = MathF.Abs(dx.Number[end]) + MathF.Abs(dy.Number[end]);
            bool wrongDirection = lastGap > firstGap;
            string classification = wrongDirection ? "wrong-direction or wrong-floor movement" : "unresolved follow route";
            yield return new Finding(
                Severity.Potential,
                CheckName,
                $"follow remained unsatisfied for {rows} ticks without completing a route step ({classification})",
                $"The action remained walk-with and request remained WithPlayer while follow reason was "
                    + $"{FindStretches.Summarise(reason, new Stretch(start, end), 3)}. Navigator identity began search {search.Number[start]:0}, "
                    + $"attempt {attempt.Number[start]:0}, completed step {completed.Number[start]:0}, "
                    + $"remaining estimate {remaining.Number[start]:0.0} ticks and ended search {search.Number[end]:0}, "
                    + $"attempt {attempt.Number[end]:0}, completed step {completed.Number[end]:0}, "
                    + $"remaining estimate {remaining.Number[end]:0.0} ticks. Follow gap changed {firstGap:0.0}->{lastGap:0.0} px. "
                    + "A countdown in remaining estimate alone is not credited, because time can pass while the body is frozen. Recovery flight rows are excluded. Replay the captured terrain and inspect the route identities to separate a wrong floor from a planner that never resolved.",
                session.Tick(start), session.Tick(end), rows);
    }
}

/// <summary>
/// Records response latency as observed wall time from a moving player's distant state to the
/// first follow selection. It remains an observation, because work, guard and recovery can each
/// legitimately delay ordinary following.
/// </summary>
public sealed class FollowingRespondsAfterDeparture : ICheck
{
    /// <summary>Fifty tiles is outside loose companionship and avoids treating calm-band drift as departure.</summary>
    private const float DeparturePixels = 800f;

    public string Name => "how long following took to respond after the player departed";
    public string[] Needs => new[] { "npc_px", "player_px", "player_vel", "action", "wall_elapsed_ms", "brain_fresh" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column npc = session["npc_px"], player = session["player_px"], velocity = session["player_vel"];
        Column action = session["action"], elapsed = session["wall_elapsed_ms"];
        int start = -1;
        bool departureLatched = false;
        for (int i = 0; i < session.Count; i++)
        {
            bool distant = Session.TryPair(npc.Text[i], out float nx, out float ny)
                && Session.TryPair(player.Text[i], out float px, out float py)
                && MathF.Abs(px - nx) + MathF.Abs(py - ny) >= DeparturePixels;
            bool moving = Session.TryPair(velocity.Text[i], out float vx, out float vy) && (vx != 0f || vy != 0f);
            if (!distant)
            {
                departureLatched = false;
                start = -1;
            }
            if (start < 0 && !departureLatched && distant && moving)
            {
                start = i;
                departureLatched = true;
            }
            if (start < 0)
                continue;
            if (action.Text[i] == "walk-with" && session["brain_fresh"].Number[i] == 1f)
            {
                double latency = elapsed.Number[i] - elapsed.Number[start];
                yield return new Finding(Severity.Oddity, Name,
                    $"following was first selected {latency:0.0} ms after an observed distant moving-player state",
                    $"The observed state began at tick {session.Tick(start)} and walk-with first appeared at tick {session.Tick(i)}. "
                        + "This is observed timing, not frame cost and not proof of an error: guard, work, recovery flight and an existing valid route can all explain a delay.",
                    session.Tick(start), session.Tick(i), i - start + 1);
                start = -1;
            }
        }
        if (start >= 0)
            yield return new Finding(Severity.Oddity, Name,
                "the player stayed distant and moving through the recorded end without a follow selection",
                $"The observed distant moving-player state began at tick {session.Tick(start)}. The capture ended before walk-with appeared, so this names missing response evidence rather than a definitive failure; guard, work, recovery or capture truncation could explain it.",
                session.Tick(start), session.Tick(session.Count - 1), session.Count - start);
    }
}
