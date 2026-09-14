#nullable enable

using System;
using Terraria;
using Terraria.ID;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// What is happening to the companion's own body: which liquid it touches and whether that
/// liquid hurts it, how long it has been in contact, whether it burns, how much life it has and
/// how much it lost just now. From those, <see cref="SelfDanger"/>, 0..1, supplies personal
/// exposure to shared safety: a hurting liquid is total danger from the first tick of contact,
/// because the body is meant to leave it at once rather than ration a reserve; fire counts while
/// it lasts; a burst of recent damage counts by its share of max life. The threat sense measures
/// danger to the player; this measures danger to the companion, and the two are kept apart so
/// "the player is safe" never hides "I am in the water".
/// </summary>
public sealed class CompanionSense
{
    private const int DamageWindowTicks = 60;

    /// <summary>The body touches water or lava it is not immune to.</summary>
    public bool InHurtingLiquid { get; private set; }
    /// <summary>The older name for the same fact, kept because the escape and the record read it: a
    /// wet body that is being hurt.</summary>
    public bool HeadUnderwater => InHurtingLiquid;
    public bool InWater { get; private set; }
    public bool InLava { get; private set; }
    /// <summary>How many consecutive ticks the body has touched a liquid that hurts it.</summary>
    public int LiquidContactTicks { get; private set; }
    public bool OnFire { get; private set; }
    public float LifeFraction { get; private set; } = 1f;

    /// <summary>Life lost inside the last second, as a share of max life.</summary>
    public float RecentDamageFraction { get; private set; }

    /// <summary>0..1: how urgently the companion's own body needs saving.</summary>
    public float SelfDanger { get; private set; }

    private int lastLife = -1;
    private int lastLifeMax = 1;
    private readonly int[] damageRing = new int[DamageWindowTicks];
    private int ringAt;

    public void Update(NPC npc, global::AICompanion.Companion.CompanionMotor motor)
    {
        InWater = motor.LiquidKind == 0;
        InLava = motor.LiquidKind == 1;
        InHurtingLiquid = motor.InHurtingLiquid;
        LiquidContactTicks = motor.LiquidContactTicks;
        OnFire = npc.HasBuff(BuffID.OnFire) || npc.HasBuff(BuffID.OnFire3) || npc.HasBuff(BuffID.Burning);
        LifeFraction = npc.lifeMax > 0 ? Math.Clamp(npc.life / (float)npc.lifeMax, 0f, 1f) : 0f;

        // Damage over the last second: a ring of per-tick losses. A max-life change (the
        // companion mirrors the player's) is not damage.
        int lost = lastLife < 0 || npc.lifeMax != lastLifeMax ? 0 : Math.Max(0, lastLife - npc.life);
        damageRing[ringAt] = lost;
        ringAt = (ringAt + 1) % DamageWindowTicks;
        int sum = 0;
        foreach (int d in damageRing) sum += d;
        RecentDamageFraction = npc.lifeMax > 0 ? sum / (float)npc.lifeMax : 0f;
        lastLife = npc.life;
        lastLifeMax = npc.lifeMax;

        float liquid = InHurtingLiquid ? 1f : 0f;
        float burning = OnFire ? 0.6f : 0f;
        float bleeding = Math.Clamp(RecentDamageFraction * 2f, 0f, 1f) * (1f - LifeFraction);
        SelfDanger = Math.Max(liquid, Math.Max(burning, bleeding));
    }
}
