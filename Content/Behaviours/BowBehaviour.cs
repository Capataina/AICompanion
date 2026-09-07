#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AICompanion.Content.Behaviours;

/// <summary>
/// The companion's bow. Picks a hostile that is on the player's screen, asks the
/// <see cref="ArrowAimer"/> for an arc that lands, and fires a vanilla friendly wooden
/// arrow owned by the player, so kills, drops and on-hit accessory effects are the
/// player's. Damage is the bow-plus-arrow base run through the player's ranged damage
/// stat at spawn, so upgrading the player upgrades the companion's arrows.
/// </summary>
public class BowBehaviour
{
    private int cooldown;

    public NPC? Target { get; private set; }

    /// <summary>The bow and arrow the companion uses, read from the game's item table rather than hard-coded.</summary>
    private static Item Bow => ContentSamples.ItemsByType[ItemID.WoodenBow];
    private static Item Arrow => ContentSamples.ItemsByType[ItemID.WoodenArrow];

    public static int BowItemType => ItemID.WoodenBow;

    /// <summary>The nearest live hostile inside the camera rectangle that can be chased, or null.</summary>
    public static NPC? FindTarget(Vector2 from)
    {
        Rectangle screen = new((int)Main.screenPosition.X, (int)Main.screenPosition.Y, Main.screenWidth, Main.screenHeight);
        NPC? best = null;
        float bestDist = float.MaxValue;
        foreach (NPC npc in Main.ActiveNPCs)
        {
            if (npc.friendly || npc.life <= 0 || npc.CountsAsACritter || !npc.CanBeChasedBy() || !npc.Hitbox.Intersects(screen))
                continue;
            float d = Vector2.DistanceSquared(from, npc.Center);
            if (d < bestDist)
            {
                bestDist = d;
                best = npc;
            }
        }
        return best;
    }

    public bool WantsToShoot(NPC npc)
    {
        Target = FindTarget(npc.Center);
        return Target != null;
    }

    /// <summary>
    /// One tick of shooting. Returns the launch velocity on the tick an arrow leaves,
    /// so the body can animate the bow; null on every other tick.
    /// </summary>
    public Vector2? Update(NPC npc, Player player)
    {
        if (Target is not NPC target)
            return null;

        npc.direction = npc.spriteDirection = target.Center.X >= npc.Center.X ? 1 : -1;
        if (cooldown-- > 0)
            return null;

        Vector2 muzzle = npc.Center + new Vector2(npc.direction * 10f, -4f);
        WeaponProfile profile = WeaponProfile.Arrow.WithSpeed(Bow.shootSpeed + Arrow.shootSpeed);
        Vector2? launch = ArrowAimer.Solve(muzzle, target, profile);
        if (launch is not Vector2 velocity)
        {
            // No arc reaches this target from here; look again next tick, maybe from a new spot.
            Target = null;
            return null;
        }

        cooldown = Bow.useTime;
        int damage = (int)player.GetTotalDamage(DamageClass.Ranged).ApplyTo(Bow.damage + Arrow.damage);
        float knockback = Bow.knockBack + Arrow.knockBack;
        Projectile.NewProjectile(npc.GetSource_FromAI(), muzzle, velocity, ProjectileID.WoodenArrowFriendly, damage, knockback, Main.myPlayer);
        return velocity;
    }
}
