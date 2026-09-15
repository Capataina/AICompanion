#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Aiming;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Inventory;

namespace AICompanion.Companion.Weapons;

/// <summary>
/// The one weapon the companion has: whatever item sits in a weapon slot, read for its numbers.
/// The item is never run — nothing here calls the player's item-use code — and the two things the
/// companion can do with the numbers are the two things the slot accepts: spawn the item's
/// projectile itself, player-owned so kills and drops stay the player's, or sweep the item in an
/// arc around the orb and strike every hostile in it through the game's own strike path. Which of
/// the two an item is was decided at the slot; this class only reads the answer.
///
/// A projectile weapon's facts follow the game's own composition of weapon and ammo in
/// <c>Player.PickAmmo</c> and <c>Player.GetWeaponDamage</c>, read rather than run: damage is the item's
/// plus the ammo's, launch speed the item's plus the ammo's, the projectile is the ammo's when the
/// ammo names one (additively for rockets and solutions), and the player's class modifier for the
/// item's class is applied the way the game applies it to a shot, with the arrow and bullet bonuses
/// where the ammo is one. Ammo is free, so the ammo is the class's default and never consumed. A
/// magic item lands at the mana pool's factor and spends its cost after the damage is read, so the
/// cast that empties the pool lands at the strength the pool had.
/// </summary>
public sealed class ItemWeapon : CompanionWeapon
{
    /// <summary>
    /// Half the swing's opening, radians. A swing is a sector centred on the aim, wide enough that a
    /// body beside the aimed one is struck too — which is what a crowd is worth to a sword — and
    /// narrow enough that a body behind the orb is not. A hundred degrees is the arc a player's
    /// broadsword sweeps on screen, near enough.
    /// </summary>
    public const float SwingHalfAngle = MathHelper.Pi * 50f / 180f;

    /// <summary>How many ticks a shot is traced for at most; a projectile that lives longer is traced this far.</summary>
    private const int MaxTraceTicks = 150;

    /// <summary>Furthest a shot is ever worth attempting, whatever the projectile's speed and life say.</summary>
    private const float MaxShotReach = 1100f;

    public Item Item { get; }
    private readonly Item? ammo;
    private readonly Projectile? sample;

    public ItemWeapon(Item item)
    {
        Item = item;
        ammo = CompanionGear.DefaultAmmo(item);
        IsSwing = item.shoot <= 0;
        ProjectileType = IsSwing ? 0 : ResolveProjectile(item, ammo);
        if (!IsSwing)
            ContentSamples.ProjectilesByType.TryGetValue(ProjectileType, out sample);
        Name = ItemID.Search.TryGetName(item.type, out string name) ? name : $"item-{item.type}";
    }

    /// <summary>
    /// Which projectile leaves the muzzle, by the rules of <c>Player.PickAmmo</c>: a rocket or solution
    /// launcher adds the ammo's projectile id to its own, any other ammo that names a projectile
    /// replaces the weapon's with it, and a weapon with no ammo fires its own.
    /// </summary>
    private static int ResolveProjectile(Item item, Item? ammo)
    {
        if (ammo == null)
            return item.shoot;
        if (item.useAmmo == AmmoID.Rocket || item.useAmmo == AmmoID.Solution)
            return item.shoot + ammo.shoot;
        return ammo.shoot > 0 ? ammo.shoot : item.shoot;
    }

    public override string Name { get; }
    public override int ItemType => Item.type;
    public override int ProjectileType { get; }
    public override bool IsSwing { get; }

    /// <summary>
    /// The swing's reach: the item's drawn width at its scale, plus the body's radius, because the
    /// item is swung from the orb's edge rather than its centre. The radius is read from the one
    /// declaration of the orb's contact circle so a resized body resizes every swing with it.
    /// </summary>
    public float SwingReach => Item.width * Item.scale + CircleContact.Radius;

    public override FlightModel Model
    {
        get
        {
            // A swing's box is the orb's own diameter, not the item's drawn height. The box is what the
            // trace sweeps against terrain and what it intercepts the aimed target with, and a forty-pixel
            // sword box straddled the floor from an orb hovering low over it, so a zombie standing on that
            // floor could not be swung at from any angle. The sector test in InSwing, with its own sight
            // test per body, decides what a swing actually strikes.
            if (IsSwing)
                return new FlightModel(Speed: SwingReach, Motion: LearnedMotion.Straight, MaxFlightTicks: 1,
                    HitboxSize: (int)CircleContact.Diameter, Reach: SwingReach);
            float speed = Item.shootSpeed + (ammo?.shootSpeed ?? 0f);
            int flight = sample == null ? MaxTraceTicks : Math.Clamp(sample.timeLeft, 1, MaxTraceTicks);
            int box = sample == null ? 8 : Math.Max(1, sample.width);
            return new FlightModel(Speed: speed, Motion: ProjectileArcs.MotionFor(ProjectileType), MaxFlightTicks: flight,
                HitboxSize: box, Reach: MathF.Min(MaxShotReach, speed * flight));
        }
    }

    /// <summary>A shot's cadence is the item's use time; a swing's is its animation, because one swing is one hit.</summary>
    public override int BaseUseTime => Math.Max(1, IsSwing ? Item.useAnimation : Item.useTime);

    public override int BaseDamage => Item.damage + (ammo?.damage ?? 0);

    /// <summary>
    /// A swing hurts everything in its arc, so its pierce is the widest the arsenal counts; a
    /// projectile pierces what its sample says, with unlimited penetration reported as that same width.
    /// </summary>
    public override int Pierce
    {
        get
        {
            if (IsSwing || sample == null) return Arsenal.MaxPierceCounted;
            return sample.penetrate < 0 ? Arsenal.MaxPierceCounted : Math.Max(1, sample.penetrate);
        }
    }

    /// <summary>
    /// The player's class modifier for the item's class, with the arrow and bullet bonuses where the
    /// ammo is one — the same combination <c>Player.GetWeaponDamage</c> applies before its mod hooks,
    /// written out rather than called so nothing here enters a hook chain that only exists once mods
    /// have loaded. Then the mana gradient for an item that costs mana.
    ///
    /// Crit is deliberately absent, as it was for the authored kit: these projectiles are spawned
    /// from the companion's own NPC source and that path carries no crit, so scoring one would be
    /// scoring a fiction.
    /// </summary>
    public override int DamagePerHit(in ActionContext ctx)
    {
        Player player = ctx.Player;
        StatModifier damage = player.GetTotalDamage(Item.DamageType);
        if (ammo != null && AmmoID.Sets.IsArrow[ammo.ammo]) damage = damage.CombineWith(player.arrowDamage);
        if (ammo != null && AmmoID.Sets.IsBullet[ammo.ammo]) damage = damage.CombineWith(player.bulletDamage);
        if (ammo != null && AmmoID.Sets.IsSpecialist[ammo.ammo]) damage = damage.CombineWith(player.specialistDamage);
        float perHit = damage.ApplyTo(BaseDamage);
        if (Item.mana > 0)
            perHit *= ctx.Companion.Mana.DamageFactor;
        return Math.Max(0, (int)(perHit + 5E-06f));
    }

    /// <summary>The item's knockback plus its free ammo's, which is what <c>Player.PickAmmo</c> hands a shot and the swing passes the strike.</summary>
    public override float Knockback => Item.knockBack + (ammo?.knockBack ?? 0f);

    public override bool PushesAwayFromOwner => WeaponEffects.PushesAwayFromOwner(ProjectileType);

    public override bool InReach(Vector2 muzzle, NPC target)
        => IsSwing ? NearestDistance(muzzle, target.Hitbox) <= SwingReach
            : Vector2.Distance(muzzle, target.Center) <= Reach;

    /// <summary>
    /// A swing counts every hostile whose box is inside the sector; a shot flies the model and
    /// counts what the flight crosses, in flight order.
    /// </summary>
    public override int Hits(Vector2 muzzle, Vector2 launch, IReadOnlyList<NPC> hostiles, NPC[] into)
    {
        if (!IsSwing)
            return TrajectoryAimer.PathHits(muzzle, launch, Model, hostiles, into);
        int found = 0;
        for (int i = 0; i < hostiles.Count && found < into.Length; i++)
        {
            NPC npc = hostiles[i];
            if (npc != null && npc.active && npc.life > 0 && InSwing(muzzle, launch, npc))
                into[found++] = npc;
        }
        return found;
    }

    /// <summary>
    /// Whether a body is inside the swing: its box within reach of the orb, its centre within the half
    /// angle of the aim, and nothing solid between the orb and it. The sight test is per body, because
    /// only the aim's own ray is traced by the flight model: without it a sword struck through a
    /// one-tile floor at anything in the sector but the aimed target.
    /// </summary>
    public bool InSwing(Vector2 muzzle, Vector2 aim, NPC npc)
    {
        if (NearestDistance(muzzle, npc.Hitbox) > SwingReach) return false;
        Vector2 toBody = npc.Center - muzzle;
        if (toBody != Vector2.Zero && aim != Vector2.Zero)
        {
            float cos = Vector2.Dot(Vector2.Normalize(toBody), Vector2.Normalize(aim));
            if (MathF.Acos(Math.Clamp(cos, -1f, 1f)) > SwingHalfAngle) return false;
        }
        return Brain.Infrastructure.Observation.LineOfSight.Between(muzzle, npc);
    }

    private static float NearestDistance(Vector2 point, Rectangle box)
    {
        float dx = MathF.Max(box.Left - point.X, MathF.Max(0f, point.X - box.Right));
        float dy = MathF.Max(box.Top - point.Y, MathF.Max(0f, point.Y - box.Bottom));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public override FireResult Fire(in ActionContext ctx, Vector2 muzzle, Vector2 launch)
    {
        int damage = DamagePerHit(ctx);
        // Read before spend, so the cast that empties the pool lands at the strength the pool had; and
        // spent for a swing as much as for a shot, or a magic item that is swung would be scaled by a
        // pool it could never empty.
        if (Item.mana > 0)
            ctx.Companion.Mana.Spend(Item.mana);
        if (IsSwing)
            return Swing(ctx, muzzle, launch, damage);
        int slot = Projectile.NewProjectile(ctx.Npc.GetSource_FromAI(), muzzle, launch, ProjectileType, damage, Knockback, Main.myPlayer);
        return new FireResult(slot, 0);
    }

    /// <summary>
    /// Strike every hostile in the arc once, player-owned through <c>Player.ApplyDamageToNPC</c>, which
    /// is the game's own path from a player to an NPC's life: its hit modifiers, the banner buff,
    /// armour penetration, the strike, and the player's on-hit and on-kill bookkeeping. There is no
    /// held-item animation; the body records the item and draws what it draws.
    /// </summary>
    private FireResult Swing(in ActionContext ctx, Vector2 muzzle, Vector2 aim, int damage)
    {
        int struck = 0;
        int direction = aim.X >= 0f ? 1 : -1;
        var strikes = new List<SwingStrike>();
        foreach (var threat in ctx.Senses.Threats.Threats)
        {
            NPC? npc = threat.Npc;
            if (npc == null || !npc.active || npc.life <= 0 || !npc.CanBeChasedBy() || !InSwing(muzzle, aim, npc))
                continue;
            // A swing passes through no NPC hit hook, so what it did is observed here around its own strike. The life it
            // took is what the game dealt while the body lives; a killing strike's is capped at the life that was left and
            // says nothing true about the damage, so it is not taught to the push table. The outcome learner is taught the
            // capped life all the same, because a kill is what the swing achieved.
            Vector2 before = npc.velocity;
            int lifeBefore = npc.life;
            int[] buffTypes = (int[])npc.buffType.Clone(), buffTimes = (int[])npc.buffTime.Clone();
            // The experience ledger reads the same strike from both sides, the way the game's own NPCKillAttempt does, because
            // no NPC hook runs on this path to tell it the companion landed it.
            Progression.CreditKillsAndFights.BeforeStrike(npc, Progression.Striker.Companion);
            ctx.Player.ApplyDamageToNPC(npc, damage, Knockback, direction, crit: false, DamageClass.Melee);
            Progression.CreditKillsAndFights.AfterStrike(npc);
            int dealt = lifeBefore - npc.life;
            if (dealt > 0 && npc.life > 0)
                WeaponEffects.ObserveHit(Item.type, npc, before, npc.velocity, Knockback, direction, damage, dealt, crit: false);
            strikes.Add(new SwingStrike(npc, Math.Max(0, dealt), buffTypes, buffTimes));
            struck++;
        }
        return new FireResult(-1, struck, strikes);
    }
}
