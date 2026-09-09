#nullable enable

using System;
using Terraria;
using Terraria.ID;
using AICompanion.Companion;

namespace AICompanion.Brain.WorldObservation;

/// <summary>
/// What is happening to the companion's own body: how much breath it has and whether its
/// head is under water, whether it stands in lava or burns, how much life it has and how
/// much it lost just now. From those, <see cref="SelfDanger"/>, 0..1, the one number the
/// survive action scores on and the grid reads when it prices lava: drowning counts from
/// half breath and is total near none, lava and fire count while they last, and a burst
/// of recent damage counts by its share of max life. The threat sense measures danger
/// to the player; this measures danger to the companion, and the two are kept apart so
/// "the player is safe" never hides "I am drowning".
/// </summary>
public sealed class CompanionSense
{
    private const int DamageWindowTicks = 60;

    public float BreathFraction { get; private set; } = 1f;
    public int BreathTicksLeft { get; private set; }
    public bool HeadUnderwater { get; private set; }
    public bool InLava { get; private set; }
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

    public void Update(NPC npc, CompanionBreath breath)
    {
        BreathFraction = breath.Fraction;
        BreathTicksLeft = breath.TicksLeft;
        HeadUnderwater = breath.HeadUnderwater;
        InLava = npc.lavaWet;
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

        float drowning = HeadUnderwater ? Math.Clamp((0.5f - BreathFraction) / 0.5f, 0f, 1f) : 0f;
        if (HeadUnderwater && BreathFraction <= 0.15f)
            drowning = 1f;
        float burning = InLava ? 1f : OnFire ? 0.6f : 0f;
        float bleeding = Math.Clamp(RecentDamageFraction * 2f, 0f, 1f) * (1f - LifeFraction);
        SelfDanger = Math.Max(drowning, Math.Max(burning, bleeding));
    }
}
