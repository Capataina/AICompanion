#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Brain.Actions;
using AICompanion.Brain.Aiming;
using AICompanion.Brain.DecisionMatrix.Senses;

namespace AICompanion.Combat.Weapons;

/// <summary>
/// The two equipped weapons and the choice between them, made by one number: how much damage each
/// would actually land in the next few seconds, fired from where the companion is standing, at the
/// hostiles that are really there. The weapon with the larger number wins, the shot is solved
/// through the aimer, aim noise is added, and it fires when the cooldown allows.
///
/// The number is deliberately not the game's own DPS readout, which is damage *output* per second
/// and would make a heavy single-target weapon look enormous against a slime it overkills by a
/// factor of fifteen. Three things separate the two. Damage is counted over a window rather than
/// per shot, so a slower weapon that clears a line can outscore a faster one that kills singly.
/// Every hostile contributes only the life it still has, so surplus damage is not counted twice.
/// And the bodies a shot hits are the ones its simulated arc actually crosses, capped by what its
/// projectile can pierce, so a wall, a gap or a bad angle removes them without anyone writing a
/// rule about walls, gaps or angles.
///
/// What that buys is the point of the design: a weapon is a set of numbers, so a roster of ninety
/// weapons needs no per-weapon behaviour. Whatever pierces, hits hard, fires fast, sprays, crits or
/// reaches further is compared on the same scale, and a shot with no solution scores zero, which is
/// why the companion can no longer stand holding a bow it cannot fire while something kills it.
/// </summary>
public sealed class Arsenal
{
    /// <summary>The window the damage is counted over: three seconds, in ticks.</summary>
    public const int HorizonTicks = 180;

    /// <summary>How many bodies one arc is walked for, and the cap for unlimited-pierce projectiles.</summary>
    public const int MaxPierceCounted = 8;

    /// <summary>How long a weapon choice is kept before the arcs are simulated again.</summary>
    private const int ChoiceCacheTicks = 12;

    public CompanionWeapon Primary = new BowWeapon();
    public CompanionWeapon Secondary = new ThrowingKnifeWeapon();

    public CompanionWeapon? LastChosen { get; private set; }
    public bool LastShotSolved { get; private set; }

    /// <summary>
    /// What each weapon scored at the last choice, the loser included. The rejected option is
    /// recorded on purpose: the log has always said which weapon was picked and never what the
    /// alternative was worth, so "why is it using that" was unanswerable from the record and had
    /// to be re-derived by hand. Two numbers make the choice auditable without playing the game.
    /// </summary>
    public float LastPrimaryExpected { get; private set; }
    public float LastSecondaryExpected { get; private set; }

    private int cooldown;

    private readonly NPC[] pierced = new NPC[MaxPierceCounted];
    private readonly List<NPC> hostiles = new();
    private readonly Dictionary<int, int> spent = new();

    public void Tick()
    {
        if (cooldown > 0)
            cooldown--;
    }

    /// <summary>The weapon that would land the most damage on this target from here, and its profile for the positioner.</summary>
    public CompanionWeapon Choose(in ActionContext ctx, NPC target)
    {
        int now = ctx.Senses.Tick;
        int slot = target.whoAmI;
        if (chosenFor == slot && now - chosenAt < ChoiceCacheTicks && chosen != null)
            return chosen;

        Collect(ctx);
        float a = ExpectedDamage(ctx, Primary, target);
        float b = ExpectedDamage(ctx, Secondary, target);

        chosen = a >= b ? Primary : Secondary;
        LastPrimaryExpected = a;
        LastSecondaryExpected = b;
        chosenFor = slot;
        chosenAt = now;
        LastChosen = chosen;
        return chosen;
    }

    private CompanionWeapon? chosen;
    private int chosenFor = -1;
    private int chosenAt = int.MinValue;

    public WeaponProfile? ProfileFor(in ActionContext ctx, NPC? target)
        => target == null ? null : Choose(ctx, target).Profile;

    /// <summary>
    /// What <paramref name="weapon"/> would take off in the next <see cref="HorizonTicks"/> ticks,
    /// firing at <paramref name="target"/> from where the companion stands. Zero when no arc lands,
    /// which is the whole of the rule that stops an unusable weapon being held.
    /// </summary>
    public float ExpectedDamage(in ActionContext ctx, CompanionWeapon weapon, NPC target)
    {
        if (Vector2.Distance(ctx.Npc.Center, target.Center) > weapon.Reach)
            return 0f;

        Vector2 muzzle = Muzzle(ctx.Npc);
        if (TrajectoryAimer.Solve(muzzle, target, weapon.Profile) is not Vector2 launch)
            return 0f;

        int crossed = TrajectoryAimer.PathHits(muzzle, launch, weapon.Profile, hostiles, pierced);
        if (crossed == 0)
            return 0f;

        // A crit doubles the hit in this game, so the expected hit is the plain damage scaled by
        // how often that happens. Scoring on the expectation rather than the plain number is what
        // lets a weapon that crits often beat one that hits slightly harder and never does.
        float crit = 1f + ctx.Player.GetTotalCritChance(DamageClass.Ranged) / 100f;
        float perHit = weapon.DamagePerHit(ctx) * crit;

        int bodies = Math.Min(weapon.Pierce, crossed);
        int shots = Math.Max(1, HorizonTicks / Math.Max(1, weapon.UseTime));
        int hits = shots * bodies * weapon.ProjectilesPerShot;

        // Hand the hits out one at a time down the line the shot crosses, and let each hostile
        // absorb only the life it has left. That is what stops a weapon being scored for damage
        // it would pour into something already dead: a shot worth three hundred against a slime
        // worth twenty is worth twenty, and the weapon that spends the same window killing eight
        // of them is worth all eight.
        spent.Clear();
        float total = 0f;
        for (int hit = 0; hit < hits; hit++)
        {
            bool landed = false;
            for (int i = 0; i < bodies; i++)
            {
                NPC npc = pierced[i];
                spent.TryGetValue(npc.whoAmI, out int already);
                int remaining = npc.life - already;
                if (remaining <= 0)
                    continue;
                int dealt = (int)MathF.Min(perHit, remaining);
                spent[npc.whoAmI] = already + dealt;
                total += dealt;
                landed = true;
                break;
            }
            if (!landed)
                break; // everything on this arc is dead inside the window; more shots buy nothing
        }
        return total;
    }

    /// <summary>The hostiles worth simulating against: the threat sense's own list, alive and hostile.</summary>
    private void Collect(in ActionContext ctx)
    {
        hostiles.Clear();
        foreach (ThreatRecord t in ctx.Senses.Threats.Threats)
            if (t.Npc != null && t.Npc.active && t.Npc.life > 0)
                hostiles.Add(t.Npc);
    }

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
        ctx.Companion.Motor.Face(target.Center.X);
        if (cooldown > 0)
        {
            ctx.Companion.HoldItem(weapon.ItemType);
            return false;
        }

        Vector2 muzzle = Muzzle(ctx.Npc);
        Vector2? solved = TrajectoryAimer.Solve(muzzle, target, weapon.Profile);
        if (solved == null)
        {
            // The choice is a dozen ticks old and the world has moved: the arc that scored is gone.
            // The other weapon is tried before giving up, because standing still holding an unusable
            // weapon is how the companion died with thirteen hostiles on it and a bow in its hand.
            CompanionWeapon other = ReferenceEquals(weapon, Primary) ? Secondary : Primary;
            if (TrajectoryAimer.Solve(muzzle, target, other.Profile) is Vector2 fallback)
            {
                weapon = other;
                chosen = other;
                LastChosen = other;
                solved = fallback;
            }
        }

        ctx.Companion.HoldItem(weapon.ItemType);
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
