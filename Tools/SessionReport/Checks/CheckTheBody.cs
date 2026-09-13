#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether the body went where the plan sent it. This is the check the fall-through defect of
/// 2026-09-09 needed and nobody had: the record held a frozen pixel position beside a live path and
/// a velocity cycling 0.30, 0.60, 0.90, 0.00 for thousands of ticks, and finding it took reading
/// three columns by hand against a decompiled collision routine. A body that is being driven and is
/// not moving is wrong in every situation, so it is the definitive form; a body at rest holding an
/// unfinished path might be waiting for something, so that is the potential form.
/// </summary>
public sealed class TheBodyMovesWhenDriven : ICheck
{
    /// <summary>Three quarters of a second. Shorter than this is a step boundary or a coast to rest.</summary>
    private const int MinFrozenTicks = 45;

    /// <summary>Under a twentieth of a pixel a tick is a body at rest rather than a body being pushed.</summary>
    private const float RestingSpeed = 0.05f;

    public string Name => "did the body move while it had somewhere to be";
    public string[] Needs => new[] { "npc_px", "path_steps", "path_at", "reflex", "npc_vel" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column pixels = session["npc_px"], steps = session["path_steps"], at = session["path_at"];
        Column reflex = session["reflex"], velocity = session["npc_vel"];
        Column? press = session.Find("press");
        Column? action = session.Find("action");

        var frozen = FindStretches.Where(session.Count, i =>
            i > 0
            && string.Equals(pixels.Text[i], pixels.Text[i - 1], StringComparison.Ordinal)
            && steps.Number[i] > 0f
            && at.Number[i] < steps.Number[i]
            && reflex.Text[i] == "-",
            MinFrozenTicks);

        foreach (var stretch in frozen)
        {
            float fastest = 0f;
            for (int i = stretch.Start; i <= stretch.End; i++)
            {
                if (!Session.TryPair(velocity.Text[i], out float vx, out float vy))
                    continue;
                fastest = MathF.Max(fastest, MathF.Max(MathF.Abs(vx), MathF.Abs(vy)));
            }

            bool driven = fastest > RestingSpeed;
            string pressed = press == null
                ? "the press column is absent, so whether it was asking to fall through is unreadable"
                : $"the fall-through press read {FindStretches.Summarise(press, stretch)} over the stretch";
            string doing = action == null ? "" : $" The action was {FindStretches.Summarise(action, stretch, 3)}.";

            yield return new Finding(
                driven ? Severity.Definitive : Severity.Potential,
                Name,
                driven
                    ? $"the body stood at one pixel for {stretch.Length} ticks while being driven at up to {fastest:0.00} px/tick"
                    : $"the body stood at one pixel for {stretch.Length} ticks with an unfinished path",
                $"npc_px never changed from {pixels.Text[stretch.Start]}, step {at.Number[stretch.Start]:0} of "
                    + $"{steps.Number[stretch.Start]:0}, and no reflex held it. Fastest velocity over the stretch "
                    + $"{fastest:0.00} px/tick, against a resting threshold of {RestingSpeed:0.00}; {pressed}.{doing} "
                    + (driven
                        ? "A velocity the collision engine cancels every tick is a body pushed into something the "
                          + "grid thinks is passable, which is the signature the platform fall-through defect wrote: "
                          + "the press was released before the platform row was cleared, so the platform caught the "
                          + "body again, one tick at a time, for ever."
                        : "This may be legitimate — a Hold request, or an arrival slack the navigator counts as "
                          + "arrived while the path index sits short — and what settles it is whether the request "
                          + "column names a kind that stands still."),
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.Length);
        }
    }
}

/// <summary>
/// Whether the moves the planner proved took as long as it proved they would. Every edge is
/// simulated before it is offered, so the ticks it should take are known before it runs; the
/// follower records the ticks it actually took. A large gap between the two means the body model
/// that proves an edge and the engine that performs it disagree, which is the defect class that has
/// bitten this project four times, and it is the one thing in the record that measures it directly.
/// </summary>
public sealed class ProvenMovesTakeTheirProvenTime : ICheck
{
    /// <summary>Twice the proven time is the slack the follower's own fault detector uses.</summary>
    private const float OverrunFactor = 2f;

    /// <summary>Below this there is nothing to measure: a two-tick walk that took five is noise.</summary>
    private const float MinProven = 8f;

    public string Name => "did each move take the time it was proven to take";
    public string[] Needs => new[] { "edge_n", "edge_kind", "edge_proven", "edge_took", "edge_outcome", "edge_from", "edge_to" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column number = session["edge_n"], kind = session["edge_kind"];
        Column proven = session["edge_proven"], took = session["edge_took"], outcome = session["edge_outcome"];

        // The edge columns are sticky: one finished move fills every row until the next finishes, so a
        // finding per row would report one overrun a thousand times. The count column is what changes.
        var byKind = new Dictionary<string, (int Count, float WorstRatio, int WorstRow)>(StringComparer.Ordinal);
        int seen = 0;
        float last = float.NaN;
        for (int i = 0; i < session.Count; i++)
        {
            if (number.Number[i] == last)
                continue;
            last = number.Number[i];
            if (float.IsNaN(proven.Number[i]) || proven.Number[i] < MinProven)
                continue;
            seen++;
            float ratio = took.Number[i] / proven.Number[i];
            if (ratio <= OverrunFactor)
                continue;
            var entry = byKind.GetValueOrDefault(kind.Text[i]);
            byKind[kind.Text[i]] = (entry.Count + 1,
                ratio > entry.WorstRatio ? ratio : entry.WorstRatio,
                ratio > entry.WorstRatio ? i : entry.WorstRow);
        }

        foreach (var pair in byKind)
        {
            int row = pair.Value.WorstRow;
            yield return new Finding(
                Severity.Potential,
                Name,
                $"{pair.Value.Count} {pair.Key} move(s) took over {OverrunFactor:0}× the ticks they were proven to take",
                $"Out of {seen} finished move(s) in the session. The worst took {took.Number[row]:0} ticks against "
                    + $"{proven.Number[row]:0} proven ({pair.Value.WorstRatio:0.0}×), from {session["edge_from"].Text[row]} "
                    + $"to {session["edge_to"].Text[row]}, ending {outcome.Text[row]}. The proof and the performance run "
                    + "over the same body arithmetic, so a systematic overrun in one kind of move means the two have "
                    + $"drifted apart for that kind; replay the same block with --follow to see the {pair.Key} tick by "
                    + "tick, and if the replay agrees with the proof then the disagreement is with the game's engine.",
                session.Tick(row), session.Tick(row), pair.Value.Count);
        }
    }
}

/// <summary>
/// Whether a companion that could not plan to the player was ever counted as stranded. The
/// stranded count is what turns into the roam behaviour that walks a sealed pocket, and it only
/// starts when a plan toward the player comes back completely empty while the reach flood closed
/// under its own budget. A long run of failed player-bound plans with the count flat at zero means
/// the body was failing to reach him and the mechanism meant to notice never fired.
/// </summary>
public sealed class BeingUnableToReachHimGetsNoticed : ICheck
{
    /// <summary>Two seconds of consecutive failed plans toward him.</summary>
    private const int MinTicks = 120;

    public string Name => "was a companion that could not reach him ever counted as stranded";
    public string[] Needs => new[] { "plan_failed", "request", "stranded" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column failed = session["plan_failed"], request = session["request"], stranded = session["stranded"];

        var stretches = FindStretches.Where(session.Count, i =>
            failed.Number[i] == 1f
            && (request.Text[i] == "WithPlayer" || request.Text[i] == "Guard")
            && stranded.Number[i] == 0f,
            MinTicks, allowGap: 15);

        foreach (var stretch in stretches)
        {
            string reach = session.Has("reach_n", "returnable_n")
                ? $"Reach over the stretch averaged {FindStretches.Mean(session["reach_n"], stretch):0} tiles with "
                  + $"{FindStretches.Mean(session["returnable_n"], stretch):0} returnable. "
                : "";
            yield return new Finding(
                Severity.Potential,
                Name,
                $"{stretch.Length} ticks of failed plans toward him with the stranded count flat at zero",
                $"The request was {FindStretches.Summarise(request, stretch, 2)}. {reach}"
                    + "A partial path counts as a failed plan and correctly does not start the count, so this is the "
                    + "expected shape whenever the companion is walking as close as it can get; it is a defect only if "
                    + "the plans were genuinely empty, which the plans file for this session answers by whether it holds "
                    + "windows for these ticks.",
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.Length);
        }
    }
}
