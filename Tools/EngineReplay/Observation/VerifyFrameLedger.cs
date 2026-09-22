#nullable enable

extern alias live;

using live::AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>
/// The producer half of the frame ledger: what one update's interval, its draws and its remainder come
/// out as, with no clock threshold anywhere in it.
///
/// <b>Every row here asserts a relationship rather than a duration.</b> A fixture that slept for twenty
/// milliseconds and required an interval near twenty would be a wall-clock threshold judged in a
/// regime no row declares, which is the shape this suite already carries two flaking rows of. What is
/// asserted instead is what cannot depend on the machine: that an interval with no predecessor is
/// unmeasured rather than fast, that the draws counted are the draws that happened, that a section
/// timed while nothing was drawn contributes nothing, and that the remainder is exactly the interval
/// less the four parts subtracted from it.
/// </summary>
internal static class VerifyFrameLedger
{
    private const string Family = "frame ledger";

    public static int Run()
    {
        int failed = 0;
        failed += RunOneRow.Case("the first interval of a session is unmeasured rather than fast", TheFirstIntervalIsUnmeasured, Family);
        failed += RunOneRow.Case("an update carries the draws that happened inside it", AnUpdateCarriesItsOwnDraws, Family);
        failed += RunOneRow.Case("an update the engine caught up on carries no draw and no drawing cost", ACatchUpUpdateCarriesNoDraw, Family);
        failed += RunOneRow.Case("the remainder is the interval less every part this mod can name", TheRemainderIsWhatIsLeft, Family);
        return failed;
    }

    /// <summary>A session's first update has no predecessor to measure from, and -1 says so. Reporting
    /// zero would make the opening of every capture the fastest frame in it.</summary>
    private static void TheFirstIntervalIsUnmeasured()
    {
        var ledger = new FrameCost();
        FrameCost.Reset();
        ledger.PostUpdateEverything();
        Require(FrameCost.IntervalMilliseconds < 0,
            $"the first update of a session must read as unmeasured, not as a fast frame; interval={FrameCost.IntervalMilliseconds}");
        Require(FrameCost.RemainderMilliseconds(9d, 1d) < 0,
            $"a remainder taken from an unmeasured interval must itself be unmeasured; remainder={FrameCost.RemainderMilliseconds(9d, 1d)}");
        ledger.PostUpdateEverything();
        Require(FrameCost.IntervalMilliseconds >= 0,
            $"the second update has a predecessor and must be measured; interval={FrameCost.IntervalMilliseconds}");
    }

    private static void AnUpdateCarriesItsOwnDraws()
    {
        var ledger = new FrameCost();
        FrameCost.Reset();
        ledger.PostUpdateEverything();
        FrameCost.CountDraw();
        using (FrameCost.TimeOverlay()) { }
        using (FrameCost.TimeInspector()) { }
        ledger.PostUpdateEverything();
        Require(FrameCost.Draws == 1, $"one draw inside the interval must be counted once; draws={FrameCost.Draws}");
        Require(FrameCost.OverlayMilliseconds >= 0 && FrameCost.InspectorMilliseconds >= 0,
            "a timed drawing section cannot contribute a negative cost");
        // And the accumulators are per interval rather than per session: a second update with no draw
        // in it must not inherit the first's.
        ledger.PostUpdateEverything();
        Require(FrameCost.Draws == 0 && FrameCost.OverlayMilliseconds == 0 && FrameCost.InspectorMilliseconds == 0,
            $"the accumulators must reset with the interval; draws={FrameCost.Draws} overlay={FrameCost.OverlayMilliseconds}"
                + $" inspector={FrameCost.InspectorMilliseconds}");
    }

    /// <summary>The distinction the whole column set exists for. The engine catches up by running two
    /// updates back to back with no draw between them, so an update carrying zero draws is a real and
    /// ordinary state — and a reader deriving frames a second from the interval would call it a fast
    /// frame.</summary>
    private static void ACatchUpUpdateCarriesNoDraw()
    {
        var ledger = new FrameCost();
        FrameCost.Reset();
        ledger.PostUpdateEverything();
        FrameCost.CountDraw();
        ledger.PostUpdateEverything();
        int drawn = FrameCost.Draws;
        ledger.PostUpdateEverything();
        Require(drawn == 1 && FrameCost.Draws == 0,
            $"a drawn update and a caught-up one must be distinguishable by their draw count; drawn={drawn} caught-up={FrameCost.Draws}");
        Require(FrameCost.IntervalMilliseconds >= 0,
            "a caught-up update still has a measured interval; it is the draw count that separates it, not the interval");
    }

    private static void TheRemainderIsWhatIsLeft()
    {
        var ledger = new FrameCost();
        FrameCost.Reset();
        ledger.PostUpdateEverything();
        FrameCost.CountDraw();
        using (FrameCost.TimeOverlay()) { }
        ledger.PostUpdateEverything();
        double interval = FrameCost.IntervalMilliseconds;
        double overlay = FrameCost.OverlayMilliseconds, inspector = FrameCost.InspectorMilliseconds;
        double remainder = FrameCost.RemainderMilliseconds(9d, 1d);
        double expected = interval - 9d - 1d - overlay - inspector;
        Require(System.Math.Abs(remainder - expected) < 1e-9,
            $"the remainder must be the interval less the previous row's brain, this row's record cost and both drawing"
                + $" sections; interval={interval:0.000} overlay={overlay:0.000} inspector={inspector:0.000}"
                + $" remainder={remainder:0.000} expected={expected:0.000}");
        // A brain or record figure the recorder has not got yet is left out rather than subtracted as a
        // zero-or-negative, because a session's first rows carry `-` for the record cost and a NaN
        // would poison every remainder after it.
        Require(System.Math.Abs(FrameCost.RemainderMilliseconds(0d, double.NaN) - (interval - overlay - inspector)) < 1e-9,
            "an unrecorded brain or record cost must be left out of the remainder rather than subtracted");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
