#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether a recorded follow objective remains unsatisfied without making progress along its route.
/// Euclidean distance is deliberately not the progress measure: a valid C-turn can first move
/// away from the player. Remaining ETA is evidence to display, never progress evidence by itself,
/// because an active traversal's estimate decreases as time passes even if the body is frozen.
/// Two signals count as progress, and the second exists because the first alone cannot see a
/// straight flight: the orb's route is smoothed by skipping up to a lookahead of raw corners into
/// one segment, so a body crossing an open span holds one segment for the whole crossing and
/// <c>route_index</c> never moves. <c>route_remaining_px</c> is the body's own projection onto
/// that segment, so it falls only when the body moves, and it is credited only while the search
/// identity is unchanged, because a replan can shorten the route without the body having travelled.
/// </summary>
public sealed class FollowingMakesRouteProgress : ICheck
{
    /// <summary>Two seconds excludes a normal short replan while retaining a sustained wrong-floor run.</summary>
    private const int MinUnsatisfiedTicks = 120;

    /// <summary>
    /// One tile, measured from the window's own start rather than from the previous row. A per-row
    /// comparison would credit the projection's own jitter on a body that is not moving, which
    /// turns this check silent; a tile is far above that jitter and far below any real approach.
    /// </summary>
    private const float ProgressPixels = 16f;

    private const string CheckName = "did an unsatisfied follow objective make route progress";
    public string Name => CheckName;
    public string[] Needs => new[]
    {
        "request", "follow_objective_valid", "follow_dx", "follow_dy", "follow_reason",
        "route_search_id", "route_attempt_id", "route_remaining_ticks", "route_remaining_px", "route_index", "action", "recovery_active", "brain_fresh"
    };

    public IEnumerable<Finding> Run(Session session)
    {
        Column request = session["request"], satisfied = session["follow_objective_valid"];
        Column dx = session["follow_dx"], dy = session["follow_dy"], reason = session["follow_reason"];
        Column search = session["route_search_id"], attempt = session["route_attempt_id"];
        // `route_index` is the segment the body is on, so it rising is a segment consumed — the orb's
        // form of the completed waypoint this watch resets on. It is read for progress only and never
        // compared against `route_points`, because the index stops one short of the count by design.
        Column remaining = session["route_remaining_ticks"], completed = session["route_index"], action = session["action"], recovery = session["recovery_active"], fresh = session["brain_fresh"];
        Column remainingPx = session["route_remaining_px"];
        int start = -1;
        for (int i = 0; i < session.Count; i++)
        {
            bool active = request.Text[i] == "WithPlayer" && satisfied.Number[i] == 0f
                && action.Text[i] is "walk-with" or "keep-company" && recovery.Number[i] == 0f && fresh.Number[i] == 1f;
            if (!active)
            {
                if (start >= 0) foreach (Finding finding in ReportWindow(session, start, i - 1, dx, dy, reason, search, attempt, remaining, completed)) yield return finding;
                start = -1;
                continue;
            }
            bool stepCompleted = i > 0 && completed.Number[i] > completed.Number[i - 1];
            // A NaN on either side makes this false, so a capture that stopped writing the column
            // reports the window rather than silently crediting progress it cannot see.
            bool closed = start >= 0
                && search.Number[i] == search.Number[start]
                && remainingPx.Number[start] - remainingPx.Number[i] > ProgressPixels;
            if (start < 0 || stepCompleted || closed)
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
                $"The companionship activity kept requesting WithPlayer while follow reason was "
                    + $"{FindStretches.Summarise(reason, new Stretch(start, end), 3)}. Navigator identity began search {search.Number[start]:0}, "
                    + $"attempt {attempt.Number[start]:0}, route segment {completed.Number[start]:0}, "
                    + $"remaining estimate {remaining.Number[start]:0.0} ticks and ended search {search.Number[end]:0}, "
                    + $"attempt {attempt.Number[end]:0}, route segment {completed.Number[end]:0}, "
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
            bool reunion = action.Text[i] == "walk-with"
                || action.Text[i] == "keep-company" && session.Find("request")?.Text[i] == "WithPlayer";
            if (reunion && session["brain_fresh"].Number[i] == 1f)
            {
                double latency = elapsed.Number[i] - elapsed.Number[start];
                yield return new Finding(Severity.Oddity, Name,
                    $"following was first selected {latency:0.0} ms after an observed distant moving-player state",
                    $"The observed state began at tick {session.Tick(start)} and a companionship reunion first appeared at tick {session.Tick(i)}. "
                        + "This is observed timing, not frame cost and not proof of an error: guard, work, recovery flight and an existing valid route can all explain a delay.",
                    session.Tick(start), session.Tick(i), i - start + 1);
                start = -1;
            }
        }
        if (start >= 0)
            yield return new Finding(Severity.Oddity, Name,
                "the player stayed distant and moving through the recorded end without a follow selection",
                $"The observed distant moving-player state began at tick {session.Tick(start)}. The capture ended before a companionship reunion appeared, so this names missing response evidence rather than a definitive failure; guard, work, recovery or capture truncation could explain it.",
                session.Tick(start), session.Tick(session.Count - 1), session.Count - start);
    }
}
