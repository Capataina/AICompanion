using System;
using System.Diagnostics;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>One main-thread allowance shared by nested planning consumers. The live brain tick
/// always begins one, so wall-clock time can decide how far a search got; an offline caller that
/// needs machine load out of the result sets <see cref="Unbounded"/>, which lifts every millisecond
/// allowance while leaving the work-count limits that bound each query untouched.</summary>
public static class LimitPlanningWork
{
    private static long deadline;
    private static DecisionWorkBudget? current;
    public static DecisionWorkBudget Current => current
        ?? throw new InvalidOperationException("Decision work must begin before borrowing its budget.");
    public static bool IsActive => current != null;
    /// <summary>Offline determinism only. Never set during play: an unbounded search can take a frame.</summary>
    public static bool Unbounded { get; set; }
    public static void Begin(double milliseconds) => Begin(new DecisionWorkBudget(Unbounded ? double.PositiveInfinity : milliseconds));
    public static void Begin(DecisionWorkBudget budget)
    {
        if (current != null) throw new InvalidOperationException("A nested planner cannot replace the decision budget.");
        current = budget ?? throw new ArgumentNullException(nameof(budget));
        deadline = budget.DeadlineTimestamp == long.MaxValue ? 0 : budget.DeadlineTimestamp;
    }
    public static void End() { deadline = 0; current = null; }

    /// <summary>Take over the allowance rather than nest inside it: end whatever is standing and begin
    /// this one. Production never calls it — the brain tick owns exactly one Begin/End pair, and the
    /// guard in <see cref="Begin(DecisionWorkBudget)"/> is what stops a nested planner minting a second
    /// deadline after exhausting the first. A harness is the other case: a per-case reset installs the
    /// allowance every fixture then borrows, and a fixture whose subject *is* a deadline installs its
    /// own over that one. Both are owners replacing an owner, which is what this says and Begin does not.</summary>
    public static void Restart(double milliseconds) { End(); Begin(milliseconds); }
    public static void Restart(DecisionWorkBudget budget) { End(); Begin(budget); }

    /// <summary>Hold the allowance for the duration of one owner's work and give back whatever was
    /// standing when it started, rather than leaving nothing behind.
    ///
    /// The brain tick is the production owner and finds nothing standing, so here this is exactly the
    /// Begin/End pair it always had. It matters in a harness: a per-case reset installs an ambient
    /// allowance so a fixture entering *below* the tick — driving an activity's own Prepare, or the
    /// aimer — can borrow one at all, and a fixture that then drives a whole brain tick must not
    /// destroy that ambient allowance on its way out. An unconditional End did exactly that, so the
    /// row after it in the same case borrowed nothing and threw.</summary>
    public static Ownership Own(double milliseconds)
    {
        DecisionWorkBudget? previous = current;
        long previousDeadline = deadline;
        current = null;
        deadline = 0;
        Begin(milliseconds);
        return new Ownership(previous, previousDeadline);
    }

    public readonly struct Ownership : IDisposable
    {
        private readonly DecisionWorkBudget? previous;
        private readonly long previousDeadline;
        internal Ownership(DecisionWorkBudget? previous, long previousDeadline)
        {
            this.previous = previous;
            this.previousDeadline = previousDeadline;
        }
        public void Dispose() { current = previous; deadline = previousDeadline; }
    }

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
    public static bool Expired => current?.Exhausted == true || (!Unbounded && deadline != 0 && Stopwatch.GetTimestamp() >= deadline);
    /// <summary>For a consumer timing its own loop against a local allowance rather than a deadline.</summary>
    public static bool Spent(Stopwatch clock, double milliseconds) => !Unbounded && clock.Elapsed.TotalMilliseconds >= milliseconds;
}
