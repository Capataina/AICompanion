#nullable enable

namespace AICompanion.Brain.DecisionMatrix.Decision;

/// <summary>
/// Every tunable number of the brain in one place, so a playtest note ("it hugs too
/// close", "it gives up on loot too soon") is one edit here and nowhere else.
/// </summary>
public static class Weights
{
    /// <summary>Bonus multiplier the running action keeps, so scores do not flicker.</summary>
    public const float Commitment = 1.15f;

    /// <summary>How fast the horizon charge falls once an action would outlast the horizon, in ticks of overrun to zero.</summary>
    public const float HorizonOverrunToZero = 240f;

    /// <summary>Distance band to the player when calm, in px: closer than Near or further than Far scores worse.</summary>
    public const float CalmBandNear = 96f;
    public const float CalmBandFar = 560f;

    /// <summary>The band when the player is in danger.</summary>
    public const float ThreatBandNear = 32f;
    public const float ThreatBandFar = 160f;

    /// <summary>Beyond this the companion drops everything and comes back, whatever else is going on.</summary>
    public const float LeashHard = 1400f;

    public const float WanderFloor = 0.05f;
    public const float FollowIntentDistance = 140f;

    /// <summary>Loot: value of the nearest pickup fades with distance over this many px.</summary>
    public const float LootReach = 900f;
    public const int LootTripTicksPerPx = 1; // approximates 1 px per tick allowing for jumps

    /// <summary>Hunt: how far beyond the screen a target is still worth chasing.</summary>
    public const float HuntReach = 1100f;

    /// <summary>Kite: a walker inside this many px is "on top of me".</summary>
    public const float KiteTrigger = 64f;

    /// <summary>Reflex: a threat whose predicted hitbox meets the companion inside this many ticks triggers a dodge.</summary>
    public const int DodgeLookaheadTicks = 20;

    /// <summary>
    /// Positioner: how many feet tiles the flood from the companion's feet may visit when it asks
    /// which candidate spots are reachable. Spent once per rescore, not per candidate.
    /// </summary>
    public const int ReachFloodBudget = 400;
}
