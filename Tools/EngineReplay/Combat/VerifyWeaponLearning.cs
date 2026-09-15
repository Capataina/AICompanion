extern alias live;

using System.Diagnostics;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using AICompanion.Tools.Ledger;
using C = live::AICompanion.Companion.Brain.Activities.ActionContext;
using T = live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatRecord;
using W = live::AICompanion.Companion.Weapons.WeaponEffects;
using L = live::AICompanion.Companion.Weapons.AttackLearning;
using S = live::AICompanion.Companion.Weapons.ShotOutcomes;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;
using CompanionGear = live::AICompanion.Companion.Inventory.CompanionGear;
using GearSlot = live::AICompanion.Companion.Inventory.GearSlot;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;
using Arsenal = live::AICompanion.Companion.Weapons.Arsenal;
using Positioner = live::AICompanion.Companion.Brain.Infrastructure.Position.Positioner;
using PositionRequest = live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;
using Landed = live::AICompanion.Companion.Weapons.TrackLandedHits;
using SpawnHook = live::AICompanion.Companion.Weapons.ForgetReusedShotSlots;
using Credit = live::AICompanion.Companion.Progression.CreditKillsAndFights;
using Striker = live::AICompanion.Companion.Progression.Striker;
using Generations = live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources;
using OrbPace = live::AICompanion.Companion.Brain.Infrastructure.Movement.OrbPace;
using ItemWeapon = live::AICompanion.Companion.Weapons.ItemWeapon;

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

    public static int Run()
    {
        AnAimCoefficientIsLearnedFromWhetherAimMattered();
        TheHandsAimWhereTheLearnerSaysAimPays();
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
        float widest = Weights.WeaponAimOffsetRadians * Weights.WeaponAimOffsetSteps;
        for (int i = 0; i < Shots; i++)
        {
            float share = (i % 3) / 2f;
            float[] x = L.Context(100f + 500f * (float)random.NextDouble(), 800f, share * widest, 4f * (float)random.NextDouble(), 0,
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
        float[] probe = L.Context(300f, 800f, 0f, 0f, 0, 0f, 6f, debuffedByOther: false);
        float firstAsk = L.Factor(ItemID.WoodenBow, NPCID.BlueSlime, probe, explore: true, tick: 5);
        float secondAsk = L.Factor(ItemID.WoodenBow, NPCID.BlueSlime, probe, explore: true, tick: 5);
        Require(firstAsk == secondAsk, $"two asks in one tick about an enemy type never struck read one draw; first={firstAsk} second={secondAsk}");
    }

    /// <summary>
    /// The hands fire where the learner says the aim pays. A posterior planted with a positive aim coefficient makes the widest
    /// candidate the best, so the shot leaves exactly the widest offset off the intercept; one with a negative coefficient
    /// keeps the intercept, so it leaves exactly on it. The bow's own aim noise is set to nothing for the row, because the
    /// noise is wider than the gap between the two answers and the first version of this row passed both arms at the same
    /// noisy angle. The zombie floats five hundred pixels away in open air, where the widest offset misses its box by more
    /// than the box is tall, so the offset shot can only be fired through the terrain-only clearance check, which is what the
    /// row therefore also proves. Either shot opens one outcome window.
    /// </summary>
    private static void TheHandsAimWhereTheLearnerSaysAimPays()
    {
        float widest = Weights.WeaponAimOffsetRadians * Weights.WeaponAimOffsetSteps;
        foreach (float coefficient in new[] { .8f, -.8f })
        {
            // The orb stands to the zombie's right: five hundred pixels to its left is inside the five-tile margin the trace
            // refuses at the world's edge, which read as a bow with no arc rather than as a scene built too close to it.
            var scene = Scene(0f, new Vector2(500f, 0f), floating: true, (GearSlot.FirstWeapon, ItemID.WoodenBow));
            L.Reset();
            var mean = new float[L.FeatureCount];
            mean[L.AimOffset] = coefficient;
            L.Assume(ItemID.WoodenBow, mean, 1e-6f);
            Arsenal arsenal = scene.Companion.Arsenal;
            Require(arsenal.Weapons.Count == 1, "premise: the bow is the one weapon in hand");
            arsenal.Weapons[0].AimNoise = 0f;
            float missBy = 500f * MathF.Tan(widest);
            Require(missBy > scene.Enemy.height, $"premise: the widest offset misses the zombie's box at this range; miss={missBy} height={scene.Enemy.height}");
            Require(arsenal.TryFire(scene.Ctx, scene.Enemy), $"premise: the bow fires; outcome={arsenal.LastFireOutcome}");
            float offset = MathF.Abs(arsenal.LastAimOffset);
            EmitLedgerRows.Detail(FormattableString.Invariant($"aim coefficient {coefficient:0.0}: left {MathHelper.ToDegrees(offset):0.000} deg off the intercept, widest candidate {MathHelper.ToDegrees(widest):0.000} deg"));
            if (coefficient > 0f)
                Require(MathF.Abs(offset - widest) < 1e-3f, $"a learner that says aiming off pays fires at the widest offset; offset={offset} widest={widest}");
            else
                Require(offset < 1e-3f, $"a learner that says aiming off costs fires on the intercept; offset={offset}");
            Require(S.OpenCount == 1, $"the shot opened one outcome window; open={S.OpenCount}");
        }
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
        float[] plain = L.Context(300f, 800f, 0f, 0f, 0, 0f, 6f, debuffedByOther: false);
        float[] debuffed = L.Context(300f, 800f, 0f, 0f, 0, 0f, 6f, debuffedByOther: true);
        float factorPlain = L.Factor(ItemID.PlatinumBow, NPCID.Zombie, plain, explore: false, 0);
        float factorDebuffed = L.Factor(ItemID.PlatinumBow, NPCID.Zombie, debuffed, explore: false, 0);
        EmitLedgerRows.Detail(FormattableString.Invariant($"burst factor: plain {factorPlain:0.000}, against the other weapon's debuff {factorDebuffed:0.000}"));
        Require(factorDebuffed > factorPlain + .5f, $"the burst weapon's value against a debuffed target exceeds its value against the same target undebuffed; plain={factorPlain} debuffed={factorDebuffed}");

        int Opening(bool debuffLearned)
        {
            var scene = Scene(0f, new Vector2(-300f, 0f), floating: true, (GearSlot.FirstWeapon, ItemID.PlatinumBow), (GearSlot.SecondWeapon, ItemID.WoodenBow));
            scene.Enemy.lifeMax = scene.Enemy.life = 500;
            L.Reset();
            TeachBurst(ItemID.PlatinumBow);
            if (debuffLearned)
                for (int i = 0; i < 8; i++) L.ObserveDebuff(ItemID.WoodenBow, NPCID.Zombie, applied: true, 600);
            Arsenal arsenal = scene.Companion.Arsenal;
            var weapon = arsenal.Choose(scene.Ctx, scene.Enemy);
            EmitLedgerRows.Detail(FormattableString.Invariant($"debuff {(debuffLearned ? "learned" : "unlearned")}: opens with {weapon?.Name}; expected damage first slot {arsenal.LastPrimaryExpected:0.0}, second slot {arsenal.LastSecondaryExpected:0.0}"));
            Require(weapon != null, "premise: a weapon is chosen");
            return weapon!.ItemType;
        }
        Require(Opening(debuffLearned: false) == ItemID.PlatinumBow, "premise: with no debuff learned the burst bow opens");
        Require(Opening(debuffLearned: true) == ItemID.WoodenBow, "with the weaker bow's debuff learned, the arsenal opens with the debuff");
    }

    /// <summary>Forty outcomes for the burst weapon, debuffed and plain in turn, from a seeded generator.</summary>
    private static void TeachBurst(int item)
    {
        var random = new Random(11);
        for (int i = 0; i < 40; i++)
        {
            bool debuffed = i % 2 == 0;
            float[] x = L.Context(150f + 400f * (float)random.NextDouble(), 800f, 0f, (float)random.NextDouble(), 0,
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
        float[] x = L.Context(300f, 800f, 0f, 0f, 0, 0f, 6f, debuffedByOther: false);
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
        Arsenal arsenal = scene.Companion.Arsenal;
        Require(arsenal.TryFire(scene.Ctx, scene.Enemy), $"premise: the sword swings; outcome={arsenal.LastFireOutcome}");
        Require(S.OpenCount == 0 && S.LastClosed is { Struck: 1 } outcome && outcome.Dealt > 0f,
            $"the swing's window closed on its strike; open={S.OpenCount} closed={S.LastClosed}");
        Require(L.Evidence(ItemID.CopperBroadsword) == 1, $"the swing taught the learner once; evidence={L.Evidence(ItemID.CopperBroadsword)}");
    }

    /// <summary>
    /// With something dangerous on a body the choice is the posterior mean's. The platinum bow's posterior is planted with a
    /// mean that ranks it below the wooden bow and a wide variance, the wooden bow's with a mean of the prior and almost no
    /// variance. At no danger, some seed of the sampler draws the platinum bow above the wooden one — the premise that
    /// exploration is live in this scene — and that seed repeats its choice. At the same seed with the zombie's urgency above
    /// the declared ceiling the choice is the wooden bow, which is the mean's, and the arsenal says it did not explore.
    /// </summary>
    private static void UnderRealDangerTheChoiceIsThePosteriorMean()
    {
        var scene = Scene(0f, new Vector2(-300f, 0f), floating: true, (GearSlot.FirstWeapon, ItemID.WoodenBow), (GearSlot.SecondWeapon, ItemID.PlatinumBow));
        L.Reset();
        var low = new float[L.FeatureCount];
        low[L.Bias] = -.7f;
        L.Assume(ItemID.PlatinumBow, low, 1f);
        L.Assume(ItemID.WoodenBow, new float[L.FeatureCount], 1e-6f);
        Arsenal arsenal = scene.Companion.Arsenal;
        Player player = scene.Ctx.Player;

        int ChoiceAt(float urgency, int seed)
        {
            scene.Threats[0].Urgency = urgency;
            for (int i = 0; i < 13; i++) Restate(scene.Companion, player, scene.Threats);
            L.Seed(seed);
            return arsenal.Choose(scene.Ctx, scene.Enemy)!.ItemType;
        }

        Require(Weights.WeaponExploreDangerCeiling < .9f, "premise: the danger this row uses is above the ceiling");
        int found = -1;
        for (int seed = 0; seed < 200 && found < 0; seed++)
            if (ChoiceAt(0f, seed) == ItemID.PlatinumBow) found = seed;
        Require(found >= 0, "premise: at no danger some draw explores to the weapon the mean ranks lower");
        Require(ChoiceAt(0f, found) == ItemID.PlatinumBow && arsenal.LastForecastExplored, "premise: that draw repeats and the arsenal explored");
        int underDanger = ChoiceAt(.9f, found);
        EmitLedgerRows.Detail(FormattableString.Invariant($"danger gate: seed {found} explores to the platinum bow at no danger; under danger it chooses item {underDanger}"));
        Require(underDanger == ItemID.WoodenBow && !arsenal.LastForecastExplored,
            $"under danger above the ceiling the choice is the posterior mean's and nothing is explored; chose={underDanger} explored={arsenal.LastForecastExplored}");
    }

    /// <summary>
    /// AIC-395. A firing stand is priced by the best attack any weapon in hand could make from it, not by the weapon chosen
    /// last. The player stands left of a zombie; the strong bow pushes along its flight and the weak bow has learned it pushes
    /// nothing, so from the stand on the player's side the strong bow's shot is safe, and from the far stand only the weak
    /// bow's is. On paper: the player's side is worth the strong shot, the far side the larger of the weak shot and the strong
    /// shot less its push charge, so the player's side wins whenever the charge is positive and the strong shot outvalues the
    /// weak. The arsenal's last choice is the weak bow, made for a slime the strong bow barely hurts, which is the premise that
    /// made this red on the proxy: the side share read that weak bow's push of nothing and scored both stands identically.
    /// </summary>
    private static void AStandIsPricedByEveryHandedWeapon()
    {
        var scene = Scene(1f, new Vector2(96f, -8f), floating: false, (GearSlot.FirstWeapon, ItemID.WoodenBow), (GearSlot.SecondWeapon, ItemID.PlatinumBow));
        L.Reset();
        W.AssumePush(ItemID.WoodenBow, scene.Enemy.type, 0f);
        var senses = scene.Companion.Brain.Senses;
        Arsenal arsenal = scene.Companion.Arsenal;
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
        arsenal.Choose(scene.Ctx, slime);
        Require(arsenal.LastChosen?.ItemType == ItemID.WoodenBow, $"premise: the arsenal's last choice is the weak bow; chosen={arsenal.LastChosen?.Name}");

        float valuePlayerSide = arsenal.BestShotValueFrom(scene.Ctx, playerSide, enemy);
        float valueFarSide = arsenal.BestShotValueFrom(scene.Ctx, farSide, enemy);
        float sharePlayerSide = Positioner.FiringStandShare(playerSide, enemy, senses, 0);
        float shareFarSide = Positioner.FiringStandShare(farSide, enemy, senses, 0);
        float Score(Vector2 spot, float share)
        {
            MethodInfo score = typeof(Positioner).GetMethod("ScoreSpot", BindingFlags.NonPublic | BindingFlags.Static)!;
            var request = new PositionRequest(RequestKind.LineOfFire, enemy.Center, enemy);
            return (float)score.Invoke(null, new object[] { request, spot, scene.Ctx.Player.Bottom, senses,
                Weights.ThreatBandNear, Weights.ThreatBandFar, share, arsenal.MaxReach })!;
        }
        float scorePlayerSide = Score(playerSide, sharePlayerSide), scoreFarSide = Score(farSide, shareFarSide);
        EmitLedgerRows.Detail(FormattableString.Invariant($"stand pricing: value player side {valuePlayerSide:0.000} far {valueFarSide:0.000}; share {sharePlayerSide:0.0000} / {shareFarSide:0.0000}; score {scorePlayerSide:0.0000} / {scoreFarSide:0.0000}"));
        // The ranking is asserted first because it is the row's claim; the value comparison beneath it is the mechanism that
        // produces the ranking, not a premise about the scene, and a planted proxy changes both at once.
        Require(scorePlayerSide > scoreFarSide,
            $"the stand where the strong bow's push goes away from the player outranks the stand where only the weak bow is safe; player side={scorePlayerSide} far={scoreFarSide}");
        Require(valuePlayerSide > valueFarSide, $"the player side's stand is worth the strong bow's safe shot, more than the far side's best; player side={valuePlayerSide} far={valueFarSide}");
    }

    /// <summary>
    /// The learner's cost per decision, where a decision is one full target ranking: eight zombies across the floor, both
    /// bows, the shortlist the arsenal bounds itself to. Three arms back to back in one process — untrained, trained on forty
    /// outcomes per weapon with a debuff rate, untrained again — so neither JIT nor the machine's load is credited to the
    /// learner. The senses are advanced between decisions outside the stopwatch so every ranking runs rather than returning a
    /// held target. Declared before the run: the trained arm's mean is within a millisecond of the slower untrained arm's.
    /// </summary>
    private static void TheLearnersCostPerDecisionIsMeasured()
    {
        var scene = Scene(0f, new Vector2(0f, -160f), floating: false, (GearSlot.FirstWeapon, ItemID.WoodenBow), (GearSlot.SecondWeapon, ItemID.PlatinumBow));
        Player player = scene.Ctx.Player;
        int[] columns = { 20, 26, 31, 41, 46, 52, 58 };
        for (int i = 0; i < columns.Length; i++)
            scene.Threats.Add(Threat(Zombie(40 + i, new Vector2(columns[i] * 16f, FloorY * 16f), 0f), scene.Companion, player));
        for (int i = 0; i < scene.Threats.Count; i++) scene.Threats[i].Urgency = .05f * i;
        Arsenal arsenal = scene.Companion.Arsenal;

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
                target = arsenal.BestTarget(scene.Ctx);
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
        float[] x = L.Context(300f, 800f, 0f, 1f, 1, 1f, 6f, debuffedByOther: false);
        TeachBurst(ItemID.PlatinumBow);
        for (int i = 0; i < 10000; i++) L.Factor(ItemID.PlatinumBow, NPCID.Zombie, x, explore: true, i);
        factorClock.Stop();
        double untrained = Math.Max(coldUntrained.Mean, warmUntrained.Mean);
        EmitLedgerRows.Detail(FormattableString.Invariant($"cost per ranking of 8 hostiles, 2 weapons, ms (lower is better): untrained mean {coldUntrained.Mean:0.000} then {warmUntrained.Mean:0.000} (p95 {warmUntrained.P95:0.000}); trained mean {trainedArm.Mean:0.000} (p95 {trainedArm.P95:0.000}); one sampled factor with a fresh draw {factorClock.Elapsed.TotalMilliseconds * 1000.0 / 10000:0.0} us"));
        Require(trainedArm.Mean - untrained < 1.0, $"the learner adds under a millisecond to a full ranking; trained={trainedArm.Mean} untrained={untrained}");
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
            float[] x = L.Context(300f, 800f, 0f, 0f, 0, 0f, 6f, debuffedByOther: false);
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

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
