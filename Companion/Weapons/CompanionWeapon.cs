#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Aiming;

namespace AICompanion.Companion.Weapons;

/// <summary>
/// What one use of a weapon put into the world: the projectile slot the game gave a shot, or the
/// bodies a swing struck. A swing has no projectile and a shot has struck nothing yet, so the two
/// numbers are never both meaningful and <see cref="Landed"/> is what a caller asks.
/// </summary>
public readonly record struct FireResult(int ProjectileSlot, int Struck)
{
    public static readonly FireResult Nothing = new(-1, 0);
    public bool IsShot => ProjectileSlot >= 0;
    public bool Landed => IsShot || Struck > 0;
}

/// <summary>
/// A weapon in the companion's hands, as the arsenal sees it: facts about itself and the two
/// geometric questions the arsenal asks of every weapon in the same words. It holds no opinion
/// about when it should be used, because the arsenal decides that by working out what each weapon
/// would actually land in the next few seconds, and a weapon that carried its own view of "suits a
/// crowd" would be overruling that arithmetic with a guess. That separation is what lets any item
/// arrive as numbers rather than as a rule.
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

    /// <summary>How the aimer flies one use of this weapon, read fresh each time because a learned motion changes under it.</summary>
    public abstract FlightModel Model { get; }

    /// <summary>Ticks between uses before the fire-rate nerf.</summary>
    public abstract int BaseUseTime { get; }

    /// <summary>Damage of one hit before the player's class bonuses and the companion's own factors.</summary>
    public abstract int BaseDamage { get; }

    /// <summary>The knockback one hit hands the game's strike, before the enemy's resistance and the game's falloff.</summary>
    public abstract float Knockback { get; }

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

    /// <summary>How many hostiles one use can hurt.</summary>
    public abstract int Pierce { get; }

    /// <summary>
    /// What one hit takes off, with every factor the actual use will apply. The scorer and the use
    /// read this same method on purpose: a weapon scored on one damage number and used with another
    /// is a weapon chosen for a reason that never happens.
    /// </summary>
    public abstract int DamagePerHit(in ActionContext ctx);

    /// <summary>Whether the target is close enough for a use to be worth tracing at all.</summary>
    public abstract bool InReach(Vector2 muzzle, NPC target);

    /// <summary>The hostiles one use from this muzzle along this launch would hurt, in the order it reaches them.</summary>
    public abstract int Hits(Vector2 muzzle, Vector2 launch, IReadOnlyList<NPC> hostiles, NPC[] into);

    /// <summary>Use the weapon: spawn the projectile, or strike the bodies in the swing.</summary>
    public abstract FireResult Fire(in ActionContext ctx, Vector2 muzzle, Vector2 launch);

    public int UseTime => (int)(BaseUseTime * FireRateFactor);
}
