#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;

namespace AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;

/// <summary>
/// One body a swing struck: how much its life fell, and the buffs it carried just before, so the outcome observer can read
/// what the strike added.
/// </summary>
public readonly record struct SwingStrike(NPC Npc, int Dealt, int[] BuffTypesBefore, int[] BuffTimesBefore);

/// <summary>
/// What one use of a weapon put into the world: the projectile slots the game gave a shot's volley, or the
/// bodies a swing struck. <see cref="ProjectileSlot"/> is the first slot the use spawned — the capacity sentinel
/// when the game had no slot — and <see cref="AllSlots"/> every spawn that landed. A swing has no projectile and
/// a shot has struck nothing yet, so slots and strikes are never both meaningful and <see cref="Landed"/> is what
/// a caller asks. A swing also carries its strikes, because no hit hook will report them later.
/// </summary>
public readonly record struct FireResult(int ProjectileSlot, int Struck, IReadOnlyList<SwingStrike>? Strikes = null, IReadOnlyList<int>? Slots = null)
{
    public static readonly FireResult Nothing = new(-1, 0);
    public bool IsShot => ProjectileSlot >= 0;
    public bool Landed => IsShot || Struck > 0;
    public IReadOnlyList<int> AllSlots => Slots ?? (IsShot ? new[] { ProjectileSlot } : Array.Empty<int>());
}

/// <summary>
/// A weapon in the companion's hands, as the arsenal sees it: facts about itself, read from the item,
/// and one use. It holds no opinion about when it should be used, because the arsenal decides that by
/// simulating what each weapon would actually land in the next few seconds, and a weapon that carried
/// its own view of "suits a crowd" would be overruling that arithmetic with a guess. That separation
/// is what lets any item arrive as numbers rather than as a rule.
///
/// Fire rate is halved until the mastery tree raises it, and a few degrees of aim noise are added
/// for the same reason; both are companion facts rather than item facts.
/// </summary>
public abstract class CompanionWeapon
{
    public abstract string Name { get; }

    /// <summary>The item recorded as held while this is used; the body decides what to draw for it.</summary>
    public abstract int ItemType { get; }

    /// <summary>The projectile this spawns, or zero for a weapon that swings.</summary>
    public abstract int ProjectileType { get; }

    public abstract bool IsSwing { get; }

    /// <summary>The pre-gate facts one use of this weapon is checked against before anything is simulated.</summary>
    public abstract FlightModel Model { get; }

    /// <summary>Ticks between uses before the fire-rate nerf.</summary>
    public abstract int BaseUseTime { get; }

    /// <summary>Damage of one hit before the player's class bonuses and the companion's own factors.</summary>
    public abstract int BaseDamage { get; }

    /// <summary>The knockback one hit hands the game's strike, before the enemy's resistance and the game's falloff.</summary>
    public abstract float Knockback { get; }

    /// <summary>The mana one use spends; zero for a weapon that costs none.</summary>
    public abstract int ManaCost { get; }

    /// <summary>
    /// Whether the game pushes this weapon's hits away from their owner, the player, rather than along the flight or the
    /// swing. <see cref="WeaponEffects.PushesAwayFromOwner"/> holds the game's list.
    /// </summary>
    public abstract bool PushesAwayFromOwner { get; }

    /// <summary>Multiplier on use time; 2 is the launch nerf, the tree lowers it.</summary>
    public float FireRateFactor = 2f;

    /// <summary>Random angle added to every use, radians; the tree lowers it.</summary>
    public float AimNoise = MathHelper.ToRadians(4f);

    /// <summary>Furthest a use is worth attempting, px, which is the model's own reach.</summary>
    public float Reach => Model.Reach;

    /// <summary>
    /// What one hit takes off, with every factor the actual use will apply. The scorer and the use
    /// read this same method on purpose: a weapon scored on one damage number and used with another
    /// is a weapon chosen for a reason that never happens.
    /// </summary>
    public abstract int DamagePerHit(in ActionContext ctx);

    /// <summary>
    /// The class-scaled base before the mana gradient: every damage bonus the player's kit applies,
    /// folded where the game folds it. The snapshot stamps one scalar per weapon rather than the
    /// whole bonus stack, because the stack's shape (per-class arrays, inheritance, modded hooks)
    /// cannot be rebuilt headless and the decision priced only the scalar.
    /// </summary>
    public abstract float ScaledBaseDamage(Player player);

    /// <summary>
    /// The stamped scalar, installed by the audit after the restored weapons rebuild: the restored
    /// player wears none of the live kit, so a recomputed bonus stack would price a naked fight.
    /// Null live and on pre-stamp captures, where the stack is computed as always.
    /// </summary>
    public float? AuditDamageOverride { get; set; }

    /// <summary>Whether the target is close enough for a use to be worth simulating at all.</summary>
    public abstract bool InReach(Vector2 muzzle, NPC target);

    /// <summary>
    /// Use the weapon: spawn the learned volley, or strike the bodies in the swing. <paramref name="aim"/> is the
    /// point the use is aimed at — the target's centre on the tick it leaves — which the volley is expanded around
    /// and the traces remember; a swing aims its sector by <paramref name="launch"/> as before.
    /// </summary>
    public abstract FireResult Fire(in ActionContext ctx, Vector2 muzzle, Vector2 launch, Vector2 aim);

    public int UseTime => (int)(BaseUseTime * FireRateFactor);
}
