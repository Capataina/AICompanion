#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Brain.Actions;
using AICompanion.Brain.Aiming;
using AICompanion.Brain.DecisionMatrix.Senses;

namespace AICompanion.Combat.Weapons;

/// <summary>
/// The two equipped weapons and the choice between them. For a target it asks each
/// weapon how well it suits the situation, takes the best, solves the shot through
/// the aimer, adds the aim noise, and fires when the cooldown allows. A target no
/// weapon can reach from here is reported so the positioner can move the companion.
/// </summary>
public sealed class Arsenal
{
    public CompanionWeapon Primary = new BowWeapon();
    public CompanionWeapon Secondary = new ThrowingKnifeWeapon();

    public CompanionWeapon? LastChosen { get; private set; }
    public bool LastShotSolved { get; private set; }
    private int cooldown;

    public void Tick()
    {
        if (cooldown > 0)
            cooldown--;
    }

    /// <summary>The weapon that suits the target best, and its flight profile for the positioner.</summary>
    public CompanionWeapon Choose(in ActionContext ctx, NPC target)
    {
        float distance = Vector2.Distance(ctx.Npc.Center, target.Center);
        int crowd = 0;
        foreach (ThreatRecord t in ctx.Senses.Threats.Threats)
            if (t.Reachable && Vector2.DistanceSquared(t.Npc.Center, target.Center) < 160f * 160f)
                crowd++;
        float a = Primary.Suitability(ctx, target, distance, crowd);
        float b = Secondary.Suitability(ctx, target, distance, crowd);
        LastChosen = a >= b ? Primary : Secondary;
        return LastChosen;
    }

    public WeaponProfile? ProfileFor(in ActionContext ctx, NPC? target)
        => target == null ? null : Choose(ctx, target).Profile;

    private readonly int[] engageCheckedAt = new int[Main.maxNPCs];
    private readonly bool[] engageResult = new bool[Main.maxNPCs];
    private const int EngageCacheTicks = 20;

    /// <summary>
    /// Whether any equipped weapon has a solvable shot at the target from where the companion
    /// stands now. Two aimer solves are dear, so the answer is cached per NPC for a third of a second.
    /// </summary>
    public bool CanEngage(in ActionContext ctx, NPC target)
    {
        int now = ctx.Senses.Tick;
        int slot = target.whoAmI;
        if (now - engageCheckedAt[slot] < EngageCacheTicks && engageCheckedAt[slot] != 0)
            return engageResult[slot];
        Vector2 muzzle = Muzzle(ctx.Npc);
        bool can = TrajectoryAimer.Solve(muzzle, target, Primary.Profile) != null
            || TrajectoryAimer.Solve(muzzle, target, Secondary.Profile) != null;
        engageCheckedAt[slot] = now;
        engageResult[slot] = can;
        return can;
    }

    /// <summary>Face the target and fire if a shot exists and the cooldown allows. Returns true on a shot.</summary>
    public bool TryFire(in ActionContext ctx, NPC? target)
    {
        if (target == null || !target.active || target.life <= 0)
        {
            LastShotSolved = false;
            return false;
        }
        CompanionWeapon weapon = Choose(ctx, target);
        ctx.Companion.HoldItem(weapon.ItemType);
        ctx.Companion.Motor.Face(target.Center.X);
        if (cooldown > 0)
            return false;

        Vector2 muzzle = Muzzle(ctx.Npc);
        Vector2? solved = TrajectoryAimer.Solve(muzzle, target, weapon.Profile);
        LastShotSolved = solved != null;
        if (solved is not Vector2 launch)
        {
            cooldown = 15; // do not re-solve every tick against a target with no arc
            return false;
        }

        float noise = (Main.rand.NextFloat() * 2f - 1f) * weapon.AimNoise;
        launch = launch.RotatedBy(noise);
        weapon.Fire(ctx, muzzle, launch);
        cooldown = weapon.UseTime;
        ctx.Companion.StartAnimation(weapon.ItemType, Math.Max(10, weapon.BaseUseTime));
        ctx.Companion.SetAimRotation(launch);
        return true;
    }

    private static Vector2 Muzzle(NPC npc) => npc.Center + new Vector2(npc.direction * 10f, -4f);
}
