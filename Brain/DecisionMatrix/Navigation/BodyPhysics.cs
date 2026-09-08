#nullable enable

using System;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// The companion body's movement numbers and the one-tick rules that use them, in the
/// navigation core so the motor, the reflex simulation, the planner's simulated jumps and
/// the replay tool all move the body by the same arithmetic. The horizontal rule is the
/// player's own shape (a small fixed gain per tick up to the walk speed, a larger fixed
/// loss when stopping or reversing); the vertical numbers are the game's NPC defaults.
/// </summary>
public static class BodyPhysics
{
    public const int Width = 20;
    public const int Height = 42;

    public const float WalkSpeed = 3.5f;
    public const float JumpVelocity = -8.5f;

    /// <summary>Speed gained per tick toward the target; the player's runAcceleration scaled to this walk speed.</summary>
    public const float Acceleration = 0.08f * WalkSpeed / 3f;

    /// <summary>Speed lost per tick when stopping or reversing; the player's runSlowdown.</summary>
    public const float Slowdown = 0.2f;

    /// <summary>The game's NPC gravity and fall-speed cap (NPC.UpdateGravity defaults).</summary>
    public const float Gravity = 0.3f;
    public const float MaxFallSpeed = 10f;

    /// <summary>
    /// One tick of horizontal physics: toward <paramref name="target"/> by the acceleration when
    /// the current speed is on the target's side, by the slowdown when it is against it or the
    /// target is zero, never overshooting.
    /// </summary>
    public static float StepVelocity(float v, float target)
    {
        if (target == 0f || MathF.Sign(v) == -MathF.Sign(target))
        {
            float slowed = v - MathF.Sign(v) * Slowdown;
            v = MathF.Sign(slowed) != MathF.Sign(v) ? 0f : slowed;
            if (target == 0f)
                return v;
        }
        float next = v + MathF.Sign(target) * Acceleration;
        // Clamp to the target only once the speed is on the target's side: a magnitude test
        // alone snapped -3.2 straight to +1.75 on a reversal, which is the instant turn the
        // slowdown above exists to prevent.
        return MathF.Sign(next) == MathF.Sign(target) && MathF.Abs(next) > MathF.Abs(target) ? target : next;
    }

    /// <summary>One tick of vertical physics in the air.</summary>
    public static float StepFall(float vy) => MathF.Min(vy + Gravity, MaxFallSpeed);

    /// <summary>
    /// Jump velocity scale for a rise of so many tiles, the heights the fighter AI uses (-6 for
    /// two tiles, -7 for three, -8 for four) and the full jump above that; one tile is a step.
    /// </summary>
    public static float JumpScaleForTiles(int tiles) => tiles switch
    {
        <= 2 => 6f / -JumpVelocity,
        3 => 7f / -JumpVelocity,
        4 => 8f / -JumpVelocity,
        _ => 1f,
    };

    /// <summary>Vertical offset of a full jump from standing after so many ticks, from the jump velocity and gravity (negative is up).</summary>
    public static float JumpOffsetAt(int ticks) => JumpVelocity * ticks + Gravity / 2f * ticks * ticks;
}
