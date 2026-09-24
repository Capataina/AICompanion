#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Computation;

/// <summary>A borrowed allowance, never a new deadline inside a nested planner. Operations
/// make suspension reproducible; the monotonic clock independently limits production work.</summary>
public sealed class DecisionWorkBudget
{
    private readonly Func<long> timestamp;
    private readonly long frequency;
    private readonly long started;
    private readonly Dictionary<string, long> consumed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> cuts = new(StringComparer.Ordinal);

    public DecisionWorkBudget(double milliseconds, long operationAllowance = long.MaxValue,
        Func<long>? timestamp = null, long frequency = 0)
    {
        if (double.IsNaN(milliseconds) || milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if (operationAllowance < 0) throw new ArgumentOutOfRangeException(nameof(operationAllowance));
        this.timestamp = timestamp ?? Stopwatch.GetTimestamp;
        // An injected clock is a fixture's reproducible one and is read on every check, so a row that cuts
        // at an exact tick of it still does; the real clock is read once per stride, below.
        clockStride = timestamp == null ? RealClockStride : 1;
        this.frequency = frequency == 0 ? Stopwatch.Frequency : frequency;
        if (this.frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        started = this.timestamp();
        AllowanceMilliseconds = milliseconds;
        double ticks = milliseconds * this.frequency / 1000d;
        DeadlineTimestamp = double.IsPositiveInfinity(ticks) || ticks >= long.MaxValue - started
            ? long.MaxValue : started + (long)ticks;
        OperationAllowance = operationAllowance;
    }

    public long DeadlineTimestamp { get; }
    public double AllowanceMilliseconds { get; }
    public long OperationAllowance { get; }
    public long OperationsUsed { get; private set; }
    public long RemainingOperations => OperationAllowance - OperationsUsed;
    public bool Cut { get; private set; }
    public string FirstCutSubsystem { get; private set; } = "";
    public bool Exhausted => RemainingOperations == 0 || DeadlinePassed();

    /// <summary>
    /// How many deadline checks share one read of the real clock. The course's model scheduler asks
    /// <see cref="Exhausted"/> once per single operation, and a clock read per ask was 8.4% of the brain
    /// thread on the replay of the 22 September 2026 capture (profiled 23 September 2026) — the read, not
    /// the work it guarded. Reading every sixteenth check lets up to fifteen further *checks* through after
    /// the deadline has passed, and the bound is in checks rather than operations: a check can be a
    /// multi-operation `TrySpend`, or an `Exhausted` poll followed by work that is not metered. The route
    /// search, the reach flood and course travel read the clock themselves on every expansion
    /// (`LimitPlanningWork.Deadline`), so the coarse work stays prompt; the time a late check can cost is
    /// not measured, and <see cref="OverrunMilliseconds"/> is where it shows. Once the deadline is seen
    /// passed it stays passed. Measured within one replay basin, whole-brain p99 fell 1.5–2.3 ms with the
    /// stride in (23 September 2026).
    /// </summary>
    private const int RealClockStride = 16;
    private readonly int clockStride;
    private int checksSinceClock;
    private bool deadlineSeen;

    private bool DeadlinePassed()
    {
        if (deadlineSeen) return true;
        if (DeadlineTimestamp == long.MaxValue) return false;
        if (++checksSinceClock < clockStride) return false;
        checksSinceClock = 0;
        // The real clock is asked through the decision clock, so a recorded session can hand its replay the same
        // answer; an injected clock is a fixture's own reproducible one and is asked directly.
        return deadlineSeen = clockStride == RealClockStride
            ? DecisionClock.Passed(DeadlineTimestamp)
            : timestamp() >= DeadlineTimestamp;
    }
    public double ElapsedMilliseconds => (timestamp() - started) * 1000d / frequency;
    public double OverrunMilliseconds => DeadlineTimestamp == long.MaxValue ? 0d
        : Math.Max(0d, (timestamp() - DeadlineTimestamp) * 1000d / frequency);
    public IReadOnlyDictionary<string, long> Consumed => consumed;
    public IReadOnlyDictionary<string, long> Cuts => cuts;

    public bool TrySpend(string subsystem, long operations = 1)
    {
        if (string.IsNullOrWhiteSpace(subsystem)) throw new ArgumentException("A budget consumer must be named.", nameof(subsystem));
        if (operations <= 0) throw new ArgumentOutOfRangeException(nameof(operations));
        if (operations > RemainingOperations || DeadlinePassed())
        {
            if (!Cut) FirstCutSubsystem = subsystem;
            Cut = true;
            cuts[subsystem] = cuts.GetValueOrDefault(subsystem) + 1;
            return false;
        }
        OperationsUsed += operations;
        consumed[subsystem] = consumed.GetValueOrDefault(subsystem) + operations;
        return true;
    }
}

/// <summary>
/// The one question a decision asks the wall clock — has this deadline passed — so that a recorded session can
/// hand its replay the same answers in the same order.
///
/// <para>Three places cut work by the clock and every one of them asks here: the allowance's own strided check
/// above, <c>LimitPlanningWork.Expired</c> against the narrowed deadline, and the route search's per-expansion
/// check. An operation count cannot stand in for them, which is why this exists rather than a recorded
/// <see cref="DecisionWorkBudget.OperationsUsed"/>: the route search reads its own deadline between metered
/// expansions, <c>Narrow</c> tightens a deadline no operation count knows about, and an <c>Exhausted</c> poll
/// that returns false lets unmetered work run before the next poll sees the cut. Replaying the *answers*
/// reproduces every one of those cuts at the check where the play took it, whatever the replaying machine's
/// speed.</para>
///
/// <para>Default-inert. With no tape running and no replay installed, <see cref="Passed"/> is exactly the clock
/// comparison each caller made before it. The recorder starts a tape at the top of the companion's tick and ends
/// it at the bottom (<c>ReplayInputs</c>), which costs one branch and, on a tick that asks, a counter bump per
/// question. <see cref="Replay"/> is harness-only: the world run's <c>--reproduce</c> installs one tick's answers
/// before that tick and nothing in the mod ever calls it.</para>
///
/// <para>The tape is run-length encoded, alternating from "not passed": <c>812.1.30.4</c> is 812 answers of not
/// passed, one passed, thirty not, four passed. A tick that asked nothing writes an empty tape.</para>
/// </summary>
public static class DecisionClock
{
    private static bool taping;
    private static readonly List<int> runs = new();
    private static bool runValue;
    private static int runLength;

    private static int[]? replay;
    private static int replayRun;
    private static int replayLeftInRun;

    /// <summary>Whether a harness has installed recorded answers.</summary>
    public static bool Replaying => replay != null;

    /// <summary>Questions asked after the installed answers ran out. Nonzero means the replay asked more than the
    /// play did, which is a divergence upstream of the clock rather than a clock fault.</summary>
    public static int ReplayOverruns { get; private set; }

    /// <summary>Recorded answers the replay never asked for this tick: the play asked more than the replay did.</summary>
    public static int ReplayUnasked
    {
        get
        {
            if (replay == null) return 0;
            int left = replayLeftInRun;
            for (int run = replayRun + 1; run < replay.Length; run++) left += replay[run];
            return left;
        }
    }

    /// <summary>Whether <paramref name="deadline"/>, a <see cref="Stopwatch"/> timestamp, has passed.</summary>
    public static bool Passed(long deadline)
    {
        if (replay != null) return NextRecordedAnswer();
        bool passed = Stopwatch.GetTimestamp() >= deadline;
        if (taping) Append(passed);
        return passed;
    }

    /// <summary>Begin recording this tick's answers, discarding any tape left open.</summary>
    public static void StartTape()
    {
        taping = true;
        runs.Clear();
        runValue = false;
        runLength = 0;
    }

    /// <summary>Stop recording and return the tick's tape, empty when nothing asked.</summary>
    public static string EndTape()
    {
        taping = false;
        if (runLength > 0 || runs.Count > 0) runs.Add(runLength);
        string tape = runs.Count == 0 ? "" : string.Join(".", runs);
        runs.Clear();
        runLength = 0;
        runValue = false;
        return tape;
    }

    /// <summary>Harness-only: answer every question from <paramref name="tape"/> until <see cref="StopReplaying"/>.
    /// A malformed tape is refused with its shape named, because a replay that silently read part of one is cutting
    /// its searches somewhere nobody chose.</summary>
    public static void Replay(string tape)
    {
        var parsed = new List<int>();
        if (tape.Length > 0)
            foreach (string part in tape.Split('.'))
            {
                if (!int.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int length) || length < 0)
                    throw new FormatException($"a decision-clock tape is dot-separated non-negative run lengths; got \"{tape}\" with the part \"{part}\"");
                parsed.Add(length);
            }
        replay = parsed.ToArray();
        replayRun = 0;
        replayLeftInRun = replay.Length == 0 ? 0 : replay[0];
        ReplayOverruns = 0;
    }

    /// <summary>Harness-only: go back to the real clock.</summary>
    public static void StopReplaying()
    {
        replay = null;
        replayRun = 0;
        replayLeftInRun = 0;
    }

    private static void Append(bool passed)
    {
        if (passed != runValue)
        {
            runs.Add(runLength);
            runValue = passed;
            runLength = 0;
        }
        runLength++;
    }

    private static bool NextRecordedAnswer()
    {
        int[] tape = replay!;
        while (replayLeftInRun == 0 && replayRun + 1 < tape.Length)
            replayLeftInRun = tape[++replayRun];
        if (replayLeftInRun == 0)
        {
            // The play never asked this question. Answering "passed" ends whatever work asked it at once, so a
            // replay that has already diverged cannot run an unbounded search on the strength of it.
            ReplayOverruns++;
            return true;
        }
        replayLeftInRun--;
        return (replayRun & 1) == 1;
    }
}
