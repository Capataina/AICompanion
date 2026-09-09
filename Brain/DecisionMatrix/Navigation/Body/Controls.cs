#nullable enable

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// What the brain asks of the body for one tick, in the body's own terms and nobody else's: a
/// horizontal speed to accelerate toward (signed, so the sign is the facing), a jump at a share
/// of the full jump velocity, whether to pass the platform underfoot, and whether the move in
/// hand is going down. A traversal produces one of these every tick both when it is simulated by
/// the planner and when it is performed by the follower, and the motor turns it into NPC
/// velocity; nothing else ever moves the body, so a move the planner proved is the move the
/// follower makes.
///
/// <see cref="Descend"/> is the tick's vertical intent, and it exists because the kerb rules have
/// to know it. Every vanilla walker computes the same bit fresh each tick — the town NPC from
/// whether it is above its home, the fighter from whether its target is below — and decides from
/// it whether the body may be lifted onto a platform at its knee. The companion's motor hard-coded
/// that permission on, so it climbed the platform it was falling through on every tick of every
/// descent while gravity ran to the fall cap: the shaft freeze of 2026-09-09.
///
/// The bit is owned by the step in hand rather than derived from where the goal is, which is the
/// one place this deliberately departs from vanilla. A fighter's rule is "the target is below me",
/// and applied to a planned route that walks *across* a platform on its way to a drop somewhere
/// else it would fall through the platform underfoot and leave the route. Intent that belongs to
/// the move cannot make that mistake.
/// </summary>
public readonly record struct Controls(float MoveX, bool Jump = false, float JumpScale = 1f, bool FallThrough = false, bool Descend = false)
{
    /// <summary>Ask for nothing: the body slows to a stop under its own slowdown.</summary>
    public static readonly Controls None = new(0f);

    /// <summary>Ask for nothing while a descent is in hand, so the kerb rules still know the body is going down.</summary>
    public static readonly Controls NoneDescending = new(0f, Descend: true);
}
