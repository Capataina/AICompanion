using System;
using System.Diagnostics;

namespace AICompanion.Companion.Brain.SharedMovementSystem;

/// <summary>One main-thread allowance shared by nested planning consumers. Offline callers
/// leave it unset so deterministic work limits, rather than machine load, decide test results.</summary>
public static class LimitPlanningWork
{
    private static long deadline;
    public static void Begin(double milliseconds) => deadline = Stopwatch.GetTimestamp()
        + (long)(milliseconds * Stopwatch.Frequency / 1000d);
    public static void End() => deadline = 0;
    public static long Deadline(double milliseconds)
    {
        long own = milliseconds > 0 ? Stopwatch.GetTimestamp() + (long)(milliseconds * Stopwatch.Frequency / 1000d) : 0;
        return deadline == 0 ? own : own == 0 ? deadline : Math.Min(deadline, own);
    }
    public static bool Expired => deadline != 0 && Stopwatch.GetTimestamp() >= deadline;
}
