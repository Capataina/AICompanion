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
    // Named after the reach sense that owns these two counts from schema 0.31.0. A capture older than that
    // carries the previous names and skips this check, which is the honest outcome: the old columns were
    // written by a different owner and matching them by position would be guessing.
    public string[] Needs => new[] { "reach_any", "reach_two_way" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column reach = session["reach_any"], returnable = session["reach_two_way"];
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
/// Whether the reach flood ever settles. Every optional activity now asks the flood rather than searching for
/// itself, and an unsettled flood is the one answer that correctly stops work from starting — so a flood that
/// stays unsettled stops all of it, silently and for as long as it lasts. The offers say the honest thing
/// ("not yet known") on every one of those rows, which is exactly why nothing in the record shouts.
///
/// <para>The flood is bounded per rescore and grows across successive ones, and the region in a cave is small,
/// so settling takes a handful of rescores after a world change and a terrain edit restarts it. A stretch
/// longer than that is not the flood working, it is the flood not finishing: a region too large for the
/// budget, an edit on every tick restarting it, or a body that never resolves because whatever won the tick
/// asked for a hold.</para>
/// </summary>
public sealed class TheReachFloodSettles : ICheck
{
    public string Name => "does the reach flood ever finish";
    public string[] Needs => new[] { "reach_complete" };

    /// <summary>How many consecutive unsettled rows stop reading as a flood in progress. The flood's own unit
    /// is the rescore rather than the tick and the positioner rescores on its own interval, so this is a few
    /// seconds of play rather than a few rescores; short enough to catch a stall, long enough that ordinary
    /// settling after a dig never reaches it.</summary>
    private const int Unsettled = 600;

    public IEnumerable<Finding> Run(Session session)
    {
        Column complete = session["reach_complete"];
        // A gap is allowed because the flood flickers by design: it finishes, a dig raises the terrain
        // revision, and it starts again. A run broken by those single settled rows is several stretches that
        // each fall under the threshold, which is how a stall hides from the check written to find it.
        var stalled = FindStretches.Where(session.Count, i => complete.Number[i] < 0.5, Unsettled, allowGap: 30);
        if (stalled.Count == 0)
            yield break;

        int rows = 0;
        foreach (var stretch in stalled)
            rows += stretch.Length;
        yield return new Finding(
            Severity.Potential,
            Name,
            $"the reach flood stayed unfinished for {rows} row(s)",
            $"{rows} row(s) across {stalled.Count} stretch(es) of at least {Unsettled}, the longest "
                + $"{Longest(stalled)} rows. Until the flood finishes, a tile it has not claimed is not-yet-known "
                + "rather than unreachable, and every activity that asks about a place it cannot already see is "
                + "correctly refusing to start on an unanswered search — so lighting, collecting, mining and "
                + "chopping are all held by this, and each of them reports only its own honest 'not yet'. Look "
                + "for a region larger than the flood's per-rescore budget, a terrain edit on nearly every tick "
                + "restarting it, or a companion that spends the stretch holding, since the flood is advanced by "
                + "the positioner's resolve and a hold returns before it refreshes.",
            session.Tick(stalled[0].Start), session.Tick(stalled[^1].End), rows);
    }

    private static int Longest(System.Collections.Generic.IReadOnlyList<Stretch> stretches)
    {
        int longest = 0;
        foreach (var stretch in stretches)
            if (stretch.Length > longest) longest = stretch.Length;
        return longest;
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
        "torch_reason", "lighting_sites",
    };

    public IEnumerable<Finding> Run(Session session)
    {
        // The declaration and this set are unioned rather than the declaration replacing it, and the
        // difference is not cosmetic: the writer's `text_columns` line is a hand-maintained string sitting
        // beside the header builder, so a new textual column is two places to remember and the second one is
        // the one that gets forgotten. `torch_reason` was forgotten in exactly that way, and because a
        // declared list replaced this set outright, every capture carrying it reported all 15,105 of its
        // cells as failing to parse as numbers — a finding about the reader, printed against the writer,
        // that no amount of correcting this set could have cleared for a capture already written. A curated
        // set can only ever mark a column textual, never numeric, so the union cannot hide a real fault: a
        // genuinely numeric column named here would be a mistake in this list and nowhere else.
        var textColumns = new HashSet<string>(Wordy, System.StringComparer.Ordinal);
        if (session.Metadata.TryGetValue("text_columns", out string? declared))
            textColumns.UnionWith(declared.Split(','));
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
