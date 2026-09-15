extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Tools.Ledger;
using C = live::AICompanion.Companion.Brain.Activities.ActionContext;
using T = live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatRecord;
using CompanionGear = live::AICompanion.Companion.Inventory.CompanionGear;
using GearSlot = live::AICompanion.Companion.Inventory.GearSlot;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;
using ItemWeapon = live::AICompanion.Companion.Weapons.ItemWeapon;
using Arsenal = live::AICompanion.Companion.Weapons.Arsenal;

/// <summary>
/// The arsenal fed by the gear rather than by an authored pair. Each row puts one real item in a
/// weapon slot, stands a zombie where that item can reach it, and asks the arsenal to fire: the
/// projectile that appears must be the item's (or its free ammo's), player-owned, at the item's
/// composed speed and damage; a magic cast must spend the pool and land at the pool's factor, and
/// still land at an empty pool; a swing must strike the body in its arc through the game's own
/// strike path and refuse a body out of reach; a refused item forced into a slot must never reach
/// the arsenal; and an empty gear must say so rather than throw.
///
/// Enemy AI does not run and the spawned projectiles are never advanced, so these establish what
/// leaves the muzzle and what a swing does on contact, not a fight.
/// </summary>
internal static class VerifyItemWeapon
{
    private const int FloorY = 60;

    public static int Run()
    {
        ABowFiresItsFreeArrowPlayerOwned();
        APistolFiresTheMusketBallsProjectileAtTheComposedSpeed();
        AWandSpendsManaAndLandsAtTheGradient();
        ASwordStrikesTheBodyInItsArcAndRefusesOneOutOfReach();
        ARefusedItemInASlotNeverReachesTheArsenal();
        AnEmptyGearSaysNoWeapon();
        Console.WriteLine("item weapon: a bow fires the wooden arrow player-owned, a pistol the musket ball at the composed speed, a wand spends mana and lands at the gradient down to an empty pool, a sword strikes in its arc and refuses out of reach, a yoyo in a slot reaches nothing, and an empty gear says no-weapon");
        return 0;
    }

    /// <summary>
    /// A zombie on the floor, this many tiles to the companion's right, on a floor at row 60 this scene
    /// lays itself. The per-case reset does not rebuild the tile map, so a scene that trusted the suite's
    /// floor inherited whatever the cases before it dug or built there: alone this fixture passed and in
    /// sequence a flat bullet met a tile an earlier case had left in the air.
    /// </summary>
    private static (CompanionNPC Companion, NPC Enemy, C Ctx) Scene(int tilesAway, params (GearSlot Slot, int Item)[] gear)
    {
        for (int x = 20; x < 60; x++)
        {
            for (int y = 40; y < FloorY; y++) { Tile air = Main.tile[x, y]; air.HasTile = false; air.LiquidAmount = 0; }
            for (int y = FloorY; y <= FloorY + 2; y++) { Tile floor = Main.tile[x, y]; floor.HasTile = true; floor.TileType = 1; floor.Slope = 0; floor.IsHalfBlock = false; }
        }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(30 * 16f, FloorY * 16f - player.height);
        player.velocity = Vector2.Zero;
        companion.NPC.position = new Vector2(32 * 16f, FloorY * 16f - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;
        foreach (Projectile projectile in Main.projectile) projectile.active = false;
        // The player's strike path reads the banner buff table off the scene metrics the game
        // constructs at start-up and this host never does; an empty table is the one a fresh world has.
        Main.SceneMetrics ??= new SceneMetrics();

        CompanionGear slots = player.GetModPlayer<CompanionPlayer>().Gear;
        for (int i = 0; i < CompanionGear.SlotCount; i++) slots.Slots[i] = new Item();
        foreach (var (slot, item) in gear) slots.Slots[(int)slot].SetDefaults(item);

        var enemy = new NPC();
        enemy.SetDefaults(NPCID.Zombie);
        enemy.whoAmI = 25;
        enemy.active = true;
        enemy.velocity = Vector2.Zero;
        enemy.Bottom = new Vector2(32 * 16f + tilesAway * 16f, FloorY * 16f);
        Main.npc[25] = enemy;

        companion.Brain.Senses.Update(companion.NPC, player, companion.Motor);
        var threats = companion.Brain.Senses.Threats.Threats;
        threats.Clear();
        threats.Add(new T
        {
            Npc = enemy,
            DistanceToCompanion = Vector2.Distance(companion.NPC.Bottom, enemy.Bottom),
            DistanceToPlayer = Vector2.Distance(player.Bottom, enemy.Bottom),
        });
        return (companion, enemy, new C(companion, companion.Brain.Senses));
    }

    private static Projectile TheOneActiveProjectile()
    {
        Projectile? found = null;
        foreach (Projectile projectile in Main.projectile)
            if (projectile.active)
            {
                Require(found == null, "exactly one projectile must have been spawned");
                found = projectile;
            }
        Require(found != null, "a projectile must have been spawned");
        return found!;
    }

    private static void ABowFiresItsFreeArrowPlayerOwned()
    {
        var (companion, enemy, ctx) = Scene(10, (GearSlot.FirstWeapon, ItemID.WoodenBow));
        Arsenal arsenal = companion.Arsenal;
        Require(arsenal.BestTarget(ctx) == enemy, "the zombie ten tiles away must be the best target for a bow");
        Require(arsenal.TryFire(ctx, enemy), $"the bow must fire; outcome={arsenal.LastFireOutcome}");
        Require(arsenal.LastFireOutcome == "fired" && arsenal.LastChosen is ItemWeapon { ItemType: ItemID.WoodenBow },
            $"the shot must be recorded as fired by the bow; outcome={arsenal.LastFireOutcome} chosen={arsenal.LastChosen?.Name}");
        Projectile arrow = TheOneActiveProjectile();
        Item bow = ContentSamples.ItemsByType[ItemID.WoodenBow], woodenArrow = ContentSamples.ItemsByType[ItemID.WoodenArrow];
        Require(arrow.type == woodenArrow.shoot, $"a bow with free ammo fires the wooden arrow's projectile; got {arrow.type}");
        Require(arrow.owner == Main.myPlayer && arrow.friendly, "the arrow is the player's, so its kill is the player's");
        float speed = arrow.velocity.Length();
        Require(MathF.Abs(speed - (bow.shootSpeed + woodenArrow.shootSpeed)) < .01f,
            $"launch speed is the bow's plus the arrow's ({bow.shootSpeed + woodenArrow.shootSpeed}); got {speed}");
        int expected = ((ItemWeapon)arsenal.LastChosen!).DamagePerHit(ctx);
        Require(arrow.damage == expected && expected == bow.damage + woodenArrow.damage,
            $"the arrow carries the bow's plus the arrow's damage as the weapon scored it; projectile={arrow.damage} weapon={expected} items={bow.damage + woodenArrow.damage}");
        Require(arsenal.CooldownTicks == arsenal.LastChosen!.UseTime, "a shot starts the weapon's own cooldown");
        Require(arsenal.MaxReach > 0f && arsenal.Weapons.Count == 1, "one weapon in hand, with a reach");
    }

    private static void APistolFiresTheMusketBallsProjectileAtTheComposedSpeed()
    {
        var (companion, enemy, ctx) = Scene(10, (GearSlot.SecondWeapon, ItemID.FlintlockPistol));
        Arsenal arsenal = companion.Arsenal;
        Require(arsenal.TryFire(ctx, enemy), $"the pistol must fire; outcome={arsenal.LastFireOutcome}");
        Projectile bullet = TheOneActiveProjectile();
        Item pistol = ContentSamples.ItemsByType[ItemID.FlintlockPistol], ball = ContentSamples.ItemsByType[ItemID.MusketBall];
        Require(bullet.type == ball.shoot && bullet.type == ProjectileID.Bullet, $"a gun with free ammo fires the musket ball's projectile; got {bullet.type}");
        Require(MathF.Abs(bullet.velocity.Length() - (pistol.shootSpeed + ball.shootSpeed)) < .01f,
            $"launch speed is the pistol's plus the ball's; got {bullet.velocity.Length()} expected {pistol.shootSpeed + ball.shootSpeed}");
        Require(bullet.damage == pistol.damage + ball.damage, $"damage is the pistol's plus the ball's; got {bullet.damage}");
        // A bullet flies straight, so the shot must have been solved on the direct line from the orb's
        // centre to the zombie's, within the arsenal's aim noise. The earlier form asserted a flat
        // shot, which held only while the muzzle and the target happened to be level: an orb hovers
        // at a different height from a walker's eye, and the noise alone is four degrees.
        float direct = (enemy.Center - companion.NPC.Center).ToRotation();
        float fired = bullet.velocity.ToRotation();
        Require(MathF.Abs(MathHelper.WrapAngle(fired - direct)) <= arsenal.LastChosen!.AimNoise + .02f,
            $"a bullet is solved on the direct line; fired {MathHelper.ToDegrees(fired):0.0} deg against direct {MathHelper.ToDegrees(direct):0.0} deg, noise {MathHelper.ToDegrees(arsenal.LastChosen!.AimNoise):0.0} deg");
    }

    private static void AWandSpendsManaAndLandsAtTheGradient()
    {
        var (companion, enemy, ctx) = Scene(6, (GearSlot.FirstWeapon, ItemID.WandofSparking));
        Arsenal arsenal = companion.Arsenal;
        Item wand = ContentSamples.ItemsByType[ItemID.WandofSparking];
        var mana = companion.Mana;
        Require(mana.Fraction == 1f && mana.DamageFactor == 1f, "the premise: a full pool lands at full strength");
        Require(arsenal.TryFire(ctx, enemy), $"the wand must fire; outcome={arsenal.LastFireOutcome}");
        Projectile spark = TheOneActiveProjectile();
        Require(spark.type == wand.shoot, $"the wand fires its own projectile; got {spark.type}");
        Require(spark.damage == wand.damage, $"a full pool lands the wand's own damage {wand.damage}; got {spark.damage}");
        Require(MathF.Abs(mana.Current - (mana.Max - wand.mana)) < .001f, $"the cast spent {wand.mana}; pool {mana.Current} of {mana.Max}");

        // Drain the pool, wait out the cooldown, and cast again: it still fires, at half strength.
        mana.Spend(mana.Max);
        Require(mana.Fraction == 0f, "the pool is empty");
        foreach (Projectile projectile in Main.projectile) projectile.active = false;
        int reload = arsenal.CooldownTicks;
        for (int tick = 0; tick <= reload; tick++) arsenal.Tick();
        Require(arsenal.CooldownTicks == 0, "the reload has been waited out");
        Require(arsenal.TryFire(ctx, enemy), $"an empty pool never refuses a cast; outcome={arsenal.LastFireOutcome}");
        Projectile tired = TheOneActiveProjectile();
        int half = (int)(wand.damage * live::AICompanion.Companion.Weapons.CompanionMana.EmptyDamageFactor);
        Require(tired.damage == half, $"an empty pool lands at the empty factor: expected {half}, got {tired.damage}");
        Require(((ItemWeapon)arsenal.LastChosen!).DamagePerHit(ctx) == half, "the scorer reads the same tired damage the cast landed");
    }

    private static void ASwordStrikesTheBodyInItsArcAndRefusesOneOutOfReach()
    {
        var (companion, enemy, ctx) = Scene(1, (GearSlot.FirstWeapon, ItemID.CopperBroadsword));
        Arsenal arsenal = companion.Arsenal;
        var sword = (ItemWeapon)arsenal.Weapons[0];
        Require(sword.IsSwing && sword.ProjectileType == 0, "a broadsword is a swing with no projectile");
        Require(sword.InReach(Arsenal.Muzzle(companion.NPC), enemy), $"the premise: the zombie a tile away is inside the swing's reach of {sword.SwingReach}px");
        int lifeBefore = enemy.life;
        Require(arsenal.BestTarget(ctx) == enemy, "the adjacent zombie is the best target for a sword");
        Require(arsenal.TryFire(ctx, enemy), $"the sword must swing; outcome={arsenal.LastFireOutcome}");
        Require(arsenal.LastFireOutcome == "fired", $"a swing is recorded as fired; got {arsenal.LastFireOutcome}");
        Require(enemy.life < lifeBefore, $"the swing struck the zombie through the game's strike path; life {lifeBefore} -> {enemy.life}");
        bool anyProjectile = false;
        foreach (Projectile projectile in Main.projectile) anyProjectile |= projectile.active;
        Require(!anyProjectile, "a swing spawns no projectile");

        // The same sword against a zombie ten tiles off: nothing to swing at, and nothing struck.
        var (far, farEnemy, farCtx) = Scene(10, (GearSlot.FirstWeapon, ItemID.CopperBroadsword));
        int farLife = farEnemy.life;
        Require(far.Arsenal.BestTarget(farCtx) == null, "a zombie out of the swing's reach is not a target for a sword alone");
        Require(!far.Arsenal.TryFire(farCtx, farEnemy) && farEnemy.life == farLife,
            $"the sword refuses a body out of reach and strikes nothing; outcome={far.Arsenal.LastFireOutcome}");
    }

    private static void ARefusedItemInASlotNeverReachesTheArsenal()
    {
        // The UI never lets a yoyo in; a save written by hand could. The arsenal is the second gate.
        var (companion, enemy, ctx) = Scene(10, (GearSlot.FirstWeapon, ItemID.WoodYoyo), (GearSlot.SecondWeapon, ItemID.WoodenBow));
        Arsenal arsenal = companion.Arsenal;
        Require(arsenal.TryFire(ctx, enemy), $"the bow beside the yoyo still fires; outcome={arsenal.LastFireOutcome}");
        Require(arsenal.Weapons.Count == 1 && arsenal.Weapons[0].ItemType == ItemID.WoodenBow,
            $"only the bow is enumerated; got {arsenal.Weapons.Count} weapon(s)");
    }

    private static void AnEmptyGearSaysNoWeapon()
    {
        var (companion, enemy, ctx) = Scene(10);
        Arsenal arsenal = companion.Arsenal;
        Require(arsenal.Weapons.Count == 0 && arsenal.MaxReach == 0f, "no weapons, no reach");
        Require(arsenal.BestTarget(ctx) == null, "nothing is a target with nothing to fire");
        Require(!arsenal.CanEngage(ctx, enemy), "nothing can be engaged with nothing to fire");
        Require(!arsenal.TryFire(ctx, enemy) && arsenal.LastFireOutcome == "no-weapon", $"the outcome names the empty hands; got {arsenal.LastFireOutcome}");
        Require(float.IsPositiveInfinity(arsenal.EstimateRemovalTicks(ctx, enemy)), "a target cannot be removed with nothing to fire");
        Require(arsenal.ProfileFor(ctx, enemy) == null, "no flight model with nothing to fire");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
