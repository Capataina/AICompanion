#nullable enable

using System;
using Terraria;
using Terraria.ID;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// What is happening to the companion's own body: whether it burns, how much life it has and how much it lost just now.
/// From those, <see cref="SelfDanger"/>, 0..1, is the companion's personal exposure: fire counts while it lasts, and a burst
/// of recent damage counts by its share of max life. The threat sense measures danger to the player; this measures danger
/// to the companion, and the two are kept apart so "the player is safe" never hides "I am being hurt".
///
/// <para>Liquid is not here. Until 15 September 2026 a water or lava contact was total danger from its first tick, because
/// the body was meant to leave it at once; every liquid has been air to the orb since, so touching one is no exposure at
/// all, and which liquid the body is in is read by the motor for the record and nothing else.</para>
/// </summary>
public sealed class CompanionSense
{
    private const int DamageWindowTicks = 60;

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

    public void Update(NPC npc)
    {
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

        float burning = OnFire ? 0.6f : 0f;
        float bleeding = Math.Clamp(RecentDamageFraction * 2f, 0f, 1f) * (1f - LifeFraction);
        SelfDanger = Math.Max(burning, bleeding);
    }
}
