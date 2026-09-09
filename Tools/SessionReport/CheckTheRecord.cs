#nullable enable

using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

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
    };

    public IEnumerable<Finding> Run(Session session)
    {
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
            if (Wordy.Contains(name))
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
