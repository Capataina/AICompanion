using System;

namespace AICompanion.Companion.Brain.WorldInteractions;

/// <summary>Time until this hitter's next tile completion, assuming unchanged permission,
/// damage and cadence. It excludes approach, further vein tiles and collection.</summary>
public readonly record struct RemainingToolWork(int DamageRemaining, int DamagePerHit, int Hits, float Ticks, string Basis)
{
    public static RemainingToolWork? Estimate(int currentDamage, int damagePerHit, int cooldown, int useTime, string basis)
    {
        if (damagePerHit <= 0 || useTime <= 0) return null;
        // Terraria's HitTile/PickTile and the closed axe operation remove at 100 damage.
        // Do not combine another hitter's buffer with this actor's native progress.
        // The caller has observed a tile still present. Even a saturated damage buffer
        // cannot certify its removal; another admitted operation is still necessary.
        int remaining = Math.Max(1, 100 - Math.Clamp(currentDamage, 0, 100));
        int hits = (int)Math.Ceiling(remaining / (double)damagePerHit);
        float ticks = Math.Max(0, cooldown) + (hits - 1) * (float)useTime + 1;
        return new(remaining, damagePerHit, hits, ticks, basis);
    }
}
