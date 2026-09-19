using System;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>The existing companionship curve, shared by live following and frozen course
/// forecasts. Geometry and the player's recovery preference are inputs, never global reads.</summary>
public static class MeasureCompanionshipGap
{
    public static float Beyond(float deltaX, float deltaY, float halfWidth, float halfHeight)
        => MathF.Max(0f, MathF.Max(MathF.Abs(deltaX) - halfWidth, MathF.Abs(deltaY) - halfHeight));

    public static float Pull(float gap, float halfWidth, float halfHeight, float recoveryRadius)
    {
        float inner = MathF.Max(halfWidth, halfHeight);
        float span = MathF.Max(1f, recoveryRadius - inner);
        return Math.Clamp(gap / span, 0f, 1f);
    }
}
