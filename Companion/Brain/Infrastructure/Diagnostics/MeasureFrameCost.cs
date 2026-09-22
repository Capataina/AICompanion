#nullable enable

using System;
using System.Diagnostics;
using Terraria.ModLoader;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>
/// The whole game update, measured, so the half of the frame outside the brain stops being
/// unattributed.
///
/// <b>Why it exists.</b> The 22 September 2026 capture ran at 38 updates a second against 60, and the
/// brain accounted for 10.17 ms of a 25.30 ms median gap. Subtracting the brain and the recorder left
/// a median residue of about 14 ms that nothing instrumented at all, and two candidates for it — the
/// inspector's own drawing, which was switched on for the whole session, and the engine — could not
/// be told apart by any column the capture carried. Every reading of that session therefore had to
/// say "and the rest is unattributed", which is exactly the shape of finding a recorder exists to
/// prevent.
///
/// <b>What it measures, and what a reader must not assume it measures.</b> The interval is
/// update-to-update, taken at <see cref="PostUpdateEverything"/>, and **an update is not a frame**.
/// FNA runs a fixed timestep and catches up by running two updates back to back with no draw between
/// them, which is what the capture's 6.30 ms minimum gap is; on such an update the overlay and
/// inspector costs are legitimately zero and the interval is legitimately short. So
/// <see cref="Draws"/> counts the draws that happened inside the interval, and frames a second is
/// derived from that count rather than from the interval — a figure named "frames per second" and
/// computed from update gaps is updates a second wearing the wrong name.
///
/// <b>The clocks are not the brain's.</b> Every figure here is this class's own
/// <see cref="Stopwatch"/>, started once and never restarted inside a session, so the interval and
/// the two draw costs are on one clock and are subtractable from each other. What they are *not*
/// comparable with is a figure taken any other way — the suite's own warning that two timings are
/// comparable only when taken the same way binds here as hard as anywhere, and the recorder's guide
/// states the regime of each of the five columns this feeds.
/// </summary>
public sealed class FrameCost : ModSystem
{
    /// <summary>One frame at sixty a second. A fact of the engine rather than a tunable: Terraria's
    /// fixed timestep is 1/60 s, and the number is written out rather than derived because the engine
    /// exposes no constant for it and a formula here would be a second declaration to keep honest.</summary>
    public const double FrameMilliseconds = 1000d / 60d;

    private static readonly Stopwatch clock = new();
    private static double lastUpdateAt = double.NaN;
    private static double pendingOverlay, pendingInspector;
    private static int pendingDraws;

    /// <summary>Milliseconds between this update and the one before it, or -1 on the first update of a
    /// session, where there is no previous update to measure from. A rate over no interval is
    /// unmeasured rather than zero, which is the rule the travel columns already follow.</summary>
    public static double IntervalMilliseconds { get; private set; } = -1;

    /// <summary>What the brain overlay's world layers and cost strip cost inside the measured
    /// interval, in milliseconds. Zero is a real answer: an update the engine caught up on has no draw
    /// in it, and an inspector switched off draws nothing.</summary>
    public static double OverlayMilliseconds { get; private set; }

    /// <summary>What the inspector's own panel cost inside the measured interval, in milliseconds.
    /// Separate from the overlay because they are switched on separately and one is a menu a person
    /// opens while the other keeps drawing after it closes.</summary>
    public static double InspectorMilliseconds { get; private set; }

    /// <summary>How many draws happened inside the measured interval. Usually one; zero on an update
    /// the engine ran to catch up, and this is the column that lets a reader tell that update from a
    /// slow one.</summary>
    public static int Draws { get; private set; }

    public override void Load() => clock.Restart();

    /// <summary>Forgets the previous session's interval, so a world load does not report the time
    /// spent loading as one enormous frame.</summary>
    public static void Reset()
    {
        if (!clock.IsRunning) clock.Restart();
        lastUpdateAt = double.NaN;
        IntervalMilliseconds = -1;
        OverlayMilliseconds = InspectorMilliseconds = 0;
        Draws = 0;
        pendingOverlay = pendingInspector = 0;
        pendingDraws = 0;
    }

    public override void OnWorldLoad() => Reset();

    /// <summary>
    /// Closes the previous interval and opens the next one.
    ///
    /// <b>The anchor is the end of an update rather than its start, and that is a fact about the
    /// runtime rather than a preference.</b> `ModSystem` on this tModLoader exposes no
    /// `PreUpdateEverything`; `PostUpdateEverything` is the hook this folder already uses, and the
    /// interval between two consecutive calls of it is one whole update plus the draws around it,
    /// which is the same quantity measured from the other end. The consequence a reader has to hold is
    /// the phase: a row written inside update N sees the interval that closed at the end of update
    /// N−1, so `frame_ms` describes the update before the row's own. Every figure subtracted from it
    /// is that same update's — the previous row's brain, and `record_ms`, which is already the
    /// previous row's cost — so the remainder is a quantity rather than a mixture.
    /// </summary>
    public override void PostUpdateEverything()
    {
        double now = clock.Elapsed.TotalMilliseconds;
        IntervalMilliseconds = double.IsNaN(lastUpdateAt) ? -1 : now - lastUpdateAt;
        lastUpdateAt = now;
        OverlayMilliseconds = pendingOverlay;
        InspectorMilliseconds = pendingInspector;
        Draws = pendingDraws;
        pendingOverlay = pendingInspector = 0;
        pendingDraws = 0;
    }

    /// <summary>Times one section of drawing into the interval being accumulated. The caller brackets
    /// its own work rather than this class hooking the draw, because the overlay and the inspector are
    /// two sections of one callback and only the caller knows which is which.</summary>
    public readonly struct Section : IDisposable
    {
        private readonly double began;
        private readonly bool inspector;
        internal Section(bool inspector)
        {
            this.inspector = inspector;
            began = clock.Elapsed.TotalMilliseconds;
        }
        public void Dispose()
        {
            double cost = clock.Elapsed.TotalMilliseconds - began;
            if (inspector) pendingInspector += cost; else pendingOverlay += cost;
        }
    }

    /// <summary>Counts one draw into the interval being accumulated. Called from the top of the draw
    /// callback before anything can return, so a session with the inspector switched off still reports
    /// how many draws its updates carried — which is the whole of how frames a second is separated
    /// from updates a second.</summary>
    public static void CountDraw() => pendingDraws++;

    /// <summary>Times the brain overlay's world layers and cost strip.</summary>
    public static Section TimeOverlay() => new(inspector: false);

    /// <summary>Times the inspector's own panel.</summary>
    public static Section TimeInspector() => new(inspector: true);

    /// <summary>
    /// What is left of the interval once everything this mod can account for is taken out of it: the
    /// brain, the recorder's own write, and the two drawing sections.
    ///
    /// <b>The brain figure is the previous row's, deliberately, and the regime matters more than the
    /// arithmetic.</b> The interval a row can see is the one that closed at the end of the update
    /// before it, so the brain cost inside it is that update's and not this row's. The recorder's own
    /// `record_ms` is already the previous row's for the same reason — a row cannot contain the time
    /// spent writing itself — so the two line up on one update and the remainder is a quantity rather
    /// than a mixture. Reading this row's `brain_ms` out of it instead would be off by one tick's
    /// brain, which on the 22 September capture is about 40% of the frame.
    ///
    /// It is a remainder and not a measurement of the engine, and the column's name says only that it
    /// is what is left: Terraria's own update, the health notch, the profile card, and anything
    /// another mod draws all sit inside it, and so does any cost this mod has that nothing here times.
    /// A negative value is possible in principle — the clocks are separate and a very short interval
    /// can be over-subtracted — and it is written as it comes out rather than clamped, because a
    /// clamp turns an instrument fault into a plausible zero.
    /// </summary>
    public static double RemainderMilliseconds(double previousBrainMilliseconds, double recordMilliseconds)
    {
        if (IntervalMilliseconds < 0) return -1;
        double accounted = OverlayMilliseconds + InspectorMilliseconds;
        if (previousBrainMilliseconds > 0) accounted += previousBrainMilliseconds;
        if (recordMilliseconds > 0) accounted += recordMilliseconds;
        return IntervalMilliseconds - accounted;
    }
}
