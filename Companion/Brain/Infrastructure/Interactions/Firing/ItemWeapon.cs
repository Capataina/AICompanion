#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;
using AICompanion.Companion.Inventory;

namespace AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;

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
            // A swing's box is the orb's own diameter, not the item's drawn height, because the reach gate
            // measures from the orb: a forty-pixel sword box straddled the floor from an orb hovering low
            // over it, so a zombie standing on that floor could not be swung at from any angle. The sector
            // test in InSwing, with its own sight test per body, decides what a swing actually strikes.
            if (IsSwing)
                return new FlightModel(Speed: SwingReach, HitboxSize: (int)CircleContact.Diameter,
                    MaxFlightTicks: 1, Reach: SwingReach);
            float speed = Item.shootSpeed + (ammo?.shootSpeed ?? 0f);
            int flight = sample == null ? MaxTraceTicks : Math.Clamp(sample.timeLeft, 1, MaxTraceTicks);
            int box = sample == null ? 8 : Math.Max(1, sample.width);
            return new FlightModel(Speed: speed, HitboxSize: box, MaxFlightTicks: flight,
                Reach: MathF.Min(MaxShotReach, speed * flight));
        }
    }

    /// <summary>A shot's cadence is the item's use time; a swing's is its animation, because one swing is one hit.</summary>
    public override int BaseUseTime => Math.Max(1, IsSwing ? Item.useAnimation : Item.useTime);

    public override int BaseDamage => Item.damage + (ammo?.damage ?? 0);

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
        float perHit = AuditDamageOverride ?? ScaledBaseDamage(ctx.Player);
        if (Item.mana > 0)
            perHit *= ctx.Companion.Mana.DamageFactor;
        return Math.Max(0, (int)(perHit + 5E-06f));
    }

    public override float ScaledBaseDamage(Player player)
    {
        StatModifier damage = player.GetTotalDamage(Item.DamageType);
        if (ammo != null && AmmoID.Sets.IsArrow[ammo.ammo]) damage = damage.CombineWith(player.arrowDamage);
        if (ammo != null && AmmoID.Sets.IsBullet[ammo.ammo]) damage = damage.CombineWith(player.bulletDamage);
        if (ammo != null && AmmoID.Sets.IsSpecialist[ammo.ammo]) damage = damage.CombineWith(player.specialistDamage);
        return damage.ApplyTo(BaseDamage);
    }

    /// <summary>The item's knockback plus its free ammo's, which is what <c>Player.PickAmmo</c> hands a shot and the swing passes the strike.</summary>
    public override float Knockback => Item.knockBack + (ammo?.knockBack ?? 0f);

    public override int ManaCost => Item.mana;

    /// <summary>The launch speed one use fires at: the item's plus its free ammo's, the number the volley is expanded and the model flown with.</summary>
    public float LaunchSpeed => Item.shootSpeed + (ammo?.shootSpeed ?? 0f);

    public override bool PushesAwayFromOwner => WeaponEffects.PushesAwayFromOwner(ProjectileType);

    public override bool InReach(Vector2 muzzle, NPC target)
        => IsSwing ? NearestDistance(muzzle, target.Hitbox) <= SwingReach
            : Vector2.Distance(muzzle, target.Center) <= Reach;

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
        return LineOfSight.Between(muzzle, npc);
    }

    private static float NearestDistance(Vector2 point, Rectangle box)
    {
        float dx = MathF.Max(box.Left - point.X, MathF.Max(0f, point.X - box.Right));
        float dy = MathF.Max(box.Top - point.Y, MathF.Max(0f, point.Y - box.Bottom));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public override FireResult Fire(in ActionContext ctx, Vector2 muzzle, Vector2 launch, Vector2 aim)
    {
        int damage = DamagePerHit(ctx);
        // Read before spend, so the cast that empties the pool lands at the strength the pool had; and
        // spent for a swing as much as for a shot, or a magic item that is swung would be scaled by a
        // pool it could never empty.
        if (Item.mana > 0)
            ctx.Companion.Mana.Spend(Item.mana);
        if (IsSwing)
            return Swing(ctx, muzzle, launch, damage);
        int tick = (int)Main.GameUpdateCount;
        float composedSpeed = Item.shootSpeed + (ammo?.shootSpeed ?? 0f);
        ModifierState modifiers = ApplyCompanionModifiers.Current();
        int useId = GroupSpawnsIntoUses.OpenCompanionUse(tick, Item.type, aim, muzzle, damage, composedSpeed);
        // An item the player has never fired has no shape: one projectile, exactly as before, so an
        // unobserved item fires bit-identically to the single shot it always fired rather than through a
        // normalise-and-rescale round trip. Every spawn still joins the use and opens its trace, so arcs
        // keep learning from default shots and the cursor spoof knows their aim.
        if (!LearnVolleyShapes.HasShape(Item.type))
        {
            int only = Projectile.NewProjectile(ctx.Npc.GetSource_FromAI(), muzzle, launch, ProjectileType, damage, Knockback, Main.myPlayer);
            var landed = new List<int>();
            if ((uint)only < (uint)Main.maxProjectiles)
            {
                RecordProjectileFlights.NoteCompanionSpawn(only, Main.projectile[only], useId, modifiers);
                landed.Add(only);
            }
            GroupSpawnsIntoUses.CloseCompanionUse(useId, tick);
            return new FireResult(only, 0, null, landed);
        }
        Vector2 aimDirection = launch == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(launch);
        var slots = new List<int>();
        int first = -1;
        foreach (VolleySpawn spec in LearnVolleyShapes.ShapeFor(Item.type)
            .Expand(muzzle, aim, aimDirection, ProjectileType, damage, composedSpeed))
        {
            int slot = Projectile.NewProjectile(ctx.Npc.GetSource_FromAI(), spec.Position, spec.Velocity, spec.ProjectileType, spec.Damage, Knockback, Main.myPlayer);
            if (first < 0) first = slot;
            if ((uint)slot >= (uint)Main.maxProjectiles) continue;
            RecordProjectileFlights.NoteCompanionSpawn(slot, Main.projectile[slot], useId, modifiers);
            slots.Add(slot);
        }
        GroupSpawnsIntoUses.CloseCompanionUse(useId, tick);
        return new FireResult(first, 0, null, slots);
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
            // took is the strike's whole damage whether or not it killed, because NPC.StrikeNPC subtracts the damage with no
            // floor at zero (NPC.cs 92307 as decompiled) and checkDead only deactivates the body (84177): a kill records what
            // a projectile's on-hit hook reports for the same hit. A killing strike's push says nothing, since the body is
            // gone, so only a strike the body survived teaches the push table.
            Vector2 before = npc.velocity;
            int lifeBefore = npc.life;
            int[] buffTypes = (int[])npc.buffType.Clone(), buffTimes = (int[])npc.buffTime.Clone();
            // The experience ledger reads the same strike from both sides, the way the game's own NPCKillAttempt does, because
            // no NPC hook runs on this path to tell it the companion landed it.
            Progression.CreditKillsAndFights.BeforeStrike(npc, Progression.Striker.Companion);
            DeliverStrike(ctx.Player, npc, damage, Knockback, direction);
            Progression.CreditKillsAndFights.AfterStrike(npc);
            int dealt = lifeBefore - npc.life;
            if (dealt > 0 && npc.life > 0)
                WeaponEffects.ObserveHit(Item.type, npc, before, npc.velocity, Knockback, direction, damage, dealt, crit: false);
            strikes.Add(new SwingStrike(npc, Math.Max(0, dealt), buffTypes, buffTimes));
            struck++;
        }
        return new FireResult(-1, struck, strikes);
    }

    /// <summary>
    /// The strike itself: <c>Player.ApplyDamageToNPC</c> as melee with no crit, taking the player, the body, the damage, the
    /// knockback and the direction. A fixture substitutes it, because a killing strike through the player's bookkeeping sends
    /// the strike as a network client and reads loot tables and achievements, none of which exist headless; nothing in the
    /// mod assigns it.
    /// </summary>
    public static Action<Player, NPC, int, float, int> DeliverStrike = (player, npc, damage, knockback, direction)
        => player.ApplyDamageToNPC(npc, damage, knockback, direction, crit: false, DamageClass.Melee);
}
