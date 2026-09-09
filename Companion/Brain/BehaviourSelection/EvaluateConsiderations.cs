#nullable enable

using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.BehaviourSelection;

/// <summary>
/// Named curves from a sensed value to 0..1. Every action's score is a product of
/// these, so a zero from any one vetoes the action and no other term can buy it back.
/// Keeping the shapes named keeps the tuning readable: "inverse of distance over 400 px"
/// says what a bare expression hides.
/// </summary>
public static class Consideration
{
    /// <summary>1 at zero, falling to 0 at <paramref name="max"/>.</summary>
    public static float Inverse(float value, float max) => MathHelper.Clamp(1f - value / max, 0f, 1f);

    /// <summary>0 at zero, rising to 1 at <paramref name="max"/>.</summary>
    public static float Rising(float value, float max) => MathHelper.Clamp(value / max, 0f, 1f);

    /// <summary>1 inside the band, falling off to 0 over <paramref name="falloff"/> outside it.</summary>
    public static float Band(float value, float low, float high, float falloff)
    {
        if (value < low) return MathHelper.Clamp(1f - (low - value) / falloff, 0f, 1f);
        if (value > high) return MathHelper.Clamp(1f - (value - high) / falloff, 0f, 1f);
        return 1f;
    }

    public static float Step(bool condition, float whenTrue = 1f, float whenFalse = 0f) => condition ? whenTrue : whenFalse;

    /// <summary>Never quite zero: a floor that keeps an action eligible while others are absent.</summary>
    public static float AtLeast(float value, float floor) => MathHelper.Max(value, floor);
}
