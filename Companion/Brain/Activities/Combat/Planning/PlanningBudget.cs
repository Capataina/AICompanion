#nullable enable

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

/// <summary>
/// The milliseconds a decision may spend simulating. Checked at every spawn and tick of every simulated use;
/// the first check past the allowance marks the budget cut and the simulation returns what it has, so a slow
/// frame degrades to fewer simulated aims rather than a missed tick. Fixtures run unbounded, the way the suite
/// lifts every allowance.
/// </summary>
public sealed class PlanningBudget
{
    private readonly long allowanceTicks;
    private readonly long startedAt;

    private PlanningBudget(long allowanceTicks)
    {
        this.allowanceTicks = allowanceTicks;
        startedAt = System.Environment.TickCount64;
    }

    /// <summary>Whether the budget has run out at some check so far.</summary>
    public bool Cut { get; private set; }

    /// <summary>How many uses were simulated under this budget, for the cost strip.</summary>
    public int Simulations { get; private set; }

    public static PlanningBudget Unbounded() => new(long.MaxValue);

    public static PlanningBudget FromMilliseconds(float milliseconds)
        => new((long)(milliseconds * System.TimeSpan.TicksPerMillisecond / 1000f));

    /// <summary>True while the budget remains. Marks the budget cut the first time it does not.</summary>
    public bool Check()
    {
        if (Cut) return false;
        if (System.Environment.TickCount64 - startedAt > allowanceTicks)
        {
            Cut = true;
            return false;
        }
        return true;
    }

    public void NoteSimulation() => Simulations++;
}
