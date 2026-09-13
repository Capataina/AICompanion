#nullable enable

using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether the occurrence stream kept everything it was offered. From schema 0.29.0 every row carries
/// <c>events_dropped</c>, the running count of occurrences refused because a failed write had stopped the event writer;
/// the sidecar cannot record its own loss, and the rows keep being written, so only the rows can say when it began. Any
/// drop is Potential: strikes, pickups, outcomes and grants after that tick are missing from the sidecar, so an identity
/// check that finds nothing after it has not measured that stretch.
/// </summary>
public sealed class NoOccurrenceWasDropped : ICheck
{
    public string Name => "did the occurrence stream keep everything it was offered";
    public string[] Needs => new[] { "tick", "events_written", "events_dropped" };

    public IEnumerable<Finding> Run(Session session)
    {
        int first = -1;
        for (int i = 0; i < session.Count && first < 0; i++)
            if (session["events_dropped"].Number[i] > 0) first = i;
        if (first < 0) yield break;
        int last = session.Count - 1;
        yield return new Finding(Severity.Potential, Name,
            $"the occurrence stream stopped: {session["events_dropped"].Text[last]} occurrence(s) dropped from tick {session.Tick(first)}",
            $"The event writer stopped after a failed write, having written {session["events_written"].Text[first]} record(s); every occurrence offered afterwards was counted and refused while rows kept being written. "
            + $"Strikes, pickups, attempt outcomes and grants after tick {session.Tick(first)} are missing from the sidecar, so any event-based check that finds nothing after that tick has not measured it.",
            session.Tick(first), session.Tick(last), (int)System.Math.Min(int.MaxValue, session["events_dropped"].Number[last]));
    }
}

/// <summary>
/// Whether the recorder closed the capture normally. From schema 0.28.0 every normal closure — world unload, mod
/// unload, recording switched off, a new world superseding the session — ends the file with
/// <c># end=&lt;reason&gt;;rows=&lt;n&gt;</c>, and nothing else writes that line: a failed row write disposes the stream
/// without it, and a killed game never reaches it. A capture without it is an interrupted capture, whose rows since the
/// last flush and whose closing census, map and event closure may be missing. An end marker naming a row count the
/// file does not hold is Definitive, because the recorder counts exactly the rows it wrote.
/// </summary>
public sealed class TheCaptureWasClosed : ICheck, ICheckCoverage
{
    public string Name => "was the capture closed normally";
    public string[] Needs => System.Array.Empty<string>();

    public string? Missing(Session session)
        => CompletedTransferClaimsWereReceived.SchemaAtLeast(session, new System.Version(0, 28, 0)) ? null
            : "an end marker on normal closure (first written by schema 0.28.0)";

    public IEnumerable<Finding> Run(Session session)
    {
        int last = session.Count == 0 ? 0 : session.Tick(session.Count - 1);
        if (!session.Metadata.TryGetValue("end", out string? end))
        {
            yield return new Finding(Severity.Potential, Name, "interrupted capture: the recording has no end marker",
                $"The file holds {session.Count} row(s){(session.Count > 0 ? $" ending at tick {last}" : "")} and ends without the line every normal closure writes, so the game was closed without unloading the world, crashed, or a write failed. "
                + "Rows written since the last flush, the session census and map, and the event stream's closure may be missing, so nothing absent near the end of this capture is evidence that it did not happen.",
                last, last, 0);
            yield break;
        }
        foreach (string part in end.Split(';'))
            if (part.StartsWith("rows=", System.StringComparison.Ordinal) && int.TryParse(part[5..], out int rows) && rows != session.Count + session.Ragged)
                yield return new Finding(Severity.Definitive, Name,
                    $"the end marker names {rows} row(s) and the file holds {session.Count + session.Ragged}",
                    $"The recorder counts each row it wrote and states the count when it closes ({end}). A file holding a different number was cut, edited or appended to after closure, so its rows are not the session the recorder wrote.",
                    last, last, System.Math.Abs(rows - session.Count - session.Ragged));
    }
}

/// <summary>
/// Whether the record contradicts itself, asked before anything is concluded from it. A broken
/// instrument reads exactly like a broken brain, and this project has spent whole diagnoses on the
/// instrument: four of its defects came from one mechanism being modelled one way in the code that
/// decides and another way in the code that watches. So the first checks in the report are the ones
/// whose findings mean "do not trust the rest of this file yet".
/// </summary>
public sealed class TicksAdvance : ICheck
{
    public string Name => "does the tick column only ever go forward";
    public string[] Needs => new[] { "tick" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column tick = session["tick"];
        int backwards = 0, firstRow = -1;
        float worst = 0f;
        for (int i = 1; i < session.Count; i++)
        {
            float delta = tick.Number[i] - tick.Number[i - 1];
            if (delta > 0f)
                continue;
            backwards++;
            if (firstRow < 0)
                firstRow = i;
            if (delta < worst)
                worst = delta;
        }
        if (backwards == 0)
            yield break;

        yield return new Finding(
            Severity.Definitive,
            Name,
            "the tick column goes backwards, so every age and duration measured from it is wrong",
            $"{backwards} row(s) carry a tick at or below the row before them, the worst by {worst:0} ticks. "
                + "A duration in this file is the difference between two ticks, so a negative step makes every "
                + "stretch length and every 'ticks since' in the rest of this report unsafe. Two worlds written "
                + "to one file, or a counter reset, are the causes to check first.",
            session.Tick(firstRow - 1), session.Tick(firstRow), backwards);
    }
}

/// <summary>
/// Whether the reachability tier reports a subset as a superset. Returnable tiles are by
/// construction those reached tiles the body can also come home from, so the count can never
/// exceed the reach count; a row where it does means the two counts are taken from different floods
/// or a different tick, and every conclusion about the companion entering somewhere unrecoverable
/// rests on them.
/// </summary>
public sealed class ReturnableFitsInsideReach : ICheck
{
    public string Name => "can it come home from more places than it can reach";
    public string[] Needs => new[] { "reach_n", "returnable_n" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column reach = session["reach_n"], returnable = session["returnable_n"];
        var bad = FindStretches.Where(session.Count, i => returnable.Number[i] > reach.Number[i], 1);
        if (bad.Count == 0)
            yield break;

        int rows = 0;
        foreach (var stretch in bad)
            rows += stretch.Length;
        yield return new Finding(
            Severity.Definitive,
            Name,
            "the returnable count exceeds the reach count, which the flood cannot produce",
            $"{rows} row(s) across {bad.Count} stretch(es); the first reads reach {reach.Number[bad[0].Start]:0} "
                + $"and returnable {returnable.Number[bad[0].Start]:0}. Returnable tiles are a filtered subset of "
                + "reached tiles, so either the two counts are written from different floods or one is stale, and "
                + "the tier's own decision about entering a one-way place is unreadable until that is settled.",
            session.Tick(bad[0].Start), session.Tick(bad[^1].End), rows);
    }
}

/// <summary>
/// Whether any column the writer produced failed to parse where the reader expected a number. This
/// is the check that catches the writer changing a cell's shape — packing a letter into a column
/// that used to be plain, or writing a name where a count belonged — before a check built on that
/// column reports a clean run it never measured.
/// </summary>
public sealed class ColumnsHoldWhatTheyClaim : ICheck
{
    public string Name => "did every numeric column parse";
    public string[] Needs => System.Array.Empty<string>();

    // The columns that carry a name, a word or a pair on purpose. Everything else in the file is a
    // number, so anything unparsed outside this set is the writer and the reader disagreeing.
    private static readonly HashSet<string> Wordy = new(System.StringComparer.Ordinal)
    {
        "state", "action", "reflex", "top_threat", "target", "request", "anchor", "spot", "next_kind",
        "npc_tile", "npc_px", "npc_vel", "held", "weapon", "fire", "engage", "torch", "player_tile",
        "edge_kind", "edge_from", "edge_to", "edge_outcome", "spot_home",
        "diverge_invalid_reason", "sample_phase", "player_px", "player_vel", "player_liquid", "player_hit", "npc_hit",
        "player_state", "player_activity", "player_support", "npc_support", "control", "control_source",
        "observed_vel", "observed_mobility", "predicted_vel", "predicted_mobility",
        "follow_reason", "recovery_reason", "guard_reason", "mine_policy", "mine_status", "mine_target", "target_evidence",
    };

    public IEnumerable<Finding> Run(Session session)
    {
        var textColumns = session.Metadata.TryGetValue("text_columns", out string? declared)
            ? new HashSet<string>(declared.Split(','), System.StringComparer.Ordinal) : Wordy;
        if (session.Ragged > 0)
            yield return new Finding(
                Severity.Potential,
                Name,
                "some rows held the wrong number of cells and were dropped",
                $"{session.Ragged} row(s) of the file did not have {session.Names.Count} cells and are not in this "
                    + "report. A session killed mid-write leaves one such row at the end, which is harmless; more "
                    + "than one means the writer emitted a row that disagrees with its own header.",
                0, 0, session.Ragged);

        foreach (string name in session.Names)
        {
            if (textColumns.Contains(name))
                continue;
            Column column = session[name];
            if (column.Unparsed == 0)
                continue;
            yield return new Finding(
                Severity.Potential,
                Name,
                $"the column {name} held {column.Unparsed} cell(s) that are not a number",
                $"Every check that reads {name} is measuring {session.Count - column.Unparsed} of {session.Count} rows. "
                    + "The writer packs a unit or a state letter into several columns on purpose and the reader strips "
                    + "those; a cell that survives the stripping and still holds no number is a shape the reader has "
                    + "never seen, so the column wants adding to the reader's wordy set or the writer wants fixing.",
                0, 0, column.Unparsed);
        }
    }
}
