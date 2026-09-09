#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace AICompanion.Companion.Brain.SharedMovementSystem;

/// <summary>
/// A count of everything the follower was offered and everything it managed, with no verdict
/// attached to any of it.
///
/// It exists because every instrument this project had before it was a detector, and a detector
/// can only find a failure someone imagined: it fires on a threshold a person chose, so a
/// behaviour nobody thought to threshold produces no report at all, and zero findings looks
/// exactly like a clean run. On 2026-09-09 the follower completed 1,227 walks, eight drops, no
/// jumps and no fall-throughs in nine minutes. Every one of those numbers was already in the
/// record and none of them was in any check's domain, so the session read clean and the defect
/// was found only because Caner mentioned in passing that he had watched it jump.
///
/// A census is the other shape of instrument and the difference is where the silence lands. It
/// counts every category whether or not anything happened in it, so a category with nothing in it
/// prints a zero, and <c>Jump: planned 47, begun 1, completed 0</c> is a line nobody reads past.
/// A detector's zero is silence; a census's zero is a row.
///
/// Nothing here judges. The report is counts and the reasoning stays with whoever reads it, which
/// is deliberate: the moment a census grows a threshold it becomes a detector with a worse
/// interface, and the questions it answers are the ones nobody knew to ask.
///
/// It is game-free so that the replay tool's follow harness fills the same counters from the
/// offline body, which is what makes the two comparable.
/// </summary>
public static class BehaviourCensus
{
    private static readonly int Kinds = Enum.GetValues<MoveKind>().Length;

    private static readonly int[] planned = new int[Kinds];
    private static readonly int[] begun = new int[Kinds];
    private static readonly int[] completed = new int[Kinds];
    private static readonly int[] faulted = new int[Kinds];
    private static readonly int[] interrupted = new int[Kinds];

    // Per kind, per fault reason, so "the jumps all timed out" and "the jumps all mislanded" are
    // different findings rather than one number with the cause guessed at afterwards.
    private static readonly int[,] faults = new int[Kinds, Enum.GetValues<TraversalFault>().Length];

    /// <summary>
    /// Walks split by which way they go, because "it does not like walking down slopes" is a claim
    /// about one third of the walk rows and the record could not answer it. A walk edge is offered
    /// for the same row, one row up (a kerb the step-up lifts over) or one row down (a slope or a
    /// short ledge the step-down lowers onto), and a descending walk that is planned often and
    /// completed rarely is a different defect from one that is never planned at all — the first is
    /// the body failing the move, the second is the walk proof refusing to offer it.
    /// </summary>
    private static readonly int[] walkPlanned = new int[3];
    private static readonly int[] walkBegun = new int[3];
    private static readonly int[] walkCompleted = new int[3];

    private static readonly Dictionary<string, (int asked, int reached)> requests = new(StringComparer.Ordinal);
    private static string? episode;
    private static bool episodeReached;

    private static int plans, planSteps, pathsFound;

    /// <summary>A new session, a new world, a new companion: the counts belong to one playthrough.</summary>
    public static void Reset()
    {
        Array.Clear(planned);
        Array.Clear(begun);
        Array.Clear(completed);
        Array.Clear(faulted);
        Array.Clear(interrupted);
        Array.Clear(faults);
        Array.Clear(walkPlanned);
        Array.Clear(walkBegun);
        Array.Clear(walkCompleted);
        requests.Clear();
        episode = null;
        episodeReached = false;
        plans = planSteps = pathsFound = 0;
    }

    /// <summary>Every step of a path the planner returned: what the body was offered, which is the denominator for everything else.</summary>
    public static void Planned(NavPath path)
    {
        plans++;
        pathsFound++;
        planSteps += path.Steps.Count;
        foreach (NavStep step in path.Steps)
        {
            planned[(int)step.Kind]++;
            if (step.Kind == MoveKind.Walk)
                walkPlanned[Vertical(step)]++;
        }
    }

    /// <summary>A plan that returned nothing, counted so the ratio of plans to paths is readable.</summary>
    public static void PlanFailed() => plans++;

    /// <summary>The follower has started performing this step.</summary>
    public static void Begun(NavStep step)
    {
        begun[(int)step.Kind]++;
        if (step.Kind == MoveKind.Walk)
            walkBegun[Vertical(step)]++;
    }

    /// <summary>The step ended, either done (<see cref="TraversalFault.None"/>) or faulted with a reason.</summary>
    public static void Finished(NavStep step, TraversalFault outcome)
    {
        int kind = (int)step.Kind;
        if (outcome == TraversalFault.None)
        {
            completed[kind]++;
            if (step.Kind == MoveKind.Walk)
                walkCompleted[Vertical(step)]++;
            return;
        }
        if (outcome == TraversalFault.Interrupted)
            interrupted[kind]++;
        else
            faulted[kind]++;
        faults[kind, (int)outcome]++;
    }

    /// <summary>
    /// The brain has begun asking for somewhere new. An episode is one continuous stretch of
    /// wanting one thing, so a walk-with that lasts a minute is one ask rather than 3,600, and
    /// "asked for 40 places, reached 11" is a sentence about the companion rather than about the
    /// tick rate.
    /// </summary>
    public static void RequestBegan(string kind)
    {
        if (episode == kind)
            return;
        Close();
        episode = kind;
        episodeReached = false;
    }

    /// <summary>The navigator arrived at what the current episode asked for.</summary>
    public static void RequestReached() => episodeReached = true;

    /// <summary>The brain has stopped asking for anything; closes the open episode.</summary>
    public static void RequestEnded() => Close();

    private static void Close()
    {
        if (episode == null)
            return;
        requests.TryGetValue(episode, out (int asked, int reached) at);
        requests[episode] = (at.asked + 1, at.reached + (episodeReached ? 1 : 0));
        episode = null;
        episodeReached = false;
    }

    /// <summary>Which way a walk edge goes: 0 up, 1 flat, 2 down, matching the row labels in the report.</summary>
    private static int Vertical(NavStep step)
    {
        int dy = step.Tile.Y - step.From.Y;
        return dy < 0 ? 0 : dy == 0 ? 1 : 2;
    }

    /// <summary>
    /// The census as text, for the head of a session report. Every row is printed whether or not
    /// anything happened in it, which is the whole mechanism: a kind the body never managed is a
    /// visible zero rather than a missing line.
    /// </summary>
    public static string Report()
    {
        Close();
        var sb = new StringBuilder();
        sb.AppendLine("behaviour census — everything offered and everything managed, no verdict attached");
        sb.AppendLine();
        sb.AppendLine($"  plans run {plans:n0}, of which {pathsFound:n0} returned a path carrying {planSteps:n0} steps in total");
        sb.AppendLine();
        sb.AppendLine("  move            planned    begun completed  faulted interrupted   outcomes by reason");
        for (int k = 0; k < Kinds; k++)
        {
            var reasons = new List<string>();
            for (int f = 1; f < faults.GetLength(1); f++)
                if (faults[k, f] > 0)
                    reasons.Add($"{(TraversalFault)f} {faults[k, f]:n0}");
            sb.AppendLine($"  {((MoveKind)k).ToString(),-14}{planned[k],8:n0} {begun[k],8:n0} {completed[k],8:n0} {faulted[k],8:n0} {interrupted[k],11:n0}   {(reasons.Count == 0 ? "-" : string.Join(", ", reasons))}");
        }
        sb.AppendLine();
        sb.AppendLine("  walks by direction, because a descent refused and a descent failed are different defects");
        string[] labels = { "up (a kerb)", "flat", "down (a slope or lip)" };
        for (int v = 0; v < 3; v++)
            sb.AppendLine($"  {labels[v],-24}{walkPlanned[v],8:n0} {walkBegun[v],8:n0} {walkCompleted[v],8:n0}");
        sb.AppendLine();
        sb.AppendLine("  places asked for, and whether the body got there");
        if (requests.Count == 0)
            sb.AppendLine("    nothing was ever asked for, which is itself the finding");
        else
            foreach (var pair in requests)
                sb.AppendLine($"    {pair.Key,-14} asked {pair.Value.asked,5:n0}   reached {pair.Value.reached,5:n0}   abandoned {pair.Value.asked - pair.Value.reached,5:n0}");
        return sb.ToString();
    }
}
