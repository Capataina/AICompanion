#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Tools.Ledger;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// What the whole game update cost, and which part of it was whose — as numbers and never as a verdict.
///
/// <para>Before schema 0.45.0 a reader of a slow capture could say what the brain cost and nothing
/// else. On the 22 September 2026 capture that left a median residue of about 14 ms per frame
/// unattributed between the engine, the inspector's own drawing and everything else, and every
/// independent reading of that session had to end its frame-rate paragraph with a sentence saying so.
/// These rows split it.</para>
///
/// <para><b>Updates and frames are two numbers here, deliberately.</b> The engine runs a fixed
/// timestep and catches up by running two updates back to back with no draw between them, so an
/// update is not a frame and a figure named "frames per second" computed from update gaps is updates
/// a second wearing the wrong name — the exact misnamed-quantity failure this tree's guides ban.
/// Updates a second come from the intervals; frames a second come from the recorded draw count, which
/// the producer takes at the draw callback itself.</para>
/// </summary>
public sealed class MeasureTheFrame : IMeasure
{
    public string Name => "frame";

    public string[] Needs => new[]
    {
        "tick", "wall_elapsed_ms", "frame_ms", "draws", "overlay_ms", "inspector_ms", "engine_ms", "brain_ms", "record_ms",
    };

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        Column frame = session["frame_ms"], draws = session["draws"], overlay = session["overlay_ms"];
        Column inspector = session["inspector_ms"], engine = session["engine_ms"];
        Column brain = session["brain_ms"], record = session["record_ms"], wall = session["wall_elapsed_ms"];

        // A row whose interval is negative is one the producer could not measure — the first update of
        // a session has no predecessor — and it is excluded rather than counted as a fast frame.
        var measured = Enumerable.Range(0, session.Count).Where(i => frame.Number[i] >= 0).ToList();
        if (measured.Count == 0)
        {
            yield return PlayRow.Skipped(Name + "/updates-per-second",
                "the capture carries the frame ledger's columns and no row with a measured interval");
            yield break;
        }

        double seconds = (wall.Number[session.Count - 1] - wall.Number[0]) / 1000.0;
        double intervalTotal = measured.Sum(i => (double)frame.Number[i]);

        yield return PlayRow.Count(Name + "/updates-per-second",
            seconds > 0 ? session.Count / seconds : 0, "per-second", "higher-is-better",
            $"{session.Count:n0} companion AI update(s) over {seconds:0.0} s of wall clock. The engine's own timestep is "
            + $"sixty a second, so this is the share of real time the world actually advanced through");

        double drawTotal = measured.Sum(i => (double)draws.Number[i]);
        yield return PlayRow.Count(Name + "/frames-per-second",
            seconds > 0 ? drawTotal / seconds : 0, "per-second", "higher-is-better",
            $"{drawTotal:n0} draw(s) counted inside {measured.Count:n0} measured interval(s) over {seconds:0.0} s. Taken from "
            + "the recorded draw count rather than from the interval, because the engine catches up by running two "
            + "updates with no draw between them and an interval-derived figure would call those updates frames");

        yield return PlayRow.Share(Name + "/overrun-share",
            measured.Count(i => frame.Number[i] > FrameBudgetMilliseconds), measured.Count, "lower-is-better",
            $"measured intervals longer than one frame at sixty a second ({FrameBudgetMilliseconds:0.00} ms), which is a "
            + "fact of the engine's fixed timestep rather than a threshold anybody chose");

        yield return PlayRow.Count(Name + "/median-ms", Median(measured.Select(i => (double)frame.Number[i])),
            "ms", "lower-is-better",
            $"the median measured interval, against a {FrameBudgetMilliseconds:0.00} ms budget");

        // The split is taken over the *summed* interval rather than as a mean of per-row shares,
        // because a mean of shares weights a 6 ms catch-up update the same as a 90 ms hitch and the
        // question is where the session's time went.
        //
        // The brain column is read one row back, and that is a phase rather than an adjustment.
        // `frame_ms` is anchored at PostUpdateEverything, so a row written inside update N carries the
        // interval that closed at the end of update N−1; the brain cost inside that interval is
        // therefore the *previous* row's, which is exactly what `FrameCost.RemainderMilliseconds` is
        // handed when it computes `engine_ms`. Reading this row's `brain_ms` against it — which this
        // measure did until 22 September 2026, under a comment claiming otherwise — makes the five
        // shares a mixture of two updates rather than a split of one, and on the 22 September capture
        // that is a 10 ms column against a 25 ms interval being taken from the wrong tick. `record_ms`
        // needs no shift: the producer already writes the previous row's cost there, because a row
        // cannot contain the time spent writing itself.
        //
        // The split therefore runs over the measured rows that *have* a predecessor in the file, and its
        // denominator is those rows' own intervals rather than the whole session's — a numerator and a
        // denominator taken over different row sets is the arithmetic this folder's conventions already
        // ban. The interval statistics above keep every measured row, because none of them reads a
        // neighbour.
        var attributable = measured.Where(i => i > 0).ToList();
        double attributableTotal = attributable.Sum(i => (double)frame.Number[i]);
        foreach ((string part, Column column, int back, string what) in new[]
        {
            ("brain", brain, 1, "the companion's brain on the update the interval covers, read one row back because the interval closed at the end of the previous update"),
            ("record", record, 0, "this recorder's own write, which the producer already writes as the previous row's cost"),
            ("overlay", overlay, 0, "the brain overlay's world layers and cost strip"),
            ("inspector", inspector, 0, "the inspector's own panel"),
            ("engine", engine, 0, "everything left over: Terraria's update, the notch, the card, any other mod, and any cost of ours nothing times"),
        })
            yield return PlayRow.Share(Name + "/" + part + "-share",
                attributable.Sum(i => Math.Max(0d, (double)column.Number[i - back])), attributableTotal, null,
                $"of the measured interval on the {attributable.Count:n0} row(s) that have a predecessor to attribute it to, spent in {what}");
    }

    /// <summary>One frame at sixty a second, the engine's own fixed timestep. A fact of the world
    /// rather than a tuned line, and the producer declares the same number at
    /// <c>FrameCost.FrameMilliseconds</c>; if the two ever drift the share here stops meaning what the
    /// occurrence stream means.</summary>
    internal const double FrameBudgetMilliseconds = 1000.0 / 60.0;

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }
}
