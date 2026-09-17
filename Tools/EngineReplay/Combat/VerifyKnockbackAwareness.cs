extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Tools.Ledger;
using C = live::AICompanion.Companion.Brain.Activities.ActionContext;
using T = live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatRecord;
using E = live::AICompanion.Companion.Brain.Activities.Combat.Planning.EvaluateAttackOutcomes;
using W = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.WeaponEffects;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;
using CompanionGear = live::AICompanion.Companion.Inventory.CompanionGear;
using GearSlot = live::AICompanion.Companion.Inventory.GearSlot;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;
using Combat = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.CompanionCombat;
using Forecasts = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.ForecastUses;
using Positioner = live::AICompanion.Companion.Brain.Infrastructure.Position.Positioner;
using PositionRequest = live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;

/// <summary>
/// A hit pushes, and the companion knows it. The owner watched it knock enemies into the player again and again on
/// 15 September 2026, and nothing in the mod modelled knockback at all; these rows hold the four things that answer it.
///
/// The prior is the game's: a strike through the game's own player-to-NPC path leaves exactly the velocity the table
/// predicts, on both of the game's knockback branches and for an enemy that resists all of it. The table learns: a swing
/// files what it did against the weapon and the enemy type, and hits that push half as far teach that pair, and only that
/// pair, half the push. The arsenal charges a shot for the danger its push adds and never for the danger the enemy already
/// carried, which is the acceptance scene: the player on one side of a zombie, a bow on the other, and the same shot from
/// the player's side as the control. Position selection prefers the player's side of the target in proportion to the push,
/// and a weapon that pushes nothing has no preference. And a wound to a dangerous enemy is worth more than the same wound
/// to a harmless one, while a killing hit is never charged for a push.
///
/// Every pass line was declared before the first run. The acceptance scene uses a zombie whose knockback resistance is set
/// to full, so a wooden bow's push is two pixels a tick held for the settle window rather than one, which is large enough
/// that a charge and a share read well clear of rounding; the prior row uses the zombie's own resistance.
///
/// Enemy AI does not run and no projectile is advanced, so this establishes the arithmetic and the decisions it feeds, not
/// a fight. The projectile hook path — the NPC modify hook keeping the pre-strike velocity and the on-hit hook teaching the
/// table — is not exercised, because no loader runs the global hooks headless; its arithmetic is the same method the swing
/// row drives.
/// </summary>
internal static class VerifyKnockbackAwareness
{
    private const int FloorY = 60;

    public static int Run()
    {
        ThePriorIsTheGamesOwnStrike();
        ASwingTeachesTheTableWhatItDid();
        LearnedPushesReplaceThePriorForTheirPairOnly();
        TheOwnerSideRuleIsTheGamesList();
        AShotIsChargedForPushingAnEnemyIntoThePlayer();
        AFiringStandIsWorthLessWhereItsShotPushesTheTargetIntoThePlayer();
        AWoundIsCreditedByDangerAndAKillIsNeverChargedForItsPush();
        Console.WriteLine("knockback awareness: the prior is the game's strike on both branches, a swing teaches its pair, a push into the player is charged and a push away is not, a stand whose shot pushes into the player keeps less of its score, and a wound to a dangerous enemy earns credit");
        return 0;
    }

    private static (CompanionNPC Companion, NPC Enemy, C Ctx) Scene(float enemyResist, params (GearSlot Slot, int Item)[] gear)
    {
        for (int x = 10; x < 70; x++)
        {
            for (int y = 40; y < FloorY; y++) { Tile air = Main.tile[x, y]; air.HasTile = false; air.LiquidAmount = 0; }
            for (int y = FloorY; y <= FloorY + 2; y++) { Tile floor = Main.tile[x, y]; floor.HasTile = true; floor.TileType = 1; floor.Slope = 0; floor.IsHalfBlock = false; }
        }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
        var companion = VerifyCompanionLifecycle.Create();
        W.Reset();
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.position = new Vector2(30 * 16f, FloorY * 16f - player.height);
        player.velocity = Vector2.Zero;
        foreach (Projectile projectile in Main.projectile) projectile.active = false;
        Main.SceneMetrics ??= new SceneMetrics();

        CompanionGear slots = player.GetModPlayer<CompanionPlayer>().Gear;
        for (int i = 0; i < CompanionGear.SlotCount; i++) slots.Slots[i] = new Item();
        foreach (var (slot, item) in gear) slots.Slots[(int)slot].SetDefaults(item);

        var enemy = new NPC();
        enemy.SetDefaults(NPCID.Zombie);
        enemy.whoAmI = 25;
        enemy.active = true;
        enemy.velocity = Vector2.Zero;
        enemy.knockBackResist = enemyResist;
        enemy.Bottom = new Vector2(36 * 16f, FloorY * 16f);
        Main.npc[25] = enemy;

        // The orb directly above the zombie, so a push either way moves the zombie away from it by the same amount and the
        // companion's half of the charge is the same for the two mirrored shots: what differs between them is the player.
        companion.NPC.Center = enemy.Center + new Vector2(0f, -200f);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.life = companion.NPC.lifeMax;

        companion.Brain.Senses.Update(companion.NPC, player);
        var threats = companion.Brain.Senses.Threats.Threats;
        threats.Clear();
        float toPlayer = Vector2.Distance(enemy.Center, player.Center);
        float toCompanion = Vector2.Distance(enemy.Center, companion.NPC.Center);
        threats.Add(new T
        {
            Npc = enemy,
            Class = live::AICompanion.Companion.Brain.Infrastructure.Observation.MovementClass.Walker,
            CanReachPlayer = true,
            CanReachCompanion = true,
            ExpectedDamage = enemy.damage,
            ObservedSpeed = 1f,
            DistanceToPlayer = toPlayer,
            DistanceToCompanion = toCompanion,
            TicksToPlayer = toPlayer,
            TicksToCompanion = toCompanion,
            EffectiveDamageToPlayer = live::AICompanion.Companion.Brain.Infrastructure.Observation.EstimateEffectiveDamage.ToPlayer(player, enemy.damage),
            EffectiveDamageToCompanion = live::AICompanion.Companion.Brain.Infrastructure.Observation.EstimateEffectiveDamage.ToNpc(companion.NPC, enemy.damage),
            HasSightOnPlayer = true,
            HasSightOnCompanion = true,
        });
        return (companion, enemy, new C(companion, companion.Brain.Senses));
    }

    /// <summary>
    /// A strike through <c>Player.ApplyDamageToNPC</c>, the game's own path, against the table's prior given the same
    /// knockback, direction and the damage the strike dealt. Three arms: a zombie at its own resistance struck hard enough
    /// for the heavy branch; a zombie with a great deal of life, already moving the other way, for the light branch where
    /// the resistance lands a second time; and a zombie that resists all knockback. A prior that forgot the second
    /// resistance, the falloff or the heavy branch's clamp misses one of them.
    /// </summary>
    private static void ThePriorIsTheGamesOwnStrike()
    {
        var (_, _, ctx) = Scene(.5f);
        Player player = ctx.Player;

        (Vector2 Native, Vector2 Prior, int Dealt) Strike(float resist, int lifeMax, Vector2 velocity, float knockback, int direction, int damage)
        {
            var npc = new NPC();
            npc.SetDefaults(NPCID.Zombie);
            npc.whoAmI = 30;
            npc.active = true;
            npc.lifeMax = npc.life = lifeMax;
            npc.knockBackResist = resist;
            npc.velocity = velocity;
            npc.Bottom = new Vector2(40 * 16f, FloorY * 16f);
            Main.npc[30] = npc;
            int lifeBefore = npc.life;
            player.ApplyDamageToNPC(npc, damage, knockback, direction, crit: false);
            int dealt = lifeBefore - npc.life;
            Require(dealt > 0 && npc.life > 0, $"premise: the strike must land and leave the zombie alive; dealt={dealt} life={npc.life}");
            Vector2 prior = W.PriorVelocityAfter(velocity, knockback, direction, dealt, npc);
            return (npc.velocity, prior, dealt);
        }

        var heavy = Strike(.5f, 45, Vector2.Zero, 6f, -1, 20);
        Require(heavy.Dealt * 10 > 45, $"premise: the first arm is the heavy branch; dealt={heavy.Dealt}");
        Require(Vector2.Distance(heavy.Native, heavy.Prior) < 1e-4f,
            $"heavy branch: the prior must be the game's strike; native={heavy.Native} prior={heavy.Prior}");
        Require(heavy.Native.X < 0f, $"premise: a leftward strike pushes left; native={heavy.Native}");

        var light = Strike(.5f, 5000, new Vector2(1.5f, 0f), 6f, -1, 20);
        Require(light.Dealt * 10 <= 5000, $"premise: the second arm is the light branch; dealt={light.Dealt}");
        Require(Vector2.Distance(light.Native, light.Prior) < 1e-4f,
            $"light branch: the prior must be the game's strike, resistance applied twice; native={light.Native} prior={light.Prior}");
        Require(MathF.Abs(light.Native.X - (-3f * .5f)) < 1e-4f,
            $"premise read from the decompile: knockback 6 at half resistance is 3, then times the direction and the resistance again; native={light.Native}");

        var immune = Strike(0f, 45, new Vector2(.7f, 0f), 6f, -1, 20);
        Require(immune.Native == new Vector2(.7f, 0f) && immune.Prior == immune.Native,
            $"an enemy that resists all knockback is not pushed, and the prior says so; native={immune.Native} prior={immune.Prior}");
        EmitLedgerRows.Detail(FormattableString.Invariant($"prior against native strike: heavy {heavy.Native.X:0.000},{heavy.Native.Y:0.000}; light {light.Native.X:0.000},{light.Native.Y:0.000}; immune unchanged"));
    }

    /// <summary>
    /// A sword swing through the arsenal strikes the zombie and files one push sample and one damage sample against the
    /// sword and the zombie type, each a factor of one because nothing but the game's own arithmetic stood between the
    /// strike and the prior.
    /// </summary>
    private static void ASwingTeachesTheTableWhatItDid()
    {
        var (companion, enemy, ctx) = Scene(.5f, (GearSlot.FirstWeapon, ItemID.CopperBroadsword));
        companion.NPC.Center = enemy.Center + new Vector2(-22f, -4f);
        Combat combat = companion.Combat;
        Require(W.PushEvidence(ItemID.CopperBroadsword, NPCID.Zombie) == 0, "premise: nothing learned before the swing");
        int lifeBefore = enemy.life;
        var swung = CombatFixture.FireOnce(companion, ctx);
        Require(swung.Fired, $"premise: the sword must swing; outcome={combat.LastFireOutcome}");
        Require(enemy.life < lifeBefore && enemy.life > 0, $"premise: the swing wounds without killing; life {lifeBefore} -> {enemy.life}");
        Require(W.PushEvidence(ItemID.CopperBroadsword, NPCID.Zombie) == 1 && W.DamageEvidence(ItemID.CopperBroadsword, NPCID.Zombie) == 1,
            $"a landed swing files one push and one damage sample; push={W.PushEvidence(ItemID.CopperBroadsword, NPCID.Zombie)} damage={W.DamageEvidence(ItemID.CopperBroadsword, NPCID.Zombie)}");
        float push = W.PushFactor(ItemID.CopperBroadsword, NPCID.Zombie), damage = W.DamageFactor(ItemID.CopperBroadsword, NPCID.Zombie);
        Require(MathF.Abs(push - 1f) < 1e-3f, $"an unmodded strike teaches the prior's own push; factor={push}");
        Require(MathF.Abs(damage - 1f) < .05f, $"an unmodded strike teaches the prior's own damage; factor={damage}");
    }

    /// <summary>
    /// Three hits that push half as far as the prior says teach that weapon on that enemy type a factor of one half, and the
    /// same weapon on another enemy type, and another weapon on the same enemy, keep the prior. A push the other way teaches a
    /// negative factor and turns the settled push round. A hit whose prior predicts no horizontal change files no push sample,
    /// and a crit files nothing.
    /// </summary>
    private static void LearnedPushesReplaceThePriorForTheirPairOnly()
    {
        var (_, enemy, _) = Scene(.5f);
        Vector2 before = Vector2.Zero;
        Vector2 prior = W.PriorVelocityAfter(before, 4f, 1, 10, enemy);
        Require(prior.X > .5f, $"premise: the prior pushes right; {prior}");
        Vector2 halved = new(prior.X * .5f, prior.Y);
        for (int i = 0; i < 3; i++)
            W.ObserveHit(ItemID.WoodenBow, enemy, before, halved, 4f, 1, 10f, 10, crit: false);
        Require(MathF.Abs(W.PushFactor(ItemID.WoodenBow, NPCID.Zombie) - .5f) < 1e-4f,
            $"half-pushes teach half the push; factor={W.PushFactor(ItemID.WoodenBow, NPCID.Zombie)}");
        Require(W.PushFactor(ItemID.WoodenBow, NPCID.BlueSlime) == 1f && W.PushFactor(ItemID.ThrowingKnife, NPCID.Zombie) == 1f,
            "another enemy type and another weapon keep the prior");
        float settledHalf = W.SettledPush(ItemID.WoodenBow, enemy, 4f, 10f, 1);
        float settledPrior = W.SettledPush(ItemID.ThrowingKnife, enemy, 4f, 10f, 1);
        Require(MathF.Abs(settledHalf - settledPrior * .5f) < 1e-3f && settledPrior > 0f,
            $"the settled push follows the learned factor; learned={settledHalf} prior={settledPrior}");

        for (int i = 0; i < W.SamplesKept; i++)
            W.ObserveHit(ItemID.ThrowingKnife, enemy, before, new Vector2(-prior.X, prior.Y), 4f, 1, 10f, 10, crit: false);
        Require(W.SettledPush(ItemID.ThrowingKnife, enemy, 4f, 10f, 1) < 0f, "a weapon whose hits push the other way learns the other direction");

        int crits = W.DamageEvidence(ItemID.CopperBroadsword, NPCID.Zombie);
        W.ObserveHit(ItemID.CopperBroadsword, enemy, before, prior, 4f, 1, 10f, 10, crit: true);
        Require(W.DamageEvidence(ItemID.CopperBroadsword, NPCID.Zombie) == crits && W.PushEvidence(ItemID.CopperBroadsword, NPCID.Zombie) == 0,
            "a crit teaches nothing, since the prior does not model its extra push");

        Vector2 atCap = W.PriorVelocityAfter(Vector2.Zero, 4f, -1, 10, enemy);
        W.ObserveHit(ItemID.WoodYoyo, enemy, atCap, atCap, 4f, -1, 10f, 10, crit: false);
        Require(W.PushEvidence(ItemID.WoodYoyo, NPCID.Zombie) == 0 && W.DamageEvidence(ItemID.WoodYoyo, NPCID.Zombie) == 1,
            "a heavy hit on an enemy already at the push's speed predicts no change, so it files damage and no push");
    }

    /// <summary>
    /// The owner-side rule is the game's list, and its direction is away from the player whichever side the shot came from.
    /// </summary>
    private static void TheOwnerSideRuleIsTheGamesList()
    {
        var swing = new Projectile();
        swing.SetDefaults(ProjectileID.NightsEdge);
        ContentSamples.ProjectilesByType[ProjectileID.NightsEdge] = swing;
        Require(swing.aiStyle is >= 188 and <= 191, $"premise: the Night's Edge projectile is a 1.4.4 swing projectile; aiStyle={swing.aiStyle}");
        Require(W.PushesAwayFromOwner(ProjectileID.NightsEdge) && W.PushesAwayFromOwner(697) && !W.PushesAwayFromOwner(ProjectileID.WoodenArrowFriendly) && !W.PushesAwayFromOwner(0),
            "the swing projectiles and the listed types push away from the owner, an arrow and a swing do not");
        Require(W.PriorDirection(true, flightX: 5f, targetCentreX: 100f, playerCentreX: 200f) == -1
            && W.PriorDirection(true, flightX: -5f, targetCentreX: 100f, playerCentreX: 200f) == -1,
            "an owner-side push points away from the player whichever way the flight goes");
        Require(W.PriorDirection(false, flightX: -5f, targetCentreX: 100f, playerCentreX: 200f) == -1
            && W.PriorDirection(false, flightX: 5f, targetCentreX: 100f, playerCentreX: 200f) == 1,
            "an ordinary push follows the flight");
    }

    /// <summary>
    /// The acceptance scene's first half. The player stands left of a zombie; a bow shot from the zombie's right pushes it
    /// left, into the player, and the same shot from the mirrored muzzle on the player's side pushes it right, away from him.
    /// The orb sits directly above the zombie so its own half of the charge cannot tell the two apart. The far-side shot must
    /// carry a charge and the near-side shot none, and the far-side shot's value must be lower by the charge.
    /// </summary>
    private static void AShotIsChargedForPushingAnEnemyIntoThePlayer()
    {
        var (companion, enemy, ctx) = Scene(1f, (GearSlot.FirstWeapon, ItemID.WoodenBow));
        Combat combat = companion.Combat;
        var bow = combat.Weapons[0];
        Vector2 far = enemy.Center + new Vector2(96f, 0f), near = enemy.Center - new Vector2(96f, 0f);
        Require(ctx.Player.Center.X < enemy.Center.X && near.X < enemy.Center.X, "premise: the player and the near muzzle are left of the zombie");
        Require(combat.ShotSolves(ctx, far, enemy) && combat.ShotSolves(ctx, near, enemy), "premise: both muzzles have a shot");

        float perHit = MathF.Max(1f, bow.DamagePerHit(ctx) - enemy.defense / 2f);
        float chargeFar = Forecasts.InducedDanger(ctx, bow, enemy, far, new Vector2(-1f, 0f), perHit);
        float chargeNear = Forecasts.InducedDanger(ctx, bow, enemy, near, new Vector2(1f, 0f), perHit);
        float valueFar = combat.BestShotValueFrom(ctx, far, enemy);
        float valueNear = combat.BestShotValueFrom(ctx, near, enemy);
        EmitLedgerRows.Detail(FormattableString.Invariant($"push charge: far {chargeFar:0.000} near {chargeNear:0.000}; value far {valueFar:0.000} near {valueNear:0.000}; settled push {W.SettledPush(bow.ItemType, enemy, bow.Knockback, perHit, -1):0.0}px"));
        Require(chargeFar >= .2f, $"a push into the player is charged for the danger it adds; charge={chargeFar}");
        Require(chargeNear == 0f, $"a push away from the player and not toward the orb adds no danger; charge={chargeNear}");
        Require(valueNear - valueFar >= .25f, $"the shot that pushes the zombie into the player is worth less; far={valueFar} near={valueNear}");

        // The orb's half is weighed at the muzzle the forecast is asked about, not at the live body hovering above: the same
        // leftward push from a stand left of the zombie carries the zombie toward that stand, and from a stand to its right
        // carries it away. The player's half is the same push both times, so any difference is the orb's.
        float intoStand = Forecasts.InducedDanger(ctx, bow, enemy, near, new Vector2(-1f, 0f), perHit);
        float awayFromStand = Forecasts.InducedDanger(ctx, bow, enemy, far, new Vector2(-1f, 0f), perHit);
        EmitLedgerRows.Detail(FormattableString.Invariant($"orb half at the stand: into the stand {intoStand:0.000} away from it {awayFromStand:0.000}"));
        Require(intoStand > awayFromStand + .05f,
            $"a push into the stand the orb would fire from is charged for the orb as well as the player; into={intoStand} away={awayFromStand}");

        // No push, no charge: the same far shot, on the same trajectory, from a weapon that has learned it pushes nothing.
        // Compared against itself rather than against the mirrored muzzle, because the aim sweep may land the two mirrored
        // arcs a tick apart, and a tick of timing is worth more than the tolerance an equality would need.
        W.AssumePush(bow.ItemType, enemy.type, 0f);
        Require(Forecasts.InducedDanger(ctx, bow, enemy, far, new Vector2(-1f, 0f), perHit) == 0f, "a weapon that pushes nothing is charged nothing");
        float valueFarNoPush = combat.BestShotValueFrom(ctx, far, enemy);
        Require(valueFarNoPush - valueFar >= .25f,
            $"the far shot's lost value is its push charge: without the push it is worth more; with={valueFar} without={valueFarNoPush}");
    }

    /// <summary>
    /// The acceptance scene's second half, restated when position selection stopped reading the chosen weapon's push and
    /// started pricing a stand by the best attack any weapon in hand makes from it — and restated again when the
    /// positioner stopped pricing stands at all. With the bow, the player's side of the zombie is worth more than its
    /// mirror on the far side, because the far stand's shot carries the push charge; the arsenal's per-stand value is
    /// the whole claim now, since no score multiplies it any more. The control compares the far stand with itself
    /// rather than with its mirror: with a learned push of nothing its value rises by the charge it no longer pays.
    /// Mirrored stands are never compared for equality, because the aim sweep can land mirrored arcs a tick apart and
    /// a tick moves a value past any sane tolerance.
    /// </summary>
    private static void AFiringStandIsWorthLessWhereItsShotPushesTheTargetIntoThePlayer()
    {
        var (companion, enemy, ctx) = Scene(1f, (GearSlot.FirstWeapon, ItemID.WoodenBow));
        Combat combat = companion.Combat;
        Vector2 near = enemy.Center - new Vector2(96f, 0f), far = enemy.Center + new Vector2(96f, 0f);

        float valueNear = combat.BestShotValueFrom(ctx, near, enemy);
        float valueFar = combat.BestShotValueFrom(ctx, far, enemy);
        EmitLedgerRows.Detail(FormattableString.Invariant($"stand value: near {valueNear:0.000} far {valueFar:0.000} ideal {combat.IdealShotValue(ctx, enemy):0.000}"));
        Require(valueFar > 0f, $"a stand whose only shot pushes into the player is still worth its shot, never vetoed; far={valueFar}");
        Require(valueNear > valueFar, $"the player's side of the zombie is worth more than its mirror; near={valueNear} far={valueFar}");

        W.AssumePush(ItemID.WoodenBow, enemy.type, 0f);
        float valueFarNoPush = combat.BestShotValueFrom(ctx, far, enemy);
        Require(valueFarNoPush > valueFar,
            $"the far stand's lost value is its push charge: without the push it is worth more; with={valueFar} without={valueFarNoPush}");
    }

    /// <summary>
    /// The same ten-damage wound on a zombie beside the player and on a harmless slime, both with life to spare: the dangerous
    /// wound is worth more, by the partial share of its prevented harm. A killing hit carrying a large push charge is worth
    /// exactly what the same kill without the charge is worth.
    /// </summary>
    private static void AWoundIsCreditedByDangerAndAKillIsNeverChargedForItsPush()
    {
        var dangerous = new E.Target(0, 45f, .8f, 20f);
        var harmless = new E.Target(1, 45f, .05f, 5f);
        var hitZombie = new E.Attack(0, 0, 60, 5, new[] { new E.Hit(0, 10f) });
        var hitSlime = new E.Attack(0, 1, 60, 5, new[] { new E.Hit(1, 10f) });
        var targets = new[] { dangerous, harmless };
        var zombie = E.Evaluate(hitZombie, new[] { hitZombie }, targets, 0, 60);
        var slime = E.Evaluate(hitSlime, new[] { hitSlime }, targets, 0, 60);
        Require(zombie.Kills == 0 && slime.Kills == 0 && zombie.Damage == slime.Damage, "premise: two equal wounds, no kill");
        float timing = 1f - 5f / 60f;
        float expectedGap = (20f * .8f - 5f * .05f) * (10f / 45f) * Weights.AttackPartialHarmShare * timing * Weights.AttackPreventedHarmWeight;
        Require(MathF.Abs((zombie.Value - slime.Value) - expectedGap) < 1e-3f && expectedGap > .5f,
            $"a wound is credited by the share of the enemy's life it removes times its danger; gap={zombie.Value - slime.Value} expected={expectedGap}");

        var kill = new E.Attack(0, 0, 60, 5, new[] { new E.Hit(0, 50f, InducedDanger: 100f) });
        var cleanKill = new E.Attack(0, 0, 60, 5, new[] { new E.Hit(0, 50f) });
        Require(E.Evaluate(kill, new[] { kill }, targets, 0, 60) == E.Evaluate(cleanKill, new[] { cleanKill }, targets, 0, 60),
            "a killing hit is never charged for its push");
        var wound = new E.Attack(0, 0, 60, 5, new[] { new E.Hit(0, 10f, InducedDanger: 1f) });
        Require(MathF.Abs(E.Evaluate(hitZombie, new[] { hitZombie }, targets, 0, 60).Value - E.Evaluate(wound, new[] { wound }, targets, 0, 60).Value
            - Weights.KnockbackInducedDangerWeight * timing) < 1e-3f,
            "a wounding hit is charged its push's danger at the charge weight, discounted like the harm it would have prevented");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
