#nullable enable

using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

/// <summary>Named tactical charges shared by the simulator and planner.</summary>
public static class SpendCombatDecisionWork
{
    /// <summary>The subsystem a simulated use is charged to. Named once here because the audit
    /// reads the same counter the search writes; a second copy of the string in the tool would
    /// keep compiling after this one moved and would silently count nothing.</summary>
    public const string SimulationSubsystem = "combat-simulation";

    public static bool Expand(this DecisionWorkBudget budget) => budget.TrySpend("combat-tactical-search");
    public static bool Simulate(this DecisionWorkBudget budget) => budget.TrySpend(SimulationSubsystem);

    /// <summary>How many uses this allowance actually simulated. The old `PlanningBudget` carried its
    /// own simulation counter; the shared allowance counts every consumer, so the simulation count is
    /// this one named share of it rather than `OperationsUsed`.</summary>
    public static long Simulations(this DecisionWorkBudget budget)
        => budget.Consumed.TryGetValue(SimulationSubsystem, out long spent) ? spent : 0;
    // Existing search loops use Check at every suspension point. Keeping that idiom while the
    // planner migrates makes each point charge the shared allowance rather than a local counter.
    public static bool Check(this DecisionWorkBudget budget) => budget.TrySpend("combat-tactical-search");
    public static void NoteSimulation(this DecisionWorkBudget budget) { }
}
