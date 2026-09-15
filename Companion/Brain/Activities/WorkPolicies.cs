#nullable enable

namespace AICompanion.Companion.Brain.Activities;

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

    /// <summary>Whether the player's mining list lets the companion take this ore tile as work.</summary>
    public static bool MinesOre(int tileType) => PlayerIntegration.CompanionPreferences.Current.MiningList.Allows(tileType);

    /// <summary>The list instance and its revision, so a retained answer about which ores are allowed can tell it is stale; another character's list is another instance.</summary>
    public static (object List, int Revision) MiningListVersion
        => (PlayerIntegration.CompanionPreferences.Current.MiningList, PlayerIntegration.CompanionPreferences.Current.MiningList.Revision);
}
