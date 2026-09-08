#nullable enable

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// What the brain asks of the body for one tick, in the body's own terms and nobody else's: a
/// horizontal speed to accelerate toward (signed, so the sign is the facing), a jump at a share
/// of the full jump velocity, and whether to pass the platform underfoot. A traversal produces
/// one of these every tick both when it is simulated by the planner and when it is performed by
/// the follower, and the motor turns it into NPC velocity; nothing else ever moves the body, so
/// a move the planner proved is the move the follower makes.
/// </summary>
public readonly record struct Controls(float MoveX, bool Jump = false, float JumpScale = 1f, bool FallThrough = false)
{
    /// <summary>Ask for nothing: the body slows to a stop under its own slowdown.</summary>
    public static readonly Controls None = new(0f);
}
