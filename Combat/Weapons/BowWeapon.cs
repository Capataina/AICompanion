#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Brain.Decision.Actions;
using AICompanion.Combat.Ballistics;

namespace AICompanion.Combat.Weapons;

/// <summary>
/// The long-range single-target weapon: a wooden bow firing a vanilla friendly arrow
/// the player owns, so kills, drops and on-hit accessories are the player's. Suits
/// distant and single targets and bosses; loses to the knife up close on a crowd.
/// </summary>
public sealed class BowWeapon : CompanionWeapon
{
    private static Item Bow => ContentSamples.ItemsByType[ItemID.WoodenBow];
    private static Item Arrow => ContentSamples.ItemsByType[ItemID.WoodenArrow];

    public override string Name => "bow";
    public override int ItemType => ItemID.WoodenBow;
    public override WeaponProfile Profile => WeaponProfile.Arrow.WithSpeed(Bow.shootSpeed + Arrow.shootSpeed);
    public override int BaseUseTime => Bow.useTime;
    public override float Reach => 1100f;

    public override float Suitability(in ActionContext ctx, NPC target, float distance, int crowdAround)
    {
        float range = MathHelper.Clamp(distance / 500f, 0.35f, 1f);
        float single = crowdAround <= 1 ? 1f : 0.6f;
        float boss = target.boss ? 1f : 0.85f;
        return range * single * boss;
    }

    public override Vector2 Fire(in ActionContext ctx, Vector2 muzzle, Vector2 launch)
    {
        int damage = (int)ctx.Player.GetTotalDamage(DamageClass.Ranged).ApplyTo(Bow.damage + Arrow.damage);
        Projectile.NewProjectile(ctx.Npc.GetSource_FromAI(), muzzle, launch, ProjectileID.WoodenArrowFriendly, damage, Bow.knockBack + Arrow.knockBack, Main.myPlayer);
        return launch;
    }
}
