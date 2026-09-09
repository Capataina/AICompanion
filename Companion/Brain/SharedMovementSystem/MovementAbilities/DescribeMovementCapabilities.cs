namespace AICompanion.Companion.Brain.SharedMovementSystem;

/// <summary>
/// Abilities the body has, separate from <see cref="MobilityState"/>'s consumable state. A
/// live adapter supplies this once per tick; the simulator receives the same value, so adding a
/// mastery ability changes one rule rather than creating a planner-only move.
/// </summary>
public readonly record struct MovementCapabilities(int AirJumpCount = 0, bool CanDash = false, bool CanSwim = false, bool CanFly = false)
{
    /// <summary>The shipping companion has its ground jump only.</summary>
    public static readonly MovementCapabilities Basic = new();
}
