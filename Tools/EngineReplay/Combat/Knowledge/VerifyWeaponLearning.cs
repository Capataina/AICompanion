extern alias live;

using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using AICompanion.Tools.Ledger;
using C = live::AICompanion.Companion.Brain.Activities.ActionContext;
using T = live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatRecord;
using W = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.WeaponEffects;
using L = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.AttackLearning;
using S = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.ShotOutcomes;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;
using CompanionGear = live::AICompanion.Companion.Inventory.CompanionGear;
using GearSlot = live::AICompanion.Companion.Inventory.GearSlot;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;
using Combat = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.CompanionCombat;
using Forecasts = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.ForecastUses;
using Fight = live::AICompanion.Companion.Brain.Activities.Combat.FightEnemies;
using Positioner = live::AICompanion.Companion.Brain.Infrastructure.Position.Positioner;
using PositionRequest = live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;
using Landed = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits;
using SpawnHook = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.ForgetReusedShotSlots;
using Credit = live::AICompanion.Companion.Progression.CreditKillsAndFights;
using Striker = live::AICompanion.Companion.Progression.Striker;
using Generations = live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources;
using OrbPace = live::AICompanion.Companion.Brain.Infrastructure.Movement.OrbPace;
using ItemWeapon = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.ItemWeapon;
using Reprice = live::AICompanion.Companion.Brain.Activities.Combat.Planning.ReevaluateAttackPlan;

/// <summary>
/// Weapon choice, target choice, firing position and aim are one decision valued by what the companion's own shots
/// achieved, learned online with the arsenal's arithmetic as the prior. The owner ruled on 15 September 2026 that decisions
/// evaluate every option and learn from what their own actions did; these rows hold that ruling against the hands.
///
/// Every pass line was declared before the first run. The learner rows feed synthetic outcome streams from a seeded
/// generator, because the global projectile hooks that deliver real outcomes do not run headless; the arsenal rows put real
/// items in the gear and ask the real arsenal, with the learner's state planted or taught. Enemy AI does not run and no
/// projectile is advanced, so what this establishes is the learning arithmetic, the attribution and the decisions they
/// feed, not a fight.
/// </summary>
internal static class VerifyWeaponLearning
{
    private const int FloorY = 60;
    private const int AirRow = 50;

    /// <summary>
    /// The volley cone the synthetic contexts below were fired under. The regression scales an aim offset by the
    /// cone the arsenal hands it — the volley's learned spread, not a constant — and these rows teach no volley,
    /// so they declare the cone instead; near the old authored cone's scale, so the declared margins still read.
    /// </summary>
    private const float AssumedCone = 0.1f;

    public static int Run()
    {
        AnAimCoefficientIsLearnedFromWhetherAimMattered();
        TheHandsFireTheSimulatorsBestAim();
        ADebuffThenBurstPairIsOpenedWithTheDebuff();
        AChildProjectileBelongsToTheShotThatFiredItsParent();
        ASwingIsTaughtTheMomentItLands();
        UnderRealDangerTheChoiceIsThePosteriorMean();
        AStandIsPricedByEveryHandedWeapon();
        TheLearnersCostPerDecisionIsMeasured();
        Console.WriteLine("weapon learning: aim is learned from whether it mattered, a debuff pair opens with the debuff, a child projectile is its parent's shot, a swing teaches at once, danger holds the choice to the mean, and a stand is priced by every weapon in hand");
        return 0;
    }

    private sealed record Setting(CompanionNPC Companion, NPC Enemy, C Ctx, List<T> Threats);

    /// <summary>
    /// A floor across the scene, the player left of a zombie, the gear as given, and the companion placed relative to the
    /// zombie. <paramref name="floating"/> stands the zombie in open air above the floor, so a shot aimed off it has room to
    /// fly past without meeting the ground.
    /// </summary>
    private static Setting Scene(float enemyResist, Vector2 companionOffset, bool floating, params (GearSlot Slot, int Item)[] gear)
    {
        for (int x = 10; x < 90; x++)
        {
            for (int y = 30; y < FloorY; y++) { Tile air = Main.tile[x, y]; air.HasTile = false; air.LiquidAmount = 0; }
            for (int y = FloorY; y <= FloorY + 2; y++) { Tile floor = Main.tile[x, y]; floor.HasTile = true; floor.TileType = 1; floor.Slope = 0; floor.IsHalfBlock = false; }
        }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
        var companion = VerifyCompanionLifecycle.Create();
        W.Reset();
        S.Clear();
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

        var enemy = Zombie(25, new Vector2(36 * 16f, (floating ? AirRow : FloorY) * 16f), enemyResist);

        companion.NPC.Center = enemy.Center + companionOffset;
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.life = companion.NPC.lifeMax;

        var threats = new List<T> { Threat(enemy, companion, player) };
        Restate(companion, player, threats);
        return new Setting(companion, enemy, new C(companion, companion.Brain.Senses), threats);
    }

    private static NPC Zombie(int slot, Vector2 bottom, float resist)
    {
        var npc = new NPC();
        npc.SetDefaults(NPCID.Zombie);
        npc.whoAmI = slot;
        npc.active = true;
        npc.velocity = Vector2.Zero;
        npc.knockBackResist = resist;
        npc.Bottom = bottom;
        Main.npc[slot] = npc;
        return npc;
    }

    private static T Threat(NPC enemy, CompanionNPC companion, Player player)
    {
        float toPlayer = Vector2.Distance(enemy.Center, player.Center);
        float toCompanion = Vector2.Distance(enemy.Center, companion.NPC.Center);
        return new T
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
        };
    }

    /// <summary>One senses update, then the scene's own threat records in place of whatever the sense built, so the tick advances and the threats stay what the row declared.</summary>
    private static void Restate(CompanionNPC companion, Player player, List<T> threats)
    {
        companion.Brain.Senses.Update(companion.NPC, player);
        var live = companion.Brain.Senses.Threats.Threats;
        live.Clear();
        live.AddRange(threats);
    }

    /// <summary>
    /// A weapon whose outcome ignores its aim and a weapon whose outcome falls with it, fed the same forty contexts from one
    /// seeded generator: aim shares of none, half and the widest candidate in turn, the other inputs random. The first
    /// lands its forecast whatever the aim, give or take a small noise; the second lands it on the intercept, half of it at
    /// half the offset and none at the widest. Declared before the run: after forty shots the first's aim coefficient is
    /// within a fifth of zero and the second's is below minus six tenths — the true slope is minus one on this scale, and
    /// the prior's pull toward zero is what the margin allows for.
    /// </summary>
    private static void AnAimCoefficientIsLearnedFromWhetherAimMattered()
    {
        L.Reset();
        var random = new Random(7);
        const int Shots = 40;
        float widest = AssumedCone;
        for (int i = 0; i < Shots; i++)
        {
            float share = (i % 3) / 2f;
            float[] x = L.Context(100f + 500f * (float)random.NextDouble(), 800f, share * widest, widest, 4f * (float)random.NextDouble(), 0,
                3f * (float)random.NextDouble(), 6f, debuffedByOther: false);
            float noise = .15f * (2f * (float)random.NextDouble() - 1f);
            L.Observe(ItemID.MagicMissile, NPCID.Zombie, x, 1f + noise);
            L.Observe(ItemID.WoodenBow, NPCID.Zombie, x, MathF.Max(0f, 1f - share + noise));
        }
        float homing = L.Coefficient(ItemID.MagicMissile, L.AimOffset);
        float straight = L.Coefficient(ItemID.WoodenBow, L.AimOffset);
        EmitLedgerRows.Detail(FormattableString.Invariant($"aim coefficient after {Shots} shots: aim-blind {homing:0.000}, aim-sensitive {straight:0.000}"));
        Require(MathF.Abs(homing) < .2f, $"a weapon whose outcome ignores its aim learns an aim coefficient near zero; coefficient={homing}");
        Require(straight < -.6f, $"a weapon whose outcome falls with its aim learns a clearly negative aim coefficient; coefficient={straight}");

        // Every draw a decision reads is kept for the decision, including the bias of an enemy type the weapon has never
        // struck: a fresh number per ask made the aim candidates of one forecast compete against different noise.
        float[] probe = L.Context(300f, 800f, 0f, AssumedCone, 0f, 0, 0f, 6f, debuffedByOther: false);
        float firstAsk = L.Factor(ItemID.WoodenBow, NPCID.BlueSlime, probe, explore: true, tick: 5);
        float secondAsk = L.Factor(ItemID.WoodenBow, NPCID.BlueSlime, probe, explore: true, tick: 5);
        Require(firstAsk == secondAsk, $"two asks in one tick about an enemy type never struck read one draw; first={firstAsk} second={secondAsk}");
    }

    /// <summary>
    /// The hands fire the aim the simulator prices best, and the learner no longer moves the shot. A posterior planted
    /// with a positive aim coefficient — aim off and be rewarded — still leaves on the intercept in the open, because the
    /// winner among the solver's aims is the most simulated damage on the target, never the largest learned factor; the
    /// learner scales the value and teaches the outcome, and the aim it is taught against is the one that left. The bow's
    /// own aim noise is set to nothing for the row, because the noise is wider than the gap between the intercept and the
    /// solver's spread and the first version of this row passed both arms at the same noisy angle. The shot opens one
    /// outcome window. Behind a wall nothing is fired at all: no aim's use lands, so there is no proved shot to take.
    /// Aims compete on simulated damage only — the simulator is authoritative for geometry — while the learner
    /// prices weapons, targets and stands; the plan searches each pair at its simulated-best aim for the same
    /// reason the old choice did, and this row holds that ruling against the new hands.
    /// </summary>
    private static void TheHandsFireTheSimulatorsBestAim()
    {
        // The orb stands to the zombie's right: five hundred pixels to its left is inside the five-tile margin the
        // trace refuses at the world's edge, which read as a bow with no arc rather than as a scene built too close
        // to it.
        var scene = Scene(0f, new Vector2(500f, 0f), floating: true, (GearSlot.FirstWeapon, ItemID.WoodenBow));
        // The zombie hangs in the air and stays there: with gravity on, its forecast falls to the floor and the
        // intercept honestly meets it there, which is a different scene than the one either arm describes.
        scene.Enemy.noGravity = true;
        L.Reset();
        var mean = new float[L.FeatureCount];
        mean[L.AimOffset] = .8f;
        L.Assume(ItemID.WoodenBow, mean, 1e-6f);
        Combat combat = scene.Companion.Combat;
        Require(combat.Weapons.Count == 1, "premise: the bow is the one weapon in hand");
        combat.Weapons[0].AimNoise = 0f;
        var use = CombatFixture.FireOnce(scene.Companion, scene.Ctx);
        Require(use.Fired, $"premise: the bow fires; outcome={combat.LastFireOutcome}");
        float offset = MathF.Abs(combat.LastAimOffset);
        EmitLedgerRows.Detail(FormattableString.Invariant($"the planted aim reward leaves {MathHelper.ToDegrees(offset):0.000} deg off the intercept, and the shot opened {S.OpenCount} outcome window"));
        Require(offset < 1e-3f, $"a learner that says aiming off pays must not move the shot; offset={offset}");
        Require(S.OpenCount == 1, $"the shot opened one outcome window; open={S.OpenCount}");

        // The same scene with a wall between the muzzle and the zombie. The arrow dies on contact, so every aim's
        // use ends on the wall and none lands: the hands hold, report no arc, and open no window.
        var walled = Scene(0f, new Vector2(500f, 0f), floating: true, (GearSlot.FirstWeapon, ItemID.WoodenBow));
        walled.Enemy.noGravity = true;
        int wallX = (int)(walled.Enemy.Center.X / 16f) + 8;
        // From the top of the cleared air down into the floor: a short wall is a lob over, and the solver is
        // allowed its lob, so the wall has to outstand every launch angle the intercept search may propose —
        // and a gap at its foot is a thread under, which the first version of this arm fired through.
        for (int y = AirRow - 20; y <= AirRow + 10; y++)
        {
            Tile wall = Main.tile[wallX, y];
            wall.HasTile = true;
            wall.TileType = 1;
            wall.Slope = 0;
            wall.IsHalfBlock = false;
        }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        L.Reset();
        Combat walledCombat = walled.Companion.Combat;
        walledCombat.Weapons[0].AimNoise = 0f;
        var held = CombatFixture.FireOnce(walled.Companion, walled.Ctx);
        Require(!held.Fired, $"behind a wall the bow must hold; outcome={walledCombat.LastFireOutcome}");
        Require(walledCombat.LastFireOutcome == "no-use-worth-firing", $"holding reports no use worth firing; outcome={walledCombat.LastFireOutcome}");
        Require(S.OpenCount == 0, $"the held shot opened no outcome window; open={S.OpenCount}");
    }

    /// <summary>
    /// A debuff-then-burst pair. The burst weapon lands six tenths of its forecast on a plain target and one and six tenths on
    /// one the other weapon has debuffed; declared before the run, its learned factor against a debuffed zombie exceeds its
    /// factor against the same zombie undebuffed by at least a half. Then the arsenal holds both: a weaker bow in the second
    /// slot, whose hits have been seen to leave zombies debuffed, and the burst bow in the first. With the debuff unlearned the
    /// burst bow opens, which is the control; with it learned, the weaker bow opens, because the continuation after it prices
    /// the burst bow's hits against a debuffed zombie. The zombie is given life enough that the continuation, not a kill,
    /// decides, and resists every push so no charge moves either value.
    /// </summary>
    private static void ADebuffThenBurstPairIsOpenedWithTheDebuff()
    {
        L.Reset();
        TeachBurst(ItemID.PlatinumBow);
        float[] plain = L.Context(300f, 800f, 0f, AssumedCone, 0f, 0, 0f, 6f, debuffedByOther: false);
        float[] debuffed = L.Context(300f, 800f, 0f, AssumedCone, 0f, 0, 0f, 6f, debuffedByOther: true);
        float factorPlain = L.Factor(ItemID.PlatinumBow, NPCID.Zombie, plain, explore: false, 0);
        float factorDebuffed = L.Factor(ItemID.PlatinumBow, NPCID.Zombie, debuffed, explore: false, 0);
        EmitLedgerRows.Detail(FormattableString.Invariant($"burst factor: plain {factorPlain:0.000}, against the other weapon's debuff {factorDebuffed:0.000}"));
        Require(factorDebuffed > factorPlain + .5f, $"the burst weapon's value against a debuffed target exceeds its value against the same target undebuffed; plain={factorPlain} debuffed={factorDebuffed}");

        int Opening(bool debuffLearned)
        {
            var scene = Scene(0f, new Vector2(-300f, 0f), floating: true, (GearSlot.FirstWeapon, ItemID.PlatinumBow), (GearSlot.SecondWeapon, ItemID.WoodenBow));
            // The planted threat carries reachability but no urgency, so the zombie threatens nobody and
            // threat and prevention are both zero: damage alone decides, and damage is a rate, so the
            // boosted three-use plan ties the plain two-use one and the earlier first hit wins. A sensed
            // threat always carries its urgency, so the record is restated with the sense's own rule and
            // the bigger wounds remove more of a real threat. The draws do not move — same seed, same
            // calls — only the danger the plans remove them against.
            T planted = scene.Threats[0];
            planted.Urgency = live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatUrgency.ToPlayer(
                planted.EffectiveDamageToPlayer, scene.Ctx.Player.statLife, planted.IsBoss,
                planted.TicksToPlayer, planted.Shoots, planted.HasSightOnPlayer);
            planted.UrgencyToCompanion = live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatUrgency.ToCompanion(
                planted.EffectiveDamageToCompanion, scene.Ctx.Npc.life, planted.IsBoss,
                planted.TicksToCompanion, planted.Shoots, planted.HasSightOnCompanion);
            scene.Enemy.lifeMax = scene.Enemy.life = 500;
            L.Reset();
            TeachBurst(ItemID.PlatinumBow);
            if (debuffLearned)
                for (int i = 0; i < 8; i++) L.ObserveDebuff(ItemID.WoodenBow, NPCID.Zombie, applied: true, 600);
            var plan = CombatFixture.Search(scene.Companion, scene.Ctx);
            var weapon = CombatFixture.OpeningWeapon(scene.Companion, plan);
            EmitLedgerRows.Detail(FormattableString.Invariant($"debuff {(debuffLearned ? "learned" : "unlearned")}: opens with {weapon?.Name}; plan {(plan == null ? "none" : FormattableString.Invariant($"weighted {plan.Weighted:0.0}"))}"));
            Require(weapon != null, "premise: a weapon is chosen");
            return weapon!.ItemType;
        }
        Require(Opening(debuffLearned: false) == ItemID.PlatinumBow, "premise: with no debuff learned the burst bow opens");
        Require(Opening(debuffLearned: true) == ItemID.WoodenBow, "with the weaker bow's debuff learned, the plan opens with the debuff");
    }

    /// <summary>Forty outcomes for the burst weapon, debuffed and plain in turn, from a seeded generator.</summary>
    private static void TeachBurst(int item)
    {
        var random = new Random(11);
        for (int i = 0; i < 40; i++)
        {
            bool debuffed = i % 2 == 0;
            float[] x = L.Context(150f + 400f * (float)random.NextDouble(), 800f, 0f, AssumedCone, (float)random.NextDouble(), 0,
                (float)random.NextDouble(), 6f, debuffed);
            L.Observe(item, NPCID.Zombie, x, (debuffed ? 1.6f : .6f) + .05f * (2f * (float)random.NextDouble() - 1f));
        }
    }

    /// <summary>
    /// A shot's outcome window holds its projectile and every projectile descended from it. Tested on the attribution
    /// function directly with constructed parent sources, because the global spawn hook does not run headless: a child whose
    /// parent is the shot's projectile joins the window, and so does that child's child; a projectile spawned by an NPC, or
    /// by a projectile nobody registered, joins nothing; and a slot reused by an unrelated spawn is forgotten first. Damage
    /// the grandchild lands counts toward the shot, the window stays open until the last descendant dies, and the buff a hit
    /// added is read off the body, marked as the bow's for the other weapon's context, and taught to the debuff rate.
    /// </summary>
    private static void AChildProjectileBelongsToTheShotThatFiredItsParent()
    {
        L.Reset();
        S.Clear();
        var zombie = Zombie(25, new Vector2(36 * 16f, FloorY * 16f), .5f);
        Projectile Slot(int index)
        {
            var projectile = new Projectile { whoAmI = index, active = true };
            Main.projectile[index] = projectile;
            return projectile;
        }
        Projectile parent = Slot(5), child = Slot(9), grandchild = Slot(11), stranger = Slot(20);
        float[] x = L.Context(300f, 800f, 0f, AssumedCone, 0f, 0, 0f, 6f, debuffedByOther: false);
        int window = S.Open(ItemID.WoodenBow, zombie, x, predictedDamage: 10f, predictedStruck: 1, predictedCharge: 0f, useTicks: 60, impactTicks: 20, now: 0);
        S.AddSlot(window, parent.whoAmI);

        Require(S.AttributeSpawn(child.whoAmI, new EntitySource_Parent(parent)) == window, "a projectile spawned by the shot's projectile joins the shot's window");
        Require(S.AttributeSpawn(grandchild.whoAmI, new EntitySource_Parent(child)) == window, "a projectile spawned by that child joins the same window");
        Require(S.AttributeSpawn(12, new EntitySource_Parent(zombie)) == null && S.WindowOf(12) == null, "a projectile an NPC spawned joins nothing");
        Require(S.AttributeSpawn(13, new EntitySource_Parent(stranger)) == null && S.WindowOf(13) == null, "a projectile spawned by an unregistered projectile joins nothing");

        S.BeforeStrike(zombie, parent.whoAmI);
        zombie.buffType[0] = BuffID.OnFire;
        zombie.buffTime[0] = 300;
        S.Landed(zombie, parent.whoAmI, 3);
        S.Landed(zombie, grandchild.whoAmI, 7);
        Require(S.DebuffedByOther(zombie, ItemID.PlatinumBow) && !S.DebuffedByOther(zombie, ItemID.WoodenBow),
            "the buff the bow's hit added counts as debuffed for the other weapon and not for the bow itself");

        Require(S.AttributeSpawn(child.whoAmI, null) == null && S.WindowOf(child.whoAmI) == null && S.WindowOf(parent.whoAmI) == window,
            "a slot reused by an unrelated spawn is forgotten, and the window keeps its other projectiles");
        S.Retire(parent.whoAmI);
        Require(S.OpenCount == 1, $"the window stays open while a descendant lives; open={S.OpenCount}");
        S.Retire(grandchild.whoAmI);
        Require(S.OpenCount == 0 && S.LastClosed is { } outcome, "the window closes when its last projectile dies");
        outcome = S.LastClosed!.Value;
        EmitLedgerRows.Detail(FormattableString.Invariant($"closed shot: dealt {outcome.Dealt:0}, struck {outcome.Struck}, debuffs {outcome.DebuffsApplied}, ratio {outcome.Ratio:0.000}, reward {outcome.Reward:0.00}"));
        Require(outcome.Dealt == 10f && outcome.Struck == 1 && outcome.DebuffsApplied == 1,
            $"the grandchild's damage is the shot's, one body was struck and one debuff applied; dealt={outcome.Dealt} struck={outcome.Struck} debuffs={outcome.DebuffsApplied}");
        Require(MathF.Abs(outcome.Ratio - 1f) < 1e-4f, $"a shot that landed exactly its forecast teaches a ratio of one; ratio={outcome.Ratio}");
        Require(L.Evidence(ItemID.WoodenBow) == 1 && MathF.Abs(L.DebuffChance(ItemID.WoodenBow, NPCID.Zombie) - .5f) < 1e-4f && L.DebuffTicks(ItemID.WoodenBow, NPCID.Zombie) == 300,
            $"the closed shot taught the learner once and the debuff rate one application; evidence={L.Evidence(ItemID.WoodenBow)} chance={L.DebuffChance(ItemID.WoodenBow, NPCID.Zombie)} ticks={L.DebuffTicks(ItemID.WoodenBow, NPCID.Zombie)}");
    }

    /// <summary>A sword swing through the arsenal strikes the zombie beside the orb, and its window opens and closes around the strike, teaching the learner once.</summary>
    private static void ASwingIsTaughtTheMomentItLands()
    {
        var scene = Scene(.5f, new Vector2(-22f, -4f), floating: false, (GearSlot.FirstWeapon, ItemID.CopperBroadsword));
        L.Reset();
        Combat combat = scene.Companion.Combat;
        var swung = CombatFixture.FireOnce(scene.Companion, scene.Ctx);
        Require(swung.Fired, $"premise: the sword swings; outcome={combat.LastFireOutcome}");
        Require(S.OpenCount == 0 && S.LastClosed is { Struck: 1 } outcome && outcome.Dealt > 0f,
            $"the swing's window closed on its strike; open={S.OpenCount} closed={S.LastClosed}");
        Require(L.Evidence(ItemID.CopperBroadsword) == 1, $"the swing taught the learner once; evidence={L.Evidence(ItemID.CopperBroadsword)}");
    }

    /// <summary>
    /// With something dangerous on a body the choice is the posterior mean's. The platinum bow's posterior is planted with a
    /// mean that ranks it below the wooden bow and a wide variance, the wooden bow's with a mean of the prior and almost no
    /// variance. At no danger, some tick's draw puts the platinum bow above the wooden one — the premise that exploration is
    /// live in this scene — and a second search on that tick repeats the choice. On the same tick with the zombie's urgency
    /// above the declared ceiling the choice is the wooden bow, which is the mean's, and the forecast gate says it did not
    /// explore. Ticks are the sampler's seed now; a rolling seed the audit could not reproduce was the sampler before.
    /// </summary>
    private static void UnderRealDangerTheChoiceIsThePosteriorMean()
    {
        var scene = Scene(0f, new Vector2(-300f, 0f), floating: true, (GearSlot.FirstWeapon, ItemID.WoodenBow), (GearSlot.SecondWeapon, ItemID.PlatinumBow));
        L.Reset();
        var low = new float[L.FeatureCount];
        low[L.Bias] = -.7f;
        L.Assume(ItemID.PlatinumBow, low, 1f);
        L.Assume(ItemID.WoodenBow, new float[L.FeatureCount], 1e-6f);
        Player player = scene.Ctx.Player;

        int ChoiceAt(float urgency)
        {
            scene.Threats[0].Urgency = urgency;
            var plan = CombatFixture.Search(scene.Companion, scene.Ctx);
            return CombatFixture.OpeningWeapon(scene.Companion, plan)!.ItemType;
        }

        Require(Weights.WeaponExploreDangerCeiling < .9f, "premise: the danger this row uses is above the ceiling");
        int found = -1;
        for (int attempt = 0; attempt < 200 && found < 0; attempt++)
        {
            for (int i = 0; i < 13; i++) Restate(scene.Companion, player, scene.Threats);
            if (ChoiceAt(0f) == ItemID.PlatinumBow) found = scene.Ctx.Senses.Tick;
        }
        Require(found >= 0, "premise: at no danger some draw explores to the weapon the mean ranks lower");
        Require(ChoiceAt(0f) == ItemID.PlatinumBow && Forecasts.Explore(scene.Ctx), "premise: that draw repeats and the forecast explored");
        int underDanger = ChoiceAt(.9f);
        EmitLedgerRows.Detail(FormattableString.Invariant($"danger gate: tick {found} explores to the platinum bow at no danger; under danger it chooses item {underDanger}"));
        Require(underDanger == ItemID.WoodenBow && !Forecasts.Explore(scene.Ctx),
            $"under danger above the ceiling the choice is the posterior mean's and nothing is explored; chose={underDanger} explored={Forecasts.Explore(scene.Ctx)}");
    }

    /// <summary>
    /// AIC-395. A firing stand is priced by the best attack any weapon in hand could make from it, not by the weapon chosen
    /// last. The player stands left of a zombie; the strong bow pushes along its flight and the weak bow has learned it pushes
    /// nothing, so from the stand on the player's side the strong bow's shot is safe, and from the far stand only the weak
    /// bow's is. On paper: the player's side is worth the strong shot, the far side the larger of the weak shot and the strong
    /// shot less its push charge, so the player's side wins whenever the charge is positive and the strong shot outvalues the
    /// weak. The plan against a slime the strong bow barely hurts opens with the weak bow, which is the premise that
    /// made this red on the proxy: the stand value read that weak bow's push of nothing and priced both stands identically.
    /// </summary>
    private static void AStandIsPricedByEveryHandedWeapon()
    {
        var scene = Scene(1f, new Vector2(96f, -8f), floating: false, (GearSlot.FirstWeapon, ItemID.WoodenBow), (GearSlot.SecondWeapon, ItemID.PlatinumBow));
        L.Reset();
        W.AssumePush(ItemID.WoodenBow, scene.Enemy.type, 0f);
        var senses = scene.Companion.Brain.Senses;
        Combat combat = scene.Companion.Combat;
        NPC enemy = scene.Enemy;
        Vector2 playerSide = enemy.Center - new Vector2(96f, 0f), farSide = enemy.Center + new Vector2(96f, 0f);

        var slime = new NPC();
        slime.SetDefaults(NPCID.BlueSlime);
        slime.whoAmI = 26;
        slime.active = true;
        slime.Bottom = new Vector2(enemy.Center.X, 42 * 16f);
        Main.npc[26] = slime;
        senses.Threats.Threats.Add(new T
        {
            Npc = slime, Class = live::AICompanion.Companion.Brain.Infrastructure.Observation.MovementClass.Walker,
            ExpectedDamage = slime.damage, ObservedSpeed = 1f,
            DistanceToCompanion = Vector2.Distance(slime.Center, scene.Companion.NPC.Center),
            TicksToCompanion = Vector2.Distance(slime.Center, scene.Companion.NPC.Center),
        });
        for (int i = 0; i < W.SamplesKept; i++)
            W.ObserveHit(ItemID.PlatinumBow, slime, Vector2.Zero, Vector2.Zero, 0f, 1, 40f, 1, crit: false);
        var slimeOnly = new List<T> { senses.Threats.Threats[senses.Threats.Threats.Count - 1] };
        Restate(scene.Companion, scene.Ctx.Player, slimeOnly);
        var slimePlan = CombatFixture.Search(scene.Companion, scene.Ctx);
        var slimeOpener = CombatFixture.OpeningWeapon(scene.Companion, slimePlan);
        Restate(scene.Companion, scene.Ctx.Player, scene.Threats);
        Require(slimeOpener?.ItemType == ItemID.WoodenBow, $"premise: the plan against the slime opens with the weak bow; chosen={slimeOpener?.Name}");

        float valuePlayerSide = combat.BestShotValueFrom(scene.Ctx, playerSide, enemy);
        float valueFarSide = combat.BestShotValueFrom(scene.Ctx, farSide, enemy);
        EmitLedgerRows.Detail(FormattableString.Invariant($"stand pricing: value player side {valuePlayerSide:0.000} far {valueFarSide:0.000}"));
        // The positioner no longer multiplies a stand's value into a score, so the arsenal's per-stand
        // value is the ranking now: the player's side is worth the strong bow's safe shot, more than
        // the far side's best, which is the larger of the weak shot and the strong shot less its push
        // charge. A planted proxy changes the value, not a multiplier, so the claim moved with it.
        Require(valuePlayerSide > valueFarSide, $"the player side's stand is worth the strong bow's safe shot, more than the far side's best; player side={valuePlayerSide} far={valueFarSide}");
    }

    /// <summary>
    /// The learner's cost per decision, where a decision is one full plan search: eight zombies across the floor, both
    /// bows, the proposal targets the search bounds itself to. Three arms back to back in one process — untrained, trained on forty
    /// outcomes per weapon with a debuff rate, untrained again — so neither JIT nor the machine's load is credited to the
    /// learner. The senses are advanced between decisions outside the stopwatch so every search runs on a fresh tick.
    /// Declared before the run: the trained arm's mean is within a millisecond of the slower untrained arm's.
    /// </summary>
    private static void TheLearnersCostPerDecisionIsMeasured()
    {
        var scene = Scene(0f, new Vector2(0f, -160f), floating: false, (GearSlot.FirstWeapon, ItemID.WoodenBow), (GearSlot.SecondWeapon, ItemID.PlatinumBow));
        Player player = scene.Ctx.Player;
        int[] columns = { 20, 26, 31, 41, 46, 52, 58 };
        for (int i = 0; i < columns.Length; i++)
            scene.Threats.Add(Threat(Zombie(40 + i, new Vector2(columns[i] * 16f, FloorY * 16f), 0f), scene.Companion, player));
        for (int i = 0; i < scene.Threats.Count; i++) scene.Threats[i].Urgency = .05f * i;
        Combat combat = scene.Companion.Combat;

        (double Mean, double P95, NPC? Target) Arm(bool trained)
        {
            L.Reset();
            if (trained)
            {
                TeachBurst(ItemID.PlatinumBow);
                TeachBurst(ItemID.WoodenBow);
                for (int i = 0; i < 8; i++) L.ObserveDebuff(ItemID.WoodenBow, NPCID.Zombie, applied: true, 600);
            }
            var samples = new List<double>();
            NPC? target = null;
            var stopwatch = new Stopwatch();
            for (int decision = 0; decision < 70; decision++)
            {
                for (int i = 0; i < 16; i++) Restate(scene.Companion, player, scene.Threats);
                stopwatch.Restart();
                var plan = CombatFixture.Search(scene.Companion, scene.Ctx);
                target = plan == null || plan.PrimaryTarget < 0 ? null : Main.npc[plan.PrimaryTarget];
                stopwatch.Stop();
                if (decision >= 10) samples.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
            samples.Sort();
            return (samples.Average(), samples[(int)(samples.Count * .95)], target);
        }

        var coldUntrained = Arm(trained: false);
        var trainedArm = Arm(trained: true);
        var warmUntrained = Arm(trained: false);
        Require(trainedArm.Target != null && warmUntrained.Target != null, "premise: every arm ranks a target");
        var factorClock = Stopwatch.StartNew();
        float[] x = L.Context(300f, 800f, 0f, AssumedCone, 1f, 1, 1f, 6f, debuffedByOther: false);
        TeachBurst(ItemID.PlatinumBow);
        for (int i = 0; i < 10000; i++) L.Factor(ItemID.PlatinumBow, NPCID.Zombie, x, explore: true, i);
        factorClock.Stop();
        double untrained = Math.Max(coldUntrained.Mean, warmUntrained.Mean);
        EmitLedgerRows.Detail(FormattableString.Invariant($"cost per ranking of 8 hostiles, 2 weapons, ms (lower is better): untrained mean {coldUntrained.Mean:0.000} then {warmUntrained.Mean:0.000} (p95 {warmUntrained.P95:0.000}); trained mean {trainedArm.Mean:0.000} (p95 {trainedArm.P95:0.000}); one sampled factor with a fresh draw {factorClock.Elapsed.TotalMilliseconds * 1000.0 / 10000:0.0} us"));
        Require(trainedArm.Mean - untrained < 1.0, $"the learner adds under a millisecond to a full ranking; trained={trainedArm.Mean} untrained={untrained}");
    }

    /// <summary>
    /// A weapon's misses against one enemy type stay with that type. The bow is taught three outcomes against demon eyes in a
    /// flier's context — at range, fast across the line, with the orb moving — and then asked about a zombie it has never
    /// struck, floating three hundred pixels off. Two arms from one seed: in the first the eye shots all missed, in the control
    /// they all landed exactly their forecast, so both arms hold the same amount of evidence and draw the same random numbers,
    /// and only what the evidence said differs. Declared before the run: the misses were learned about the eye (its mean factor
    /// below three quarters in the miss arm); the zombie's mean factor in the miss arm is within five hundredths of the
    /// control's; over two hundred decisions the miss arm finds no target no more often than the control, and its mean attack
    /// value is at least ninety-five percent of the control's. The first review of the learner measured the zombie at 0.139
    /// and no target on 53 of 200 decisions after three eye misses, because the weapon model took most of every miss and every
    /// enemy type reads the weapon model.
    /// </summary>
    public static int MissesAgainstOneEnemyTypeStayWithThatType()
    {
        (float Zombie, float Eye, int NoTarget, float MeanValue) Arm(float eyeRatio)
        {
            var scene = Scene(0f, new Vector2(-300f, 0f), floating: true, (GearSlot.FirstWeapon, ItemID.WoodenBow));
            L.Reset();
            Combat combat = scene.Companion.Combat;
            Require(combat.Weapons.Count == 1, "premise: the bow is the one weapon in hand");
            float reach = combat.Weapons[0].Reach;
            float[] eyeContext = L.Context(260f, reach, 0f, AssumedCone, 5f, 0, 2f, OrbPace.MaxSpeed, debuffedByOther: false);
            for (int i = 0; i < 3; i++) L.Observe(ItemID.WoodenBow, NPCID.DemonEye, eyeContext, eyeRatio);
            float[] zombieContext = L.Context(300f, reach, 0f, AssumedCone, 0f, 0, 0f, OrbPace.MaxSpeed, debuffedByOther: false);
            float zombie = L.Factor(ItemID.WoodenBow, NPCID.Zombie, zombieContext, explore: false, 0);
            float eye = L.Factor(ItemID.WoodenBow, NPCID.DemonEye, eyeContext, explore: false, 0);
            int noTarget = 0;
            float value = 0f;
            const int Decisions = 200;
            for (int decision = 0; decision < Decisions; decision++)
            {
                for (int i = 0; i < 16; i++) Restate(scene.Companion, scene.Ctx.Player, scene.Threats);
                var plan = CombatFixture.Search(scene.Companion, scene.Ctx);
                if (plan == null) noTarget++;
                value += plan?.Weighted ?? 0f;
            }
            return (zombie, eye, noTarget, value / Decisions);
        }

        var missed = Arm(0f);
        var landed = Arm(1f);
        EmitLedgerRows.Detail(FormattableString.Invariant($"after three demon-eye outcomes, zombie mean factor (higher is better): misses {missed.Zombie:0.000}, control {landed.Zombie:0.000}; eye {missed.Eye:0.000} / {landed.Eye:0.000}; no target on {missed.NoTarget} / {landed.NoTarget} of 200 decisions (lower is better); mean attack value {missed.MeanValue:0.00} / {landed.MeanValue:0.00}"));
        Require(missed.Eye < .75f, $"premise: the misses were learned about the demon eye; eye factor={missed.Eye}");
        // Two-sided: a first version required only that the zombie was not cut, and a mutation that spread the eye misses into
        // the zombie as a raised factor of 1.455 passed it. Evidence about one type moves another type neither way.
        Require(MathF.Abs(missed.Zombie - landed.Zombie) <= .05f, $"demon-eye outcomes do not move the bow's value against a zombie it never shot; zombie after misses={missed.Zombie} control={landed.Zombie}");
        Require(missed.NoTarget <= landed.NoTarget, $"misses against demon eyes do not make the arsenal find no zombie to shoot; no target {missed.NoTarget} against control {landed.NoTarget} of 200");
        Require(missed.MeanValue >= .95f * landed.MeanValue, $"misses against demon eyes do not cut the zombie shot's value; mean value {missed.MeanValue} against control {landed.MeanValue}");
        return 0;
    }

    /// <summary>
    /// A swing records a kill the way a projectile does: as the strike's damage, not the life that happened to be left. Two
    /// swings of one copper broadsword at identical blue slimes beside the orb, one with its ordinary life and one with two
    /// life left. A projectile's hit reports the strike's damage whether it kills or not, and the forecast it is divided by is the
    /// same uncapped damage, so a finishing arrow teaches a ratio of one. Declared before the run: the killing swing's recorded
    /// damage and ratio equal the wounding swing's, and the wounding swing's ratio is one within a thousandth, which is what
    /// says both are the strike rather than both being wrong together. The swing already met this when the row was written:
    /// the game takes a strike's whole damage off the body's life without stopping at zero, so the life a killing swing took
    /// is the strike. The swing's own comment had said a killing strike was capped at the life left, a review took that as a
    /// defect, and a recording capped that way is the rule this row turns red.
    /// </summary>
    public static int ASwingKillIsRecordedAsTheStrike()
    {
        (float Dealt, float Ratio, bool Died) Swing(int lifeLeft)
        {
            var scene = Scene(.5f, new Vector2(-22f, -4f), floating: false, (GearSlot.FirstWeapon, ItemID.CopperBroadsword));
            L.Reset();
            // A slime rather than the scene's zombie: a zombie's death effect spawns gore, which has no graphics state to
            // spawn into headless, and a slime's is dust, which the dedicated-server flag already skips.
            Vector2 bottom = scene.Enemy.Bottom;
            scene.Enemy.SetDefaults(NPCID.BlueSlime);
            scene.Enemy.active = true;
            scene.Enemy.knockBackResist = .5f;
            scene.Enemy.Bottom = bottom;
            if (lifeLeft > 0) scene.Enemy.life = lifeLeft;
            Combat combat = scene.Companion.Combat;
            // The strike is the game's own hit modifiers and NPC.StrikeNPC under the two headless allowances
            // VerifyCompanionExperience's strike uses, neither of which touches the life taken: the game skips a killing hit
            // effect's gore while paused, and NPCLoot, which reads the bestiary, the drop database and the achievements nothing
            // headless has, returns on its first line for a network client. The player's bookkeeping around StrikeNPC is what is
            // left out, because as a client it sends the strike over a network nothing headless has; the swing's own reading of
            // what the strike took is the mod's code running unchanged.
            var deliver = ItemWeapon.DeliverStrike;
            ItemWeapon.DeliverStrike = (player, npc, damage, knockback, direction) =>
            {
                NPC.HitInfo hit = npc.GetIncomingStrikeModifiers(Terraria.ModLoader.DamageClass.Melee, direction)
                    .ToHitInfo(damage, false, knockback, false, player.luck);
                bool paused = Main.gamePaused;
                int netMode = Main.netMode;
                Main.gamePaused = true;
                Main.netMode = 1;
                try { npc.StrikeNPC(hit); }
                finally { Main.gamePaused = paused; Main.netMode = netMode; }
            };
            bool fired;
            try { fired = CombatFixture.FireOnce(scene.Companion, scene.Ctx).Fired; }
            finally { ItemWeapon.DeliverStrike = deliver; }
            Require(fired, $"premise: the sword swings; outcome={combat.LastFireOutcome}");
            Require(S.LastClosed is { Struck: 1 }, $"premise: the swing's window closed on one strike; closed={S.LastClosed}");
            return (S.LastClosed!.Value.Dealt, S.LastClosed!.Value.Ratio, !scene.Enemy.active || scene.Enemy.life <= 0);
        }

        var wound = Swing(0);
        var kill = Swing(2);
        EmitLedgerRows.Detail(FormattableString.Invariant($"sword: wounding swing recorded {wound.Dealt:0} (ratio {wound.Ratio:0.000}); killing swing on two life recorded {kill.Dealt:0} (ratio {kill.Ratio:0.000})"));
        Require(!wound.Died && kill.Died, $"premise: the first swing wounds and the second kills; wound died={wound.Died} kill died={kill.Died}");
        Require(MathF.Abs(wound.Ratio - 1f) < 1e-3f, $"a wounding swing records exactly its forecast; ratio={wound.Ratio}");
        Require(kill.Dealt == wound.Dealt && MathF.Abs(kill.Ratio - wound.Ratio) < 1e-4f,
            $"a killing swing records the strike's damage like a wounding one; kill dealt={kill.Dealt} ratio={kill.Ratio}, wound dealt={wound.Dealt} ratio={wound.Ratio}");
        return 0;
    }

    /// <summary>
    /// The push charge on a hit is weighted by the learned hit rate its damage is. The orb stands on the zombie's far side, so
    /// the bow's push carries the zombie toward the player; the zombie has life enough that nothing is killed, and the threat's
    /// urgency is held above the exploration ceiling so every forecast reads the posterior mean. Four values of the stand: the
    /// untrained bow and the bow taught to land half its forecast, each against a zombie that ignores pushes and one that takes
    /// them in full. Without a push nothing is charged, so the trained value over the untrained is the learned factor on the
    /// damage, and the difference a push makes is the charge. Declared before the run: every value is positive, so no follow-up
    /// was dropped for going negative, which would make the arithmetic nonlinear; the untrained charge is positive; and the
    /// trained charge equals the untrained charge times the learned factor to within five percent.
    /// </summary>
    public static int APushIsChargedAtTheLearnedHitRate()
    {
        float Value(bool trained, float resist)
        {
            var scene = Scene(resist, new Vector2(96f, -8f), floating: false, (GearSlot.FirstWeapon, ItemID.WoodenBow));
            scene.Enemy.lifeMax = scene.Enemy.life = 5000;
            scene.Threats[0].Urgency = .9f;
            Restate(scene.Companion, scene.Ctx.Player, scene.Threats);
            L.Reset();
            Combat combat = scene.Companion.Combat;
            Vector2 stand = scene.Companion.NPC.Center;
            if (trained)
            {
                float[] x = L.Context(Vector2.Distance(stand, scene.Enemy.Center), combat.Weapons[0].Reach, 0f, AssumedCone, 0f, 0, 0f, OrbPace.MaxSpeed, debuffedByOther: false);
                for (int i = 0; i < 40; i++) L.Observe(ItemID.WoodenBow, NPCID.Zombie, x, .5f);
            }
            Require(!Forecasts.Explore(scene.Ctx), "premise: the danger gate holds the forecast to the posterior mean");
            return combat.BestShotValueFrom(scene.Ctx, stand, scene.Enemy);
        }

        float untrainedStill = Value(false, 0f), untrainedPushed = Value(false, 1f);
        float trainedStill = Value(true, 0f), trainedPushed = Value(true, 1f);
        float factor = trainedStill / untrainedStill;
        float untrainedCharge = untrainedStill - untrainedPushed, trainedCharge = trainedStill - trainedPushed;
        EmitLedgerRows.Detail(FormattableString.Invariant($"push charge: untrained value {untrainedStill:0.000} still, {untrainedPushed:0.000} pushed (charge {untrainedCharge:0.000}); trained value {trainedStill:0.000} still, {trainedPushed:0.000} pushed (charge {trainedCharge:0.000}); learned factor {factor:0.000}, charge ratio {trainedCharge / untrainedCharge:0.000}"));
        Require(untrainedStill > 0f && untrainedPushed > 0f && trainedStill > 0f && trainedPushed > 0f,
            $"premise: every value is positive; {untrainedStill} {untrainedPushed} {trainedStill} {trainedPushed}");
        Require(untrainedCharge > 0f, $"premise: a push toward the player is charged; charge={untrainedCharge}");
        Require(factor < .9f, $"premise: the bow learned it lands less than its forecast; factor={factor}");
        Require(MathF.Abs(trainedCharge - factor * untrainedCharge) <= .05f * factor * untrainedCharge,
            $"the push charge is weighted by the same learned factor as the damage; trained charge={trainedCharge}, untrained charge times factor={factor * untrainedCharge}");
        return 0;
    }

    /// <summary>
    /// A shot whose target died to someone else before the shot could land teaches nothing about the weapon. Tested on the
    /// outcome windows directly, because the projectile hooks do not run headless: a window is opened against a zombie with a
    /// forecast landing twenty ticks later, and the zombie is taken out of the world in one of five ways. Declared before the
    /// run: killed by someone else before the landing tick and never struck by the shot, nothing is taught; its slot reused by
    /// a new enemy before the landing, nothing is taught; left alive and missed, the miss is taught as a ratio of zero; killed
    /// by someone else after the landing tick, the miss is still taught; and struck by the shot, which then kills it, the hit
    /// is taught. The death seen only when the window closes counts at the close, so a window closed before the landing tick
    /// on a body already gone teaches nothing either.
    /// </summary>
    public static int AShotWhoseTargetDiedToSomeoneElseTeachesNothing()
    {
        const ulong Opened = 1000;
        const int Impact = 20;
        (int Evidence, float Ratio) Arm(Action<NPC, int> during, ulong closeAt)
        {
            L.Reset();
            S.Clear();
            var zombie = Zombie(25, new Vector2(36 * 16f, FloorY * 16f), .5f);
            float[] x = L.Context(300f, 800f, 0f, AssumedCone, 0f, 0, 0f, 6f, debuffedByOther: false);
            int window = S.Open(ItemID.WoodenBow, zombie, x, predictedDamage: 10f, predictedStruck: 1, predictedCharge: 0f, useTicks: 60, impactTicks: Impact, now: Opened);
            S.AddSlot(window, 5);
            for (ulong tick = Opened; tick < closeAt; tick++)
            {
                during(zombie, (int)(tick - Opened));
                S.Tick(tick);
            }
            S.Close(window, closeAt, bounded: false);
            return (L.Evidence(ItemID.WoodenBow), S.LastClosed?.Ratio ?? float.NaN);
        }
        void Die(NPC npc) { npc.life = 0; npc.active = false; }

        var killedEarly = Arm((npc, age) => { if (age == 8) Die(npc); }, Opened + 60);
        var reused = Arm((npc, age) => { if (age == 8) Generations.Spawn(npc); }, Opened + 60);
        var missed = Arm((_, _) => { }, Opened + 60);
        var killedLate = Arm((npc, age) => { if (age == Impact + 10) Die(npc); }, Opened + 60);
        var goneAtClose = Arm((npc, age) => { if (age == 4) Die(npc); }, Opened + 5);
        var struckAndKilled = Arm((npc, age) =>
        {
            if (age != 18) return;
            S.BeforeStrike(npc, 5);
            S.Landed(npc, 5, 10);
            Die(npc);
        }, Opened + 60);
        EmitLedgerRows.Detail(FormattableString.Invariant($"evidence taught: killed by another before landing {killedEarly.Evidence}, slot reused {reused.Evidence}, gone at an early close {goneAtClose.Evidence}; missed {missed.Evidence} (ratio {missed.Ratio:0.000}), killed by another after landing {killedLate.Evidence} (ratio {killedLate.Ratio:0.000}), struck and killed {struckAndKilled.Evidence} (ratio {struckAndKilled.Ratio:0.000})"));
        Require(missed.Evidence == 1 && missed.Ratio == 0f, $"premise: a miss at a living target is taught as zero; evidence={missed.Evidence} ratio={missed.Ratio}");
        Require(killedEarly.Evidence == 0, $"a target killed by someone else before the shot could land teaches nothing; evidence={killedEarly.Evidence}");
        Require(reused.Evidence == 0, $"a target whose slot a new enemy took before the landing teaches nothing; evidence={reused.Evidence}");
        Require(goneAtClose.Evidence == 0, $"a target found gone when the window closes before the landing teaches nothing; evidence={goneAtClose.Evidence}");
        Require(killedLate.Evidence == 1 && killedLate.Ratio == 0f, $"a target killed by someone else after the shot should have landed is still a miss; evidence={killedLate.Evidence} ratio={killedLate.Ratio}");
        Require(struckAndKilled.Evidence == 1 && MathF.Abs(struckAndKilled.Ratio - 1f) < 1e-4f, $"a shot that struck its target and killed it teaches its hit; evidence={struckAndKilled.Evidence} ratio={struckAndKilled.Ratio}");
        return 0;
    }

    /// <summary>
    /// The committed plan survives ordinary motion and re-searches on a change that should change the choice, driven
    /// through the real activity with combat selected so preparations commit like a running fight. A held plan is visible
    /// as the committed plan id staying where the search stamped it. Declared before the run: the zombie drifting two pixels
    /// a tick with a velocity of its own, and the orb drifting a pixel a tick, keep the plan for three ticks; a second
    /// hostile appearing with urgency above what the plan admitted, the plan's primary leaving, the learner revising, and
    /// the zombie displaced a hundred pixels sideways each re-search on the tick they happen — the displacement through
    /// the re-evaluation, whose re-flown uses no longer solve, rather than through validity, which ordinary motion also
    /// survives. Sideways, because the plan aims at the falling zombie's landing spot: a hundred pixels up falls back
    /// into the same intercept, which still solves and rightly holds. Before this row the stamp hashed every hostile's
    /// centre and velocity, so the hold was renewed never.
    /// </summary>
    public static int TheTargetHoldSurvivesOrdinaryMotion()
    {
        var scene = Scene(0f, new Vector2(-300f, 0f), floating: true, (GearSlot.FirstWeapon, ItemID.WoodenBow));
        L.Reset();
        Combat combat = scene.Companion.Combat;
        Player player = scene.Ctx.Player;
        var fight = scene.Companion.Brain.Chooser.Actions.OfType<Fight>().Single();
        scene.Companion.Brain.Chooser.Activity.Select(fight, scene.Ctx);
        T zombieThreat = scene.Threats[0];

        int Establish()
        {
            for (int i = 0; i < 16; i++) Restate(scene.Companion, player, scene.Threats);
            using var decision = CombatFixture.BeginDecision();
            fight.Prepare(scene.Ctx);
            int id = combat.Planner.Committed?.Id ?? -1;
            Require(id >= 0, "premise: a plan is committed");
            return id;
        }
        bool Reranked(Action change)
        {
            int before = combat.Planner.Committed?.Id ?? -1;
            change();
            Restate(scene.Companion, player, scene.Threats);
            using var decision = CombatFixture.BeginDecision();
            fight.Prepare(scene.Ctx);
            return (combat.Planner.Committed?.Id ?? -2) != before;
        }

        // Walking before the plan is searched, not starting to walk on the first motion tick: a
        // standstill-to-walk flip moves the intercept sixty pixels and the re-flown aims rightly miss
        // it, which is a behaviour change, not ordinary drift. Ordinary drift is the forecast coming
        // true, so the velocity it comes true at has to be the one the plan was searched against.
        scene.Enemy.velocity = new Vector2(2f, .3f);
        int held = Establish();
        int keptTicks = 0;
        for (int i = 0; i < 3; i++)
            if (!Reranked(() =>
                {
                    scene.Enemy.position.X += 2f;
                    scene.Enemy.velocity = new Vector2(2f, .3f);
                    scene.Companion.NPC.position.X += 1f;
                    scene.Companion.NPC.velocity = new Vector2(1f, 0f);
                }))
                keptTicks++;
        bool motionHeld = keptTicks == 3 && combat.Planner.Committed?.Id == held;

        Establish();
        var second = Zombie(27, new Vector2(40 * 16f, AirRow * 16f), 0f);
        var secondThreat = Threat(second, scene.Companion, player);
        secondThreat.Urgency = .5f;
        bool appearing = Reranked(() => scene.Threats.Add(secondThreat));
        Establish();
        int primary = combat.Planner.Committed!.PrimaryTarget;
        T victim = scene.Threats.First(t => t.Npc.whoAmI == primary);
        bool leaving = Reranked(() => scene.Threats.Remove(victim));
        scene.Threats.Clear();
        scene.Threats.Add(zombieThreat);
        Establish();
        bool revising = Reranked(() => L.ObserveDebuff(ItemID.WoodenBow, NPCID.Zombie, applied: false, 0));
        Establish();
        bool jumping = Reranked(() => scene.Enemy.position.X += 100f);
        // The reason the commitment ended, not only that it did: a row that says "kept 0 of 3" and
        // stops names a symptom, and the reader then has to rebuild the scene to find out which of
        // seven checks released it.
        // Two reasons, not one: the planner says the commitment ended, and the re-pricing says which
        // of its eight checks emptied the attack list. "uses-stopped-solving" alone sent a reader
        // back to rebuild the scene to find out which.
        string motionReason = combat.Planner.LastInvalidation + " / " + Reprice.LastRefusal;
        EmitLedgerRows.Detail($"target hold: ordinary motion kept it {keptTicks} of 3 ticks (last release: {motionReason}); re-ranked on a hostile appearing {appearing}, leaving {leaving}, the learner revising {revising}, a hundred-pixel jump {jumping}");
        Require(motionHeld, $"ordinary motion of the target and the orb keeps the hold; kept {keptTicks} of 3 ticks, last release {motionReason}");
        Require(appearing, "a hostile appearing re-ranks at once");
        Require(leaving, "a hostile leaving re-ranks at once");
        Require(revising, "the learner revising re-ranks at once");
        Require(jumping, "a hundred-pixel jump re-ranks at once");
        return 0;
    }

    /// <summary>
    /// Every projectile the companion spawns is the companion's, whether or not a forecast opened an outcome window for it,
    /// and so is every projectile descended from one, for as long as it lives. The spawn hook is the real global projectile
    /// class, called directly because the loader does not run it headless, and the reader is the experience system's own
    /// striker test. Declared before the run: a shot registered with no window reads as the companion's; its child and
    /// grandchild read as the companion's and the experience system names the companion as their striker; a child spawned
    /// after the parent's window closed on its bound still reads as the companion's; a projectile an NPC spawned, one spawned
    /// by a projectile nobody registered, and a slot reused by an unrelated spawn do not.
    /// </summary>
    public static int EveryProjectileTheCompanionSpawnsIsTheCompanions()
    {
        L.Reset();
        S.Clear();
        Landed.Clear();
        var hook = new SpawnHook();
        var zombie = Zombie(25, new Vector2(36 * 16f, FloorY * 16f), .5f);
        Projectile Spawn(int index, Entity? parent)
        {
            var projectile = new Projectile { whoAmI = index, active = true, friendly = true, owner = Main.myPlayer };
            Main.projectile[index] = projectile;
            hook.OnSpawn(projectile, parent == null ? null! : new EntitySource_Parent(parent));
            return projectile;
        }

        var companionBody = VerifyCompanionLifecycle.Create().NPC;
        Projectile root = Spawn(5, companionBody);
        Landed.Register(root.whoAmI, zombie, ItemID.WoodenBow);
        Require(S.WindowOf(root.whoAmI) == null, "premise: the shot has no outcome window, as a shot fired without a forecast has none");
        Require(Landed.IsCompanionShot(root.whoAmI), "a shot registered without a window is the companion's");
        Projectile child = Spawn(9, root), grandchild = Spawn(11, child);
        bool childOwned = Landed.IsCompanionShot(child.whoAmI), grandchildOwned = Landed.IsCompanionShot(grandchild.whoAmI);
        Striker childStriker = Credit.StrikerOf(child), grandchildStriker = Credit.StrikerOf(grandchild);
        Projectile fromNpc = Spawn(12, zombie);
        Projectile stranger = Spawn(20, null);
        stranger.friendly = true;
        Projectile fromStranger = Spawn(13, stranger);

        float[] x = L.Context(300f, 800f, 0f, AssumedCone, 0f, 0, 0f, 6f, debuffedByOther: false);
        Projectile windowed = Spawn(30, companionBody);
        Landed.Register(windowed.whoAmI, zombie, ItemID.WoodenBow);
        int window = S.Open(ItemID.WoodenBow, zombie, x, predictedDamage: 10f, predictedStruck: 1, predictedCharge: 0f, useTicks: 60, impactTicks: 20, now: 0);
        S.AddSlot(window, windowed.whoAmI);
        S.Tick((ulong)Weights.ShotOutcomeWindowTicks + 1);
        Require(S.OpenCount == 0, "premise: the window closed on its bound");
        Projectile late = Spawn(31, windowed);
        bool lateOwned = Landed.IsCompanionShot(late.whoAmI);
        Spawn(9, null);
        bool reusedOwned = Landed.IsCompanionShot(9);

        EmitLedgerRows.Detail($"attribution: child {childOwned} ({childStriker}), grandchild {grandchildOwned} ({grandchildStriker}), after the window's bound {lateOwned}; from an NPC {Landed.IsCompanionShot(fromNpc.whoAmI)}, from an unregistered projectile {Landed.IsCompanionShot(fromStranger.whoAmI)}, reused slot {reusedOwned}");
        Require(childOwned && grandchildOwned, $"a windowless shot's child and grandchild are the companion's; child={childOwned} grandchild={grandchildOwned}");
        Require(childStriker == Striker.Companion && grandchildStriker == Striker.Companion, $"the experience system credits them to the companion; child={childStriker} grandchild={grandchildStriker}");
        Require(lateOwned, "a child spawned after its parent's window closed on its bound is the companion's");
        Require(!Landed.IsCompanionShot(fromNpc.whoAmI), "a projectile an NPC spawned is not the companion's");
        Require(!Landed.IsCompanionShot(fromStranger.whoAmI), "a projectile spawned by an unregistered projectile is not the companion's");
        Require(!reusedOwned, "a slot reused by an unrelated spawn is not the companion's");
        return 0;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
