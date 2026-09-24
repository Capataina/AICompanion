#nullable enable

using System;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>
/// Whether one tick's brain cost is an outlier against the run's own recent ticks — never against a number of
/// milliseconds.
///
/// <para><b>Why relative.</b> The owner ruled on 24 September 2026 that no cost is judged by a hard-coded line,
/// because "two milliseconds for a specific playthrough might look a lot different than another playthrough": a
/// slower machine, a heavier modpack or a busier world moves every tick together, and what a player feels is the
/// tick that stands out from the ones around it. So the fence is Tukey's far-out rule over a rolling window,
/// upper quartile plus three interquartile ranges, which is unit-free and moves with the machine: multiply every
/// cost by a constant and the fence moves by the same constant, so a uniformly slower run reports exactly the
/// spikes a faster one does.</para>
///
/// <para><b>Three choices tune the instrument rather than the brain, and each is named at its constant.</b> The
/// window is <see cref="WindowTicks"/>; a tick is judged against the window <i>before</i> it joins it, so a spike
/// cannot raise its own fence; and the fence never sits below <see cref="FenceFloorOverMedian"/> times the window's
/// median, which is what keeps a near-constant cost — an interquartile range near zero, where the Tukey fence
/// collapses onto the upper quartile — from calling a tick a few percent above typical a spike. Nothing is judged
/// until the window is full, because the first seconds of a session are JIT compilation and a fence estimated from
/// a handful of ticks is noise.</para>
///
/// <para><b>No allocation per tick.</b> The window is kept twice in preallocated arrays: a ring in arrival order,
/// to know which value leaves, and the same values sorted, maintained by one binary search and one block move per
/// arrival and per departure, so the quartiles are two index reads rather than a sort. The cost is two
/// <see cref="Array.Copy(Array, int, Array, int, int)"/> moves of at most the window's length a tick.</para>
///
/// <para>Game-free and brain-free, so <c>Tools/EngineReplay/Observation/VerifyBrainSectionProfiler.cs</c> drives it
/// with synthetic series: one injected outlier fires, a steady series and a slowly rising one stay silent.</para>
/// </summary>
public sealed class DetectCostSpikes
{
    /// <summary>
    /// Ticks in the rolling window: ten seconds at the engine's sixty a second. Long enough that the brain's own
    /// periodic work — a positioner rescore, a reach re-root, a decision spanning a few ticks — is part of the
    /// distribution many times over rather than an event in it, and that a quartile is estimated from hundreds of
    /// ticks. Short enough that a regime change the player also feels, a fight starting, becomes the new normal
    /// within seconds rather than reporting every tick of the fight as a spike against the calm before it.
    /// </summary>
    public const int WindowTicks = 600;

    /// <summary>Tukey's far-out multiplier: upper quartile plus this many interquartile ranges. Three rather than
    /// the 1.5 of the ordinary outlier rule, because the brain's cost is heavy-tailed by design — a decision
    /// settling costs several times a carried tick — and the question is the tick nobody would call ordinary.</summary>
    public const double InterquartileMultiplier = 3.0;

    /// <summary>
    /// The fence never sits below this multiple of the window's median. A unit-free floor rather than a
    /// millisecond one: with a near-constant cost the interquartile range is near zero, the Tukey fence collapses
    /// onto the upper quartile, and a tick a few percent above typical would count as a spike. Doubling the
    /// typical tick is the least a player could feel as a hitch in the brain's share of a frame.
    /// </summary>
    public const double FenceFloorOverMedian = 2.0;

    private readonly double[] ring;
    private readonly double[] sorted;
    private int count;
    private int next;

    public DetectCostSpikes(int windowTicks = WindowTicks)
    {
        if (windowTicks < 8) throw new ArgumentOutOfRangeException(nameof(windowTicks), windowTicks, "a quartile needs a window of at least eight ticks");
        ring = new double[windowTicks];
        sorted = new double[windowTicks];
    }

    /// <summary>The window's length.</summary>
    public int Window => ring.Length;

    /// <summary>Ticks held so far, up to <see cref="Window"/>.</summary>
    public int Count => count;

    /// <summary>Whether the window is full, which is when a fence exists at all.</summary>
    public bool Ready => count == ring.Length;

    /// <summary>The judgement of the last observed tick, against the window as it stood before that tick joined it.</summary>
    public Verdict Last { get; private set; }

    /// <summary>
    /// One tick's verdict: whether it crossed, the fence it was judged against and the three order statistics the
    /// fence was computed from, so a reader can recompute it. The fence is NaN while the window fills.
    /// </summary>
    public readonly record struct Verdict(bool Spike, double Cost, double Fence, double LowerQuartile, double Median, double UpperQuartile)
    {
        /// <summary>How the fence was computed, in the words a reader of the occurrence needs to recompute it.</summary>
        public string Rule(int window) => FormattableString.Invariant(
            $"max(q3+{InterquartileMultiplier:0.#}*iqr, {FenceFloorOverMedian:0.#}*median) over the previous {window} brain ticks");
    }

    /// <summary>
    /// Judges <paramref name="cost"/> against the window, then adds it. A non-finite or negative cost is not a
    /// measurement and is neither judged nor kept.
    /// </summary>
    public Verdict Observe(double cost)
    {
        if (!double.IsFinite(cost) || cost < 0)
            return Last = new Verdict(false, cost, double.NaN, double.NaN, double.NaN, double.NaN);
        Verdict verdict;
        if (Ready)
        {
            double q1 = sorted[(ring.Length - 1) / 4];
            double median = sorted[(ring.Length - 1) / 2];
            double q3 = sorted[3 * (ring.Length - 1) / 4];
            double fence = Math.Max(q3 + InterquartileMultiplier * (q3 - q1), FenceFloorOverMedian * median);
            verdict = new Verdict(cost > fence, cost, fence, q1, median, q3);
        }
        else verdict = new Verdict(false, cost, double.NaN, double.NaN, double.NaN, double.NaN);

        if (Ready)
        {
            // The oldest value leaves the sorted copy before the new one enters it.
            Remove(ring[next]);
        }
        else count++;
        ring[next] = cost;
        next = (next + 1) % ring.Length;
        Insert(cost);
        return Last = verdict;
    }

    private int Held => Ready ? ring.Length - 1 : count - 1;

    private void Remove(double value)
    {
        int size = ring.Length;
        int at = Array.BinarySearch(sorted, 0, size, value);
        if (at < 0) return; // unreachable: every value in the ring is in the sorted copy
        Array.Copy(sorted, at + 1, sorted, at, size - at - 1);
    }

    private void Insert(double value)
    {
        // `Held` values are in the sorted copy before this one; after a removal that is the window less one.
        int size = Held;
        int at = Array.BinarySearch(sorted, 0, size, value);
        if (at < 0) at = ~at;
        Array.Copy(sorted, at, sorted, at + 1, size - at);
        sorted[at] = value;
    }

    /// <summary>Empties the window, for a new session.</summary>
    public void Reset()
    {
        count = 0;
        next = 0;
        Last = default;
        Array.Clear(ring);
        Array.Clear(sorted);
    }
}
