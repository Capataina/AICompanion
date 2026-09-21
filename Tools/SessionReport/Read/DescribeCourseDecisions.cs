#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// The course as a story: what the companion decided, in tick order, coalesced so that a run of ticks
/// holding the same decision is one line rather than six hundred.
///
/// <para>It is the third of the three readers the retained-course plan asks for, beside the contracts
/// check and the work measure, and it is the one that answers "what was it doing at tick 8127" — the
/// question every diagnosis of a capture has started from. The default report prints the changes; the
/// full timeline prints every decision, because a run that looks stable at this resolution can be
/// re-deciding the same thing every tick and only the unabridged list shows it.</para>
///
/// <para>The coalescing key is deliberately what a person asks about — the reason, the activity and
/// the bound purpose — and not the whole record. Order counts and refusal tallies move every tick in
/// a live fight, so keying on them would produce one line per tick and call it a story.</para>
/// </summary>
public static class DescribeCourseDecisions
{
    /// <summary>Intervals printed in the default report. The full timeline prints every one.</summary>
    private const int DefaultIntervals = 24;

    public static string Of(Session session, bool fullTimeline)
    {
        CourseDecisionLog log = ReadCourseDecisions.From(ReadGodsEyeEvents.Read(session.Path));
        if (log.Decisions.Count == 0)
            return log.Unreadable > 0
                ? $"course decisions  none readable; {log.Unreadable} payload(s) unreadable\n"
                : "";

        var runs = Coalesce(log.Decisions);
        var text = new StringBuilder();
        text.Append($"course decisions  {log.Decisions.Count:n0} decision(s) in {runs.Count:n0} run(s)");
        if (log.Unreadable > 0) text.Append($"; {log.Unreadable:n0} payload(s) unreadable");
        text.Append('\n');

        IEnumerable<Run> shown = fullTimeline ? runs : runs.Take(DefaultIntervals);
        foreach (Run run in shown)
        {
            // The first decision of a run carries the numbers, because those are what the run began
            // under; quoting the last one's would describe the tick the companion left, not the one
            // where it committed.
            CourseDecision first = run.First;
            text.Append($"  {run.FromTick}–{run.ToTick}  {run.Decisions,5:n0}x  {first.What}");
            text.Append($"  reason={first.Reason}");
            if (!first.Settled) text.Append(" unsettled");
            text.Append($"  steps={first.Steps} priced={first.OrdersPriced} refused={first.OrdersRefused}");
            if (!first.SearchExhausted) text.Append(" cut");
            if (first.ReleaseReason.Length > 0) text.Append($"  released={first.ReleaseReason}");
            if (first.Refusals.Count > 0)
                text.Append("  " + string.Join(" ", first.Refusals.OrderByDescending(r => r.Value)
                    .Take(3).Select(r => $"{r.Key}x{r.Value}")));
            text.Append('\n');
        }
        if (!fullTimeline && runs.Count > DefaultIntervals)
            text.Append($"  … {runs.Count - DefaultIntervals:n0} further run(s); --timeline prints every one\n");
        return text.ToString();
    }

    private sealed record Run(long FromTick, long ToTick, int Decisions, CourseDecision First);

    private static List<Run> Coalesce(IReadOnlyList<CourseDecision> decisions)
    {
        var runs = new List<Run>();
        CourseDecision open = decisions[0];
        long from = open.Tick, to = open.Tick;
        int count = 1;
        for (int i = 1; i < decisions.Count; i++)
        {
            CourseDecision next = decisions[i];
            if (Same(open, next)) { to = next.Tick; count++; continue; }
            runs.Add(new Run(from, to, count, open));
            open = next; from = to = next.Tick; count = 1;
        }
        runs.Add(new Run(from, to, count, open));
        return runs;
    }

    private static bool Same(CourseDecision a, CourseDecision b)
        => string.Equals(a.Reason, b.Reason, StringComparison.Ordinal)
            && string.Equals(a.Activity, b.Activity, StringComparison.Ordinal)
            && string.Equals(a.Purpose, b.Purpose, StringComparison.Ordinal);
}
