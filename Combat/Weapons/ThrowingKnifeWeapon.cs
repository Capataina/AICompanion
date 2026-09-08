#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Brain.Decision.Actions;
using AICompanion.Combat.Ballistics;

namespace AICompanion.Combat.Weapons;

/// <summary>
/// The short-range weapon for v1: a thrown knife, faster to throw and harder-hitting
/// than an arrow but with a short useful reach. Suits close targets and crowds.
/// The knife's own flight (aiStyle 2) drops from the first tick.
/// </summary>
public sealed class ThrowingKnifeWeapon : CompanionWeapon
{
    private static Item Knife => ContentSamples.ItemsByType[ItemID.ThrowingKnife];

    public override string Name => "knife";
    public override int ItemType => ItemID.ThrowingKnife;
    public override WeaponProfile Profile => new(Speed: Knife.shootSpeed, StraightTicks: 0, Gravity: 0.1f, MaxFallSpeed: 16f, MaxFlightTicks: 90, HitboxSize: 10);
    public override int BaseUseTime => Knife.useTime;
    public override float Reach => 380f;

    public override float Suitability(in ActionContext ctx, NPC target, float distance, int crowdAround)
    {
        float close = MathHelper.Clamp(1f - distance / 420f, 0f, 1f);
        float crowd = crowdAround >= 2 ? 1f : 0.7f;
        float boss = target.boss ? 0.6f : 1f;
        return close * crowd * boss;
    }

    public override Vector2 Fire(in ActionContext ctx, Vector2 muzzle, Vector2 launch)
    {
        int damage = (int)ctx.Player.GetTotalDamage(DamageClass.Ranged).ApplyTo(Knife.damage);
        Projectile.NewProjectile(ctx.Npc.GetSource_FromAI(), muzzle, launch, ProjectileID.ThrowingKnife, damage, Knife.knockBack, Main.myPlayer);
        return launch;
    }
}
