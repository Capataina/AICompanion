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
    public bool Exhausted => RemainingOperations == 0 || timestamp() >= DeadlineTimestamp;
    public double ElapsedMilliseconds => (timestamp() - started) * 1000d / frequency;
    public double OverrunMilliseconds => DeadlineTimestamp == long.MaxValue ? 0d
        : Math.Max(0d, (timestamp() - DeadlineTimestamp) * 1000d / frequency);
    public IReadOnlyDictionary<string, long> Consumed => consumed;
    public IReadOnlyDictionary<string, long> Cuts => cuts;

    public bool TrySpend(string subsystem, long operations = 1)
    {
        if (string.IsNullOrWhiteSpace(subsystem)) throw new ArgumentException("A budget consumer must be named.", nameof(subsystem));
        if (operations <= 0) throw new ArgumentOutOfRangeException(nameof(operations));
        if (operations > RemainingOperations || timestamp() >= DeadlineTimestamp)
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
