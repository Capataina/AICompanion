#nullable enable

namespace AICompanion.Companion.Brain.Behaviours.Work;

/// <summary>How a work action may begin. The setting is deliberately a runtime value so a future UI can persist it without changing the action contract.</summary>
public enum WorkPolicy
{
    Disabled,
    Mimic,
    Opportunistic,
}

/// <summary>
/// Runtime work-policy surface. Mining defaults to opportunistic help: a nearby visible ore is
/// enough to start a job, while Mimic retains the player's recent-ore-hit trigger for players
/// who want help only while they are mining.
/// </summary>
public static class WorkPolicies
{
    public static WorkPolicy Mining { get => PlayerIntegration.CompanionPreferences.Current.Mining; set => PlayerIntegration.CompanionPreferences.Current.Mining = value; }
    public static WorkPolicy Chopping { get => PlayerIntegration.CompanionPreferences.Current.Chopping; set => PlayerIntegration.CompanionPreferences.Current.Chopping = value; }
}
