using System;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// Return pressure rises with separation and the time needed to undo it, and there is none anywhere inside the player's
/// region. Separation is measured on the gap beyond the region's edge, the one measure every pull and separation in the
/// brain reads, so a companion inside the region reads zero whatever the player is doing and whatever a route home round a
/// thin wall would cost: the owner ruled there is no pull inside the region, and a travel estimate that could raise one
/// there would be the pull by another name.
/// </summary>
public static class CalculateRegroupUrgency
{
    /// <param name="gapBeyondRegion">How far outside the region the companion is; zero inside.</param>
    /// <param name="rampDistance">How far beyond the edge the travel pressure takes to count in full, so a companion just
    /// outside with a long estimated route is not treated as lost.</param>
    public static float Evaluate(float gapBeyondRegion, float returnTicks, float movingAwaySpeed, int stalledTicks,
        float rampDistance, float fullDistance, float freeReturnTicks, float fullReturnTicks)
    {
        if (gapBeyondRegion <= 0f) return 0f;
        float futureGap = gapBeyondRegion + MathF.Max(0f, movingAwaySpeed) * freeReturnTicks;
        float separation = Math.Clamp(futureGap / MathF.Max(1f, fullDistance), 0f, 1f);
        float travel = Math.Clamp((returnTicks + stalledTicks - freeReturnTicks) / MathF.Max(1f, fullReturnTicks - freeReturnTicks), 0f, 1f);
        // Travel cost matters only once outside the region, and ramps in over a distance beyond its edge; an enemy at the
        // companion's feet must not make a nearby companion abandon protection because its route estimate grew.
        return MathF.Max(separation, travel * Math.Clamp(gapBeyondRegion / MathF.Max(1f, rampDistance), 0f, 1f));
    }
}
