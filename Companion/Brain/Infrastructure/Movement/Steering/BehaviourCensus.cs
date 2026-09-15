#nullable enable

using System.Collections.Generic;
using System.Text;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The whole run's movement accounting, process-wide: how many places of each kind were asked
/// for, how many were reached, how many plans were made and how many found nothing. One
/// continuous stretch of wanting one kind of place is one ask, so the record reads "asked for 40
/// places, reached 11" rather than counting a minute of following as 3,600 requests. It counted
/// jumps begun, refused and mislanded for the walker; a body that flies has none of those, and
/// what it has instead is whether the place was reached and how many plans it took.
/// </summary>
public static class BehaviourCensus
{
    private sealed class Kind
    {
        public int Requests, Reached, Abandoned;
    }

    private static readonly Dictionary<string, Kind> kinds = new();
    private static string? openKind;
    private static bool episodeReached;
    private static int plans, emptyPlans, replans, pendingPlans;

    public static void Reset()
    {
        kinds.Clear();
        openKind = null;
        episodeReached = false;
        plans = emptyPlans = replans = pendingPlans = 0;
    }

    public static void Planned(bool replan)
    {
        plans++;
        if (replan) replans++;
    }

    public static void PlanFailed() => emptyPlans++;
    public static void PlanPending() => pendingPlans++;

    /// <summary>A stretch of asking for one kind of place has begun; the same kind again continues it.</summary>
    public static void RequestBegan(string kind)
    {
        if (openKind == kind) return;
        Close();
        openKind = kind;
        episodeReached = false;
        Entry(kind).Requests++;
    }

    public static void RequestReached() => episodeReached = true;
    public static void RequestEnded() => Close();

    private static void Close()
    {
        if (openKind == null) return;
        Kind entry = Entry(openKind);
        if (episodeReached) entry.Reached++; else entry.Abandoned++;
        openKind = null;
        episodeReached = false;
    }

    private static Kind Entry(string kind)
    {
        if (!kinds.TryGetValue(kind, out Kind? entry)) kinds[kind] = entry = new Kind();
        return entry;
    }

    public static string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"plans {plans} (replans {replans}, found nothing {emptyPlans}, unfinished {pendingPlans})");
        foreach (var (kind, entry) in kinds)
            sb.AppendLine($"{kind}: asked {entry.Requests}, reached {entry.Reached}, abandoned {entry.Abandoned}");
        return sb.ToString();
    }
}
