#nullable enable

using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

/// <summary>Named tactical charges shared by the simulator and planner.</summary>
internal static class SpendCombatDecisionWork
{
    public static bool Expand(this DecisionWorkBudget budget) => budget.TrySpend("combat-tactical-search");
    public static bool Simulate(this DecisionWorkBudget budget) => budget.TrySpend("combat-simulation");
    // Existing search loops use Check at every suspension point. Keeping that idiom while the
    // planner migrates makes each point charge the shared allowance rather than a local counter.
    public static bool Check(this DecisionWorkBudget budget) => budget.TrySpend("combat-tactical-search");
    public static void NoteSimulation(this DecisionWorkBudget budget) { }
}
