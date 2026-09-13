#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AICompanion.Tools.SessionReport;

/// <summary>How a tool effect reached its attempt, so a reader can tell a row-proven join from an interval inference.</summary>
public enum ToolEffectRoute { OpenAttemptRow, ClosedOnItsTick, OutcomeInterval }

/// <summary>Everything the record names under one attempt identity: its conclusion, the grants issued under it and the tool effects it produced.</summary>
public sealed class AttemptEvidence
{
    public AttemptEvidence(long attemptId) => AttemptId = attemptId;
    public long AttemptId { get; }
    public GodsEyeEvent? Outcome { get; set; }
    public List<GodsEyeEvent> DuplicateOutcomes { get; } = new();
    public List<GodsEyeEvent> Grants { get; } = new();
    public List<(GodsEyeEvent Effect, ToolEffectRoute Route)> ToolEffects { get; } = new();

    public long? OutcomeActivityId => Number(Outcome?.Field("activity-id"));
    public long? StartTick => Number(Outcome?.Field("start-tick"));
    public long? EndTick => Number(Outcome?.Field("end-tick"));

    /// <summary>The earliest tick any joined record names, which places an attempt with no outcome on a timeline.</summary>
    public long FirstTick => new[] { StartTick ?? long.MaxValue, Grants.Count > 0 ? Grants.Min(g => g.tick) : long.MaxValue, ToolEffects.Count > 0 ? ToolEffects.Min(t => t.Effect.tick) : long.MaxValue }.Min();
    public long LastTick => new[] { EndTick ?? long.MinValue, Grants.Count > 0 ? Grants.Max(g => g.tick) : long.MinValue, ToolEffects.Count > 0 ? ToolEffects.Max(t => t.Effect.tick) : long.MinValue }.Max();

    private static long? Number(string? value) => ReadGodsEyeEvents.TryLong(value, out long result) ? result : null;
}

public sealed class AttemptJoin
{
    public SortedDictionary<long, AttemptEvidence> Attempts { get; } = new();
    public List<GodsEyeEvent> OutcomesWithoutIdentity { get; } = new();
    public List<GodsEyeEvent> GrantsWithoutIdentity { get; } = new();
    public List<GodsEyeEvent> UnjoinedToolEffects { get; } = new();
    public int GrantsOutsideAttempts { get; set; }
    public int OutcomeCount { get; set; }
    public int GrantCount { get; set; }
    public int ToolEffectCount { get; set; }
}

/// <summary>
/// Reads one attempt's grants, effects and conclusion together by identity rather than by the
/// time window they happened to fall in. The producer's identities decide every join:
///
///   attempt-outcome   attempt-id, process-wide unique (OwnCurrentActivity's static counter)
///   control-grant     attempt-id of the attempt open at finalisation, zero when none is open —
///                     every safety, recovery and downed path suspends before it finalises
///   tool-effect       activity-id and choice-id only. Its attempt= is the tool instance's own
///                     counter (ObserveTileToolEffect.cs), unrelated to attempt identity, so it is
///                     never used; the effect joins through the TSV row at its tick, which names
///                     the attempt open when the tick was recorded, or the attempt that closed on
///                     that tick under the same activity (an attempt can open, strike and be
///                     suspended by recovery inside one tick). Only with no such row does it fall
///                     back to the single recorded outcome of that activity whose interval
///                     contains the tick, and more than one such outcome leaves it unjoined.
///
/// An identity no record resolves stays visible as unjoined; nothing is merged by proximity.
/// </summary>
public static class JoinAttemptEvidence
{
    private const int AttemptsShown = 12;

    public static AttemptJoin Build(Session session, GodsEyeEventLog log)
    {
        var join = new AttemptJoin();
        AttemptEvidence At(long id)
        {
            if (!join.Attempts.TryGetValue(id, out AttemptEvidence? attempt))
                join.Attempts[id] = attempt = new AttemptEvidence(id);
            return attempt;
        }

        var toolEffects = new List<GodsEyeEvent>();
        foreach (GodsEyeEvent e in log.Events)
        {
            switch (e.kind)
            {
                case "attempt-outcome":
                    join.OutcomeCount++;
                    if (!ReadGodsEyeEvents.TryLong(e.Field("attempt-id"), out long outcomeId) || outcomeId <= 0) { join.OutcomesWithoutIdentity.Add(e); break; }
                    AttemptEvidence concluded = At(outcomeId);
                    if (concluded.Outcome == null) concluded.Outcome = e; else concluded.DuplicateOutcomes.Add(e);
                    break;
                case "control-grant":
                    join.GrantCount++;
                    if (!ReadGodsEyeEvents.TryLong(e.Field("attempt-id"), out long grantAttempt) || grantAttempt < 0) { join.GrantsWithoutIdentity.Add(e); break; }
                    if (grantAttempt == 0) { join.GrantsOutsideAttempts++; break; }
                    At(grantAttempt).Grants.Add(e);
                    break;
                case "tool-effect":
                    join.ToolEffectCount++;
                    toolEffects.Add(e);
                    break;
            }
        }

        // Tool effects join after every outcome is known, because the interval fallback needs them all.
        Column? open = session.Find("activity_attempt_id"), endId = session.Find("attempt_end_id"),
            endTick = session.Find("attempt_end_tick"), endActivity = session.Find("attempt_end_activity_id");
        Dictionary<long, int> rowAt = RowsByTick(session);
        foreach (GodsEyeEvent e in toolEffects)
        {
            if (!ReadGodsEyeEvents.TryLong(e.ChannelField("activity-id"), out long activityId)) { join.UnjoinedToolEffects.Add(e); continue; }
            long? joined = null;
            ToolEffectRoute route = ToolEffectRoute.OutcomeInterval;
            if (open != null && rowAt.TryGetValue(e.tick, out int row))
            {
                if (LongAt(open, row) is long openId && openId > 0) { joined = openId; route = ToolEffectRoute.OpenAttemptRow; }
                else if (endId != null && endTick != null && endActivity != null && LongAt(endTick, row) == e.tick
                    && LongAt(endActivity, row) == activityId && LongAt(endId, row) is long closedId && closedId > 0)
                { joined = closedId; route = ToolEffectRoute.ClosedOnItsTick; }
            }
            if (joined == null)
            {
                var containing = join.Attempts.Values
                    .Where(a => a.OutcomeActivityId == activityId && a.StartTick <= e.tick && e.tick <= a.EndTick)
                    .Take(2).ToList();
                if (containing.Count == 1) joined = containing[0].AttemptId;
            }
            if (joined is long id) At(id).ToolEffects.Add((e, route));
            else join.UnjoinedToolEffects.Add(e);
        }
        return join;
    }

    public static string Describe(string tsvPath, Session session, bool full)
    {
        if (!session.Has("activity_attempt_id"))
            return "attempts  unavailable — this session predates attempt identity (schema 0.20.0), so occurrences are summarised only by the time windows above\n";
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(tsvPath);
        if (!log.Present)
            return "attempts  unavailable — there is no -events.jsonl sidecar, so outcomes, grants and tool effects cannot be joined\n";

        AttemptJoin join = Build(session, log);
        int withoutOutcome = join.Attempts.Values.Count(a => a.Outcome == null);
        var text = new StringBuilder();
        text.Append($"attempts  {join.Attempts.Count:n0} attempt identities from {join.OutcomeCount:n0} outcome(s), {join.GrantCount:n0} control grant transition(s) and {join.ToolEffectCount:n0} tool effect(s); {withoutOutcome:n0} carry no recorded outcome\n");
        text.Append("  join      outcomes and grants by attempt id; tool effects by activity id through the row at their tick, else the one outcome interval containing it; a tool's own attempt= counter is never an attempt identity\n");
        text.Append($"  unjoined  {join.GrantsOutsideAttempts:n0} grant(s) issued with no attempt open (safety, recovery, downing or no activity); {join.GrantsWithoutIdentity.Count:n0} grant(s) and {join.OutcomesWithoutIdentity.Count:n0} outcome(s) with no readable attempt id; {join.UnjoinedToolEffects.Count:n0} tool effect(s) no attempt contains\n");
        IEnumerable<AttemptEvidence> shown = join.Attempts.Values;
        if (!full && join.Attempts.Count > AttemptsShown)
        {
            text.Append($"  latest {AttemptsShown} of {join.Attempts.Count:n0}; --timeline lists every attempt and every unjoined record\n");
            shown = join.Attempts.Values.Skip(join.Attempts.Count - AttemptsShown);
        }
        foreach (AttemptEvidence attempt in shown)
            text.Append("  ").Append(DescribeAttempt(attempt)).Append('\n');
        if (full)
            foreach (GodsEyeEvent e in join.GrantsWithoutIdentity.Concat(join.OutcomesWithoutIdentity).Concat(join.UnjoinedToolEffects).OrderBy(e => e.seq))
                text.Append($"  unjoined  tick {e.tick} {e.kind} {e.label} channel={e.channel} {Abbreviate(e.detail, 300)}\n");
        return text.ToString();
    }

    internal static string DescribeAttempt(AttemptEvidence a)
    {
        var line = new StringBuilder($"attempt {a.AttemptId}");
        if (a.Outcome is { } o)
            line.Append($"  {o.label} ({o.Field("family") ?? "family unrecorded"}) activity {o.Field("activity-id") ?? "unrecorded"}  ticks {o.Field("start-tick") ?? "?"}..{o.Field("end-tick") ?? "?"}  {o.channel}  cause={o.Field("cause") ?? "unrecorded"}  credited effects {o.amount}");
        else
        {
            var named = a.Grants.Select(g => g.Field("activity-id")).Concat(a.ToolEffects.Select(t => t.Effect.ChannelField("activity-id")))
                .Where(id => id != null).Distinct().ToArray();
            line.Append($"  outcome unrecorded (still open when capture ended, or its occurrence was lost)  activity {(named.Length == 0 ? "unrecorded" : string.Join("/", named))} per its grants and effects");
        }
        if (a.DuplicateOutcomes.Count > 0) line.Append($"; {a.DuplicateOutcomes.Count} further outcome(s) under the same id");
        line.Append($"; grants {a.Grants.Count}");
        if (a.Grants.Count > 0)
            line.Append(" (").Append(string.Join(", ", a.Grants.GroupBy(g => $"{g.Field("requested-owner") ?? "?"}/{g.Field("hand") ?? "?"}").Select(g => $"{g.Key}×{g.Count()}"))).Append(')');
        line.Append($"; tool effects {a.ToolEffects.Count}");
        if (a.ToolEffects.Count > 0)
            line.Append(" (").Append(string.Join(", ", a.ToolEffects.GroupBy(t => t.Effect.Field("effect") ?? "?").Select(g => $"{g.Key} {g.Count()}"))).Append(')');
        return line.ToString();
    }

    /// <summary>
    /// Row index by recorded engine tick. The recorder writes one row per NPC update after the brain
    /// has run (CompanionNPC.AI: Brain.Tick, then BrainTelemetry.Record), so a tick names at most one
    /// row; a repeated tick keeps its first row and TicksAdvance reports the repetition.
    /// </summary>
    internal static Dictionary<long, int> RowsByTick(Session session)
    {
        var rows = new Dictionary<long, int>();
        if (session.Find("tick") is not { } tick) return rows;
        for (int i = 0; i < session.Count; i++)
            if (LongAt(tick, i) is long value) rows.TryAdd(value, i);
        return rows;
    }

    /// <summary>
    /// An identity cell read as an exact integer. <see cref="Column.Number"/> is a float, which stops
    /// representing consecutive integers at 2^24 — a choice id or engine tick in a long-running host
    /// passes that — so identities are parsed from the text instead.
    /// </summary>
    internal static long? LongAt(Column column, int row)
        => long.TryParse(column.Text[row], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;

    internal static string Abbreviate(string value, int maximum)
        => value.Length <= maximum ? value : value[..maximum] + "… (full detail in --timeline)";
}
