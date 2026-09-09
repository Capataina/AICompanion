#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Brain.Actions;
using AICompanion.Brain.Aiming;

namespace AICompanion.Combat.Weapons;

/// <summary>
/// The piercing weapon: a thrown knife, twice as fast to throw as the bow is to draw, whose vanilla
/// projectile passes through two bodies. It hits softer than the bow per throw and wins whenever the
/// arc crosses more than one enemy, which is a thing the arsenal discovers from the shot rather than
/// a thing written down here. The knife's own flight (aiStyle 2) drops from the first tick, and its
/// short reach is a fact about the arc rather than a rule about when to use it.
/// </summary>
public sealed class ThrowingKnifeWeapon : CompanionWeapon
{
    private static Item Knife => ContentSamples.ItemsByType[ItemID.ThrowingKnife];

    public ThrowingKnifeWeapon()
    {
        // Halved so that one knife a target is worth less per second than one arrow and two targets
        // on the same throw are worth more. With the bow doubled, the crossing point sits between
        // one enemy on the line and two, which is exactly where the pierce is supposed to decide it.
        DamageFactor = 0.5f;
    }

    public override string Name => "knife";
    public override int ItemType => ItemID.ThrowingKnife;
    public override int ProjectileType => ProjectileID.ThrowingKnife;
    public override WeaponProfile Profile => new(Speed: Knife.shootSpeed, StraightTicks: 0, Gravity: 0.1f, MaxFallSpeed: 16f, MaxFlightTicks: 90, HitboxSize: 10, Reach: 380f);
    public override int BaseUseTime => Knife.useTime;
    public override int BaseDamage => Knife.damage;
    public override float Reach => 380f;

    public override Vector2 Fire(in ActionContext ctx, Vector2 muzzle, Vector2 launch)
    {
        Projectile.NewProjectile(ctx.Npc.GetSource_FromAI(), muzzle, launch, ProjectileType, DamagePerHit(ctx), Knife.knockBack, Main.myPlayer);
        return launch;
    }
}
