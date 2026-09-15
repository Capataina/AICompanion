#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.Infrastructure.Aiming;
using AICompanion.Companion.Brain.Infrastructure.Observation;

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
    /// <summary>A registered shot: the target it was aimed at, when, and the item that fired it, which is what the weapon-effects table is keyed by.</summary>
    public readonly record struct Shot(int AimSlot, int AimGeneration, ulong FiredTick, int ItemType);

    /// <summary>One landed hit: the NPC struck and its spawn generation, the target the shot was aimed at,
    /// the effective damage the game reported and when.</summary>
    public readonly record struct LandedHit(int HitSlot, int HitGeneration, int AimSlot, int AimGeneration,
        int Damage, ulong Tick, ulong FiredTick)
    {
        public bool StruckAimedTarget => HitSlot == AimSlot && HitGeneration == AimGeneration;
    }

    private static readonly Shot?[] shots = new Shot?[Main.maxProjectiles + 1];

    // Whether each slot's projectile is the companion's: set when the arsenal registers a shot and when a projectile spawns
    // from a parent projectile that is, cleared at every spawn first. Kept apart from the outcome windows, which exist to
    // teach the learner and open only for a shot that had a forecast and only until their bound, so attribution read off
    // them handed a forecast-less shot's children, and every child spawned late in a long flight, to the player.
    private static readonly bool[] companions = new bool[Main.maxProjectiles + 1];

    public static LandedHit? Last { get; private set; }

    /// <summary>Landed hits attributed to companion shots since the ledger was last cleared.</summary>
    public static int Count { get; private set; }

    public static void Register(int projectileSlot, NPC? aimed, int itemType)
    {
        if ((uint)projectileSlot >= (uint)shots.Length) return;
        shots[projectileSlot] = new Shot(aimed?.whoAmI ?? -1, aimed == null ? 0 : HostileAttackSources.Generation(aimed), Main.GameUpdateCount, itemType);
        companions[projectileSlot] = true;
    }

    /// <summary>
    /// Whether the projectile in this slot is the companion's, for as long as it lives: a shot the arsenal registered, or any
    /// projectile descended from one through <c>EntitySource_Parent</c>, whether or not the shot had a forecast or an open
    /// outcome window. Every companion shot is owned by the local player, so ownership alone cannot say this. This is the
    /// contract the experience system's striker test reads, and it is decided here and nowhere else.
    /// </summary>
    public static bool IsCompanionShot(int projectileSlot)
        => (uint)projectileSlot < (uint)companions.Length && companions[projectileSlot];

    /// <summary>
    /// A projectile has just spawned into this slot: the slot's previous identity is forgotten, and the new projectile is the
    /// companion's if its source names a parent projectile that is. The companion's own shot names the companion NPC as its
    /// parent, so it is not attributed here; the arsenal registers it after this has cleared the slot.
    /// </summary>
    public static void AttributeSpawn(int projectileSlot, IEntitySource? source)
    {
        Forget(projectileSlot);
        if ((uint)projectileSlot >= (uint)companions.Length) return;
        if (source is EntitySource_Parent { Entity: Projectile parent } && parent.whoAmI != projectileSlot && IsCompanionShot(parent.whoAmI))
            companions[projectileSlot] = true;
    }

    // The velocity each NPC had just before a registered companion projectile struck it, and which projectile slot (plus
    // one, so zero is none) took it. Per NPC rather than per projectile because a piercing shot strikes several bodies in
    // one update, and each strike's modify hook runs directly before its own strike.
    private static readonly Vector2[] preStrikeVelocity = new Vector2[Main.maxNPCs + 1];
    private static readonly int[] preStrikeBy = new int[Main.maxNPCs + 1];

    /// <summary>
    /// A registered companion projectile is about to strike this NPC: keep its velocity. Called from the NPC modify hook,
    /// which <c>Projectile.Damage</c> runs through <c>CombinedHooks.ModifyHitNPCWithProj</c> before <c>StrikeNPC</c> writes
    /// the knockback, so the velocity here is the one the push is added to.
    /// </summary>
    public static void BeforeStrike(NPC npc, Projectile projectile)
    {
        if ((uint)projectile.whoAmI >= (uint)shots.Length || shots[projectile.whoAmI] == null) return;
        if ((uint)npc.whoAmI >= (uint)preStrikeBy.Length) return;
        preStrikeVelocity[npc.whoAmI] = npc.velocity;
        preStrikeBy[npc.whoAmI] = projectile.whoAmI + 1;
    }

    /// <summary>
    /// The strike landed: teach the weapon-effects table what it did, against the velocity kept just before it. The prior's
    /// direction is the projectile's own <c>direction</c>, which is what <c>Projectile.Damage</c> hands the modifiers, or
    /// away from the owner for the types the game overrides.
    /// </summary>
    public static void AfterStrike(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
    {
        if ((uint)npc.whoAmI >= (uint)preStrikeBy.Length || preStrikeBy[npc.whoAmI] != projectile.whoAmI + 1) return;
        preStrikeBy[npc.whoAmI] = 0;
        if (shots[projectile.whoAmI] is not { } shot) return;
        bool awayFromOwner = WeaponEffects.PushesAwayFromOwner(projectile.type);
        int direction = WeaponEffects.PriorDirection(awayFromOwner, projectile.direction, npc.Center.X, Main.player[projectile.owner].Center.X);
        WeaponEffects.ObserveHit(shot.ItemType, npc, preStrikeVelocity[npc.whoAmI], npc.velocity, projectile.knockBack, direction,
            projectile.damage, damageDone, hit.Crit || hit.InstantKill);
    }

    public static void Forget(int projectileSlot)
    {
        if ((uint)projectileSlot >= (uint)shots.Length) return;
        shots[projectileSlot] = null;
        companions[projectileSlot] = false;
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
        System.Array.Clear(companions);
        System.Array.Clear(preStrikeBy);
        Last = null;
        Count = 0;
    }
}

/// <summary>
/// Clears a projectile slot's shot identity and its arc watch at every native spawn, and feeds the arc
/// learner every post-AI velocity of a registered companion projectile until it dies. The learner
/// (<c>../Brain/Infrastructure/Aiming/LearnProjectileArcs.cs</c>) owns what is done with the samples; this
/// hook only delivers them, under the same slot-reuse discipline as the shot ledger: a spawn forgets first,
/// the arsenal registers after its own spawn has cleared the slot, and a death retires the watch.
/// </summary>
public sealed class ForgetReusedShotSlots : GlobalProjectile
{
    public override void OnSpawn(Projectile projectile, IEntitySource source)
    {
        TrackLandedHits.AttributeSpawn(projectile.whoAmI, source);
        ProjectileArcs.Forget(projectile.whoAmI);
        // Forgets the slot's outcome window first and then joins a child to its parent projectile's window, so a
        // splitting or star-calling shot is credited with what its children land.
        ShotOutcomes.AttributeSpawn(projectile.whoAmI, source);
    }

    public override void PostAI(Projectile projectile) => ProjectileArcs.Observe(projectile);

    public override void OnKill(Projectile projectile, int timeLeft)
    {
        ProjectileArcs.Retire(projectile.whoAmI);
        ShotOutcomes.Retire(projectile.whoAmI);
    }
}

/// <summary>
/// Attributes a native projectile hit to the companion shot registered in that slot, if any, feeds the weapon-effects
/// table the velocity the hit was added to and the velocity it left, and feeds the outcome window the damage and the buffs
/// the hit added — for the shot's own projectile and for every descendant attributed to it.
/// </summary>
public sealed class ObserveLandedCompanionHits : GlobalNPC
{
    public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
    {
        TrackLandedHits.BeforeStrike(npc, projectile);
        ShotOutcomes.BeforeStrike(npc, projectile.whoAmI);
    }

    public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
    {
        TrackLandedHits.ObserveHit(npc, projectile, damageDone);
        TrackLandedHits.AfterStrike(npc, projectile, hit, damageDone);
        ShotOutcomes.Landed(npc, projectile.whoAmI, damageDone);
    }
}
