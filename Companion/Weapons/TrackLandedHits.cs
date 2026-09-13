#nullable enable

using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.WorldObservation;

namespace AICompanion.Companion.Weapons;

/// <summary>
/// Which NPC a companion projectile actually struck, kept beside the target it was aimed at. Pursuit
/// target (where the feet go), aim target (what the hands chose) and hit target (what the game says was
/// struck) are three different facts: a piercing arrow aimed at one enemy can land on the one in front,
/// and a shot at a visible enemy is not progress against the hidden one a hunt is walking toward.
///
/// The arsenal registers every shot it fires against the slot the game gave the projectile. Every native
/// projectile spawn first forgets its slot, so a later projectile reusing that slot — the player's own
/// arrow, a hostile's shot — can never inherit a companion shot's identity; a companion shot is registered
/// after its own spawn has cleared the slot. A hit is attributed only to a registered slot. This is a
/// brain-side fact for recording and later consumers, deliberately not a God's Eye record, so gameplay
/// never has to read the diagnostic stream to learn what it hit. Singleplayer: one ledger per process.
/// </summary>
public static class TrackLandedHits
{
    public readonly record struct Shot(int AimSlot, int AimGeneration, ulong FiredTick);

    /// <summary>One landed hit: the NPC struck and its spawn generation, the target the shot was aimed at,
    /// the effective damage the game reported and when.</summary>
    public readonly record struct LandedHit(int HitSlot, int HitGeneration, int AimSlot, int AimGeneration,
        int Damage, ulong Tick, ulong FiredTick)
    {
        public bool StruckAimedTarget => HitSlot == AimSlot && HitGeneration == AimGeneration;
    }

    private static readonly Shot?[] shots = new Shot?[Main.maxProjectiles + 1];

    public static LandedHit? Last { get; private set; }

    /// <summary>Landed hits attributed to companion shots since the ledger was last cleared.</summary>
    public static int Count { get; private set; }

    public static void Register(int projectileSlot, NPC? aimed)
    {
        if ((uint)projectileSlot >= (uint)shots.Length) return;
        shots[projectileSlot] = new Shot(aimed?.whoAmI ?? -1, aimed == null ? 0 : HostileAttackSources.Generation(aimed), Main.GameUpdateCount);
    }

    public static void Forget(int projectileSlot)
    {
        if ((uint)projectileSlot < (uint)shots.Length) shots[projectileSlot] = null;
    }

    public static void ObserveHit(NPC npc, Projectile projectile, int damageDone)
    {
        if ((uint)projectile.whoAmI >= (uint)shots.Length || shots[projectile.whoAmI] is not { } shot) return;
        Last = new LandedHit(npc.whoAmI, HostileAttackSources.Generation(npc), shot.AimSlot, shot.AimGeneration,
            System.Math.Max(0, damageDone), Main.GameUpdateCount, shot.FiredTick);
        Count++;
    }

    public static void Clear()
    {
        System.Array.Clear(shots);
        Last = null;
        Count = 0;
    }
}

/// <summary>Clears a projectile slot's shot identity at every native spawn.</summary>
public sealed class ForgetReusedShotSlots : GlobalProjectile
{
    public override void OnSpawn(Projectile projectile, IEntitySource source) => TrackLandedHits.Forget(projectile.whoAmI);
}

/// <summary>Attributes a native projectile hit to the companion shot registered in that slot, if any.</summary>
public sealed class ObserveLandedCompanionHits : GlobalNPC
{
    public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
        => TrackLandedHits.ObserveHit(npc, projectile, damageDone);
}
