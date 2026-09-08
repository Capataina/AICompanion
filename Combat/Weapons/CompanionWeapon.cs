#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Brain.Decision.Actions;
using AICompanion.Combat.Ballistics;

namespace AICompanion.Combat.Weapons;

/// <summary>
/// One of the companion's own weapons. Knows how its projectile flies (for the aimer
/// and the positioner), how often it can fire, and how well it suits a situation, so
/// the arsenal picks the weapon and the player never labels slots close or long.
/// Fire rate is halved and a few degrees of aim noise are added until the mastery
/// tree upgrades them.
/// </summary>
public abstract class CompanionWeapon
{
    public abstract string Name { get; }

    /// <summary>The item drawn in the companion's hand.</summary>
    public abstract int ItemType { get; }

    public abstract WeaponProfile Profile { get; }

    /// <summary>Ticks between shots before the fire-rate nerf.</summary>
    public abstract int BaseUseTime { get; }

    /// <summary>Multiplier on use time; 2 is the launch nerf, the tree lowers it.</summary>
    public float FireRateFactor = 2f;

    /// <summary>Random angle added to every shot, radians; the tree lowers it.</summary>
    public float AimNoise = MathHelper.ToRadians(4f);

    /// <summary>Furthest a shot is worth attempting, px.</summary>
    public abstract float Reach { get; }

    /// <summary>0..1: how well this weapon suits shooting <paramref name="target"/> from here, given how many hostiles crowd it.</summary>
    public abstract float Suitability(in ActionContext ctx, NPC target, float distance, int crowdAround);

    /// <summary>Spawn the projectile. Returns the launch velocity for the arm pose.</summary>
    public abstract Vector2 Fire(in ActionContext ctx, Vector2 muzzle, Vector2 launch);

    public int UseTime => (int)(BaseUseTime * FireRateFactor);
}
