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
        return deadlineSeen = timestamp() >= DeadlineTimestamp;
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
