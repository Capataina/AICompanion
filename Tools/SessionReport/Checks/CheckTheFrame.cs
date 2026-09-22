#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether the whole game update fitted the engine's own timestep, and where it went when it did not.
///
/// <para>The report already had a brain-cost check, and on the 22 September 2026 capture that check
/// was true and useless: the brain took 10.17 ms of a 25.30 ms median interval, so it was both the
/// largest single contributor and less than half the problem, and nothing could say what the other
/// half was. This reads the frame ledger schema 0.45.0 added and reports the overrun share **with the
/// split beside it**, because the share alone sends the next session to tune whatever was measured
/// last rather than whatever is large.</para>
///
/// <para>It is Potential rather than Definitive. An overrunning frame is not a contradiction in the
/// record: the engine legitimately spends a long update on a world save, a chunk load or a mod
/// nobody here can see, and a session played on a machine doing something else overruns for reasons
/// no code change would fix. What makes it worth reporting is the *share*, and the finding states the
/// threshold so it can be argued with.</para>
/// </summary>
public sealed class TheFrameFitsTheEnginesTimestep : ICheck
{
    /// <summary>
    /// The share of measured intervals that may overrun before the session is worth opening.
    ///
    /// A quarter, and the reason is what the number stands for rather than where it was fitted: below
    /// it the overruns read as individual hitches a player notices and forgets, and at or above it the
    /// world is persistently advancing slower than real time, which is the thing he reported as
    /// "practically unplayable". The 22 September capture sits at 84%. A healthy session on this
    /// machine has never been measured, so this is a line drawn from one bad recording and one
    /// argument, and it is stated in the finding for exactly that reason.
    /// </summary>
    private const double InspectionShare = 0.25;

    public string Name => "did the whole frame fit the engine's timestep";

    public string[] Needs => new[]
    {
        "tick", "frame_ms", "draws", "overlay_ms", "inspector_ms", "engine_ms", "brain_ms", "record_ms",
    };

    public IEnumerable<Finding> Run(Session session)
    {
        Column frame = session["frame_ms"];
        var measured = Enumerable.Range(0, session.Count).Where(i => frame.Number[i] >= 0).ToList();
        if (measured.Count == 0) yield break;

        int over = measured.Count(i => frame.Number[i] > MeasureTheFrame.FrameBudgetMilliseconds);
        double share = (double)over / measured.Count;
        if (share < InspectionShare) yield break;

        // The split reads the brain one row back and runs only over rows that have a predecessor, for
        // the phase reason `MeasureTheFrame` states: `frame_ms` is anchored at PostUpdateEverything, so
        // a row's interval closed at the end of the *previous* update and the brain cost inside it is
        // the previous row's — which is what `FrameCost.RemainderMilliseconds` is handed for `engine_ms`.
        var attributable = measured.Where(i => i > 0).ToList();
        double total = attributable.Sum(i => (double)frame.Number[i]);
        string split = attributable.Count == 0 ? "no row has a predecessor to attribute its interval to"
            : string.Join(", ", new[] { ("brain_ms", 1), ("record_ms", 0), ("overlay_ms", 0), ("inspector_ms", 0), ("engine_ms", 0) }
            .Select(entry =>
            {
                (string name, int back) = entry;
                Column part = session[name];
                double spent = attributable.Sum(i => Math.Max(0d, (double)part.Number[i - back]));
                return $"{name[..^3]} {spent / attributable.Count:0.00} ms a frame ({(total <= 0 ? 0 : 100.0 * spent / total):0.0}%)";
            }));

        int worst = measured.OrderByDescending(i => frame.Number[i]).First();
        double draws = measured.Sum(i => (double)session["draws"].Number[i]);

        yield return new Finding(Severity.Potential, Name,
            $"{share * 100:0.0}% of measured intervals ran longer than one frame at sixty a second",
            $"{over:n0} of {measured.Count:n0} measured intervals exceeded {MeasureTheFrame.FrameBudgetMilliseconds:0.00} ms; "
            + $"the inspection threshold is {InspectionShare * 100:0}%, chosen as the line between hitches a player forgets "
            + "and a world persistently advancing slower than real time, and it is an argued line rather than a measured "
            + $"one. Mean split across those intervals: {split}. The worst interval was {frame.Number[worst]:0.00} ms at "
            + $"tick {session.Tick(worst)}. {draws:n0} draw(s) were counted inside those intervals, so the frames a second "
            + "figure is the draw count and not the interval — an engine catching up runs two updates with no draw between "
            + "them. The split names where the time went; it does not establish that any one part could be made smaller, "
            + "and `engine` is a remainder that holds Terraria's own update and anything else drawing on this machine.",
            session.Tick(measured[0]), session.Tick(measured[^1]), over);
    }
}
