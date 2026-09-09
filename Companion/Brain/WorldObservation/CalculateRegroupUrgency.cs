using System;

namespace AICompanion.Companion.Brain.WorldObservation;

/// <summary>Return pressure rises with separation and the time needed to undo it.</summary>
public static class CalculateRegroupUrgency
{
    public static float Evaluate(float distance, float returnTicks, float movingAwaySpeed, int stalledTicks,
        float comfortableDistance, float fullDistance, float freeReturnTicks, float fullReturnTicks)
    {
        float futureDistance = distance + MathF.Max(0f, movingAwaySpeed) * freeReturnTicks;
        float separation = Math.Clamp((futureDistance - comfortableDistance) / MathF.Max(1f, fullDistance - comfortableDistance), 0f, 1f);
        float travel = Math.Clamp((returnTicks + stalledTicks - freeReturnTicks) / MathF.Max(1f, fullReturnTicks - freeReturnTicks), 0f, 1f);
        // Travel cost matters only once outside the comfortable band; an enemy at our feet
        // must not make a nearby companion abandon protection because its route estimate grew.
        return MathF.Max(separation, travel * Math.Clamp((distance - comfortableDistance) / MathF.Max(1f, comfortableDistance), 0f, 1f));
    }
}
