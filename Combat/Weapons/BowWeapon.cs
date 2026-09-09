#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Brain.Actions;
using AICompanion.Brain.Aiming;

namespace AICompanion.Combat.Weapons;

/// <summary>
/// The single-target weapon: a wooden bow firing a vanilla friendly arrow the player owns, so
/// kills, drops and on-hit accessories are the player's. Its arrow passes through one body, and
/// it is the harder hitter per second against that one body — which is the whole of what makes it
/// worth carrying beside the knife. Nothing here says "use me at range"; the arsenal works that
/// out from the arc, because a bow is only a long-range weapon where a long shot actually solves.
/// </summary>
public sealed class BowWeapon : CompanionWeapon
{
    private static Item Bow => ContentSamples.ItemsByType[ItemID.WoodenBow];
    private static Item Arrow => ContentSamples.ItemsByType[ItemID.WoodenArrow];

    public BowWeapon()
    {
        // Vanilla gives the bow 4 + 5 damage every 30 ticks and the knife 12 every 15, so before
        // any scaling the knife deals 2.67 times the bow's damage a second and also pierces twice.
        // Doubling the bow puts it ahead on one target and leaves the knife ahead on two, which is
        // the only relationship under which both slots get used.
        DamageFactor = 2f;
    }

    public override string Name => "bow";
    public override int ItemType => ItemID.WoodenBow;
    public override int ProjectileType => ProjectileID.WoodenArrowFriendly;
    public override WeaponProfile Profile => WeaponProfile.Arrow.WithSpeed(Bow.shootSpeed + Arrow.shootSpeed);
    public override int BaseUseTime => Bow.useTime;
    public override int BaseDamage => Bow.damage + Arrow.damage;
    public override float Reach => 1100f;

    public override Vector2 Fire(in ActionContext ctx, Vector2 muzzle, Vector2 launch)
    {
        Projectile.NewProjectile(ctx.Npc.GetSource_FromAI(), muzzle, launch, ProjectileType, DamagePerHit(ctx), Bow.knockBack + Arrow.knockBack, Main.myPlayer);
        return launch;
    }
}
