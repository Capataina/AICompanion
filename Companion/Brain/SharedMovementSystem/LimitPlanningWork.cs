using System;
using System.Diagnostics;

namespace AICompanion.Companion.Brain.SharedMovementSystem;

/// <summary>One main-thread allowance shared by nested planning consumers. The live brain tick
/// always begins one, so wall-clock time can decide how far a search got; an offline caller that
/// needs machine load out of the result sets <see cref="Unbounded"/>, which lifts every millisecond
/// allowance while leaving the work-count limits that bound each query untouched.</summary>
public static class LimitPlanningWork
{
    private static long deadline;
    /// <summary>Offline determinism only. Never set during play: an unbounded search can take a frame.</summary>
    public static bool Unbounded { get; set; }
    public static void Begin(double milliseconds) => deadline = Unbounded ? 0 : Stopwatch.GetTimestamp()
        + (long)(milliseconds * Stopwatch.Frequency / 1000d);
    public static void End() => deadline = 0;

    /// <summary>Tighten the shared deadline for one bounded piece of work, restoring the wider one
    /// when disposed, so every nested query inside (route, reach, approach) stops at the narrower
    /// share. A non-positive share leaves the outer deadline as the only bound.</summary>
    public static Scope Narrow(double milliseconds)
    {
        long prior = deadline;
        if (!Unbounded && milliseconds > 0)
        {
            long own = Stopwatch.GetTimestamp() + (long)(milliseconds * Stopwatch.Frequency / 1000d);
            deadline = deadline == 0 ? own : Math.Min(deadline, own);
        }
        return new Scope(prior);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly long prior;
        internal Scope(long prior) => this.prior = prior;
        public void Dispose() => deadline = prior;
    }
    public static long Deadline(double milliseconds)
    {
        if (Unbounded) return 0;
        long own = milliseconds > 0 ? Stopwatch.GetTimestamp() + (long)(milliseconds * Stopwatch.Frequency / 1000d) : 0;
        return deadline == 0 ? own : own == 0 ? deadline : Math.Min(deadline, own);
    }
    public static bool Expired => !Unbounded && deadline != 0 && Stopwatch.GetTimestamp() >= deadline;
    /// <summary>For a consumer timing its own loop against a local allowance rather than a deadline.</summary>
    public static bool Spent(Stopwatch clock, double milliseconds) => !Unbounded && clock.Elapsed.TotalMilliseconds >= milliseconds;
}
