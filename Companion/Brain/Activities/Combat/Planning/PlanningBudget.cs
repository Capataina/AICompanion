#nullable enable

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

/// <summary>
/// What one decision may spend simulating: a millisecond allowance for the frame and a simulation count
/// for determinism. The clock cuts where it cuts on each machine, so a millisecond-only budget plans a
/// different fight on a fast machine than a slow one and no replay can reproduce a cut; the count cuts
/// the same search at the same simulation everywhere, and the clock stays as the frame guard behind it.
/// Checked at every spawn and tick of every simulated use; the first check past either allowance marks
/// the budget cut and the simulation returns what it has. The suite lifts the millisecond allowance but
/// never the count — a count cut is the same cut in the suite as in the game, which is the point —
/// so fixtures that must not cut run <see cref="Unbounded"/>, and rows about a cut search run with
/// production allowances instead, the way every other cut row does.
/// </summary>
public sealed class PlanningBudget
{
    private readonly long allowanceMs;
    private readonly int maxSimulations;
    private readonly long startedAt;

    private PlanningBudget(long allowanceMs, int maxSimulations)
    {
        this.allowanceMs = allowanceMs;
        this.maxSimulations = maxSimulations;
        startedAt = System.Environment.TickCount64;
    }

    /// <summary>Whether the budget has run out at some check so far.</summary>
    public bool Cut { get; private set; }

    /// <summary>How many uses were simulated under this budget, for the cost strip.</summary>
    public int Simulations { get; private set; }

    /// <summary>The allowance in milliseconds, compared to <see cref="System.Environment.TickCount64"/>.
    /// A previous conversion stored TimeSpan ticks / 1000 and then compared them to TickCount64, so a
    /// configured 4 ms ran as 40 ms — the night-crowd hitch of 18 September 2026.</summary>
    public float AllowanceMilliseconds => allowanceMs == long.MaxValue ? float.PositiveInfinity : allowanceMs;

    /// <summary>The simulation count beside it, for the snapshot: a count cut replays exactly.</summary>
    public int AllowanceSimulations => maxSimulations;

    public static PlanningBudget Unbounded() => new(long.MaxValue, int.MaxValue);

    public static PlanningBudget FromMilliseconds(float milliseconds, int maxSimulations = int.MaxValue)
        => new(float.IsPositiveInfinity(milliseconds) || milliseconds >= 1e12f
            ? long.MaxValue
            : (long)System.Math.Max(0.0, milliseconds), maxSimulations);

    /// <summary>
    /// One greedy from-here price after the decision clock has already cut with nothing in the pool.
    /// Count-capped and clock-free, so a crowd that exhausted the 4 ms search still offers a shot from
    /// where the body is rather than Unresolved:budget-cut, and a replay of that fallback is the same
    /// cut on every machine.
    /// </summary>
    public static PlanningBudget FromHereFallback() => new(long.MaxValue, 256);

    /// <summary>True while the budget remains. Marks the budget cut the first time it does not: the count
    /// first, because it holds under the suite's lifted clock, then the clock behind the lift.</summary>
    public bool Check()
    {
        if (Cut) return false;
        if (Simulations >= maxSimulations)
        {
            Cut = true;
            return false;
        }
        if (Infrastructure.Movement.LimitPlanningWork.Unbounded) return true;
        if (System.Environment.TickCount64 - startedAt > allowanceMs)
        {
            Cut = true;
            return false;
        }
        return true;
    }

    public void NoteSimulation() => Simulations++;
}
