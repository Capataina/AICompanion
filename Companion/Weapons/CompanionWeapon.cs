#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.ProjectileAiming;

namespace AICompanion.Companion.Weapons;

/// <summary>
/// One of the companion's own weapons. It states facts about itself and nothing else: how its
/// projectile flies, how hard it hits, how often it can fire, and how many bodies one shot can
/// pass through. It holds no opinion about when it should be used, because the arsenal decides
/// that by working out what each weapon would actually land in the next few seconds, and a
/// weapon that carried its own view of "suits a crowd" would be overruling that arithmetic with
/// a guess. That separation is what lets a new weapon arrive as numbers rather than as a rule.
///
/// Fire rate is halved and damage is scaled until the mastery tree upgrades them, and a few
/// degrees of aim noise are added for the same reason.
/// </summary>
public abstract class CompanionWeapon
{
    public abstract string Name { get; }

    /// <summary>The item drawn in the companion's hand.</summary>
    public abstract int ItemType { get; }

    /// <summary>The vanilla projectile this fires, which is where its pierce comes from.</summary>
    public abstract int ProjectileType { get; }

    public abstract WeaponProfile Profile { get; }

    /// <summary>Ticks between shots before the fire-rate nerf.</summary>
    public abstract int BaseUseTime { get; }

    /// <summary>Damage of one hit before the balance factor and the player's ranged bonuses.</summary>
    public abstract int BaseDamage { get; }

    /// <summary>Multiplier on use time; 2 is the launch nerf, the tree lowers it.</summary>
    public float FireRateFactor = 2f;

    /// <summary>
    /// Multiplier on damage, which is the one knob the roster is balanced with. It exists because
    /// the weapons read vanilla item numbers and those numbers were balanced for a player holding
    /// one weapon at a time, not for a companion choosing between two: the throwing knife out-damages
    /// the wooden bow outright and also pierces, so on any honest reckoning the bow would never be
    /// chosen and one of the two slots would be decoration.
    /// </summary>
    public float DamageFactor = 1f;

    /// <summary>Random angle added to every shot, radians; the tree lowers it.</summary>
    public float AimNoise = MathHelper.ToRadians(4f);

    /// <summary>Furthest a shot is worth attempting, px.</summary>
    public abstract float Reach { get; }

    /// <summary>
    /// How many hostiles one shot can hurt, taken from the vanilla projectile's own penetration
    /// rather than written down here, so a weapon that later fires a different projectile pierces
    /// whatever that projectile pierces. A projectile with unlimited penetration reports the width
    /// of the arsenal's pierce buffer, since nothing can hit more bodies than the arc is walked for.
    /// </summary>
    public virtual int Pierce
    {
        get
        {
            int p = ContentSamples.ProjectilesByType[ProjectileType].penetrate;
            return p < 0 ? Arsenal.MaxPierceCounted : System.Math.Max(1, p);
        }
    }

    /// <summary>
    /// What one hit takes off, with the player's ranged bonuses applied the way they are when the
    /// shot is actually fired. The scorer and the shot read this same method on purpose: a weapon
    /// scored on one damage number and fired with another is a weapon chosen for a reason that
    /// never happens.
    /// </summary>
    public int DamagePerHit(in ActionContext ctx)
        => (int)ctx.Player.GetTotalDamage(DamageClass.Ranged).ApplyTo(BaseDamage * DamageFactor);

    /// <summary>Spawn the projectile and return its Terraria slot for the causal shot record.</summary>
    public abstract int Fire(in ActionContext ctx, Vector2 muzzle, Vector2 launch);

    public int UseTime => (int)(BaseUseTime * FireRateFactor);
}
