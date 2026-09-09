#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether the body was ever held in place by something other than the world. The engine cannot
/// do this to a body it is integrating: <c>Collision_MoveWhileDry</c> does
/// <c>position += velocity</c> unconditionally and sets a collide flag only when its tile
/// collision changed the velocity it was handed, so a real velocity beside no displacement means
/// something wrote the position back during the AI phase, where the engine's own displacement
/// measure cannot see it.
///
/// This is the check the shaft freeze of 2026-09-09 needed and nobody had. The cause turned out to
/// be our own motor calling <c>Collision.StepUp</c> with the platform permission hard-coded on, so
/// it climbed the platform it was falling through every tick; finding it took reading three columns
/// by hand against a decompiled collision routine, because the state read as airborne (ground is
/// <c>velocity.Y == 0</c>) and therefore registered as neither stuck nor standing.
/// </summary>
public sealed class TheBodyIsNeverPinned : ICheck
{
    /// <summary>A quarter of a second. Shorter is a wall met on the tick it is met, or a hair of velocity on a slope's diagonal.</summary>
    private const int MinPinnedTicks = 15;

    public string Name => "was the body ever held in place with a velocity it never spent";
    public string[] Needs => new[] { "pinned", "npc_vel" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column pinned = session["pinned"], velocity = session["npc_vel"];
        Column? action = session.Find("action");
        Column? press = session.Find("press");
        Column? descend = session.Find("descend");

        foreach (var stretch in FindStretches.Where(session.Count, i => pinned.Number[i] >= MinPinnedTicks, 1, allowGap: 30))
        {
            float worst = FindStretches.Max(pinned, stretch);
            string doing = action == null ? "" : $" The action was {FindStretches.Summarise(action, stretch, 3)}.";
            string intent = press == null || descend == null
                ? ""
                : $" The press read {FindStretches.Summarise(press, stretch, 2)} and the descend bit {FindStretches.Summarise(descend, stretch, 2)}.";
            yield return new Finding(
                Severity.Definitive,
                Name,
                $"the body was pinned for up to {worst:n0} consecutive ticks",
                $"Velocity over the stretch was {FindStretches.Summarise(velocity, stretch, 3)} while the position did not change. "
                    + "A body the engine is integrating cannot hold a velocity and not move, so something wrote its position "
                    + "back inside the AI phase. The motor's own step-up did exactly this before the guard of 2026-09-09; "
                    + "another writer of npc.position during AI is the thing to look for."
                    + doing + intent,
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.End - stretch.Start + 1);
        }
    }
}

/// <summary>
/// Whether the rule that proves every planned move and the engine that performs it still agree.
/// Every edge the planner offers is simulated with <c>BodyMotion.Step</c> and then performed by the
/// motor against the game's own collision, so the two being different rules is the failure that
/// produces a body standing still holding a valid path — the defect of runs 3, 5, 6 and 7
/// (2026-09-08) every time, and the reason the shaft replayed clean in 264 ticks while freezing in
/// play: the offline platform lift required a grounded body and the motor's ran on any
/// non-negative vertical velocity.
///
/// The mod measures it live rather than by replaying a recording, because a replay only ever tests
/// the tiles the recording happened to visit. Some disagreement is expected and is not a defect:
/// the offline rule approximates slopes, half blocks and liquid movement. So the distribution is
/// always reported as a baseline and only a large sustained gap is called wrong, which is the
/// honest shape while nobody yet knows what normal looks like — the first session with this column
/// is what establishes that.
/// </summary>
public sealed class TheTwoBodiesAgree : ICheck
{
    /// <summary>Beyond this the two rules are describing different moves rather than rounding differently, in px.</summary>
    private const float SeriousPx = 6f;

    /// <summary>How long a serious gap must hold before it is a disagreement rather than one odd tick.</summary>
    private const int MinTicks = 30;

    public string Name => "do the simulated body and the engine still agree";
    public string[] Needs => new[] { "diverge", "diverge_valid", "diverge_invalid_reason" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column diverge = session["diverge"], valid = session["diverge_valid"];
        Column? kind = session.Find("next_kind");

        var readings = diverge.Number.Where((v, i) => valid.Number[i] == 1f && !float.IsNaN(v)).ToArray();
        if (readings.Length == 0)
            yield break;
        Array.Sort(readings);
        float median = readings[readings.Length / 2];
        float p99 = readings[(int)(readings.Length * 0.99f)];
        yield return new Finding(
            Severity.Oddity,
            Name,
            $"the two motion rules differed by a median of {median:0.00} px and a 99th percentile of {p99:0.00} px",
            "This is a baseline rather than a complaint: the offline rule approximates the engine's slope, half-block and "
                + "liquid handling, so a small standing difference is expected. It is here so the number has a history — a "
                + "session where the median moves is a session where one of the two rules changed, and every planned move "
                + "is proven with the offline one.",
            session.Tick(0), session.Tick(session.Count - 1), session.Count);

        foreach (var stretch in FindStretches.Where(session.Count, i => valid.Number[i] == 1f && diverge.Number[i] > SeriousPx, MinTicks, allowGap: 10))
        {
            string moves = kind == null ? "" : $" The step in hand was {FindStretches.Summarise(kind, stretch, 3)}.";
            yield return new Finding(
                Severity.Definitive,
                Name,
                $"the two motion rules disagreed by more than {SeriousPx:n0} px for {stretch.End - stretch.Start + 1:n0} ticks, worst {FindStretches.Max(diverge, stretch):0.00} px",
                "A gap this size held this long means the rule the planner proves moves with and the collision that performs "
                    + "them are describing different moves, so an edge can be provable and unperformable. Read the step kind "
                    + "below and check that traversal's simulation against the engine path it is meant to mirror."
                    + moves,
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.End - stretch.Start + 1);
        }
    }
}

/// <summary>
/// Whether every kind of move the planner put in front of the body was ever actually made.
///
/// This is the coverage gap of 2026-09-09 turned into a check. In nine minutes the follower
/// completed 1,227 walks, eight drops, no jumps and no fall-throughs. Every one of those numbers
/// was already in the record, and no check asked the question, so the session read clean — the
/// defect surfaced only because Caner mentioned in passing that he had watched it jump. A detector
/// fires on a threshold someone chose and can therefore only find a failure someone imagined; this
/// one asks a question with no threshold in it at all, which is why it can catch a behaviour nobody
/// predicted: a kind that was offered and never once completed is wrong whatever the reason.
///
/// The mod also writes a fuller version of this beside the session as a census file, which the
/// report prints above the findings. This exists as well as that because a check can make the run
/// exit non-zero, and because it reads the session's own columns rather than a sibling file, so an
/// old session still gets the answer.
/// </summary>
public sealed class EveryMoveOfferedGetsMade : ICheck
{
    public string Name => "was every kind of move the plan offered ever actually made";
    public string[] Needs => new[] { "edge_n", "edge_kind", "edge_outcome", "next_kind" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column number = session["edge_n"], kind = session["edge_kind"], outcome = session["edge_outcome"];
        Column offered = session["next_kind"];

        // One edge report can sit in the column for many ticks, because it is sticky until the next
        // one. The counter is what separates a new report from the same one read again, and without
        // it two identical consecutive completions of one edge de-duplicate into one.
        var made = new Dictionary<string, int>(StringComparer.Ordinal);
        var failed = new Dictionary<string, int>(StringComparer.Ordinal);
        float previous = float.NaN;
        for (int i = 0; i < session.Count; i++)
        {
            float n = number.Number[i];
            if (float.IsNaN(n) || (!float.IsNaN(previous) && n <= previous))
                continue;
            previous = n;
            string k = kind.Text[i];
            if (k.Length == 0 || k == "-")
                continue;
            bool ok = outcome.Text[i] is "None" or "-" or "";
            var into = ok ? made : failed;
            into[k] = into.TryGetValue(k, out int had) ? had + 1 : 1;
        }

        // What the body was put in front of, which is the denominator: a kind that never appears
        // here was never offered, and that is a different finding from one offered and never made.
        var wasOffered = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < session.Count; i++)
        {
            string k = offered.Text[i];
            if (k.Length == 0 || k == "-")
                continue;
            wasOffered[k] = wasOffered.TryGetValue(k, out int had) ? had + 1 : 1;
        }

        foreach (var pair in wasOffered.OrderByDescending(p => p.Value))
        {
            made.TryGetValue(pair.Key, out int done);
            failed.TryGetValue(pair.Key, out int lost);
            if (done > 0)
                continue;
            yield return new Finding(
                lost > 0 ? Severity.Definitive : Severity.Potential,
                Name,
                $"{pair.Key} steps were offered but never once completed",
                $"The step in hand was {pair.Key} on {pair.Value:n0} ticks, and the edge log holds {done:n0} completions "
                    + $"against {lost:n0} faults for it. A move the planner keeps putting in front of the body and the body "
                    + "never makes is either an edge the grid should not be offering or a performance the traversal cannot "
                    + "deliver, and the fault reasons say which: none at all means the step never even ended, so look at "
                    + "the timeout allowance and whether the body reached the take-off.",
                session.Tick(0), session.Tick(session.Count - 1), pair.Value);
        }
    }
}
